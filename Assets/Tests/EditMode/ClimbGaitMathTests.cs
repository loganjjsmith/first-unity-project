using NUnit.Framework;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the climbing gait's stepping rules. The feel (step length, speed, height, which paws pair up)
    /// is judged in playtesting; these only guarantee the rules themselves behave.
    /// </summary>
    public class ClimbGaitMathTests
    {
        [Test]
        public void WantsStep_SmallDriftWithinReach_StaysPlanted()
        {
            Assert.IsFalse(ClimbGaitMath.WantsStep(drift: 0.1f, stepDistance: 0.2f, reachNeeded: 0.3f, legReach: 0.43f, out bool forced));
            Assert.IsFalse(forced);
        }

        [Test]
        public void WantsStep_DriftPastStepDistance_StepsUnforced()
        {
            Assert.IsTrue(ClimbGaitMath.WantsStep(drift: 0.25f, stepDistance: 0.2f, reachNeeded: 0.3f, legReach: 0.43f, out bool forced));
            Assert.IsFalse(forced);
        }

        [Test]
        public void WantsStep_OverReach_IsForcedEvenWithNoDrift()
        {
            // The stretched-leg bug from the first IK playtest: a leg that can't reach its planted spot must step.
            Assert.IsTrue(ClimbGaitMath.WantsStep(drift: 0f, stepDistance: 0.2f, reachNeeded: 0.5f, legReach: 0.43f, out bool forced));
            Assert.IsTrue(forced);
        }

        [Test]
        public void MaxDriftBeforeOverReach_IsThePythagoreanSlack()
        {
            Assert.That(ClimbGaitMath.MaxDriftBeforeOverReach(0.5f, 0.3f), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(ClimbGaitMath.MaxDriftBeforeOverReach(0.5f, 0.6f), Is.EqualTo(0f));
        }

        [Test]
        public void StepTrigger_Regression_FiresBeforeTheReachLimitSoGroupsStillAlternate()
        {
            // Playtest settings that made all four paws jump together: step distance 0.5, reach 0.5,
            // paws planted 0.4 out. Over-reach came after 0.3 of drift, long before 0.5.
            float maxDrift = ClimbGaitMath.MaxDriftBeforeOverReach(0.5f, 0.4f);
            float trigger = ClimbGaitMath.StepTrigger(0.5f, maxDrift, 0.85f);
            Assert.That(trigger, Is.LessThan(maxDrift));
            Assert.That(ClimbGaitMath.StepTrigger(0.1f, maxDrift, 0.85f), Is.EqualTo(0.1f));
        }

        [Test]
        public void LandAhead_LeadsAlongTheMotion_AndNotWhenStill()
        {
            var ideal = new UnityEngine.Vector3(1f, 2f, 3f);
            var landed = ClimbGaitMath.LandAhead(ideal, new UnityEngine.Vector3(0f, 8f, 0f), 0.1f);
            Assert.That((landed - ideal - new UnityEngine.Vector3(0f, 0.1f, 0f)).magnitude, Is.LessThan(1e-5f));
            Assert.That(ClimbGaitMath.LandAhead(ideal, UnityEngine.Vector3.zero, 0.1f), Is.EqualTo(ideal));
        }

        [Test]
        public void GroupMayStep_NothingStepping_IsTrue()
        {
            Assert.IsTrue(ClimbGaitMath.GroupMayStep(0, 0));
            Assert.IsTrue(ClimbGaitMath.GroupMayStep(1, 0));
        }

        [Test]
        public void GroupMayStep_OwnGroupStepping_IsTrue()
        {
            int mask = ClimbGaitMath.WithStepping(0, 1);
            Assert.IsTrue(ClimbGaitMath.GroupMayStep(1, mask));
        }

        [Test]
        public void GroupMayStep_OtherGroupStepping_IsFalse()
        {
            int mask = ClimbGaitMath.WithStepping(0, 0);
            Assert.IsFalse(ClimbGaitMath.GroupMayStep(1, mask));
        }

        [Test]
        public void GroupMayStep_OutOfRangeGroupsAreClampedRatherThanThrowing()
        {
            int mask = ClimbGaitMath.WithStepping(0, -5);
            Assert.IsTrue(ClimbGaitMath.GroupMayStep(0, mask));
            Assert.IsFalse(ClimbGaitMath.GroupMayStep(1, mask));
            Assert.DoesNotThrow(() => ClimbGaitMath.WithStepping(0, 99));
        }

        [Test]
        public void AdvanceStep_AccumulatesAndClampsAtOne()
        {
            float t = ClimbGaitMath.AdvanceStep(0f, 0.04f, 0.08f);
            Assert.That(t, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(ClimbGaitMath.AdvanceStep(t, 1f, 0.08f), Is.EqualTo(1f));
        }

        [Test]
        public void AdvanceStep_ZeroDurationLandsAtOnce_NegativeTimeDoesNotRewind()
        {
            Assert.That(ClimbGaitMath.AdvanceStep(0f, 0.01f, 0f), Is.EqualTo(1f));
            Assert.That(ClimbGaitMath.AdvanceStep(0.5f, -1f, 0.08f), Is.EqualTo(0.5f));
        }
    }
}
