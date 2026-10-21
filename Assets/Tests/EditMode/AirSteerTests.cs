using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>
    /// Pins the "paper airplane" air steer (<see cref="AirSteer"/>): camera-relative,
    /// ground-plane only, and no stronger on a diagonal than straight ahead.
    /// </summary>
    public class AirSteerTests
    {
        // A camera facing east. Its right-hand side is south.
        static readonly Vector3 East = new Vector3(1f, 0f, 0f);
        static readonly Vector3 South = new Vector3(0f, 0f, -1f);
        const float Strength = 10f;
        const float Tolerance = 1e-4f;

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance), $"expected {expected}, got {actual}");
        }

        [Test]
        public void NoStickMeansNoSteering()
        {
            AssertVector(Vector3.zero, AirSteer.Acceleration(Vector2.zero, East, South, Strength));
        }

        [Test]
        public void TheStickIsTranslatedByTheCamera()
        {
            // The worked example from the lesson: W goes where the camera looks, D goes to its right.
            AssertVector(East * Strength, AirSteer.Acceleration(new Vector2(0f, 1f), East, South, Strength));
            AssertVector(South * Strength, AirSteer.Acceleration(new Vector2(1f, 0f), East, South, Strength));
            AssertVector(-East * Strength, AirSteer.Acceleration(new Vector2(0f, -1f), East, South, Strength));
        }

        [Test]
        public void ACameraLookingDownStillSteersAlongTheGroundAtFullStrength()
        {
            // 45 degrees down: before flattening and normalizing, forward was about (0.7, -0.7, 0).
            Vector3 tiltedForward = new Vector3(1f, -1f, 0f).normalized;
            Vector3 steer = AirSteer.Acceleration(new Vector2(0f, 1f), tiltedForward, South, Strength);

            Assert.That(steer.y, Is.EqualTo(0f).Within(Tolerance), "Steering must never push up or down.");
            Assert.That(steer.magnitude, Is.EqualTo(Strength).Within(Tolerance), "Looking down must not weaken it.");
        }

        [Test]
        public void ADiagonalIsNoStrongerThanStraightAhead()
        {
            Vector3 diagonal = AirSteer.Acceleration(new Vector2(1f, 1f), East, South, Strength);

            Assert.That(diagonal.magnitude, Is.EqualTo(Strength).Within(Tolerance));
            AssertVector((East + South).normalized * Strength, diagonal); // south-east
        }

        [Test]
        public void ACameraLookingStraightDownLeavesOnlySideways()
        {
            // Forward flattens to nothing, so W does nothing - but it must not produce NaN, and A/D still work.
            Vector3 straightDown = Vector3.down;
            AssertVector(Vector3.zero, AirSteer.Acceleration(new Vector2(0f, 1f), straightDown, South, Strength));
            AssertVector(South * Strength, AirSteer.Acceleration(new Vector2(1f, 0f), straightDown, South, Strength));
        }
    }
}
