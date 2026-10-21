using System;
using StarterAssets;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Pull away from the wire opposite the camera, then release to fling. The cause of the pull decides
    /// the flight: a hazard pull ragdolls into <c>RagdollFlight</c>, a purposeful one launches under control
    /// into <c>Launched</c>, and a release too weak to launch snaps back onto the wire. The stretch and
    /// launch maths live in <see cref="SlingshotMath"/> and <see cref="SlingshotConfig"/>; this
    /// MonoBehaviour is the scene wiring.
    /// </summary>
    /// <remarks>
    /// The pull bends the wire around the body through the wire's <see cref="BendableWire"/>, if it has one,
    /// and lets it spring back on any exit from charging. The pull itself is measured from the wire's rest
    /// shape, so nothing about the launch depends on the bend.
    /// </remarks>
    [RequireComponent(typeof(PlayerModes))]
    [RequireComponent(typeof(WireRider))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StarterAssetsInputs))]
    public class Slingshot : MonoBehaviour
    {
        [Tooltip("Stretch/launch tuning. Create one via the asset menu: Movement Sandbox > Slingshot Config.")]
        public SlingshotConfig Config;

        [Tooltip("What the pull's SphereCast clamps against, so a big pull can't drag the body through geometry.")]
        public LayerMask ObstructionLayer;

        PlayerModes modes;
        WireRider wireRider;
        CharacterController characterController;
        StarterAssetsInputs inputs;
        Animator animator;

        InputAction hazardAction;
        InputAction purposefulAction;
        InputAction aimLockAction;

        // Which key started this charge, so only that key's release fires it and the flight matches its cause.
        InputAction chargeAction;
        LaunchCause chargeCause;

        float heldSeconds;
        float pull;
        Vector3 pullDir;
        bool aimLocked;
        Vector3 lockedPullDir;
        Quaternion frozenFacing;
        bool wasJumpHeld;

        // The wire being pulled, if it can bend, and the stretch of it between the poles either side.
        BendableWire bendable;
        float bendSpanStart;
        float bendSpanEnd;

        /// <summary>0..1 charge fraction, ready for a UI meter.</summary>
        public float ChargePercent => Config != null && Config.MaxStretch > 0f ? Mathf.Clamp01(pull / Config.MaxStretch) : 0f;

        /// <summary>
        /// Fired on a <b>hazard</b> release strong enough to launch, in place of routing through
        /// <see cref="PlayerModes.Launch"/> (that method always targets <c>Launched</c>, not
        /// <c>RagdollFlight</c>). <see cref="PlayerRagdoll"/> subscribes and applies the velocity to every
        /// ragdoll body. A purposeful release does not fire it; it goes through <see cref="WireRider.Detach"/>
        /// into <c>Launched</c> instead.
        /// </summary>
        public event Action<Vector3> Fired;

        void Awake()
        {
            modes = GetComponent<PlayerModes>();
            wireRider = GetComponent<WireRider>();
            characterController = GetComponent<CharacterController>();
            inputs = GetComponent<StarterAssetsInputs>();
            animator = GetComponent<Animator>();

            if (ObstructionLayer == 0)
                ObstructionLayer = LayerMask.GetMask("Ground", "Props", "Climbable");

            // Without a config the WireRiding case below never starts a charge, which would look like an
            // input bug rather than missing setup.
            if (Config == null)
                Debug.LogError("Slingshot needs a SlingshotConfig or X and C do nothing on a wire. Create one with " +
                               "Assets > Create > Movement Sandbox > Slingshot Config, then drag it into " +
                               "Slingshot's Config field.", this);

            // Defined in code, beside the only script that reads them. X and C share the same charge and
            // maths, one key per cause, so both flights can be compared from the same wire.
            hazardAction = new InputAction("SlingshotHazard", binding: "<Keyboard>/x");
            purposefulAction = new InputAction("SlingshotPurposeful", binding: "<Keyboard>/c");
            aimLockAction = new InputAction("AimLock", binding: "<Keyboard>/q");
            hazardAction.Enable();
            purposefulAction.Enable();
            aimLockAction.Enable();

            modes.Respawned += OnRespawned;
            modes.ModeChanged += OnModeChanged;
        }

        void OnDestroy()
        {
            hazardAction?.Dispose();
            purposefulAction?.Dispose();
            aimLockAction?.Dispose();

            if (modes != null)
            {
                modes.Respawned -= OnRespawned;
                modes.ModeChanged -= OnModeChanged;
            }
        }

        void Update()
        {
            bool jumpPressedEdge = inputs.jump && !wasJumpHeld;
            wasJumpHeld = inputs.jump;

            // Dead during the respawn wait: no pulling or releasing.
            if (modes.IsRespawning)
                return;

            switch (modes.Current)
            {
                case PlayerMode.WireRiding:
                    // Holding the key is enough, no fresh press needed: once charging starts this case stops
                    // running until a weak release brings it back.
                    if (Config == null || wireRider.CurrentWire == null)
                        break;
                    if (hazardAction.IsPressed())
                        StartCharge(hazardAction, LaunchCause.Hazard);
                    else if (purposefulAction.IsPressed())
                        StartCharge(purposefulAction, LaunchCause.Purposeful);
                    break;

                case PlayerMode.SlingshotCharging:
                    TickCharging(jumpPressedEdge);
                    break;
            }
        }

        // ---------------------------------------------------------------- charge

        void StartCharge(InputAction action, LaunchCause cause)
        {
            if (!modes.TryTransition(ModeTrigger.StartCharge))
                return;

            chargeAction = action;
            chargeCause = cause;
            heldSeconds = 0f;
            pull = 0f;
            aimLocked = false;
            frozenFacing = transform.rotation; // travel is frozen while charging, so the ride facing holds
            wireRider.FreezeTravel = true;

            bendable = wireRider.CurrentWire.GetComponent<BendableWire>();
            wireRider.TryGetBendSpan(out bendSpanStart, out bendSpanEnd);

            PlayerAnimation.EnterStillPose(animator);
        }

        void TickCharging(bool jumpPressedEdge)
        {
            // Space is checked before the pull key: a cancel beats a release. ModeRules agrees - Jump wins
            // over the release triggers from SlingshotCharging.
            if (jumpPressedEdge)
            {
                CancelWithDrop();
                return;
            }

            if (aimLockAction.WasPressedThisFrame())
            {
                aimLocked = !aimLocked;
                if (aimLocked)
                    lockedPullDir = pullDir;
            }

            pullDir = aimLocked ? lockedPullDir : -CameraBasis.Forward;

            heldSeconds += Time.deltaTime;
            pull = Config.StretchAfterHold(heldSeconds);

            PositionPulledBody();

            if (!chargeAction.IsPressed())
                ReleaseCharge();
        }

        /// <summary>
        /// Places the body <c>pull</c> metres from the ride point along <c>pullDir</c>, clamped by a
        /// SphereCast so a long pull cannot drag it through geometry, and bends the wire with it. Measured
        /// from the wire's rest shape, since the bend this causes must not move its own starting point.
        /// </summary>
        void PositionPulledBody()
        {
            Vector3 ridePoint = wireRider.RestRidePoint;
            Vector3 desired = ridePoint + pullDir * pull;

            if (pull > 0f)
            {
                float radius = Mathf.Max(0.05f, characterController.radius);
                if (Physics.SphereCast(ridePoint, radius, pullDir, out RaycastHit hit, pull, ObstructionLayer, QueryTriggerInteraction.Ignore))
                    desired = ridePoint + pullDir * Mathf.Max(0f, hit.distance);
            }

            transform.position = desired;
            transform.rotation = frozenFacing;

            BendWire(desired - ridePoint);
        }

        /// <summary>
        /// Presses the wire out by <paramref name="displacement"/> around the body. The contact is as wide as
        /// the body's collider along the wire and centred on the collider, not the root, so the bend follows
        /// whatever body is pulling.
        /// </summary>
        void BendWire(Vector3 displacement)
        {
            if (bendable == null)
                return;

            float3 rawTangent = wireRider.CurrentWire.EvaluateTangent(wireRider.T);
            Vector3 along = new Vector3(rawTangent.x, rawTangent.y, rawTangent.z).normalized;
            Vector3 scale = transform.lossyScale;

            Vector3 halfExtents = Vector3.Scale(
                new Vector3(characterController.radius, characterController.height * 0.5f, characterController.radius), scale);
            float halfWidth = WireBendMath.HalfExtentAlong(halfExtents, transform.rotation, along);
            float contactShift = Vector3.Dot(transform.rotation * Vector3.Scale(characterController.center, scale), along);

            bendable.Hold(wireRider.T, contactShift, halfWidth, bendSpanStart, bendSpanEnd, displacement);
        }

        /// <summary>
        /// Every way out of a pull - either release, a cancel, going limp, a respawn - leaves this mode, so
        /// letting the wire spring back here covers them all.
        /// </summary>
        void OnModeChanged(PlayerMode previous, PlayerMode next)
        {
            if (previous != PlayerMode.SlingshotCharging || bendable == null)
                return;

            bendable.Release();
            bendable = null;
        }

        void ReleaseCharge()
        {
            if (!Config.CanLaunch(pull))
            {
                // Weak release: snap back onto the wire. No detach - WireRider's currentWire/t/facingSign
                // were never touched, so riding resumes exactly where charging began.
                wireRider.FreezeTravel = false;
                modes.TryTransition(ModeTrigger.ReleaseChargeWeak);
                return;
            }

            Vector3 stretchVector = pullDir * pull;
            Vector3 velocity = Config.ComputeLaunchVelocity(stretchVector);

            if (ModeRules.FlightFor(chargeCause) == PlayerMode.Launched)
            {
                // Purposeful: the player stays in control, so the flight can catch a pole or another wire.
                // That is why this blocks only the wire just left, through the same Detach a Space-drop uses,
                // rather than locking every wire as the hazard path does.
                wireRider.FreezeTravel = false;
                wireRider.Detach(velocity, requireGroundBeforeWire: false, PlayerModes.LaunchKind.Slingshot);
                return;
            }

            // Hazard: lock every wire (Lockouts.LockAllWires exists so an immediate re-grab can't eat the
            // launch), not just the one just left - which is why it does not go through WireRider.Detach.
            modes.Lockouts.LockAllWires(wireRider.SlingshotWireLockDuration);
            modes.Lockouts.LockClimb(WireRider.ClimbLockAfterWireExit);
            wireRider.FreezeTravel = false;
            wireRider.ClearWire();

            modes.TryTransition(ModeTrigger.ReleaseChargeStrong);
            Fired?.Invoke(velocity);
        }

        void CancelWithDrop()
        {
            wireRider.FreezeTravel = false;
            // Same kick a normal Space-drop uses, plus no wire grab until grounded - the only wire exit
            // that keeps that rule.
            wireRider.DetachWithKick(requireGroundBeforeWire: true);
        }

        void OnRespawned()
        {
            heldSeconds = 0f;
            pull = 0f;
            aimLocked = false;
            wireRider.FreezeTravel = false;
        }
    }
}
