using NUnit.Framework;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the damped spring the wire twangs back with. The numbers are the wire's defaults: stiffness 400
    /// (about three wobbles a second) and damping 8.
    /// </summary>
    public class SpringTests
    {
        const float Stiffness = 400f, Damping = 8f;

        /// <summary>Runs the spring from full displacement for <paramref name="seconds"/>, returning the lowest point reached.</summary>
        static float Run(float seconds, float deltaTime, out float position, out float velocity, float damping = Damping)
        {
            position = 1f;
            velocity = 0f;
            float lowest = position;
            for (float time = 0f; time < seconds; time += deltaTime)
            {
                Spring.Step(ref position, ref velocity, Stiffness, damping, deltaTime);
                if (position < lowest)
                    lowest = position;
            }
            return lowest;
        }

        [Test]
        public void ItComesBackToRest()
        {
            Run(2f, 1f / 60f, out float position, out float velocity);
            Assert.That(position, Is.EqualTo(0f).Within(0.01f));
            Assert.That(velocity, Is.EqualTo(0f).Within(0.1f));
        }

        [Test]
        public void ItOvershootsRestOnTheWayBack()
        {
            // The wobble: it swings past rest to the other side before settling.
            float lowest = Run(0.5f, 1f / 60f, out _, out _);
            Assert.That(lowest, Is.LessThan(-0.2f));
        }

        [Test]
        public void ASpringAtRestStaysAtRest()
        {
            float position = 0f, velocity = 0f;
            for (int i = 0; i < 60; i++)
                Spring.Step(ref position, ref velocity, Stiffness, Damping, 1f / 60f);
            Assert.That(position, Is.EqualTo(0f));
            Assert.That(velocity, Is.EqualTo(0f));
        }

        [Test]
        public void MoreDampingMeansLessWobble()
        {
            float lightlyDamped = Run(0.5f, 1f / 60f, out _, out _, damping: 4f);
            float heavilyDamped = Run(0.5f, 1f / 60f, out _, out _, damping: 20f);
            Assert.That(heavilyDamped, Is.GreaterThan(lightlyDamped));
        }

        [Test]
        public void ItSettlesAtLowAndHighFrameRates()
        {
            // Updating velocity before position keeps a slow frame from adding energy.
            Run(2f, 1f / 30f, out float at30, out _);
            Run(2f, 1f / 144f, out float at144, out _);
            Assert.That(at30, Is.EqualTo(0f).Within(0.02f));
            Assert.That(at144, Is.EqualTo(0f).Within(0.02f));
        }
    }
}
