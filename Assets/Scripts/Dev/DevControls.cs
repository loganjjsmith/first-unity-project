using UnityEngine;
using UnityEngine.InputSystem;
using MovementSandbox.Environment;
using MovementSandbox.Identity;

namespace MovementSandbox.Dev
{
    /// <summary>
    /// Every playtest shortcut in one place. Dev tooling, not a game feature: disable or remove this
    /// component before a build that other people play.
    /// <list type="bullet">
    /// <item><b>1-9</b> teleport to the matching entry in <see cref="Targets"/>, so a test setup is one key away.</item>
    /// <item><b>Z</b> goes limp on the spot from any mode, keeping the current momentum.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The bindings are defined in code rather than in the shared input actions asset, so the game's own
    /// control scheme never carries debug keys. The template's unused "Previous" and "Next" actions also
    /// sit on 1 and 2 in <c>InputSystem_Actions</c>; nothing reads them, so there is no conflict.
    /// </remarks>
    [RequireComponent(typeof(PlayerModes))]
    public class DevControls : MonoBehaviour
    {
        const int MaxTeleportKeys = 9;

        [Tooltip("Element 0 is key 1, element 1 is key 2, and so on up to 9. Leave a slot empty to skip " +
                 "that key. Use RespawnPoints with 'Is Default' off, so R still respawns at the start.")]
        public RespawnPoint[] Targets = new RespawnPoint[0];

        PlayerModes modes;
        PlayerRagdoll ragdoll;
        InputAction[] teleportActions;
        InputAction goLimpAction;

        void Awake()
        {
            modes = GetComponent<PlayerModes>();
            ragdoll = GetComponent<PlayerRagdoll>();

            int count = Mathf.Min(Targets.Length, MaxTeleportKeys);
            teleportActions = new InputAction[count];
            for (int i = 0; i < count; i++)
            {
                teleportActions[i] = new InputAction($"DevTeleport{i + 1}", binding: $"<Keyboard>/{i + 1}");
                teleportActions[i].Enable();
            }

            goLimpAction = new InputAction("DevGoLimp", binding: "<Keyboard>/z");
            goLimpAction.Enable();
        }

        void OnDestroy()
        {
            if (teleportActions != null)
            {
                foreach (InputAction action in teleportActions)
                    action?.Dispose();
            }
            goLimpAction?.Dispose();
        }

        void Update()
        {
            for (int i = 0; i < teleportActions.Length; i++)
            {
                if (teleportActions[i].WasPressedThisFrame() && Targets[i] != null)
                    modes.TeleportTo(Targets[i]);
            }

            if (goLimpAction.WasPressedThisFrame() && ragdoll != null)
                ragdoll.GoLimp();
        }
    }
}
