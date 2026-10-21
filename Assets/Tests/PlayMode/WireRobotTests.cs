using System.Collections;
using NUnit.Framework;
using MovementSandbox.Identity;
using MovementSandbox.Mechanics;
using UnityEngine;
using UnityEngine.TestTools;

namespace MovementSandbox.Tests
{
    /// <summary>
    /// Riding and leaving wires, played by a robot. Every test starts on Wire_01, which runs 120 m from
    /// Pole_01 to Pole_03 with no other wire at the far end.
    /// </summary>
    public class WireRobotTests : RobotTestFixture
    {
        /// <summary>Well under the slingshot's minimum launch stretch at its pull speed.</summary>
        const float WeakPullSeconds = 0.1f;

        [UnityTest]
        public IEnumerator RidingToTheFarEndOfAWirePerchesOnThePoleThere()
        {
            yield return LoadTestScene();
            yield return RideOutOntoAWire();

            var climber = Player.GetComponent<PoleClimber>();
            yield return HoldUntil(Keyboard.wKey, () => Player.Current == PlayerMode.Perched, 12f,
                "the player to ride Wire_01 to its far end and perch");
            Assert.That(climber.CurrentPole.name, Is.EqualTo("Pole_03"),
                "The ride along Wire_01 should end perched on Pole_03, the pole at its far end.");
        }

        [UnityTest]
        public IEnumerator JumpingOnAWireDropsThePlayerToTheGround()
        {
            yield return LoadTestScene();
            yield return RideOutOntoAWire();

            yield return Tap(Keyboard.spaceKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.Launched, 1f,
                "Space on the wire to drop the player off it");
            yield return WaitUntil(() => Player.Current == PlayerMode.Walking, 8f,
                "the dropped player to land and walk");
        }

        [UnityTest]
        public IEnumerator AWeakSlingshotPullSnapsBackOntoTheWire()
        {
            yield return LoadTestScene();
            yield return RideOutOntoAWire();

            Press(Keyboard.xKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.SlingshotCharging, 1f,
                "holding X on the wire to start a slingshot pull");
            yield return new WaitForSeconds(WeakPullSeconds);
            Release(Keyboard.xKey);

            yield return WaitUntil(() => Player.Current == PlayerMode.WireRiding, 1f,
                "a pull too weak to launch to put the player back on the wire");
        }

        [UnityTest]
        public IEnumerator JumpingDuringASlingshotPullCancelsItAndTheWireSpringsBack()
        {
            yield return LoadTestScene();
            yield return RideOutOntoAWire();
            var wire = GameObject.Find("Wire_01").GetComponent<BendableWire>();
            Assert.That(wire, Is.Not.Null, "Wire_01 should be bendable.");

            Press(Keyboard.xKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.SlingshotCharging, 1f,
                "holding X on the wire to start a slingshot pull");
            yield return new WaitForSeconds(1f);
            float pulledBend = MaxBend(wire);
            Assert.That(pulledBend, Is.GreaterThan(0.5f), "A one-second pull should visibly bend the wire.");

            // Space while still holding X: a cancel beats a release.
            yield return Tap(Keyboard.spaceKey);
            Release(Keyboard.xKey);
            yield return WaitUntil(() => Player.Current == PlayerMode.Launched, 1f,
                "Space during the pull to cancel it and drop the player");

            yield return WaitUntil(() => MaxBend(wire) < 0.05f, 5f,
                $"Wire_01 to spring back straight after the cancel (it was bent {pulledBend:F2} m)");
        }

        /// <summary>The wire's largest displacement from its rest shape, sampled along its length.</summary>
        static float MaxBend(BendableWire wire)
        {
            float max = 0f;
            for (int i = 0; i <= 50; i++)
                max = Mathf.Max(max, wire.Offset(i / 50f).magnitude);
            return max;
        }
    }
}
