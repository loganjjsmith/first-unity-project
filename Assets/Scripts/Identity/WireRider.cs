using System.Collections.Generic;
using StarterAssets;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Ride a wire as a sticky rail, mount from the air or a perch, or drop deliberately. Riding into a pole
    /// continues onto the one way on, lets the camera pick at a split, and otherwise perches; only an end
    /// with no pole under it hops off. The pure geometry lives in <see cref="WireRiderMath"/> and the
    /// wire/pole topology in <see cref="WireNetwork"/>; this MonoBehaviour reads input and drives the
    /// transform directly while <see cref="PlayerModes"/> has disabled the
    /// <see cref="CharacterController"/> for WireRiding.
    /// </summary>
    /// <remarks>
    /// Every <see cref="SplineContainer"/> in the scene is a wire. Wires carry no collider, so mounting is a
    /// nearest-point query on the splines rather than a physics overlap.
    /// </remarks>
    [RequireComponent(typeof(PlayerModes))]
    [RequireComponent(typeof(PoleClimber))]
    [RequireComponent(typeof(ThirdPersonController))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StarterAssetsInputs))]
    public class WireRider : MonoBehaviour
    {
        /// <summary>
        /// A short pole-grab lock after any wire exit, so landing beside a pole straight off a wire does not
        /// grab it in the same instant.
        /// </summary>
        public const float ClimbLockAfterWireExit = 0.15f;

        [Header("Tuning")]
        public float WireMoveSpeed = 16f;
        public float WirePerchGrabRange = 11f;
        public float WireAirGrabRange = 4.4f;
        public float WireReentryLockDuration = 1.25f;
        public float WireJumpOffSpeed = 3f;
        public float WireJumpOffUpSpeed = 10f;
        public float SlingshotWireLockDuration = 1.75f;

        [Tooltip("Extra gap between the cable's top and the feet, metres. The cable's own thickness is " +
                 "measured from its Spline Extrude, and the Fox's origin is already at its feet, so 0 means " +
                 "feet on the wire. Tuning on top of the measurement, not a fitted body dimension.")]
        public float RideClearance = 0f;

        [Header("Poles and junctions")]
        [Tooltip("How close a spline knot must be to a Pole for the wire to count as attached there.")]
        public float PoleAttachRadius = 1f;

        [Tooltip("Stopping within this many body lengths of a pole on the wire perches on it.")]
        public float HaltPerchBodyLengths = 1.5f;

        [Tooltip("At a split with several wires, how closely the camera must point along one (dot, 0-1) " +
                 "for the ride to continue onto it. 0.5 is within 60 degrees.")]
        public float JunctionMinCameraDot = 0.5f;

        [Tooltip("...and by how much it must beat the next-best branch, so looking between two branches " +
                 "perches rather than guessing.")]
        public float JunctionClearMargin = 0.2f;

        [Tooltip("From a perch, how closely the camera must point along a wire for W to take it. 0 means " +
                 "anywhere in the front half.")]
        public float PerchMountMinCameraDot = 0f;

        [Tooltip("After a perch leap, that pole's own wires can't be grabbed for this long, so a leap is a " +
                 "leap. Wires on other poles are grabbable immediately.")]
        public float PerchJumpWireIgnoreSeconds = 0.5f;

        [Tooltip("Animator Speed value driven while actively travelling along the wire - the same " +
                 "best-effort walk placeholder PoleClimber uses for climbing, since the Fox pack has no " +
                 "ride clip either.")]
        public float RidingMovingAnimSpeed = 8f;

        PlayerModes modes;
        ThirdPersonController controller;
        CharacterController characterController;
        StarterAssetsInputs inputs;
        Animator animator;
        PoleClimber poleClimber;
        WireNetwork network;

        bool wasMovingOnWire;
        Collider switchedAtPole;
        Vector3 rideVelocity;
        Collider ignoredPole;
        float ignoredPoleTimer;

        SplineContainer currentWire;
        float t;
        float facingSign = 1f;
        bool wasJumpHeld;
        bool wasUpHeld;

        int pendingLandingWireId = Lockouts.NoWire;
        float pendingLandingLock;

        /// <summary>The wire currently being ridden (or charging a slingshot pull from), or null.</summary>
        public SplineContainer CurrentWire => currentWire;

        /// <summary>The current parametric position (0..1) along <see cref="CurrentWire"/>.</summary>
        public float T => t;

        /// <summary>
        /// The world-space ride point on <see cref="CurrentWire"/> at <see cref="T"/>, on the wire's actual
        /// shape: if someone is bending it, the rider moves with the bend.
        /// </summary>
        public Vector3 RidePoint => currentWire != null
            ? WireRiderMath.RidePoint(WirePosition(currentWire, t), RideOffsetFor(currentWire))
            : transform.position;

        /// <summary>
        /// The ride point on the wire's rest shape, ignoring any bend. The slingshot pulls from here, since
        /// the bend it causes must not move the point it is measured from.
        /// </summary>
        public Vector3 RestRidePoint => currentWire != null
            ? WireRiderMath.RidePoint(ToVector3(currentWire.EvaluatePosition(t)), RideOffsetFor(currentWire))
            : transform.position;

        /// <summary>
        /// Set by <c>Slingshot</c> while charging, so travel input cannot fight the pull. Charging is its own
        /// mode, so <see cref="TickRiding"/> does not run then anyway; the flag states the intent outright
        /// rather than leaving it to depend on that gating.
        /// </summary>
        public bool FreezeTravel { get; set; }

        WireRiderParams Params => new WireRiderParams(
            WireMoveSpeed, WirePerchGrabRange, WireAirGrabRange, WireReentryLockDuration,
            WireJumpOffSpeed, WireJumpOffUpSpeed, SlingshotWireLockDuration);

        /// <summary>
        /// Built on first use rather than in Awake, because it needs PoleClimber's pole layer and
        /// PoleClimber fills in its default in its own Awake, which may run after this one.
        /// </summary>
        WireNetwork Network => network ??= new WireNetwork(poleClimber.PoleLayer, PoleAttachRadius);

        void Awake()
        {
            modes = GetComponent<PlayerModes>();
            controller = GetComponent<ThirdPersonController>();
            characterController = GetComponent<CharacterController>();
            inputs = GetComponent<StarterAssetsInputs>();
            animator = GetComponent<Animator>();
            poleClimber = GetComponent<PoleClimber>();

            modes.Landed += OnLanded;
            modes.Respawned += OnRespawned;
            poleClimber.JumpedOffPerch += OnJumpedOffPerch;
        }

        void OnDestroy()
        {
            if (modes == null)
                return;

            modes.Landed -= OnLanded;
            modes.Respawned -= OnRespawned;
            if (poleClimber != null)
                poleClimber.JumpedOffPerch -= OnJumpedOffPerch;
        }

        void Update()
        {
            ignoredPoleTimer = Mathf.Max(0f, ignoredPoleTimer - Time.deltaTime);

            bool jumpPressedEdge = inputs.jump && !wasJumpHeld;
            bool upPressedEdge = inputs.move.y > 0.1f && !wasUpHeld;
            wasJumpHeld = inputs.jump;
            wasUpHeld = inputs.move.y > 0.1f;

            // Dead during the respawn wait: hold still. After the edge bookkeeping, so a key held through
            // the death animation isn't read as a fresh press afterwards.
            if (modes.IsRespawning)
                return;

            switch (modes.Current)
            {
                case PlayerMode.Airborne:
                case PlayerMode.Launched:
                    TryAutoMountFromAir();
                    break;

                case PlayerMode.Perched:
                    // A fresh W press, not a held one: otherwise holding W up a pole would perch for one
                    // frame and then mount the wire straight away.
                    if (upPressedEdge)
                        TryMountFromPerch();
                    break;

                case PlayerMode.WireRiding:
                    TickRiding(jumpPressedEdge);
                    break;
            }
        }

        // ---------------------------------------------------------------- mount

        void TryAutoMountFromAir()
        {
            if (characterController.isGrounded)
                return;

            Vector3 center = transform.TransformPoint(characterController.center);
            Collider skipPole = ignoredPoleTimer > 0f ? ignoredPole : null;
            if (!TryFindNearestWire(center, Params.WireAirGrabRange, skipPole, out SplineContainer wire, out float nearestT))
                return;

            if (!modes.Lockouts.CanGrabWire(WireId(wire)))
                return;

            if (!modes.TryTransition(ModeTrigger.GrabWireFromAir))
                return;

            Mount(wire, nearestT);
        }

        void TryMountFromPerch()
        {
            // Wires attached to this pole: the camera picks which one, and which way along it - "turn back"
            // is just looking back down the wire you arrived on. Nearest-point alone can't choose here,
            // because every wire on the pole starts at the same spot.
            List<WireExit> exits = Network.ExitsFrom(poleClimber.CurrentPole, null, 0f, 0f);
            if (exits.Count > 0)
            {
                int chosen = WireRiderMath.BestAlignedIndex(CameraBasis.PlanarForward, WireNetwork.Directions(exits), PerchMountMinCameraDot, 0f);
                if (chosen < 0)
                    return;

                WireExit exit = exits[chosen];
                if (!modes.Lockouts.CanGrabWire(WireId(exit.Wire)))
                    return;
                if (!modes.TryTransition(ModeTrigger.MountWireFromPerch))
                    return;

                Mount(exit.Wire, exit.T);
                facingSign = exit.Sign;
                SnapToRidePose();
                return;
            }

            // No wire attached to this pole: mount any wire in range instead.
            Vector3 center = transform.TransformPoint(characterController.center);
            if (!TryFindNearestWire(center, Params.WirePerchGrabRange, null, out SplineContainer wire, out float nearestT))
                return;

            if (!modes.Lockouts.CanGrabWire(WireId(wire)))
                return;

            if (!modes.TryTransition(ModeTrigger.MountWireFromPerch))
                return;

            Mount(wire, nearestT);
        }

        /// <summary>
        /// The nearest mountable wire within range, measured from the capsule centre rather than the feet.
        /// </summary>
        bool TryFindNearestWire(Vector3 fromWorldPoint, float range, Collider skipPole, out SplineContainer nearestWire, out float nearestT)
        {
            nearestWire = null;
            nearestT = 0f;
            float bestDistance = float.MaxValue;

            foreach (SplineContainer candidate in Network.AllWires())
            {
                Spline spline = candidate.Spline;
                if (spline == null || spline.Count < 2)
                    continue;

                if (skipPole != null && Network.TouchesPole(candidate, skipPole))
                    continue;

                float3 localPoint = ToFloat3(candidate.transform.InverseTransformPoint(fromWorldPoint));
                SplineUtility.GetNearestPoint(spline, localPoint, out float3 nearestLocal, out float candidateT);

                Vector3 nearestWorld = candidate.transform.TransformPoint(ToVector3(nearestLocal));
                Vector3 ridePoint = WireRiderMath.RidePoint(nearestWorld, RideOffsetFor(candidate));
                float distance = Vector3.Distance(fromWorldPoint, ridePoint);

                if (distance <= range && distance < bestDistance)
                {
                    bestDistance = distance;
                    nearestWire = candidate;
                    nearestT = candidateT;
                }
            }

            return nearestWire != null;
        }

        void Mount(SplineContainer wire, float atT)
        {
            currentWire = wire;
            t = atT;
            wasMovingOnWire = false;
            rideVelocity = Vector3.zero;
            switchedAtPole = null;

            Vector3 tangent = ToVector3(wire.EvaluateTangent(t));
            facingSign = WireRiderMath.MountFacingSign(CameraBasis.PlanarForward, tangent);

            controller.ResetVerticalVelocity();
            inputs.JumpInput(false); // cleared on every mode enter and exit

            // Grabbing a wire can move the body metres in one frame, so it is a teleport, not motion.
            modes.Teleport(RidePosition(), RideRotation());
            PlayerAnimation.EnterStillPose(animator);
        }

        // ---------------------------------------------------------------- riding

        void TickRiding(bool jumpPressedEdge)
        {
            if (currentWire == null)
                return; // shouldn't happen given the mode gating, but never crash if it does

            if (jumpPressedEdge)
            {
                DetachWithKick();
                return;
            }

            bool isMoving = false;
            rideVelocity = Vector3.zero;
            if (!FreezeTravel)
            {
                Vector3 tangent = ToVector3(currentWire.EvaluateTangent(t));
                float direction = WireRiderMath.TravelDirection(CameraBasis.PlanarForward, tangent, inputs.move.y, facingSign);
                isMoving = direction != 0f;

                if (isMoving)
                {
                    float previousT = t;
                    currentWire.Spline.GetPointAtLinearDistance(t, direction * WireMoveSpeed * Time.deltaTime, out float newT);
                    t = newT;
                    facingSign = direction;
                    rideVelocity = tangent.normalized * (direction * WireMoveSpeed);

                    if (Network.TryFindReachedStop(currentWire, previousT, t, direction, out PoleStop stop))
                    {
                        if (ArriveAtPole(stop, direction))
                            return; // perched
                    }
                    else
                    {
                        switchedAtPole = null; // moved on without reaching a pole: any junction is behind us
                        if (WireRiderMath.ReachedEnd(t, direction))
                        {
                            DetachWithKick(PlayerModes.LaunchKind.WireEnd); // a free end, with no pole under it
                            return;
                        }
                    }
                }
            }

            // The cheap edge check first, so the stop scan only runs on the frame travel stops.
            if (!isMoving && wasMovingOnWire && TryPerchOnHalt(wasMovingOnWire, isMoving))
                return;

            wasMovingOnWire = isMoving;
            SnapToRidePose();
            PlayerAnimation.SetTravelling(animator, isMoving, RidingMovingAnimSpeed);
        }

        // ---------------------------------------------------------------- poles and junctions

        /// <summary>
        /// Rode onto a pole stop. Keeps riding if there is a way on (switching wires at a junction), and
        /// perches otherwise. Returns true if it perched, which ends this frame's riding.
        /// </summary>
        bool ArriveAtPole(PoleStop stop, float direction)
        {
            // Reversing into the pole we switched wires at on the previous frame. It happens when the camera
            // disagrees with the wire the junction chose: W then reads as "back" on the new wire. Choosing
            // again would bounce between the two wires every frame, so treat it as an unclear choice, and an
            // unclear choice perches.
            if (stop.Pole == switchedAtPole)
            {
                PerchOnPole(stop.Pole);
                return true;
            }

            Vector3 arriving = ToVector3(currentWire.EvaluateTangent(stop.T)).normalized * direction;
            List<WireExit> exits = Network.ExitsFrom(stop.Pole, currentWire, stop.T, direction);
            int chosen = WireRiderMath.ChooseContinuation(arriving, CameraBasis.PlanarForward, WireNetwork.Directions(exits),
                JunctionMinCameraDot, JunctionClearMargin);

            if (chosen < 0)
            {
                PerchOnPole(stop.Pole);
                return true;
            }

            WireExit exit = exits[chosen];
            if (exit.Wire != currentWire || exit.Sign != direction)
            {
                currentWire = exit.Wire;
                t = exit.T;
                facingSign = exit.Sign;
                switchedAtPole = stop.Pole;
            }
            return false;
        }

        /// <summary>Stopped travelling next to a pole on this wire: perch on it.</summary>
        bool TryPerchOnHalt(bool wasMoving, bool isMoving)
        {
            Vector3 here = ToVector3(currentWire.EvaluatePosition(t));
            float bodyLength = Mathf.Max(characterController.height,
                Mathf.Abs(characterController.center.z) + characterController.radius);
            float reach = HaltPerchBodyLengths * bodyLength;

            foreach (PoleStop stop in Network.StopsOf(currentWire))
            {
                if (WireRiderMath.ShouldPerchOnHalt(wasMoving, isMoving, Vector3.Distance(here, stop.Point), reach))
                {
                    PerchOnPole(stop.Pole);
                    return true;
                }
            }
            return false;
        }

        void PerchOnPole(Collider pole)
        {
            if (!poleClimber.PerchOn(pole))
            {
                DetachWithKick(); // the mode table refused - never leave the rider stuck on nothing
                return;
            }

            currentWire = null;
            wasMovingOnWire = false;
            rideVelocity = Vector3.zero;
            switchedAtPole = null;
        }

        /// <summary>
        /// How high above the spline the root rides: the cable's own radius, measured from its Spline
        /// Extrude, plus <see cref="RideClearance"/>. The Fox's origin is at its feet, so this puts the feet
        /// on the cable, and a thicker cable needs no retuning.
        /// </summary>
        float RideOffsetFor(SplineContainer wire)
        {
            SplineExtrude extrude = wire.GetComponent<SplineExtrude>();
            float thickness = extrude != null ? extrude.Radius * wire.transform.lossyScale.y : 0f;
            return thickness + RideClearance;
        }

        Vector3 RidePosition() => RidePoint;

        Quaternion RideRotation() =>
            Quaternion.Euler(0f, WireRiderMath.FacingYawFromTangent(ToVector3(currentWire.EvaluateTangent(t)), facingSign), 0f);

        void SnapToRidePose()
        {
            transform.SetPositionAndRotation(RidePosition(), RideRotation());
        }

        /// <summary>
        /// The stretch of the current wire a pull at the rider's position may bend: from the pole before it
        /// to the pole after it, or to the wire's own ends. False when not on a wire.
        /// </summary>
        public bool TryGetBendSpan(out float startT, out float endT)
        {
            startT = 0f;
            endT = 1f;
            if (currentWire == null)
                return false;

            List<PoleStop> stops = Network.StopsOf(currentWire);
            List<float> stopTs = new List<float>(stops.Count);
            foreach (PoleStop stop in stops)
                stopTs.Add(stop.T);

            WireBendMath.SpanAround(stopTs, t, out startT, out endT);
            return true;
        }

        // ---------------------------------------------------------------- detach

        /// <summary>
        /// A Space drop or an automatic end hop, which share one kick (see
        /// <see cref="WireRiderMath.DetachVelocity"/>). The slingshot's Space-cancel while charging reuses it
        /// with <paramref name="requireGroundBeforeWire"/>, so that cancel cannot grab any wire until grounded.
        /// </summary>
        /// <remarks>
        /// A Space drop or end hop blocks only the wire just left, so another wire can be caught mid-air.
        /// </remarks>
        public void DetachWithKick(PlayerModes.LaunchKind kind = PlayerModes.LaunchKind.WireDrop,
            bool requireGroundBeforeWire = false)
        {
            WireRiderParams p = Params;
            Vector3 tangent = ToVector3(currentWire.EvaluateTangent(t));
            // Only a ride in progress carries momentum - a slingshot cancel reuses this from charging, where
            // the last riding velocity is stale.
            Vector3 carried = modes.Current == PlayerMode.WireRiding ? rideVelocity : Vector3.zero;
            Vector3 velocity = WireRiderMath.DetachVelocity(CameraBasis.Forward, CameraBasis.PlanarForward, tangent, carried, in p);
            Detach(velocity, requireGroundBeforeWire, kind);
        }

        /// <summary>
        /// Leaves the current wire and hands off to <see cref="PlayerModes.Launch"/>. Public so the
        /// slingshot's purposeful release and its Space-cancel share the same wire-release bookkeeping. A
        /// hazard release does not come through here: it locks every wire
        /// (<c>Lockouts.LockAllWires</c>), not just the one just left.
        /// </summary>
        public void Detach(Vector3 velocity, bool requireGroundBeforeWire,
            PlayerModes.LaunchKind kind = PlayerModes.LaunchKind.WireDrop)
        {
            if (currentWire == null)
                return;

            WireRiderParams p = Params;
            int wireId = WireId(currentWire);
            float lockSeconds = WireRiderMath.DetachLockDuration(in p);
            modes.Lockouts.BlockWire(wireId, lockSeconds);
            modes.Lockouts.LockClimb(ClimbLockAfterWireExit);

            // Every drop tops the lock up again on landing, so touching down right under the wire just
            // left cannot snap straight back onto it.
            pendingLandingWireId = wireId;
            pendingLandingLock = WireRiderMath.LandingLockTopUp(in p);

            if (requireGroundBeforeWire)
                modes.Lockouts.RequireGroundBeforeWire();

            currentWire = null;
            switchedAtPole = null;
            inputs.JumpInput(false);
            modes.Launch(velocity, kind);
        }

        /// <summary>
        /// Drops the wire reference with none of <see cref="Detach"/>'s lock/launch bookkeeping - for a
        /// caller that handles its own mode transition and lockouts: a hazard slingshot release, or going
        /// limp on the wire.
        /// </summary>
        public void ClearWire()
        {
            currentWire = null;
            switchedAtPole = null;
        }

        /// <summary>Tops the lock on the wire just left back up on landing (see <see cref="Detach"/>).</summary>
        void OnLanded()
        {
            if (pendingLandingWireId == Lockouts.NoWire)
                return;

            modes.Lockouts.BlockWire(pendingLandingWireId, pendingLandingLock);
            pendingLandingWireId = Lockouts.NoWire;
        }

        void OnJumpedOffPerch(Collider pole)
        {
            ignoredPole = pole;
            ignoredPoleTimer = PerchJumpWireIgnoreSeconds;
        }

        void OnRespawned()
        {
            currentWire = null;
            pendingLandingWireId = Lockouts.NoWire;
            wasMovingOnWire = false;
            rideVelocity = Vector3.zero;
            ignoredPoleTimer = 0f;
            switchedAtPole = null;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// A per-wire int key for <see cref="Lockouts"/>, which identifies wires by a plain <c>int</c>.
        /// <c>EntityId</c>'s own <c>int</c> conversion is obsolete-as-error in this Unity version, so this
        /// goes through <c>GetHashCode()</c> instead - enough for "which wire did I just leave", which never
        /// needs to survive a domain reload.
        /// </summary>
        static int WireId(SplineContainer wire) => wire.GetEntityId().GetHashCode();

        /// <summary>A wire's actual world position at <paramref name="atT"/>: its rest shape plus any bend.</summary>
        static Vector3 WirePosition(SplineContainer wire, float atT) =>
            wire.TryGetComponent(out BendableWire bendable)
                ? bendable.PositionAt(atT)
                : ToVector3(wire.EvaluatePosition(atT));

        static Vector3 ToVector3(float3 v) => new Vector3(v.x, v.y, v.z);
        static float3 ToFloat3(Vector3 v) => new float3(v.x, v.y, v.z);
    }
}
