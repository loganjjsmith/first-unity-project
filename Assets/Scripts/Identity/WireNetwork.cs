using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using MovementSandbox.Mechanics;

namespace MovementSandbox.Identity
{
    /// <summary>A knot of a wire that sits on a pole: the end of the line, or a midway support.</summary>
    public struct PoleStop
    {
        public float T;
        public Collider Pole;
        public Vector3 Point;
    }

    /// <summary>One way off a pole along a wire: which wire, where on it, and which way along it.</summary>
    public struct WireExit
    {
        public SplineContainer Wire;
        public float T;
        public float Sign;
        public Vector3 Direction;
    }

    /// <summary>
    /// The scene's wires and how they meet the poles: which knots sit on which pole, and every way off a
    /// pole along a wire. <see cref="WireRider"/> asks it the topology questions and keeps the riding to
    /// itself.
    /// </summary>
    /// <remarks>
    /// Attachment is found, not configured: a knot placed on a Pole's top is attached to that pole, so
    /// building a wire is just placing knots, with nothing extra to wire up per wire. Poles and wires don't
    /// move at runtime, so each wire's stops are worked out once and cached.
    /// </remarks>
    public sealed class WireNetwork
    {
        readonly LayerMask poleLayer;
        readonly float poleAttachRadius;
        readonly Dictionary<SplineContainer, List<PoleStop>> stopsCache = new Dictionary<SplineContainer, List<PoleStop>>();

        SplineContainer[] wires = new SplineContainer[0];
        int wiresFrame = -1;

        /// <param name="poleLayer">Layer the poles live on (PoleClimber's).</param>
        /// <param name="poleAttachRadius">How close a knot must be to a Pole to count as attached.</param>
        public WireNetwork(LayerMask poleLayer, float poleAttachRadius)
        {
            this.poleLayer = poleLayer;
            this.poleAttachRadius = poleAttachRadius;
        }

        /// <summary>Every active wire in the scene. Looked up at most once a frame, however many callers ask.</summary>
        public SplineContainer[] AllWires()
        {
            if (wiresFrame != Time.frameCount)
            {
                wires = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Exclude);
                wiresFrame = Time.frameCount;
            }
            return wires;
        }

        /// <summary>The knots of this wire that sit on a Pole.</summary>
        public List<PoleStop> StopsOf(SplineContainer wire)
        {
            if (stopsCache.TryGetValue(wire, out List<PoleStop> cached))
                return cached;

            List<PoleStop> stops = new List<PoleStop>();
            Spline spline = wire.Spline;
            for (int i = 0; spline != null && i < spline.Count; i++)
            {
                float3 local = spline[i].Position;
                Vector3 knot = wire.transform.TransformPoint(new Vector3(local.x, local.y, local.z));
                Collider pole = FindPoleAt(knot);
                if (pole == null)
                    continue;

                float knotT = spline.ConvertIndexUnit(i, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                stops.Add(new PoleStop { T = knotT, Pole = pole, Point = knot });
            }

            stopsCache[wire] = stops;
            return stops;
        }

        public bool TouchesPole(SplineContainer wire, Collider pole)
        {
            foreach (PoleStop stop in StopsOf(wire))
            {
                if (stop.Pole == pole)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The pole stop this move reaches, the nearest to <paramref name="fromT"/> if it reaches several.
        /// See <see cref="WireRiderMath.PassesStop"/> for which stops count.
        /// </summary>
        public bool TryFindReachedStop(SplineContainer wire, float fromT, float toT, float direction, out PoleStop reached)
        {
            reached = default;
            float bestGap = float.MaxValue;

            foreach (PoleStop stop in StopsOf(wire))
            {
                if (!WireRiderMath.PassesStop(stop.T, fromT, toT, direction))
                    continue;

                float gap = Mathf.Abs(stop.T - fromT);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    reached = stop;
                }
            }
            return bestGap < float.MaxValue;
        }

        /// <summary>
        /// Every way off <paramref name="pole"/> along any wire attached to it, except straight back along
        /// the wire just arrived on. A stop mid-wire offers both directions; an end offers only inward.
        /// Pass a null <paramref name="arrivedOn"/> to get every way off, as from a perch.
        /// </summary>
        public List<WireExit> ExitsFrom(Collider pole, SplineContainer arrivedOn, float arrivedT, float arrivedDirection)
        {
            List<WireExit> exits = new List<WireExit>();
            if (pole == null)
                return exits;

            foreach (SplineContainer wire in AllWires())
            {
                foreach (PoleStop stop in StopsOf(wire))
                {
                    if (stop.Pole != pole)
                        continue;

                    float3 rawTangent = wire.EvaluateTangent(stop.T);
                    Vector3 tangent = new Vector3(rawTangent.x, rawTangent.y, rawTangent.z);
                    if (tangent.sqrMagnitude < 1e-8f)
                        continue;
                    tangent.Normalize();

                    for (int s = 0; s < 2; s++)
                    {
                        float sign = s == 0 ? 1f : -1f;
                        if (sign > 0f && stop.T >= 0.999f)
                            continue;
                        if (sign < 0f && stop.T <= 0.001f)
                            continue;

                        bool isTheWayBack = wire == arrivedOn && Mathf.Abs(stop.T - arrivedT) < 0.001f && sign == -arrivedDirection;
                        if (isTheWayBack)
                            continue;

                        exits.Add(new WireExit { Wire = wire, T = stop.T, Sign = sign, Direction = tangent * sign });
                    }
                }
            }
            return exits;
        }

        public static List<Vector3> Directions(List<WireExit> exits)
        {
            List<Vector3> directions = new List<Vector3>(exits.Count);
            foreach (WireExit exit in exits)
                directions.Add(exit.Direction);
            return directions;
        }

        Collider FindPoleAt(Vector3 point)
        {
            Collider best = null;
            float bestDistance = float.MaxValue;
            foreach (Collider candidate in Physics.OverlapSphere(point, poleAttachRadius, poleLayer, QueryTriggerInteraction.Ignore))
            {
                if (!candidate.CompareTag("Pole"))
                    continue;

                float distance = Vector3.Distance(point, candidate.ClosestPoint(point));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }
    }
}
