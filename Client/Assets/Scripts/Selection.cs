using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// What the local player has selected. Purely local: selection is never sent
    /// to the peer, so two players can hold different selections without the
    /// simulations differing by a bit. Nothing here writes to the world.
    ///
    ///   drag            box select units and workers, fighters winning ties
    ///   click           take one entity, buildings included
    ///   double click    every entity of that type on screen
    ///   shift           add, or remove one already selected
    ///   ctrl 1-9        assign a control group
    ///   1-9             recall it; twice in a row centres the camera on it
    ///   backspace       camera onto your base
    /// </summary>
    public sealed class Selection : MonoBehaviour
    {
        public static Selection Instance { get; private set; }

        /// <summary>Screen distance below which a drag counts as a click instead.</summary>
        private const float ClickPixels = 6f;
        private const float PickRadius = 1.1f;

        /// <summary>Two clicks inside this are one double click, and so are two group recalls.</summary>
        private const float DoubleTapSeconds = 0.35f;

        private const int Groups = 9;

        private readonly List<int> selected = new List<int>();

        /// <summary>Scratch for one box, so deciding what the box caught allocates nothing.</summary>
        private readonly List<int> boxed = new List<int>();

        // Control groups hold ids, not entities, and dead ids are dropped on
        // recall. Ids are never reused by the simulation, so a stale id can only
        // ever name the corpse it originally named.
        private readonly List<int>[] groups = new List<int>[Groups];

        private MatchRunner runner;

        /// <summary>The world these ids belong to. A new one makes every one of them a lie.</summary>
        private World held;

        private Camera cam;
        private Vector2 anchor;
        private bool dragging;

        private float lastClickTime = -10f;
        private Vector2 lastClickPoint;
        private int lastGroup = -1;
        private float lastGroupTime = -10f;
        private int idleCursor;

        /// <summary>Set by whatever is using the left button for something else.</summary>
        public bool Blocked;

        public IReadOnlyList<int> Selected => selected;

        // Linear scan, but a selection is a handful of ids and this runs once per
        // rendered entity per frame.
        public bool Contains(int id) => selected.Contains(id);

        /// <summary>Drops everything else. What clicking one entry of the HUD list means.</summary>
        public void SelectOnly(int id)
        {
            selected.Clear();
            selected.Add(id);
        }

        /// <summary>Narrows the selection to one role. What ctrl-clicking an entry means.</summary>
        public void KeepRole(Role role)
        {
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                if (runner.World.GetEntity(selected[i]).Role != role) selected.RemoveAt(i);
            }
        }

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

            // Ids outlive nothing: a restart renumbers from zero, so a selection or
            // a control group held over from the last match would name an entity
            // that does not exist, and recalling it would read off the end.
            if (!ReferenceEquals(runner.World, held))
            {
                held = runner.World;
                selected.Clear();
                for (int g = 0; g < Groups; g++) groups[g] = null;
                idleCursor = 0;
            }

            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            Prune();
            // Group keys work while the left button belongs to something else: an
            // armed command is aimed at whatever the groups just recalled.
            GroupKeys();
            if (Input.GetKeyDown(KeyCode.Backspace)) JumpToBase();

            if (Blocked)
            {
                dragging = false;
                return;
            }

            if (Input.GetMouseButtonDown(0) && !Hud.OverUi(Input.mousePosition))
            {
                anchor = Input.mousePosition;
                dragging = true;
            }

            if (!dragging || !Input.GetMouseButtonUp(0)) return;
            dragging = false;

            Vector2 end = Input.mousePosition;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Vector2.Distance(anchor, end) >= ClickPixels)
            {
                if (!shift) selected.Clear();
                Box(anchor, end);
                return;
            }
            Click(end, shift);
        }

        /// <summary>Drops dead entities so an order never names a corpse.</summary>
        private void Prune()
        {
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                if (!runner.World.GetEntity(selected[i]).Alive) selected.RemoveAt(i);
            }
        }

        private void Click(Vector2 screen, bool shift)
        {
            bool doubleClick = Time.unscaledTime - lastClickTime < DoubleTapSeconds &&
                               Vector2.Distance(screen, lastClickPoint) < ClickPixels;
            lastClickTime = Time.unscaledTime;
            lastClickPoint = screen;

            int hit = runner.EntityAt(cam.ScreenToWorldPoint(screen), PickRadius, mineOnly: true);
            if (hit < 0)
            {
                if (!shift) selected.Clear();
                return;
            }

            if (doubleClick)
            {
                SelectVisibleRole(runner.World.GetEntity(hit).Role, shift);
                return;
            }

            if (!shift) { SelectOnly(hit); return; }
            // Shift both adds and removes: clicking a selected unit again is how a
            // player takes one body back out of a group they just boxed.
            if (!selected.Remove(hit)) selected.Add(hit);
        }

        private void Box(Vector2 a, Vector2 b)
        {
            Vector2 min = cam.ScreenToWorldPoint(Vector2.Min(a, b));
            Vector2 max = cam.ScreenToWorldPoint(Vector2.Max(a, b));
            World world = runner.World;

            boxed.Clear();
            bool fighters = false;

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != runner.LocalPeer) continue;
                if (e.Kind != EntityKind.Unit && e.Kind != EntityKind.Worker) continue;

                Vector2 p = runner.DrawPosition(i);
                if (p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y) continue;

                boxed.Add(i);
                fighters |= e.Kind == EntityKind.Unit;
            }

            // A box over the mining line and the army takes the army and leaves the
            // miners digging. Dragging workers into an attack is the constant
            // annoyance of the genre, and it is why every RTS drops them when a
            // fighter is in the box too. A box of nothing but workers still takes them.
            for (int i = 0; i < boxed.Count; i++)
            {
                int id = boxed[i];
                if (fighters && world.GetEntity(id).Kind == EntityKind.Worker) continue;
                if (!selected.Contains(id)) selected.Add(id);
            }
        }

        /// <summary>Every one of that role the player can currently see. What a double click means.</summary>
        private void SelectVisibleRole(Role role, bool shift)
        {
            if (!shift) selected.Clear();
            World world = runner.World;

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != runner.LocalPeer || e.Role != role) continue;
                if (e.Kind != EntityKind.Unit && e.Kind != EntityKind.Worker) continue;

                Vector3 v = cam.WorldToViewportPoint(runner.DrawPosition(i));
                if (v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                if (!selected.Contains(i)) selected.Add(i);
            }
        }

        private void GroupKeys()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            for (int g = 0; g < Groups; g++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + g))) continue;

                if (ctrl)
                {
                    groups[g] = new List<int>(selected);
                    return;
                }
                Recall(g);
                return;
            }
        }

        private void Recall(int g)
        {
            List<int> group = groups[g];
            if (group == null || group.Count == 0) return;

            selected.Clear();
            for (int i = 0; i < group.Count; i++)
            {
                if (runner.World.GetEntity(group[i]).Alive) selected.Add(group[i]);
            }

            bool again = lastGroup == g && Time.unscaledTime - lastGroupTime < DoubleTapSeconds;
            lastGroup = g;
            lastGroupTime = Time.unscaledTime;
            if (again) Center();
        }

        /// <summary>Camera onto the middle of what is selected. Drawn positions, so it lands where the eye is.</summary>
        private void Center()
        {
            if (selected.Count == 0 || CameraRig.Instance == null) return;

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < selected.Count; i++) sum += runner.DrawPosition(selected[i]);
            CameraRig.Instance.CenterOn(sum / selected.Count);
        }

        /// <summary>
        /// Camera onto your own base, without selecting it: the point is to look at
        /// home while a fight is happening elsewhere, not to lose the army that is
        /// in it. Backspace, which is what StarCraft II binds this to. The scenario
        /// gives each peer exactly one base, so the first match is the only match.
        /// </summary>
        private void JumpToBase()
        {
            if (CameraRig.Instance == null) return;
            World world = runner.World;

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != runner.LocalPeer || e.Role != Role.Base) continue;
                CameraRig.Instance.CenterOn(runner.DrawPosition(i));
                return;
            }
        }

        /// <summary>
        /// Idle is read out of simulation state, never marked on it: no gather node,
        /// nothing carried, and standing where it was last told to stand.
        /// </summary>
        private bool IsIdleWorker(Entity e) =>
            e.Alive && e.Kind == EntityKind.Worker && e.Owner == runner.LocalPeer &&
            e.GatherNodeId < 0 && e.CarryAmount == 0 && e.Target.Equals(e.Position);

        public int IdleWorkerCount()
        {
            if (runner == null) return 0;
            int n = 0;
            for (int i = 0; i < runner.World.EntityCount; i++)
            {
                if (IsIdleWorker(runner.World.GetEntity(i))) n++;
            }
            return n;
        }

        /// <summary>Selects the next idle worker and puts the camera on it. Cycles round.</summary>
        public void CycleIdleWorker()
        {
            if (runner == null) return;
            int count = runner.World.EntityCount;
            if (count == 0) return;

            for (int step = 1; step <= count; step++)
            {
                int id = (idleCursor + step) % count;
                if (!IsIdleWorker(runner.World.GetEntity(id))) continue;

                idleCursor = id;
                SelectOnly(id);
                if (CameraRig.Instance != null) CameraRig.Instance.CenterOn(runner.DrawPosition(id));
                return;
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
