using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>Pins the bent wire's shape: a taut V from the poles, rounded into a U across the body.</summary>
    public class WireBendMathTests
    {
        // A 40 m span between poles at 10 and 50, pressed at 30 by a body 1 m wide.
        const float SpanStart = 10f, SpanEnd = 50f, Centre = 30f, HalfWidth = 0.5f;

        static float W(float distance) => WireBendMath.Weight(distance, Centre, HalfWidth, SpanStart, SpanEnd);

        [Test]
        public void TheBodyIsWrappedNotPinched()
        {
            // Full displacement at the body's centre, and nearly full across its whole width.
            Assert.That(W(Centre), Is.EqualTo(1f));
            Assert.That(W(Centre - HalfWidth), Is.GreaterThan(0.99f).And.LessThan(1f));
            Assert.That(W(Centre + HalfWidth), Is.GreaterThan(0.99f).And.LessThan(1f));
        }

        [Test]
        public void ThePolesAndEverythingBeyondThemStayPut()
        {
            Assert.That(W(SpanStart), Is.EqualTo(0f));
            Assert.That(W(SpanEnd), Is.EqualTo(0f));
            Assert.That(W(SpanStart - 5f), Is.EqualTo(0f));
            Assert.That(W(SpanEnd + 5f), Is.EqualTo(0f));
        }

        [Test]
        public void TheWireEasesSteadilyFromPoleToContact()
        {
            float previous = 0f;
            for (float d = SpanStart; d <= Centre - HalfWidth; d += 0.5f)
            {
                float weight = W(d);
                Assert.That(weight, Is.GreaterThanOrEqualTo(previous), $"dipped at {d} m");
                previous = weight;
            }
        }

        [Test]
        public void BothSidesOfAContactInTheMiddleMatch()
        {
            for (float offset = 1f; offset < 19f; offset += 2f)
                Assert.That(W(Centre - offset), Is.EqualTo(W(Centre + offset)).Within(1e-5f));
        }

        [Test]
        public void TheLegsAreStraightLikeATautBand()
        {
            // Past the rounded corner, equal steps along the wire drop the weight by equal amounts.
            float legLength = Centre - HalfWidth - SpanStart;
            for (float d = SpanStart + 1f; d < Centre - 2f * HalfWidth; d += 2f)
                Assert.That(W(d), Is.EqualTo((d - SpanStart) / legLength).Within(1e-4f), $"not straight at {d} m");
        }

        [Test]
        public void TheCornerAtTheBodyIsRoundedNotKinked()
        {
            // Either side of the body's edge the slope changes gradually: finite differences across the
            // corner never jump from flat to the full leg slope in one step.
            float legSlope = 1f / (Centre - HalfWidth - SpanStart);
            const float step = 0.05f;
            float edge = Centre - HalfWidth;
            float previousSlope = 0f;
            for (float d = Centre; d > edge - 2f * HalfWidth; d -= step)
            {
                float slope = (W(d) - W(d - step)) / step;
                Assert.That(slope - previousSlope, Is.LessThan(legSlope * 0.25f), $"kink near {d} m");
                previousSlope = slope;
            }
        }

        [Test]
        public void AContactOverlappingAPoleStillLeavesThePoleAtRest()
        {
            Assert.That(WireBendMath.Weight(SpanStart, SpanStart + 0.2f, 0.5f, SpanStart, SpanEnd), Is.EqualTo(0f));
            Assert.That(WireBendMath.Weight(SpanStart + 0.1f, SpanStart + 0.2f, 0.5f, SpanStart, SpanEnd), Is.EqualTo(1f));
        }

        [Test]
        public void ABodyIsAsWideAsItsExtentAlongTheWire()
        {
            Vector3 halfExtents = new Vector3(0.3f, 0.8f, 0.5f);
            Assert.That(WireBendMath.HalfExtentAlong(halfExtents, Quaternion.identity, Vector3.right), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(WireBendMath.HalfExtentAlong(halfExtents, Quaternion.identity, Vector3.forward), Is.EqualTo(0.5f).Within(1e-5f));
            // Turned a quarter turn, its depth now lies along the wire.
            Assert.That(WireBendMath.HalfExtentAlong(halfExtents, Quaternion.Euler(0f, 90f, 0f), Vector3.right), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void AWireAtAnAngleToTheBodyTakesBothSides()
        {
            Vector3 diagonal = new Vector3(1f, 0f, 1f);
            float expected = (0.3f + 0.5f) / Mathf.Sqrt(2f);
            Assert.That(WireBendMath.HalfExtentAlong(new Vector3(0.3f, 0.8f, 0.5f), Quaternion.identity, diagonal), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void TheSpanRunsBetweenTheNearestPolesEitherSide()
        {
            WireBendMath.SpanAround(new List<float> { 0f, 0.3f, 0.7f, 1f }, 0.5f, out float start, out float end);
            Assert.That(start, Is.EqualTo(0.3f));
            Assert.That(end, Is.EqualTo(0.7f));
        }

        [Test]
        public void AWireWithoutPolesBendsEndToEnd()
        {
            WireBendMath.SpanAround(new List<float>(), 0.4f, out float start, out float end);
            Assert.That(start, Is.EqualTo(0f));
            Assert.That(end, Is.EqualTo(1f));
        }
    }
}
