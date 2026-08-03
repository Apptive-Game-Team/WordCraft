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
    /// IMGUI on purpose, and kept that way at roadmap 3-1. UI Toolkit needs a
    /// PanelSettings and a theme stylesheet, which are serialised assets, and the
    /// client boots from an empty scene through RuntimeInitializeOnLoadMethod so a
    /// gameplay change never has to touch one. IMGUI needs no asset at all, so the
    /// whole HUD stays a diff in one file.
    ///
    /// ponytail: IMGUI relayouts and allocates strings every frame. It is a few
    /// dozen widgets, so it does not show; move to UI Toolkit the day the HUD
    /// appears in a profile, and accept the asset that comes with it.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public const float TopBarHeight = 26f;
        public const float BottomPanelHeight = 150f;

        /// <summary>The minimap is square and as tall as the bar, which is what sizes it.</summary>
        private const float MinimapSize = BottomPanelHeight;

        private const float InfoWidth = 300f;
        private const float CardCell = 74f;
        private const float CardWidth = CommandCard.Cols * CardCell + 16f;
        private const float EntryWidth = 108f;
        private const float EntryHeight = 22f;

        private const float MenuWidth = 470f;
        private const float MenuHeight = 300f;

        /// <summary>The address the player last joined. Typing an IP twice is not a game.</summary>
        private const string AddressKey = "wordcraft.address";

        private static readonly Color PanelColor = new Color(0.06f, 0.06f, 0.08f, 0.94f);
        private static readonly Color HaltColor = new Color(0.35f, 0.05f, 0.05f, 0.94f);
        private static readonly Color PickedColor = new Color(0.6f, 1f, 0.65f);
        private static readonly Color BadColor = new Color(1f, 0.5f, 0.42f);

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
            return screenPosition.y <= BottomPanelHeight || screenPosition.y >= Screen.height - TopBarHeight;
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
        /// why it is opaque.
        /// </summary>
        private void StartScreen()
        {
            Rect panel = Middle(MenuWidth, MenuHeight);
            Fill(panel, PanelColor);

            GUILayout.BeginArea(Inside(panel));
            GUILayout.Label("WORDCRAFT");
            GUILayout.Space(8f);

            GUILayout.Label("faction");
            FactionPicker();
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("address", GUILayout.Width(56f));
            address = GUILayout.TextField(address ?? "", 45);
            GUILayout.Label("port", GUILayout.Width(30f));
            portText = GUILayout.TextField(portText ?? "", 5, GUILayout.Width(58f));
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);

            if (runner.Connecting) ConnectionState();
            else HostOrJoin();

            GUILayout.EndArea();
        }

        /// <summary>All six by name, because a faction the player cannot see is not a choice.</summary>
        private void FactionPicker()
        {
            string[] names = Enum.GetNames(typeof(Faction));
            for (int row = 0; row < names.Length; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < row + 3 && i < names.Length; i++)
                {
                    GUI.color = (int)faction == i ? PickedColor : Color.white;
                    if (GUILayout.Button(names[i], GUILayout.Height(24f))) faction = (Faction)i;
                    GUI.color = Color.white;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void HostOrJoin()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HOST", GUILayout.Height(30f))) Begin(null);
            if (GUILayout.Button("JOIN", GUILayout.Height(30f))) Begin(address);
            GUILayout.EndHorizontal();

            // No address, no port, no handshake: nothing here can be malformed, so
            // this one takes no error path and starts the match outright.
            if (GUILayout.Button("SOLO", GUILayout.Height(30f)))
            {
                formError = null;
                runner.StartSolo(faction);
            }

            if (formError != null)
            {
                GUI.color = BadColor;
                GUILayout.Label(formError);
                GUI.color = Color.white;
            }
            GUILayout.Label("one machine hosts, the other joins its address. same port on both.");
            GUILayout.Label("solo plays the simulation's own opponent, no network at all.");
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

            GUI.color = rejected ? BadColor : Color.white;
            GUILayout.Label(ConnectionText(session));
            GUI.color = Color.white;

            GUILayout.Space(6f);
            if (GUILayout.Button(rejected ? "back" : "cancel", GUILayout.Height(26f))) runner.ShowStart();
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
        /// what a player can copy into a bug report.
        /// </summary>
        private void ResultScreen()
        {
            Rect panel = Middle(MenuWidth + 220f, 210f);
            Fill(panel, runner.Halted ? HaltColor : PanelColor);

            GUILayout.BeginArea(Inside(panel));
            GUILayout.Label(runner.Outcome ?? "MATCH OVER");

            if (runner.Halted)
            {
                GUILayout.Label(runner.Session.StopReason ?? "no reason recorded");
                if (!runner.Session.ReportComplete) GUILayout.Label("waiting for the peer state dump...");
            }

            GUILayout.Space(6f);
            GUILayout.Label(runner.EndTick + " ticks, " + runner.EndSeconds.ToString("0.0") + " s");
            GUILayout.Space(10f);

            if (GUILayout.Button("play again", GUILayout.Height(30f))) runner.ShowStart();
            GUILayout.EndArea();
        }

        private static Rect Middle(float width, float height) =>
            new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        private static Rect Inside(Rect panel) =>
            new Rect(panel.x + 18f, panel.y + 14f, panel.width - 36f, panel.height - 28f);

        private static void Fill(Rect area, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Top: what the player has to know without asking. Population turns red at
        /// the cap because a capped peer produces nothing and the simulation says so
        /// silently; the number going red is the only warning there is.
        /// </summary>
        private void TopBar(World world)
        {
            var bar = new Rect(0f, 0f, Screen.width, TopBarHeight);
            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.88f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = Color.white;

            int peer = runner.LocalPeer;
            int used = world.GetPopulation(peer);
            int cap = world.PopulationCap(peer);

            GUI.Label(new Rect(10f, 4f, 180f, 20f), "mana " + world.GetResources(peer));

            GUI.color = used >= cap ? new Color(1f, 0.45f, 0.35f) : Color.white;
            GUI.Label(new Rect(150f, 4f, 180f, 20f), "pop " + used + "/" + cap);
            GUI.color = Color.white;

            GUI.Label(new Rect(280f, 4f, 160f, 20f), "tick " + world.Tick);
            GUI.Label(new Rect(400f, 4f, 420f, 20f),
                StateText(runner.Session) + "  peer " + peer + "  (" + runner.Link + ")");

            if (selection == null) return;
            int idle = selection.IdleWorkerCount();
            GUI.enabled = idle > 0;
            if (GUI.Button(new Rect(Screen.width - 130f, 2f, 122f, TopBarHeight - 4f), "idle worker " + idle))
            {
                selection.CycleIdleWorker();
            }
            GUI.enabled = true;
        }

        private void BottomPanel(World world)
        {
            var panel = new Rect(0f, Screen.height - BottomPanelHeight, Screen.width, BottomPanelHeight);
            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.88f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            int lead = selection == null
                ? -1
                : CommandCard.Representative(world, selection.Selected, runner.LocalPeer);

            // Flush into the corner and the full height of the bar: the minimap is
            // aimed at by muscle memory, and a margin around it is a margin the
            // pointer can miss into.
            Minimap.Draw(new Rect(0f, panel.y, MinimapSize, MinimapSize), runner);

            float textX = MinimapSize + 8f;
            Info(new Rect(textX, panel.y + 8f, InfoWidth, BottomPanelHeight - 16f), world, lead);

            float cardX = Screen.width - CardWidth - 8f;
            List(new Rect(textX + InfoWidth + 8f, panel.y + 8f,
                Mathf.Max(0f, cardX - textX - InfoWidth - 16f), BottomPanelHeight - 16f), world);
            Card(new Rect(cardX, panel.y + 8f, CardWidth, BottomPanelHeight - 16f));
        }

        /// <summary>Left: who this is, how hurt, whose. One representative plus the count.</summary>
        private void Info(Rect area, World world, int lead)
        {
            GUILayout.BeginArea(area);
            if (lead < 0)
            {
                GUILayout.Label("nothing selected");
                GUILayout.Label("drag to select, right click to order");
                GUILayout.EndArea();
                return;
            }

            Entity e = world.GetEntity(lead);
            GUILayout.Label(FactionData.Name(world.FactionOf(e.Owner), e.Role));
            GUILayout.Label("hp " + e.Hp + " / " + e.MaxHp);

            GUI.color = PeerColor(e.Owner);
            GUILayout.Label("peer " + e.Owner + "  " + world.FactionOf(e.Owner));
            GUI.color = Color.white;

            int count = selection.Selected.Count;
            if (count > 1) GUILayout.Label(count + " selected");

            if (e.Kind == EntityKind.Building)
            {
                GUILayout.Label(e.BuildTicksLeft > 0
                    ? "under construction, " + e.BuildTicksLeft + "t left"
                    : "queue " + e.QueueCount + "  next in " + e.ProduceTicksLeft + "t");
            }
            else if (e.Kind == EntityKind.Worker)
            {
                GUILayout.Label("carrying " + e.CarryAmount);
            }

            if (Orders.Instance != null && Orders.Instance.Pending != CommandType.None)
            {
                GUILayout.Label(Orders.Instance.Pending + ": click a target, Esc cancels");
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// Middle: the selection as a grid. A click keeps that one entity, a
        /// ctrl-click keeps every entity of the same role already selected.
        /// </summary>
        private void List(Rect area, World world)
        {
            if (selection == null || area.width < EntryWidth) return;

            int cols = Mathf.Max(1, (int)(area.width / EntryWidth));
            int rows = Mathf.Max(1, (int)(area.height / EntryHeight));
            bool ctrl = Event.current.control || Event.current.command;

            for (int i = 0; i < selection.Selected.Count; i++)
            {
                if (i >= cols * rows)
                {
                    GUI.Label(new Rect(area.x, area.yMax - EntryHeight, EntryWidth, EntryHeight),
                        "+" + (selection.Selected.Count - i) + " more");
                    return;
                }

                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                var cell = new Rect(area.x + i % cols * EntryWidth, area.y + i / cols * EntryHeight,
                    EntryWidth - 3f, EntryHeight - 3f);

                GUI.color = e.Hp * 2 < e.MaxHp ? new Color(1f, 0.55f, 0.5f) : Color.white;
                bool clicked = GUI.Button(cell, Short(world, e) + " " + e.Hp);
                GUI.color = Color.white;

                if (!clicked) continue;
                if (ctrl) selection.KeepRole(e.Role);
                else selection.SelectOnly(id);
                return; // the list just changed under the loop
            }
        }

        /// <summary>
        /// Right: the 3x3 card. The key is drawn on its own cell because the cell
        /// is the thing worth learning; see CommandCard for the table behind it.
        /// </summary>
        private void Card(Rect area)
        {
            Orders orders = Orders.Instance;
            if (orders == null) return;

            CardKind kind = orders.Kind();
            CardSlot[] card = CommandCard.Of(kind, runner.World.FactionOf(runner.LocalPeer));

            // The card changes under the same rectangle, so it says what it is.
            // Without this a build submenu and a produce row look alike.
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(area.x + 2f, area.y - 16f, area.width, 16f), CommandCard.Title(kind));
            GUI.color = Color.white;

            float size = CardCell - 4f;

            for (int i = 0; i < CommandCard.Cells; i++)
            {
                var cell = new Rect(area.x + i % CommandCard.Cols * CardCell,
                    area.y + i / CommandCard.Cols * CardCell, size, size);

                if (card[i].Type == CommandType.None)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.12f);
                    GUI.DrawTexture(cell, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    continue;
                }

                GUI.color = orders.Pending == card[i].Type ? new Color(0.6f, 1f, 0.65f) : Color.white;
                if (GUI.Button(cell, CommandCard.Keys[i].ToString() + "\n" + card[i].Label)) orders.Run(card[i]);
                GUI.color = Color.white;
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

        private static Color PeerColor(int peer) =>
            peer >= 0 && peer < MatchView.PeerColor.Length ? MatchView.PeerColor[peer] : Color.gray;
    }
}
