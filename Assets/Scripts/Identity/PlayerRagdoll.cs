using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.Animations;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Hands the body to ragdoll physics - flung by a hazard slingshot, or gone limp - waits for
    /// <see cref="RagdollRecovery"/> to say the flight is over, plants the player where it landed, and holds
    /// a short stun before returning control. The recovery timing rules live in <see cref="RagdollRecovery"/>;
    /// this MonoBehaviour is the scene wiring.
    /// </summary>
    [RequireComponent(typeof(PlayerModes))]
    [RequireComponent(typeof(Slingshot))]
    [RequireComponent(typeof(ThirdPersonController))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StarterAssetsInputs))]
    [RequireComponent(typeof(VelocityTracker))]
    public class PlayerRagdoll : MonoBehaviour
    {
        /// <summary>The plant sits this far above the ground hit; <see cref="Recover"/> then settles the capsule onto it.</summary>
        const float PlantClearance = 0.2f;

        [Header("Rig")]
        [Tooltip("Every ragdoll Rigidbody. Auto-filled via GetComponentsInChildren if left empty.")]
        public Rigidbody[] Bodies;

        [Tooltip("The torso/root body - used for the tumble spin, ground-contact sampling, and the plant raycast origin.")]
        public Rigidbody TorsoBody;

        [Header("Camera: follows the ragdoll's position, never its rotation")]
        [Tooltip("The bone whose position the camera tracks while ragdolled. Its rotation is ignored.")]
        public Transform HipsBone;

        [Tooltip("The camera's usual follow target (PlayerCameraRoot). It is moved to the hips while " +
                 "ragdolled and put back afterwards; the mouse keeps orbiting it the whole time.")]
        public Transform NormalCameraTarget;

        [Header("Bones outside the ragdoll chain")]
        [Tooltip("Parent Constraints on bones the animation normally places but that are not children of a " +
                 "ragdoll body - the Fox's four IK foot bones. Switched on only while ragdolled, because an " +
                 "active constraint would override the walk animation's feet.")]
        public ParentConstraint[] FollowWhileRagdolled = new ParentConstraint[0];

        [Header("Air steering")]
        [Tooltip("Steering at full stick during a RagdollFlight, in m/s² (AirSteer). Gentler than Launched's, " +
                 "which lives on PlayerModes: an inflicted flight only lets you aim the landing.")]
        public float RagdollFlightSteerStrength = 6f;

        [Header("Launch jolt (hazard flights only)")]
        [Tooltip("How hard each limb is jolted at launch, as a fraction of the launch speed: 0.15 on a 30 m/s " +
                 "fling is up to 4.5 m/s per limb, in a random direction. 0 turns limb jolts off.")]
        public float LaunchLimbJolt = 0.15f;

        [Tooltip("Random extra torso spin at launch, in rad/s, on top of the forward tumble. Keep it modest: " +
                 "Rigidbody.maxAngularVelocity is 50 rad/s by default, far too high to stop a propeller on its own.")]
        public float LaunchSpinJolt = 3f;

        [Header("Recovery tuning")]
        public float LaunchAngularSpeed = 1.35f;
        public float RecoverMinTime = 0.35f;
        public float RecoverMaxTime = 10f;
        public float MinGroundContactTime = 1.4f;
        public float GroundContactGiveUpTime = 2.65f;
        public float RecoverMaxSpeed = 16f;
        public float GroundContactHeight = 1.25f;
        public float ContactResetMargin = 0.625f;
        public float MinWorldY = -80f;
        public float MaxWorldY = 2000f;
        public float PostRagdollStunDuration = 1.25f;

        [Tooltip("What counts as walkable ground for the contact check and the plant raycast.")]
        public LayerMask GroundLayer;

        PlayerModes modes;
        Slingshot slingshot;
        CharacterController characterController;
        Animator animator;
        ThirdPersonController tpc;
        StarterAssetsInputs inputs;
        WireRider wireRider;
        VelocityTracker velocityTracker;

        Vector3 cameraTargetRestLocal;
        float cameraHeightAboveHips;

        float elapsed;
        float groundContactSeconds;
        float stunTimer;

        RagdollRecoverySettings Settings => new RagdollRecoverySettings(
            RecoverMinTime, RecoverMaxTime, MinGroundContactTime, GroundContactGiveUpTime,
            RecoverMaxSpeed, GroundContactHeight, ContactResetMargin, MinWorldY, MaxWorldY);

        void Awake()
        {
            modes = GetComponent<PlayerModes>();
            slingshot = GetComponent<Slingshot>();
            characterController = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
            tpc = GetComponent<ThirdPersonController>();
            inputs = GetComponent<StarterAssetsInputs>();
            wireRider = GetComponent<WireRider>();
            velocityTracker = GetComponent<VelocityTracker>();

            if (Bodies == null || Bodies.Length == 0)
                Bodies = GetComponentsInChildren<Rigidbody>(true);

            // Fail loudly: without these, a ragdoll launch hangs the root mid-air until the recovery
            // timeout, and the only other sign is a warning afterwards.
            if (Bodies.Length == 0)
                Debug.LogError("PlayerRagdoll: no ragdoll Rigidbodies under this object - a ragdoll launch " +
                               "will hang the player mid-air until the recovery timeout. Add the ragdoll " +
                               "rig or remove this component.", this);
            if (TorsoBody == null)
                Debug.LogError("PlayerRagdoll: TorsoBody is not assigned - ground contact and the plant point " +
                               "will use the frozen root, so every flight times out and plants under the " +
                               "launch point.", this);
            if (HipsBone == null || NormalCameraTarget == null)
                Debug.LogWarning("PlayerRagdoll: HipsBone and NormalCameraTarget are not both " +
                                 "assigned - the camera will not follow the ragdoll.", this);

            // Enforced here rather than trusted to the Editor: one body left non-kinematic with gravity on
            // falls from the first frame.
            foreach (Rigidbody body in Bodies)
            {
                if (body == null)
                    continue;

                body.useGravity = false; // applied per body in FixedUpdate instead
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            IgnoreSelfCollisions();

            // Measured from the model rather than typed in, so a different body keeps the same framing.
            if (HipsBone != null && NormalCameraTarget != null)
            {
                cameraTargetRestLocal = NormalCameraTarget.localPosition;
                cameraHeightAboveHips = NormalCameraTarget.position.y - HipsBone.position.y;
            }

            // Enforced for the same reason as the bodies above: one left ticked in the Editor would pin the
            // feet to the legs during normal animation.
            SetFollowConstraints(false);

            if (GroundLayer == 0)
                GroundLayer = LayerMask.GetMask("Ground", "Props");

            slingshot.Fired += OnFired;
            modes.Respawned += OnRespawned;
        }

        /// <summary>
        /// Standard ragdoll practice: a body's own parts never collide with each other. A lower leg pinned
        /// between the ground, another body part and a tight joint limit has no legal position, so the solver
        /// shoves it back and forth every step - limbs flailing on a body lying still, and worse the harder
        /// the solver tries. Done per pair rather than with the layer matrix, so two players' ragdolls still
        /// pile into each other. Not saved with the scene and lost if a collider is deactivated, which is why
        /// it runs here at startup rather than being set up once in the Editor.
        /// </summary>
        void IgnoreSelfCollisions()
        {
            var colliders = new List<Collider>();
            foreach (Rigidbody body in Bodies)
            {
                if (body != null)
                    colliders.AddRange(body.GetComponents<Collider>());
            }

            for (int i = 0; i < colliders.Count; i++)
                for (int j = i + 1; j < colliders.Count; j++)
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
        }

        void OnDestroy()
        {
            if (slingshot != null)
                slingshot.Fired -= OnFired;
            if (modes != null)
                modes.Respawned -= OnRespawned;
        }

        /// <summary>
        /// Gravity is applied per body, because useGravity is off (Awake) and the global Physics.gravity is
        /// left alone for everything else in the scene. The value is the controller's own Gravity, so the
        /// flight arc and walking/jumping share one tuning number.
        /// </summary>
        void FixedUpdate()
        {
            if (!ModeRules.IsRagdolled(modes.Current))
                return;

            Vector3 acceleration = new Vector3(0f, tpc.Gravity, 0f);

            // Only the guided flight steers; a plain Ragdoll is a short knock with no guidance, and the dead
            // get no input at all. Applied to every body like gravity, so the whole fox drifts rather than
            // the torso dragging the limbs.
            if (modes.Current == PlayerMode.RagdollFlight && !modes.IsRespawning)
                acceleration += AirSteer.Acceleration(inputs.move, CameraBasis.Forward, CameraBasis.Right, RagdollFlightSteerStrength);

            foreach (Rigidbody body in Bodies)
            {
                if (body != null && !body.isKinematic)
                    body.AddForce(acceleration, ForceMode.Acceleration);
            }
        }

        void Update()
        {
            switch (modes.Current)
            {
                case PlayerMode.RagdollFlight:
                case PlayerMode.Ragdoll:
                    FollowHipsWithCamera();
                    TickFlight();
                    break;

                case PlayerMode.Stunned:
                    TickStun();
                    break;
            }
        }

        // ---------------------------------------------------------------- enter

        /// <summary>
        /// A hazard slingshot release. By the time this fires, <c>Slingshot</c> has already moved the mode
        /// to <c>RagdollFlight</c>, so the Animator/TPC/CharacterController are already off via
        /// <c>ModeRules</c> - this only does the ragdoll-specific half.
        /// </summary>
        void OnFired(Vector3 velocity)
        {
            EnterRagdoll(velocity, tumble: true);
        }

        /// <summary>
        /// Go limp from any mode: ragdoll physics with only the momentum the player already had and no
        /// launch or spin added - running forward flops forward, and on a wire gravity takes it straight down
        /// through the line (the ragdoll layer ignores wires). The dev controls' Z key calls it.
        /// </summary>
        public void GoLimp()
        {
            if (modes.IsRespawning)
                return;

            PlayerMode previous = modes.Current;
            // Read before the mode change: momentum is measured on the root, which stops moving once the
            // bodies take over.
            Vector3 velocity = velocityTracker.Velocity;

            if (!modes.TryTransition(ModeTrigger.GoLimp))
                return;

            // Already simulating (a guided flight going limp): the bodies keep their own motion, and the
            // root's measured velocity is near zero because the root is frozen during a flight.
            if (ModeRules.IsRagdolled(previous))
                return;

            if (wireRider != null && (previous == PlayerMode.WireRiding || previous == PlayerMode.SlingshotCharging))
            {
                wireRider.FreezeTravel = false;
                wireRider.ClearWire();
            }

            EnterRagdoll(velocity, tumble: false);
        }

        void EnterRagdoll(Vector3 velocity, bool tumble)
        {
            if (Bodies.Length == 0 || TorsoBody == null)
                Debug.LogError("PlayerRagdoll: launched with no ragdoll rig or no TorsoBody - this flight will " +
                               "hang until it times out (see the Awake error).", this);

            elapsed = 0f;
            groundContactSeconds = 0f;

            if (animator != null)
                animator.enabled = false;

            foreach (Rigidbody body in Bodies)
            {
                if (body == null)
                    continue;

                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.linearVelocity = velocity; // every body, not just the torso, so the limbs don't lag behind
            }

            SetFollowConstraints(true);

            if (tumble && TorsoBody != null)
            {
                // A gentle forward tumble: spin the torso about the horizontal axis perpendicular to the
                // flight direction; the joints carry the rest of the body with it.
                Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
                Vector3 tumbleAxis = flatVelocity.sqrMagnitude > 1e-6f
                    ? Vector3.Cross(Vector3.up, flatVelocity.normalized)
                    : Vector3.right;
                TorsoBody.angularVelocity = tumbleAxis * LaunchAngularSpeed;

                ApplyLaunchJolt(velocity.magnitude);
            }
        }

        /// <summary>
        /// A hazard fling is a surprise, so the body does not brace against it: the launch's energy goes
        /// through it unevenly, as spin and limb jolts, instead of the whole fox flying as one rigid lump.
        /// Once, at launch only; nothing is added during the flight. Going limp never gets this, and a
        /// purposeful launch never ragdolls at all.
        /// </summary>
        void ApplyLaunchJolt(float launchSpeed)
        {
            foreach (Rigidbody body in Bodies)
            {
                if (body == null)
                    continue;

                if (body == TorsoBody)
                    body.AddTorque(LaunchSpinJolt * Random.onUnitSphere, ForceMode.VelocityChange);
                else
                    body.AddForce(launchSpeed * LaunchLimbJolt * Random.insideUnitSphere, ForceMode.VelocityChange);
            }
        }

        // ---------------------------------------------------------------- camera

        /// <summary>
        /// The camera follows the flight's position, not the ragdoll's rotation. Following a bone directly
        /// would hand the camera every twist of the tumbling body; moving the usual target instead leaves the
        /// orbit on the mouse, like an observer. Runs in Update because interpolated bodies have already
        /// placed the bones by then, and the camera reads its target later, in LateUpdate.
        /// </summary>
        void FollowHipsWithCamera()
        {
            if (HipsBone != null && NormalCameraTarget != null)
                NormalCameraTarget.position = HipsBone.position + Vector3.up * cameraHeightAboveHips;
        }

        void RestoreCameraTarget()
        {
            if (NormalCameraTarget != null)
                NormalCameraTarget.localPosition = cameraTargetRestLocal;
        }

        /// <summary>Hands every body back to the animation, stopped. Used on recovery and on respawn.</summary>
        void FreezeBodies()
        {
            foreach (Rigidbody body in Bodies)
            {
                if (body == null)
                    continue;

                // Velocities first: Unity refuses to set them on a body that is already kinematic and logs a
                // warning per body, which on a respawn from normal walking is a warning for every body.
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }
        }

        // ---------------------------------------------------------------- bones outside the chain

        /// <summary>
        /// The Fox's feet are not children of its legs: each foot bone hangs off an IK control bone beside
        /// <c>Body</c>, and the animations move them to match the legs every frame. With the Animator off
        /// nothing would move them, so they would stay behind and stretch the skin. A Parent Constraint holds
        /// each one to its lower leg instead, but only while ragdolled.
        /// </summary>
        void SetFollowConstraints(bool active)
        {
            foreach (ParentConstraint constraint in FollowWhileRagdolled)
            {
                if (constraint != null)
                    constraint.constraintActive = active;
            }
        }

        // ---------------------------------------------------------------- flight

        void TickFlight()
        {
            elapsed += Time.deltaTime;

            Vector3 torsoPosition = TorsoBody != null ? TorsoBody.position : transform.position;
            float distanceToGround = MeasureDistanceToGround(torsoPosition);

            RagdollRecoverySettings s = Settings;
            groundContactSeconds = RagdollRecovery.UpdateGroundContactTime(in s, groundContactSeconds, distanceToGround, Time.deltaTime);

            bool forced = RagdollRecovery.IsOutOfBounds(in s, torsoPosition);
            float torsoSpeed = TorsoBody != null ? TorsoBody.linearVelocity.magnitude : 0f;

            RecoverReason reason = RagdollRecovery.Evaluate(in s, elapsed, groundContactSeconds, torsoSpeed, forced);
            if (reason != RecoverReason.None)
                Recover(reason);
        }

        float MeasureDistanceToGround(Vector3 torsoPosition)
        {
            Vector3 origin = torsoPosition + Vector3.up * 0.6f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, GroundLayer, QueryTriggerInteraction.Ignore)
                && RagdollRecovery.IsWalkableGround(hit.normal))
            {
                return Mathf.Max(0f, torsoPosition.y - hit.point.y);
            }

            return float.PositiveInfinity;
        }

        // ---------------------------------------------------------------- recover / plant

        void Recover(RecoverReason reason)
        {
            Vector3 torsoPosition = TorsoBody != null ? TorsoBody.position : transform.position;
            Vector3 recoveryVelocity = TorsoBody != null ? TorsoBody.linearVelocity : Vector3.zero;

            // Stand up facing the way the body was still moving, falling back to the torso's own forward if
            // it landed too slowly for velocity to mean anything, and finally to whatever the root faces.
            float facingYaw = FacingYawFromRecovery(recoveryVelocity, torsoPosition);

            Vector3 plantPoint = ComputePlantPoint(torsoPosition);

            FreezeBodies();

            // The frozen root jumps from where the flight began to where the body landed: a teleport.
            modes.Teleport(plantPoint, Quaternion.Euler(0f, facingYaw, 0f));

            if (animator != null)
            {
                animator.enabled = true;
                PlayerAnimation.PlayIdle(animator);
            }

            RestoreCameraTarget();
            SetFollowConstraints(false);

            stunTimer = PostRagdollStunDuration;

            // A wire lock after any ragdoll, running alongside the stun, so a little of it is left once
            // control returns. The release-time lock in Slingshot always runs out mid-flight, because
            // recovery needs ground contact first. LockAllWires keeps the longer of two locks, so this can't
            // shorten anything.
            if (wireRider != null)
                modes.Lockouts.LockAllWires(wireRider.SlingshotWireLockDuration);

            inputs.JumpInput(false); // cleared on every mode exit, so a Space mashed mid-flight can't fire on stand-up

            // TryTransition re-enables the CharacterController for Stunned via ApplyModeEffects, so it runs
            // last, after the teleport.
            modes.TryTransition(ModeTrigger.Recover);

            // Nothing calls Move while Stunned - the base controller is gated and the ballistic mover only
            // runs in Launched - so settle the capsule onto the ground here, or it hovers at the plant
            // clearance for the whole stun. Kept short so a plant over a ledge isn't dragged down a long way.
            if (characterController.enabled)
                characterController.Move(Vector3.down * (PlantClearance * 2f));

            // Only an abnormal ending is worth a console line; normal landings are silent.
            if (reason == RecoverReason.Forced || reason == RecoverReason.TimedOut)
                Debug.LogWarning($"PlayerRagdoll: flight ended abnormally ({reason}).", this);
        }

        float FacingYawFromRecovery(Vector3 recoveryVelocity, Vector3 torsoPosition)
        {
            Vector3 flat = new Vector3(recoveryVelocity.x, 0f, recoveryVelocity.z);
            if (flat.sqrMagnitude > 0.01f)
                return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

            if (TorsoBody != null)
            {
                Vector3 torsoForward = new Vector3(TorsoBody.transform.forward.x, 0f, TorsoBody.transform.forward.z);
                if (torsoForward.sqrMagnitude > 1e-6f)
                    return Mathf.Atan2(torsoForward.x, torsoForward.z) * Mathf.Rad2Deg;
            }

            return transform.eulerAngles.y;
        }

        /// <summary>
        /// Casts from just above the torso first, which lands on the surface it actually rests on, and only
        /// falls back to a cast from 15 m up - which on its own can plant the player on top of an overhang
        /// above where it came to rest - if the short cast finds nothing.
        /// </summary>
        Vector3 ComputePlantPoint(Vector3 torsoPosition)
        {
            if (TryRaycastGround(torsoPosition + Vector3.up * 1f, out Vector3 nearPlant))
                return nearPlant;

            if (TryRaycastGround(torsoPosition + Vector3.up * 15f, out Vector3 farPlant))
                return farPlant;

            // Nothing walkable found: stand up where the torso is rather than failing.
            return torsoPosition;
        }

        bool TryRaycastGround(Vector3 origin, out Vector3 plantPoint)
        {
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, GroundLayer, QueryTriggerInteraction.Ignore)
                && RagdollRecovery.IsWalkableGround(hit.normal))
            {
                plantPoint = hit.point + Vector3.up * PlantClearance;
                return true;
            }

            plantPoint = default;
            return false;
        }

        // ---------------------------------------------------------------- stun

        void TickStun()
        {
            stunTimer -= Time.deltaTime;
            if (stunTimer <= 0f)
            {
                inputs.JumpInput(false); // a Space pressed during the stun must not jump on the first free frame
                modes.TryTransition(ModeTrigger.StunExpired);
            }
        }

        // ---------------------------------------------------------------- respawn

        /// <summary>
        /// Respawn works mid-flight too. <c>PlayerModes</c> teleports the root and re-enables base
        /// locomotion, but knows nothing about ragdoll bodies - without this they would keep simulating
        /// wherever they were, disconnected from the teleported root, like a second floppy body left behind.
        /// </summary>
        void OnRespawned()
        {
            FreezeBodies();

            // PlayerModes plays Idle before raising Respawned, which does nothing while this script still
            // has the Animator disabled after an interrupted flight - so play it again once re-enabled.
            if (animator != null)
            {
                animator.enabled = true;
                PlayerAnimation.PlayIdle(animator);
            }

            RestoreCameraTarget();
            SetFollowConstraints(false);

            stunTimer = 0f;
        }
    }
}
