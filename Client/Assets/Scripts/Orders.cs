using System.Collections.Generic;
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
    ///   arrive key         names 차원 유랑종's 도착 지점; sends nothing by itself
    ///   more key           turns the production or research list to its next page; sends nothing
    ///   research cell      Research to the one representative 기술 건물, row in Arg
    ///   ability cell       Cast, row in Arg, to every selected body that holds the row;
    ///                      a point-aimed row waits for the next ground click
    /// </summary>
    public sealed class Orders : MonoBehaviour
    {
        public static Orders Instance { get; private set; }

        private const float PickRadius = 1.1f;

        /// <summary>Command waiting for a left click to name its target, or None.</summary>
        public CommandType Pending { get; private set; }

        /// <summary>
        /// The row of the faction's ability table an armed Cast will send, read
        /// only while <see cref="Pending"/> is Cast. The Arg of the command, and
        /// the number the card cell carries in Slot.
        /// </summary>
        public int CastIndex { get; private set; }

        /// <summary>Which building an armed Build will place. None when nothing is armed.</summary>
        public Role Placing { get; private set; }
        public int PlacingSlot { get; private set; }

        /// <summary>True while the worker's build submenu is showing instead of its card.</summary>
        public bool BuildMenuOpen { get; private set; }
        public bool GrowMenuOpen { get; private set; }
        private World menuWorld;

        /// <summary>
        /// True while the 도착 지점 cell is armed and the next left click on the map
        /// names a point instead of firing a command. Separate from
        /// <see cref="Pending"/> because nothing is pending: the click puts a point
        /// in this component and no command on the wire.
        /// </summary>
        public bool Aiming { get; private set; }

        /// <summary>
        /// The 도착 지점 the player has named, in simulation coordinates. Read only
        /// when <see cref="HasArrivalPoint"/>, and carried on the Target of every
        /// Produce this client sends from here on.
        ///
        /// One per client rather than one per 통로. The simulation keeps its copy on
        /// the building that accepted the Produce and overwrites it with each new
        /// one, which is exactly what one point sent with every Produce comes to;
        /// a second point kept per building here would be a second answer to a
        /// question the simulation already answers.
        /// </summary>
        public FixVec2 ArrivalPoint { get; private set; }

        /// <summary>
        /// Whether a point has been named at all. Explicit rather than testing
        /// ArrivalPoint against zero, for the reason Entity.HasArrivalPoint is
        /// explicit: (0,0) is a point on the map like any other, and it is also
        /// exactly what an unaimed Produce has always carried.
        /// </summary>
        public bool HasArrivalPoint { get; private set; }

        /// <summary>
        /// Which page of the production or research list the building card shows. View state
        /// like <see cref="BuildMenuOpen"/>: nothing about it reaches the
        /// simulation, and it goes back to 0 whenever the selection changes, so a
        /// new building always opens on its first entries.
        /// </summary>
        private int Page { get; set; }

        /// <summary>The selection Page was last valid for, id by id. See SyncPage.</summary>
        private readonly List<int> pageSelection = new List<int>();

        private MatchRunner runner;
        private Selection selection;
        private Camera cam;

        /// <summary>
        /// The world the arrival point was named in. A point is about one match's
        /// map and one match's anchors, and this component outlives a match: without
        /// this, a restart would open with the previous game's point already aimed
        /// and riding the first Produce of the new one.
        /// </summary>
        private World aimedWorld;

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
        public CardKind Kind() => BuildMenuOpen ? CardKind.BuildMenu : GrowMenuOpen ? CardKind.GrowMenu : SelectionKind();

        /// <summary>
        /// The card the keys fire and the HUD draws, built from one place so the
        /// two cannot be handed different pages.
        /// </summary>
        public CardSlot[] Card()
        {
            // The HUD can draw a frame before Start has found the runner.
            if (runner == null || selection == null)
            {
                return CommandCard.Of(CardKind.None, null, 0, -1, Role.None, 0, 0);
            }
            SyncPage();
            return CommandCard.Of(Kind(), runner.World, runner.LocalPeer, LeadId(), LeadRole(), LeadSlot(), Page);
        }

        /// <summary>The entity the card speaks for, -1 when nothing qualifies.</summary>
        public int LeadId()
        {
            if (runner == null || selection == null) return -1;
            return CommandCard.Representative(runner.World, selection.Selected, runner.LocalPeer);
        }

        /// <summary>
        /// The role of the entity the card speaks for, Role.None when nothing
        /// qualifies. Only the building card reads it (Tech lists research and has
        /// its own title); the entity is the same one Kind() ranks, so title and
        /// cells are about one building.
        /// </summary>
        public Role LeadRole()
        {
            if (runner == null || selection == null) return Role.None;
            int id = CommandCard.Representative(runner.World, selection.Selected, runner.LocalPeer);
            return id < 0 ? Role.None : runner.World.GetEntity(id).Role;
        }

        /// <summary>
        /// The roster entry of the same entity, 0 when nothing qualifies. The
        /// building card lists what this one building entry makes or researches,
        /// so a 석조 병영 and a 마법 탑 show different cards.
        /// </summary>
        public int LeadSlot()
        {
            if (runner == null || selection == null) return 0;
            int id = CommandCard.Representative(runner.World, selection.Selected, runner.LocalPeer);
            return id < 0 ? 0 : runner.World.GetEntity(id).Slot;
        }

        /// <summary>
        /// Drops Page to 0 when the selection is not the one it was turned on. Run
        /// by whoever reads the card first each frame (Update or the HUD), so a
        /// frame never draws a new building at the old one's page.
        ///
        /// Compared id by id against a copy, in order, not by representative alone:
        /// "the selection changed" means the player picked something else, and a
        /// representative alone would miss swapping one building for another of the
        /// same kind. Exact rather than hashed, because a collision would keep a
        /// stale page.
        /// </summary>
        private void SyncPage()
        {
            if (!ReferenceEquals(menuWorld, runner.World))
            {
                menuWorld = runner.World;
                pageSelection.Clear();
                Page = 0;
                Cancel();
            }
            IReadOnlyList<int> now = selection.Selected;
            bool same = now.Count == pageSelection.Count;
            for (int i = 0; same && i < now.Count; i++) same = now[i] == pageSelection[i];
            if (same) return;

            Cancel();
            pageSelection.Clear();
            for (int i = 0; i < now.Count; i++) pageSelection.Add(now[i]);
            Page = 0;
        }

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

            // A new match is a new map, so whatever was aimed at the last one is
            // dropped before anything can send it.
            if (HasArrivalPoint && !ReferenceEquals(runner.World, aimedWorld)) ClearAim();

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
            if (GrowMenuOpen && CommandCard.GrowthEntries(runner.World.FactionOf(runner.LocalPeer), LeadRole(), LeadSlot()).Count == 0)
                GrowMenuOpen = false;

            // The armed aim belongs to the 통로's card for the same reason, and it
            // is the 통로 dying rather than the selection changing that matters
            // most: an aim left armed over a building that is gone is a click that
            // names a point nothing will ever read.
            if (Aiming && !Aims()) Aiming = false;

            // An armed Cast belongs to the body that holds the row, for the same
            // reason: with the selection gone to something that cannot cast it, the
            // next click would be an order no card is offering any more.
            if (Pending == CommandType.Cast && !CastArmable(CastIndex)) Pending = CommandType.None;

            CardSlot[] card = Card();
            for (int i = 0; i < CommandCard.Cells; i++)
            {
                if (Input.GetKeyDown(CommandCard.Keys[i])) Run(card[i]);
            }

            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();

            // The left button belongs to an armed command while it is armed, so the
            // click that completes it does not also redo the selection underneath.
            // An armed aim owns it for the same reason.
            selection.Blocked = Pending != CommandType.None || Aiming;

            if ((Pending != CommandType.None || Aiming) && Input.GetMouseButtonDown(0))
            {
                if (Hud.OverUi(Input.mousePosition)) return;

                // A click that misses the field (the sky) names no point. Every other
                // order falls back to the camera's frame, which is harmless for a walk
                // and wrong for a Cast: the player would fire at a spot they never
                // pointed at. The cast stays armed for the next click.
                if (Pending == CommandType.Cast && !Field.Pointer(cam, Input.mousePosition, out _)) return;

                if (Aiming) Aim(MouseWorld());
                else Fire(MouseWorld());
                return;
            }

            if (!Input.GetMouseButtonDown(1) || Hud.OverUi(Input.mousePosition)) return;
            if (Pending != CommandType.None || BuildMenuOpen || GrowMenuOpen || Aiming) Cancel();
            else RightClick(MouseWorld());
        }

        /// <summary>Back to no armed command and no submenu. Esc and right click both mean this.</summary>
        private void Cancel()
        {
            Pending = CommandType.None;
            Placing = Role.None;
            PlacingSlot = 0;
            BuildMenuOpen = false;
            GrowMenuOpen = false;
            // The arm, not the point. Esc out of an aim and the point named before
            // it still stands, the way Esc out of a Move leaves the last one walked.
            Aiming = false;
        }

        /// <summary>
        /// Forgets the point itself. Only a new match calls this: a named point is
        /// as durable as a rally point, and the simulation has no way to unset one
        /// either (Sim/World.cs never clears HasRallyPoint).
        ///
        /// The player unaims by aiming somewhere no anchor covers, which is the
        /// simulation's own rule rather than a second one invented here —
        /// AimArrival clears HasArrivalPoint on a point it refuses, so a click on
        /// open ground puts production back at the 통로.
        /// </summary>
        private void ClearAim()
        {
            HasArrivalPoint = false;
            ArrivalPoint = FixVec2.Zero;
            aimedWorld = null;
        }

        /// <summary>
        /// Names 도착 지점. Nothing goes on the wire here — the point waits for the
        /// next Produce, whose Target is where the simulation reads it (Sim/World.cs
        /// Apply, Sim/Driftworlds.cs AimArrival).
        ///
        /// Taken wherever the player clicked, whatever the cursor's tint said. The
        /// tint is a guess made a tick early and the simulation is what decides,
        /// which is the rule the build ghost already plays by. Refusing the click
        /// here would also take the player's only way back to unaimed production.
        ///
        /// No sound: this press is answered by the cell releasing and the mark
        /// appearing on the map, the way the mixer button's is answered by the
        /// panel. Sound.Command means a command went out, and none did.
        /// </summary>
        private void Aim(Vector2 point)
        {
            Aiming = false;
            ArrivalPoint = MatchRunner.ToSim(point);
            HasArrivalPoint = true;
            aimedWorld = runner.World;
        }

        /// <summary>
        /// Runs a card cell. Anything that still needs a point or a victim is armed
        /// and waits for the click; everything else goes out now.
        /// </summary>
        public void Run(CardSlot slot)
        {
            if (runner == null || runner.Session.State != SessionState.Running) return;
            if (slot.Locked || slot.Maxed) return;
            if (slot.Type == CommandType.Grow)
            {
                Cancel();
                Page = 0;
                if (slot.Produce == Role.None)
                {
                    GrowMenuOpen = true;
                    return;
                }
                GrowthCost growth = FactionData.Growth(runner.World.FactionOf(runner.LocalPeer), slot.Produce, slot.Slot);
                foreach (int id in selection.Selected)
                {
                    Entity e = runner.World.GetEntity(id);
                    if (growth.Grown && e.Alive && e.Owner == runner.LocalPeer &&
                        e.Role == growth.FromRole && e.Slot == growth.FromSlot && !World.IsGrowing(e))
                        Issue(CommandType.Grow, id, FixVec2.Zero, Command.RosterArg(slot.Produce, slot.Slot));
                }
                return;
            }
            if (runner == null) return;
            if (runner.Session.State != SessionState.Running) return;

            if (slot.Aim)
            {
                // The card draws this cell dead for the five factions that never
                // arrive and for a 차원 유랑종 building that is not a 통로, and the
                // key has to mean what the button does or the dead cell is only
                // dead to the mouse. Same guard, same reason, as 징발's below.
                if (!Aims()) return;

                // A press while it is armed is the way back out, so the one cell
                // both arms and disarms and the player never has to find Esc.
                bool was = Aiming;
                Cancel();
                Aiming = !was;
                return;
            }

            if (slot.More)
            {
                // Wraps, so the last page's More is the way back to the first and
                // the player never has to find another key for it. Nothing is
                // sent: the page is this component's own.
                Page = ProductionMenu.NextPage(Page,
                    CommandCard.PageCount(Kind(), runner.World.FactionOf(runner.LocalPeer), LeadRole(), LeadSlot()));
                return;
            }

            if (slot.Type == CommandType.Cast)
            {
                Cast(slot);
                return;
            }

            if (slot.Type == CommandType.Research)
            {
                Research(slot);
                return;
            }

            if (slot.Type == CommandType.None) return;

            // The card draws this cell dead for the five factions that cannot
            // capture, and the key has to mean the same thing the button does or
            // the dead cell is only dead to the mouse.
            if (slot.Type == CommandType.Capture &&
                !CaptureOrder.Available(runner.World.FactionOf(runner.LocalPeer)))
            {
                return;
            }

            if (slot.Type == CommandType.Build)
            {
                // The cell that names no building is the one that opens the menu.
                if (slot.Produce == Role.None)
                {
                    Cancel();
                    Page = 0;
                    BuildMenuOpen = true;
                    return;
                }
                BuildMenuOpen = false;
                Placing = slot.Produce;
                PlacingSlot = slot.Slot;
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

            // The point rides Produce's Target, and it is zero until the player
            // names one — which is what every client and every recorded log has
            // always sent, and what every other faction sends forever. Aiming is
            // an extra the player may take, never a step between the button and
            // the unit: this cell produces on the first press whether or not
            // anything has been aimed.
            FixVec2 target = slot.Type == CommandType.Produce && HasArrivalPoint
                ? ArrivalPoint
                : FixVec2.Zero;
            ToSelection(slot.Type, target, arg);
        }

        /// <summary>
        /// Research, to exactly one building: the representative, the same body
        /// the card was drawn for. Not to the selection the way Cancel goes. Naming
        /// every selected 기술 건물 would start the row on the first and have the
        /// simulation refuse it on the rest (a peer buys one level of a row at a
        /// time), which is a command on the wire for nothing. Arg is the row of the
        /// upgrade table, which is what the card cell carries in Slot.
        ///
        /// A maxed cell is refused here for the reason a dead 징발 cell is: the
        /// card draws it on a Void face, and the key has to mean what the button
        /// does. Tier and resources are not asked. The simulation is what decides
        /// those, which is the rule the build menu follows.
        /// </summary>
        private void Research(CardSlot slot)
        {
            if (slot.Maxed) return;
            int id = CommandCard.Representative(runner.World, selection.Selected, runner.LocalPeer);
            if (id < 0 || runner.World.GetEntity(id).Role != Role.Tech) return;
            Issue(CommandType.Research, id, FixVec2.Zero, slot.Slot);
        }

        /// <summary>
        /// An ability cell. A row aimed at nothing goes out now; one aimed at a
        /// point is armed (Pending) and the next ground click sends it.
        ///
        /// A locked cell is refused here for the reason a maxed research row is:
        /// the card draws it on a Void face, and the key has to mean what the
        /// button does. A cell cooling down is not. The cooldown on the card is
        /// the representative's, the rest of the selection may be ready, and the
        /// simulation refuses whatever is not.
        /// </summary>
        private void Cast(CardSlot slot)
        {
            if (slot.Locked || !CastArmable(slot.Slot)) return;

            AbilityRow row = FactionData.Ability(runner.World.FactionOf(runner.LocalPeer), slot.Slot);
            Cancel();
            if (!AbilityMenu.NeedsPoint(row))
            {
                CastEach(slot.Slot, row, FixVec2.Zero);
                return;
            }
            Pending = CommandType.Cast;
            CastIndex = slot.Slot;
        }

        /// <summary>
        /// Whether the representative holds this row of the local faction's table
        /// and it is an active at all, which is what a Cast cell needs before it
        /// may arm or fire. The representative, because it is the body the card
        /// was drawn for; the rest of the selection is sorted out by CastEach.
        /// </summary>
        private bool CastArmable(int index)
        {
            if (runner == null || selection == null) return false;
            int id = LeadId();
            if (id < 0) return false;
            AbilityRow row = FactionData.Ability(runner.World.FactionOf(runner.LocalPeer), index);
            return FactionData.IsCast(row.Kind) && AbilityMenu.Holds(runner.World.GetEntity(id), row);
        }

        /// <summary>
        /// The row, named by every selected body of ours that
        /// <see cref="AbilityMenu.Receives"/>, one Cast each. Not to the
        /// representative alone the way Research goes: every body has a cooldown
        /// and a position of its own, and sending only the one that leads would
        /// leave the others idle. Two kinds are the exception, and the rule for
        /// both is in AbilityMenu.Receives: 굳기 goes to the representative alone,
        /// and 전개 to the bodies in the representative's state. The simulation
        /// refuses the casters that cannot (cooldown, range, cost), each on its
        /// own, and a refused Cast changes nothing.
        /// </summary>
        private void CastEach(int index, AbilityRow row, FixVec2 target)
        {
            World world = runner.World;
            int lead = LeadId();
            if (lead < 0) return;
            Entity representative = world.GetEntity(lead);
            for (int i = 0; i < selection.Selected.Count; i++)
            {
                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                if (e.Owner != runner.LocalPeer || !AbilityMenu.Receives(representative, e, row)) continue;
                Issue(CommandType.Cast, id, target, index);
            }
        }

        /// <summary>
        /// Whether the current selection is something that keeps an arrival point,
        /// which is what makes the Arrive cell live. The representative is the same
        /// body the card is drawn for (Hud.Card), so the key and the button ask
        /// about the same building.
        /// </summary>
        public bool Aims()
        {
            if (runner == null || selection == null) return false;
            World world = runner.World;
            return ArrivalOrder.Aims(world,
                runner.LocalPeer,
                CommandCard.Representative(world, selection.Selected, runner.LocalPeer));
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
                int slot = PlacingSlot;
                Placing = Role.None;
                PlacingSlot = 0;
                // Build names a peer, a building, and a cell, not an entity, so it
                // goes out once however many workers are selected. One per worker
                // would spend the first one's cost and have the simulation refuse
                // the rest. Sent whatever the ghost's tint says: the tint is a
                // guess made a tick late, and the simulation is what decides.
                Issue(CommandType.Build, -1, MatchRunner.ToSim(point), Command.RosterArg(role, slot));
                return;
            }

            if (type == CommandType.Cast)
            {
                // Taken wherever the player clicked, whatever the ring said: the
                // ring is the reach the simulation tests the point against, and it
                // is the simulation that decides.
                if (!CastArmable(CastIndex)) return;
                CastEach(CastIndex, FactionData.Ability(runner.World.FactionOf(runner.LocalPeer), CastIndex),
                    MatchRunner.ToSim(point));
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

        /// <summary>Where the pointer is on the map, or the frame's centre when it points at the sky. The
        /// ground is hills now, so this is a ray against the terrain and not a plane at a depth.</summary>
        private Vector2 MouseWorld() =>
            Field.Pointer(cam, Input.mousePosition, out Vector2 under) ? under : CameraRig.Instance.Frame;
    }
}
