using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the slingshot launch formula, and in particular the five failure modes described on
    /// <see cref="SlingshotMath"/>. Each "Regression" test fails if one of them comes back.
    /// </summary>
    public class SlingshotMathTests
    {
        /// <summary>The live tuning, so numbers here match the game's.</summary>
        const float LiveMaxStretch = 10f;
        const float LiveMinStretch = 0.5f;
        const float LiveStiffness = 22f;
        const float LiveElasticity = 1.15f;
        const float LiveCeiling = 320f;

        /// <summary>Live speed per metre of stretch: 22 * 1.15 = 25.3 m/s per metre.</summary>
        const float LiveSpeedPerMetre = LiveStiffness * LiveElasticity;

        static SlingshotParams Live(
            float ceiling = LiveCeiling,
            float minPitch = -90f,
            float maxPitch = 90f,
            float pitchBias = 0f)
        {
            return new SlingshotParams(
                LiveMaxStretch, LiveMinStretch, LiveStiffness, LiveElasticity, ceiling,
                minPitch, maxPitch, pitchBias);
        }

        /// <summary>A stretch vector pulled down and back at 45 degrees, so the launch is up and forward at 45.</summary>
        static Vector3 PullDownBack45(float metres)
        {
            return new Vector3(0f, -Mathf.Sqrt(0.5f), -Mathf.Sqrt(0.5f)) * metres;
        }

        // ---------------------------------------------------------------- speed

        [Test]
        public void LaunchSpeed_IsLinearInStretch()
        {
            SlingshotParams p = Live();

            Assert.That(SlingshotMath.LaunchSpeed(in p, 1f), Is.EqualTo(LiveSpeedPerMetre).Within(1e-3f));
            Assert.That(SlingshotMath.LaunchSpeed(in p, 4f), Is.EqualTo(4f * LiveSpeedPerMetre).Within(1e-3f));
            Assert.That(SlingshotMath.LaunchSpeed(in p, 10f), Is.EqualTo(10f * LiveSpeedPerMetre).Within(1e-3f));
        }

        [Test]
        public void LaunchSpeed_ZeroStretch_IsZero()
        {
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.LaunchSpeed(in p, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void LaunchSpeed_NegativeStretch_IsZero()
        {
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.LaunchSpeed(in p, -5f), Is.EqualTo(0f));
        }

        [Test]
        public void LaunchSpeed_IsClampedByCeiling()
        {
            SlingshotParams p = Live(ceiling: 100f);
            Assert.That(SlingshotMath.LaunchSpeed(in p, 10f), Is.EqualTo(100f).Within(1e-3f));
        }

        [Test]
        public void LaunchSpeed_LiveTuningStaysUnderTheCeiling()
        {
            // With live values the 320 ceiling never binds, so it is a safety net and not a
            // hidden tuning knob. If tuning changes enough to hit it, this test says so.
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.LaunchSpeed(in p, LiveMaxStretch), Is.LessThan(LiveCeiling));
        }

        [Test]
        public void Elasticity_ScalesSpeedLinearly()
        {
            // Elasticity scales speed, not energy. Range therefore scales with its square,
            // since drag-free range goes as v^2 -- so doubling elasticity quadruples distance. Asserting the
            // speed relationship is enough; the range consequence follows from it.
            var soft = new SlingshotParams(LiveMaxStretch, LiveMinStretch, LiveStiffness, 1f, LiveCeiling);
            var stiff = new SlingshotParams(LiveMaxStretch, LiveMinStretch, LiveStiffness, 2f, LiveCeiling);

            float softSpeed = SlingshotMath.LaunchSpeed(in soft, 4f);
            float stiffSpeed = SlingshotMath.LaunchSpeed(in stiff, 4f);

            Assert.That(stiffSpeed, Is.EqualTo(softSpeed * 2f).Within(1e-3f));
        }

        // ---------------------------------------------------------------- stretch

        [Test]
        public void StretchMetres_ClampsToCap()
        {
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.StretchMetres(in p, Vector3.forward * 25f), Is.EqualTo(LiveMaxStretch).Within(1e-4f));
        }

        [Test]
        public void StretchMetres_MeasuresVectorLength()
        {
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.StretchMetres(in p, new Vector3(3f, 4f, 0f)), Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void StretchMetres_NaN_ReadsAsZero()
        {
            // A stretch vector is derived from body positions, and a diverging ragdoll is a real NaN source.
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.StretchMetres(in p, new Vector3(float.NaN, 0f, 0f)), Is.EqualTo(0f));
        }

        [Test]
        public void CanLaunch_BelowMinimumStretch_IsFalse()
        {
            // Regression: a 36 ms tap of the pull key must not launch.
            SlingshotParams p = Live();
            Assert.IsFalse(SlingshotMath.CanLaunch(in p, 0.12f));
            Assert.IsFalse(SlingshotMath.CanLaunch(in p, LiveMinStretch - 0.01f));
        }

        [Test]
        public void CanLaunch_AtOrAboveMinimumStretch_IsTrue()
        {
            SlingshotParams p = Live();
            Assert.IsTrue(SlingshotMath.CanLaunch(in p, LiveMinStretch));
            Assert.IsTrue(SlingshotMath.CanLaunch(in p, LiveMaxStretch));
        }

        // ---------------------------------------------------------------- direction

        [Test]
        public void LaunchDirection_IsOppositeTheStretch_AndUnitLength()
        {
            SlingshotParams p = Live();
            Vector3 stretch = new Vector3(2f, 0f, 0f);
            Vector3 direction = SlingshotMath.LaunchDirection(in p, stretch);

            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Vector3.Dot(direction, stretch.normalized), Is.EqualTo(-1f).Within(1e-4f));
        }

        [Test]
        public void LaunchDirection_IgnoresStretchMagnitude()
        {
            // Direction and power are separate concerns; magnitude only feeds speed.
            SlingshotParams p = Live();
            Vector3 near = SlingshotMath.LaunchDirection(in p, PullDownBack45(0.6f));
            Vector3 far = SlingshotMath.LaunchDirection(in p, PullDownBack45(9f));

            Assert.That(Vector3.Distance(near, far), Is.LessThan(1e-4f));
        }

        [Test]
        public void LaunchDirection_ZeroStretch_IsZero()
        {
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.LaunchDirection(in p, Vector3.zero), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void LaunchDirection_NaNStretch_IsZero()
        {
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.LaunchDirection(in p, new Vector3(0f, float.NaN, 0f)), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Regression_FortyFiveDegreeLobIsReachable()
        {
            // Launch direction must not be raw camera forward: the camera's pitch clamp would cap launch
            // elevation at about +26.7 degrees, putting the maximum-range lob out of reach. Elevation comes
            // from the stretch vector with its own range.
            SlingshotParams p = Live();
            Vector3 direction = SlingshotMath.LaunchDirection(in p, PullDownBack45(5f));

            float elevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            Assert.That(elevation, Is.EqualTo(45f).Within(0.01f));
        }

        [Test]
        public void LaunchDirection_StraightUpIsReachable()
        {
            SlingshotParams p = Live();
            Vector3 direction = SlingshotMath.LaunchDirection(in p, Vector3.down * 3f);

            Assert.That(direction.y, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void LaunchDirection_StraightDownPullIsHandled()
        {
            // Pulling straight up has no azimuth to preserve; the result must still be a unit vector.
            SlingshotParams p = Live();
            Vector3 direction = SlingshotMath.LaunchDirection(in p, Vector3.up * 3f);

            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(direction.y, Is.EqualTo(-1f).Within(1e-3f));
        }

        [Test]
        public void LaunchDirection_IsClampedByThePitchRange()
        {
            SlingshotParams p = Live(minPitch: 0f, maxPitch: 30f);
            Vector3 direction = SlingshotMath.LaunchDirection(in p, PullDownBack45(5f));

            float elevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            Assert.That(elevation, Is.EqualTo(30f).Within(0.01f));
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void LaunchDirection_PitchBiasRaisesElevation_AndKeepsAzimuth()
        {
            SlingshotParams p = Live(pitchBias: 20f);
            Vector3 stretch = new Vector3(0f, 0f, -5f); // pulled straight back, level
            Vector3 direction = SlingshotMath.LaunchDirection(in p, stretch);

            float elevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            Assert.That(elevation, Is.EqualTo(20f).Within(0.01f));

            // Azimuth is untouched: still launching along +z.
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            Assert.That(Vector3.Dot(flat, Vector3.forward), Is.EqualTo(1f).Within(1e-3f));
        }

        // ---------------------------------------------------------------- one flight model

        [Test]
        public void Regression_SpeedNeverFallsAsThePullGrows()
        {
            // Two flight models split at a speed threshold would let a 0.47 m pull go further than a
            // 0.48 m pull. One flight model means speed is
            // monotonic in stretch all the way to the cap -- and at a fixed angle, distance follows speed, so
            // pinning speed pins "a longer pull never travels shorter" without doing ballistics here.
            SlingshotParams p = Live();
            float previousSpeed = -1f;

            for (int step = 0; step <= 200; step++)
            {
                float stretch = LiveMaxStretch * step / 200f;
                float speed = SlingshotMath.LaunchSpeed(in p, stretch);

                Assert.That(speed, Is.GreaterThanOrEqualTo(previousSpeed),
                    $"Speed went down between {stretch:F3} m and the previous step.");
                previousSpeed = speed;
            }
        }

        [Test]
        public void Regression_SpeedIsContinuousAcrossTheOldTwelveMetreFork()
        {
            // The old fork sat at 12 m/s, which is about 0.474 m of stretch at live tuning. Crossing it used
            // to drop the launch from ~11.9 m/s to ~4.7 m/s. There is no discontinuity now.
            SlingshotParams p = Live();
            float forkStretch = 12f / LiveSpeedPerMetre;

            float below = SlingshotMath.LaunchSpeed(in p, forkStretch - 0.001f);
            float above = SlingshotMath.LaunchSpeed(in p, forkStretch + 0.001f);

            Assert.That(above - below, Is.LessThan(0.1f));
            Assert.That(above, Is.GreaterThan(below));
        }

        [Test]
        public void Regression_SpeedPlateausRatherThanFallingAtTheCeiling()
        {
            // The safety ceiling must flatten the curve, not fold it back down.
            SlingshotParams p = Live(ceiling: 60f);
            float previousSpeed = -1f;

            for (int step = 0; step <= 100; step++)
            {
                float stretch = LiveMaxStretch * step / 100f;
                float speed = SlingshotMath.LaunchSpeed(in p, stretch);

                Assert.That(speed, Is.GreaterThanOrEqualTo(previousSpeed));
                previousSpeed = speed;
            }

            Assert.That(previousSpeed, Is.EqualTo(60f).Within(1e-3f), "and it should sit at the ceiling");
        }

        // ---------------------------------------------------------------- composed velocity

        [Test]
        public void ComputeLaunchVelocity_CombinesDirectionAndSpeed()
        {
            SlingshotParams p = Live();
            Vector3 stretch = PullDownBack45(4f);
            Vector3 velocity = SlingshotMath.ComputeLaunchVelocity(in p, stretch);

            Assert.That(velocity.magnitude, Is.EqualTo(4f * LiveSpeedPerMetre).Within(0.01f));
            Assert.That(Vector3.Dot(velocity.normalized, SlingshotMath.LaunchDirection(in p, stretch)),
                Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ComputeLaunchVelocity_UsesTheCappedStretch()
        {
            SlingshotParams p = Live();
            Vector3 velocity = SlingshotMath.ComputeLaunchVelocity(in p, PullDownBack45(40f));

            Assert.That(velocity.magnitude, Is.EqualTo(LiveMaxStretch * LiveSpeedPerMetre).Within(0.01f));
        }

        [Test]
        public void Regression_ComputeLaunchVelocityRefusesASubMinimumPull()
        {
            // The minimum has to be enforced by the formula, not by the caller remembering to ask. A
            // "small but non-zero" velocity is how a two-frame tap would launch.
            SlingshotParams p = Live();
            Vector3 tap = PullDownBack45(0.12f); // well under the 0.5 m minimum

            Assert.IsFalse(SlingshotMath.CanLaunch(in p, 0.12f));
            Assert.That(SlingshotMath.ComputeLaunchVelocity(in p, tap), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Regression_DefaultParamsAreInertRatherThanSurprising()
        {
            // For a struct, `default` and `new SlingshotParams()` bind to the implicit zero-initialising
            // constructor, never to the one whose tail parameters are optional. The same shape produced
            // all-zero RagdollRecoverySettings and ended every flight on its first frame. Here zeroes mean
            // MaxStretch 0, so nothing can launch: the safe direction, pinned so it stays a known state.
            SlingshotParams zeroed = default;

            Assert.That(SlingshotMath.StretchMetres(in zeroed, PullDownBack45(5f)), Is.EqualTo(0f));
            Assert.That(SlingshotMath.ComputeLaunchVelocity(in zeroed, PullDownBack45(5f)), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ComputeLaunchVelocity_ZeroStretch_IsZero()
        {
            SlingshotParams p = Live();
            Assert.That(SlingshotMath.ComputeLaunchVelocity(in p, Vector3.zero), Is.EqualTo(Vector3.zero));
        }
    }
}
