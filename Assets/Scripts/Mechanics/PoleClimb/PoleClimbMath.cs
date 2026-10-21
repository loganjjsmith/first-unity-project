using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Tuning <see cref="PoleClimbMath"/> needs, as plain numbers. Same shape as
    /// <see cref="SlingshotParams"/> and the same warning applies: a default-constructed instance has
    /// every field at 0, which makes <see cref="PoleClimbMath.CanGrab"/> permissive (a zero grab margin)
    /// rather than inert, so build these from real tuning rather than field by field.
    /// </summary>
    public readonly struct PoleClimbParams
    {
        /// <summary>Vertical climb speed, m/s.</summary>
        public readonly float ClimbSpeed;

        /// <summary>Gap kept between the pole's surface and the body, metres.</summary>
        public readonly float ClimbSurfacePadding;

        /// <summary>A/D orbit speed, degrees per second.</summary>
        public readonly float ClimbOrbitSpeed;

        /// <summary>S auto-demount height above the pole bottom, in body-halves.</summary>
        public readonly float ClimbDemountBodyHeights;

        /// <summary>Perch facing snaps to this many steps around the pole (8 = every 45 degrees).</summary>
        public readonly int PerchYawSteps;

        /// <summary>Seat height above the pole top, metres.</summary>
        public readonly float PerchTopPadding;

        /// <summary>Perch turn rate toward the snapped facing, used with the frame-rate-independent
        /// <c>1 - exp(-rate*dt)</c> rule, not a raw per-frame Lerp factor.</summary>
        public readonly float PerchTurnSpeed;

        /// <summary>Horizontal speed of a pole jump-off kick, m/s.</summary>
        public readonly float PoleJumpOutSpeed;

        /// <summary>Vertical speed of a pole jump-off kick, m/s.</summary>
        public readonly float PoleJumpUpSpeed;

        public PoleClimbParams(
            float climbSpeed,
            float climbSurfacePadding,
            float climbOrbitSpeed,
            float climbDemountBodyHeights,
            int perchYawSteps,
            float perchTopPadding,
            float perchTurnSpeed,
            float poleJumpOutSpeed,
            float poleJumpUpSpeed)
        {
            ClimbSpeed = climbSpeed;
            ClimbSurfacePadding = climbSurfacePadding;
            ClimbOrbitSpeed = climbOrbitSpeed;
            ClimbDemountBodyHeights = climbDemountBodyHeights;
            PerchYawSteps = perchYawSteps;
            PerchTopPadding = perchTopPadding;
            PerchTurnSpeed = perchTurnSpeed;
            PoleJumpOutSpeed = poleJumpOutSpeed;
            PoleJumpUpSpeed = poleJumpUpSpeed;
        }
    }

    /// <summary>
    /// The pole climb, perch and jump-off formulas. Pure static maths, no state,
    /// no scene access, so it is unit-testable and there is exactly one place each rule is decided.
    /// </summary>
    /// <remarks>
    /// Three failure modes are designed out here rather than guarded against, each pinned by a regression
    /// test:
    /// <list type="bullet">
    /// <item><b>Jumping off the wrong side.</b> A fallback for a degenerate flattened-forward vector can
    /// silently never run and send every jump to one world side of the pole. <see cref="ResolveOutward"/>
    /// always returns a real direction - the current offset from the pole axis, which is non-zero by
    /// construction while climbing or perched - so there is no degenerate case to fall back from.</item>
    /// <item><b>World-relative orbit.</b> A/D would flip when the camera swings to the far side of the
    /// pole. <see cref="OrbitDirectionSign"/> derives the sign from camera-right dotted with the
    /// orbit tangent, so A/D stays camera-relative regardless of which side the camera is looking from.</item>
    /// <item><b>Jumping into the pole.</b> A kick along raw camera-forward launches the player into the
    /// pole when the player is looking at it. <see cref="JumpOffHorizontalDirection"/> falls back
    /// to <paramref name="outward"/> whenever camera-forward points back toward the pole.</item>
    /// </list>
    /// </remarks>
    public static class PoleClimbMath
    {
        /// <summary>Below this squared length a flat direction has no usable heading.</summary>
        const float FlatEpsilonSqr = 1e-8f;

        /// <summary>
        /// How far out from the pole's axis the body sticks, given the pole's own footprint and the
        /// body's radius. The real grab reach is this plus a small margin (see <see cref="CanGrab"/>).
        /// </summary>
        public static float StickDistance(float poleBoundsExtentX, float poleBoundsExtentZ, float bodyRadius, in PoleClimbParams p)
        {
            float poleRadius = Mathf.Max(Mathf.Abs(poleBoundsExtentX), Mathf.Abs(poleBoundsExtentZ));
            return poleRadius + Mathf.Max(0f, bodyRadius) + p.ClimbSurfacePadding;
        }

        /// <summary>
        /// How far out from the pole's axis the body sits *while climbing*: the pole's radius plus
        /// <paramref name="hugDistance"/>, measured from the pole's surface to the body point. Kept separate
        /// from <see cref="StickDistance"/> so the IK-planted cling can pull the
        /// belly in close without also shrinking grab reach or moving the demount and jump-off points.
        /// </summary>
        public static float ClimbHugStickDistance(float poleBoundsExtentX, float poleBoundsExtentZ, float hugDistance)
        {
            float poleRadius = Mathf.Max(Mathf.Abs(poleBoundsExtentX), Mathf.Abs(poleBoundsExtentZ));
            return poleRadius + Mathf.Max(0f, hugDistance);
        }

        /// <summary>
        /// Whether a pole may be grabbed from here. Only checks geometry - tag, layer and lockouts are the
        /// caller's job. <paramref name="flatDistanceFromAxis"/> is the horizontal distance from the pole's
        /// vertical axis to the grabbing position.
        /// </summary>
        public static bool CanGrab(float positionY, float poleTopY, float flatDistanceFromAxis, float stickDistance)
        {
            if (positionY >= poleTopY - 0.05f)
                return false;

            return flatDistanceFromAxis <= stickDistance + 0.3f;
        }

        /// <summary>Half the body's height, used throughout as the climb pose's vertical unit.</summary>
        public static float BodyHalf(float characterControllerHeight)
        {
            return Mathf.Max(0.1f, characterControllerHeight * 0.5f);
        }

        /// <summary>
        /// One frame of A/D orbit around the pole. <paramref name="orbitInput"/> is already signed by
        /// <see cref="OrbitDirectionSign"/> if camera-relative orbit is wanted (it should be).
        /// </summary>
        public static Vector3 RotateOffset(Vector3 offsetDir, float orbitInput, in PoleClimbParams p, float deltaTime)
        {
            float degrees = orbitInput * p.ClimbOrbitSpeed * deltaTime;
            return Quaternion.AngleAxis(degrees, Vector3.up) * offsetDir;
        }

        /// <summary>
        /// Sign to apply to raw A/D input so orbit direction stays camera-relative rather than
        /// world-relative. <paramref name="tangent"/> is <paramref name="offsetDir"/> rotated
        /// 90 degrees - "the direction a positive orbit input would move you." Returns +1 or -1, never 0,
        /// so a caller can multiply it straight into <see cref="RotateOffset"/>'s orbit input.
        /// </summary>
        public static float OrbitDirectionSign(Vector3 cameraRight, Vector3 offsetDir)
        {
            Vector3 tangent = new Vector3(offsetDir.z, 0f, -offsetDir.x);
            float dot = Vector3.Dot(new Vector3(cameraRight.x, 0f, cameraRight.z), tangent);
            return dot >= 0f ? 1f : -1f;
        }

        /// <summary>
        /// One frame of vertical climb, clamped between just above the pole's bottom and just above its
        /// top (the extra <c>0.1 * bodyHalf</c> at the top is headroom for the perch-entry check to fire
        /// before the body would visually clip through the pole cap).
        /// </summary>
        /// <summary>
        /// The effective bottom of a climb, passed wherever the pole's bottom used to be. It is the higher of
        /// the pole's own bottom and the ground under the climber, because anything planted in the world is
        /// sunk into it and must not be climbable underground, or the player burrows and the camera
        /// follows it under the floor. It is then lifted by how far the body
        /// hangs below its body point in the nose-up climb pose, so the hind end stays above the floor too.
        /// </summary>
        public static float ClimbBottomY(float poleBottomY, bool groundFound, float groundY, float bodyReachBelow)
        {
            float floor = groundFound ? Mathf.Max(poleBottomY, groundY) : poleBottomY;
            return floor + Mathf.Max(0f, bodyReachBelow);
        }

        public static float ClampClimbY(float y, float verticalInput, in PoleClimbParams p, float deltaTime,
            float poleBottomY, float poleTopY, float bodyHalf)
        {
            float next = y + verticalInput * p.ClimbSpeed * deltaTime;
            return Mathf.Clamp(next, poleBottomY + 0.15f * bodyHalf, poleTopY + 0.1f * bodyHalf);
        }

        /// <summary>The climb pose's world position, given the current offset direction and height.</summary>
        public static Vector3 ClimbPosition(Vector3 axisXZ, Vector3 offsetDir, float stickDistance, float y)
        {
            Vector3 flatOffset = offsetDir.sqrMagnitude > FlatEpsilonSqr ? offsetDir.normalized : Vector3.forward;
            Vector3 pos = axisXZ + flatOffset * stickDistance;
            pos.y = y;
            return pos;
        }

        /// <summary>The root yaw while climbing: facing away from the pole.</summary>
        public static float FacingYawFromOffset(Vector3 offsetDir)
        {
            if (offsetDir.sqrMagnitude < FlatEpsilonSqr)
                return 0f;

            return Mathf.Atan2(offsetDir.x, offsetDir.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The full body orientation while climbing: vertical against the pole rather than upright on the
        /// ground. Forward points up or down the pole depending on <paramref name="facingSign"/> (positive
        /// - climbing up, head skyward; negative - climbing down, head-first toward the ground) and up always points away from the pole's axis, so the body's belly faces the pole
        /// surface regardless of which way it faces along the pole - the "hugging the pole" pose, approximated here as a root rotation since the imported model has no
        /// dedicated climb pose to switch to.
        /// </summary>
        public static Quaternion ClimbRotation(Vector3 offsetDir, float facingSign)
        {
            Vector3 outward = offsetDir.sqrMagnitude > FlatEpsilonSqr ? offsetDir.normalized : Vector3.forward;
            Vector3 forward = facingSign >= 0f ? Vector3.up : Vector3.down;
            return Quaternion.LookRotation(forward, outward);
        }

        /// <summary>
        /// The climb facing sign to carry into the next frame's <see cref="ClimbRotation"/>: flips to
        /// match <paramref name="verticalInput"/> once it clears the same 0.1 deadzone used elsewhere in
        /// this file for "is there real intent here," otherwise holds <paramref name="previousSign"/> -
        /// releasing W/S mid-climb should not snap the pose back to face-up, only a deliberate press of
        /// the other direction should.
        /// </summary>
        public static float ClimbFacingSign(float previousSign, float verticalInput)
        {
            return Mathf.Abs(verticalInput) > 0.1f ? Mathf.Sign(verticalInput) : previousSign;
        }

        /// <summary>
        /// The transform position to assign so the actual visual body - offset from the transform by
        /// <paramref name="bodyOffset"/> (<c>CharacterController.center</c>, tuned to the Fox's own visual
        /// center rather than the transform origin) - ends up at
        /// <paramref name="desiredBodyPoint"/> once rotated by <paramref name="rotation"/>. Needed anywhere
        /// a pose writes the transform directly, as the climb pose does while the CharacterController is
        /// disabled: without it, the invisible origin lands on the intended point while the visible mesh
        /// sits <c>rotation * bodyOffset</c> away from it - unnoticeable while walking, where that offset
        /// is constant, but a visible gap the moment a pose's rotation changes what "away" means, as the
        /// climb pose's vertical-forward orientation does to what was a forward-and-up offset on the
        /// ground.
        /// </summary>
        public static Vector3 RootPositionForBodyPoint(Vector3 desiredBodyPoint, Quaternion rotation, Vector3 bodyOffset)
        {
            return desiredBodyPoint - rotation * bodyOffset;
        }

        /// <summary>Whether holding W here should mantle onto the perch instead of continuing to climb.</summary>
        public static bool ShouldPerch(float y, float poleTopY, float bodyHalf)
        {
            return y >= poleTopY - 0.2f * bodyHalf;
        }

        /// <summary>The perch's seated position, re-applied every frame while perched.</summary>
        public static Vector3 PerchPosition(Vector3 axisXZ, float poleTopY, in PoleClimbParams p, float bodyHalf)
        {
            Vector3 pos = axisXZ;
            pos.y = poleTopY + p.PerchTopPadding + 0.15f * bodyHalf;
            return pos;
        }

        /// <summary>
        /// The facing yaw the perch snaps toward, given the camera's current yaw: the nearest of
        /// <see cref="PoleClimbParams.PerchYawSteps"/> equally spaced directions around the pole.
        /// </summary>
        public static float PerchTargetYaw(float cameraYawDegrees, in PoleClimbParams p)
        {
            int steps = Mathf.Max(1, p.PerchYawSteps);
            float stepSize = 360f / steps;
            float wrapped = Mathf.Repeat(cameraYawDegrees, 360f);
            return Mathf.Round(wrapped / stepSize) * stepSize;
        }

        /// <summary>Degrees within which the perch's turn snaps straight to its target rather than creeping.</summary>
        const float PerchSnapDegrees = 2f;

        /// <summary>
        /// One frame of the perch's turn toward its target yaw. Delegates to <see cref="Damping"/>'s
        /// frame-rate-independent <c>1 - exp(-rate*dt)</c> smoothing rather than a
        /// frame-rate-dependent <c>LerpAngle</c>, with a 2-degree snap so the perch settles instead of
        /// creeping forever.
        /// </summary>
        public static float SmoothYawTowards(float currentYaw, float targetYaw, float turnSpeed, float deltaTime)
        {
            return Damping.TowardAngleWithSnap(currentYaw, targetYaw, turnSpeed, deltaTime, PerchSnapDegrees);
        }

        /// <summary>Whether holding S here should step off the pole rather than continuing to climb down.</summary>
        public static bool ShouldDemount(float y, in PoleClimbParams p, float poleBottomY, float bodyHalf)
        {
            return y <= poleBottomY + bodyHalf * (1f + p.ClimbDemountBodyHeights);
        }

        /// <summary>
        /// The flat point to step out to on demount, outside the pole's footprint. The caller still has to
        /// raycast down from above this point to place it on the actual ground (a scene concern, not math).
        /// </summary>
        public static Vector3 DemountStepOutPoint(Vector3 axisXZ, Vector3 offsetDir, float stickDistance, float bodyRadius)
        {
            Vector3 flatOffset = offsetDir.sqrMagnitude > FlatEpsilonSqr ? offsetDir.normalized : Vector3.forward;
            float stepOut = Mathf.Max(stickDistance, bodyRadius) + bodyRadius + 0.25f;
            return axisXZ + flatOffset * stepOut;
        }

        /// <summary>
        /// The direction to treat as "away from the pole" for a jump-off. Always the current
        /// offset from the axis, which is non-zero by construction while climbing or perched, so there is
        /// no degenerate zero-vector case for a fallback to miss.
        /// </summary>
        public static Vector3 ResolveOutward(Vector3 offsetDir)
        {
            return offsetDir.sqrMagnitude > FlatEpsilonSqr ? offsetDir.normalized : Vector3.forward;
        }

        /// <summary>Where a jump-off teleports to before the kick is applied.</summary>
        public static Vector3 JumpOffPosition(Vector3 axisXZ, Vector3 outward, float stickDistance, float bodyRadius, float y)
        {
            float distance = Mathf.Max(stickDistance, bodyRadius) + bodyRadius + 0.35f;
            Vector3 pos = axisXZ + outward * distance;
            pos.y = y + 0.15f;
            return pos;
        }

        /// <summary>
        /// The kick's horizontal direction. Camera-forward, unless it points back toward the
        /// pole (dot product with <paramref name="outward"/> is negative), in which case <paramref
        /// name="outward"/> itself is used so a jump-off can never launch into the pole just stepped off.
        /// </summary>
        public static Vector3 JumpOffHorizontalDirection(Vector3 cameraPlanarForward, Vector3 outward)
        {
            if (Vector3.Dot(cameraPlanarForward, outward) < 0f)
                return outward;

            return cameraPlanarForward.sqrMagnitude > FlatEpsilonSqr ? cameraPlanarForward.normalized : outward;
        }

        /// <summary>The jump-off kick's velocity.</summary>
        public static Vector3 JumpOffVelocity(Vector3 cameraPlanarForward, Vector3 outward, in PoleClimbParams p)
        {
            Vector3 horizontal = JumpOffHorizontalDirection(cameraPlanarForward, outward);
            return horizontal * p.PoleJumpOutSpeed + Vector3.up * p.PoleJumpUpSpeed;
        }

        /// <summary>
        /// The leap off a perch: straight out along the way the player is facing, Assassin's Creed style.
        /// Separate from <see cref="JumpOffVelocity"/>, which kicks away from the side
        /// of a pole mid-climb; on a perch there is no side, only a facing - and the perch already turns to
        /// follow the camera, so facing is where the player is looking.
        /// </summary>
        public static Vector3 PerchJumpVelocity(Vector3 facing, float outSpeed, float upSpeed)
        {
            Vector3 flat = new Vector3(facing.x, 0f, facing.z);
            Vector3 horizontal = flat.sqrMagnitude > FlatEpsilonSqr ? flat.normalized : Vector3.forward;
            return horizontal * outSpeed + Vector3.up * upSpeed;
        }
    }
}
