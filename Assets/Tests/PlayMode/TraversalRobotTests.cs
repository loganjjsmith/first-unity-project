using System.Collections;
using NUnit.Framework;
using MovementSandbox.Mechanics;
using UnityEngine;
using UnityEngine.TestTools;

namespace MovementSandbox.Tests
{
    /// <summary>
    /// The player's core traversal, played by a robot through real input. The spawn point faces Pole_01,
    /// and one of that pole's wires leaves it on the side the camera faces, so the whole route is
    /// "hold W, then press W again".
    /// </summary>
    public class TraversalRobotTests : RobotTestFixture
    {
        [UnityTest]
        public IEnumerator HoldingForwardWalksIntoThePoleAndClimbsToThePerch()
        {
            yield return LoadTestScene();
            float startHeight = PlayerPosition.y;

            Press(Keyboard.wKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.Climbing, 5f,
                "the player to walk into Pole_01 and grab it");
            yield return WaitUntil(() => Player.Current == PlayerMode.Perched, 10f,
                "the climb to reach the top of Pole_01");
            Release(Keyboard.wKey);

            Assert.That(PlayerPosition.y, Is.GreaterThan(startHeight + 10f),
                "The player is perched, but not much higher than where it started.");
        }

        [UnityTest]
        public IEnumerator PressingForwardOnThePerchMountsAWireAndRidesAlongIt()
        {
            yield return LoadTestScene();
            yield return RideOutOntoAWire();
        }

        [UnityTest]
        public IEnumerator JumpingMidClimbKicksThePlayerOffThePole()
        {
            yield return LoadTestScene();
            float startHeight = PlayerPosition.y;

            yield return HoldUntil(Keyboard.wKey,
                () => Player.Current == PlayerMode.Climbing && PlayerPosition.y > startHeight + 4f, 6f,
                "the player to climb 4 m up Pole_01");

            yield return Tap(Keyboard.spaceKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.Launched, 1f,
                "Space mid-climb to kick the player off the pole");
            yield return WaitUntil(() => Player.Current == PlayerMode.Walking, 6f,
                "the player to land after kicking off the pole");
        }

        [UnityTest]
        public IEnumerator JumpingFromThePerchLeapsOffIt()
        {
            yield return LoadTestScene();
            yield return HoldUntil(Keyboard.wKey, () => Player.Current == PlayerMode.Perched, 15f,
                "the player to climb Pole_01 to the perch");

            yield return Tap(Keyboard.spaceKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.Launched, 1f,
                "Space on the perch to leap off");

            // A 40 m drop past two wires: it may land, or catch one of them on the way down.
            yield return WaitUntil(() => Player.Current == PlayerMode.Walking
                                         || Player.Current == PlayerMode.WireRiding, 10f,
                "the leap to end on the ground or on a wire");
        }

        [UnityTest]
        public IEnumerator SprintingIsClearlyFasterThanWalking()
        {
            yield return LoadTestScene();

            // Backwards, away from Pole_01, which is only a few metres in front of the spawn point. Each
            // speed is measured after a moment to get up to it.
            float walkSpeed = 0f;
            float sprintSpeed = 0f;
            Press(Keyboard.sKey);
            yield return new WaitForSeconds(0.3f);
            yield return MeasureFlatSpeed(0.3f, speed => walkSpeed = speed);

            Press(Keyboard.leftShiftKey);
            yield return new WaitForSeconds(0.4f);
            yield return MeasureFlatSpeed(0.3f, speed => sprintSpeed = speed);
            Release(Keyboard.leftShiftKey);
            Release(Keyboard.sKey);

            Assert.That(walkSpeed, Is.GreaterThan(1f), "Holding S barely moved the player.");
            Assert.That(sprintSpeed, Is.GreaterThan(walkSpeed * 1.5f),
                $"Sprinting ({sprintSpeed:F1} m/s) is not clearly faster than walking ({walkSpeed:F1} m/s).");
        }

        [UnityTest]
        public IEnumerator ASprintingJumpOffTheAirGrabPlatformCatchesTheWire()
        {
            yield return LoadTestScene();

            // Dev key 1 puts the player on AirGrabPlatform, facing Wire_01 about 15 m away and a little
            // higher: out of reach from the platform, in reach of a sprinting jump.
            Vector3 platformSpot = GameObject.Find("AirGrabTeleport").transform.position;
            yield return Tap(Keyboard.digit1Key);
            yield return WaitUntil(() => Vector3.Distance(PlayerPosition, platformSpot) < 1f, 1f,
                "dev key 1 to teleport the player onto the air-grab platform");
            yield return WaitUntilReadyToJump();

            Press(Keyboard.leftShiftKey);
            Press(Keyboard.wKey);
            yield return new WaitForSeconds(0.15f);
            yield return Tap(Keyboard.spaceKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.WireRiding, 4f,
                "the player to catch Wire_01 in mid-air");
            Release(Keyboard.wKey);
            Release(Keyboard.leftShiftKey);
        }
    }
}
