using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>Why the ragdoll stopped simulating. Logged on recovery so a bad flight is diagnosable.</summary>
    public enum RecoverReason
    {
        /// <summary>Still flying.</summary>
        None = 0,

        /// <summary>Physics diverged or the body left the world. Not a normal landing.</summary>
        Forced,

        /// <summary>The flight ran out of time without ever settling. Usually means something is wrong.</summary>
        TimedOut,

        /// <summary>Landed, rolled, and came to rest. The normal path.</summary>
        Settled,

        /// <summary>On the ground long enough that we stopped waiting for it to be still.</summary>
        GaveUpOnGround,
    }

    /// <summary>Thresholds that decide when a ragdoll flight ends.</summary>
    /// <remarks>
    /// Two of the defaults are honest question marks:
    /// <list type="bullet">
    /// <item><see cref="GroundContactGiveUpTime"/> (2.65 s) happens to equal
    /// 1.4 + 1.25; nothing says that relationship is intentional.</item>
    /// <item><see cref="MinWorldY"/> and <see cref="MaxWorldY"/> (-80 / 2000) are hard-coded world bounds
    /// that can mask problems rather than solve them. They are kept as a backstop for
    /// a genuinely diverged body, but they are tied to the current map and world scale, so they need
    /// revisiting whenever either changes.</item>
    /// </list>
    /// Distances and speeds here assume the current world scale (1 unit = 25 cm, gravity -80). Revisit
    /// them if the scale changes.
    /// </remarks>
    public readonly struct RagdollRecoverySettings
    {
        /// <summary>Never recover before this, so a launch cannot end on the frame it starts.</summary>
        public readonly float MinTime;

        /// <summary>Give up after this long no matter what.</summary>
        public readonly float MaxTime;

        /// <summary>Ground contact must last this long before a still body counts as settled.</summary>
        public readonly float MinGroundContactTime;

        /// <summary>Ground contact this long ends the flight even if the body is still moving.</summary>
        public readonly float GroundContactGiveUpTime;

        /// <summary>At or below this torso speed the body counts as at rest.</summary>
        public readonly float MaxRecoverSpeed;

        /// <summary>Within this distance of the ground counts as contact.</summary>
        public readonly float GroundContactHeight;

        /// <summary>
        /// Extra height above <see cref="GroundContactHeight"/> the body must clear before the contact
        /// timer resets. This is the hysteresis band, so a bounce does not restart the landing clock.
        /// </summary>
        /// <remarks>
        /// A margin rather than a multiplier so the band cannot be configured away: any margin at or below
        /// zero would make the reset height equal the contact height, leaving no band at all, and a
        /// bouncing ragdoll would then reset its landing clock on every hop and never settle. The
        /// constructor floors it at <see cref="MinContactResetMargin"/>.
        /// </remarks>
        public readonly float ContactResetMargin;

        public readonly float MinWorldY;
        public readonly float MaxWorldY;

        public RagdollRecoverySettings(
            float minTime = 0.35f,
            float maxTime = 10f,
            float minGroundContactTime = 1.4f,
            float groundContactGiveUpTime = 2.65f,
            float maxRecoverSpeed = 16f,
            float groundContactHeight = 1.25f,
            float contactResetMargin = 0.625f,
            float minWorldY = -80f,
            float maxWorldY = 2000f)
        {
            MinTime = minTime;
            MaxTime = maxTime;
            MinGroundContactTime = minGroundContactTime;
            GroundContactGiveUpTime = groundContactGiveUpTime;
            MaxRecoverSpeed = maxRecoverSpeed;
            GroundContactHeight = groundContactHeight;
            ContactResetMargin = Mathf.Max(MinContactResetMargin, contactResetMargin);
            MinWorldY = minWorldY;
            MaxWorldY = maxWorldY;
        }

        /// <summary>
        /// Smallest hysteresis band worth having. Anything less and a body resting exactly at the contact
        /// height could flicker between counting and resetting on floating-point noise alone.
        /// </summary>
        public const float MinContactResetMargin = 0.05f;

        /// <summary>
        /// The height the body must rise above before the contact timer resets. Always strictly greater
        /// than <see cref="GroundContactHeight"/>, by construction.
        /// </summary>
        public float ContactResetHeight => GroundContactHeight + ContactResetMargin;

        /// <summary>The canonical preset.</summary>
        /// <remarks>
        /// Every value is written out even though the constructor declares the same defaults, and it has to
        /// stay that way. For a <b>struct</b>, <c>new RagdollRecoverySettings()</c> binds to the implicit
        /// zero-initialising constructor and never to one whose parameters are all optional, so the
        /// "obvious" shorter form silently produces all-zero settings: a zero MaxTime ends every flight as
        /// TimedOut on its first frame, and zero world bounds put the player out of bounds while it is
        /// standing still. Named arguments are safe (<c>new RagdollRecoverySettings(maxTime: 5f)</c>);
        /// only the no-argument form is the trap.
        /// </remarks>
        public static RagdollRecoverySettings Live => new RagdollRecoverySettings(
            minTime: 0.35f,
            maxTime: 10f,
            minGroundContactTime: 1.4f,
            groundContactGiveUpTime: 2.65f,
            maxRecoverSpeed: 16f,
            groundContactHeight: 1.25f,
            contactResetMargin: 0.625f, // reset at 1.875 m
            minWorldY: -80f,
            maxWorldY: 2000f);
    }

    /// <summary>
    /// Decides when a ragdoll flight is over. Pure logic over timers and distances, deliberately separate
    /// from the <c>PlayerRagdoll</c> component that owns the bodies, so the trickiest timing rules in the
    /// POC can be read and tested without a scene.
    /// </summary>
    /// <remarks>
    /// The component's job is to feed this: measure the distance to walkable ground under the torso, keep
    /// the contact timer with <see cref="UpdateGroundContactTime"/>, and act on
    /// <see cref="Evaluate"/>. It names *why* recovery
    /// happened, so a flight that times out is distinguishable from one that lands.
    /// </remarks>
    public static class RagdollRecovery
    {
        /// <summary>
        /// Whether a surface counts as walkable ground for landing purposes. The 0.35 default is
        /// a placeholder; it should follow the controller's slope limit.
        /// </summary>
        public static bool IsWalkableGround(Vector3 surfaceNormal, float minNormalY = 0.35f)
        {
            return surfaceNormal.y >= minNormalY;
        }

        /// <summary>
        /// Whether the body has left the playable world or its physics has diverged. This is a backstop for
        /// a broken simulation, not a game rule.
        /// </summary>
        public static bool IsOutOfBounds(in RagdollRecoverySettings s, Vector3 torsoPosition)
        {
            if (float.IsNaN(torsoPosition.x) || float.IsNaN(torsoPosition.y) || float.IsNaN(torsoPosition.z))
                return true;

            if (float.IsInfinity(torsoPosition.x) || float.IsInfinity(torsoPosition.y) || float.IsInfinity(torsoPosition.z))
                return true;

            return torsoPosition.y < s.MinWorldY || torsoPosition.y > s.MaxWorldY;
        }

        /// <summary>
        /// Advances the ground-contact timer. Pass <see cref="float.PositiveInfinity"/> for
        /// <paramref name="distanceToGround"/> when no walkable ground was found beneath the torso.
        /// </summary>
        /// <remarks>
        /// Three bands, and the middle one is the point of the whole function: below the contact height the
        /// timer runs, above the reset height it clears, and in between it *holds*. Without that gap a body
        /// bouncing across the contact threshold would restart its landing clock on every hop and could
        /// never satisfy <see cref="RagdollRecoverySettings.MinGroundContactTime"/>.
        /// </remarks>
        public static float UpdateGroundContactTime(
            in RagdollRecoverySettings s,
            float currentContactTime,
            float distanceToGround,
            float deltaTime)
        {
            if (float.IsNaN(distanceToGround))
                return 0f;

            if (distanceToGround <= s.GroundContactHeight)
                return currentContactTime + Mathf.Max(0f, deltaTime);

            if (distanceToGround > s.ContactResetHeight)
                return 0f;

            return currentContactTime;
        }

        /// <summary>
        /// Whether the flight should end, and why. Checked in priority order: a broken simulation first,
        /// then the hard time limit, then a real landing.
        /// </summary>
        public static RecoverReason Evaluate(
            in RagdollRecoverySettings s,
            float elapsedSeconds,
            float groundContactSeconds,
            float torsoSpeed,
            bool forceRecover)
        {
            if (forceRecover || float.IsNaN(torsoSpeed) || float.IsInfinity(torsoSpeed))
                return RecoverReason.Forced;

            if (elapsedSeconds >= s.MaxTime)
                return RecoverReason.TimedOut;

            // Nothing below here may end a flight that has only just started.
            if (elapsedSeconds < s.MinTime)
                return RecoverReason.None;

            if (groundContactSeconds < s.MinGroundContactTime)
                return RecoverReason.None;

            if (torsoSpeed <= s.MaxRecoverSpeed)
                return RecoverReason.Settled;

            if (groundContactSeconds >= s.GroundContactGiveUpTime)
                return RecoverReason.GaveUpOnGround;

            return RecoverReason.None;
        }

        /// <summary>Convenience form of <see cref="Evaluate"/> when the reason is not needed.</summary>
        public static bool ShouldRecover(
            in RagdollRecoverySettings s,
            float elapsedSeconds,
            float groundContactSeconds,
            float torsoSpeed,
            bool forceRecover)
        {
            return Evaluate(in s, elapsedSeconds, groundContactSeconds, torsoSpeed, forceRecover) != RecoverReason.None;
        }
    }
}
