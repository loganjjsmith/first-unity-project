using NUnit.Framework;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the re-grab lockouts: timers that must keep ticking in every mode, never silently freezing.
    /// </summary>
    public class LockoutsTests
    {
        const float Step = 1f / 60f;

        // Live durations.
        const float PoleReentryLock = 0.45f;
        const float SlingshotWireLock = 1.75f;
        const float ChargeCancelWireLock = 1.49f;

        const int WireA = 101;
        const int WireB = 202;

        Lockouts locks;

        [SetUp]
        public void Setup()
        {
            locks = new Lockouts();
        }

        static void Advance(Lockouts locks, float seconds)
        {
            int steps = (int)(seconds / Step) + 1;
            for (int i = 0; i < steps; i++)
                locks.Tick(Step);
        }

        // ---------------------------------------------------------------- nothing locked by default

        [Test]
        public void FreshLockouts_AllowEverything()
        {
            Assert.IsTrue(locks.CanGrabPole);
            Assert.IsTrue(locks.CanGrabWire(WireA));
            Assert.IsFalse(locks.NeedsGroundBeforeWire);
        }

        // ---------------------------------------------------------------- the climb lock

        [Test]
        public void ClimbLock_BlocksThenExpires()
        {
            // The point of this lock: jumping off a pole while the grab input is still held must not
            // immediately re-stick to the same pole.
            locks.LockClimb(PoleReentryLock);
            Assert.IsFalse(locks.CanGrabPole);

            Advance(locks, PoleReentryLock * 0.5f);
            Assert.IsFalse(locks.CanGrabPole, "Still locked halfway through.");

            Advance(locks, PoleReentryLock);
            Assert.IsTrue(locks.CanGrabPole);
        }

        [Test]
        public void ClimbLock_DoesNotAffectWires()
        {
            locks.LockClimb(PoleReentryLock);
            Assert.IsTrue(locks.CanGrabWire(WireA));
        }

        [Test]
        public void Regression_AShorterLockNeverCancelsALongerOne()
        {
            // The old failure mode: DetachFromWire set a timer and BeginRagdollFlight immediately zeroed it,
            // because each transition wrote its own subset of flags.
            locks.LockClimb(1f);
            locks.LockClimb(0.1f);

            Advance(locks, 0.2f);
            Assert.IsFalse(locks.CanGrabPole, "The 0.1 s request must not have shortened the 1 s lock.");

            Advance(locks, 1f);
            Assert.IsTrue(locks.CanGrabPole);
        }

        [Test]
        public void ALongerLockExtendsAShorterOne()
        {
            locks.LockClimb(0.1f);
            locks.LockClimb(1f);

            Advance(locks, 0.3f);
            Assert.IsFalse(locks.CanGrabPole);
        }

        // ---------------------------------------------------------------- one wire vs all wires

        [Test]
        public void BlockWire_BlocksOnlyThatWire()
        {
            // Wire-to-wire hops are allowed, and only the wire just left is blocked.
            locks.BlockWire(WireA, 1.25f);

            Assert.IsFalse(locks.CanGrabWire(WireA));
            Assert.IsTrue(locks.CanGrabWire(WireB), "Hopping to another wire must stay legal.");
        }

        [Test]
        public void BlockWire_Expires()
        {
            locks.BlockWire(WireA, 1.25f);
            Advance(locks, 1.3f);

            Assert.IsTrue(locks.CanGrabWire(WireA));
            Assert.That(locks.BlockedWire, Is.EqualTo(Lockouts.NoWire));
        }

        [Test]
        public void BlockWire_ASecondWireReplacesTheFirst()
        {
            // Only "the wire I just left" is ever tracked, and there is only one of those.
            locks.BlockWire(WireA, 1.25f);
            locks.BlockWire(WireB, 1.25f);

            Assert.IsTrue(locks.CanGrabWire(WireA));
            Assert.IsFalse(locks.CanGrabWire(WireB));
        }

        [Test]
        public void BlockWire_ReBlockingTheSameWireExtendsIt()
        {
            locks.BlockWire(WireA, 0.2f);
            locks.BlockWire(WireA, 1.5f);

            Advance(locks, 0.5f);
            Assert.IsFalse(locks.CanGrabWire(WireA));
        }

        [Test]
        public void BlockWire_IgnoresNoWireAndNonPositiveDurations()
        {
            locks.BlockWire(Lockouts.NoWire, 1f);
            locks.BlockWire(WireA, 0f);
            locks.BlockWire(WireB, -1f);

            Assert.IsTrue(locks.CanGrabWire(WireA));
            Assert.IsTrue(locks.CanGrabWire(WireB));
        }

        [Test]
        public void LockAllWires_BlocksEveryWire()
        {
            // A slingshot launch: re-grabbing anything straight away would eat the launch.
            locks.LockAllWires(SlingshotWireLock);

            Assert.IsFalse(locks.CanGrabWire(WireA));
            Assert.IsFalse(locks.CanGrabWire(WireB));

            Advance(locks, SlingshotWireLock * 0.5f);
            Assert.IsFalse(locks.CanGrabWire(WireB));

            Advance(locks, SlingshotWireLock);
            Assert.IsTrue(locks.CanGrabWire(WireB));
        }

        [Test]
        public void LockAllWires_DoesNotAffectPoles()
        {
            // Bonking off a pole mid-flight is comedy, so a wire lock must not block it.
            locks.LockAllWires(SlingshotWireLock);
            Assert.IsTrue(locks.CanGrabPole);
        }

        // ---------------------------------------------------------------- needing to land first

        [Test]
        public void RequireGround_BlocksWiresUntilLanding()
        {
            locks.RequireGroundBeforeWire();
            Assert.IsFalse(locks.CanGrabWire(WireA));

            Advance(locks, 10f);
            Assert.IsFalse(locks.CanGrabWire(WireA), "Time alone must not satisfy it.");

            locks.NotifyGrounded();
            Assert.IsTrue(locks.CanGrabWire(WireA));
        }

        [Test]
        public void Regression_LandingDoesNotCancelATimedWireLock()
        {
            // Landing early should not hand back a lock that is still meant to be stopping a re-grab.
            locks.LockAllWires(ChargeCancelWireLock);
            locks.RequireGroundBeforeWire();

            locks.NotifyGrounded();
            Assert.IsFalse(locks.CanGrabWire(WireA), "The timed lock still has time left.");

            Advance(locks, ChargeCancelWireLock);
            Assert.IsTrue(locks.CanGrabWire(WireA));
        }

        [Test]
        public void NotifyGrounded_IsSafeWhenNothingWasRequired()
        {
            locks.NotifyGrounded();
            Assert.IsTrue(locks.CanGrabWire(WireA));
            Assert.IsTrue(locks.CanGrabPole);
        }

        // ---------------------------------------------------------------- respawn and ticking

        [Test]
        public void ClearAll_ReleasesEverything()
        {
            locks.LockClimb(5f);
            locks.LockAllWires(5f);
            locks.BlockWire(WireA, 5f);
            locks.RequireGroundBeforeWire();

            locks.ClearAll();

            Assert.IsTrue(locks.CanGrabPole);
            Assert.IsTrue(locks.CanGrabWire(WireA));
            Assert.IsFalse(locks.NeedsGroundBeforeWire);
            Assert.That(locks.BlockedWire, Is.EqualTo(Lockouts.NoWire));
        }

        [Test]
        public void Tick_IgnoresNonPositiveAndNaNDeltas()
        {
            locks.LockClimb(0.5f);

            locks.Tick(0f);
            locks.Tick(-1f);
            locks.Tick(float.NaN);

            Assert.That(locks.ClimbLockRemaining, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Tick_NeverDrivesALockNegative()
        {
            locks.LockClimb(0.1f);
            Advance(locks, 5f);

            Assert.That(locks.ClimbLockRemaining, Is.EqualTo(0f));
            Assert.That(locks.AllWiresLockRemaining, Is.EqualTo(0f));
        }

        // ---------------------------------------------------------------- a real sequence

        [Test]
        public void ASlingshotLaunchThenLandingBehavesAsSpecified()
        {
            // Charge on wire A, launch, fly, land, and only then be able to grab a wire again.
            locks.LockAllWires(SlingshotWireLock);
            locks.BlockWire(WireA, SlingshotWireLock);

            Assert.IsFalse(locks.CanGrabWire(WireA));
            Assert.IsFalse(locks.CanGrabWire(WireB));
            Assert.IsTrue(locks.CanGrabPole, "A pole can still be caught mid-flight.");

            Advance(locks, SlingshotWireLock + 0.1f);
            locks.NotifyGrounded();

            Assert.IsTrue(locks.CanGrabWire(WireA));
            Assert.IsTrue(locks.CanGrabWire(WireB));
        }

        [Test]
        public void APoleKickThenAWireGrabBehavesAsSpecified()
        {
            // Kicking off a pole locks the pole but leaves wires open, so a kick can be used to reach one.
            locks.LockClimb(PoleReentryLock);

            Assert.IsFalse(locks.CanGrabPole);
            Assert.IsTrue(locks.CanGrabWire(WireA));

            Advance(locks, PoleReentryLock + 0.05f);
            Assert.IsTrue(locks.CanGrabPole);
        }
    }
}
