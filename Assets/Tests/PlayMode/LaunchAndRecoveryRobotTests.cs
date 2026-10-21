using System.Collections;
using NUnit.Framework;
using MovementSandbox.Mechanics;
using UnityEngine;
using UnityEngine.TestTools;

namespace MovementSandbox.Tests
{
    /// <summary>
    /// The chaos half of the POC, played by a robot: slingshot launches of both causes, going limp, and
    /// respawning. Each flight must end with the player back in control, however it lands.
    /// </summary>
    public class LaunchAndRecoveryRobotTests : RobotTestFixture
    {
        /// <summary>Long enough to stretch well past the launch threshold, well short of a full pull.</summary>
        const float PullSeconds = 1.5f;

        /// <summary>The ragdoll's own give-up time plus its stun, with room to spare.</summary>
        const float RecoveryTimeout = 15f;

        [UnityTest]
        public IEnumerator AHazardSlingshotFlingsARagdollThatRecovers()
        {
            yield return LoadTestScene();
            yield return RideOutOntoAWire();

            yield return HoldFor(Keyboard.xKey, PullSeconds);
            yield return WaitUntil(() => Player.Current == PlayerMode.RagdollFlight, 1f,
                "a hazard pull (X) to fling the player into a ragdoll");

            yield return WaitUntil(() => Player.Current == PlayerMode.Walking, RecoveryTimeout,
                "the ragdoll to land, recover, shake off the stun and walk again");
        }

        [UnityTest]
        public IEnumerator APurposefulSlingshotLaunchesUnderControlAndLandsSomewhere()
        {
            yield return LoadTestScene();
            yield return RideOutOntoAWire();

            yield return HoldFor(Keyboard.cKey, PullSeconds);
            yield return WaitUntil(() => Player.Current == PlayerMode.Launched, 1f,
                "a purposeful pull (C) to launch the player");

            // A controlled flight may end on the ground, or by catching a pole or another wire mid-air.
            yield return WaitUntil(() => Player.Current == PlayerMode.Walking
                                         || Player.Current == PlayerMode.WireRiding
                                         || Player.Current == PlayerMode.Climbing
                                         || Player.Current == PlayerMode.Perched,
                RecoveryTimeout, "the launched player to land or catch something");
        }

        [UnityTest]
        public IEnumerator GoingLimpOnTheGroundRecoversToWalking()
        {
            yield return LoadTestScene();

            yield return Tap(Keyboard.zKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.Ragdoll, 1f,
                "the go-limp key (Z) to drop the player into a ragdoll");

            yield return WaitUntil(() => Player.Current == PlayerMode.Walking, RecoveryTimeout,
                "the limp player to recover and walk again");
        }

        [UnityTest]
        public IEnumerator RespawnReturnsThePlayerToTheSpawnPoint()
        {
            yield return LoadTestScene();
            Vector3 spawn = PlayerPosition;

            yield return HoldFor(Keyboard.sKey, 0.6f);
            Assert.That(Vector3.Distance(PlayerPosition, spawn), Is.GreaterThan(2f),
                "The player needs to have walked away from the spawn point for this test to mean anything.");

            // Respawn plays a death animation first, so the player only moves once that has finished.
            yield return Tap(Keyboard.rKey);
            yield return WaitUntil(() => !Player.IsRespawning && Vector3.Distance(PlayerPosition, spawn) < 1f,
                5f, "the respawn (R) to put the player back at the spawn point");
            Assert.That(Player.Current, Is.EqualTo(PlayerMode.Walking), "A respawned player should be walking.");
        }
    }
}
