using UnityEngine;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Measures how fast the player's root is really moving, whichever system is moving it. The
    /// CharacterController only knows its own moves, and the pole, wire and slingshot write the transform
    /// directly, so going limp reads its momentum from here.
    /// </summary>
    public class VelocityTracker : MonoBehaviour
    {
        /// <summary>Metres per second, measured over the last frame.</summary>
        public Vector3 Velocity { get; private set; }

        Vector3 lastPosition;

        void OnEnable()
        {
            // Start from where we are, or the first frame would measure a jump from the origin.
            lastPosition = transform.position;
        }

        void LateUpdate()
        {
            // A paused frame has no duration to divide by.
            if (Time.deltaTime == 0)
                return;

            Velocity = (transform.position - lastPosition) / Time.deltaTime;
            lastPosition = transform.position;
        }

        /// <summary>
        /// Forgets the last position, so a teleport is not measured as movement. Called by
        /// <see cref="PlayerModes.Teleport"/>.
        /// </summary>
        public void ResetTracking()
        {
            Velocity = Vector3.zero;
            lastPosition = transform.position;
        }
    }
}
