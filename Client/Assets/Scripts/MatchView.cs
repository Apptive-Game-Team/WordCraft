using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Draws the world. Reads simulation state and nothing else writes back, so
    /// this whole file could be deleted without changing a single tick.
    ///
    /// Shapes and colours only: a disc for mobile things, a square for static
    /// ones, hue for owner. Real art is content work that has to wait for the
    /// per-file ownership check on the WordOnline assets.
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

        public Camera Cam { get; private set; }

        private MatchRunner runner;
        private Sprite disc;
        private Sprite square;
        private readonly List<SpriteRenderer> views = new List<SpriteRenderer>();

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

            for (int i = views.Count; i < world.EntityCount; i++) views.Add(Create(world.GetEntity(i)));

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                SpriteRenderer sr = views[i];
                if (!e.Alive)
                {
                    sr.enabled = false;
                    continue;
                }

                sr.enabled = true;
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

        private SpriteRenderer Create(Entity e)
        {
            Color owner = e.Owner >= 0 && e.Owner < PeerColor.Length ? PeerColor[e.Owner] : Color.gray;

            switch (e.Kind)
            {
                case EntityKind.Worker:
                {
                    var sr = NewRenderer("Worker", disc, Color.Lerp(owner, Color.white, 0.45f), 10);
                    sr.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
                    return sr;
                }
                case EntityKind.Unit:
                {
                    var sr = NewRenderer("Unit", disc, owner, 10);
                    sr.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                    return sr;
                }
                case EntityKind.Building:
                {
                    var sr = NewRenderer("Building", square, owner, 5);
                    sr.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
                    return sr;
                }
                default:
                {
                    // Rotated square: a diamond reads as "not a building" at a glance.
                    var sr = NewRenderer("Node", square, NodeColor, 5);
                    sr.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                    return sr;
                }
            }
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
