namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// A damped spring, one step at a time: something pulled away from rest (0) is pulled back toward it,
    /// overshoots, and wobbles to a stop. Used for the wire twanging back after a slingshot release.
    /// </summary>
    public static class Spring
    {
        /// <summary>
        /// Advances the spring by <paramref name="deltaTime"/> seconds. <paramref name="position"/> is how
        /// far from rest it is (0 = at rest), and <paramref name="velocity"/> how fast that is changing.
        /// </summary>
        /// <param name="stiffness">How hard it pulls back toward rest. Higher wobbles faster.</param>
        /// <param name="damping">How quickly the wobble dies away. 0 would wobble forever.</param>
        public static void Step(ref float position, ref float velocity, float stiffness, float damping, float deltaTime)
        {
            // Pulled back toward rest, harder the further out, minus a drag that bleeds the wobble away.
            float acceleration = (-stiffness * position) - (damping * velocity);
            velocity += acceleration * deltaTime;
            // Moved by the updated velocity, not the old one: that keeps a slow frame from adding energy.
            position += velocity * deltaTime;
        }
    }
}
