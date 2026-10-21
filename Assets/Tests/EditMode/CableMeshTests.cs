using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MovementSandbox.Mechanics.Tests
{
    /// <summary>Pins the cable tube's geometry: right size, round, and facing outward so it is visible.</summary>
    public class CableMeshTests
    {
        const float Radius = 0.05f;
        const int Sides = 8;

        static Mesh Build(List<Vector3> points)
        {
            Mesh mesh = new Mesh();
            new CableMesh().Build(mesh, points, Radius, Sides);
            return mesh;
        }

        static List<Vector3> Straight(int count) =>
            Enumerable.Range(0, count).Select(i => new Vector3(i, 0f, 0f)).ToList();

        [Test]
        public void EveryPointGetsAClosedRing()
        {
            Mesh mesh = Build(Straight(5));
            Assert.That(mesh.vertexCount, Is.EqualTo(5 * (Sides + 1)));
            Assert.That(mesh.triangles.Length, Is.EqualTo(4 * Sides * 6));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void EveryVertexSitsOneRadiusFromTheCable()
        {
            List<Vector3> points = new List<Vector3> { Vector3.zero, new Vector3(1f, 0.5f, 0f), new Vector3(2f, 0f, 1f) };
            Mesh mesh = Build(points);
            Vector3[] vertices = mesh.vertices;
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 centre = points[v / (Sides + 1)];
                Assert.That(Vector3.Distance(vertices[v], centre), Is.EqualTo(Radius).Within(1e-5f));
            }
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void FacesPointOutward()
        {
            // A face pointing inward would be culled, and the cable would vanish from outside.
            Mesh mesh = Build(Straight(3));
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                Assert.That(Vector3.Dot(faceNormal, normals[triangles[i]]), Is.GreaterThan(0f), $"triangle {i / 3} faces inward");
            }
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void TooFewPointsMakesNoCable()
        {
            Mesh mesh = Build(new List<Vector3> { Vector3.zero });
            Assert.That(mesh.vertexCount, Is.EqualTo(0));
            Object.DestroyImmediate(mesh);
        }
    }
}
