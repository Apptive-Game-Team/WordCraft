using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Read-only objectives over a real match; the player issues every game command.</summary>
    public sealed class TutorialPanel : MonoBehaviour
    {
        // This onboarding panel deliberately uses larger text than the compact match HUD.
        private const float Width = 340f;
        private const float Height = 292f;
        private const float Padding = 12f;
        private const float ButtonHeight = 30f;
        private const float Gap = 6f;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle hintStyle;
        private Vector2 scroll;
        private TutorialProgress shown;
        private int shownStep = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Tutorial").AddComponent<TutorialPanel>();

        private void Awake() => DontDestroyOnLoad(gameObject);

        private static bool Visible(MatchRunner runner) =>
            runner != null && runner.Phase == Phase.Match && runner.Tutorial != null && !Hud.MixerOpen;

        private static Rect Bounds()
        {
            float width = Mathf.Min(Width, Screen.width - Padding * 2f);
            float top = UiStyle.TopBar + Padding + (Screen.width < 900 ? UiStyle.AlertHeight + UiStyle.S2 : 0f);
            float available = Screen.height - top - UiStyle.BottomPanel - Padding;
            return new Rect(Screen.width - width - Padding, top,
                width, Mathf.Min(Height, Mathf.Max(190f, available)));
        }

        /// <summary>Input uses bottom-left pixels; the GUI rectangle uses top-left pixels.</summary>
        public static bool Covers(Vector2 screenPosition)
        {
            if (!Visible(MatchRunner.Instance)) return false;
            return Bounds().Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        }

        private void Styles()
        {
            if (bodyStyle != null) return;
            bodyStyle = new GUIStyle(UiStyle.Toned(UiStyle.Ink))
            {
                fontSize = 17,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(),
                margin = new RectOffset(),
            };
            titleStyle = new GUIStyle(bodyStyle) { fontSize = 19, fontStyle = FontStyle.Bold };
            hintStyle = new GUIStyle(bodyStyle) { fontSize = 13 };
            hintStyle.normal.textColor = UiStyle.InkDim;
        }

        private void OnGUI()
        {
            MatchRunner runner = MatchRunner.Instance;
            if (!Visible(runner)) return;
            TutorialProgress progress = runner.Tutorial;
            if (!ReferenceEquals(shown, progress) || shownStep != progress.Step)
            {
                shown = progress;
                shownStep = progress.Step;
                scroll = Vector2.zero;
            }
            Styles();
            Rect panel = Bounds();
            UiStyle.PanelBox(panel, UiStyle.Panel);
            float x = panel.x + Padding;
            float width = panel.width - Padding * 2f;
            float y = panel.y + Padding;
            string heading = progress.Completed ? "튜토리얼 완료" : "기초 훈련  " + (progress.Step + 1) + " / " + TutorialProgress.StepCount;
            GUI.Label(new Rect(x, y, width, 25f), heading, titleStyle);
            y += 29f;
            float ratio = Mathf.Clamp01(progress.Step / (float)TutorialProgress.StepCount);
            UiStyle.Fill(new Rect(x, y, width, 5f), UiStyle.Void);
            UiStyle.Fill(new Rect(x, y, width * ratio, 5f), UiStyle.Good);
            y += 13f;

            float rows = progress.Completed ? 3f : 2f;
            float buttonsY = panel.yMax - Padding - rows * ButtonHeight - (rows - 1f) * Gap;
            float hintY = buttonsY - 23f;
            Rect textArea = new Rect(x, y, width, Mathf.Max(24f, hintY - y - Gap));
            string text = progress.Title + "\n" + progress.Instructions;
            float contentWidth = width - 16f;
            float textHeight = bodyStyle.CalcHeight(new GUIContent(text), contentWidth);
            scroll = GUI.BeginScrollView(textArea, scroll,
                new Rect(0f, 0f, contentWidth, Mathf.Max(textArea.height, textHeight)));
            GUI.Label(new Rect(0f, 0f, contentWidth, Mathf.Max(textArea.height, textHeight)), text, bodyStyle);
            GUI.EndScrollView();
            GUI.Label(new Rect(x, hintY, width, 20f),
                progress.Completed ? "훈련 완료 · 아래에서 다음 행동을 선택하세요" :
                runner.Paused ? "멈춤 · P를 누르면 계속합니다" : "P: 일시정지 / 계속", hintStyle);

            float half = (width - Gap) * 0.5f;
            int focus = progress.FocusEntityId(runner.World);
            if (UiStyle.Button(new Rect(x, buttonsY, half, ButtonHeight), "목표 보기", enabled: focus >= 0))
                Focus(runner, focus);
            if (UiStyle.Button(new Rect(x + half + Gap, buttonsY, half, ButtonHeight), "기지 보기"))
                FocusBase(runner);
            buttonsY += ButtonHeight + Gap;
            if (UiStyle.Button(new Rect(x, buttonsY, half, ButtonHeight), "처음부터"))
            {
                runner.StartTutorial();
                return;
            }
            if (UiStyle.Button(new Rect(x + half + Gap, buttonsY, half, ButtonHeight), "메뉴"))
            {
                runner.ShowStart();
                return;
            }
            if (progress.Completed && UiStyle.Button(new Rect(x, buttonsY + ButtonHeight + Gap, width, ButtonHeight),
                "AI와 실전 시작", armed: true)) runner.StartSolo(Faction.TreeSpirits, AiDifficulty.Easy);
        }

        private static void Focus(MatchRunner runner, int id)
        {
            if (CameraRig.Instance == null || id < 0 || id >= runner.World.EntityCount) return;
            Entity entity = runner.World.GetEntity(id);
            if (entity.Alive) CameraRig.Instance.CenterOn(runner.DrawPosition(id));
        }

        private static void FocusBase(MatchRunner runner)
        {
            for (int i = 0; i < runner.World.EntityCount; i++)
            {
                Entity entity = runner.World.GetEntity(i);
                if (entity.Alive && entity.Owner == runner.LocalPeer && entity.Role == Role.Base)
                {
                    Focus(runner, i);
                    return;
                }
            }
        }
    }
}
