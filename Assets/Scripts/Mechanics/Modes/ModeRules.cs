using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// The single source of truth for which mode changes are legal, and what each mode implies about the
    /// base controller. Pure lookup, no state.
    /// </summary>
    /// <remarks>
    /// The transition table, in code. With the mode decided in one place, no transition can quietly undo
    /// state another has just set up. Almost every transition goes through <see cref="Next"/>, and the component's job shrinks to asking
    /// and obeying. Two exceptions set the mode directly: <c>PlayerModes.Launch</c>, whose callers have
    /// already let go of their pole or wire (it asserts against the Jump rows instead), and respawn, which
    /// is legal from everywhere.
    ///
    /// The lockouts that decide *whether a trigger fires at all* are separate, in <see cref="Lockouts"/>.
    /// This type answers only "given that it fired, where do we end up".
    /// </remarks>
    public static class ModeRules
    {
        /// <summary>
        /// The mode this trigger leads to, or <c>null</c> when the trigger does not apply in this mode.
        /// A null result is normal and means "ignore it", not an error.
        /// </summary>
        public static PlayerMode? Next(PlayerMode from, ModeTrigger trigger)
        {
            // Respawn is legal from everywhere, including mid-flight and mid-stun, and it clears the stun
            // rather than being swallowed by it.
            if (trigger == ModeTrigger.Respawn)
                return PlayerMode.Walking;

            // Going limp is also legal from everywhere, mid-stun and mid-flight included: a knock doesn't
            // wait for a convenient moment. From a guided flight it drops the guidance.
            if (trigger == ModeTrigger.GoLimp)
                return from == PlayerMode.Ragdoll ? (PlayerMode?)null : PlayerMode.Ragdoll;

            switch (from)
            {
                case PlayerMode.Walking:
                    switch (trigger)
                    {
                        case ModeTrigger.GrabPole: return PlayerMode.Climbing;
                        case ModeTrigger.Jump: return PlayerMode.Airborne;
                        case ModeTrigger.LeaveGround: return PlayerMode.Airborne;
                        default: return null;
                    }

                case PlayerMode.Airborne:
                    switch (trigger)
                    {
                        case ModeTrigger.GrabPole: return PlayerMode.Climbing;
                        case ModeTrigger.GrabWireFromAir: return PlayerMode.WireRiding;
                        case ModeTrigger.Land: return PlayerMode.Walking;
                        default: return null;
                    }

                case PlayerMode.Launched:
                    // A launch is still airborne, so the same grabs apply. Whether they are *allowed* is
                    // the lockouts' business: a pole kick locks the pole briefly, and a wire drop locks
                    // that wire. (PoleClimber does not act on GrabPole while Launched yet.)
                    switch (trigger)
                    {
                        case ModeTrigger.GrabPole: return PlayerMode.Climbing;
                        case ModeTrigger.GrabWireFromAir: return PlayerMode.WireRiding;
                        case ModeTrigger.Land: return PlayerMode.Walking;
                        default: return null;
                    }

                case PlayerMode.Climbing:
                    switch (trigger)
                    {
                        case ModeTrigger.Jump: return PlayerMode.Launched;
                        case ModeTrigger.ReachPoleTop: return PlayerMode.Perched;
                        case ModeTrigger.ReachPoleBottom: return PlayerMode.Walking;
                        case ModeTrigger.PoleLost: return PlayerMode.Walking;
                        default: return null;
                    }

                case PlayerMode.Perched:
                    switch (trigger)
                    {
                        case ModeTrigger.MountWireFromPerch: return PlayerMode.WireRiding;
                        case ModeTrigger.Jump: return PlayerMode.Launched;
                        case ModeTrigger.DescendFromPerch: return PlayerMode.Climbing;
                        // Deliberately no PoleLost here: a perch
                        // that silently became Walking would drop the player from the pole top with no
                        // placement rule. If a pole can ever vanish under a perch, that needs specifying.
                        default: return null;
                    }

                case PlayerMode.WireRiding:
                    switch (trigger)
                    {
                        case ModeTrigger.Jump: return PlayerMode.Launched;
                        case ModeTrigger.StartCharge: return PlayerMode.SlingshotCharging;
                        // A free end (no pole) hops off; an end at a pole perches instead.
                        case ModeTrigger.ReachWireEnd: return PlayerMode.Launched;
                        case ModeTrigger.PerchFromWire: return PlayerMode.Perched;
                        default: return null;
                    }

                case PlayerMode.SlingshotCharging:
                    switch (trigger)
                    {
                        // Jump is checked before the pull input, so a cancel beats a release.
                        case ModeTrigger.Jump: return PlayerMode.Launched;
                        case ModeTrigger.ReleaseChargeWeak: return PlayerMode.WireRiding;
                        case ModeTrigger.ReleaseChargeStrong: return PlayerMode.RagdollFlight;
                        default: return null;
                    }

                case PlayerMode.RagdollFlight:
                case PlayerMode.Ragdoll:
                    switch (trigger)
                    {
                        case ModeTrigger.Recover: return PlayerMode.Stunned;
                        default: return null;
                    }

                case PlayerMode.Stunned:
                    switch (trigger)
                    {
                        case ModeTrigger.StunExpired: return PlayerMode.Walking;
                        // Everything else is deliberately swallowed, so a stun cannot be climbed or
                        // walked out of early.
                        default: return null;
                    }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Whether the <c>CharacterController</c> should be enabled in this mode.
        /// </summary>
        /// <remarks>
        /// On in Walking, Airborne and Launched; off while another system places the body directly. Stunned
        /// is on because the player is stood on the ground being collided with, just not accepting input:
        /// a stun allows camera control only, no movement.
        /// </remarks>
        public static bool UsesCharacterController(PlayerMode mode)
        {
            switch (mode)
            {
                case PlayerMode.Walking:
                case PlayerMode.Airborne:
                case PlayerMode.Launched:
                case PlayerMode.Stunned:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether the base controller's own locomotion should run, i.e. what the
        /// <c>LocomotionEnabled</c> edit to <c>ThirdPersonController</c> gates.
        /// </summary>
        /// <remarks>
        /// Camera look is never gated by this. Disabling the whole component would freeze the camera, which
        /// is the entire reason for the flag.
        ///
        /// Launched is false even though the controller is enabled, because the ballistic mover drives it
        /// and two things calling <c>Move</c> in one frame would fight over the body. Launched gets a soft air
        /// steer, much weaker than walking; that lives in the ballistic mover, so base locomotion stays off.
        /// </remarks>
        public static bool UsesBaseLocomotion(PlayerMode mode)
        {
            return mode == PlayerMode.Walking || mode == PlayerMode.Airborne;
        }

        /// <summary>
        /// The flight a launch becomes. The launch maths is the same
        /// whatever caused it, and only intent differs - a chosen launch stays in control, an inflicted one
        /// flops.
        /// </summary>
        public static PlayerMode FlightFor(LaunchCause cause)
        {
            return cause == LaunchCause.Purposeful ? PlayerMode.Launched : PlayerMode.RagdollFlight;
        }

        /// <summary>Whether ragdoll physics owns the body: a guided flight or a plain limp ragdoll.</summary>
        public static bool IsRagdolled(PlayerMode mode)
        {
            return mode == PlayerMode.RagdollFlight || mode == PlayerMode.Ragdoll;
        }

        /// <summary>Whether this mode holds onto a pole or a wire, and so must release it on exit.</summary>
        public static bool IsAttached(PlayerMode mode)
        {
            switch (mode)
            {
                case PlayerMode.Climbing:
                case PlayerMode.Perched:
                case PlayerMode.WireRiding:
                case PlayerMode.SlingshotCharging:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether the player is off the ground in a way the Animator should show as airborne.</summary>
        public static bool IsAirborne(PlayerMode mode)
        {
            switch (mode)
            {
                case PlayerMode.Airborne:
                case PlayerMode.Launched:
                case PlayerMode.RagdollFlight:
                    return true;
                default:
                    return false;
            }
        }
    }
}
