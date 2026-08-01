using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// What the local player has selected. Purely local: selection is never sent
    /// to the peer, so two players can hold different selections without the
    /// simulations differing by a bit.
    ///
    /// Drag a box to take units and workers, click to take any one owned entity
    /// including a building. Hold shift to add.
    /// </summary>
    public sealed class Selection : MonoBehaviour
    {
        public static Selection Instance { get; private set; }

        /// <summary>Screen distance below which a drag counts as a click instead.</summary>
        private const float ClickPixels = 6f;
        private const float PickRadius = 1.1f;

        private readonly List<int> selected = new List<int>();
        private MatchRunner runner;
        private Camera cam;
        private Vector2 anchor;
        private bool dragging;

        /// <summary>Set by whatever is using the left button for something else.</summary>
        public bool Blocked;

        public IReadOnlyList<int> Selected => selected;

        // Linear scan, but a selection is a handful of ids and this runs once per
        // rendered entity per frame.
        public bool Contains(int id) => selected.Contains(id);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Selection").AddComponent<Selection>();

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start() => runner = MatchRunner.Instance;

        private void Update()
        {
            if (runner == null) return;
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            Prune();

            if (Blocked)
            {
                dragging = false;
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                anchor = Input.mousePosition;
                dragging = true;
            }

            if (!dragging || !Input.GetMouseButtonUp(0)) return;
            dragging = false;

            Vector2 end = Input.mousePosition;
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) selected.Clear();

            if (Vector2.Distance(anchor, end) < ClickPixels) Pick(end);
            else Box(anchor, end);
        }

        /// <summary>Drops dead entities so an order never names a corpse.</summary>
        private void Prune()
        {
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                if (!runner.World.GetEntity(selected[i]).Alive) selected.RemoveAt(i);
            }
        }

        private void Pick(Vector2 screen)
        {
            Vector2 point = cam.ScreenToWorldPoint(screen);
            World world = runner.World;
            int best = -1;
            float bestDistance = PickRadius;

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != runner.LocalPeer) continue;

                float d = Vector2.Distance(point, runner.DrawPosition(i));
                if (d > bestDistance) continue;
                best = i;
                bestDistance = d;
            }

            if (best >= 0 && !selected.Contains(best)) selected.Add(best);
        }

        private void Box(Vector2 a, Vector2 b)
        {
            Vector2 min = cam.ScreenToWorldPoint(Vector2.Min(a, b));
            Vector2 max = cam.ScreenToWorldPoint(Vector2.Max(a, b));
            World world = runner.World;

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != runner.LocalPeer) continue;
                if (e.Kind != EntityKind.Unit && e.Kind != EntityKind.Worker) continue;

                Vector2 p = runner.DrawPosition(i);
                if (p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y) continue;
                if (!selected.Contains(i)) selected.Add(i);
            }
        }

        private void OnGUI()
        {
            if (!dragging) return;

            Vector2 a = anchor;
            Vector2 b = Input.mousePosition;
            // GUI space puts y at the top, screen space at the bottom.
            var rect = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));

            GUI.color = new Color(0.45f, 0.95f, 0.55f, 0.12f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.95f, 0.55f, 0.85f);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
