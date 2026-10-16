using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Synchronous, isolated portraits of the actual field mesh and paper material.</summary>
    public static class ModelPortrait
    {
        public const int Size = 256;
        private const int Layer = 31;
        private static readonly Dictionary<(Faction, Role, int), Texture2D> cache = new Dictionary<(Faction, Role, int), Texture2D>();
        private static readonly string[] Globals = { "_WcFogParams", "_WcSkyLight", "_WcEarthLight", "_WcBands", "_WcRim", "_WcHaze" };
        public static int CachedCount => cache.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ClearCache();
            Application.quitting -= ClearCache;
            Application.quitting += ClearCache;
        }
        public static void ClearCache()
        {
            foreach (Texture2D texture in cache.Values)
                if (texture != null) Object.DestroyImmediate(texture);
            cache.Clear();
        }
        public static Texture2D Get(Faction faction, Role role, int slot)
        {
            if ((int)faction < 0 || (int)faction > 5 || role == Role.None ||
                (int)role < 0 || (int)role > (int)Role.Tech || slot < 0 || !FactionData.Has(faction, role, slot)) return null;
            var key = (faction, role, slot);
            if (cache.TryGetValue(key, out Texture2D existing) && existing != null) return existing;
            Texture2D made = Render(faction, role, slot);
            cache[key] = made;
            return made;
        }
        private static Texture2D Render(Faction faction, Role role, int slot)
        {
            // Nothing survives a render except its texture. No scene camera,
            // simulation, shared material, mesh or quality setting is modified.
            var savedGlobals = new Vector4[Globals.Length];
            for (int i = 0; i < Globals.Length; i++) savedGlobals[i] = Shader.GetGlobalVector(Globals[i]);
            Light[] lights = Object.FindObjectsOfType<Light>();
            var masks = new int[lights.Length];
            for (int i = 0; i < lights.Length; i++) masks[i] = lights[i].cullingMask;
            Light savedSun = RenderSettings.sun;
            RenderTexture previous = RenderTexture.active;
            GameObject root = null;
            RenderTexture target = null;
            Texture2D result = null;
            bool complete = false;
            try
            {
                root = new GameObject("WordCraft Portrait Temporary") { hideFlags = HideFlags.HideAndDontSave };
                // Far from both the field and any objects on the preview layer.
                root.transform.position = new Vector3(10000f, 10000f, 10000f);
                EntityKind kind = FactionData.IsBuilding(role) ? EntityKind.Building : role == Role.Worker ? EntityKind.Worker : EntityKind.Unit;
                BodyRig rig = Models.Build(root.transform, faction, role, slot, kind, 0, false);
                rig.enabled = false;
                foreach (Transform child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = Layer;
                Renderer[] renderers = rig.GetComponentsInChildren<Renderer>();
                Bounds bounds = new Bounds(rig.transform.position, Vector3.zero);
                bool first = true;
                foreach (Renderer renderer in renderers)
                {
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                var cameraObject = new GameObject("Portrait Camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << Layer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.075f, 0.09f, 0.11f, 1f);
                camera.orthographic = true;
                camera.aspect = 1f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                camera.renderingPath = RenderingPath.Forward;
                // The bodies face +z: view from +z and their left, at the field's elevated angle.
                // Human entrances face -z; creatures and defensive weapons face +z.
                bool entrance = faction == Faction.Humans && kind == EntityKind.Building && role != Role.Defense;
                Quaternion angle = Quaternion.Euler(23f, entrance ? 30f : 150f, 0f);
                camera.transform.rotation = angle;
                float radius = Mathf.Max(0.2f, bounds.extents.magnitude);
                camera.transform.position = bounds.center - camera.transform.forward * (radius * 3f + 2f);
                float half = 0f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = Quaternion.Inverse(angle) * (corner - bounds.center);
                    half = Mathf.Max(half, Mathf.Abs(local.x), Mathf.Abs(local.y));
                }
                camera.orthographicSize = Mathf.Max(0.15f, half * 1.12f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = radius * 6f + 5f;

                for (int i = 0; i < lights.Length; i++) lights[i].cullingMask &= ~(1 << Layer);
                var lightObject = new GameObject("Portrait Light");
                lightObject.transform.SetParent(root.transform, false);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << Layer;
                light.color = Sky.SunColour;
                light.intensity = Sky.SunIntensity;
                light.transform.rotation = Quaternion.Euler(Sky.SunAngles);
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                RenderSettings.sun = light;
                Shader.SetGlobalVector("_WcFogParams", Vector4.zero);
                Shader.SetGlobalColor("_WcSkyLight", Sky.SkyLight);
                Shader.SetGlobalColor("_WcEarthLight", Sky.EarthLight);
                Shader.SetGlobalVector("_WcBands", Sky.Bands);
                Shader.SetGlobalColor("_WcRim", Sky.Rim);
                Shader.SetGlobalVector("_WcHaze", Vector4.zero);
                target = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                result = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                { name = "Model portrait " + faction + "/" + role + "/" + slot, hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                result.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                result.Apply(false, false);
                camera.targetTexture = null;
                complete = true;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderSettings.sun = savedSun;
                for (int i = 0; i < Globals.Length; i++) Shader.SetGlobalVector(Globals[i], savedGlobals[i]);
                for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].cullingMask = masks[i];
                if (root != null) Object.DestroyImmediate(root);
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (!complete && result != null) Object.DestroyImmediate(result);
            }
        }
    }
}
