using System.Collections.Generic;
using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Builds a round cable mesh through a list of points: a ring of vertices around each point, joined into
    /// a tube. Much cheaper than extruding a spline, because the points are already where the cable goes, so
    /// a bent wire can be rebuilt every frame. Buffers are kept between builds; the triangles are only
    /// rebuilt when the point or side count changes.
    /// </summary>
    public sealed class CableMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        int builtRings = -1;
        int builtSides = -1;

        /// <summary>
        /// Writes a tube of <paramref name="radius"/> through <paramref name="points"/> into
        /// <paramref name="mesh"/>, with <paramref name="sides"/> faces around. Open ended.
        /// </summary>
        public void Build(Mesh mesh, IReadOnlyList<Vector3> points, float radius, int sides)
        {
            int rings = points.Count;
            sides = Mathf.Max(3, sides);
            if (rings < 2)
            {
                mesh.Clear();
                return;
            }

            vertices.Clear();
            normals.Clear();
            uvs.Clear();

            // Each ring's orientation is carried along from the previous one (parallel transport), so the
            // tube never twists, whichever way the cable runs.
            Vector3 side = Perpendicular(TangentAt(points, 0));
            float along = 0f;
            for (int i = 0; i < rings; i++)
            {
                Vector3 tangent = TangentAt(points, i);
                side = Vector3.ProjectOnPlane(side, tangent);
                side = side.sqrMagnitude > 1e-8f ? side.normalized : Perpendicular(tangent);
                Vector3 other = Vector3.Cross(tangent, side);

                if (i > 0)
                    along += Vector3.Distance(points[i - 1], points[i]);

                // One extra vertex closes the ring, so the texture wraps without a seam.
                for (int s = 0; s <= sides; s++)
                {
                    float angle = s * 2f * Mathf.PI / sides;
                    Vector3 outward = Mathf.Cos(angle) * side + Mathf.Sin(angle) * other;
                    vertices.Add(points[i] + outward * radius);
                    normals.Add(outward);
                    uvs.Add(new Vector2((float)s / sides, along));
                }
            }

            bool topologyChanged = rings != builtRings || sides != builtSides;
            if (topologyChanged)
            {
                mesh.Clear();
                BuildTriangles(rings, sides);
                builtRings = rings;
                builtSides = sides;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (topologyChanged)
                mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        void BuildTriangles(int rings, int sides)
        {
            triangles.Clear();
            int stride = sides + 1;
            for (int i = 0; i < rings - 1; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = i * stride + s;
                    int b = a + stride;
                    // Wound so the faces point outward (Unity treats clockwise as front-facing).
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
            }
        }

        static Vector3 TangentAt(IReadOnlyList<Vector3> points, int i)
        {
            Vector3 before = points[Mathf.Max(0, i - 1)];
            Vector3 after = points[Mathf.Min(points.Count - 1, i + 1)];
            Vector3 tangent = after - before;
            return tangent.sqrMagnitude > 1e-12f ? tangent.normalized : Vector3.forward;
        }

        static Vector3 Perpendicular(Vector3 tangent)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(reference, tangent).normalized;
        }
    }
}
