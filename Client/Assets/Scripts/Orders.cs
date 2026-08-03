using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Turns clicks and card presses into commands. Every path out of here is
    /// Session.Issue, which queues the command for Tick + InputDelay on both
    /// peers; nothing here touches the world, so a dropped order is a missing
    /// command and never a simulation that disagrees with the peer's.
    ///
    ///   right click        move, gather a node, or attack an enemy
    ///   card key or button the command in that cell, see CommandCard
    ///   left click         completes an armed command, Esc cancels it
    /// </summary>
    public sealed class Orders : MonoBehaviour
    {
        public static Orders Instance { get; private set; }

        private const float PickRadius = 1.1f;

        /// <summary>Command waiting for a left click to name its target, or None.</summary>
        public CommandType Pending { get; private set; }

        private MatchRunner runner;
        private Selection selection;
        private Camera cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Orders").AddComponent<Orders>();

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            runner = MatchRunner.Instance;
            selection = Selection.Instance;
        }

        /// <summary>The card the current selection shows. The HUD draws it, the keys fire it.</summary>
        public CardKind Kind()
        {
            if (runner == null || selection == null) return CardKind.None;
            int id = CommandCard.Representative(runner.World, selection.Selected, runner.LocalPeer);
            return id < 0 ? CardKind.None : CommandCard.KindOf(runner.World.GetEntity(id));
        }

        private void Update()
        {
            if (runner == null || selection == null) return;
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            if (runner.Session.State != SessionState.Running)
            {
                Pending = CommandType.None;
                selection.Blocked = false;
                return;
            }

            CardSlot[] card = CommandCard.Of(Kind());
            for (int i = 0; i < CommandCard.Cells; i++)
            {
                if (Input.GetKeyDown(CommandCard.Keys[i])) Run(card[i]);
            }

            if (Input.GetKeyDown(KeyCode.Escape)) Pending = CommandType.None;

            // The left button belongs to an armed command while it is armed, so the
            // click that completes it does not also redo the selection underneath.
            selection.Blocked = Pending != CommandType.None;

            if (Pending != CommandType.None && Input.GetMouseButtonDown(0))
            {
                if (!Hud.OverUi(Input.mousePosition)) Fire(MouseWorld());
                return;
            }

            if (!Input.GetMouseButtonDown(1) || Hud.OverUi(Input.mousePosition)) return;
            if (Pending != CommandType.None) Pending = CommandType.None;
            else RightClick(MouseWorld());
        }

        /// <summary>
        /// Runs a card cell. Anything that still needs a point or a victim is armed
        /// and waits for the click; everything else goes out now.
        /// </summary>
        public void Run(CardSlot slot)
        {
            if (runner == null || slot.Type == CommandType.None) return;
            if (runner.Session.State != SessionState.Running) return;

            if (CommandCard.NeedsTarget(slot.Type))
            {
                Pending = slot.Type;
                return;
            }
            ToSelection(slot.Type, FixVec2.Zero, (int)slot.Produce);
        }

        private void Fire(Vector2 point)
        {
            CommandType type = Pending;
            Pending = CommandType.None;
            selection.Blocked = false;

            if (type == CommandType.Build)
            {
                // Build names a peer and a cell, not an entity, so it goes out once
                // however many workers are selected. One per worker would spend the
                // first one's cost and have the simulation refuse the rest.
                runner.Session.Issue(CommandType.Build, -1, MatchRunner.ToSim(point));
                return;
            }

            if (type == CommandType.Attack)
            {
                int hit = runner.EntityAt(point, PickRadius, mineOnly: false);
                // The simulation refuses an Attack that names nothing hostile, so a
                // click on empty ground drops the order rather than inventing a
                // substitute target each peer would pick for itself.
                if (hit < 0 || runner.World.GetEntity(hit).Owner == runner.LocalPeer) return;
                ToSelection(CommandType.Attack, FixVec2.Zero, hit);
                return;
            }

            ToSelection(type, MatchRunner.ToSim(point), 0);
        }

        /// <summary>
        /// One command per selected entity. The simulation is what decides whether
        /// each one is legal; the view filters only what it would be silly to send,
        /// so the rules live in exactly one place and cannot drift between peers.
        /// </summary>
        private void ToSelection(CommandType type, FixVec2 target, int arg)
        {
            World world = runner.World;
            for (int i = 0; i < selection.Selected.Count; i++)
            {
                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                if (!e.Alive || e.Owner != runner.LocalPeer) continue;
                if (type == CommandType.Move && e.Speed.Raw == 0) continue;

                runner.Session.Issue(type, id, target, arg);
            }
        }

        private void RightClick(Vector2 point)
        {
            World world = runner.World;
            int hit = runner.EntityAt(point, PickRadius, mineOnly: false);
            Entity aimed = hit >= 0 ? world.GetEntity(hit) : default;
            bool node = hit >= 0 && aimed.Kind == EntityKind.ResourceNode;
            bool enemy = hit >= 0 && aimed.Owner >= 0 && aimed.Owner != runner.LocalPeer;

            FixVec2 target = hit >= 0 ? aimed.Position : MatchRunner.ToSim(point);

            for (int i = 0; i < selection.Selected.Count; i++)
            {
                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                if (!e.Alive || e.Owner != runner.LocalPeer) continue;

                if (node && e.Kind == EntityKind.Worker)
                {
                    runner.Session.Issue(CommandType.Gather, id, FixVec2.Zero, hit);
                }
                else if (enemy)
                {
                    // A building cannot chase, but a turret takes the order standing
                    // still, so it is sent one too and the simulation sorts it out.
                    runner.Session.Issue(CommandType.Attack, id, FixVec2.Zero, hit);
                }
                else if (e.Speed.Raw != 0)
                {
                    runner.Session.Issue(CommandType.Move, id, target);
                }
                else if (e.Kind == EntityKind.Building)
                {
                    // Right-clicking the ground with a building selected is what
                    // every RTS means by a rally point.
                    runner.Session.Issue(CommandType.SetRallyPoint, id, target);
                }
            }
        }

        private Vector2 MouseWorld() => cam.ScreenToWorldPoint(Input.mousePosition);
    }
}
