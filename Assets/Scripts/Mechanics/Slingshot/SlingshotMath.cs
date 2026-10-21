using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Tuning the launch formula needs, as plain numbers.
    /// </summary>
    /// <remarks>
    /// This is a struct rather than a direct <see cref="SlingshotConfig"/> reference so the formula
    /// has no Unity-native dependencies: <c>AnimationCurve</c> and <c>ScriptableObject</c> are backed by
    /// native objects and cannot be constructed in a plain EditMode test, but <c>Vector3</c> and
    /// <c>Mathf</c> are pure managed code. The config samples its curve and hands the result over.
    ///
    /// <para>Normally you get one from <see cref="SlingshotConfig.ToParams"/>. A default-constructed
    /// instance -- <c>default</c>, <c>new SlingshotParams()</c>, or an element of a freshly allocated
    /// array -- has <see cref="MaxStretch"/> 0, so it is <b>inert</b>: stretch always reads as zero and no
    /// launch is ever possible. That is deliberate and pinned by a test. It is the safe direction to fail,
    /// but it fails silently, so build these from a config rather than field by field. The same shape
    /// caught us out once already in <see cref="RagdollRecoverySettings"/>, where a zero
    /// <c>MaxTime</c> ended every ragdoll flight on its first frame.</para>
    /// </remarks>
    public readonly struct SlingshotParams
    {
        /// <summary>Stretch cap in metres. A pure cap: it does not affect how fast the pull grows.</summary>
        public readonly float MaxStretch;

        /// <summary>Below this many metres of stretch, releasing snaps back instead of launching.</summary>
        public readonly float MinStretchToLaunch;

        /// <summary>Launch speed in m/s per metre of (curve-shaped) stretch. An ideal spring's sqrt(k/m).</summary>
        public readonly float Stiffness;

        /// <summary>Band preset multiplier. Scales speed, so range scales with its square.</summary>
        public readonly float Elasticity;

        /// <summary>Safety clamp on launch speed, m/s. Tuning should stay below it; it is not a design knob.</summary>
        public readonly float SpeedCeiling;

        /// <summary>Lowest launch elevation in degrees, after <see cref="LaunchPitchBias"/>.</summary>
        public readonly float MinLaunchPitch;

        /// <summary>Highest launch elevation in degrees, after <see cref="LaunchPitchBias"/>.</summary>
        public readonly float MaxLaunchPitch;

        /// <summary>Degrees added to the stretch vector's own elevation before clamping. 0 keeps it as aimed.</summary>
        public readonly float LaunchPitchBias;

        public SlingshotParams(
            float maxStretch,
            float minStretchToLaunch,
            float stiffness,
            float elasticity,
            float speedCeiling,
            float minLaunchPitch = -90f,
            float maxLaunchPitch = 90f,
            float launchPitchBias = 0f)
        {
            MaxStretch = maxStretch;
            MinStretchToLaunch = minStretchToLaunch;
            Stiffness = stiffness;
            Elasticity = elasticity;
            SpeedCeiling = speedCeiling;
            MinLaunchPitch = minLaunchPitch;
            MaxLaunchPitch = maxLaunchPitch;
            LaunchPitchBias = launchPitchBias;
        }
    }

    /// <summary>
    /// The slingshot launch formula. Pure static maths, no state, no scene access, so it is unit-testable
    /// and there is exactly one place where launch power is decided.
    /// </summary>
    /// <remarks>
    /// Design lock: launch energy comes from metres of stretch, and speed is linear in stretch, which is
    /// what an ideal spring does (v = x * sqrt(k/m)).
    ///
    /// Five failure modes are designed out here rather than guarded against, and
    /// <c>SlingshotMathTests</c> pins each one:
    /// <list type="number">
    /// <item>One flight model only. Switching models at a speed threshold would let a longer pull travel
    /// shorter. Speed here is monotonic in stretch.</item>
    /// <item>Launch direction comes from the stretch vector, not raw camera pitch.</item>
    /// <item>The curve is evaluated in metres (see <see cref="SlingshotConfig.EffectiveStretch"/>), so
    /// <see cref="SlingshotParams.MaxStretch"/> is a pure cap and does not rescale the curve.</item>
    /// <item>Pull rate is its own number, so changing the cap does not change how fast the pull grows.</item>
    /// <item>The caller applies the returned velocity to every ragdoll body, not just the torso.</item>
    /// </list>
    /// </remarks>
    public static class SlingshotMath
    {
        /// <summary>Below this squared length a stretch vector has no usable direction.</summary>
        const float DirectionEpsilonSqr = 1e-8f;

        /// <summary>Below this a flattened direction is treated as straight up or straight down.</summary>
        const float FlatEpsilonSqr = 1e-8f;

        /// <summary>
        /// Raw stretch in metres: how far the player has been pulled from its rest point on the wire,
        /// clamped to the cap. NaN or infinite input reads as zero rather than propagating.
        /// </summary>
        public static float StretchMetres(in SlingshotParams p, Vector3 stretchVector)
        {
            float sqr = stretchVector.sqrMagnitude;

            // PhysX and a diverging ragdoll are the only real NaN sources, but a stretch vector can be
            // derived from a body position, so check here where physics data enters the formula.
            if (float.IsNaN(sqr) || float.IsInfinity(sqr))
                return 0f;

            return Mathf.Clamp(Mathf.Sqrt(sqr), 0f, Mathf.Max(0f, p.MaxStretch));
        }

        /// <summary>
        /// Whether releasing at this much stretch should launch. A deliberate minimum, so a tap that
        /// lasts two frames cannot fling the player across the map.
        /// </summary>
        public static bool CanLaunch(in SlingshotParams p, float stretchMetres)
        {
            return stretchMetres >= Mathf.Max(0f, p.MinStretchToLaunch);
        }

        /// <summary>
        /// Launch speed in m/s for a given curve-shaped stretch. Linear in stretch before the curve,
        /// and flat once the safety ceiling binds.
        /// </summary>
        public static float LaunchSpeed(in SlingshotParams p, float effectiveStretchMetres)
        {
            float raw = p.Stiffness * p.Elasticity * Mathf.Max(0f, effectiveStretchMetres);
            return Mathf.Min(raw, Mathf.Max(0f, p.SpeedCeiling));
        }

        /// <summary>
        /// Unit launch direction: opposite the stretch, with its elevation biased and clamped into the
        /// configured pitch range. Returns <see cref="Vector3.zero"/> when the stretch has no direction.
        /// </summary>
        /// <remarks>
        /// The pitch range exists because the pull direction is derived from where the camera looks, so a
        /// camera pitch clamp becomes a launch elevation clamp. Tied together, that would cap
        /// launches at about +27 degrees and put the 45-degree maximum-range lob out of reach. Keeping
        /// elevation as its own clamped number means the camera's limits and the launch's limits can be
        /// tuned separately.
        /// </remarks>
        public static Vector3 LaunchDirection(in SlingshotParams p, Vector3 stretchVector)
        {
            Vector3 launch = -stretchVector;
            float sqr = launch.sqrMagnitude;
            if (float.IsNaN(sqr) || float.IsInfinity(sqr) || sqr < DirectionEpsilonSqr)
                return Vector3.zero;

            launch /= Mathf.Sqrt(sqr);

            float minPitch = Mathf.Min(p.MinLaunchPitch, p.MaxLaunchPitch);
            float maxPitch = Mathf.Max(p.MinLaunchPitch, p.MaxLaunchPitch);

            Vector3 flat = new Vector3(launch.x, 0f, launch.z);
            if (flat.sqrMagnitude < FlatEpsilonSqr)
            {
                // Straight up or straight down: there is no azimuth to preserve, so only clamp the sign.
                float vertical = Mathf.Clamp(launch.y >= 0f ? 90f : -90f, minPitch, maxPitch);
                return ShapeDirection(Vector3.forward, vertical);
            }

            flat.Normalize();
            float pitch = Mathf.Atan2(launch.y, Vector3.Dot(launch, flat)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch + p.LaunchPitchBias, minPitch, maxPitch);
            return ShapeDirection(flat, pitch);
        }

        /// <summary>
        /// The launch velocity for a stretch vector, with the curve already applied to its magnitude.
        /// Direction comes from the vector, speed from the curve-shaped metres.
        /// </summary>
        public static Vector3 ComputeLaunchVelocity(in SlingshotParams p, Vector3 stretchVector, float effectiveStretchMetres)
        {
            // The minimum is enforced here rather than left to the caller. A release below it has to snap
            // back to the wire, and "small but non-zero velocity" would let
            // any two-frame tap launch. Callers still use CanLaunch to pick which way the mode goes;
            // they just cannot get a launch velocity by forgetting to.
            if (!CanLaunch(in p, StretchMetres(in p, stretchVector)))
                return Vector3.zero;

            return LaunchDirection(in p, stretchVector) * LaunchSpeed(in p, effectiveStretchMetres);
        }

        /// <summary>
        /// The launch velocity for a stretch vector with no curve shaping (an identity curve).
        /// The config overload applies its curve first.
        /// </summary>
        public static Vector3 ComputeLaunchVelocity(in SlingshotParams p, Vector3 stretchVector)
        {
            return ComputeLaunchVelocity(in p, stretchVector, StretchMetres(in p, stretchVector));
        }

        /// <summary>Rebuilds a unit vector from a flat unit direction and an elevation in degrees.</summary>
        static Vector3 ShapeDirection(Vector3 flatUnit, float pitchDegrees)
        {
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            return flatUnit * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
        }
    }
}
