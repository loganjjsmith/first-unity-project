using UnityEngine;

namespace MovementSandbox.Mechanics
{
    /// <summary>
    /// Tuning for one slingshot band preset. Make one asset per feel (floss / rubber / hyper) so presets
    /// can be swapped and compared without editing a prefab.
    /// </summary>
    /// <remarks>
    /// Two of the defaults are deliberate choices worth knowing:
    /// <list type="bullet">
    /// <item>Pull rate is its own field instead of <c>maxPull / pullDurationToMax</c>, so the cap no longer
    /// secretly sets the rate.</item>
    /// <item>Minimum stretch to launch is 0.5 m rather than 0.12 m, so a two-frame tap cannot fling the
    /// player across the map.</item>
    /// </list>
    /// The maths lives in <see cref="SlingshotMath"/>. This asset only holds numbers and samples the curve.
    ///
    /// <para><b>These defaults assume the current world scale</b> (1 unit =
    /// 25 cm, gravity -80). Stretch is in metres of that world, so if the game moves to real scale,
    /// <see cref="stiffness"/>, <see cref="maxStretch"/> and <see cref="minStretchToLaunch"/> all need
    /// rescaling, and <c>SlingshotConfigTests</c> will fail loudly to say so.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Movement Sandbox/Slingshot Config", fileName = "SlingshotConfig")]
    public sealed class SlingshotConfig : ScriptableObject
    {
        [Header("Stretch")]
        [Tooltip("Stretch cap, in metres. A pure cap: raising it does not make a given hold stronger.")]
        [SerializeField, Min(0.01f)] float maxStretch = 10f;

        [Tooltip("How fast stretch grows while the pull is held, in metres per second.")]
        [SerializeField, Min(0.01f)] float pullSpeed = 3.3333333f;

        [Tooltip("Releasing below this much stretch snaps back onto the wire instead of launching (metres).")]
        [SerializeField, Min(0f)] float minStretchToLaunch = 0.5f;

        [Header("Power")]
        [Tooltip("Launch speed in m/s per metre of stretch. This is the main range knob.")]
        [SerializeField, Min(0f)] float stiffness = 22f;

        [Tooltip("Band preset multiplier on speed. Range scales with its square, so 1.15 is about 32% more range.")]
        [SerializeField, Min(0f)] float elasticity = 1.15f;

        [Tooltip("Safety clamp on launch speed (m/s). Tuning should stay under it; it is not a feel knob.")]
        [SerializeField, Min(0f)] float speedCeiling = 320f;

        [Header("Stretch curve (optional)")]
        [Tooltip("Off means speed is exactly linear in stretch, which is what the playtested version did.")]
        [SerializeField] bool useStretchCurve = false;

        [Tooltip("Maps real metres of stretch to effective metres. Both axes are metres, so the cap stays a pure cap.")]
        [SerializeField] AnimationCurve stretchCurve = AnimationCurve.Linear(0f, 0f, 10f, 10f);

        [Header("Launch elevation")]
        [Tooltip("Lowest launch elevation in degrees. Negative aims downward.")]
        [SerializeField, Range(-90f, 90f)] float minLaunchPitch = -90f;

        [Tooltip("Highest launch elevation in degrees. 45 is the flat-ground maximum-range lob.")]
        [SerializeField, Range(-90f, 90f)] float maxLaunchPitch = 90f;

        [Tooltip("Degrees added to the aimed elevation before clamping. 0 launches exactly opposite the pull.")]
        [SerializeField, Range(-90f, 90f)] float launchPitchBias = 0f;

        public float MaxStretch => maxStretch;
        public float PullSpeed => pullSpeed;
        public float MinStretchToLaunch => minStretchToLaunch;

        /// <summary>Seconds of holding the pull to reach the cap, derived from the rate rather than setting it.</summary>
        public float SecondsToMaxStretch => pullSpeed > 0f ? maxStretch / pullSpeed : float.PositiveInfinity;

        /// <summary>
        /// Stretch in metres after holding the pull for <paramref name="heldSeconds"/>, capped.
        /// </summary>
        /// <remarks>
        /// The rate and the cap are separate fields, so raising the cap does not make a given hold
        /// stronger. If the rate were derived as <c>maxPull / pullDurationToMax</c>, the same
        /// one-second hold would be 2.5x stronger at cap 10 than at cap 4.
        ///
        /// Stretch is hold-driven rather than player-positioned: the player
        /// controls direction and release timing, not the distance. This lives on the config rather than in
        /// <see cref="SlingshotMath"/> because both numbers it needs are tuning, and putting it here keeps
        /// the cap applied in one place.
        /// </remarks>
        public float StretchAfterHold(float heldSeconds)
        {
            if (heldSeconds <= 0f || pullSpeed <= 0f)
                return 0f;

            return Mathf.Min(pullSpeed * heldSeconds, maxStretch);
        }

        /// <summary>The tuning as plain numbers, for <see cref="SlingshotMath"/>.</summary>
        public SlingshotParams ToParams()
        {
            return new SlingshotParams(
                maxStretch,
                minStretchToLaunch,
                stiffness,
                elasticity,
                speedCeiling,
                minLaunchPitch,
                maxLaunchPitch,
                launchPitchBias);
        }

        /// <summary>
        /// Real metres of stretch shaped into effective metres. Identity unless a curve is enabled.
        /// </summary>
        public float EffectiveStretch(float rawStretchMetres)
        {
            float clamped = Mathf.Clamp(rawStretchMetres, 0f, maxStretch);
            if (!useStretchCurve || stretchCurve == null || stretchCurve.length == 0)
                return clamped;

            // Both axes are metres, so the output is capped exactly like the input. Without this a curve
            // drawn above the cap would push launch speed past what maxStretch claims is possible, and the
            // cap would stop being a cap. More power belongs in stiffness, not in a curve overshoot.
            return Mathf.Clamp(stretchCurve.Evaluate(clamped), 0f, maxStretch);
        }

        /// <summary>Launch velocity for a stretch vector, with this asset's curve applied.</summary>
        public Vector3 ComputeLaunchVelocity(Vector3 stretchVector)
        {
            SlingshotParams p = ToParams();
            float stretch = SlingshotMath.StretchMetres(in p, stretchVector);
            return SlingshotMath.ComputeLaunchVelocity(in p, stretchVector, EffectiveStretch(stretch));
        }

        /// <summary>Whether releasing at this stretch launches, for the charging state to gate on.</summary>
        public bool CanLaunch(float rawStretchMetres)
        {
            SlingshotParams p = ToParams();
            return SlingshotMath.CanLaunch(in p, Mathf.Clamp(rawStretchMetres, 0f, maxStretch));
        }

        void OnValidate()
        {
            // Keep Inspector values coherent here rather than repairing them at runtime, so what the
            // Inspector shows is what actually runs.
            if (minStretchToLaunch > maxStretch)
                minStretchToLaunch = maxStretch;

            if (minLaunchPitch > maxLaunchPitch)
                minLaunchPitch = maxLaunchPitch;

            if (useStretchCurve && stretchCurve != null && stretchCurve.length > 0)
            {
                // Both axes are metres, which is not the usual Unity idiom: most curve fields are a 0..1
                // shaping multiplier, and drawing one of those here would quietly cut a full pull to a
                // tenth of its power with nothing to say so. So check both axes, not just time.
                Keyframe last = stretchCurve[stretchCurve.length - 1];
                string label = string.IsNullOrEmpty(name) ? nameof(SlingshotConfig) : name;

                if (!Mathf.Approximately(last.time, maxStretch))
                {
                    Debug.LogWarning(
                        $"{label}: stretch curve ends at {last.time} m on the x axis but the cap is " +
                        $"{maxStretch} m. Move the last key to the cap, or the top of the pull will not " +
                        "shape as drawn.",
                        this);
                }

                if (!Mathf.Approximately(last.value, maxStretch))
                {
                    Debug.LogWarning(
                        $"{label}: stretch curve ends at {last.value} on the y axis, but that axis is " +
                        $"metres of effective stretch and should reach the cap of {maxStretch} m. A curve " +
                        "drawn 0..1 is not a multiplier here and would cut launch power to roughly " +
                        $"{(maxStretch > 0f ? 1f / maxStretch : 0f):P0} of full.",
                        this);
                }
            }
        }
    }
}
