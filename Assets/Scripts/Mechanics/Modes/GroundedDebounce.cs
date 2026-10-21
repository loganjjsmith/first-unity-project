using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Turns the base controller's per-frame grounded flag into a steady "has left the ground" signal, so
    /// Walking only becomes Airborne once the body has really been off the ground for a moment.
    /// </summary>
    /// <remarks>
    /// Sprinting downhill, Starter Assets' grounded sphere check blinks off for a frame or two every few
    /// frames. Mirrored directly, that flips Walking and Airborne about ten times a second and fires a
    /// spurious <c>Landed</c> each time. Starter Assets guards its own falling animation
    /// the same way (<c>FallTimeout</c>), and the caller passes that value in, so the mode and the animation
    /// agree on when a fall has started. The same grace also covers the one stale frame of
    /// <c>Grounded</c> after an identity mode hands control back, since the base controller skips its
    /// ground check while its locomotion is off.
    /// </remarks>
    public sealed class GroundedDebounce
    {
        float ungroundedFor;

        /// <summary>
        /// Feeds one frame. Returns true once the body has been continuously ungrounded for at least
        /// <paramref name="grace"/> seconds; any grounded frame starts the count again.
        /// </summary>
        public bool Tick(bool grounded, float deltaTime, float grace)
        {
            if (grounded)
            {
                ungroundedFor = 0f;
                return false;
            }

            if (deltaTime > 0f) // also rejects NaN
                ungroundedFor += deltaTime;

            return ungroundedFor >= Mathf.Max(0f, grace);
        }

        /// <summary>Forgets any partial count. Call whenever the base controller is not the one driving.</summary>
        public void Reset()
        {
            ungroundedFor = 0f;
        }
    }
}
