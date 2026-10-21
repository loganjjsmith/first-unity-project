using System.Collections.Generic;
using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Tuning <see cref="WireRiderMath"/> needs, as plain numbers.
    /// Same shape and same warning as <see cref="PoleClimbParams"/>: build these from real tuning, not
    /// field by field, or a default-constructed instance's all-zero fields will silently produce inert
    /// (rather than merely wrong) behaviour.
    /// </summary>
    public readonly struct WireRiderParams
    {
        /// <summary>Ride speed along the wire, m/s.</summary>
        public readonly float WireMoveSpeed;

        /// <summary>Reach for the perch's "Hold W to yoink" mount, metres.</summary>
        public readonly float WirePerchGrabRange;

        /// <summary>Reach for the automatic airborne mount, metres.</summary>
        public readonly float WireAirGrabRange;

        /// <summary>Base no-regrab time after leaving a wire, seconds.</summary>
        public readonly float WireReentryLockDuration;

        /// <summary>Horizontal speed of a drop/hop-off kick, m/s.</summary>
        public readonly float WireJumpOffSpeed;

        /// <summary>Vertical speed of a drop/hop-off kick, m/s.</summary>
        public readonly float WireJumpOffUpSpeed;

        /// <summary>Wire lock after a slingshot launch, seconds.</summary>
        public readonly float SlingshotWireLockDuration;

        public WireRiderParams(
            float wireMoveSpeed,
            float wirePerchGrabRange,
            float wireAirGrabRange,
            float wireReentryLockDuration,
            float wireJumpOffSpeed,
            float wireJumpOffUpSpeed,
            float slingshotWireLockDuration)
        {
            WireMoveSpeed = wireMoveSpeed;
            WirePerchGrabRange = wirePerchGrabRange;
            WireAirGrabRange = wireAirGrabRange;
            WireReentryLockDuration = wireReentryLockDuration;
            WireJumpOffSpeed = wireJumpOffSpeed;
            WireJumpOffUpSpeed = wireJumpOffUpSpeed;
            SlingshotWireLockDuration = slingshotWireLockDuration;
        }
    }

    /// <summary>
    /// The wire-ride, mount and detach formulas. Pure static maths, no state, no
    /// Splines dependency - the actual spline sampling (<c>EvaluatePosition</c>, <c>EvaluateTangent</c>,
    /// <c>GetPointAtLinearDistance</c>) lives on <c>WireRider</c>, which feeds this the results.
    /// </summary>
    /// <remarks>
    /// One failure mode is designed out here rather than guarded against, pinned by a regression test:
    /// <list type="bullet">
    /// <item><b>Crawling when looking across the wire.</b> A speed that ramps with how closely the camera
    /// looks along the wire makes travel crawl at full input when looking nearly perpendicular to it. <see cref="TravelDirection"/>
    /// only ever resolves a direction (never a magnitude) - the caller always moves at full
    /// <see cref="WireRiderParams.WireMoveSpeed"/> once a direction is resolved, so there is no ramp left
    /// to be slow.</item>
    /// </list>
    /// </remarks>
    public static class WireRiderMath
    {
        /// <summary>Below this squared length a flat direction has no usable heading.</summary>
        const float FlatEpsilonSqr = 1e-8f;

        /// <summary>
        /// Below this, camera-forward is treated as too close to perpendicular to the wire to tell which
        /// way "along the camera" means.
        /// </summary>
        const float PerpendicularDeadzone = 0.15f;

        /// <summary>Above this |dot| with the tangent, an end-hop or drop kicks along the wire rather than the camera.</summary>
        const float EndHopTangentThreshold = 0.55f;

        /// <summary>The ride point: the rest path offset straight up by the per-rider clearance.</summary>
        public static Vector3 RidePoint(Vector3 restPoint, float rideOffset)
        {
            return restPoint + Vector3.up * rideOffset;
        }

        /// <summary>The facing sign to commit to at mount time: which way along the tangent the camera is looking.</summary>
        public static float MountFacingSign(Vector3 cameraPlanarForward, Vector3 tangent)
        {
            float dot = Vector3.Dot(cameraPlanarForward, tangent);
            return dot >= 0f ? 1f : -1f;
        }

        /// <summary>
        /// The direction to travel along the spline's increasing-t tangent this frame: +1, -1, or 0 for no
        /// input at all. A direction only, never a speed - see the type remarks. <paramref name="verticalInput"/> is W/S as a
        /// signed scalar (W positive, S negative), matching every other identity script's input reading.
        /// </summary>
        public static float TravelDirection(Vector3 cameraPlanarForward, Vector3 tangent, float verticalInput, float previousFacingSign)
        {
            if (Mathf.Abs(verticalInput) < 1e-4f)
                return 0f;

            float camDotTangent = Vector3.Dot(cameraPlanarForward, tangent);
            float along = camDotTangent * verticalInput;

            // Camera looking ~perpendicular to the wire: there's no reliable "along the camera" signal, so
            // fall back to the committed facing rather than stalling.
            if (Mathf.Abs(camDotTangent) < PerpendicularDeadzone && Mathf.Abs(along) < PerpendicularDeadzone)
                along = verticalInput * previousFacingSign;

            return along >= 0f ? 1f : -1f;
        }

        /// <summary>Whether travelling in <paramref name="direction"/> has carried the rider off the end at <paramref name="t"/>.</summary>
        public static bool ReachedEnd(float t, float direction)
        {
            if (direction > 0f)
                return t >= 0.999f;
            if (direction < 0f)
                return t <= 0.001f;
            return false;
        }

        /// <summary>
        /// The direction for a drop or end-hop kick: camera-forward, unless the camera is looking close
        /// enough to along the wire itself (|dot| with the tangent above the threshold), in which case the
        /// kick follows the tangent instead - the same "don't launch into the thing you're leaving" shape
        /// as <see cref="PoleClimbMath.JumpOffHorizontalDirection"/>, here avoiding a kick that
        /// reads as launching back along the rail rather than off it.
        /// </summary>
        public static Vector3 DetachDirection(Vector3 cameraForward, Vector3 cameraPlanarForward, Vector3 tangent)
        {
            float dot = Vector3.Dot(cameraForward, tangent);
            if (Mathf.Abs(dot) > EndHopTangentThreshold)
            {
                Vector3 alongTangent = tangent.sqrMagnitude > FlatEpsilonSqr ? tangent.normalized : Vector3.forward;
                return alongTangent * Mathf.Sign(dot);
            }

            if (cameraPlanarForward.sqrMagnitude > FlatEpsilonSqr)
                return cameraPlanarForward.normalized;

            return tangent.sqrMagnitude > FlatEpsilonSqr ? tangent.normalized : Vector3.forward;
        }

        /// <summary>
        /// The detach kick's velocity - used for both a manual Space drop and an automatic end hop, since
        /// they share one kick. If the two ever need to differ, this is the one place to split.
        /// </summary>
        /// <param name="rideVelocity">
        /// The rider's velocity along the wire at the moment of leaving it, zero if standing still. Carried
        /// into the kick so a jump off a moving ride keeps its momentum - enough to
        /// jump from the wire to an adjacent rooftop.
        /// </param>
        public static Vector3 DetachVelocity(Vector3 cameraForward, Vector3 cameraPlanarForward, Vector3 tangent, Vector3 rideVelocity, in WireRiderParams p)
        {
            Vector3 direction = DetachDirection(cameraForward, cameraPlanarForward, tangent);
            return rideVelocity + direction * p.WireJumpOffSpeed + Vector3.up * p.WireJumpOffUpSpeed;
        }

        /// <summary>
        /// Which of several directions best matches <paramref name="reference"/>, compared flat (yaw only),
        /// or -1 when none is a clear match. "Clear" means the best is at least <paramref name="minDot"/>
        /// aligned, and beats the runner-up by at least <paramref name="clearMargin"/> - so a camera looking
        /// straight between two branches, or at the dead end of a T, picks nothing rather than guessing.
        /// </summary>
        public static int BestAlignedIndex(Vector3 reference, IReadOnlyList<Vector3> options, float minDot, float clearMargin)
        {
            Vector3 flatReference = Flat(reference);
            if (options == null || options.Count == 0 || flatReference == Vector3.zero)
                return -1;

            int best = -1;
            float bestDot = float.NegativeInfinity;
            float secondDot = float.NegativeInfinity;

            for (int i = 0; i < options.Count; i++)
            {
                Vector3 flatOption = Flat(options[i]);
                if (flatOption == Vector3.zero)
                    continue;

                float dot = Vector3.Dot(flatReference, flatOption);
                if (dot > bestDot)
                {
                    secondDot = bestDot;
                    bestDot = dot;
                    best = i;
                }
                else if (dot > secondDot)
                {
                    secondDot = dot;
                }
            }

            if (best < 0 || bestDot < minDot)
                return -1;
            if (bestDot - secondDot < clearMargin)
                return -1;
            return best;
        }

        /// <summary>
        /// Riding into a pole: which way on to continue, or -1 to perch.
        /// <paramref name="outgoing"/> is every way off the pole except back the way the rider came.
        /// <list type="bullet">
        /// <item>None: the end of the line. Perch.</item>
        /// <item>One (a midway pole, or two wires meeting): keep riding, as long as it does not turn back on
        /// itself (more than 90 degrees from the direction of arrival). A V-shaped pair perches instead.</item>
        /// <item>Several (a 3- or 4-way split): the camera chooses. If it does not clearly point along one
        /// branch - the dead end of a T, or between two branches - perch and let the player decide.</item>
        /// </list>
        /// </summary>
        public static int ChooseContinuation(Vector3 arrivingDirection, Vector3 cameraPlanarForward, IReadOnlyList<Vector3> outgoing, float minCameraDot, float clearMargin)
        {
            if (outgoing == null || outgoing.Count == 0)
                return -1;

            if (outgoing.Count == 1)
                return Vector3.Dot(Flat(arrivingDirection), Flat(outgoing[0])) > 0f ? 0 : -1;

            return BestAlignedIndex(cameraPlanarForward, outgoing, minCameraDot, clearMargin);
        }

        /// <summary>How close two normalized spline positions must be to count as the same point.</summary>
        const float StopEpsilon = 0.001f;

        /// <summary>
        /// Whether moving from <paramref name="fromT"/> to <paramref name="toT"/> in <paramref name="direction"/>
        /// reaches the pole stop at <paramref name="stopT"/>. A stop sitting at <paramref name="fromT"/> is
        /// excluded, so riding away from a pole (after mounting from its perch, or continuing through it)
        /// doesn't re-arrive at it - except when that stop is the wire's end and the move pushes into it.
        /// The spline clamps t at its ends, so a rider already at an end can never "cross" the stop there,
        /// yet it is riding into the pole, and a pole end perches.
        /// </summary>
        public static bool PassesStop(float stopT, float fromT, float toT, float direction)
        {
            if (Mathf.Abs(stopT - fromT) <= StopEpsilon && ReachedEnd(stopT, direction))
                return true;

            if (direction > 0f)
                return stopT > fromT + StopEpsilon && stopT <= toT + StopEpsilon;
            if (direction < 0f)
                return stopT < fromT - StopEpsilon && stopT >= toT - StopEpsilon;
            return false;
        }

        /// <summary>
        /// Stopping next to a pole perches on it: only on the frame travel
        /// stops, not every idle frame, so mounting from a perch and standing still does not bounce straight
        /// back onto the pole.
        /// </summary>
        public static bool ShouldPerchOnHalt(bool wasMoving, bool isMoving, float distanceToPole, float reach)
        {
            return wasMoving && !isMoving && distanceToPole <= reach;
        }

        /// <summary>The flat (yaw-only) unit direction, or zero if it has no usable heading.</summary>
        static Vector3 Flat(Vector3 v)
        {
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            return flat.sqrMagnitude > FlatEpsilonSqr ? flat.normalized : Vector3.zero;
        }

        /// <summary>
        /// The wire lock applied on a Space drop, an end hop, or a purposeful slingshot release - 1.49 s
        /// at the live tuning (max(1.25, 1.75 * 0.85)), not the base <see cref="WireRiderParams.WireReentryLockDuration"/>
        /// on its own, which is a softer detach with no other stated trigger in the current mode table.
        /// </summary>
        public static float DetachLockDuration(in WireRiderParams p)
        {
            return Mathf.Max(p.WireReentryLockDuration, p.SlingshotWireLockDuration * 0.85f);
        }

        /// <summary>
        /// The minimum lock a landing tops the wire just left back up to, after any detach. 0.875 s is
        /// exactly half of <see cref="WireRiderParams.SlingshotWireLockDuration"/> at the live tuning
        /// (1.75), so it is derived here rather than hard-coded, in case the live numbers ever move.
        /// </summary>
        public static float LandingLockTopUp(in WireRiderParams p)
        {
            return p.SlingshotWireLockDuration * 0.5f;
        }

        /// <summary>The facing yaw while riding: the tangent, flipped by the current facing sign.</summary>
        public static float FacingYawFromTangent(Vector3 tangent, float facingSign)
        {
            Vector3 flat = new Vector3(tangent.x * facingSign, 0f, tangent.z * facingSign);
            if (flat.sqrMagnitude < FlatEpsilonSqr)
                return 0f;

            return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }
    }
}
