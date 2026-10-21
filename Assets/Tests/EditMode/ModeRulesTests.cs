using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the transition table, plus the invariants that make a state machine
    /// worth having at all: no mode is a dead end, respawn always works, and a mode cannot be reached by a
    /// trigger the table does not list.
    /// </summary>
    public class ModeRulesTests
    {
        static PlayerMode[] AllModes => (PlayerMode[])Enum.GetValues(typeof(PlayerMode));
        static ModeTrigger[] AllTriggers => (ModeTrigger[])Enum.GetValues(typeof(ModeTrigger));

        // ---------------------------------------------------------------- invariants over the whole table

        [Test]
        public void RespawnWorksFromEveryMode()
        {
            // Including mid-flight and mid-stun: respawn must clear a stun, not be swallowed by it.
            foreach (PlayerMode mode in AllModes)
            {
                Assert.That(ModeRules.Next(mode, ModeTrigger.Respawn), Is.EqualTo(PlayerMode.Walking),
                    $"Respawn should work from {mode}.");
            }
        }

        [Test]
        public void EveryModeCanBeLeft()
        {
            // A mode with no way out is a soft lock. Respawn does not count, or this would be trivially true.
            foreach (PlayerMode mode in AllModes)
            {
                bool hasExit = false;
                foreach (ModeTrigger trigger in AllTriggers)
                {
                    if (trigger == ModeTrigger.Respawn)
                        continue;

                    PlayerMode? next = ModeRules.Next(mode, trigger);
                    if (next.HasValue && next.Value != mode)
                    {
                        hasExit = true;
                        break;
                    }
                }

                Assert.IsTrue(hasExit, $"{mode} has no exit other than respawn.");
            }
        }

        [Test]
        public void EveryModeIsReachable()
        {
            var reachable = new HashSet<PlayerMode> { PlayerMode.Walking };
            bool grew = true;

            while (grew)
            {
                grew = false;
                foreach (PlayerMode mode in new List<PlayerMode>(reachable))
                foreach (ModeTrigger trigger in AllTriggers)
                {
                    PlayerMode? next = ModeRules.Next(mode, trigger);
                    if (next.HasValue && reachable.Add(next.Value))
                        grew = true;
                }
            }

            foreach (PlayerMode mode in AllModes)
                Assert.IsTrue(reachable.Contains(mode), $"{mode} cannot be reached from Walking.");
        }

        [Test]
        public void NoTransitionReturnsAnUndefinedMode()
        {
            foreach (PlayerMode mode in AllModes)
            foreach (ModeTrigger trigger in AllTriggers)
            {
                PlayerMode? next = ModeRules.Next(mode, trigger);
                if (next.HasValue)
                    Assert.IsTrue(Enum.IsDefined(typeof(PlayerMode), next.Value));
            }
        }

        [Test]
        public void TransitionsAreDeterministic()
        {
            foreach (PlayerMode mode in AllModes)
            foreach (ModeTrigger trigger in AllTriggers)
            {
                Assert.That(ModeRules.Next(mode, trigger), Is.EqualTo(ModeRules.Next(mode, trigger)));
            }
        }

        // ---------------------------------------------------------------- the core loop

        [Test]
        public void ThePocLoopIsWalkable()
        {
            // walk -> climb -> perch -> wire -> charge -> flop -> recover -> walk.
            // This is the core loop, so if the table cannot express it, nothing else matters.
            PlayerMode mode = PlayerMode.Walking;

            mode = Step(mode, ModeTrigger.GrabPole, PlayerMode.Climbing);
            mode = Step(mode, ModeTrigger.ReachPoleTop, PlayerMode.Perched);
            mode = Step(mode, ModeTrigger.MountWireFromPerch, PlayerMode.WireRiding);
            mode = Step(mode, ModeTrigger.StartCharge, PlayerMode.SlingshotCharging);
            mode = Step(mode, ModeTrigger.ReleaseChargeStrong, PlayerMode.RagdollFlight);
            mode = Step(mode, ModeTrigger.Recover, PlayerMode.Stunned);
            Step(mode, ModeTrigger.StunExpired, PlayerMode.Walking);
        }

        static PlayerMode Step(PlayerMode from, ModeTrigger trigger, PlayerMode expected)
        {
            PlayerMode? next = ModeRules.Next(from, trigger);
            Assert.IsTrue(next.HasValue, $"{trigger} should be legal in {from}.");
            Assert.That(next.Value, Is.EqualTo(expected));
            return next.Value;
        }

        // ---------------------------------------------------------------- specific rows

        [Test]
        public void WeakReleaseGoesBackToTheWire()
        {
            Assert.That(ModeRules.Next(PlayerMode.SlingshotCharging, ModeTrigger.ReleaseChargeWeak),
                Is.EqualTo(PlayerMode.WireRiding));
        }

        [Test]
        public void JumpCancelsACharge()
        {
            // Jump is read before the pull input, so cancelling beats releasing.
            Assert.That(ModeRules.Next(PlayerMode.SlingshotCharging, ModeTrigger.Jump),
                Is.EqualTo(PlayerMode.Launched));
        }

        [Test]
        public void AFreeWireEndHopsOffButAPoleEndPerches()
        {
            // Riding into a pole perches,
            // and only an end with no pole under it still hops off.
            Assert.That(ModeRules.Next(PlayerMode.WireRiding, ModeTrigger.ReachWireEnd),
                Is.EqualTo(PlayerMode.Launched));
            Assert.That(ModeRules.Next(PlayerMode.WireRiding, ModeTrigger.PerchFromWire),
                Is.EqualTo(PlayerMode.Perched));
        }

        [Test]
        public void PerchFromWireOnlyAppliesWhileRiding()
        {
            foreach (PlayerMode mode in AllModes)
            {
                if (mode == PlayerMode.WireRiding)
                    continue;

                Assert.IsNull(ModeRules.Next(mode, ModeTrigger.PerchFromWire), $"{mode} is not on a wire.");
            }
        }

        [Test]
        public void Regression_LeavingTheGroundMakesWalkingAirborne()
        {
            // Walking must become Airborne, or a normal jump or walking off a pole top could never catch a
            // wire - the wire grab only runs in the air.
            Assert.That(ModeRules.Next(PlayerMode.Walking, ModeTrigger.LeaveGround),
                Is.EqualTo(PlayerMode.Airborne));
            Assert.That(ModeRules.Next(PlayerMode.Airborne, ModeTrigger.Land),
                Is.EqualTo(PlayerMode.Walking));
        }

        [Test]
        public void PoleBottomStepsOffAndPoleTopPerches()
        {
            Assert.That(ModeRules.Next(PlayerMode.Climbing, ModeTrigger.ReachPoleBottom),
                Is.EqualTo(PlayerMode.Walking));
            Assert.That(ModeRules.Next(PlayerMode.Climbing, ModeTrigger.ReachPoleTop),
                Is.EqualTo(PlayerMode.Perched));
        }

        [Test]
        public void ALaunchCanStillCatchAPoleOrAWire()
        {
            // Being mid-launch does not forbid a grab; the lockouts decide that, and they are separate.
            Assert.That(ModeRules.Next(PlayerMode.Launched, ModeTrigger.GrabPole),
                Is.EqualTo(PlayerMode.Climbing));
            Assert.That(ModeRules.Next(PlayerMode.Launched, ModeTrigger.GrabWireFromAir),
                Is.EqualTo(PlayerMode.WireRiding));
        }

        [Test]
        public void PoleLostIsAClimbingExitOnly()
        {
            // Pole-lost applies only to Climbing. A perch that silently became Walking would drop
            // the player from the pole top with no placement rule, so it is deliberately not handled --
            // pinned here because "the pole went away, go to Walking" reads plausible enough to re-add.
            Assert.That(ModeRules.Next(PlayerMode.Climbing, ModeTrigger.PoleLost),
                Is.EqualTo(PlayerMode.Walking));
            Assert.IsNull(ModeRules.Next(PlayerMode.Perched, ModeTrigger.PoleLost));
        }

        [Test]
        public void Regression_StunnedSwallowsEverythingButItsOwnExpiry()
        {
            // Only the stun expiring may end a stun, or it could be walked or climbed out of early.
            foreach (ModeTrigger trigger in AllTriggers)
            {
                // GoLimp is a knock, not an input: a stunned player can still be knocked limp.
                if (trigger == ModeTrigger.StunExpired || trigger == ModeTrigger.Respawn || trigger == ModeTrigger.GoLimp)
                    continue;

                Assert.IsNull(ModeRules.Next(PlayerMode.Stunned, trigger),
                    $"{trigger} must not break the stun.");
            }
        }

        [Test]
        public void Regression_RagdollFlightOnlyEndsByRecoveringOrRespawning()
        {
            // Nothing the player presses should interrupt a flight except respawn. GoLimp is a knock, not an
            // input, and it only swaps the guided flight for the unguided one.
            foreach (ModeTrigger trigger in AllTriggers)
            {
                if (trigger == ModeTrigger.Recover || trigger == ModeTrigger.Respawn || trigger == ModeTrigger.GoLimp)
                    continue;

                Assert.IsNull(ModeRules.Next(PlayerMode.RagdollFlight, trigger),
                    $"{trigger} must not interrupt a ragdoll flight.");
            }
        }

        [Test]
        public void GoingLimpWorksFromEveryModeButRagdollItself()
        {
            foreach (PlayerMode mode in AllModes)
            {
                PlayerMode? next = ModeRules.Next(mode, ModeTrigger.GoLimp);
                if (mode == PlayerMode.Ragdoll)
                    Assert.IsNull(next, "Already limp; going limp again is not a change.");
                else
                    Assert.That(next, Is.EqualTo(PlayerMode.Ragdoll), $"GoLimp should work from {mode}.");
            }
        }

        [Test]
        public void ARagdollOnlyEndsByRecoveringOrRespawning()
        {
            // Same rule as a flight: once limp, the body is physics's until it has settled.
            Assert.That(ModeRules.Next(PlayerMode.Ragdoll, ModeTrigger.Recover), Is.EqualTo(PlayerMode.Stunned));
            foreach (ModeTrigger trigger in AllTriggers)
            {
                if (trigger == ModeTrigger.Recover || trigger == ModeTrigger.Respawn)
                    continue;

                Assert.IsNull(ModeRules.Next(PlayerMode.Ragdoll, trigger), $"{trigger} must not end a ragdoll.");
            }
        }

        [Test]
        public void TheCauseDecidesTheFlight()
        {
            // Same launch maths, different intent.
            Assert.That(ModeRules.FlightFor(LaunchCause.Purposeful), Is.EqualTo(PlayerMode.Launched));
            Assert.That(ModeRules.FlightFor(LaunchCause.Hazard), Is.EqualTo(PlayerMode.RagdollFlight));
        }

        [Test]
        public void APurposefulSlingshotCanReachLaunched()
        {
            // The purposeful release goes through PlayerModes.Launch, which asserts against the Jump row
            // from wherever it starts - so that row has to lead to Launched from a charge.
            Assert.That(ModeRules.Next(PlayerMode.SlingshotCharging, ModeTrigger.Jump),
                Is.EqualTo(ModeRules.FlightFor(LaunchCause.Purposeful)));
            Assert.That(ModeRules.Next(PlayerMode.SlingshotCharging, ModeTrigger.ReleaseChargeStrong),
                Is.EqualTo(ModeRules.FlightFor(LaunchCause.Hazard)));
        }

        [Test]
        public void RagdolledIsExactlyTheTwoRagdollModes()
        {
            foreach (PlayerMode mode in AllModes)
            {
                bool expected = mode == PlayerMode.RagdollFlight || mode == PlayerMode.Ragdoll;
                Assert.That(ModeRules.IsRagdolled(mode), Is.EqualTo(expected), $"{mode}");
            }
        }

        [Test]
        public void WalkingCannotGrabAWireOutOfNowhere()
        {
            // The wire is grabbed from the air or from a perch, never from standing on the ground.
            Assert.IsNull(ModeRules.Next(PlayerMode.Walking, ModeTrigger.GrabWireFromAir));
            Assert.IsNull(ModeRules.Next(PlayerMode.Walking, ModeTrigger.MountWireFromPerch));
        }

        [Test]
        public void ChargingIsOnlyReachableFromTheWire()
        {
            foreach (PlayerMode mode in AllModes)
            {
                PlayerMode? next = ModeRules.Next(mode, ModeTrigger.StartCharge);
                if (next.HasValue)
                    Assert.That(mode, Is.EqualTo(PlayerMode.WireRiding));
            }
        }

        // ---------------------------------------------------------------- what each mode implies

        [Test]
        public void AttachedModesAreExactlyThePoleAndWireModes()
        {
            Assert.IsTrue(ModeRules.IsAttached(PlayerMode.Climbing));
            Assert.IsTrue(ModeRules.IsAttached(PlayerMode.Perched));
            Assert.IsTrue(ModeRules.IsAttached(PlayerMode.WireRiding));
            Assert.IsTrue(ModeRules.IsAttached(PlayerMode.SlingshotCharging));

            Assert.IsFalse(ModeRules.IsAttached(PlayerMode.Walking));
            Assert.IsFalse(ModeRules.IsAttached(PlayerMode.Airborne));
            Assert.IsFalse(ModeRules.IsAttached(PlayerMode.Launched));
            Assert.IsFalse(ModeRules.IsAttached(PlayerMode.RagdollFlight));
            Assert.IsFalse(ModeRules.IsAttached(PlayerMode.Stunned));
        }

        [Test]
        public void AttachedModesNeverUseTheCharacterController()
        {
            // The controller is off whenever a script is placing the body, which is what stops the direct
            // transform writes from fighting the controller.
            foreach (PlayerMode mode in AllModes)
            {
                if (ModeRules.IsAttached(mode))
                    Assert.IsFalse(ModeRules.UsesCharacterController(mode), $"{mode}");
            }
        }

        [Test]
        public void RagdollFlightNeverUsesTheCharacterControllerOrBaseLocomotion()
        {
            // While the limbs simulate, nothing else may drive the body. These are exclusive modes.
            Assert.IsFalse(ModeRules.UsesCharacterController(PlayerMode.RagdollFlight));
            Assert.IsFalse(ModeRules.UsesBaseLocomotion(PlayerMode.RagdollFlight));
            Assert.IsFalse(ModeRules.UsesCharacterController(PlayerMode.Ragdoll));
            Assert.IsFalse(ModeRules.UsesBaseLocomotion(PlayerMode.Ragdoll));
        }

        [Test]
        public void BaseLocomotionRunsOnlyWhereTheBaseControllerIsDriving()
        {
            Assert.IsTrue(ModeRules.UsesBaseLocomotion(PlayerMode.Walking));
            Assert.IsTrue(ModeRules.UsesBaseLocomotion(PlayerMode.Airborne));

            // Launched keeps the controller but not its locomotion: the ballistic mover owns Move(), and two
            // callers per frame would fight over the body.
            Assert.IsTrue(ModeRules.UsesCharacterController(PlayerMode.Launched));
            Assert.IsFalse(ModeRules.UsesBaseLocomotion(PlayerMode.Launched));

            Assert.IsFalse(ModeRules.UsesBaseLocomotion(PlayerMode.Stunned), "Stun means no input.");
        }

        [Test]
        public void BaseLocomotionImpliesTheCharacterController()
        {
            foreach (PlayerMode mode in AllModes)
            {
                if (ModeRules.UsesBaseLocomotion(mode))
                    Assert.IsTrue(ModeRules.UsesCharacterController(mode), $"{mode}");
            }
        }

        [Test]
        public void AirborneModesAreTheOnesOffTheGround()
        {
            Assert.IsTrue(ModeRules.IsAirborne(PlayerMode.Airborne));
            Assert.IsTrue(ModeRules.IsAirborne(PlayerMode.Launched));
            Assert.IsTrue(ModeRules.IsAirborne(PlayerMode.RagdollFlight));

            Assert.IsFalse(ModeRules.IsAirborne(PlayerMode.Walking));
            Assert.IsFalse(ModeRules.IsAirborne(PlayerMode.Stunned));
        }
    }
}
