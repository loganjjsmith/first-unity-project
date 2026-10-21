using System.Collections.Generic;
using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// The shape of a wire bent by something pressing on it: a taut V, straight from each pole to the body,
    /// with the bottom rounded over the body's width so it wraps the body instead of pinching to a point.
    /// Distances are measured along the wire. Pure maths, so riding, the pull and the visible cable all agree
    /// on one shape.
    /// </summary>
    public static class WireBendMath
    {
        /// <summary>
        /// How much of the bend's displacement applies at <paramref name="distance"/> along the wire: 1 at the
        /// body's centre, falling in a straight line to 0 at the poles either side, with the corner at each
        /// edge of the body rounded off so the bottom of the V is a U as wide as the body.
        /// </summary>
        /// <param name="contactCentre">Where the body presses, as a distance along the wire.</param>
        /// <param name="contactHalfWidth">Half the body's width along the wire.</param>
        /// <param name="spanStart">The nearest pole (or wire end) before the contact.</param>
        /// <param name="spanEnd">The nearest pole (or wire end) after the contact.</param>
        public static float Weight(float distance, float contactCentre, float contactHalfWidth, float spanStart, float spanEnd)
        {
            if (distance <= spanStart || distance >= spanEnd)
                return 0f;

            float contactStart = Mathf.Max(spanStart, contactCentre - contactHalfWidth);
            float contactEnd = Mathf.Min(spanEnd, contactCentre + contactHalfWidth);

            return distance < contactCentre
                ? Leg(contactStart - distance, contactStart - spanStart, contactHalfWidth)
                : Leg(distance - contactEnd, spanEnd - contactEnd, contactHalfWidth);
        }

        /// <summary>
        /// One side of the V. <paramref name="outward"/> is how far past the body's edge toward the pole the
        /// point is (negative inside the body), and the leg runs straight from 1 at the edge to 0 at the pole.
        /// The corner at the edge is replaced by a parabola over <paramref name="rounding"/> either side of
        /// it that meets the flat inside and the straight leg with matching slopes, so there is no kink.
        /// </summary>
        static float Leg(float outward, float legLength, float rounding)
        {
            if (legLength <= 0f)
                return outward <= 0f ? 1f : 0f;

            float slope = 1f / legLength;
            float b = Mathf.Min(rounding, legLength * 0.5f);
            if (b < 1e-5f)
                return Mathf.Clamp01(1f - slope * Mathf.Max(0f, outward));

            if (outward <= -b)
                return 1f;
            if (outward >= b)
                return Mathf.Max(0f, 1f - slope * outward);
            float intoCorner = outward + b;
            return 1f - slope * intoCorner * intoCorner / (4f * b);
        }

        /// <summary>
        /// Half the size of a box along <paramref name="direction"/>: how far a body with these half
        /// extents, turned by <paramref name="rotation"/>, reaches along it either side of its centre. Used
        /// to size the contact from the body's own collider, so any body shape bends the wire by its width.
        /// </summary>
        public static float HalfExtentAlong(Vector3 halfExtents, Quaternion rotation, Vector3 direction)
        {
            if (direction.sqrMagnitude < 1e-8f)
                return 0f;

            direction.Normalize();
            return Mathf.Abs(Vector3.Dot(rotation * Vector3.right, direction)) * halfExtents.x
                 + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, direction)) * halfExtents.y
                 + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, direction)) * halfExtents.z;
        }

        /// <summary>
        /// The stretch of wire a bend at <paramref name="t"/> may move: from the last pole stop before it to
        /// the first after it, or to the wire's own ends (0 and 1) where there is none.
        /// </summary>
        public static void SpanAround(IReadOnlyList<float> stopTs, float t, out float start, out float end)
        {
            start = 0f;
            end = 1f;
            foreach (float stop in stopTs)
            {
                if (stop < t && stop > start)
                    start = stop;
                if (stop > t && stop < end)
                    end = stop;
            }
        }
    }
}
