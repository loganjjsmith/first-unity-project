using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the smoothing rule, and demonstrates the two concrete ways
    /// <c>Lerp(a, b, k * deltaTime)</c> is wrong. The "Contrast" tests deliberately compute the naive
    /// version so the difference is measurable rather than asserted on faith.
    /// </summary>
    public class DampingTests
    {
        /// <summary>The live perch turn rate, so the numbers here are the real ones.</summary>
        const float PerchTurnRate = 14f;

        /// <summary>Steps a value toward 1 for a whole second and returns where it ended up.</summary>
        static float SmoothForOneSecond(float rate, float deltaTime)
        {
            float value = 0f;
            int steps = Mathf.RoundToInt(1f / deltaTime);
            for (int i = 0; i < steps; i++)
                value = Damping.Toward(value, 1f, rate, deltaTime);

            return value;
        }

        static float SmoothFor(float seconds, float rate, float deltaTime)
        {
            float value = 0f;
            int steps = Mathf.RoundToInt(seconds / deltaTime);
            for (int i = 0; i < steps; i++)
                value = Damping.Toward(value, 1f, rate, deltaTime);

            return value;
        }

        /// <summary>The old, frame-rate-dependent way, for comparison only.</summary>
        static float NaiveLerpFor(float seconds, float rate, float deltaTime)
        {
            float value = 0f;
            int steps = Mathf.RoundToInt(seconds / deltaTime);
            for (int i = 0; i < steps; i++)
                value = Mathf.Lerp(value, 1f, rate * deltaTime);

            return value;
        }

        // ---------------------------------------------------------------- the factor itself

        [Test]
        public void Factor_IsAlwaysBetweenZeroAndOne()
        {
            float[] rates = { 0.1f, 1f, 14f, 100f, 10000f };
            float[] steps = { 1f / 240f, 1f / 60f, 1f / 30f, 0.25f, 2f };

            foreach (float rate in rates)
            foreach (float step in steps)
            {
                float factor = Damping.Factor(rate, step);
                Assert.That(factor, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(1f),
                    $"rate {rate}, dt {step}");
            }
        }

        [Test]
        public void Factor_DegenerateInputsDoNotMove()
        {
            Assert.That(Damping.Factor(0f, 1f / 60f), Is.EqualTo(0f));
            Assert.That(Damping.Factor(-5f, 1f / 60f), Is.EqualTo(0f));
            Assert.That(Damping.Factor(14f, 0f), Is.EqualTo(0f));
            Assert.That(Damping.Factor(14f, -1f), Is.EqualTo(0f));
            Assert.That(Damping.Factor(float.NaN, 1f / 60f), Is.EqualTo(0f));
            Assert.That(Damping.Factor(14f, float.NaN), Is.EqualTo(0f));
        }

        [Test]
        public void Factor_MatchesTheClosedFormErrorDecay()
        {
            // The contract that makes the rate meaningful: after 1/rate seconds, 1/e of the error is left.
            float remaining = 1f - SmoothForOneSecond(1f, 1f / 240f);
            Assert.That(remaining, Is.EqualTo(1f / Mathf.Exp(1f)).Within(1e-3f));
        }

        // ---------------------------------------------------------------- frame-rate independence

        [Test]
        public void Toward_ReachesTheSamePlaceAtAnyFrameRate()
        {
            float at30 = SmoothForOneSecond(5f, 1f / 30f);
            float at60 = SmoothForOneSecond(5f, 1f / 60f);
            float at240 = SmoothForOneSecond(5f, 1f / 240f);

            float expected = 1f - Mathf.Exp(-5f);
            Assert.That(at30, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(at60, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(at240, Is.EqualTo(expected).Within(1e-4f));
        }

        [Test]
        public void Contrast_NaiveLerpTurnsFasterAtLowFrameRates()
        {
            // 0.1 s of perch turning: the correct version lands in the same place either way, the naive
            // one is about 8 percentage points further along at 30 fps than at 240 fps. That difference is
            // what makes naive turning feel different on different hardware.
            float correctAt30 = SmoothFor(0.1f, PerchTurnRate, 1f / 30f);
            float correctAt240 = SmoothFor(0.1f, PerchTurnRate, 1f / 240f);
            float naiveAt30 = NaiveLerpFor(0.1f, PerchTurnRate, 1f / 30f);
            float naiveAt240 = NaiveLerpFor(0.1f, PerchTurnRate, 1f / 240f);

            Assert.That(Mathf.Abs(correctAt30 - correctAt240), Is.LessThan(0.005f),
                "The corrected smoothing must not care about the frame rate.");
            Assert.That(Mathf.Abs(naiveAt30 - naiveAt240), Is.GreaterThan(0.05f),
                "If this stops being true the comparison is no longer demonstrating anything.");
        }

        // ---------------------------------------------------------------- no overshoot

        [Test]
        public void Contrast_NaiveLerpSnapsOnALongFrame()
        {
            // A 200 ms hitch at rate 14 gives k*dt = 2.8. Mathf.Lerp clamps t, so this does not overshoot;
            // it snaps the whole way to the target in one frame, losing the smoothing exactly when a frame
            // spike makes a pop most visible. The factor form approaches 1 without reaching it.
            const float longFrame = 0.2f;

            float naive = Mathf.Lerp(0f, 1f, PerchTurnRate * longFrame);
            Assert.That(naive, Is.EqualTo(1f), "Mathf.Lerp clamps, so the naive version snaps.");

            float corrected = Damping.Toward(0f, 1f, PerchTurnRate, longFrame);
            Assert.That(corrected, Is.LessThan(1f), "Smoothing must survive a long frame.");
            Assert.That(corrected, Is.GreaterThan(0.9f), "It should still be most of the way there.");
        }

        [Test]
        public void Toward_NeverPassesTheTarget()
        {
            float value = 0f;
            for (int i = 0; i < 500; i++)
            {
                value = Damping.Toward(value, 1f, PerchTurnRate, 0.5f); // deliberately huge steps
                Assert.That(value, Is.LessThanOrEqualTo(1f));
            }

            Assert.That(value, Is.EqualTo(1f).Within(1e-4f), "and it should still converge");
        }

        [Test]
        public void Toward_WorksDownwardsToo()
        {
            float value = 10f;
            for (int i = 0; i < 200; i++)
            {
                value = Damping.Toward(value, 2f, 5f, 1f / 60f);
                Assert.That(value, Is.GreaterThanOrEqualTo(2f));
            }

            Assert.That(value, Is.EqualTo(2f).Within(1e-3f));
        }

        // ---------------------------------------------------------------- angles

        [Test]
        public void TowardAngle_TakesTheShortWayAroundZero()
        {
            // The perch snaps facing to 45-degree steps, so crossing 0/360 happens constantly.
            float result = Damping.TowardAngle(350f, 10f, PerchTurnRate, 1f / 60f);

            float before = Mathf.DeltaAngle(350f, 10f); // +20
            float after = Mathf.DeltaAngle(result, 10f);

            Assert.That(before, Is.GreaterThan(0f));
            Assert.That(after, Is.GreaterThan(0f), "It must not have turned the long way round.");
            Assert.That(after, Is.LessThan(before), "and it must have made progress");
        }

        [Test]
        public void TowardAngle_ConvergesAcrossTheWrap()
        {
            float value = 350f;
            for (int i = 0; i < 300; i++)
                value = Damping.TowardAngle(value, 10f, PerchTurnRate, 1f / 60f);

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(value, 10f)), Is.LessThan(0.01f));
        }

        [Test]
        public void TowardAngleWithSnap_SnapsInsideTheThreshold()
        {
            // The perch snaps within 2 degrees rather than creeping forever.
            float result = Damping.TowardAngleWithSnap(9f, 10f, PerchTurnRate, 1f / 60f, 2f);
            Assert.That(result, Is.EqualTo(10f));
        }

        [Test]
        public void TowardAngleWithSnap_DoesNotSnapOutsideTheThreshold()
        {
            float result = Damping.TowardAngleWithSnap(0f, 45f, PerchTurnRate, 1f / 60f, 2f);
            Assert.That(result, Is.Not.EqualTo(45f));
            Assert.That(result, Is.GreaterThan(0f).And.LessThan(45f));
        }

        [Test]
        public void TowardAngleWithSnap_SnapsAcrossTheWrap()
        {
            float result = Damping.TowardAngleWithSnap(359f, 0f, PerchTurnRate, 1f / 60f, 2f);
            Assert.That(result, Is.EqualTo(0f));
        }
    }
}
