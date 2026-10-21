using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the pole climb/perch/jump-off formulas, and in particular the three failure modes
    /// described on <see cref="PoleClimbMath"/>. Each "Regression" test fails if one of them comes back.
    /// </summary>
    public class PoleClimbMathTests
    {
        /// <summary>The live tuning, so numbers here match the game's.</summary>
        static PoleClimbParams Live()
        {
            return new PoleClimbParams(
                climbSpeed: 16f,
                climbSurfacePadding: 0.2f,
                climbOrbitSpeed: 180f,
                climbDemountBodyHeights: 0.35f,
                perchYawSteps: 8,
                perchTopPadding: 0.2f,
                perchTurnSpeed: 14f,
                poleJumpOutSpeed: 3f,
                poleJumpUpSpeed: 14f);
        }

        const float BodyRadius = 0.35f;
        const float PoleExtentX = 0.5f;
        const float PoleExtentZ = 0.5f;

        // ---------------------------------------------------------------- grab

        [Test]
        public void StickDistance_UsesLargerPoleExtent()
        {
            PoleClimbParams p = Live();
            float d = PoleClimbMath.StickDistance(0.3f, 0.7f, BodyRadius, in p);
            Assert.That(d, Is.EqualTo(0.7f + BodyRadius + p.ClimbSurfacePadding).Within(1e-4f));
        }

        [Test]
        public void ClimbHugStickDistance_DefaultHugMatchesStickDistance()
        {
            // The shipped default hug (0.55) is radius + padding, so adding the IK cling changes nothing
            // until it is tuned down.
            PoleClimbParams p = Live();
            float stick = PoleClimbMath.StickDistance(PoleExtentX, PoleExtentZ, BodyRadius, in p);
            float hug = PoleClimbMath.ClimbHugStickDistance(PoleExtentX, PoleExtentZ, BodyRadius + p.ClimbSurfacePadding);
            Assert.That(hug, Is.EqualTo(stick).Within(1e-4f));
        }

        [Test]
        public void ClimbHugStickDistance_UsesLargerPoleExtentAndNeverSinksIntoThePole()
        {
            Assert.That(PoleClimbMath.ClimbHugStickDistance(0.3f, 0.7f, 0.1f), Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(PoleClimbMath.ClimbHugStickDistance(0.3f, 0.7f, -0.5f), Is.EqualTo(0.7f).Within(1e-4f));
        }

        [Test]
        public void CanGrab_WithinReach_IsTrue()
        {
            PoleClimbParams p = Live();
            float stick = PoleClimbMath.StickDistance(PoleExtentX, PoleExtentZ, BodyRadius, in p);
            Assert.IsTrue(PoleClimbMath.CanGrab(positionY: 1f, poleTopY: 40f, flatDistanceFromAxis: stick, stickDistance: stick));
        }

        [Test]
        public void CanGrab_BeyondReach_IsFalse()
        {
            PoleClimbParams p = Live();
            float stick = PoleClimbMath.StickDistance(PoleExtentX, PoleExtentZ, BodyRadius, in p);
            Assert.IsFalse(PoleClimbMath.CanGrab(positionY: 1f, poleTopY: 40f, flatDistanceFromAxis: stick + 0.31f, stickDistance: stick));
        }

        [Test]
        public void CanGrab_AtOrAbovePoleTop_IsFalse()
        {
            // Skip the pole if pos.y >= pole.bounds.max.y - 0.05.
            PoleClimbParams p = Live();
            float stick = PoleClimbMath.StickDistance(PoleExtentX, PoleExtentZ, BodyRadius, in p);
            Assert.IsFalse(PoleClimbMath.CanGrab(positionY: 39.96f, poleTopY: 40f, flatDistanceFromAxis: 0f, stickDistance: stick));
        }

        // ---------------------------------------------------------------- orbit

        [Test]
        public void RotateOffset_PositiveInput_RotatesCounterClockwiseAroundUp()
        {
            PoleClimbParams p = Live();
            Vector3 result = PoleClimbMath.RotateOffset(Vector3.forward, orbitInput: 1f, in p, deltaTime: 1f / p.ClimbOrbitSpeed);
            // One degree of rotation from (0,0,1) should gain a small positive X and stay near unit length.
            Assert.That(result.magnitude, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(result.x, Is.GreaterThan(0f));
        }

        [Test]
        public void OrbitDirectionSign_Regression_B5_FlipsWithCameraSide()
        {
            // A world-relative A/D orbit would flip when the camera swings to the pole's far side.
            // The sign must come from camera-right vs. the orbit tangent, so the same raw input produces
            // the opposite world rotation once the camera has moved to the opposite side.
            Vector3 offsetDir = Vector3.forward; // player is on the +Z side of the pole

            float signFromNear = PoleClimbMath.OrbitDirectionSign(cameraRight: Vector3.right, offsetDir: offsetDir);
            float signFromFar = PoleClimbMath.OrbitDirectionSign(cameraRight: -Vector3.right, offsetDir: offsetDir);

            Assert.That(signFromFar, Is.EqualTo(-signFromNear));
        }

        // ---------------------------------------------------------------- climb pose

        [Test]
        public void ClampClimbY_ClampsToJustAbovePoleTop()
        {
            PoleClimbParams p = Live();
            float bodyHalf = PoleClimbMath.BodyHalf(1f);
            float y = PoleClimbMath.ClampClimbY(39.9f, verticalInput: 1f, in p, deltaTime: 10f, poleBottomY: 0f, poleTopY: 40f, bodyHalf: bodyHalf);
            Assert.That(y, Is.EqualTo(40f + 0.1f * bodyHalf).Within(1e-4f));
        }

        [Test]
        public void CanGrab_Regression_ABodyTouchingThePoleIsAlwaysWithinReach()
        {
            // Measured from the Fox's root, 0.575 m behind its capsule, walking straight into a pole would
            // stop 7.5 cm short of grabbing. Measured from the body point, a capsule
            // touching the pole sits at poleRadius + bodyRadius from the axis, and that must be grabbable.
            PoleClimbParams p = Live();
            float poleRadius = 0.5f, bodyRadius = 0.35f;
            float stick = PoleClimbMath.StickDistance(poleRadius, poleRadius, bodyRadius, in p);
            Assert.IsTrue(PoleClimbMath.CanGrab(0f, 40f, poleRadius + bodyRadius, stick));
        }

        [Test]
        public void ClimbBottomY_Regression_SunkPoleStopsAtTheGround()
        {
            // A measured case: pole collider bottom 16.34, ground surface 17.08.
            float bottom = PoleClimbMath.ClimbBottomY(16.34f, groundFound: true, groundY: 17.08f, bodyReachBelow: 0f);
            Assert.That(bottom, Is.EqualTo(17.08f).Within(1e-4f));
        }

        [Test]
        public void ClimbBottomY_PoleStandingAboveTheGround_UsesThePoleBottom()
        {
            // e.g. a pole standing on a raised platform: the ground far below must not extend the climb into thin air
            float bottom = PoleClimbMath.ClimbBottomY(5f, groundFound: true, groundY: 0f, bodyReachBelow: 0f);
            Assert.That(bottom, Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void ClimbBottomY_NoGroundFound_FallsBackToThePoleBottom()
        {
            float bottom = PoleClimbMath.ClimbBottomY(16.34f, groundFound: false, groundY: 999f, bodyReachBelow: 0f);
            Assert.That(bottom, Is.EqualTo(16.34f).Within(1e-4f));
        }

        [Test]
        public void ClimbBottomY_LiftsByTheBodyReach_ButNeverLowersForANegativeOne()
        {
            Assert.That(PoleClimbMath.ClimbBottomY(0f, true, 1f, bodyReachBelow: 0.575f), Is.EqualTo(1.575f).Within(1e-4f));
            Assert.That(PoleClimbMath.ClimbBottomY(0f, true, 1f, bodyReachBelow: -2f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ClampClimbY_ClampsToJustAbovePoleBottom()
        {
            PoleClimbParams p = Live();
            float bodyHalf = PoleClimbMath.BodyHalf(1f);
            float y = PoleClimbMath.ClampClimbY(0.1f, verticalInput: -1f, in p, deltaTime: 10f, poleBottomY: 0f, poleTopY: 40f, bodyHalf: bodyHalf);
            Assert.That(y, Is.EqualTo(0.15f * bodyHalf).Within(1e-4f));
        }

        [Test]
        public void FacingYawFromOffset_FacesAwayFromThePole()
        {
            // Standing on the +Z side (offset = +Z), the root should face +Z (yaw 0).
            Assert.That(PoleClimbMath.FacingYawFromOffset(Vector3.forward), Is.EqualTo(0f).Within(1e-3f));
            // Standing on the +X side (offset = +X), the root should face +X (yaw 90).
            Assert.That(PoleClimbMath.FacingYawFromOffset(Vector3.right), Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void ClimbRotation_ForwardPointsUpThePole_UpPointsAwayFromIt()
        {
            Quaternion rot = PoleClimbMath.ClimbRotation(Vector3.forward, facingSign: 1f);

            Vector3 forward = rot * Vector3.forward;
            Vector3 up = rot * Vector3.up;

            Assert.That(Vector3.Distance(forward, Vector3.up), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(up, Vector3.forward), Is.LessThan(1e-4f));
        }

        [Test]
        public void ClimbRotation_NegativeFacingSign_ForwardPointsDownThePole()
        {
            // Climbing down is head-first - the same belly-to-pole hug, facing the other way.
            Quaternion rot = PoleClimbMath.ClimbRotation(Vector3.forward, facingSign: -1f);

            Vector3 forward = rot * Vector3.forward;
            Vector3 up = rot * Vector3.up;

            Assert.That(Vector3.Distance(forward, Vector3.down), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(up, Vector3.forward), Is.LessThan(1e-4f));
        }

        [Test]
        public void ClimbRotation_DegenerateOffset_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => PoleClimbMath.ClimbRotation(Vector3.zero, facingSign: 1f));
        }

        [Test]
        public void ClimbFacingSign_HoldsPreviousWhenInputInDeadzone()
        {
            Assert.That(PoleClimbMath.ClimbFacingSign(previousSign: 1f, verticalInput: 0.05f), Is.EqualTo(1f));
            Assert.That(PoleClimbMath.ClimbFacingSign(previousSign: -1f, verticalInput: -0.05f), Is.EqualTo(-1f));
        }

        [Test]
        public void ClimbFacingSign_FlipsOnDeliberateOppositeInput()
        {
            Assert.That(PoleClimbMath.ClimbFacingSign(previousSign: 1f, verticalInput: -1f), Is.EqualTo(-1f));
            Assert.That(PoleClimbMath.ClimbFacingSign(previousSign: -1f, verticalInput: 1f), Is.EqualTo(1f));
        }

        [Test]
        public void RootPositionForBodyPoint_IdentityRotation_SubtractsOffsetDirectly()
        {
            Vector3 result = PoleClimbMath.RootPositionForBodyPoint(
                desiredBodyPoint: new Vector3(1f, 2f, 3f),
                rotation: Quaternion.identity,
                bodyOffset: new Vector3(0f, 0.4f, 0.575f));

            Assert.That(Vector3.Distance(result, new Vector3(1f, 1.6f, 2.425f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void RootPositionForBodyPoint_RoundTripsThroughTheRotation()
        {
            // The defining property, under the actual climb rotation: root + rotation*offset must land
            // exactly back on the desired body point, whatever the rotation is - this is what fixes the
            // climbing gap (the body, not the invisible root, ends up at the intended distance).
            Quaternion rotation = PoleClimbMath.ClimbRotation(Vector3.forward, facingSign: 1f);
            Vector3 bodyOffset = new Vector3(0f, 0.4f, 0.575f);
            Vector3 desiredBodyPoint = new Vector3(2f, 5f, -1f);

            Vector3 root = PoleClimbMath.RootPositionForBodyPoint(desiredBodyPoint, rotation, bodyOffset);
            Vector3 reconstructedBodyPoint = root + rotation * bodyOffset;

            Assert.That(Vector3.Distance(reconstructedBodyPoint, desiredBodyPoint), Is.LessThan(1e-4f));
        }

        // ---------------------------------------------------------------- perch

        [Test]
        public void ShouldPerch_NearTop_IsTrue()
        {
            float bodyHalf = 0.5f;
            Assert.IsTrue(PoleClimbMath.ShouldPerch(y: 39.95f, poleTopY: 40f, bodyHalf: bodyHalf));
        }

        [Test]
        public void ShouldPerch_BelowTop_IsFalse()
        {
            float bodyHalf = 0.5f;
            Assert.IsFalse(PoleClimbMath.ShouldPerch(y: 30f, poleTopY: 40f, bodyHalf: bodyHalf));
        }

        [Test]
        public void PerchTargetYaw_SnapsToNearestStep()
        {
            PoleClimbParams p = Live(); // 8 steps = 45 degrees apart
            Assert.That(PoleClimbMath.PerchTargetYaw(50f, in p), Is.EqualTo(45f).Within(1e-3f));
            Assert.That(PoleClimbMath.PerchTargetYaw(100f, in p), Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void SmoothYawTowards_SnapsWithinTwoDegrees()
        {
            Assert.That(PoleClimbMath.SmoothYawTowards(44f, 45f, turnSpeed: 14f, deltaTime: 0.001f), Is.EqualTo(45f));
        }

        [Test]
        public void SmoothYawTowards_FarAway_MovesTowardTarget()
        {
            float result = PoleClimbMath.SmoothYawTowards(0f, 90f, turnSpeed: 14f, deltaTime: 0.1f);
            Assert.That(result, Is.GreaterThan(0f));
            Assert.That(result, Is.LessThan(90f));
        }

        // ---------------------------------------------------------------- demount

        [Test]
        public void ShouldDemount_NearBottom_IsTrue()
        {
            PoleClimbParams p = Live();
            float bodyHalf = 0.5f;
            Assert.IsTrue(PoleClimbMath.ShouldDemount(y: 0.6f, in p, poleBottomY: 0f, bodyHalf: bodyHalf));
        }

        [Test]
        public void ShouldDemount_HighUp_IsFalse()
        {
            PoleClimbParams p = Live();
            float bodyHalf = 0.5f;
            Assert.IsFalse(PoleClimbMath.ShouldDemount(y: 20f, in p, poleBottomY: 0f, bodyHalf: bodyHalf));
        }

        // ---------------------------------------------------------------- jump-off

        [Test]
        public void ResolveOutward_Regression_B1_NeverDegenerates()
        {
            // A fallback for a zero flattened-forward vector can silently never run and send every jump
            // to world +Z. ResolveOutward always has a real, non-degenerate input
            // (the current offset from the axis) so there is nothing to fall back from.
            Assert.That(PoleClimbMath.ResolveOutward(new Vector3(0.6f, 0f, 0.8f)).magnitude, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(PoleClimbMath.ResolveOutward(Vector3.zero), Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void JumpOffHorizontalDirection_Regression_B6_UsesOutwardWhenCameraFacesThePole()
        {
            // Jumping off while looking at the pole must not launch straight into it.
            Vector3 outward = Vector3.forward; // player is on the +Z side
            Vector3 cameraLookingAtPole = Vector3.back; // camera faces -Z, i.e. toward the pole

            Vector3 result = PoleClimbMath.JumpOffHorizontalDirection(cameraLookingAtPole, outward);

            Assert.That(result, Is.EqualTo(outward));
        }

        [Test]
        public void JumpOffHorizontalDirection_CameraFacingAway_UsesCameraForward()
        {
            Vector3 outward = Vector3.forward;
            Vector3 cameraLookingAway = Vector3.forward;

            Vector3 result = PoleClimbMath.JumpOffHorizontalDirection(cameraLookingAway, outward);

            Assert.That(result, Is.EqualTo(cameraLookingAway));
        }

        [Test]
        public void JumpOffVelocity_MatchesLiveTuning()
        {
            PoleClimbParams p = Live();
            Vector3 v = PoleClimbMath.JumpOffVelocity(Vector3.forward, Vector3.forward, in p);

            Assert.That(v.z, Is.EqualTo(p.PoleJumpOutSpeed).Within(1e-3f));
            Assert.That(v.y, Is.EqualTo(p.PoleJumpUpSpeed).Within(1e-3f));
        }

        [Test]
        public void PerchPosition_SitsOnTheAxisAboveTheTop()
        {
            // Used by climbing to the top and by riding a wire into a pole.
            PoleClimbParams p = Live();
            Vector3 pos = PoleClimbMath.PerchPosition(new Vector3(2f, 0f, 3f), 5f, in p, 0.5f);
            Assert.That(pos.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(pos.z, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(pos.y, Is.EqualTo(5f + p.PerchTopPadding + 0.15f * 0.5f).Within(1e-4f));
        }

        [Test]
        public void ClimbPosition_StandsOffTheAxisAtTheGivenHeight()
        {
            Vector3 pos = PoleClimbMath.ClimbPosition(new Vector3(1f, 0f, 1f), new Vector3(0f, 0f, 2f), 0.9f, 4f);
            Assert.That(pos.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(pos.z, Is.EqualTo(1.9f).Within(1e-4f));
            Assert.That(pos.y, Is.EqualTo(4f).Within(1e-4f));
        }

        [Test]
        public void DemountStepOutPoint_ClearsTheFootprint()
        {
            PoleClimbParams p = Live();
            float stick = PoleClimbMath.StickDistance(PoleExtentX, PoleExtentZ, BodyRadius, in p);
            Vector3 point = PoleClimbMath.DemountStepOutPoint(Vector3.zero, Vector3.right, stick, BodyRadius);
            Assert.That(point.x, Is.GreaterThan(PoleExtentX + BodyRadius), "stepped out clear of the pole and the body");
            Assert.That(point.z, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void PerchJumpVelocity_StraightDownFacingFallsBackToWorldForward()
        {
            Vector3 v = PoleClimbMath.PerchJumpVelocity(Vector3.down, 10f, 12f);
            Assert.That(v.z, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(v.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(v.y, Is.EqualTo(12f).Within(1e-3f));
        }

        [Test]
        public void Regression_PerchJumpGoesWhereThePlayerFaces()
        {
            // The perch leap follows the facing, flattened - not the mid-climb kick-off, which starts from the
            // side of the pole last climbed and could throw the player backwards.
            Vector3 facingLeftAndSlightlyDown = new Vector3(-1f, -0.3f, 0f);
            Vector3 v = PoleClimbMath.PerchJumpVelocity(facingLeftAndSlightlyDown, 10f, 12f);

            Assert.That(v.x, Is.EqualTo(-10f).Within(1e-3f));
            Assert.That(v.z, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(v.y, Is.EqualTo(12f).Within(1e-3f));
        }

        [Test]
        public void JumpOffPosition_PlacesOutsidePoleFootprint()
        {
            PoleClimbParams p = Live();
            float stick = PoleClimbMath.StickDistance(PoleExtentX, PoleExtentZ, BodyRadius, in p);
            Vector3 pos = PoleClimbMath.JumpOffPosition(Vector3.zero, Vector3.forward, stick, BodyRadius, y: 5f);

            float expectedDistance = Mathf.Max(stick, BodyRadius) + BodyRadius + 0.35f;
            Assert.That(pos.z, Is.EqualTo(expectedDistance).Within(1e-4f));
            Assert.That(pos.y, Is.EqualTo(5.15f).Within(1e-4f));
        }
    }
}
