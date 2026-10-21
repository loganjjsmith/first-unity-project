using UnityEngine;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Camera-relative directions read from <see cref="Camera.main"/> (the Cinemachine Brain drives it).
    /// One shared helper rather than each identity script reading the camera transform its own way.
    /// </summary>
    public static class CameraBasis
    {
        /// <summary>
        /// The Galaxy-style tilt <see cref="ClimbCameraUp"/> currently applies to the camera. Every direction
        /// below has it undone, so controls read the same basis they would with an upright camera and only
        /// the view changes.
        /// </summary>
        public static Quaternion UpRotation = Quaternion.identity;

        static Quaternion UntiltedRotation => Quaternion.Inverse(UpRotation) * Camera.main.transform.rotation;

        /// <summary>The camera's forward vector, or world forward if there is no main camera yet.</summary>
        public static Vector3 Forward => Camera.main != null ? UntiltedRotation * Vector3.forward : Vector3.forward;

        /// <summary>Forward flattened onto the XZ plane and renormalized - for ground-relative movement.</summary>
        public static Vector3 PlanarForward
        {
            get
            {
                Vector3 forward = Forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            }
        }

        /// <summary>The camera's yaw in degrees.</summary>
        public static float Yaw => Camera.main != null ? UntiltedRotation.eulerAngles.y : 0f;

        /// <summary>The camera's right vector - used to keep things like pole orbit camera-relative.</summary>
        public static Vector3 Right => Camera.main != null ? UntiltedRotation * Vector3.right : Vector3.right;
    }
}
