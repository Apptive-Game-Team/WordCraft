using System;
using System.Collections;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Opt-in render fixture; never created during an ordinary launch.</summary>
    public sealed class CardUiPreview : MonoBehaviour
    {
        private string output;
        private World world;
        private CardSlot detail;
        private int caseIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-cardPreview");
            if (at < 0 || at + 1 >= args.Length) return;
            var preview = new GameObject("Card UI render fixture").AddComponent<CardUiPreview>();
            preview.output = args[at + 1];
        }

        private IEnumerator Start()
        {
            // Hide the ordinary menu only in this isolated screenshot process.
            foreach (Hud hud in FindObjectsOfType<Hud>()) hud.enabled = false;
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            for (caseIndex = 0; caseIndex < 3; caseIndex++)
            {
                Faction faction = caseIndex == 0 ? Faction.TreeSpirits :
                    caseIndex == 1 ? Faction.WaterSlimes : Faction.Humans;
                Role role = caseIndex == 0 ? Role.Worker :
                    caseIndex == 1 ? Role.Signature : Role.Tech;
                world = MatchScenario.Build(0xC0FFEE, faction, Faction.Hellfire);
                detail = Slot(faction, role, caseIndex == 2 ? CommandType.Build : CommandType.Produce);
                for (int i = 0; i < 12; i++) yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(output + "-" + caseIndex + ".png");
                for (int i = 0; i < 8; i++) yield return new WaitForEndOfFrame();
            }
            Debug.Log("OK: card preview screenshots captured");
            Application.Quit(0);
        }

        private static CardSlot Slot(Faction faction, Role role, CommandType type)
        {
            int cost = type == CommandType.Build ? FactionData.BuildCost(faction, role, 0) :
                FactionData.Production(faction, role, 0).Resources;
            return new CardSlot { Type = type, Produce = role, Slot = 0,
                Label = FactionData.Name(faction, role, 0) + "\n" + cost };
        }

        private void OnGUI()
        {
            if (world == null || caseIndex >= 3) return;
            GUI.depth = -1000;
            UiStyle.Fill(new Rect(0, 0, Screen.width, Screen.height), UiStyle.Void);
            UiStyle.Note(new Rect(20, 20, Screen.width - 40, 36),
                "실제 카드 렌더 · 여섯 종족 초상 · 활성 / 잠금 / 선택 상태");
            Faction[] factions = { Faction.TreeSpirits, Faction.Hellfire, Faction.WaterSlimes,
                Faction.RockGolems, Faction.Driftworlds, Faction.Humans };
            for (int f = 0; f < factions.Length; f++)
            {
                float x = 20 + f * 150;
                UiStyle.Note(new Rect(x, 65, 145, 25), factions[f].ToString());
                for (int s = 0; s < 2; s++)
                {
                    Role role = s == 0 ? Role.Worker : factions[f] == Faction.Humans ? Role.Ranged : Role.Melee;
                    CardSlot slot = Slot(factions[f], role, CommandType.Produce);
                    UiStyle.CardCell(new Rect(x + s * 60, 95, 54, 54), s == 0 ? "U" : "F",
                        CardPortrait.CostLabel(slot), f == 5, f != 4, CardPortrait.For(factions[f], slot));
                }
            }
            DrawGrid(new Rect(20, 200, 240, 200), CommandCard.Of(CardKind.BuildMenu,
                world, 0, -1, Role.Worker, 0, 0), "건설 1페이지");
            DrawGrid(new Rect(290, 200, 240, 200), CommandCard.Of(CardKind.BuildMenu,
                world, 0, -1, Role.Worker, 0, 1), "건설 2페이지");
            DrawGrid(new Rect(560, 200, 240, 200), CommandCard.Building(world, 0,
                Role.Production, 0, 0), "생산 카드");
            var anchor = new Rect(Screen.width - 70, Screen.height - 70, 54, 54);
            Texture portrait = CardPortrait.For(world.FactionOf(0), detail);
            UiStyle.CardCell(anchor, caseIndex == 0 ? "U" : "F", CardPortrait.CostLabel(detail), false, true, portrait);
            Rect tooltip = UiStyle.CardTooltip(anchor, CardDetails.Text(world, 0, detail), portrait);
            if (Event.current.type == EventType.Repaint &&
                (tooltip.xMin < 0 || tooltip.yMin < 0 || tooltip.xMax > Screen.width || tooltip.yMax > Screen.height))
                Debug.LogError("Card preview tooltip exceeded screen bounds: " + tooltip);
        }

        private void DrawGrid(Rect area, CardSlot[] slots, string title)
        {
            UiStyle.Note(new Rect(area.x, area.y - 28, area.width, 25), title);
            for (int i = 0; i < slots.Length; i++)
            {
                CardSlot slot = slots[i];
                var cell = new Rect(area.x + i % CommandCard.Cols * 60,
                    area.y + i / CommandCard.Cols * 60, 54, 54);
                if (slot.Type == CommandType.None && !slot.Aim && !slot.More)
                { UiStyle.EmptyCell(cell); continue; }
                Texture portrait = CardPortrait.For(world.FactionOf(0), slot);
                UiStyle.CardCell(cell, CommandCard.Shortcut(slot, i).ToString(),
                    portrait == null ? slot.Label : CardPortrait.CostLabel(slot), false,
                    !slot.Locked && !slot.Maxed, portrait);
            }
        }
    }
}
