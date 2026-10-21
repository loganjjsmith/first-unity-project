using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// Lets a wire bend when something presses on it, and spring back when it lets go. The wire's rest
    /// spline is never edited - poles, stops and riding positions are all measured along it - so the bend
    /// is an offset on top: <see cref="PositionAt"/> is rest position plus bend, and everything that needs
    /// the wire's real shape (riders, the slingshot pull, the visible cable) reads it from here. With no bend
    /// the offset is exactly zero.
    /// </summary>
    /// <remarks>
    /// While bent, the wire's own mesh is hidden and a bent copy is built with the same radius and sides
    /// and drawn directly each frame, so nothing is added to the scene. Distances along the wire are in the
    /// spline's local units; the wire's transform is assumed to be uniformly scaled.
    /// </remarks>
    [DefaultExecutionOrder(-50)] // steps the spring before riders read the shape this frame
    [RequireComponent(typeof(SplineContainer))]
    [RequireComponent(typeof(SplineExtrude))]
    [RequireComponent(typeof(MeshRenderer))]
    public class BendableWire : MonoBehaviour
    {
        [Tooltip("How hard the wire pulls back to rest after a release. Higher wobbles faster; 400 is about " +
                 "three wobbles a second.")]
        public float SpringStiffness = 400f;

        [Tooltip("How quickly the wobble dies away. 8 settles in about a second; 0 would wobble forever.")]
        public float SpringDamping = 8f;

        [Tooltip("Spacing, in metres, of the points the bent cable is rebuilt through. Smaller is smoother " +
                 "and costs more per frame.")]
        public float SampleSpacing = 0.5f;

        const float SettledAmplitude = 0.001f;
        const float SettledSpeed = 0.01f;

        SplineContainer container;
        SplineExtrude extrude;
        MeshRenderer meshRenderer;
        Mesh bentMesh;
        readonly CableMesh cable = new CableMesh();
        readonly List<Vector3> restPoints = new List<Vector3>();
        readonly List<float> restDistances = new List<float>();
        readonly List<Vector3> bentPoints = new List<Vector3>();

        bool held;
        float contactCentre;
        float contactHalfWidth;
        float spanStart;
        float spanEnd;
        Vector3 displacement;
        float amplitude;
        float amplitudeVelocity;
        bool wasBent;

        /// <summary>True while something presses on the wire or it is still springing back.</summary>
        public bool IsBent => held || amplitude != 0f || amplitudeVelocity != 0f;

        float Scale => Mathf.Abs(transform.lossyScale.x);

        void Awake()
        {
            container = GetComponent<SplineContainer>();
            extrude = GetComponent<SplineExtrude>();
            meshRenderer = GetComponent<MeshRenderer>();
            bentMesh = new Mesh { name = name + " (bent)" };
            bentMesh.MarkDynamic();
        }

        void OnDestroy()
        {
            Destroy(bentMesh);
        }

        void OnDisable()
        {
            held = false;
            amplitude = amplitudeVelocity = 0f;
            if (meshRenderer != null)
                meshRenderer.enabled = true;
            wasBent = false;
        }

        /// <summary>
        /// Presses on the wire this frame. Call every frame while pressing; <see cref="Release"/> lets go.
        /// </summary>
        /// <param name="t">The rider's position on the wire (0..1 along the spline).</param>
        /// <param name="contactShift">How far the body's centre sits from <paramref name="t"/> along the wire, in
        /// metres, positive toward increasing t. The contact is centred on the body, not on the rider's root.</param>
        /// <param name="halfWidth">Half the body's width along the wire, in metres.</param>
        /// <param name="spanStartT">The pole or wire end before the contact; the wire does not move there.</param>
        /// <param name="spanEndT">The pole or wire end after the contact.</param>
        /// <param name="worldDisplacement">How far the contact is pushed from rest, in world space.</param>
        public void Hold(float t, float contactShift, float halfWidth, float spanStartT, float spanEndT, Vector3 worldDisplacement)
        {
            float perMetre = Scale > 0f ? 1f / Scale : 1f;
            contactCentre = DistanceAt(t) + contactShift * perMetre;
            contactHalfWidth = halfWidth * perMetre;
            spanStart = DistanceAt(spanStartT);
            spanEnd = DistanceAt(spanEndT);
            displacement = worldDisplacement;
            amplitude = 1f;
            amplitudeVelocity = 0f;
            held = true;
        }

        /// <summary>Lets go: the wire springs back from wherever it was held, with a wobble.</summary>
        public void Release()
        {
            held = false;
        }

        /// <summary>How far the bend moves the wire at <paramref name="t"/>, in world space.</summary>
        public Vector3 Offset(float t)
        {
            if (!IsBent)
                return Vector3.zero;

            float weight = WireBendMath.Weight(DistanceAt(t), contactCentre, contactHalfWidth, spanStart, spanEnd);
            return displacement * (amplitude * weight);
        }

        /// <summary>The wire's actual world position at <paramref name="t"/>: rest position plus the bend.</summary>
        public Vector3 PositionAt(float t)
        {
            float3 rest = container.EvaluatePosition(t);
            return new Vector3(rest.x, rest.y, rest.z) + Offset(t);
        }

        void Update()
        {
            if (held || !IsBent)
                return;

            Spring.Step(ref amplitude, ref amplitudeVelocity, SpringStiffness, SpringDamping, Time.deltaTime);
            if (Mathf.Abs(amplitude) < SettledAmplitude && Mathf.Abs(amplitudeVelocity) < SettledSpeed)
                amplitude = amplitudeVelocity = 0f;
        }

        void LateUpdate()
        {
            bool bent = IsBent;
            if (bent != wasBent)
            {
                meshRenderer.enabled = !bent;
                wasBent = bent;
            }

            if (!bent)
                return;

            RebuildBentMesh();
            RenderParams renderParams = new RenderParams(meshRenderer.sharedMaterial)
            {
                shadowCastingMode = meshRenderer.shadowCastingMode,
                receiveShadows = meshRenderer.receiveShadows,
                layer = gameObject.layer,
            };
            Graphics.RenderMesh(renderParams, bentMesh, 0, transform.localToWorldMatrix);
        }

        /// <summary>
        /// Rebuilds the visible cable as rest plus bend, in the wire's local space, with the wire's own
        /// radius and side count so it matches the real one. The rest points along the wire never change,
        /// so they are sampled once; each frame only adds the bend and rebuilds the tube.
        /// </summary>
        void RebuildBentMesh()
        {
            SampleRestShape();

            Vector3 localDisplacement = transform.InverseTransformVector(displacement) * amplitude;
            for (int i = 0; i < restPoints.Count; i++)
            {
                float weight = WireBendMath.Weight(restDistances[i], contactCentre, contactHalfWidth, spanStart, spanEnd);
                bentPoints[i] = restPoints[i] + localDisplacement * weight;
            }

            cable.Build(bentMesh, bentPoints, extrude.Radius, extrude.Sides);
        }

        void SampleRestShape()
        {
            if (restPoints.Count > 0)
                return;

            Spline rest = container.Spline;
            float length = rest.GetLength();
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.05f, SampleSpacing)) + 1);
            for (int i = 0; i < samples; i++)
            {
                float distance = length * i / (samples - 1);
                float t = rest.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
                float3 point = rest.EvaluatePosition(t);
                restPoints.Add(new Vector3(point.x, point.y, point.z));
                restDistances.Add(distance);
                bentPoints.Add(Vector3.zero);
            }
        }

        float DistanceAt(float t) =>
            container.Spline.ConvertIndexUnit(t, PathIndexUnit.Normalized, PathIndexUnit.Distance);
    }
}
