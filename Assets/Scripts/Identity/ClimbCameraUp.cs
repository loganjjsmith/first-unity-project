using StarterAssets;
using UnityEngine;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Mario Galaxy-style camera. While the player clings to a surface, the camera's up turns smoothly
    /// toward that surface's outward direction, so whatever it is climbing reads as the floor. The goal is
    /// that climbing stays readable as it gets more complex (branches at odd angles, not only upright
    /// poles). It turns back to world up on letting go, and while perched, where the top of the pole
    /// really is the floor.
    ///
    /// Everything happens in Update, so the tilt is ready before ThirdPersonController and the Cinemachine
    /// Brain read it in LateUpdate. Those two share the default LateUpdate slot with no fixed order, so a
    /// script trying to run between them could not rely on it.
    /// </summary>
    [DefaultExecutionOrder(50)] // after PoleClimber (0), so this frame's surface direction is used
    [RequireComponent(typeof(PlayerModes))]
    [RequireComponent(typeof(PoleClimber))]
    [RequireComponent(typeof(ThirdPersonController))]
    public class ClimbCameraUp : MonoBehaviour
    {
        [Tooltip("An empty GameObject that the Cinemachine Brain's World Up Override points at. Its up is " +
                 "the camera's up; without it Cinemachine places the shoulder offset as if the world were upright.")]
        public Transform CameraUpTarget;

        [Tooltip("How quickly the camera's up turns toward the surface. Higher is snappier; 10 settles in " +
                 "roughly 0.3 s at any frame rate.")]
        public float TurnRate = 10f;

        [Tooltip("How far up the pole, in metres, the player climbs before the camera has fully swept in " +
                 "behind it. At the bottom the camera still looks at the pole straight on, like on the ground.")]
        public float SweepHeight = 2f;

        PlayerModes modes;
        PoleClimber climber;
        ThirdPersonController controller;
        Vector3 currentUp = Vector3.up;

        void Awake()
        {
            modes = GetComponent<PlayerModes>();
            climber = GetComponent<PoleClimber>();
            controller = GetComponent<ThirdPersonController>();
        }

        void Update()
        {
            Vector3 targetUp = modes.Current == PlayerMode.Climbing ? climber.SurfaceUp : Vector3.up;
            if (targetUp.sqrMagnitude < 1e-6f)
                targetUp = Vector3.up;

            if (modes.Current == PlayerMode.Climbing)
            {
                // Sweep in as the player climbs rather than snapping behind it on the grab: no tilt at the
                // bottom of the pole (the camera still looks at it straight on, and stays above ground),
                // full tilt at SweepHeight. SmoothStep eases both ends so the sweep never starts or stops
                // abruptly.
                float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, SweepHeight, climber.HeightAboveBottom));
                targetUp = Vector3.Slerp(Vector3.up, targetUp, weight);
            }

            // Frame-rate independent smoothing on top, so even a sudden change of target (grab, let go,
            // perch) turns the camera gradually. Fast camera roll is what makes players feel motion-sick.
            currentUp = Vector3.Slerp(currentUp, targetUp, Damping.Factor(TurnRate, Time.deltaTime));
            Quaternion upRotation = Quaternion.FromToRotation(Vector3.up, currentUp);

            Apply(upRotation);
        }

        void OnDisable()
        {
            currentUp = Vector3.up;
            Apply(Quaternion.identity);
        }

        void Apply(Quaternion upRotation)
        {
            controller.CameraUpRotation = upRotation;
            CameraBasis.UpRotation = upRotation;
            if (CameraUpTarget != null)
                CameraUpTarget.rotation = upRotation;
        }
    }
}
