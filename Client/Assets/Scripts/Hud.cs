using System;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The client's three screens: the start menu, the match HUD, and the result.
    /// A phase and a switch, because there are three of them and there is no fourth
    /// waiting; a scene manager here would be one screen per scene asset.
    ///
    /// Match HUD: a bar across the top and a panel across the bottom.
    ///
    /// Every colour and every measurement comes from UiStyle, and every widget is
    /// one of its components. This file decides where things go and what they say;
    /// it decides nothing about how they look. See docs/UI-STYLE.md.
    ///
    /// IMGUI on purpose, and kept that way at roadmap 3-1. UI Toolkit needs a
    /// PanelSettings and a theme stylesheet, which are serialised assets, and the
    /// client boots from an empty scene through RuntimeInitializeOnLoadMethod so a
    /// gameplay change never has to touch one. IMGUI needs no asset at all, so the
    /// whole HUD stays a diff in two files.
    ///
    /// ponytail: IMGUI relayouts and allocates strings every frame. It is a few
    /// dozen widgets, so it does not show; move to UI Toolkit the day the HUD
    /// appears in a profile, and accept the asset that comes with it.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        /// <summary>The address the player last joined. Typing an IP twice is not a game.</summary>
        private const string AddressKey = "wordcraft.address";

        /// <summary>Wide enough for five digits and no wider, so it reads as a port.</summary>
        private const float PortWidth = UiStyle.S5 * 2f;

        /// <summary>The faction picker is two rows of three. Six names, one glance.</summary>
        private const int PickerCols = 3;

        private MatchRunner runner;
        private Selection selection;

        // Start screen form state. Local to the view; nothing here reaches the
        // simulation until StartMatch turns it into a MatchConfig.
        private string address = "";
        private string portText = MatchRunner.DefaultPort.ToString();
        private Faction faction = MatchConfig.DefaultFaction(0);
        private string formError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft HUD").AddComponent<Hud>();

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void Start()
        {
            runner = MatchRunner.Instance;
            selection = Selection.Instance;
            address = PlayerPrefs.GetString(AddressKey, "");
        }

        /// <summary>
        /// True when a screen-space point is over the HUD instead of the world.
        /// Both the picker and the order code ask before acting, so a click on a
        /// command button never also drags a selection box across the map.
        ///
        /// Off the match screen the whole window is UI: the menu sits over a live
        /// map, and a press on "play again" must not also box-select behind it.
        ///
        /// The minimap is inside the bottom panel, so it is already covered here:
        /// a click on it must not also drag a selection box across the world.
        /// </summary>
        public static bool OverUi(Vector2 screenPosition)
        {
            MatchRunner runner = MatchRunner.Instance;
            if (runner != null && runner.Phase != Phase.Match) return true;
            return screenPosition.y <= UiStyle.BottomPanel ||
                   screenPosition.y >= Screen.height - UiStyle.TopBar;
        }

        private void OnGUI()
        {
            if (runner == null) return;

            switch (runner.Phase)
            {
                case Phase.Start:
                    StartScreen();
                    break;
                case Phase.Result:
                    ResultScreen();
                    break;
                default:
                    TopBar(runner.World);
                    BottomPanel(runner.World);
                    break;
            }
        }

        // ---- start screen ----

        /// <summary>
        /// Host or join, who to play, and where. Drawn over the idle map, which is
        /// why it is opaque. Laid out top to bottom in one column: a menu read once
        /// is the one place in this client where reading order beats density.
        /// </summary>
        private void StartScreen()
        {
            Rect panel = UiStyle.Middle(UiStyle.MenuWidth, UiStyle.MenuHeight);
            UiStyle.PanelBox(panel, UiStyle.Panel);

            GUILayout.BeginArea(UiStyle.Inside(panel));
            GUILayout.Label("WORDCRAFT", UiStyle.DisplayStyle);
            GUILayout.Space(UiStyle.S4);

            GUILayout.Label("faction", UiStyle.MicroStyle);
            GUILayout.Space(UiStyle.S1);
            FactionPicker();
            GUILayout.Space(UiStyle.S4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("address", UiStyle.MicroStyle);
            GUILayout.Label("port", UiStyle.MicroStyle, GUILayout.Width(PortWidth));
            GUILayout.EndHorizontal();
            GUILayout.Space(UiStyle.S1);

            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address ?? "", 45, UiStyle.FieldStyle);
            GUILayout.Space(UiStyle.S2);
            portText = GUILayout.TextField(portText ?? "", 5, UiStyle.FieldStyle, GUILayout.Width(PortWidth));
            GUILayout.EndHorizontal();
            GUILayout.Space(UiStyle.S4);

            if (runner.Connecting) ConnectionState();
            else HostOrJoin();

            GUILayout.EndArea();
        }

        /// <summary>All six by name, because a faction the player cannot see is not a choice.</summary>
        private void FactionPicker()
        {
            string[] names = Enum.GetNames(typeof(Faction));
            for (int row = 0; row < names.Length; row += PickerCols)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < row + PickerCols && i < names.Length; i++)
                {
                    if (i > row) GUILayout.Space(UiStyle.S1);
                    if (UiStyle.Button(names[i], UiStyle.Control, (int)faction == i)) faction = (Faction)i;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(UiStyle.S1);
            }
        }

        private void HostOrJoin()
        {
            GUILayout.BeginHorizontal();
            bool host = UiStyle.Button("HOST", UiStyle.ControlTall);
            GUILayout.Space(UiStyle.S2);
            bool join = UiStyle.Button("JOIN", UiStyle.ControlTall);
            GUILayout.EndHorizontal();
            GUILayout.Space(UiStyle.S2);

            // No address, no port, no handshake: nothing here can be malformed, so
            // this one takes no error path and starts the match outright.
            bool solo = UiStyle.Button("SOLO", UiStyle.ControlTall);
            GUILayout.Space(UiStyle.S3);

            if (formError != null)
            {
                GUILayout.Label(formError, UiStyle.Toned(UiStyle.Danger));
                GUILayout.Space(UiStyle.S2);
            }

            GUILayout.Label("one machine hosts, the other joins its address. same port on both.",
                UiStyle.MicroStyle);
            GUILayout.Label("solo plays the simulation's own opponent, no network at all.",
                UiStyle.MicroStyle);

            // Acted on after the labels, so a press does not relayout the block it
            // was pressed in halfway through drawing it.
            if (host) Begin(null);
            else if (join) Begin(address);
            else if (solo)
            {
                formError = null;
                runner.StartSolo(faction);
            }
        }

        private void Begin(string remote)
        {
            if (!int.TryParse(portText, out int port))
            {
                formError = "port is not a number: " + portText;
                return;
            }

            formError = runner.StartMatch(remote, port, faction);
            if (formError != null || remote == null) return;

            PlayerPrefs.SetString(AddressKey, remote.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Where the connection has got to. The handshake produces exact rejection
        /// reasons, so the reason is shown as it was written rather than flattened
        /// into "could not connect".
        /// </summary>
        private void ConnectionState()
        {
            LockstepSession session = runner.Session;
            bool rejected = session.State == SessionState.Stopped;

            GUILayout.Label(ConnectionText(session),
                UiStyle.Toned(rejected ? UiStyle.Danger : UiStyle.Ink));
            GUILayout.Space(UiStyle.S3);

            if (UiStyle.Button(rejected ? "back" : "cancel", UiStyle.Control)) runner.ShowStart();
        }

        private string ConnectionText(LockstepSession session)
        {
            switch (session.State)
            {
                case SessionState.Running: return "connected";
                // Verbatim. The handshake already says exactly what disagreed and
                // what it expected, and "could not connect" throws all of it away.
                case SessionState.Stopped: return session.StopReason ?? "rejected, no reason recorded";
                default: return runner.PeerHeard ? "handshaking, " + runner.Link : runner.Link;
            }
        }

        // ---- result screen ----

        /// <summary>
        /// How it ended and how long it took. A halt shows the stop reason as the
        /// session wrote it, tick and both hashes included, because that string is
        /// what a player can copy into a bug report. The panel itself goes red on a
        /// halt: an outcome and a crash must not share a surface.
        /// </summary>
        private void ResultScreen()
        {
            Rect panel = UiStyle.Middle(UiStyle.ResultWidth, UiStyle.ResultHeight);
            UiStyle.PanelBox(panel, runner.Halted ? UiStyle.HaltPanel : UiStyle.Panel);

            GUILayout.BeginArea(UiStyle.Inside(panel));
            GUILayout.Label(runner.Outcome ?? "MATCH OVER", UiStyle.DisplayStyle);
            GUILayout.Space(UiStyle.S2);

            if (runner.Halted)
            {
                GUILayout.Label(runner.Session.StopReason ?? "no reason recorded",
                    UiStyle.Toned(UiStyle.Danger));
                if (!runner.Session.ReportComplete)
                {
                    GUILayout.Label("waiting for the peer state dump...", UiStyle.MicroStyle);
                }
                GUILayout.Space(UiStyle.S2);
            }

            GUILayout.Label(runner.EndTick + " ticks  ·  " + runner.EndSeconds.ToString("0.0") + " s",
                UiStyle.MicroStyle);
            GUILayout.Space(UiStyle.S4);

            if (UiStyle.Button("play again", UiStyle.ControlTall)) runner.ShowStart();
            GUILayout.EndArea();
        }

        // ---- match: top bar ----

        /// <summary>
        /// What the player has to know without asking, in the order they ask it.
        /// Mana and population are readouts because they are numbers read mid-fight;
        /// everything else up here is diagnostic and is drawn at the quietest weight
        /// the system has, so it never competes with them.
        ///
        /// Population turns red at the cap because a capped peer produces nothing and
        /// the simulation says so silently; the number going red is the only warning
        /// there is.
        /// </summary>
        private void TopBar(World world)
        {
            var bar = new Rect(0f, 0f, Screen.width, UiStyle.TopBar);
            UiStyle.BarBox(bar, edgeOnTop: false);

            int peer = runner.LocalPeer;
            int used = world.GetPopulation(peer);
            int cap = world.PopulationCap(peer);

            float x = UiStyle.S4;
            float y = UiStyle.S2;

            UiStyle.Readout(new Rect(x, y, UiStyle.ReadoutWidth, UiStyle.ReadoutHeight),
                "mana", world.GetResources(peer).ToString(), UiStyle.Ink);
            x += UiStyle.ReadoutWidth;

            UiStyle.Readout(new Rect(x, y, UiStyle.ReadoutWidth, UiStyle.ReadoutHeight),
                "pop", used + " / " + cap, used >= cap ? UiStyle.Danger : UiStyle.Ink);
            x += UiStyle.ReadoutWidth + UiStyle.S4;

            bool stopped = runner.Session.State == SessionState.Stopped;
            float right = Screen.width - UiStyle.IdleWidth - UiStyle.S4 - UiStyle.S4;
            float width = Mathf.Max(0f, right - x);

            UiStyle.Note(new Rect(x, y, width, UiStyle.Micro + UiStyle.S1),
                StateText(runner.Session), stopped ? UiStyle.Danger : UiStyle.InkMute);
            UiStyle.Note(new Rect(x, y + UiStyle.Micro + UiStyle.S1, width, UiStyle.Line),
                "peer " + peer + "  ·  tick " + world.Tick + "  ·  " + runner.Link, UiStyle.InkMute);

            if (selection == null) return;
            int idle = selection.IdleWorkerCount();
            var button = new Rect(Screen.width - UiStyle.IdleWidth - UiStyle.S4,
                (UiStyle.TopBar - UiStyle.Control) * 0.5f, UiStyle.IdleWidth, UiStyle.Control);
            if (UiStyle.Button(button, "idle worker  " + idle, armed: false, enabled: idle > 0))
            {
                selection.CycleIdleWorker();
            }
        }

        // ---- match: bottom panel ----

        /// <summary>
        /// Four columns, left to right in the order the hand reaches for them: the
        /// map, who is selected, the whole selection, the commands. The map and the
        /// card are flush against their corners because both are aimed at by muscle
        /// memory, and a margin around either is a margin the pointer can miss into.
        /// </summary>
        private void BottomPanel(World world)
        {
            var panel = new Rect(0f, Screen.height - UiStyle.BottomPanel, Screen.width, UiStyle.BottomPanel);
            UiStyle.BarBox(panel, edgeOnTop: true);

            int lead = selection == null
                ? -1
                : CommandCard.Representative(world, selection.Selected, runner.LocalPeer);

            Minimap.Draw(new Rect(0f, panel.y, UiStyle.MinimapSize, UiStyle.MinimapSize), runner);

            float top = panel.y + UiStyle.S2;
            float height = UiStyle.BottomPanel - UiStyle.S2 * 2f;

            float infoX = UiStyle.MinimapSize + UiStyle.S3;
            float cardX = Screen.width - UiStyle.CardWidth - UiStyle.S3;

            Info(new Rect(infoX, top, UiStyle.InfoWidth, height), world, lead);

            float listX = infoX + UiStyle.InfoWidth + UiStyle.S3;
            UiStyle.Fill(new Rect(listX - UiStyle.S2, top, UiStyle.Hairline, height), UiStyle.Edge);

            List(new Rect(listX, top, Mathf.Max(0f, cardX - listX - UiStyle.S3), height), world);
            Card(new Rect(cardX, top, UiStyle.CardWidth, height));
        }

        /// <summary>Who this is, how hurt, whose. One representative plus the count.</summary>
        private void Info(Rect area, World world, int lead)
        {
            float y = area.y;
            UiStyle.Header(new Rect(area.x, y, area.width, UiStyle.Micro + UiStyle.S1), "selected");
            y += UiStyle.Micro + UiStyle.S1;

            if (lead < 0)
            {
                UiStyle.Note(new Rect(area.x, y, area.width, UiStyle.Line), "nothing");
                UiStyle.Note(new Rect(area.x, y + UiStyle.Line + UiStyle.S2, area.width, UiStyle.Line),
                    "drag to select  ·  right click to order", UiStyle.InkMute);
                return;
            }

            Entity e = world.GetEntity(lead);
            GUI.Label(new Rect(area.x, y, area.width, UiStyle.Line + UiStyle.S1),
                FactionData.Name(world.FactionOf(e.Owner), e.Role), UiStyle.BigStyle);
            y += UiStyle.Line + UiStyle.S2;

            float ratio = e.MaxHp > 0 ? Mathf.Clamp01(e.Hp / (float)e.MaxHp) : 0f;
            UiStyle.Meter(new Rect(area.x, y, area.width, UiStyle.S1), ratio, UiStyle.Health(ratio));
            y += UiStyle.S1 + UiStyle.S1;

            UiStyle.Note(new Rect(area.x, y, area.width, UiStyle.Line),
                e.Hp + " / " + e.MaxHp + "  hp");
            y += UiStyle.Line;

            // The one place a per-player hue is allowed in the HUD: it names an
            // owner. A swatch plus the text, because colour alone is not a label.
            UiStyle.Fill(new Rect(area.x, y + UiStyle.S1, UiStyle.S2, UiStyle.S2), UiStyle.Owner(e.Owner));
            UiStyle.Label(new Rect(area.x + UiStyle.S3, y, area.width - UiStyle.S3, UiStyle.Line),
                "peer " + e.Owner + "  " + world.FactionOf(e.Owner), UiStyle.Owner(e.Owner));
            y += UiStyle.Line + UiStyle.S1;

            int count = selection.Selected.Count;
            if (count > 1)
            {
                UiStyle.Note(new Rect(area.x, y, area.width, UiStyle.Line), count + " selected");
                y += UiStyle.Line;
            }

            string state = StateOf(e);
            if (state != null)
            {
                UiStyle.Note(new Rect(area.x, y, area.width, UiStyle.Line), state);
                y += UiStyle.Line;
            }

            // The pending order is the only thing on this panel the player is in the
            // middle of, so it is the only thing wearing the accent.
            if (Orders.Instance != null && Orders.Instance.Pending != CommandType.None)
            {
                UiStyle.Label(new Rect(area.x, y, area.width, UiStyle.Line),
                    Orders.Instance.Pending + ": click a target, Esc cancels", UiStyle.Accent);
            }
        }

        private static string StateOf(Entity e)
        {
            if (e.Kind == EntityKind.Building)
            {
                return e.BuildTicksLeft > 0
                    ? "under construction, " + e.BuildTicksLeft + "t left"
                    : "queue " + e.QueueCount + "  ·  next in " + e.ProduceTicksLeft + "t";
            }
            return e.Kind == EntityKind.Worker ? "carrying " + e.CarryAmount : null;
        }

        /// <summary>
        /// The selection as a grid of chips. A click keeps that one entity, a
        /// ctrl-click keeps every entity of the same role already selected.
        /// </summary>
        private void List(Rect area, World world)
        {
            if (selection == null || area.width < UiStyle.ChipWidth) return;

            UiStyle.Header(new Rect(area.x, area.y, area.width, UiStyle.Micro + UiStyle.S1), "selection");
            var grid = new Rect(area.x, area.y + UiStyle.Micro + UiStyle.S1,
                area.width, area.height - UiStyle.Micro - UiStyle.S1);

            float step = UiStyle.ChipWidth + UiStyle.S1;
            float rowStep = UiStyle.ChipHeight + UiStyle.S1;
            int cols = Mathf.Max(1, (int)(grid.width / step));
            int rows = Mathf.Max(1, (int)(grid.height / rowStep));
            bool ctrl = Event.current.control || Event.current.command;

            for (int i = 0; i < selection.Selected.Count; i++)
            {
                if (i >= cols * rows)
                {
                    UiStyle.Note(new Rect(grid.x, grid.yMax - UiStyle.ChipHeight,
                            UiStyle.ChipWidth, UiStyle.ChipHeight),
                        "+" + (selection.Selected.Count - i) + " more");
                    return;
                }

                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                var cell = new Rect(grid.x + i % cols * step, grid.y + i / cols * rowStep,
                    UiStyle.ChipWidth, UiStyle.ChipHeight);

                float ratio = e.MaxHp > 0 ? Mathf.Clamp01(e.Hp / (float)e.MaxHp) : 0f;
                if (!UiStyle.Chip(cell, Short(world, e) + "  " + e.Hp, ratio)) continue;

                if (ctrl) selection.KeepRole(e.Role);
                else selection.SelectOnly(id);
                return; // the list just changed under the loop
            }
        }

        /// <summary>
        /// The 3x3 card. The key is drawn on its own cell because the cell is the
        /// thing worth learning; see CommandCard for the table behind it.
        /// </summary>
        private void Card(Rect area)
        {
            Orders orders = Orders.Instance;
            if (orders == null) return;

            CardKind kind = orders.Kind();
            CardSlot[] card = CommandCard.Of(kind, runner.World.FactionOf(runner.LocalPeer));

            // The card changes under the same rectangle, so it says what it is.
            // Without this a build submenu and a produce row look alike.
            UiStyle.Header(new Rect(area.x, area.y, area.width, UiStyle.Micro + UiStyle.S1),
                CommandCard.Title(kind));

            float top = area.y + UiStyle.Micro + UiStyle.S1;
            float step = UiStyle.CardCellSize + UiStyle.S1;

            for (int i = 0; i < CommandCard.Cells; i++)
            {
                var cell = new Rect(area.x + i % CommandCard.Cols * step,
                    top + i / CommandCard.Cols * step,
                    UiStyle.CardCellSize, UiStyle.CardCellSize);

                if (card[i].Type == CommandType.None)
                {
                    UiStyle.EmptyCell(cell);
                    continue;
                }

                if (UiStyle.CardCell(cell, CommandCard.Keys[i].ToString(), card[i].Label,
                        orders.Pending == card[i].Type))
                {
                    orders.Run(card[i]);
                }
            }
        }

        /// <summary>Roster name if the slot has one, else the role, which every slot has.</summary>
        private static string Short(World world, Entity e)
        {
            if (e.Owner < 0) return e.Kind.ToString();
            string name = FactionData.Name(world.FactionOf(e.Owner), e.Role);
            return name.Length > 0 ? name : e.Role.ToString();
        }

        private static string StateText(LockstepSession session)
        {
            switch (session.State)
            {
                case SessionState.Handshaking: return "handshaking";
                case SessionState.Running: return "running";
                default: return "STOPPED";
            }
        }
    }
}
