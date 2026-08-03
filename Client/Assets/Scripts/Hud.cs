using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
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

        private const float InfoWidth = 300f;
        private const float CardCell = 74f;
        private const float CardWidth = CommandCard.Cols * CardCell + 16f;
        private const float EntryWidth = 108f;
        private const float EntryHeight = 22f;

        private MatchRunner runner;
        private Selection selection;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft HUD").AddComponent<Hud>();

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void Start()
        {
            runner = MatchRunner.Instance;
            selection = Selection.Instance;
        }

        /// <summary>
        /// True when a screen-space point is over the HUD instead of the world.
        /// Both the picker and the order code ask before acting, so a click on a
        /// command button never also drags a selection box across the map.
        /// </summary>
        public static bool OverUi(Vector2 screenPosition) =>
            screenPosition.y <= BottomPanelHeight || screenPosition.y >= Screen.height - TopBarHeight;

        private void OnGUI()
        {
            if (runner == null) return;
            World world = runner.World;

            TopBar(world);
            BottomPanel(world);

            if (world.MatchOver) ResultBanner(world, runner.LocalPeer);
            if (runner.Session.State == SessionState.Stopped) StopBanner(runner.Session);
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

            Info(new Rect(8f, panel.y + 8f, InfoWidth, BottomPanelHeight - 16f), world, lead);

            float cardX = Screen.width - CardWidth - 8f;
            List(new Rect(InfoWidth + 16f, panel.y + 8f,
                Mathf.Max(0f, cardX - InfoWidth - 24f), BottomPanelHeight - 16f), world);
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

            CardSlot[] card = CommandCard.Of(orders.Kind());
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

        /// <summary>
        /// The simulation decided this, not the client, so both players read the
        /// same result on the same tick without anyone announcing it over the wire.
        /// </summary>
        private static void ResultBanner(World world, int localPeer)
        {
            var rect = new Rect(Screen.width * 0.5f - 160f, TopBarHeight + 24f, 320f, 56f);
            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, rect.width - 28f, rect.height - 24f));
            GUILayout.Label(world.Winner < 0
                ? "DRAW: both bases fell on the same tick"
                : world.Winner == localPeer ? "VICTORY: enemy base destroyed" : "DEFEAT: base destroyed");
            GUILayout.EndArea();
        }

        /// <summary>
        /// A halted session is the whole reason the stop reason exists: it names
        /// the divergent tick and field, and it must not be something the player
        /// has to find in a log file.
        /// </summary>
        private static void StopBanner(LockstepSession session)
        {
            var rect = new Rect(Screen.width * 0.5f - 340f, Screen.height * 0.5f - 60f, 680f, 120f);
            GUI.color = new Color(0.6f, 0.05f, 0.05f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, rect.width - 28f, rect.height - 24f));
            GUILayout.Label("MATCH STOPPED");
            GUILayout.Label(session.StopReason ?? "no reason recorded");
            if (!session.ReportComplete) GUILayout.Label("waiting for the peer state dump...");
            GUILayout.EndArea();
        }
    }
}
