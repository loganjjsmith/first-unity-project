using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Soft steering in the air, the "paper airplane": WASD relative
    /// to the camera nudges a flight that is already under way. W stretches the arc, S shortens it, A and D
    /// veer. The same maths serves <c>Launched</c> and <c>RagdollFlight</c>; only the strength differs.
    /// </summary>
    public static class AirSteer
    {
        /// <summary>
        /// The horizontal acceleration (metres per second, per second) the stick asks for.
        /// </summary>
        /// <param name="move">Stick or WASD: x is right(+)/left(-), y is forward(+)/back(-). Each -1..1.</param>
        /// <param name="cameraForward">Which way the camera faces. May tilt up or down.</param>
        /// <param name="cameraRight">The camera's right-hand direction.</param>
        /// <param name="strength">Acceleration at full stick, in m/s².</param>
        public static Vector3 Acceleration(Vector2 move, Vector3 cameraForward, Vector3 cameraRight, float strength)
        {
            Vector3 flightMovement = Vector3.zero;
            // Steering never pushes up or down - gravity owns that. Flatten both camera directions onto the
            //    ground plane, so a camera looking down at the fox still steers along the ground.
            cameraForward.y = 0;
            cameraForward = cameraForward.normalized;
            cameraRight.y = 0;
            cameraRight = cameraRight.normalized;
            // The stick as a world direction: forward scaled by move.y, plus right scaled by move.x.
            flightMovement += cameraForward * move.y;
            flightMovement += cameraRight * move.x;
            // A diagonal (W+D) must not be stronger than straight ahead: its length is about 1.41, so cap it at 1.
            flightMovement = Vector3.ClampMagnitude(flightMovement, 1f);
            flightMovement *= strength;
            return flightMovement;
        }
    }
}
