using StarterAssets;
using UnityEngine;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Grab a pole, climb it, perch at the top, demount at the bottom, or kick off in any direction. The
    /// pure geometry lives in <see cref="PoleClimbMath"/>; this MonoBehaviour finds poles, reads input, and
    /// drives the transform directly while <see cref="PlayerModes"/> has disabled the
    /// <see cref="CharacterController"/> for Climbing/Perched.
    /// </summary>
    [RequireComponent(typeof(PlayerModes))]
    [RequireComponent(typeof(ThirdPersonController))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StarterAssetsInputs))]
    public class PoleClimber : MonoBehaviour
    {
        [Header("Tuning")]
        public float ClimbSpeed = 16f;
        public float ClimbSurfacePadding = 0.2f;

        [Tooltip("Gap from the pole's surface to the body's centre while climbing. The default 0.55 is the " +
                 "capsule radius plus ClimbSurfacePadding, i.e. no change. Lower it to hug the pole; ClimbLegIK " +
                 "then plants the paws on the surface. Grab reach, demount and jump-off still use the padding.")]
        public float ClimbHugDistance = 0.55f;
        public float ClimbOrbitSpeed = 180f;
        public float ClimbDemountBodyHeights = 0.35f;
        public int PerchYawSteps = 8;
        public float PerchTopPadding = 0.2f;
        public float PerchTurnSpeed = 14f;
        public float PoleJumpOutSpeed = 3f;
        public float PoleJumpUpSpeed = 14f;
        public float ClimbReentryLockDuration = 0.45f;

        [Tooltip("Leap off a perch, toward where the player faces. The mid-climb kick-off uses " +
                 "PoleJumpOut/UpSpeed instead.")]
        public float PerchJumpOutSpeed = 10f;
        public float PerchJumpUpSpeed = 12f;

        [Tooltip("Animator Speed value driven while actively climbing/orbiting (any W/S/A/D input) - a " +
                 "best-effort stand-in for a real climb animation, since the Fox pack has none. Anywhere " +
                 "in the Walk band (0-24) works; this doesn't need to match a real climb speed.")]
        public float ClimbMovingAnimSpeed = 8f;

        [Header("Scene")]
        [Tooltip("Layer poles live on - used for the grab broadphase.")]
        public LayerMask PoleLayer;

        [Tooltip("Layer the ground lives on - used to place the feet on demount.")]
        public LayerMask GroundLayer;

        [Tooltip("Broadphase radius for finding nearby poles. The real grab reach is much shorter " +
                 "(~2m, see PoleClimbMath.CanGrab) - this is only how far out candidates are even considered.")]
        public float GrabBroadphaseRadius = 3f;

        PlayerModes modes;
        ThirdPersonController controller;
        CharacterController characterController;
        StarterAssetsInputs inputs;
        Animator animator;

        Collider currentPole;
        Vector3 offsetDir;
        float y;
        float climbBottomY;
        float cachedOrbitSign = 1f;
        float climbFacingSign = 1f;
        bool wasJumpHeld;
        bool wasDownHeld;

        /// <summary>
        /// The direction away from the pole on the fox's side of it: the "floor normal" while climbing,
        /// which <see cref="ClimbCameraUp"/> turns the camera's up toward. Only meaningful while Climbing.
        /// </summary>
        public Vector3 SurfaceUp => offsetDir;

        /// <summary>
        /// How far the body point is above the lowest point it can climb to, in metres: 0 at the bottom of
        /// the pole. <see cref="ClimbCameraUp"/> uses it to sweep the camera in as the player climbs.
        /// Only meaningful while Climbing.
        /// </summary>
        public float HeightAboveBottom => Mathf.Max(0f, y - climbBottomY);

        /// <summary>The pole being climbed or perched on, or null. <see cref="WireRider"/> reads it to find
        /// which wires leave the perch.</summary>
        public Collider CurrentPole => currentPole;

        /// <summary>Fired with the pole just leapt from. <see cref="WireRider"/> briefly ignores that
        /// pole's own wires, so a leap is a leap rather than an instant grab of the wire at your feet.</summary>
        public event System.Action<Collider> JumpedOffPerch;

        /// <summary>
        /// The body's centre in world space: the CharacterController capsule's centre, not the transform's
        /// origin. On the Fox the origin is its hind paws, 0.575 m behind the capsule, so grabs measured
        /// from the origin could never be made head-on. The climb positions this same point against the
        /// pole, so grab and climb agree on what "the body" is.
        /// </summary>
        Vector3 BodyPoint => transform.TransformPoint(characterController.center);

        PoleClimbParams Params => new PoleClimbParams(
            ClimbSpeed, ClimbSurfacePadding, ClimbOrbitSpeed, ClimbDemountBodyHeights,
            PerchYawSteps, PerchTopPadding, PerchTurnSpeed, PoleJumpOutSpeed, PoleJumpUpSpeed);

        void Awake()
        {
            modes = GetComponent<PlayerModes>();
            controller = GetComponent<ThirdPersonController>();
            characterController = GetComponent<CharacterController>();
            inputs = GetComponent<StarterAssetsInputs>();
            animator = GetComponent<Animator>();

            if (GroundLayer == 0)
                GroundLayer = LayerMask.GetMask("Ground");
            if (PoleLayer == 0)
                PoleLayer = LayerMask.GetMask("Climbable");
        }

        void Update()
        {
            bool jumpPressedEdge = inputs.jump && !wasJumpHeld;
            bool downPressedEdge = inputs.move.y < -0.5f && !wasDownHeld;
            wasJumpHeld = inputs.jump;
            wasDownHeld = inputs.move.y < -0.5f;

            // Dead during the respawn wait: hold still, and don't grab a pole on the way down.
            if (modes.IsRespawning)
                return;

            switch (modes.Current)
            {
                case PlayerMode.Walking:
                case PlayerMode.Airborne:
                    TryStartClimbFromGround();
                    break;

                case PlayerMode.Climbing:
                    TickClimbing(jumpPressedEdge);
                    break;

                case PlayerMode.Perched:
                    TickPerched(jumpPressedEdge, downPressedEdge);
                    break;
            }
        }

        // ---------------------------------------------------------------- grab

        void TryStartClimbFromGround()
        {
            if (!modes.Lockouts.CanGrabPole)
                return;

            if (inputs.move.y <= 0.1f) // "Hold W"
                return;

            if (!TryFindGrabbablePole(out Collider pole, out Vector3 dir))
                return;

            currentPole = pole;
            offsetDir = dir;
            y = BodyPoint.y; // y is the body point's height while climbing, so start it there, not at the paws
            climbBottomY = FindClimbBottomY(pole);
            cachedOrbitSign = PoleClimbMath.OrbitDirectionSign(CameraBasis.Right, offsetDir);
            climbFacingSign = 1f; // grabbing always happens moving up (Hold W), so start facing up

            controller.ResetVerticalVelocity();
            inputs.JumpInput(false); // cleared on every mode enter and exit

            modes.TryTransition(ModeTrigger.GrabPole);
            PlayerAnimation.EnterStillPose(animator);
        }

        /// <summary>
        /// The lowest height the body point may climb to on this pole, worked out once at grab (poles do not
        /// move). The ground is found straight down from the climber rather than from the pole's bottom,
        /// which may be buried. In the nose-up climb pose local forward is world up, so the root (the hind
        /// paws) sits the CharacterController's forward offset below the body point.
        /// </summary>
        float FindClimbBottomY(Collider pole)
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            bool groundFound = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 100f, GroundLayer,
                QueryTriggerInteraction.Ignore);
            return PoleClimbMath.ClimbBottomY(pole.bounds.min.y, groundFound, hit.point.y,
                Mathf.Abs(characterController.center.z));
        }

        bool TryFindGrabbablePole(out Collider pole, out Vector3 offsetToward)
        {
            pole = null;
            offsetToward = Vector3.zero;

            float bodyRadius = characterController.radius;
            Vector3 body = BodyPoint;
            Collider[] candidates = Physics.OverlapSphere(body, GrabBroadphaseRadius, PoleLayer, QueryTriggerInteraction.Ignore);

            float bestFlatDistance = float.MaxValue;
            PoleClimbParams p = Params;

            foreach (Collider candidate in candidates)
            {
                // The tag, not the object's name, is what makes something a climbable pole.
                if (!candidate.CompareTag("Pole"))
                    continue;

                Bounds bounds = candidate.bounds;
                Vector3 axisXZ = new Vector3(bounds.center.x, 0f, bounds.center.z);
                Vector3 flatOffset = new Vector3(body.x - axisXZ.x, 0f, body.z - axisXZ.z);
                float flatDistance = flatOffset.magnitude;

                float stick = PoleClimbMath.StickDistance(bounds.extents.x, bounds.extents.z, bodyRadius, in p);
                if (!PoleClimbMath.CanGrab(body.y, bounds.max.y, flatDistance, stick))
                    continue;

                if (flatDistance < bestFlatDistance)
                {
                    bestFlatDistance = flatDistance;
                    pole = candidate;
                    offsetToward = flatOffset.sqrMagnitude > 1e-6f ? flatOffset.normalized : Vector3.forward;
                }
            }

            return pole != null;
        }

        // ---------------------------------------------------------------- climbing

        void TickClimbing(bool jumpPressedEdge)
        {
            if (currentPole == null)
            {
                // Safety exit: the pole stopped being there.
                modes.Lockouts.LockClimb(ClimbReentryLockDuration);
                modes.TryTransition(ModeTrigger.PoleLost);
                return;
            }

            PoleClimbParams p = Params;
            float bodyRadius = characterController.radius;
            float bodyHalf = PoleClimbMath.BodyHalf(characterController.height);
            Bounds bounds = currentPole.bounds;
            float top = bounds.max.y;
            float bottom = climbBottomY;
            Vector3 axisXZ = new Vector3(bounds.center.x, 0f, bounds.center.z);

            if (jumpPressedEdge)
            {
                JumpOff(axisXZ, bodyRadius);
                return;
            }

            // The orbit sign is cached at grab time (and on each fresh grab or descent), not recomputed
            // every frame. Recomputed from the player's own orbiting offset against a still camera, the dot
            // product is guaranteed to cross zero partway round any full orbit and flip the direction, which
            // reads as the orbit getting stuck halfway. Caching per grab still keeps A/D camera-relative when
            // grabbing from the camera's far side.
            offsetDir = PoleClimbMath.RotateOffset(offsetDir, inputs.move.x * cachedOrbitSign, in p, Time.deltaTime);

            // If W and S are both held, S wins: a negative vertical input always reads as down, even if the
            // input system's own composite has already partly cancelled it.
            float verticalInput = inputs.move.y;
            climbFacingSign = PoleClimbMath.ClimbFacingSign(climbFacingSign, verticalInput);

            y = PoleClimbMath.ClampClimbY(y, verticalInput, in p, Time.deltaTime, bottom, top, bodyHalf);

            bool isMoving = Mathf.Abs(verticalInput) > 0.1f || Mathf.Abs(inputs.move.x) > 0.1f;
            PlayerAnimation.SetTravelling(animator, isMoving, ClimbMovingAnimSpeed);

            if (verticalInput > 0.1f && PoleClimbMath.ShouldPerch(y, top, bodyHalf))
            {
                // Upright, facing away from the pole - a well-defined starting yaw for the perch's own
                // smoothing (TickPerched reads transform.eulerAngles.y next frame; extracting yaw from the
                // climb pose's near-90-degree pitch would hit Unity's gimbal-lock case and return garbage).
                modes.Teleport(PoleClimbMath.PerchPosition(axisXZ, top, in p, bodyHalf),
                    Quaternion.Euler(0f, PoleClimbMath.FacingYawFromOffset(offsetDir), 0f));
                modes.TryTransition(ModeTrigger.ReachPoleTop);
                PlayerAnimation.EnterStillPose(animator);
                return;
            }

            if (verticalInput < -0.1f && PoleClimbMath.ShouldDemount(y, in p, bottom, bodyHalf))
            {
                Demount(axisXZ, bodyRadius);
                return;
            }

            float stick = PoleClimbMath.ClimbHugStickDistance(bounds.extents.x, bounds.extents.z, ClimbHugDistance);
            Quaternion climbRotation = PoleClimbMath.ClimbRotation(offsetDir, climbFacingSign);
            Vector3 bodyPoint = PoleClimbMath.ClimbPosition(axisXZ, offsetDir, stick, y);

            // The body point above is where the visible body should be, not the transform origin -
            // RootPositionForBodyPoint backs out CharacterController.center's rotated offset, so the fox
            // sits against the pole instead of leaving a gap.
            transform.rotation = climbRotation;
            transform.position = PoleClimbMath.RootPositionForBodyPoint(bodyPoint, climbRotation, characterController.center);
        }

        void Demount(Vector3 axisXZ, float bodyRadius)
        {
            Bounds bounds = currentPole.bounds;
            PoleClimbParams p = Params;
            float stick = PoleClimbMath.StickDistance(bounds.extents.x, bounds.extents.z, bodyRadius, in p);
            Vector3 stepOut = PoleClimbMath.DemountStepOutPoint(axisXZ, offsetDir, stick, bodyRadius);

            // From above the climb's floor, not the collider's bottom: a pole buried deeper than the 3 m
            // head-room would otherwise start this ray underground and miss the ground entirely.
            Vector3 rayOrigin = new Vector3(stepOut.x, climbBottomY + 3f, stepOut.z);
            Vector3 landing = stepOut;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, GroundLayer, QueryTriggerInteraction.Ignore))
                landing.y = hit.point.y + 0.05f;
            else
                landing.y = transform.position.y; // no ground found - stay put rather than falling forever

            modes.Lockouts.LockClimb(ClimbReentryLockDuration);
            currentPole = null;
            // Upright, facing the direction stepped out to - without this the fox keeps whatever pitch
            // the climb pose left it in (nose up or down the pole) until normal locomotion next rotates it.
            modes.Teleport(landing, Quaternion.Euler(0f, PoleClimbMath.FacingYawFromOffset(offsetDir), 0f));
            modes.TryTransition(ModeTrigger.ReachPoleBottom);
        }

        // ---------------------------------------------------------------- perch

        void TickPerched(bool jumpPressedEdge, bool downPressedEdge)
        {
            if (currentPole == null)
                return; // no PoleLost from a perch: with nothing under it, it simply holds where it is

            Bounds bounds = currentPole.bounds;
            Vector3 axisXZ = new Vector3(bounds.center.x, 0f, bounds.center.z);
            float top = bounds.max.y;
            float bodyHalf = PoleClimbMath.BodyHalf(characterController.height);
            PoleClimbParams p = Params;

            transform.position = PoleClimbMath.PerchPosition(axisXZ, top, in p, bodyHalf);

            float targetYaw = PoleClimbMath.PerchTargetYaw(CameraBasis.Yaw, in p);
            float newYaw = PoleClimbMath.SmoothYawTowards(transform.eulerAngles.y, targetYaw, PerchTurnSpeed, Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

            if (jumpPressedEdge)
            {
                PerchJump();
                return;
            }

            if (downPressedEdge)
            {
                // Climb back down the side currently faced, head-first.
                offsetDir = new Vector3(Mathf.Sin(newYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(newYaw * Mathf.Deg2Rad));
                y = top - 0.85f * bodyHalf;
                cachedOrbitSign = PoleClimbMath.OrbitDirectionSign(CameraBasis.Right, offsetDir);
                climbFacingSign = -1f;
                modes.TryTransition(ModeTrigger.DescendFromPerch);
                PlayerAnimation.EnterStillPose(animator);
            }

            // A fresh W press mounts a wire - WireRider owns that, since it knows the wires.
        }

        /// <summary>
        /// Rode a wire into this pole: perch on top of it, keeping the riding yaw. From here the perch's own
        /// choices apply - Space leaps, S climbs down, and a fresh W takes a wire. Returns false if the mode
        /// table refused, in which case nothing changed.
        /// </summary>
        public bool PerchOn(Collider pole)
        {
            if (pole == null || !modes.TryTransition(ModeTrigger.PerchFromWire))
                return false;

            Bounds bounds = pole.bounds;
            Vector3 axisXZ = new Vector3(bounds.center.x, 0f, bounds.center.z);
            float yaw = transform.eulerAngles.y; // riding keeps the body upright, so this is a clean yaw

            currentPole = pole;
            offsetDir = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            modes.Teleport(
                PoleClimbMath.PerchPosition(axisXZ, bounds.max.y, Params, PoleClimbMath.BodyHalf(characterController.height)),
                Quaternion.Euler(0f, yaw, 0f));
            climbBottomY = FindClimbBottomY(pole); // needed if the player climbs down from here
            inputs.JumpInput(false);
            PlayerAnimation.EnterStillPose(animator);
            return true;
        }

        /// <summary>
        /// The perch leap. It launches from the perch itself, toward the facing, rather than from the side
        /// of the pole last climbed, which could throw the player backwards when it faced the other way.
        /// </summary>
        void PerchJump()
        {
            Collider pole = currentPole;
            Vector3 velocity = PoleClimbMath.PerchJumpVelocity(transform.forward, PerchJumpOutSpeed, PerchJumpUpSpeed);

            modes.Lockouts.LockClimb(ClimbReentryLockDuration);
            currentPole = null;
            inputs.JumpInput(false);

            JumpedOffPerch?.Invoke(pole);
            modes.Launch(velocity, PlayerModes.LaunchKind.PoleJump);
        }

        // ---------------------------------------------------------------- jump-off

        void JumpOff(Vector3 axisXZ, float bodyRadius)
        {
            Bounds bounds = currentPole.bounds;
            PoleClimbParams p = Params;
            Vector3 outward = PoleClimbMath.ResolveOutward(offsetDir);
            float stick = PoleClimbMath.StickDistance(bounds.extents.x, bounds.extents.z, bodyRadius, in p);

            // Upright rather than whatever pitch the climb pose left it in - PlayerModes.TickLaunch never
            // touches rotation, so without this the fox would fly the whole arc pitched sideways.
            modes.Teleport(PoleClimbMath.JumpOffPosition(axisXZ, outward, stick, bodyRadius, y),
                Quaternion.Euler(0f, PoleClimbMath.FacingYawFromOffset(outward), 0f));
            Vector3 velocity = PoleClimbMath.JumpOffVelocity(CameraBasis.PlanarForward, outward, in p);

            modes.Lockouts.LockClimb(ClimbReentryLockDuration);
            currentPole = null;
            inputs.JumpInput(false); // cleared on every mode enter and exit

            modes.Launch(velocity, PlayerModes.LaunchKind.PoleJump);
        }
    }
}
