using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Turns clicks and card presses into commands. Every path out of here is
    /// Issue, and through it Session.Issue, which queues the command for
    /// Tick + InputDelay on both peers; nothing here touches the world, so a
    /// dropped order is a missing command and never a simulation that disagrees
    /// with the peer's.
    ///
    ///   right click        move, gather a node, take a 꼬마돌, or attack an enemy
    ///   card key or button the command in that cell, see CommandCard
    ///   left click         completes an armed command, Esc cancels it
    ///   build key          opens the submenu; a second key picks the building
    /// </summary>
    public sealed class Orders : MonoBehaviour
    {
        public static Orders Instance { get; private set; }

        private const float PickRadius = 1.1f;

        /// <summary>Command waiting for a left click to name its target, or None.</summary>
        public CommandType Pending { get; private set; }

        /// <summary>Which building an armed Build will place. None when nothing is armed.</summary>
        public Role Placing { get; private set; }

        /// <summary>True while the worker's build submenu is showing instead of its card.</summary>
        public bool BuildMenuOpen { get; private set; }

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
        public CardKind Kind() => BuildMenuOpen ? CardKind.BuildMenu : SelectionKind();

        private CardKind SelectionKind()
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
                Cancel();
                selection.Blocked = false;
                return;
            }

            // The submenu belongs to the worker card, so it closes with it: losing
            // the selection mid-menu must not leave the player looking at buildings
            // no selected worker could place.
            if (BuildMenuOpen && SelectionKind() != CardKind.Worker) BuildMenuOpen = false;

            CardSlot[] card = CommandCard.Of(Kind(), runner.World.FactionOf(runner.LocalPeer));
            for (int i = 0; i < CommandCard.Cells; i++)
            {
                if (Input.GetKeyDown(CommandCard.Keys[i])) Run(card[i]);
            }

            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();

            // The left button belongs to an armed command while it is armed, so the
            // click that completes it does not also redo the selection underneath.
            selection.Blocked = Pending != CommandType.None;

            if (Pending != CommandType.None && Input.GetMouseButtonDown(0))
            {
                if (!Hud.OverUi(Input.mousePosition)) Fire(MouseWorld());
                return;
            }

            if (!Input.GetMouseButtonDown(1) || Hud.OverUi(Input.mousePosition)) return;
            if (Pending != CommandType.None || BuildMenuOpen) Cancel();
            else RightClick(MouseWorld());
        }

        /// <summary>Back to no armed command and no submenu. Esc and right click both mean this.</summary>
        private void Cancel()
        {
            Pending = CommandType.None;
            Placing = Role.None;
            BuildMenuOpen = false;
        }

        /// <summary>
        /// Runs a card cell. Anything that still needs a point or a victim is armed
        /// and waits for the click; everything else goes out now.
        /// </summary>
        public void Run(CardSlot slot)
        {
            if (runner == null || slot.Type == CommandType.None) return;
            if (runner.Session.State != SessionState.Running) return;

            if (slot.Type == CommandType.Build)
            {
                // The cell that names no building is the one that opens the menu.
                if (slot.Produce == Role.None)
                {
                    CommandCard.BuildMenu(runner.World.FactionOf(runner.LocalPeer));
                    BuildMenuOpen = true;
                    return;
                }
                BuildMenuOpen = false;
                Placing = slot.Produce;
                Pending = CommandType.Build;
                return;
            }

            if (CommandCard.NeedsTarget(slot.Type))
            {
                Pending = slot.Type;
                return;
            }

            // Produce packs role and entry together the way Command.RosterArg
            // expects; every other command here (Stop, Hold, CancelProduction)
            // never set Slot, so the cast alone still means what it always did.
            int arg = slot.Type == CommandType.Produce
                ? Command.RosterArg(slot.Produce, slot.Slot)
                : (int)slot.Produce;
            ToSelection(slot.Type, FixVec2.Zero, arg);
        }

        private void Fire(Vector2 point)
        {
            CommandType type = Pending;
            Pending = CommandType.None;
            // Blocked is deliberately left set until the next Update. Script order
            // between this component and Selection is undefined, and clearing it
            // here lets the very click that fired the order also start a selection.

            if (type == CommandType.Build)
            {
                Role role = Placing;
                Placing = Role.None;
                // Build names a peer, a building, and a cell, not an entity, so it
                // goes out once however many workers are selected. One per worker
                // would spend the first one's cost and have the simulation refuse
                // the rest. Sent whatever the ghost's tint says: the tint is a
                // guess made a tick late, and the simulation is what decides.
                Issue(CommandType.Build, -1, MatchRunner.ToSim(point), (int)role);
                return;
            }

            if (type == CommandType.Capture)
            {
                int rock = runner.EntityAt(point, PickRadius, mineOnly: false);
                if (rock < 0) return;
                Capture(rock);
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
        /// 징발, to exactly one body. Not to the selection the way Move and Gather
        /// go, because a 꼬마돌 takes one worker: the simulation hands the rock to
        /// whoever finishes first and drops everyone else's clock on the next
        /// tick, so five workers sent at one rock is four workers standing still
        /// for sixty ticks and then standing still for good.
        ///
        /// The nearest one, measured off drawn positions. The measurement is local
        /// and the command that leaves here names an entity id, so both peers run
        /// the same order however each of them drew the frame.
        /// </summary>
        private void Capture(int rockId)
        {
            int worker = NearestCapturer(rockId);
            if (worker < 0) return;
            Issue(CommandType.Capture, worker, FixVec2.Zero, rockId);
        }

        /// <summary>
        /// The selected body this capture would go to, or -1 when the selection
        /// holds none that could take it. CaptureOrder is the test, so the client
        /// asks the same question here that the card asks when it draws the cell.
        /// </summary>
        private int NearestCapturer(int rockId)
        {
            World world = runner.World;
            Vector2 rock = runner.DrawPosition(rockId);
            int best = -1;
            float bestDistance = 0f;

            for (int i = 0; i < selection.Selected.Count; i++)
            {
                int id = selection.Selected[i];
                if (!CaptureOrder.Allows(world, runner.LocalPeer, id, rockId)) continue;

                float d = Vector2.Distance(rock, runner.DrawPosition(id));
                if (best >= 0 && d >= bestDistance) continue;
                best = id;
                bestDistance = d;
            }
            return best;
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

                Issue(type, id, target, arg);
            }
        }

        /// <summary>
        /// What a right click at a world point means: gather the node under it,
        /// attack the enemy under it, walk there, or rally there. Public so the
        /// minimap can hand its own world point in rather than deciding again;
        /// one copy of the rule is the only way the two cannot drift apart.
        /// </summary>
        public void RightClick(Vector2 point)
        {
            World world = runner.World;
            int hit = runner.EntityAt(point, PickRadius, mineOnly: false);
            Entity aimed = hit >= 0 ? world.GetEntity(hit) : default;
            bool node = hit >= 0 && aimed.Kind == EntityKind.ResourceNode;
            bool enemy = hit >= 0 && aimed.Owner >= 0 && aimed.Owner != runner.LocalPeer;

            // 징발 on the right button for the same reason Gather is: it is the
            // worker's other loop, the same shape of order, and a player who
            // learned to point at a node points at a rock the same way. One body
            // takes it and the rest of the selection walks there, which is what
            // pointing at something already means everywhere else on this button.
            //
            // Killing a 꼬마돌 stays on the Attack cell. A right click has to mean
            // one thing per target, and for the faction that can take this one it
            // means take it; every other faction gets the walk, which is what the
            // button did here yesterday.
            int capturing = hit >= 0 && world.IsNeutralRock(aimed) ? NearestCapturer(hit) : -1;

            FixVec2 target = hit >= 0 ? aimed.Position : MatchRunner.ToSim(point);

            for (int i = 0; i < selection.Selected.Count; i++)
            {
                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                if (!e.Alive || e.Owner != runner.LocalPeer) continue;

                if (node && e.Kind == EntityKind.Worker)
                {
                    Issue(CommandType.Gather, id, FixVec2.Zero, hit);
                }
                else if (id == capturing)
                {
                    Issue(CommandType.Capture, id, FixVec2.Zero, hit);
                }
                else if (enemy)
                {
                    // A building cannot chase, but a turret takes the order standing
                    // still, so it is sent one too and the simulation sorts it out.
                    Issue(CommandType.Attack, id, FixVec2.Zero, hit);
                }
                else if (e.Speed.Raw != 0)
                {
                    Issue(CommandType.Move, id, target);
                }
                else if (e.Kind == EntityKind.Building)
                {
                    // Right-clicking the ground with a building selected is what
                    // every RTS means by a rally point.
                    Issue(CommandType.SetRallyPoint, id, target);
                }
            }
        }

        /// <summary>
        /// Every command this file sends, and the click that says it went. One way
        /// out means one place the confirmation can live, and an order that is
        /// dropped here — a right click through the minimap after the session
        /// stopped is the only way in that Update does not already gate — makes no
        /// sound, which is the whole point of the sound.
        ///
        /// Sound.Command dedupes per frame, so an order given to forty selected
        /// units is forty of these and one click.
        /// </summary>
        private void Issue(CommandType type, int entity, FixVec2 target, int arg = 0)
        {
            if (runner.Session.State != SessionState.Running) return;
            runner.Session.Issue(type, entity, target, arg);
            Sound.Command();
        }

        private Vector2 MouseWorld() => cam.ScreenToWorldPoint(Input.mousePosition);
    }
}
