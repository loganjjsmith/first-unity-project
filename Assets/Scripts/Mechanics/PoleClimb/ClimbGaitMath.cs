using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// The rules for when a climbing paw steps (procedural stepping).
    /// Each paw stays planted on the pole until the body has carried its ideal spot far enough away, then
    /// lifts and steps to it. Legs belong to numbered groups, and a group may only start stepping while no
    /// *other* group is mid-step, so two neighbouring legs never both leave the pole at once.
    /// </summary>
    /// <remarks>
    /// Groups are tracked as a bitmask (bit N set = some leg in group N is stepping) so the per-frame check
    /// allocates nothing. Groups 0 and 1 are all the gaits here need: diagonal pairs or front/back bounding.
    /// </remarks>
    public static class ClimbGaitMath
    {
        /// <summary>Highest usable group number; keeps the bit shift inside an int.</summary>
        public const int MaxGroup = 30;

        /// <summary>
        /// Whether a planted paw wants to step. It does when its ideal spot has drifted more than
        /// <paramref name="stepDistance"/> from where it is planted, or, whatever the drift, when the body
        /// has moved so far that the leg can no longer reach the planted spot. That second case is
        /// <paramref name="forced"/>: it overrides the group rule, because the alternative is a stretched leg.
        /// </summary>
        public static bool WantsStep(float drift, float stepDistance, float reachNeeded, float legReach, out bool forced)
        {
            forced = reachNeeded > legReach;
            return forced || drift > stepDistance;
        }

        /// <summary>
        /// How far along the surface a paw can drift from where its leg sits comfortably before the leg can
        /// no longer reach it: the other side of the right triangle whose hypotenuse is the leg's reach and
        /// whose near side is the joint's distance to its ideal spot. 0 when the ideal spot is already out
        /// of reach.
        /// </summary>
        public static float MaxDriftBeforeOverReach(float legReach, float idealDistance)
        {
            float slack = legReach * legReach - idealDistance * idealDistance;
            return slack > 0f ? Mathf.Sqrt(slack) : 0f;
        }

        /// <summary>
        /// The drift at which a paw asks for an ordinary, group-respecting step: the tuned step distance,
        /// but never later than <paramref name="earlyFraction"/> of the way to over-reach. Without that cap
        /// a leg can hit its reach limit first, and forced steps ignore the groups, so every paw jumps at
        /// once and all four grip and release together.
        /// </summary>
        public static float StepTrigger(float stepDistance, float maxDrift, float earlyFraction)
        {
            return Mathf.Max(0f, Mathf.Min(stepDistance, maxDrift * Mathf.Clamp01(earlyFraction)));
        }

        /// <summary>
        /// Where a step should land: <paramref name="aheadDistance"/> past the ideal spot in the direction
        /// the body is moving, so the body travels over the paw and each plant lasts longer. A body that is
        /// barely moving lands on the ideal spot itself.
        /// </summary>
        public static Vector3 LandAhead(Vector3 ideal, Vector3 bodyVelocity, float aheadDistance)
        {
            const float StillSpeedSqr = 1e-4f;
            if (bodyVelocity.sqrMagnitude < StillSpeedSqr || aheadDistance <= 0f)
                return ideal;
            return ideal + bodyVelocity.normalized * aheadDistance;
        }

        /// <summary>True when no group other than <paramref name="group"/> has a leg mid-step.</summary>
        public static bool GroupMayStep(int group, int steppingGroupsMask)
        {
            return (steppingGroupsMask & ~Bit(group)) == 0;
        }

        /// <summary>Marks <paramref name="group"/> as having a leg mid-step.</summary>
        public static int WithStepping(int steppingGroupsMask, int group)
        {
            return steppingGroupsMask | Bit(group);
        }

        /// <summary>
        /// Advances a step's progress (0 = just lifted, 1 = landed). A non-positive duration lands at once
        /// rather than dividing by zero.
        /// </summary>
        public static float AdvanceStep(float t, float deltaTime, float stepDuration)
        {
            if (stepDuration <= 0f)
                return 1f;
            return Mathf.Clamp01(t + Mathf.Max(0f, deltaTime) / stepDuration);
        }

        static int Bit(int group)
        {
            return 1 << Mathf.Clamp(group, 0, MaxGroup);
        }
    }
}
