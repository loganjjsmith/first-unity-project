using System;
using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using MovementSandbox.Environment;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// The one mode owner: tracks the current <see cref="PlayerMode"/>, gates the base controller so
    /// exactly one system drives the body at a time, and owns respawning and teleporting. The pure
    /// mode-transition and lockout logic lives in <c>MovementSandbox.Mechanics</c> (<see cref="ModeRules"/>,
    /// <see cref="Lockouts"/>); this MonoBehaviour is the thin layer that wires it to the scene, the
    /// Starter Assets controller, and input.
    /// </summary>
    [RequireComponent(typeof(ThirdPersonController))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerModes : MonoBehaviour
    {
        public enum LaunchKind { PoleJump, WireDrop, WireEnd, Slingshot }

        [Tooltip("Gravity applied while Launched, before the base controller takes back over on landing.")]
        public float LaunchGravity = -80f;

        [Tooltip("Air steering at full stick while Launched, in m/s² (AirSteer). Stronger than a ragdoll " +
                 "flight's, because a chosen launch stays in control.")]
        public float LaunchedSteerStrength = 12f;

        [Header("Respawn")]
        [Tooltip("How long the Death clip plays for before the actual teleport - matches the clip's own " +
                 "length (1.07s) plus a little slack for the animator's blend into it.")]
        public float DeathAnimationDuration = 1.2f;

        [Tooltip("Extra pause after the death animation finishes, before the actual respawn.")]
        public float RespawnDelayAfterDeath = 1.5f;

        ThirdPersonController controller;
        CharacterController characterController;
        StarterAssetsInputs inputs;
        Animator animator;
        VelocityTracker velocityTracker;

        readonly Lockouts lockouts = new Lockouts();
        readonly GroundedDebounce groundDebounce = new GroundedDebounce();
        InputAction respawnAction;
        Vector3 launchVelocity;
        bool isRespawning;

        public PlayerMode Current { get; private set; } = PlayerMode.Walking;
        public Lockouts Lockouts => lockouts;

        public event Action<PlayerMode, PlayerMode> ModeChanged;
        public event Action<LaunchKind> Launched;
        public event Action Landed;
        public event Action Respawned;

        void Awake()
        {
            controller = GetComponent<ThirdPersonController>();
            characterController = GetComponent<CharacterController>();
            inputs = GetComponent<StarterAssetsInputs>();
            animator = GetComponent<Animator>();
            velocityTracker = GetComponent<VelocityTracker>();

            // Defined in code, beside the only script that reads it, like the slingshot and dev keys.
            respawnAction = new InputAction("Respawn", binding: "<Keyboard>/r");
            respawnAction.Enable();

            ApplyModeEffects();
        }

        void OnDestroy()
        {
            respawnAction?.Dispose();
        }

        void Update()
        {
            lockouts.Tick(Time.deltaTime);

            if (respawnAction.WasPressedThisFrame())
                Respawn();

            // Both keep running while "dead": gravity and landings are physics, not input.
            if (Current == PlayerMode.Launched)
                TickLaunch();
            else
                TrackBaseControllerGround();
        }

        /// <summary>
        /// True for the length of the death animation before a respawn. The identity scripts ignore player
        /// input while it is set, so the player holds still in every mode, not just Walking.
        /// </summary>
        public bool IsRespawning => isRespawning;

        /// <summary>
        /// Mirrors the base controller's own grounded state into Walking/Airborne, so a normal jump or
        /// walking off a pole top counts as airborne and the wire grab, which only runs in the air, sees it.
        /// Leaving the ground is debounced by the base controller's own <c>FallTimeout</c> (see
        /// <see cref="GroundedDebounce"/> for the downhill flicker it prevents); landing is immediate.
        /// </summary>
        void TrackBaseControllerGround()
        {
            if (Current != PlayerMode.Walking && Current != PlayerMode.Airborne)
            {
                // Grounded goes stale while another mode drives the body; don't carry a count across.
                groundDebounce.Reset();
                return;
            }

            bool leftGround = groundDebounce.Tick(controller.Grounded, Time.deltaTime, controller.FallTimeout);
            if (Current == PlayerMode.Walking && leftGround)
            {
                TryTransition(ModeTrigger.LeaveGround);
            }
            else if (Current == PlayerMode.Airborne && controller.Grounded)
            {
                lockouts.NotifyGrounded();
                Landed?.Invoke();
                TryTransition(ModeTrigger.Land);
            }
        }

        /// <summary>
        /// Asks for a mode change. Returns false and does nothing if the trigger does not apply in the
        /// current mode - <see cref="ModeRules.Next"/> returning null is normal, not an error.
        /// </summary>
        public bool TryTransition(ModeTrigger trigger)
        {
            PlayerMode? next = ModeRules.Next(Current, trigger);
            if (next == null)
                return false;

            SetMode(next.Value);
            return true;
        }

        void SetMode(PlayerMode next)
        {
            if (next == Current)
                return;

            PlayerMode previous = Current;
            Current = next;
            ApplyModeEffects();
            ModeChanged?.Invoke(previous, next);
        }

        void ApplyModeEffects()
        {
            controller.LocomotionEnabled = ModeRules.UsesBaseLocomotion(Current);
            characterController.enabled = ModeRules.UsesCharacterController(Current);
        }

        /// <summary>
        /// Hands the body to a ballistic mover until it lands. Every controlled launch - a pole kick or
        /// perch leap, a wire drop, a wire-end hop, or a purposeful slingshot - goes through here, which
        /// keeps input and mode concerns away from whatever fired it. A hazard slingshot goes to
        /// RagdollFlight through <see cref="PlayerRagdoll"/> instead and never comes here.
        /// </summary>
        /// <remarks>
        /// This sets Launched directly rather than asking <see cref="ModeRules.Next"/>, because every caller
        /// has already let go of its pole or wire and refusing would strand it. The table's Jump rows are the
        /// contract a launch is checked against, loudly, in development builds.
        /// </remarks>
        public void Launch(Vector3 velocity, LaunchKind kind)
        {
            Debug.Assert(ModeRules.Next(Current, ModeTrigger.Jump) == PlayerMode.Launched,
                $"PlayerModes.Launch from {Current}, which the mode table does not allow.", this);

            launchVelocity = velocity;
            inputs.JumpInput(false); // cleared on every mode exit so a held Space can't re-fire on landing
            SetMode(PlayerMode.Launched);
            Launched?.Invoke(kind);
        }

        void TickLaunch()
        {
            // Launched still uses the CharacterController for collision, just not the base controller's own
            // Move: two scripts calling Move in one frame would fight over the body.
            characterController.enabled = true;

            launchVelocity.y += LaunchGravity * Time.deltaTime;
            if (!isRespawning) // dead mid-flight: no input, physics only
                launchVelocity += AirSteer.Acceleration(inputs.move, CameraBasis.Forward, CameraBasis.Right, LaunchedSteerStrength) * Time.deltaTime;
            characterController.Move(launchVelocity * Time.deltaTime);

            if (characterController.isGrounded && launchVelocity.y <= 0f)
            {
                lockouts.NotifyGrounded();
                Landed?.Invoke();
                TryTransition(ModeTrigger.Land);
            }
        }

        /// <summary>
        /// Legal from every mode, including mid-flight and mid-stun. Plays the Death animation, pausing
        /// player input (not physics) for its length plus a short pause, then does the actual teleport - a
        /// deliberate bit of fun rather than an instant snap.
        /// </summary>
        public void Respawn()
        {
            if (isRespawning)
                return;

            RespawnPoint point = FindRespawnPoint();
            if (point == null)
            {
                Debug.LogWarning("PlayerModes.Respawn: no RespawnPoint in the scene.", this);
                return;
            }

            StartCoroutine(RespawnSequence(point));
        }

        /// <summary>
        /// Respawn without the death animation: an instant placement at <paramref name="point"/> with every
        /// mode and lock reset, and <see cref="Respawned"/> raised so the identity scripts drop whatever they
        /// were holding. Used by the playtest shortcuts; ignored mid-respawn so the two can't race.
        /// </summary>
        public void TeleportTo(RespawnPoint point)
        {
            if (isRespawning || point == null)
                return;

            DoRespawn(point);
        }

        IEnumerator RespawnSequence(RespawnPoint point)
        {
            isRespawning = true;

            // Pause player input in every mode, but not physics: the identity scripts check IsRespawning,
            // and the base controller keeps its gravity and grounding with its input masked, so a player
            // that dies mid-jump still falls and lands. DoRespawn resets it all.
            controller.InputEnabled = false;
            PlayerAnimation.PlayDeath(animator);

            yield return new WaitForSeconds(DeathAnimationDuration + RespawnDelayAfterDeath);

            // Cleared before the teleport, so DoRespawn's ApplyModeEffects hands walking back.
            isRespawning = false;
            DoRespawn(point);
        }

        /// <summary>
        /// Moves the player somewhere without it counting as movement. <b>Every</b> teleport goes through
        /// here - respawn, dev shortcuts, the ragdoll plant, the small snaps onto and off poles and wires - so
        /// a new teleport safety measure is added once, in this method, and no caller has to know about it.
        /// Movers that place the body every frame (the wire ride, the climb, the slingshot pull) are real
        /// motion and do not use this.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            // An enabled CharacterController would overwrite a transform write it didn't make itself.
            bool controllerWasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = controllerWasEnabled;

            // Otherwise the jump reads as thousands of metres per second of momentum on the next go-limp.
            if (velocityTracker != null)
                velocityTracker.ResetTracking();
        }

        void DoRespawn(RespawnPoint point)
        {
            characterController.enabled = false;

            Quaternion rotation = transform.rotation;
            if (point.UsePointYaw)
            {
                rotation = Quaternion.Euler(0f, point.Yaw, 0f);
                controller.SetCameraYaw(point.Yaw);
            }
            else
            {
                // Keep the facing but drop any climb pitch, so the capsule (offset forward from the
                // origin) isn't re-enabled tilted. The climb pose is nose-up, so fall back to the back's
                // direction, outward from the pole, as Demount does.
                Vector3 facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (facing.sqrMagnitude < 1e-4f)
                    facing = Vector3.ProjectOnPlane(transform.up, Vector3.up);
                if (facing.sqrMagnitude > 1e-4f)
                    rotation = Quaternion.LookRotation(facing, Vector3.up);
            }

            Teleport(point.Position, rotation);

            // Stale base-controller state from before the teleport: a Space pressed while dead must not
            // fire on the first grounded frame, and a fall speed must not carry to the new spot.
            controller.InputEnabled = true;
            inputs.JumpInput(false);
            controller.ResetVerticalVelocity();
            groundDebounce.Reset();

            lockouts.ClearAll();
            PlayerMode previous = Current;
            Current = PlayerMode.Walking;
            ApplyModeEffects(); // re-enables LocomotionEnabled and the CharacterController for Walking

            // Death has no outgoing transition, so it holds on the clip's last frame for the whole wait
            // rather than standing up early. Cut back to Idle here, at the exact moment of the respawn,
            // rather than timing an animator transition to a code-side delay that could drift out of sync
            // if the delay fields are tuned.
            PlayerAnimation.PlayIdle(animator);

            if (previous != Current)
                ModeChanged?.Invoke(previous, Current);
            Respawned?.Invoke();
        }

        static RespawnPoint FindRespawnPoint()
        {
            RespawnPoint fallback = null;
            foreach (RespawnPoint point in FindObjectsByType<RespawnPoint>(FindObjectsInactive.Exclude))
            {
                if (point.IsDefault)
                    return point;
                fallback = point;
            }
            return fallback;
        }
    }
}
