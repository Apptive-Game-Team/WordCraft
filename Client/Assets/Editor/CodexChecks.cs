using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class CodexChecks
    {
        public static void Run(Action<bool, string> report)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            CodexPanel previousPanel = CodexPanel.Instance;
            MatchRunner previousRunner = MatchRunner.Instance;
            bool previousOpen = CodexPanel.IsOpen;
            GameObject fixture = null;
            CodexPanel panel = previousPanel;
            var saved = new List<(FieldInfo Field, object Value)>();
            List<(Role Role, int Slot)> cached = null;
            (Role Role, int Slot)[] savedEntries = null;
            try
            {
                if (panel == null || previousRunner == null) fixture = new GameObject("codex checks");
                if (panel == null)
                {
                    panel = fixture.AddComponent<CodexPanel>();
                    SetStatic(typeof(CodexPanel), "Instance", panel); // Edit-mode Awake is not guaranteed.
                }
                foreach (FieldInfo field in typeof(CodexPanel).GetFields(fields))
                    if (!field.IsInitOnly) saved.Add((field, field.GetValue(panel)));
                cached = (List<(Role Role, int Slot)>)typeof(CodexPanel).GetField("entries", fields).GetValue(panel);
                savedEntries = cached.ToArray();
                MatchRunner runner = previousRunner;
                if (runner == null)
                {
                    runner = fixture.AddComponent<MatchRunner>();
                    SetStatic(typeof(MatchRunner), "Instance", runner);
                    runner.StartSolo(Faction.TreeSpirits);
                }
                World world = runner.World;
                ulong before = world.Hash();
                int tick = world.Tick;
                Phase phase = runner.Phase;
                // Fixed roster totals deliberately catch silently missing entries,
                // including units acquired through growth, spawning or capture.
                int[] units = { 12, 12, 12, 12, 12, 12 };
                int[] buildings = { 9, 9, 9, 9, 9, 11 };
                foreach (Faction faction in Enum.GetValues(typeof(Faction)))
                {
                    panel.Select(faction, Role.Worker, 0);
                    panel.Search = "";
                    panel.Filter = 0;
                    int all = panel.VisibleCount();
                    panel.Filter = 1; int unitCount = panel.VisibleCount();
                    panel.Filter = 2; int buildingCount = panel.VisibleCount();
                    Require(unitCount == units[(int)faction] && buildingCount == buildings[(int)faction] && all == unitCount + buildingCount,
                        faction + " roster totals or disjoint filters changed");
                    panel.Filter = 0;
                    string name = FactionData.Name(faction, Role.Worker, 0);
                    panel.Search = "  " + name + "  ";
                    Require(panel.VisibleCount() >= 1, faction + " Korean name or trimmed search failed");
                    foreach (var entry in cached)
                        Require(FactionData.Name(faction, entry.Role, entry.Slot).Contains(name), "search included an unrelated name");
                    panel.Search = "존재하지않는도감검색결과_8fc71";
                    Require(panel.VisibleCount() == 0, "missing search returned a roster entry");
                    panel.Search = "";
                    panel.VisibleCount();
                    foreach (var entry in cached)
                    {
                        string detail = CodexPanel.Details(faction, entry.Role, entry.Slot);
                        Require(detail.Contains("체력 ") && detail.Contains("필요 단계: T"), "entry lacks stats or tier");
                        var needs = FactionData.Prerequisite(faction, entry.Role, entry.Slot);
                        if (needs.Role != Role.None)
                            Require(detail.Contains("선행 건물: " + FactionData.Name(faction, needs.Role, needs.Slot)), "missing prerequisite");
                    }
                    string worker = CodexPanel.Details(faction, Role.Worker, 0);
                    Require(worker.Contains("생산: 마나 ") && worker.Contains("생산 건물: ") &&
                        worker.Contains(FactionData.Name(faction, Role.Base, 0)), "worker production source missing");
                    Require(CodexPanel.Details(faction, Role.Base, 0).Contains("건설: 마나 "), "building construction cost missing");
                }
                string growth = CodexPanel.Details(Faction.TreeSpirits, Role.Ranged, 1);
                Require(growth.Contains("성장: 풀씨 정령") && !growth.Contains("생산: 마나"), "growth-only unit advertised as producible");
                Require(CodexPanel.Details(Faction.Hellfire, Role.Ranged, 0).Contains("일반 생산 불가"), "spawn-only unit advertised as producible");
                Require(CodexPanel.Details(Faction.Humans, Role.Melee, 0) == "", "absent roster entry has details");

                CodexPanel.Close();
                Require(!CodexPanel.IsOpen && !CodexPanel.Covers(Vector2.zero), "close retained input capture");
                CodexPanel.Toggle();
                Require(CodexPanel.IsOpen, "toggle did not open codex");
                foreach (Vector2 point in new[] { Vector2.zero, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), new Vector2(Screen.width - 1, Screen.height - 1) })
                    Require(CodexPanel.Covers(point) && Hud.OverUi(point), "codex allowed world input through its overlay");
                CodexPanel.Toggle();
                Require(!CodexPanel.IsOpen, "second toggle did not close codex");
                Require(ReferenceEquals(world, runner.World) && world.Hash() == before && world.Tick == tick && runner.Phase == phase,
                    "reading codex changed the match");
                report(true, "codex covers six rosters, Korean search, filters, production/growth requirements and read-only input capture");
            }
            catch (Exception error) { report(false, "codex: " + error); }
            finally
            {
                if (previousPanel != null)
                {
                    foreach (var state in saved) state.Field.SetValue(previousPanel, state.Value);
                    if (cached != null && savedEntries != null) { cached.Clear(); cached.AddRange(savedEntries); }
                }
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                SetStatic(typeof(CodexPanel), "Instance", previousPanel);
                SetStatic(typeof(MatchRunner), "Instance", previousRunner);
                SetStatic(typeof(CodexPanel), "IsOpen", previousOpen);
            }
        }

        private static void SetStatic(Type type, string property, object value) =>
            type.GetProperty(property).GetSetMethod(true).Invoke(null, new[] { value });
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
