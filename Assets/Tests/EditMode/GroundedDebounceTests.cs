using NUnit.Framework;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the Walking/Airborne debounce, and in particular the downhill-sprint flicker.
    /// </summary>
    public class GroundedDebounceTests
    {
        const float Frame = 1f / 60f;
        const float Grace = 0.15f; // Starter Assets' FallTimeout default

        [Test]
        public void Regression_ABlinkingGroundCheckNeverReadsAsAirborne()
        {
            // Downhill sprint: two ungrounded frames out of every three, for two seconds.
            GroundedDebounce debounce = new GroundedDebounce();
            for (int frame = 0; frame < 120; frame++)
            {
                bool grounded = frame % 3 == 0;
                Assert.IsFalse(debounce.Tick(grounded, Frame, Grace), $"frame {frame} flipped to airborne");
            }
        }

        [Test]
        public void ASustainedFallReadsAsAirborneOnceTheGraceRunsOut()
        {
            GroundedDebounce debounce = new GroundedDebounce();
            int framesToAirborne = -1;
            for (int frame = 1; frame <= 60 && framesToAirborne < 0; frame++)
            {
                if (debounce.Tick(false, Frame, Grace))
                    framesToAirborne = frame;
            }

            // 0.15 s at 60 fps is 9 frames; allow one frame of float rounding either way.
            Assert.That(framesToAirborne, Is.InRange(8, 10));
        }

        [Test]
        public void AGroundedFrameStartsTheCountAgain()
        {
            GroundedDebounce debounce = new GroundedDebounce();
            debounce.Tick(false, 0.1f, Grace);
            debounce.Tick(true, Frame, Grace);
            Assert.IsFalse(debounce.Tick(false, 0.1f, Grace), "0.1 s after touching down is not a fall yet");
            Assert.IsTrue(debounce.Tick(false, 0.1f, Grace));
        }

        [Test]
        public void ResetForgetsAPartialCount()
        {
            GroundedDebounce debounce = new GroundedDebounce();
            debounce.Tick(false, 0.14f, Grace);
            debounce.Reset();
            Assert.IsFalse(debounce.Tick(false, 0.02f, Grace));
        }

        [Test]
        public void NegativeOrNaNTimeDoesNotCount()
        {
            GroundedDebounce debounce = new GroundedDebounce();
            Assert.IsFalse(debounce.Tick(false, -1f, Grace));
            Assert.IsFalse(debounce.Tick(false, float.NaN, Grace));
            Assert.IsFalse(debounce.Tick(false, 0.1f, Grace));
        }

        [Test]
        public void ZeroGraceReadsAirborneOnTheFirstUngroundedFrame()
        {
            Assert.IsTrue(new GroundedDebounce().Tick(false, Frame, 0f));
        }
    }
}
