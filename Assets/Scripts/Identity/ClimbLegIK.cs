using UnityEngine;
using UnityEngine.Animations.Rigging;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Plants the fox's paws on the pole while climbing. The Animation Rigging
    /// rig under <c>Fox/Rig 1</c> does the leg bending; this only fades that rig in and out with the
    /// Climbing mode and, each frame, moves every leg's IK target to where its paw should grip.
    /// </summary>
    /// <remarks>
    /// Runs after <see cref="PoleClimber"/> (execution order) so the targets are placed around the body's
    /// position for *this* frame; the rig then evaluates in the animation pass that follows Update. Placed
    /// in LateUpdate instead, the paws would trail a frame behind a body climbing at 16 u/s.
    /// </remarks>
    // Deliberately no [RequireComponent(typeof(PlayerModes))]: added to the wrong object, that attribute
    // silently builds a second player there (PlayerModes pulls in the whole Starter Assets controller).
    // Awake checks instead and switches itself off.
    [DefaultExecutionOrder(100)]
    public class ClimbLegIK : MonoBehaviour
    {
        [System.Serializable]
        public class Leg
        {
            public string Name;

            [Tooltip("Where this paw's ray starts: the leg's shoulder or hip bone, e.g. FrontUpperLeg.L.")]
            public Transform RayOrigin;

            [Tooltip("This leg's IK target, e.g. FrontLeg.L_IK_target. Both the leg and its paw follow it.")]
            public Transform Target;

            [Tooltip("-1 for a left leg, +1 for a right leg: which way round the pole this paw reaches.")]
            public float Side = 1f;

            [Tooltip("Legs in the same group step together; a group waits while another is mid-step. " +
                     "Diagonal pairs: FL 0, FR 1, BL 1, BR 0. Player-style bounding: FL 0, FR 0, BL 1, BR 1.")]
            public int Group;

            // Runtime stepping state; not saved with the scene.
            [System.NonSerialized] public bool HasPlant;
            [System.NonSerialized] public Vector3 Planted;
            [System.NonSerialized] public bool Stepping;
            [System.NonSerialized] public Vector3 StepFrom;
            [System.NonSerialized] public Vector3 StepTo;
            [System.NonSerialized] public float StepT;
        }

        [Tooltip("The Rig whose weight fades in while climbing (Fox/Rig 1). Its saved weight should be 0.")]
        public Rig ClimbRig;

        public Leg[] Legs = new Leg[4];

        [Tooltip("Rig weight change per second. 6 fades fully in or out in about a sixth of a second.")]
        public float FadeSpeed = 6f;

        [Tooltip("How far each paw's ray reaches from its shoulder or hip.")]
        public float PawReach = 0.8f;

        [Tooltip("How much each paw's ray tilts sideways around the pole. 0 reaches straight at it.")]
        public float SideSplay = 0.5f;

        [Tooltip("Layer poles live on. Defaults to Climbable, the same as PoleClimber.")]
        public LayerMask PoleLayer;

        [Header("Gait")]
        [Tooltip("How far a paw's ideal spot may drift from where it is planted before it steps.")]
        public float StepDistance = 0.2f;

        [Tooltip("Seconds a step takes from lift to landing.")]
        public float StepDuration = 0.08f;

        [Tooltip("How far a stepping paw lifts off the pole at the middle of its step.")]
        public float StepHeight = 0.08f;

        [Tooltip("A paw steps (waiting its group's turn) once it is this fraction of the way to over-stretching, " +
                 "even if it has not drifted Step Distance yet. Keeps the hard, group-ignoring step a last resort.")]
        [Range(0f, 1f)] public float EarlyFraction = 0.85f;

        [Tooltip("How far past its ideal spot a step lands, as a fraction of the step trigger distance, in the " +
                 "direction the body is moving. The body then passes over the paw, so each grip lasts longer.")]
        [Range(0f, 0.9f)] public float LandAheadFraction = 0.5f;

        PlayerModes modes;
        Vector3 lastBodyPosition;
        Vector3 bodyVelocity;

        void Awake()
        {
            modes = GetComponent<PlayerModes>();
            if (modes == null)
            {
                Debug.LogError("ClimbLegIK belongs on PlayerArmature, next to PlayerModes and PoleClimber. " +
                               "Disabled on '" + name + "'.", this);
                enabled = false;
                return;
            }
            if (PoleLayer == 0)
                PoleLayer = LayerMask.GetMask("Climbable");
            lastBodyPosition = transform.position;
        }

        void Update()
        {
            if (ClimbRig == null)
                return;

            if (Time.deltaTime > 0f)
                bodyVelocity = (transform.position - lastBodyPosition) / Time.deltaTime;
            lastBodyPosition = transform.position;

            float goal = modes.Current == PlayerMode.Climbing ? 1f : 0f;
            ClimbRig.weight = Mathf.MoveTowards(ClimbRig.weight, goal, FadeSpeed * Time.deltaTime);

            // Fully off means walking is exactly as it was before the rig existed; skip the rays too, and
            // forget the plants so the next climb starts from fresh ones rather than last climb's spots.
            if (ClimbRig.weight <= 0f)
            {
                foreach (Leg leg in Legs)
                    if (leg != null)
                        leg.HasPlant = leg.Stepping = false;
                return;
            }

            int steppingGroups = 0;
            foreach (Leg leg in Legs)
                if (leg != null && leg.Stepping)
                    steppingGroups = ClimbGaitMath.WithStepping(steppingGroups, leg.Group);

            foreach (Leg leg in Legs)
            {
                if (leg == null || leg.RayOrigin == null || leg.Target == null)
                    continue;
                steppingGroups = TickLeg(leg, steppingGroups);
            }
        }

        /// <summary>One paw for one frame: hold its plant, carry on a step, or start one.</summary>
        int TickLeg(Leg leg, int steppingGroups)
        {
            bool hit = TryPlacePaw(leg, out Vector3 ideal);

            // When this paw should step, and how far ahead it should land, both scale with how much room
            // its leg has around the ideal spot, so the same settings hold on thin poles and thick ones.
            float maxDrift = hit
                ? ClimbGaitMath.MaxDriftBeforeOverReach(PawReach, Vector3.Distance(leg.RayOrigin.position, ideal))
                : 0f;
            float trigger = ClimbGaitMath.StepTrigger(StepDistance, maxDrift, EarlyFraction);
            Vector3 landing = ClimbGaitMath.LandAhead(ideal, bodyVelocity, trigger * LandAheadFraction);

            if (!leg.HasPlant)
            {
                // Nothing to plant on yet: leave the paw where the rig has it until a ray finds the pole.
                if (!hit)
                    return steppingGroups;
                leg.Planted = ideal;
                leg.HasPlant = true;
            }

            if (leg.Stepping)
            {
                // The step lands on where the paw *now* wants to be, not where it wanted to be at lift-off,
                // so a paw keeps up with a body that moved on during the step. A miss mid-step keeps the
                // last spot a ray actually found.
                if (hit)
                    leg.StepTo = landing;
                leg.StepT = ClimbGaitMath.AdvanceStep(leg.StepT, Time.deltaTime, StepDuration);
                leg.Target.position = StepArc(leg.StepFrom, leg.StepTo, leg.StepT, transform.up);
                if (leg.StepT >= 1f)
                {
                    leg.Stepping = false;
                    leg.Planted = leg.StepTo;
                }
                return steppingGroups;
            }

            leg.Target.position = leg.Planted;

            // A planted paw only steps when there is somewhere real to step to.
            if (!hit)
                return steppingGroups;

            float drift = Vector3.Distance(leg.Planted, ideal);
            float reachNeeded = Vector3.Distance(leg.RayOrigin.position, leg.Planted);
            if (ClimbGaitMath.WantsStep(drift, trigger, reachNeeded, PawReach, out bool forced) &&
                (forced || ClimbGaitMath.GroupMayStep(leg.Group, steppingGroups)))
            {
                leg.Stepping = true;
                leg.StepFrom = leg.Planted;
                leg.StepTo = landing;
                leg.StepT = 0f;
                steppingGroups = ClimbGaitMath.WithStepping(steppingGroups, leg.Group);
            }
            return steppingGroups;
        }

        void OnEnable()
        {
            if (modes != null)
                modes.Respawned += ForgetPlants;
        }

        void OnDisable()
        {
            if (modes != null)
                modes.Respawned -= ForgetPlants;
            if (ClimbRig != null)
                ClimbRig.weight = 0f;
        }

        /// <summary>
        /// A respawn or teleport is an instant cut: drop the old pole's grips at once. Otherwise the rig
        /// spends its fade-out stretching the legs back toward paw spots on a pole across the map.
        /// </summary>
        void ForgetPlants()
        {
            if (ClimbRig != null)
                ClimbRig.weight = 0f;
            foreach (Leg leg in Legs)
                if (leg != null)
                    leg.HasPlant = leg.Stepping = false;
            lastBodyPosition = transform.position;
        }

        // ------------------------------------------------------------ paw placement

        /// <summary>
        /// Where this leg's paw should go this frame, if anywhere. While climbing, the fox is turned so its
        /// feet point at the pole, which makes <c>-transform.up</c> "toward the pole" and
        /// <c>transform.right</c> "sideways round it". Returns false on a miss, and then
        /// <paramref name="spot"/> means nothing: a miss is "no new spot", never "wherever the paw is now",
        /// because mid-step that is the lifted arc point and feeding it back ratchets the paw off the pole.
        /// </summary>
        bool TryPlacePaw(Leg leg, out Vector3 spot)
        {
            Vector3 rayDirection = -transform.up;
            rayDirection += transform.right * leg.Side * SideSplay;
            rayDirection = rayDirection.normalized;

            if (Physics.Raycast(leg.RayOrigin.position, rayDirection, out RaycastHit hit, PawReach, PoleLayer, QueryTriggerInteraction.Ignore))
            {
                spot = hit.point;
                return true;
            }

            spot = default;
            return false;
        }

        /// <summary>
        /// Where a stepping paw is, <paramref name="t"/> of the way through its step: 0 = still at
        /// <paramref name="from"/>, 1 = landed on <paramref name="to"/>. <paramref name="liftDirection"/> is
        /// "away from the pole" (the fox's up while climbing).
        /// </summary>
        Vector3 StepArc(Vector3 from, Vector3 to, float t, Vector3 liftDirection)
        {
            // Slide along the pole, plus a sine bump off it: 0 at lift-off and landing, StepHeight midway.
            return Vector3.Lerp(from, to, t) + (Mathf.Sin(t * Mathf.PI) * StepHeight * liftDirection);
        }
    }
}