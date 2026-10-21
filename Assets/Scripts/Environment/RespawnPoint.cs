using UnityEngine;
using UnityEngine.Serialization;

namespace MovementSandbox.Environment
{
    /// <summary>
    /// A place the player can respawn or be teleported to. Put it on an empty GameObject where the
    /// player should reappear; its Y rotation is the facing.
    /// </summary>
    public class RespawnPoint : MonoBehaviour
    {
        [Tooltip("If several points exist, respawn uses the first one with this set.")]
        [FormerlySerializedAs("isDefault")]
        public bool IsDefault = true;

        [Tooltip("On: respawn facing this marker's Y rotation. Off: keep whichever way the player was already facing.")]
        [FormerlySerializedAs("usePointYaw")]
        public bool UsePointYaw = true;

        public Vector3 Position => transform.position;
        public float Yaw => transform.eulerAngles.y;

        void OnDrawGizmos()
        {
            // Drawn even when not selected, so the spawn is easy to find in the Scene view.
            Gizmos.color = IsDefault ? new Color(0.2f, 0.9f, 0.35f, 0.9f) : new Color(0.4f, 0.7f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.05f, 0.08f);
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            Gizmos.DrawLine(transform.position, transform.position + forward.normalized * 0.8f);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.45f);
        }
    }
}
