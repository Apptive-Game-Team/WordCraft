using UnityEngine;
using UnityEngine.Rendering;

namespace WordCraft.View
{
    /// <summary>
    /// The light over the field and the quality settings that let it show: one
    /// sun with soft shadows, a near-black Unity ambient, the camera's sky, and
    /// the shading globals every Paper.cginc shader reads.
    ///
    /// Everything is set from code because the client boots from an empty scene
    /// and the project's quality presets were never chosen for a lit 3D field:
    /// the default levels ship with shadows off and no anti-aliasing, and a field
    /// of flat-shaded facets without either is a jagged board with nothing
    /// standing on it.
    ///
    /// The tuning values are public static fields rather than constants so the
    /// lead can adjust them live from a debug hook; <see cref="Update"/> pushes
    /// them every frame, so a change shows on the next frame without a rebuild.
    /// </summary>
    public static class Sky
    {
        /// <summary>
        /// Where the sun stands, as Euler angles for its transform: 42 degrees
        /// above the horizon, turned 118 degrees about world y.
        ///
        /// .art/STYLE.md asks for one light from the upper left, and a camera that
        /// can turn has no fixed screen left, so this picks the world direction
        /// that is upper left for the default camera — yaw 0, looking along +z,
        /// tilted about 55 degrees down. Upper left of that picture, slightly in
        /// front of the paper, works out to a light coming from world -x (screen
        /// left) and a little from +z (screen top), so shadows fall right and
        /// toward the viewer, where the 2D bake painted them.
        ///
        /// 42 degrees rather than higher so that flat ground (n·l ≈ 0.67) sits
        /// squarely in the base band, a ramp of one level per cell (about 40
        /// degrees) turned to the sun climbs into the highlight band and one
        /// turned away drops into the shade band. A ramp then reads by its value
        /// alone, which is the style's whole method.
        /// </summary>
        public static Vector3 SunAngles = new Vector3(42f, 118f, 0f);

        /// <summary>The sun's colour. Warm, from the palette.</summary>
        public static Color SunColour = FieldPalette.Sun;

        /// <summary>
        /// The sun's strength. One: with the hemisphere ambient on top, flat grass
        /// lands at about 1.3 times its albedo, which is where the palette was
        /// picked to sit.
        /// </summary>
        public static float SunIntensity = 1f;

        /// <summary>_WcSkyLight: hemisphere ambient from above.</summary>
        public static Color SkyLight = FieldPalette.SkyLight;

        /// <summary>_WcEarthLight: hemisphere ambient from below.</summary>
        public static Color EarthLight = FieldPalette.EarthLight;

        /// <summary>
        /// _WcBands: x shade threshold, y highlight threshold, z shade value, w
        /// highlight value.
        ///
        /// x is 0.45, not the 0.25 first proposed, so that a cast shadow shows on
        /// flat ground. Paper.cginc scales n·l by z inside a shadow, so shadowed
        /// flat ground sits at 0.67 × 0.55 ≈ 0.37; with the threshold at 0.25 that
        /// is still the base band and every shadow on the map would vanish. At 0.45
        /// it falls into the shade band, while lit flat ground (0.67) and a ramp
        /// turned side-on to the sun (about 0.51) both stay in the base band.
        /// </summary>
        public static Vector4 Bands = new Vector4(0.45f, 0.78f, 0.55f, 1.18f);

        /// <summary>_WcRim: the lit paper edge, alpha its strength.</summary>
        public static Color Rim = FieldPalette.Rim;

        /// <summary>_WcHaze: what distance fades toward, the sky, and alpha how far the furthest ground goes.</summary>
        public static Color Haze = new Color(FieldPalette.Backdrop.r, FieldPalette.Backdrop.g, FieldPalette.Backdrop.b, 0.55f);

        /// <summary>World units from the camera where haze begins. Past most of a screen's worth of map, so the play area stays crisp.</summary>
        public static float HazeStart = 70f;

        /// <summary>World units from the camera where haze reaches Haze.a.</summary>
        public static float HazeEnd = 160f;

        /// <summary>The camera's clear colour, behind the diorama.</summary>
        public static Color Backdrop = FieldPalette.Backdrop;

        private static readonly int SkyLightId = Shader.PropertyToID("_WcSkyLight");
        private static readonly int EarthLightId = Shader.PropertyToID("_WcEarthLight");
        private static readonly int BandsId = Shader.PropertyToID("_WcBands");
        private static readonly int RimId = Shader.PropertyToID("_WcRim");
        private static readonly int HazeId = Shader.PropertyToID("_WcHaze");
        private static readonly int HazeRangeId = Shader.PropertyToID("_WcHazeRange");
        private static readonly int FieldId = Shader.PropertyToID("_WcField");

        private static Light sun;

        /// <summary>
        /// Makes the sun if there is none, and sets quality, ambient and the
        /// camera. Safe to call again: the second call finds the same sun and sets
        /// the same values.
        /// </summary>
        public static void Build(Camera cam)
        {
            if (sun == null)
            {
                var go = new GameObject("Sun");
                // Edit mode (a batch-mode screen capture) refuses DontDestroyOnLoad.
                if (Application.isPlaying) Object.DontDestroyOnLoad(go);
                sun = go.AddComponent<Light>();
            }

            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            // Full strength: Paper.cginc already stops a shadow at the shade band,
            // so weakening it here as well would make shadows fainter than the
            // shade on a face turned away from the sun, and the two must match.
            sun.shadowStrength = 1f;
            sun.renderMode = LightRenderMode.ForcePixel;
            ApplySun();
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = FieldPalette.Ambient;
            RenderSettings.fog = false; // Paper.cginc does distance haze itself, and passes nofog
            RenderSettings.skybox = null;

            // The default quality levels ship with shadows off and no MSAA. Set
            // here, not in QualitySettings.asset, so a player built from any
            // quality level still looks like the game.
            QualitySettings.shadows = ShadowQuality.All;
            // Very high since #187: the strata faces and the props are small
            // facets, and a rubble stone's shadow at High was a smudge.
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowDistance = 90f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.shadowCascade2Split = 0.33f;
            QualitySettings.antiAliasing = 4;

            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Backdrop;
                cam.allowMSAA = true;
            }

            Push();
        }

        /// <summary>
        /// Once a frame: pushes the shading globals, and re-applies the sun and the
        /// backdrop so a value tuned at runtime shows at once. Leaves the _WcFog
        /// globals alone; Fog.cs owns those.
        /// </summary>
        public static void Update(Camera cam)
        {
            if (sun != null) ApplySun();
            if (cam != null) cam.backgroundColor = Backdrop;
            Push();
        }

        private static void ApplySun()
        {
            sun.transform.rotation = Quaternion.Euler(SunAngles);
            sun.color = SunColour;
            sun.intensity = SunIntensity;
        }

        private static void Push()
        {
            Shader.SetGlobalColor(SkyLightId, SkyLight);
            Shader.SetGlobalColor(EarthLightId, EarthLight);
            Shader.SetGlobalVector(BandsId, Bands);
            Shader.SetGlobalColor(RimId, Rim);
            Shader.SetGlobalColor(HazeId, Haze);
            float span = Mathf.Max(1f, HazeEnd - HazeStart);
            Shader.SetGlobalVector(HazeRangeId, new Vector4(HazeStart, 1f / span, 0f, 0f));
            // The map's size, so Paper.cginc's WcEdgeFade knows where the map ends.
            Shader.SetGlobalVector(FieldId, new Vector4(Field.Cells, 0f, 0f, 0f));
        }
    }
}
