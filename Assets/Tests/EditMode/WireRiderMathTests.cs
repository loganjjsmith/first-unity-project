using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the wire-ride, mount and detach formulas, and in particular the failure mode
    /// described on <see cref="WireRiderMath"/>.
    /// </summary>
    public class WireRiderMathTests
    {
        static WireRiderParams Live()
        {
            return new WireRiderParams(
                wireMoveSpeed: 16f,
                wirePerchGrabRange: 11f,
                wireAirGrabRange: 4.4f,
                wireReentryLockDuration: 1.25f,
                wireJumpOffSpeed: 3f,
                wireJumpOffUpSpeed: 10f,
                slingshotWireLockDuration: 1.75f);
        }

        [Test]
        public void RidePoint_OffsetsStraightUp()
        {
            Vector3 result = WireRiderMath.RidePoint(new Vector3(1f, 2f, 3f), rideOffset: 0.7f);
            Assert.That(Vector3.Distance(result, new Vector3(1f, 2.7f, 3f)), Is.LessThan(1e-4f));
        }

        // ---------------------------------------------------------------- mount

        [Test]
        public void MountFacingSign_MatchesCameraAlongTheTangent()
        {
            Assert.That(WireRiderMath.MountFacingSign(Vector3.forward, Vector3.forward), Is.EqualTo(1f));
            Assert.That(WireRiderMath.MountFacingSign(Vector3.forward, Vector3.back), Is.EqualTo(-1f));
        }

        // ---------------------------------------------------------------- travel

        [Test]
        public void TravelDirection_NoInput_IsZero()
        {
            Assert.That(WireRiderMath.TravelDirection(Vector3.forward, Vector3.forward, verticalInput: 0f, previousFacingSign: 1f), Is.EqualTo(0f));
        }

        [Test]
        public void TravelDirection_CameraAlignedWithTangent_FollowsInputSign()
        {
            // Camera looks straight along the wire's +t tangent: W (positive input) should go +t.
            float forward = WireRiderMath.TravelDirection(Vector3.forward, Vector3.forward, verticalInput: 1f, previousFacingSign: 1f);
            float backward = WireRiderMath.TravelDirection(Vector3.forward, Vector3.forward, verticalInput: -1f, previousFacingSign: 1f);

            Assert.That(forward, Is.EqualTo(1f));
            Assert.That(backward, Is.EqualTo(-1f));
        }

        [Test]
        public void TravelDirection_CameraReversed_FlipsWhichWayWGoes()
        {
            // A 180-degree camera swing should flip which way W travels.
            float withCameraOneWay = WireRiderMath.TravelDirection(Vector3.forward, Vector3.forward, verticalInput: 1f, previousFacingSign: 1f);
            float withCameraReversed = WireRiderMath.TravelDirection(Vector3.back, Vector3.forward, verticalInput: 1f, previousFacingSign: 1f);

            Assert.That(withCameraReversed, Is.EqualTo(-withCameraOneWay));
        }

        [Test]
        public void Regression_W1_PerpendicularCameraStillResolvesFullDirection()
        {
            // Looking almost exactly perpendicular to the wire must not make travel crawl. Here the
            // camera is exactly perpendicular to the tangent (dot = 0), so travel must fall back to the
            // committed facing sign rather than returning some fractional/near-zero value.
            Vector3 tangent = Vector3.forward;
            Vector3 perpendicularCamera = Vector3.right;

            float result = WireRiderMath.TravelDirection(perpendicularCamera, tangent, verticalInput: 1f, previousFacingSign: 1f);
            Assert.That(result, Is.EqualTo(1f));

            float resultOtherFacing = WireRiderMath.TravelDirection(perpendicularCamera, tangent, verticalInput: 1f, previousFacingSign: -1f);
            Assert.That(resultOtherFacing, Is.EqualTo(-1f));
        }

        [Test]
        public void TravelDirection_ResultIsAlwaysFullMagnitude()
        {
            // No partial-speed values anywhere in the input space - direction only, never a ramp.
            foreach (float cameraAngle in new[] { 0f, 10f, 45f, 80f, 89f, 91f, 135f, 179f })
            {
                Vector3 tangent = Vector3.forward;
                Vector3 camera = Quaternion.Euler(0f, cameraAngle, 0f) * Vector3.forward;

                float result = WireRiderMath.TravelDirection(camera, tangent, verticalInput: 1f, previousFacingSign: 1f);
                Assert.That(Mathf.Abs(result), Is.EqualTo(1f), $"angle {cameraAngle}");
            }
        }

        // ---------------------------------------------------------------- end reached

        [Test]
        public void ReachedEnd_MovingPastOne_IsTrue()
        {
            Assert.IsTrue(WireRiderMath.ReachedEnd(t: 0.9995f, direction: 1f));
        }

        [Test]
        public void ReachedEnd_MovingPastZero_IsTrue()
        {
            Assert.IsTrue(WireRiderMath.ReachedEnd(t: 0.0005f, direction: -1f));
        }

        [Test]
        public void ReachedEnd_AtOneButMovingBackward_IsFalse()
        {
            // At the end but moving back onto the wire - not an exit.
            Assert.IsFalse(WireRiderMath.ReachedEnd(t: 0.9995f, direction: -1f));
        }

        [Test]
        public void ReachedEnd_MidSpan_IsFalse()
        {
            Assert.IsFalse(WireRiderMath.ReachedEnd(t: 0.5f, direction: 1f));
        }

        [Test]
        public void ReachedEnd_NoDirection_IsFalse()
        {
            Assert.IsFalse(WireRiderMath.ReachedEnd(t: 0.9995f, direction: 0f));
        }

        // ---------------------------------------------------------------- detach

        [Test]
        public void DetachDirection_CameraAcrossTheWire_UsesCameraForward()
        {
            Vector3 tangent = Vector3.forward; // wire runs along Z
            Vector3 cameraAcross = Vector3.right; // looking along X, roughly perpendicular to the wire

            Vector3 result = WireRiderMath.DetachDirection(cameraAcross, cameraAcross, tangent);
            Assert.That(Vector3.Distance(result, Vector3.right), Is.LessThan(1e-4f));
        }

        [Test]
        public void DetachDirection_CameraAlongTheWire_FollowsTangent()
        {
            Vector3 tangent = Vector3.forward;
            Vector3 cameraAlong = Vector3.forward; // looking straight down the wire

            Vector3 result = WireRiderMath.DetachDirection(cameraAlong, cameraAlong, tangent);
            Assert.That(Vector3.Distance(result, Vector3.forward), Is.LessThan(1e-4f));
        }

        [Test]
        public void DetachVelocity_MatchesLiveTuning()
        {
            WireRiderParams p = Live();
            Vector3 v = WireRiderMath.DetachVelocity(Vector3.right, Vector3.right, Vector3.forward, Vector3.zero, in p);

            Assert.That(v.x, Is.EqualTo(p.WireJumpOffSpeed).Within(1e-3f));
            Assert.That(v.y, Is.EqualTo(p.WireJumpOffUpSpeed).Within(1e-3f));
        }

        [Test]
        public void Regression_DetachVelocity_KeepsTheRideMomentum()
        {
            // Jumping off a moving ride must carry the ride speed on top of the kick, or it reads as a
            // slight hop upwards then a drop.
            WireRiderParams p = Live();
            Vector3 ride = Vector3.forward * p.WireMoveSpeed;
            Vector3 v = WireRiderMath.DetachVelocity(Vector3.right, Vector3.right, Vector3.forward, ride, in p);

            Assert.That(v.z, Is.EqualTo(p.WireMoveSpeed).Within(1e-3f));
            Assert.That(v.x, Is.EqualTo(p.WireJumpOffSpeed).Within(1e-3f));
            Assert.That(v.y, Is.EqualTo(p.WireJumpOffUpSpeed).Within(1e-3f));
        }

        // ---------------------------------------------------------------- poles and junctions

        const float MinCameraDot = 0.5f;
        const float Margin = 0.2f;

        [Test]
        public void ChooseContinuation_NoWayOnPerches()
        {
            Assert.That(WireRiderMath.ChooseContinuation(Vector3.forward, Vector3.forward, new List<Vector3>(), MinCameraDot, Margin),
                Is.EqualTo(-1));
        }

        [Test]
        public void ChooseContinuation_OneWayOnKeepsRidingWhateverTheCamera()
        {
            // A midway pole, or two wires meeting: ride through even if the camera is looking sideways.
            List<Vector3> ahead = new List<Vector3> { new Vector3(0.3f, 0f, 1f) };
            Assert.That(WireRiderMath.ChooseContinuation(Vector3.forward, Vector3.right, ahead, MinCameraDot, Margin),
                Is.EqualTo(0));
        }

        [Test]
        public void ChooseContinuation_OneWayOnThatDoublesBackPerches()
        {
            // A V-shaped pair of wires: continuing would turn more than 90 degrees, so stop instead.
            List<Vector3> back = new List<Vector3> { new Vector3(0.2f, 0f, -1f) };
            Assert.That(WireRiderMath.ChooseContinuation(Vector3.forward, Vector3.forward, back, MinCameraDot, Margin),
                Is.EqualTo(-1));
        }

        [Test]
        public void ChooseContinuation_ASplitFollowsTheCamera()
        {
            List<Vector3> split = new List<Vector3> { Vector3.forward, Vector3.left, Vector3.right };
            Assert.That(WireRiderMath.ChooseContinuation(Vector3.forward, Vector3.right, split, MinCameraDot, Margin),
                Is.EqualTo(2));
            Assert.That(WireRiderMath.ChooseContinuation(Vector3.forward, Vector3.forward, split, MinCameraDot, Margin),
                Is.EqualTo(0));
        }

        [Test]
        public void ChooseContinuation_LookingAtTheDeadEndOfATPerches()
        {
            // A T where the camera faces the dead end, so no branch is a clear choice.
            List<Vector3> tee = new List<Vector3> { Vector3.left, Vector3.right };
            Assert.That(WireRiderMath.ChooseContinuation(Vector3.forward, Vector3.forward, tee, MinCameraDot, Margin),
                Is.EqualTo(-1));
        }

        [Test]
        public void ChooseContinuation_LookingBetweenTwoBranchesPerches()
        {
            List<Vector3> fork = new List<Vector3> { new Vector3(-0.5f, 0f, 1f), new Vector3(0.5f, 0f, 1f) };
            Assert.That(WireRiderMath.ChooseContinuation(Vector3.forward, Vector3.forward, fork, MinCameraDot, Margin),
                Is.EqualTo(-1));
        }

        [Test]
        public void BestAlignedIndex_IgnoresHeight()
        {
            // Wires slope up and down; only the heading should decide.
            List<Vector3> options = new List<Vector3> { new Vector3(0f, 5f, 1f), new Vector3(1f, 0f, 0f) };
            Assert.That(WireRiderMath.BestAlignedIndex(Vector3.forward, options, 0.9f, 0f), Is.EqualTo(0));
        }

        [Test]
        public void Regression_PassesStop_PushingIntoAnEndPoleReachesIt()
        {
            // A rider already at an end knot on a pole (mounted there from the perch, or
            // air-grabbed right beside it) can never "cross" that stop because the spline clamps t, so it would
            // hop off instead of perching. Pushing into the end counts as reaching the stop.
            Assert.IsTrue(WireRiderMath.PassesStop(0f, 0f, 0f, -1f));
            Assert.IsTrue(WireRiderMath.PassesStop(1f, 1f, 1f, 1f));
        }

        [Test]
        public void PassesStop_RidingAwayFromAPoleDoesNotReArriveAtIt()
        {
            // After a perch mount or a junction exit the rider starts on the stop and moves off it.
            Assert.IsFalse(WireRiderMath.PassesStop(0f, 0f, 0.02f, 1f));
            Assert.IsFalse(WireRiderMath.PassesStop(1f, 1f, 0.98f, -1f));
            // A mid-wire stop at the start of the move is still excluded, whichever way the rider goes.
            Assert.IsFalse(WireRiderMath.PassesStop(0.5f, 0.5f, 0.49f, -1f));
            Assert.IsFalse(WireRiderMath.PassesStop(0.5f, 0.5f, 0.51f, 1f));
        }

        [Test]
        public void PassesStop_NormalCrossingsAndNonCrossings()
        {
            Assert.IsTrue(WireRiderMath.PassesStop(0.5f, 0.45f, 0.52f, 1f));
            Assert.IsTrue(WireRiderMath.PassesStop(0f, 0.01f, 0f, -1f));
            Assert.IsFalse(WireRiderMath.PassesStop(0.5f, 0.52f, 0.6f, 1f), "stop is behind the move");
            Assert.IsFalse(WireRiderMath.PassesStop(0.5f, 0.3f, 0.4f, 1f), "stop is beyond this frame's move");
            Assert.IsFalse(WireRiderMath.PassesStop(0.5f, 0.45f, 0.52f, 0f), "no direction, no crossing");
        }

        [Test]
        public void BestAlignedIndex_PerchMountTurnsBack()
        {
            // The perch-mount configuration (min dot 0, no margin): "turn back" is just looking back down the wire.
            List<Vector3> options = new List<Vector3> { Vector3.forward, Vector3.back };
            Assert.That(WireRiderMath.BestAlignedIndex(Vector3.back, options, 0f, 0f), Is.EqualTo(1));
        }

        [Test]
        public void BestAlignedIndex_PerchMountIgnoresAWireBehindTheCamera()
        {
            // Min dot 0 means "anywhere in the front half", so a wire straight behind is not taken.
            List<Vector3> options = new List<Vector3> { Vector3.forward };
            Assert.That(WireRiderMath.BestAlignedIndex(Vector3.back, options, 0f, 0f), Is.EqualTo(-1));
        }

        [Test]
        public void BestAlignedIndex_SkipsVerticalOptions()
        {
            List<Vector3> options = new List<Vector3> { Vector3.up, Vector3.right };
            Assert.That(WireRiderMath.BestAlignedIndex(Vector3.right, options, 0.5f, 0.2f), Is.EqualTo(1));
        }

        [Test]
        public void BestAlignedIndex_VerticalReferencePicksNothing()
        {
            // Defensive: CameraBasis.PlanarForward never returns zero in play, but a straight-down reference
            // has no heading, so nothing can be "aligned" with it.
            List<Vector3> options = new List<Vector3> { Vector3.forward };
            Assert.That(WireRiderMath.BestAlignedIndex(Vector3.down, options, 0f, 0f), Is.EqualTo(-1));
        }

        [Test]
        public void ShouldPerchOnHalt_OnlyOnTheFrameTravelStops()
        {
            Assert.IsTrue(WireRiderMath.ShouldPerchOnHalt(true, false, 1f, 1.5f));
            Assert.IsFalse(WireRiderMath.ShouldPerchOnHalt(false, false, 1f, 1.5f), "idle after mounting from a perch");
            Assert.IsFalse(WireRiderMath.ShouldPerchOnHalt(true, true, 1f, 1.5f), "still moving");
            Assert.IsFalse(WireRiderMath.ShouldPerchOnHalt(true, false, 2f, 1.5f), "too far from the pole");
        }

        // ---------------------------------------------------------------- lock durations

        [Test]
        public void DetachLockDuration_MatchesLiveTuning()
        {
            // max(1.25, 1.75 * 0.85) = 1.4875.
            WireRiderParams p = Live();
            Assert.That(WireRiderMath.DetachLockDuration(in p), Is.EqualTo(1.4875f).Within(1e-4f));
        }

        [Test]
        public void DetachLockDuration_NeverGoesBelowTheBaseReentryLock()
        {
            WireRiderParams p = new WireRiderParams(
                wireMoveSpeed: 16f, wirePerchGrabRange: 11f, wireAirGrabRange: 4.4f,
                wireReentryLockDuration: 5f, wireJumpOffSpeed: 3f, wireJumpOffUpSpeed: 10f,
                slingshotWireLockDuration: 0.1f);

            Assert.That(WireRiderMath.DetachLockDuration(in p), Is.EqualTo(5f));
        }

        [Test]
        public void LandingLockTopUp_MatchesLiveTuning()
        {
            WireRiderParams p = Live();
            Assert.That(WireRiderMath.LandingLockTopUp(in p), Is.EqualTo(0.875f).Within(1e-4f));
        }

        // ---------------------------------------------------------------- facing

        [Test]
        public void FacingYawFromTangent_FacesAlongTheTangent()
        {
            Assert.That(WireRiderMath.FacingYawFromTangent(Vector3.forward, facingSign: 1f), Is.EqualTo(0f).Within(1e-3f));

            // 180 degrees, not compared with Is.EqualTo: atan2(-0, -1) legitimately returns -180 rather
            // than 180 (IEEE signed zero, from 0f * -1f) - the same facing direction, not a bug.
            // Mathf.DeltaAngle handles that wrap correctly.
            float backward = WireRiderMath.FacingYawFromTangent(Vector3.forward, facingSign: -1f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(backward, 180f)), Is.LessThan(1e-3f));
        }

        [Test]
        public void FacingYawFromTangent_DegenerateTangent_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => WireRiderMath.FacingYawFromTangent(Vector3.zero, facingSign: 1f));
        }
    }
}
