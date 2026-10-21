using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Frame-rate-independent exponential smoothing. Use this instead of
    /// <c>Lerp(a, b, k * deltaTime)</c>.
    /// </summary>
    /// <remarks>
    /// <c>Lerp(a, b, k * dt)</c> is wrong in two ways:
    /// <list type="bullet">
    /// <item>The amount smoothed per second depends on the frame rate, so turning is snappier at 240 fps
    /// than at 30 fps. That makes the feel depend on the player's hardware.</item>
    /// <item>On a long frame, <c>k * dt</c> exceeds 1. Unity's <c>Mathf.Lerp</c> clamps <c>t</c>, so this
    /// does not overshoot — it does something arguably worse and quietly snaps straight to the target,
    /// dropping the smoothing exactly when a frame spike makes a pop most visible.</item>
    /// </list>
    /// <c>1 - exp(-k * dt)</c> fixes both. The fraction of the remaining distance covered over a given
    /// period of time is the same at any frame rate, and the factor approaches 1 smoothly instead of
    /// hitting it, so a long frame degrades gracefully. After <c>t</c> seconds the remaining error is
    /// exactly <c>exp(-k * t)</c> of what it was, so <c>k</c> has a real meaning: the error drops to 1/e
    /// (about 37%) every <c>1/k</c> seconds.
    /// </remarks>
    public static class Damping
    {
        /// <summary>
        /// The fraction of the remaining distance to cover this frame: <c>1 - exp(-rate * deltaTime)</c>.
        /// Always in 0..1, so it can never overshoot.
        /// </summary>
        public static float Factor(float rate, float deltaTime)
        {
            if (rate <= 0f || deltaTime <= 0f)
                return 0f;

            if (float.IsNaN(rate) || float.IsNaN(deltaTime))
                return 0f;

            return 1f - Mathf.Exp(-rate * deltaTime);
        }

        /// <summary>Moves a value toward a target at the given rate.</summary>
        public static float Toward(float current, float target, float rate, float deltaTime)
        {
            return Mathf.Lerp(current, target, Factor(rate, deltaTime));
        }

        /// <summary>
        /// Moves an angle in degrees toward a target the short way around, so 350 to 10 turns 20 degrees
        /// forward rather than 340 degrees backward.
        /// </summary>
        public static float TowardAngle(float currentDegrees, float targetDegrees, float rate, float deltaTime)
        {
            return Mathf.LerpAngle(currentDegrees, targetDegrees, Factor(rate, deltaTime));
        }

        /// <summary>
        /// As <see cref="TowardAngle"/>, but snaps the last <paramref name="snapWithinDegrees"/> so a
        /// facing that should read as settled does not creep forever.
        /// </summary>
        public static float TowardAngleWithSnap(
            float currentDegrees,
            float targetDegrees,
            float rate,
            float deltaTime,
            float snapWithinDegrees)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(currentDegrees, targetDegrees)) <= snapWithinDegrees)
                return targetDegrees;

            return TowardAngle(currentDegrees, targetDegrees, rate, deltaTime);
        }
    }
}
