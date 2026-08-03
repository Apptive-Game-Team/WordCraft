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
        private static readonly Color BarBackColor = new Color(0.04f, 0.04f, 0.05f, 0.85f);
        private static readonly Color QueueColor = new Color(0.45f, 0.85f, 1.00f, 0.95f);
        private static readonly Color RallyColor = new Color(0.55f, 1.00f, 0.60f, 0.85f);

        // Overlay geometry, in grid cells. A bar is the same size whatever the art
        // under it, so a row of damaged units reads as one row.
        private const float BarHeight = 0.16f;
        private const int OverlayOrder = 50;

        /// <summary>Where the imported WordOnline sprites live, relative to a Resources folder.</summary>
        public const string SpriteFolder = "Art/Sprites/";

        public Camera Cam { get; private set; }

        private MatchRunner runner;
        private Sprite disc;
        private Sprite square;
        private readonly List<SpriteRenderer> views = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> rings = new List<SpriteRenderer>();

        // Overlays, parallel to views. Kept off the entity's own transform because
        // authored art is scaled to fit its footprint and a bar must not inherit that.
        private readonly List<SpriteRenderer> barBacks = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> barFills = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> queueFills = new List<SpriteRenderer>();

        // Rally markers are a pool, not one per entity: only the selected buildings
        // that have set one are ever drawn.
        private readonly List<SpriteRenderer> rallyMarkers = new List<SpriteRenderer>();

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

            int markers = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                SpriteRenderer sr = views[i];
                if (!e.Alive)
                {
                    sr.enabled = false;
                    rings[i].enabled = false;
                    Overlays(i, e, Vector2.zero, picked: false);
                    continue;
                }

                bool picked = Selection.Instance != null && Selection.Instance.Contains(i);
                sr.enabled = true;
                rings[i].enabled = picked;
                Vector2 p = runner.DrawPosition(i);
                sr.transform.position = new Vector3(p.x, p.y, 0f);
                Overlays(i, e, p, picked);

                // A site under construction reads as a ghost until it finishes.
                if (e.Kind == EntityKind.Building)
                {
                    Color c = sr.color;
                    c.a = e.BuildTicksLeft > 0 ? 0.35f : 1f;
                    sr.color = c;
                }

                if (picked && e.Kind == EntityKind.Building && e.HasRallyPoint)
                {
                    Rally(markers++, MatchRunner.ToView(e.RallyPoint));
                }
            }

            for (int i = markers; i < rallyMarkers.Count; i++) rallyMarkers[i].enabled = false;
        }

        /// <summary>
        /// Health bar over anything hurt or selected, production progress under a
        /// selected building. Both are read straight off the entity every frame and
        /// held nowhere, so the overlay cannot drift from what the simulation says.
        /// </summary>
        private void Overlays(int i, Entity e, Vector2 p, bool picked)
        {
            bool node = e.Kind == EntityKind.ResourceNode;
            bool hurt = e.Hp < e.MaxHp;
            bool bar = e.Alive && !node && (hurt || picked);

            barBacks[i].enabled = bar;
            barFills[i].enabled = bar;
            if (bar)
            {
                float width = e.Kind == EntityKind.Building ? 2.2f : 1.1f;
                float y = p.y + (e.Kind == EntityKind.Building ? 1.7f : 0.75f);
                float ratio = e.MaxHp > 0 ? Mathf.Clamp01(e.Hp / (float)e.MaxHp) : 0f;

                Place(barBacks[i], p.x, y, width, 1f);
                Place(barFills[i], p.x, y, width, ratio);
                barFills[i].color = ratio > 0.5f ? new Color(0.35f, 0.9f, 0.4f)
                    : ratio > 0.25f ? new Color(0.95f, 0.85f, 0.3f)
                    : new Color(0.95f, 0.35f, 0.3f);
            }

            // ProduceTicksLeft counts down and is zero in the tick before a queued
            // unit starts, so an empty bar means queued-not-started, not finished.
            bool queue = e.Alive && picked && e.Kind == EntityKind.Building && e.QueueCount > 0;
            queueFills[i].enabled = queue;
            if (queue)
            {
                float done = e.ProduceTicksLeft == 0
                    ? 0f
                    : (World.ProduceTicks - e.ProduceTicksLeft) / (float)World.ProduceTicks;
                Place(queueFills[i], p.x, p.y + 1.45f, 2.2f, done);
            }
        }

        /// <summary>Grows a bar from its left edge rather than its middle.</summary>
        private static void Place(SpriteRenderer sr, float x, float y, float width, float fill)
        {
            float w = width * fill;
            sr.transform.position = new Vector3(x - (width - w) * 0.5f, y, 0f);
            sr.transform.localScale = new Vector3(w, BarHeight, 1f);
        }

        private void Rally(int index, Vector2 point)
        {
            while (rallyMarkers.Count <= index)
            {
                var made = NewRenderer("Rally", square, RallyColor, OverlayOrder);
                made.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
                made.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                rallyMarkers.Add(made);
            }
            rallyMarkers[index].transform.position = new Vector3(point.x, point.y, 0f);
            rallyMarkers[index].enabled = true;
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

            barBacks.Add(Overlay("HpBack", BarBackColor, OverlayOrder));
            barFills.Add(Overlay("HpFill", Color.white, OverlayOrder + 1));
            queueFills.Add(Overlay("Queue", QueueColor, OverlayOrder + 1));

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

        private SpriteRenderer Overlay(string name, Color color, int order)
        {
            SpriteRenderer sr = NewRenderer(name, square, color, order);
            sr.enabled = false;
            return sr;
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
