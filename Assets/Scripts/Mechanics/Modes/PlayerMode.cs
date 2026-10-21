using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Every state the player can be in. Exactly one is current, and it is the only thing that decides
    /// who owns the body.
    /// </summary>
    /// <remarks>
    /// States that could otherwise hide in flags and timers - <see cref="Airborne"/>, <see cref="Launched"/>,
    /// <see cref="RagdollFlight"/>, <see cref="Stunned"/> - are real modes, so a question such as "am I in
    /// the air?" has one answer, in one place.
    /// </remarks>
    public enum PlayerMode
    {
        /// <summary>On the ground under the base controller. The default.</summary>
        Walking = 0,

        /// <summary>In the air under the base controller, after a normal jump or walking off an edge.</summary>
        Airborne,

        /// <summary>In the air on a ballistic path we drive: a pole kick, a wire drop or a wire-end hop.</summary>
        Launched,

        /// <summary>Stuck to a pole, climbing and orbiting.</summary>
        Climbing,

        /// <summary>Sat on top of a pole in the gargoyle pose.</summary>
        Perched,

        /// <summary>Riding the wire as a sticky rail.</summary>
        WireRiding,

        /// <summary>Holding the pull on the wire, stretching the band.</summary>
        SlingshotCharging,

        /// <summary>Flung, floppy, and simulating.</summary>
        RagdollFlight,

        /// <summary>Just stood up after a flight. No walking, no jumping, no grabbing.</summary>
        Stunned,

        /// <summary>
        /// Gone limp: ragdoll physics with only the momentum it already had, and no air guidance ever.
        /// For short knocks, where <see cref="RagdollFlight"/> is the long guided kind (a slingshot
        /// fling). Appended last so saved enum values keep
        /// their meaning.
        /// </summary>
        Ragdoll,
    }

    /// <summary>
    /// Why a launch happened, which decides the flight it becomes. The launch maths is the same either way;
    /// only the resulting mode differs. See <see cref="ModeRules.FlightFor"/>.
    /// </summary>
    public enum LaunchCause
    {
        /// <summary>The player chose it: a controlled slingshot pull (C).</summary>
        Purposeful,

        /// <summary>Something did it to the player: in this demo, the ragdoll slingshot pull (X).</summary>
        Hazard,
    }

    /// <summary>
    /// Everything that can cause a mode change: the inputs to the transition table in
    /// <see cref="ModeRules.Next"/>.
    /// </summary>
    public enum ModeTrigger
    {
        /// <summary>The pull input is held near a climbable pole, and the pole is grabbable.</summary>
        GrabPole,

        /// <summary>Airborne within reach of a wire that is not locked out.</summary>
        GrabWireFromAir,

        /// <summary>A fresh forward press while perched, with a wire in yoink range.</summary>
        MountWireFromPerch,

        /// <summary>The jump edge.</summary>
        Jump,

        /// <summary>The slingshot input went down while riding.</summary>
        StartCharge,

        /// <summary>Released below the minimum stretch, so it snaps back instead of launching.</summary>
        ReleaseChargeWeak,

        /// <summary>Released above the minimum stretch.</summary>
        ReleaseChargeStrong,

        /// <summary>Climbed to the top while still climbing up.</summary>
        ReachPoleTop,

        /// <summary>Descended to the bottom while still climbing down.</summary>
        ReachPoleBottom,

        /// <summary>The pole stopped being there. A safety exit, not a player action.</summary>
        PoleLost,

        /// <summary>A press of the descend input while perched.</summary>
        DescendFromPerch,

        /// <summary>Travelled off the end of the wire.</summary>
        ReachWireEnd,

        /// <summary>Touched down under the base controller or the ballistic mover.</summary>
        Land,

        /// <summary>The ragdoll flight is over. See <see cref="RagdollRecovery"/>.</summary>
        Recover,

        /// <summary>The post-flight stun ran out.</summary>
        StunExpired,

        /// <summary>The respawn input. Legal from every mode.</summary>
        Respawn,

        /// <summary>
        /// The base controller stopped being grounded: a jump, or walking off an edge. Without this a normal
        /// jump never left Walking, so nothing that only runs in the air (the wire grab) could see it.
        /// </summary>
        LeaveGround,

        /// <summary>
        /// Rode into a pole: the end of the line, a junction with no wire continuing ahead, or stopping
        /// next to a midway pole.
        /// </summary>
        PerchFromWire,

        /// <summary>
        /// Go limp into <see cref="PlayerMode.Ragdoll"/>. Legal from every mode but Ragdoll itself, like
        /// respawn: a knock can land at any moment.
        /// </summary>
        GoLimp,
    }
}
