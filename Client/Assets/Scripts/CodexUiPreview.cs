using System;
using System.Collections;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Opt-in screenshots of the real codex; no injected desktop input.</summary>
    public sealed class CodexUiPreview : MonoBehaviour
    {
        private string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-codexPreview");
            if (at < 0 || at + 1 >= args.Length) return;
            new GameObject("Codex render fixture").AddComponent<CodexUiPreview>().output = args[at + 1];
        }

        private IEnumerator Start()
        {
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            if (!CodexPanel.IsOpen) CodexPanel.Toggle();
            CodexPanel panel = CodexPanel.Instance;
            for (int index = 0; index < 3; index++)
            {
                if (index == 0)
                {
                    panel.Search = "";
                    panel.Filter = 0;
                    panel.Select(Faction.TreeSpirits, Role.Worker, 0);
                }
                else if (index == 1)
                {
                    panel.Search = "구름";
                    panel.Filter = 1;
                    panel.Select(Faction.WaterSlimes, Role.Signature, 0);
                }
                else
                {
                    panel.Search = "";
                    panel.Filter = 2;
                    panel.Select(Faction.Humans, Role.Tech, 0);
                }
                for (int frame = 0; frame < 12; frame++) yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(output + "-" + index + ".png");
                for (int frame = 0; frame < 8; frame++) yield return new WaitForEndOfFrame();
                Debug.Log("OK: codex preview " + index + " entries=" + panel.VisibleCount());
            }
            CodexPanel.Close();
            Application.Quit(0);
        }
    }
}
