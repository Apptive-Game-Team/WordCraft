using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Draws the world. Reads simulation state and nothing else writes back, so
    /// this whole file could be deleted without changing a single tick.
    ///
    /// Roster art where the faction has it, primitives where it does not: a disc
    /// for mobile things, a square for static ones, hue for owner. Several slots
    /// in docs/FACTIONS.md are still concepts, so the fallback is permanent
    /// furniture, not a stopgap. Art is drawn untinted because STYLE.md makes the
    /// palette the thing that identifies a faction.
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        public static readonly Color[] PeerColor =
        {
            new Color(0.35f, 0.62f, 1.00f),
            new Color(1.00f, 0.45f, 0.32f),
        };

        private static readonly Color NodeColor = new Color(1.00f, 0.82f, 0.25f);
        private static readonly Color GroundColor = new Color(0.11f, 0.12f, 0.14f);

        /// <summary>Where the imported WordOnline sprites live, relative to a Resources folder.</summary>
        public const string SpriteFolder = "Art/Sprites/";

        public Camera Cam { get; private set; }

        private MatchRunner runner;
        private Sprite disc;
        private Sprite square;
        private readonly List<SpriteRenderer> views = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> rings = new List<SpriteRenderer>();
        private readonly Dictionary<string, Sprite> art = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft View").AddComponent<MatchView>();

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            disc = MakeSprite(round: true);
            square = MakeSprite(round: false);

            float mid = MatchScenario.MapSize / 2f;
            Cam = new GameObject("Camera").AddComponent<Camera>();
            Cam.tag = "MainCamera";
            Cam.orthographic = true;
            Cam.orthographicSize = 18f;
            Cam.transform.position = new Vector3(mid, mid, -10f);
            Cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            DontDestroyOnLoad(Cam.gameObject);

            var ground = NewRenderer("Ground", square, GroundColor, -100);
            ground.transform.position = new Vector3(mid, mid, 0f);
            ground.transform.localScale = new Vector3(MatchScenario.MapSize, MatchScenario.MapSize, 1f);
        }

        private void Start() => runner = MatchRunner.Instance;

        /// <summary>
        /// LateUpdate, so the interpolated draw position is read after the frame's
        /// ticks have run rather than one frame behind them.
        /// </summary>
        private void LateUpdate()
        {
            if (runner == null) return;
            World world = runner.World;

            for (int i = views.Count; i < world.EntityCount; i++) views.Add(Create(world, world.GetEntity(i)));

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                SpriteRenderer sr = views[i];
                if (!e.Alive)
                {
                    sr.enabled = false;
                    rings[i].enabled = false;
                    continue;
                }

                sr.enabled = true;
                rings[i].enabled = Selection.Instance != null && Selection.Instance.Contains(i);
                Vector2 p = runner.DrawPosition(i);
                sr.transform.position = new Vector3(p.x, p.y, 0f);

                // A site under construction reads as a ghost until it finishes.
                if (e.Kind == EntityKind.Building)
                {
                    Color c = sr.color;
                    c.a = e.BuildTicksLeft > 0 ? 0.35f : 1f;
                    sr.color = c;
                }
            }
        }

        private SpriteRenderer Create(World world, Entity e)
        {
            Color owner = e.Owner >= 0 && e.Owner < PeerColor.Length ? PeerColor[e.Owner] : Color.gray;
            Sprite shape = e.Kind == EntityKind.Unit || e.Kind == EntityKind.Worker ? disc : square;
            Color color;
            float scale;
            float spin = 0f;

            switch (e.Kind)
            {
                case EntityKind.Worker:
                    color = Color.Lerp(owner, Color.white, 0.45f);
                    scale = 0.6f;
                    break;
                case EntityKind.Unit:
                    color = owner;
                    scale = 0.9f;
                    break;
                case EntityKind.Building:
                    color = owner;
                    scale = 1.6f;
                    break;
                default:
                    color = NodeColor;
                    scale = 0.9f;
                    // Rotated square: a diamond reads as "not a building" at a glance.
                    spin = 45f;
                    break;
            }

            Sprite drawn = ArtFor(world, e);
            var sr = NewRenderer(Label(world, e), drawn != null ? drawn : shape,
                drawn != null ? Color.white : color, e.Kind == EntityKind.Building ? 5 : 10);
            sr.transform.localScale = drawn != null ? FitScale(drawn, e) : new Vector3(scale, scale, 1f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, drawn != null ? 0f : spin);

            var ring = NewRenderer("Ring", shape, new Color(1f, 1f, 1f, 0.75f), sr.sortingOrder - 1);
            ring.transform.SetParent(sr.transform, worldPositionStays: false);
            ring.transform.localScale = Vector3.one * 1.35f;
            ring.enabled = false;
            rings.Add(ring);

            return sr;
        }

        /// <summary>Roster art for this entity, or null when the slot has none yet.</summary>
        private Sprite ArtFor(World world, Entity e)
        {
            if (e.Owner < 0 || e.Role == Role.None) return null;
            string file = FactionData.Sprite(world.FactionOf(e.Owner), e.Role);
            if (file.Length == 0) return null;

            if (!art.TryGetValue(file, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>(SpriteFolder + file);
                art[file] = sprite; // cache the miss too, so a bad name is one failed load
            }
            return sprite;
        }

        private static string Label(World world, Entity e) =>
            e.Owner < 0 ? e.Kind.ToString() : FactionData.Name(world.FactionOf(e.Owner), e.Role);

        /// <summary>
        /// Fits authored art to its role's footprint in cells. The source sprites
        /// were drawn for another game at another pixels-per-unit, so their own
        /// size means nothing here.
        /// </summary>
        private static Vector3 FitScale(Sprite sprite, Entity e)
        {
            float cells;
            switch (e.Role)
            {
                case Role.Base: cells = 4.0f; break;
                case Role.Production: cells = 3.0f; break;
                case Role.Defense: cells = 2.5f; break;
                case Role.Worker: cells = 1.2f; break;
                default: cells = 1.8f; break;
            }

            Vector2 size = sprite.bounds.size;
            float longest = Mathf.Max(size.x, size.y);
            float k = longest > 0f ? cells / longest : 1f;
            return new Vector3(k, k, 1f);
        }

        private SpriteRenderer NewRenderer(string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>One texture per shape, generated once. 1 sprite unit is 1 grid cell.</summary>
        private static Sprite MakeSprite(bool round)
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            float radius = size / 2f - 1f;
            var centre = new Vector2((size - 1) / 2f, (size - 1) / 2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inside = !round || Vector2.Distance(new Vector2(x, y), centre) <= radius;
                    pixels[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
