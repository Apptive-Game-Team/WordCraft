using System;
using System.Reflection;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class EditorDifficultyChecks
    {
        public static void Run(Action<bool, string> report)
        {
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            MatchRunner previous = MatchRunner.Instance;
            var root = new GameObject("solo difficulty check");
            try
            {
                var runner = root.AddComponent<MatchRunner>();
                typeof(MatchRunner).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { runner });
                var hud = root.AddComponent<Hud>();
                Require((AiDifficulty)typeof(Hud).GetField("difficulty", hidden).GetValue(hud) == AiDifficulty.Easy,
                    "menu did not default to easy");
                foreach (AiDifficulty level in new[] { AiDifficulty.Hard, AiDifficulty.Normal, AiDifficulty.Easy })
                {
                    runner.StartSolo(Faction.TreeSpirits, level);
                    Require(runner.World.GetPeerAiDifficulty(1) == level && runner.World.IsPeerAi(1) && !runner.World.IsPeerAi(0),
                        "solo did not apply the selected opponent difficulty");
                    Require(runner.Link.Contains(MatchRunner.DifficultyName(level)) && runner.Tutorial == null && !runner.Paused,
                        "solo difficulty label or reset was incorrect");
                }
                runner.Session.Update(1000);
                bool economy = false;
                for (int tick = 0; tick < 200; tick++)
                {
                    long now = 1000 + tick * 50;
                    runner.Session.Update(now);
                    Require(runner.Session.TryStep(now), "easy solo tick barrier stalled");
                    foreach (Command command in runner.World.LastTickCommands)
                        if (command.PeerId == 1 && (command.Type == CommandType.Build || command.Type == CommandType.Produce)) economy = true;
                }
                Require(economy, "easy difficulty delayed all opening economy commands");
                runner.StartSolo(Faction.Humans, AiDifficulty.Hard);
                runner.StartTutorial();
                Require(runner.Tutorial != null && !runner.World.IsPeerAi(1) &&
                    runner.World.GetPeerAiDifficulty(1) == AiDifficulty.Easy, "tutorial inherited hard or active AI");
                runner.StartSolo(Faction.TreeSpirits);
                Require(runner.Tutorial == null && runner.World.GetPeerAiDifficulty(1) == AiDifficulty.Easy,
                    "default solo restart inherited a previous difficulty");
                runner.ShowStart();
                Require(runner.Phase == Phase.Start && runner.Tutorial == null && !runner.Paused &&
                    !runner.World.IsPeerAi(0) && !runner.World.IsPeerAi(1), "menu retained a solo opponent");
                var cfg = new MatchConfig();
                World neutral = MatchScenario.Build(cfg.Seed, MatchConfig.DefaultFaction(0), MatchConfig.DefaultFaction(1));
                Require(runner.World.Hash() == neutral.Hash(), "solo difficulty leaked into the fresh network scenario");
                report(true, "difficulty defaults to easy, applies all levels, keeps economy active and isolates tutorial/menu scenarios");
            }
            catch (Exception error) { report(false, "solo difficulty: " + error); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                typeof(MatchRunner).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { previous });
            }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
