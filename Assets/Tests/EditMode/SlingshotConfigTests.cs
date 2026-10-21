using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the shipped defaults of <see cref="SlingshotConfig"/> and the curve-to-metres mapping.
    /// A fresh asset must behave like the version that was actually playtested, so a
    /// component reset never hands back numbers tuned for a different world.
    /// </summary>
    public class SlingshotConfigTests
    {
        SlingshotConfig config;

        [SetUp]
        public void CreateConfig()
        {
            config = ScriptableObject.CreateInstance<SlingshotConfig>();
        }

        [TearDown]
        public void DestroyConfig()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Defaults_MatchThePlaytestedTuning()
        {
            Assert.That(config.MaxStretch, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(config.PullSpeed, Is.EqualTo(10f / 3f).Within(1e-3f));

            SlingshotParams p = config.ToParams();
            Assert.That(SlingshotMath.LaunchSpeed(in p, 1f), Is.EqualTo(25.3f).Within(0.01f),
                "22 stiffness x 1.15 elasticity = 25.3 m/s per metre of stretch, as the scene ran it.");
        }

        [Test]
        public void Defaults_ReachTheCapInThreeSeconds()
        {
            // The pull rate is now its own field, but it must still describe the three-second pull that was
            // playtested, otherwise the feel changes silently.
            Assert.That(config.SecondsToMaxStretch, Is.EqualTo(3f).Within(0.01f));
        }

        [Test]
        public void StretchAfterHold_GrowsAtThePullRate()
        {
            Assert.That(config.StretchAfterHold(1f), Is.EqualTo(10f / 3f).Within(1e-3f));
            Assert.That(config.StretchAfterHold(1.2f), Is.EqualTo(4f).Within(1e-3f));
        }

        [Test]
        public void StretchAfterHold_ClampsToTheCapAndIgnoresNegativeTime()
        {
            Assert.That(config.StretchAfterHold(30f), Is.EqualTo(config.MaxStretch).Within(1e-4f));
            Assert.That(config.StretchAfterHold(0f), Is.EqualTo(0f));
            Assert.That(config.StretchAfterHold(-1f), Is.EqualTo(0f));
        }

        [Test]
        public void Regression_ThePullRateIsIndependentOfTheCap()
        {
            // A rate derived as maxPull / pullDurationToMax would make the same one-second hold 2.5x
            // stronger at cap 10 than at cap 4. Rate and cap are separate fields, so raising
            // the cap must leave a given hold exactly as strong as it was.
            float atLiveCap = config.StretchAfterHold(1f);

            SetMaxStretch(100f);
            Assert.That(config.StretchAfterHold(1f), Is.EqualTo(atLiveCap).Within(1e-4f));

            SetMaxStretch(4f);
            Assert.That(config.StretchAfterHold(1f), Is.EqualTo(atLiveCap).Within(1e-4f));
        }

        [Test]
        public void Defaults_RequireADeliberatePull()
        {
            // A minimum of half a metre, so a two-frame tap cannot launch.
            Assert.That(config.MinStretchToLaunch, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.IsFalse(config.CanLaunch(0.12f));
            Assert.IsTrue(config.CanLaunch(0.5f));
        }

        [Test]
        public void Defaults_AllowTheFortyFiveDegreeLob()
        {
            SlingshotParams p = config.ToParams();
            Vector3 stretch = new Vector3(0f, -Mathf.Sqrt(0.5f), -Mathf.Sqrt(0.5f)) * 5f;
            Vector3 direction = SlingshotMath.LaunchDirection(in p, stretch);

            float elevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            Assert.That(elevation, Is.EqualTo(45f).Within(0.01f));
        }

        [Test]
        public void EffectiveStretch_IsIdentityByDefault()
        {
            // The curve is off by default, which is exactly what the scene's linear curve did.
            Assert.That(config.EffectiveStretch(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(config.EffectiveStretch(4f), Is.EqualTo(4f).Within(1e-4f));
            Assert.That(config.EffectiveStretch(10f), Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void EffectiveStretch_ClampsToTheCap()
        {
            Assert.That(config.EffectiveStretch(25f), Is.EqualTo(config.MaxStretch).Within(1e-4f));
            Assert.That(config.EffectiveStretch(-5f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Regression_ACurveDrawnAboveTheCapCannotRaiseLaunchPower()
        {
            // Both axes of the curve are metres, so its output is capped exactly like its input. Without
            // that, a curve drawn above the cap pushed launch speed past what maxStretch says is possible
            // and the cap stopped being a cap. Extra power belongs in stiffness.
            //
            // Expecting the warning both keeps the console clean and pins the y-axis validation: a curve
            // whose last key misses the cap has to say so, because that is the mistake that is easy to make.
            LogAssert.Expect(LogType.Warning, new Regex("y axis"));
            EnableStretchCurve(AnimationCurve.Linear(0f, 0f, 10f, 40f));

            Assert.That(config.EffectiveStretch(10f), Is.EqualTo(config.MaxStretch).Within(1e-4f));
            Assert.That(config.ComputeLaunchVelocity(FullPullDownBack45()).magnitude,
                Is.EqualTo(253f).Within(1f), "Still the full-pull speed, not four times it.");
        }

        [Test]
        public void AnEnabledCurveStillShapesStretchBelowTheCap()
        {
            // The clamp must not flatten legitimate shaping: an ease-in curve should make early pull weaker
            // while a full pull still reaches full power. Keys are placed explicitly rather than smoothed,
            // so the shape is exact and the assertions do not depend on tangent maths.
            EnableStretchCurve(new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(5f, 2f),
                new Keyframe(10f, 10f)));

            Assert.That(config.EffectiveStretch(5f), Is.EqualTo(2f).Within(1e-3f),
                "Half a pull should give well under half the stretch on an ease-in band.");
            Assert.That(config.EffectiveStretch(10f), Is.EqualTo(10f).Within(1e-3f),
                "A full pull must still reach full power.");
        }

        /// <summary>
        /// Turns the curve on and installs one. The fields are `[SerializeField] private`, so a test
        /// reaches them the way the Inspector does rather than widening the API.
        /// </summary>
        void EnableStretchCurve(AnimationCurve curve)
        {
            var serialized = new UnityEditor.SerializedObject(config);
            serialized.FindProperty("useStretchCurve").boolValue = true;
            serialized.FindProperty("stretchCurve").animationCurveValue = curve;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Changes the cap the way the Inspector would, to prove the pull rate does not follow it.</summary>
        void SetMaxStretch(float metres)
        {
            var serialized = new UnityEditor.SerializedObject(config);
            serialized.FindProperty("maxStretch").floatValue = metres;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Vector3 FullPullDownBack45()
        {
            return new Vector3(0f, -Mathf.Sqrt(0.5f), -Mathf.Sqrt(0.5f)) * 10f;
        }

        /// <summary>
        /// Drag-free flat distance for a 45-degree launch from the wire's height, so the tuning can be
        /// checked against the design's distance table. Lives here rather than in the production formula:
        /// the game does not need to predict range, only this assertion does.
        /// </summary>
        static float FlatRangeFromTheWire(float speed)
        {
            const float gravity = 80f;
            const float wireHeight = 31.2f;

            float component = speed * Mathf.Sqrt(0.5f); // equal horizontal and vertical at 45 degrees
            float flightTime = (component + Mathf.Sqrt(component * component + 2f * gravity * wireHeight)) / gravity;
            return component * flightTime;
        }

        [Test]
        public void ComputeLaunchVelocity_AgreesWithTheMathDirectly()
        {
            Vector3 stretch = new Vector3(0f, -3f, -4f); // 5 m of stretch
            SlingshotParams p = config.ToParams();

            Vector3 viaConfig = config.ComputeLaunchVelocity(stretch);
            Vector3 viaMath = SlingshotMath.ComputeLaunchVelocity(in p, stretch);

            Assert.That(Vector3.Distance(viaConfig, viaMath), Is.LessThan(1e-3f));
            Assert.That(viaConfig.magnitude, Is.EqualTo(5f * 25.3f).Within(0.01f));
        }

        [Test]
        public void ComputeLaunchVelocity_FullPullIsFastEnoughToCrossTheMap()
        {
            // Sanity check against the design's distance table: a full pull is ~253 m/s, which lobbed at 45 degrees
            // from the wire covers roughly the 1 km blockout. If tuning drifts far from this, the design's
            // numbers stop describing the game.
            Vector3 stretch = new Vector3(0f, -Mathf.Sqrt(0.5f), -Mathf.Sqrt(0.5f)) * config.MaxStretch;
            Vector3 velocity = config.ComputeLaunchVelocity(stretch);

            Assert.That(velocity.magnitude, Is.EqualTo(253f).Within(1f));

            float range = FlatRangeFromTheWire(velocity.magnitude);
            Assert.That(range, Is.GreaterThan(700f));
            Assert.That(range, Is.LessThan(1000f));
        }
    }
}
