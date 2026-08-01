using System.Text;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Match HUD. IMGUI on purpose: it needs no prefabs, no canvas, and no scene
    /// asset, so a HUD change is a diff in one file.
    ///
    /// ponytail: IMGUI allocates and relayouts every frame, which is fine for a
    /// dozen labels. Move to UI Toolkit when the HUD grows past a panel or when
    /// it shows up in a profile.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        private const int PanelWidth = 330;
        private const int MaxSelectionRows = 6;

        private MatchRunner runner;
        private Selection selection;
        private readonly StringBuilder line = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft HUD").AddComponent<Hud>();

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void Start()
        {
            runner = MatchRunner.Instance;
            selection = Selection.Instance;
        }

        private void OnGUI()
        {
            if (runner == null) return;
            World world = runner.World;
            LockstepSession session = runner.Session;

            GUILayout.BeginArea(new Rect(8f, 8f, PanelWidth, Screen.height - 16f), GUI.skin.box);

            GUILayout.Label("peer " + runner.LocalPeer + "  tick " + world.Tick);
            GUILayout.Label(StateText(session) + "  (" + runner.Link + ")");
            GUILayout.Space(6f);

            for (int peer = 0; peer < MatchScenario.Peers; peer++)
            {
                GUI.color = MatchView.PeerColor[peer];
                GUILayout.Label("peer " + peer + " resources: " + world.GetResources(peer));
                GUI.color = Color.white;
            }

            GUILayout.Space(6f);
            GUILayout.Label("production");
            bool any = false;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Kind != EntityKind.Building || e.Owner != runner.LocalPeer) continue;

                any = true;
                line.Length = 0;
                line.Append("  #").Append(i);
                if (e.BuildTicksLeft > 0) line.Append(" building, ").Append(e.BuildTicksLeft).Append("t left");
                else if (e.QueueCount > 0) line.Append(" queue ").Append(e.QueueCount)
                                               .Append(", next in ").Append(e.ProduceTicksLeft).Append("t");
                else line.Append(" idle");
                GUILayout.Label(line.ToString());
            }
            if (!any) GUILayout.Label("  no buildings");

            GUILayout.Space(6f);
            GUILayout.Label("selected (" + (selection == null ? 0 : selection.Selected.Count) + ")");
            if (selection != null)
            {
                for (int i = 0; i < selection.Selected.Count && i < MaxSelectionRows; i++)
                {
                    GUILayout.Label("  " + Describe(world, selection.Selected[i]));
                }
                if (selection.Selected.Count > MaxSelectionRows) GUILayout.Label("  ...");
            }

            GUILayout.Space(6f);
            GUILayout.Label(Orders.Instance != null && Orders.Instance.BuildMode
                ? "BUILD MODE: click to place, Esc to cancel"
                : "drag select, right click order, B build, P produce");

            GUILayout.EndArea();

            if (session.State == SessionState.Stopped) StopBanner(session);
        }

        private string Describe(World world, int id)
        {
            Entity e = world.GetEntity(id);
            line.Length = 0;
            line.Append('#').Append(id).Append(' ').Append(e.Kind)
                .Append("  hp ").Append(e.Hp).Append('/').Append(e.MaxHp);

            switch (e.Kind)
            {
                case EntityKind.Worker:
                    line.Append("  carry ").Append(e.CarryAmount);
                    if (e.GatherNodeId >= 0) line.Append("  node #").Append(e.GatherNodeId);
                    break;
                case EntityKind.Building:
                    line.Append("  queue ").Append(e.QueueCount);
                    break;
                default:
                    if (e.TargetId >= 0) line.Append("  fighting #").Append(e.TargetId);
                    break;
            }
            return line.ToString();
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
