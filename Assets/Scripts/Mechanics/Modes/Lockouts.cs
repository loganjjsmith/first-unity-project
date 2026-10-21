using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// The re-grab lockouts, in one object with one tick.
    /// </summary>
    /// <remarks>
    /// Timers count seconds, never frames, and all tick in one place, so none can silently freeze while the
    /// player is in some other mode, and one transition cannot zero a lock another has just set.
    ///
    /// Two rules hold everything together:
    /// <list type="bullet">
    /// <item><b>Locks never shorten.</b> Asking for a lock takes the longer of the two, so a brief lock
    /// cannot cancel a long one that is still meant to be running.</item>
    /// <item><b>Wire locks come in two kinds.</b> Blocking one wire lets you hop wire to wire
    /// while still stopping you snapping straight back onto the one you just left. Locking all wires
    /// is for a slingshot launch, where re-grabbing anything immediately would eat the launch.</item>
    /// </list>
    /// Only the owning mode object should call the <c>Lock</c> methods, and it should do so from the same
    /// place it changes mode, so a lock and its reason stay together.
    /// </remarks>
    public sealed class Lockouts
    {
        /// <summary>Wire id meaning "no wire".</summary>
        public const int NoWire = 0;

        float climbLock;
        float allWiresLock;
        float blockedWireLock;
        int blockedWireId = NoWire;
        bool needsGroundBeforeWire;

        /// <summary>Seconds left before a pole can be grabbed again. Zero means free.</summary>
        public float ClimbLockRemaining => climbLock;

        /// <summary>Seconds left before any wire can be grabbed. Zero means free.</summary>
        public float AllWiresLockRemaining => allWiresLock;

        /// <summary>The one wire currently blocked, or <see cref="NoWire"/>.</summary>
        public int BlockedWire => blockedWireLock > 0f ? blockedWireId : NoWire;

        /// <summary>Whether a landing is still required before any wire can be grabbed.</summary>
        public bool NeedsGroundBeforeWire => needsGroundBeforeWire;

        /// <summary>Counts every lock down. Call once per frame, from one place.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime))
                return;

            climbLock = Mathf.Max(0f, climbLock - deltaTime);
            allWiresLock = Mathf.Max(0f, allWiresLock - deltaTime);

            blockedWireLock = Mathf.Max(0f, blockedWireLock - deltaTime);
            if (blockedWireLock <= 0f)
                blockedWireId = NoWire;
        }

        /// <summary>
        /// Stops poles being grabbed for a while. Live values: 0.45 s after leaving a pole, so the pull
        /// input still being held does not immediately re-stick, and at least 0.15 s after leaving a wire.
        /// </summary>
        public void LockClimb(float seconds)
        {
            climbLock = Mathf.Max(climbLock, Mathf.Max(0f, seconds));
        }

        /// <summary>
        /// Stops every wire being grabbed for a while. Live values: 1.75 s after a slingshot launch,
        /// 1.49 s after cancelling a charge with jump.
        /// </summary>
        public void LockAllWires(float seconds)
        {
            allWiresLock = Mathf.Max(allWiresLock, Mathf.Max(0f, seconds));
        }

        /// <summary>
        /// Stops one wire being grabbed for a while, leaving the others available. Use this
        /// on a normal wire drop or an end hop.
        /// </summary>
        /// <remarks>
        /// Only one wire is tracked. Blocking a second wire replaces the first, which is fine because the
        /// case it exists for is "the wire I just left" and there is only ever one of those.
        /// </remarks>
        public void BlockWire(int wireId, float seconds)
        {
            if (wireId == NoWire || seconds <= 0f)
                return;

            if (wireId == blockedWireId)
            {
                blockedWireLock = Mathf.Max(blockedWireLock, seconds);
                return;
            }

            blockedWireId = wireId;
            blockedWireLock = seconds;
        }

        /// <summary>
        /// Requires a landing before any wire can be grabbed, on top of any timed lock. Only the
        /// slingshot charge cancel uses it; every other wire exit blocks just the wire it left.
        /// </summary>
        public void RequireGroundBeforeWire()
        {
            needsGroundBeforeWire = true;
        }

        /// <summary>
        /// Call on touchdown. Satisfies <see cref="RequireGroundBeforeWire"/> and nothing else: the timed
        /// locks keep running, because landing early should not hand back a lock that is still meant to be
        /// stopping a re-grab.
        /// </summary>
        public void NotifyGrounded()
        {
            needsGroundBeforeWire = false;
        }

        /// <summary>Clears every lock. Respawn only.</summary>
        public void ClearAll()
        {
            climbLock = 0f;
            allWiresLock = 0f;
            blockedWireLock = 0f;
            blockedWireId = NoWire;
            needsGroundBeforeWire = false;
        }

        /// <summary>Whether a pole may be grabbed right now.</summary>
        public bool CanGrabPole => climbLock <= 0f;

        /// <summary>Whether this particular wire may be grabbed right now.</summary>
        public bool CanGrabWire(int wireId)
        {
            if (needsGroundBeforeWire)
                return false;

            if (allWiresLock > 0f)
                return false;

            return !(blockedWireLock > 0f && wireId == blockedWireId);
        }
    }
}
