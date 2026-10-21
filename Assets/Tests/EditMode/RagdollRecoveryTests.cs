using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the ragdoll flight-end rules, the hardest timing in the game to reason about: three overlapping time thresholds plus a speed check, all implicit in one
    /// boolean expression.
    /// </summary>
    public class RagdollRecoveryTests
    {
        static RagdollRecoverySettings Live => RagdollRecoverySettings.Live;

        [Test]
        public void Regression_LiveSettingsAreNotAllZero()
        {
            // For a struct, `new RagdollRecoverySettings()` binds to the implicit zero-initialising
            // constructor rather than to the one whose parameters are all optional, so writing Live the
            // short way silently produced all-zero settings. That made every flight end as TimedOut on its
            // first frame and put a standing player out of bounds. Caught by these tests when first run.
            RagdollRecoverySettings s = Live;

            Assert.That(s.MinTime, Is.GreaterThan(0f));
            Assert.That(s.MaxTime, Is.GreaterThan(1f));
            Assert.That(s.MinGroundContactTime, Is.GreaterThan(0f));
            Assert.That(s.GroundContactGiveUpTime, Is.GreaterThan(s.MinGroundContactTime));
            Assert.That(s.MaxRecoverSpeed, Is.GreaterThan(0f));
            Assert.That(s.GroundContactHeight, Is.GreaterThan(0f));
            Assert.That(s.ContactResetHeight, Is.GreaterThan(s.GroundContactHeight),
                "Without this gap there is no hysteresis and a bouncing body never settles.");
            Assert.That(s.ContactResetHeight, Is.EqualTo(1.875f).Within(1e-4f),
                "Contact resets at 1.875 m.");
            Assert.That(s.MinWorldY, Is.LessThan(0f));
            Assert.That(s.MaxWorldY, Is.GreaterThan(100f));
        }

        // ---------------------------------------------------------------- priority order

        [Test]
        public void Forced_EndsTheFlightImmediately()
        {
            // Even on the first frame, and even mid-air: a diverged simulation is not a landing.
            RecoverReason reason = RagdollRecovery.Evaluate(Live, 0f, 0f, 200f, forceRecover: true);
            Assert.That(reason, Is.EqualTo(RecoverReason.Forced));
        }

        [Test]
        public void Forced_OutranksTheTimeLimit()
        {
            RecoverReason reason = RagdollRecovery.Evaluate(Live, 99f, 0f, 0f, forceRecover: true);
            Assert.That(reason, Is.EqualTo(RecoverReason.Forced));
        }

        [Test]
        public void NaNSpeed_CountsAsForced()
        {
            // A diverging ragdoll is the one real NaN source, so it is checked where physics data arrives.
            Assert.That(RagdollRecovery.Evaluate(Live, 1f, 0f, float.NaN, false), Is.EqualTo(RecoverReason.Forced));
            Assert.That(RagdollRecovery.Evaluate(Live, 1f, 0f, float.PositiveInfinity, false), Is.EqualTo(RecoverReason.Forced));
        }

        [Test]
        public void TimesOut_AtTheMaximum()
        {
            // A flight that never lands still has to end, e.g. wedged in geometry.
            Assert.That(RagdollRecovery.Evaluate(Live, 9.99f, 0f, 100f, false), Is.EqualTo(RecoverReason.None));
            Assert.That(RagdollRecovery.Evaluate(Live, 10f, 0f, 100f, false), Is.EqualTo(RecoverReason.TimedOut));
        }

        // ---------------------------------------------------------------- the minimum flight time

        [Test]
        public void NothingNormalEndsTheFlightBeforeTheMinimum()
        {
            // Launching from a wire starts the torso close to the ground, so without this the flight could
            // end on the frame it began.
            RecoverReason reason = RagdollRecovery.Evaluate(Live, 0.34f, 5f, 0f, false);
            Assert.That(reason, Is.EqualTo(RecoverReason.None));
        }

        [Test]
        public void SettlesOncePastTheMinimumFlightTime()
        {
            RecoverReason reason = RagdollRecovery.Evaluate(Live, 0.35f, 5f, 0f, false);
            Assert.That(reason, Is.EqualTo(RecoverReason.Settled));
        }

        // ---------------------------------------------------------------- landing

        [Test]
        public void Settles_WhenSlowAndOnTheGroundLongEnough()
        {
            RecoverReason reason = RagdollRecovery.Evaluate(Live, 2f, 1.4f, 10f, false);
            Assert.That(reason, Is.EqualTo(RecoverReason.Settled));
        }

        [Test]
        public void DoesNotSettle_WhenGroundContactIsTooShort()
        {
            // Skimming the ground at speed is not a landing.
            RecoverReason reason = RagdollRecovery.Evaluate(Live, 2f, 1.39f, 0f, false);
            Assert.That(reason, Is.EqualTo(RecoverReason.None));
        }

        [Test]
        public void DoesNotSettle_WhileStillRollingFast()
        {
            RecoverReason reason = RagdollRecovery.Evaluate(Live, 2f, 1.5f, 50f, false);
            Assert.That(reason, Is.EqualTo(RecoverReason.None));
        }

        [Test]
        public void GivesUp_WhenOnTheGroundLongEnoughEvenIfStillMoving()
        {
            // A body sliding down a slope or twitching against geometry would otherwise never recover.
            Assert.That(RagdollRecovery.Evaluate(Live, 3f, 2.64f, 50f, false), Is.EqualTo(RecoverReason.None));
            Assert.That(RagdollRecovery.Evaluate(Live, 3f, 2.65f, 50f, false), Is.EqualTo(RecoverReason.GaveUpOnGround));
        }

        [Test]
        public void SpeedThresholdIsInclusive()
        {
            Assert.That(RagdollRecovery.Evaluate(Live, 2f, 1.5f, 16f, false), Is.EqualTo(RecoverReason.Settled));
            Assert.That(RagdollRecovery.Evaluate(Live, 2f, 1.5f, 16.01f, false), Is.EqualTo(RecoverReason.None));
        }

        [Test]
        public void ShouldRecover_AgreesWithEvaluate()
        {
            Assert.IsFalse(RagdollRecovery.ShouldRecover(Live, 2f, 0f, 100f, false));
            Assert.IsTrue(RagdollRecovery.ShouldRecover(Live, 2f, 1.5f, 0f, false));
        }

        // ---------------------------------------------------------------- the contact timer

        [Test]
        public void ContactTimer_AccumulatesWhileTouchingDown()
        {
            float contact = RagdollRecovery.UpdateGroundContactTime(Live, 0f, 1f, 0.02f);
            Assert.That(contact, Is.EqualTo(0.02f).Within(1e-5f));

            contact = RagdollRecovery.UpdateGroundContactTime(Live, contact, 0.5f, 0.02f);
            Assert.That(contact, Is.EqualTo(0.04f).Within(1e-5f));
        }

        [Test]
        public void ContactTimer_ResetsWellAboveTheGround()
        {
            float contact = RagdollRecovery.UpdateGroundContactTime(Live, 1.2f, 5f, 0.02f);
            Assert.That(contact, Is.EqualTo(0f));
        }

        [Test]
        public void ContactTimer_ResetsWhenThereIsNoGroundBelow()
        {
            float contact = RagdollRecovery.UpdateGroundContactTime(Live, 1.2f, float.PositiveInfinity, 0.02f);
            Assert.That(contact, Is.EqualTo(0f));
        }

        [Test]
        public void ContactTimer_HoldsInTheHysteresisBand()
        {
            // Between the contact height (1.25) and the reset height (1.875) the timer neither runs nor
            // clears. This band is the whole point of the function.
            float contact = RagdollRecovery.UpdateGroundContactTime(Live, 0.8f, 1.5f, 0.02f);
            Assert.That(contact, Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void ContactTimer_SurvivesABounce()
        {
            // The scenario the hysteresis exists for: land, bounce up past the contact height but not past
            // the reset height, come back down. Without the band the landing clock would restart on every
            // hop and a bouncy ragdoll could never satisfy MinGroundContactTime.
            const float step = 0.02f;
            RagdollRecoverySettings s = Live;
            float contact = 0f;

            // 0.5 s settling on the ground.
            for (int i = 0; i < 25; i++)
                contact = RagdollRecovery.UpdateGroundContactTime(in s, contact, 0.4f, step);
            Assert.That(contact, Is.EqualTo(0.5f).Within(1e-4f));

            // A small bounce into the band: the clock pauses but is not lost.
            for (int i = 0; i < 10; i++)
                contact = RagdollRecovery.UpdateGroundContactTime(in s, contact, 1.6f, step);
            Assert.That(contact, Is.EqualTo(0.5f).Within(1e-4f), "The bounce must not clear the timer.");

            // Back down, and it keeps counting from where it was.
            for (int i = 0; i < 50; i++)
                contact = RagdollRecovery.UpdateGroundContactTime(in s, contact, 0.3f, step);
            Assert.That(contact, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(contact, Is.GreaterThan(s.MinGroundContactTime), "so the body can now settle");
        }

        [Test]
        public void Regression_TheHysteresisBandCannotBeConfiguredAway()
        {
            // A margin rather than a multiplier means an empty band is unrepresentable. Before this, a
            // multiplier of 1 made the reset height equal the contact height, so a body bouncing just above
            // contact reset its landing clock every hop and the flight ran to the 10 s timeout instead of
            // settling -- the exact defect the band exists to prevent.
            var zeroed = new RagdollRecoverySettings(contactResetMargin: 0f);
            var negative = new RagdollRecoverySettings(contactResetMargin: -5f);

            Assert.That(zeroed.ContactResetHeight, Is.GreaterThan(zeroed.GroundContactHeight));
            Assert.That(negative.ContactResetHeight, Is.GreaterThan(negative.GroundContactHeight));
            Assert.That(zeroed.ContactResetMargin,
                Is.GreaterThanOrEqualTo(RagdollRecoverySettings.MinContactResetMargin));
        }

        [Test]
        public void ContactTimer_ABigBounceDoesResetIt()
        {
            float contact = 1.3f;
            contact = RagdollRecovery.UpdateGroundContactTime(Live, contact, 1.876f, 0.02f);
            Assert.That(contact, Is.EqualTo(0f), "Leaving the ground properly is a fresh landing.");
        }

        // ---------------------------------------------------------------- bounds backstop

        [Test]
        public void OutOfBounds_CatchesFallingThroughTheWorld()
        {
            Assert.IsTrue(RagdollRecovery.IsOutOfBounds(Live, new Vector3(0f, -80.1f, 0f)));
            Assert.IsTrue(RagdollRecovery.IsOutOfBounds(Live, new Vector3(0f, 2000.1f, 0f)));
        }

        [Test]
        public void OutOfBounds_AllowsNormalFlightHeights()
        {
            // The wire sits at y 31.2 and a full pull arcs well above it, so the ceiling must not clip
            // a legitimate lob.
            Assert.IsFalse(RagdollRecovery.IsOutOfBounds(Live, new Vector3(0f, 31.2f, 0f)));
            Assert.IsFalse(RagdollRecovery.IsOutOfBounds(Live, new Vector3(120f, 400f, -300f)));
        }

        [Test]
        public void OutOfBounds_CatchesDivergedPhysics()
        {
            Assert.IsTrue(RagdollRecovery.IsOutOfBounds(Live, new Vector3(float.NaN, 10f, 0f)));
            Assert.IsTrue(RagdollRecovery.IsOutOfBounds(Live, new Vector3(0f, float.NaN, 0f)));
            Assert.IsTrue(RagdollRecovery.IsOutOfBounds(Live, new Vector3(0f, 10f, float.PositiveInfinity)));
        }

        // ---------------------------------------------------------------- walkable ground

        [Test]
        public void WalkableGround_AcceptsFloorsAndRejectsWalls()
        {
            Assert.IsTrue(RagdollRecovery.IsWalkableGround(Vector3.up));
            Assert.IsTrue(RagdollRecovery.IsWalkableGround(new Vector3(0f, 0.35f, 0.94f)));
            Assert.IsFalse(RagdollRecovery.IsWalkableGround(new Vector3(0f, 0.34f, 0.94f)));
            Assert.IsFalse(RagdollRecovery.IsWalkableGround(Vector3.forward), "A pole side is not ground.");
        }

        // ---------------------------------------------------------------- a whole flight

        [Test]
        public void AWholeFlight_EndsAsSettledAtAPlausibleTime()
        {
            // Walks the state machine the way the component will, using the trace from a recorded
            // launch test: airborne for about a second, then landing and rolling to rest.
            RagdollRecoverySettings s = Live;
            const float step = 0.02f;

            float elapsed = 0f;
            float contact = 0f;
            RecoverReason reason = RecoverReason.None;

            for (int frame = 0; frame < 500 && reason == RecoverReason.None; frame++)
            {
                elapsed += step;

                // Airborne for the first second, on the ground after, slowing as it rolls.
                float distanceToGround = elapsed < 1f ? 20f : 0.3f;
                float speed = elapsed < 1f ? 40f : Mathf.Max(0f, 40f - (elapsed - 1f) * 60f);

                contact = RagdollRecovery.UpdateGroundContactTime(in s, contact, distanceToGround, step);
                reason = RagdollRecovery.Evaluate(in s, elapsed, contact, speed, false);
            }

            Assert.That(reason, Is.EqualTo(RecoverReason.Settled));
            Assert.That(elapsed, Is.GreaterThan(2.3f), "Landing plus the ground-contact wait.");
            Assert.That(elapsed, Is.LessThan(3f));
        }

        [Test]
        public void AFlightThatNeverLands_TimesOutRatherThanHanging()
        {
            RagdollRecoverySettings s = Live;
            const float step = 0.02f;

            float elapsed = 0f;
            float contact = 0f;
            RecoverReason reason = RecoverReason.None;

            for (int frame = 0; frame < 2000 && reason == RecoverReason.None; frame++)
            {
                elapsed += step;
                contact = RagdollRecovery.UpdateGroundContactTime(in s, contact, float.PositiveInfinity, step);
                reason = RagdollRecovery.Evaluate(in s, elapsed, contact, 30f, false);
            }

            Assert.That(reason, Is.EqualTo(RecoverReason.TimedOut));
            Assert.That(elapsed, Is.EqualTo(s.MaxTime).Within(step * 1.5f));
        }
    }
}
