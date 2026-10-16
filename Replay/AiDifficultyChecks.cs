using System;
using System.Collections.Generic;
using System.Reflection;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    internal static class AiDifficultyChecks
    {
        private static void Require(bool condition, string message)
        { if (!condition) throw new Exception("AI difficulty: " + message); }

        internal static void Check()
        {
            foreach (AiDifficulty difficulty in Enum.GetValues(typeof(AiDifficulty)))
                foreach (Faction faction in Enum.GetValues(typeof(Faction))) CheckMatch(faction, difficulty);
            CheckDefense();
            CheckMixedWavesAndBuildingApproaches();
            Console.WriteLine("OK: six factions respect AI difficulty pacing, economy and deterministic defaults");
        }

        private static void CheckMatch(Faction faction, AiDifficulty difficulty)
        {
            World a = MatchScenario.Build(0xC0FFEE, Faction.Hellfire, faction);
            World b = MatchScenario.Build(0xC0FFEE, Faction.Hellfire, faction);
            Require(a.GetPeerAiDifficulty(1) == AiDifficulty.Hard, "fresh scenario changed legacy difficulty");
            if (difficulty != AiDifficulty.Hard) a.SetPeerAiDifficulty(1, difficulty);
            b.SetPeerAiDifficulty(1, difficulty);
            a.SetPeerAi(1, true); b.SetPeerAi(1, true);
            int warmup = difficulty == AiDifficulty.Easy ? 2400 : 1200;
            int spacing = difficulty == AiDifficulty.Easy ? 1200 : 600;
            int period = difficulty == AiDifficulty.Easy ? 40 : difficulty == AiDifficulty.Normal ? 20 : 10;
            int cap = difficulty == AiDifficulty.Easy ? 3 : 5;
            int lastWave = -1, waves = 0;
            bool economy = false;
            int until = difficulty == AiDifficulty.Hard ? 800 : warmup + spacing + 1;
            for (int tick = 0; tick < until && !a.MatchOver; tick++)
            {
                a.Step(Array.Empty<Command>()); b.Step(Array.Empty<Command>());
                Require(a.Hash() == b.Hash(), faction + "/" + difficulty + " desync at " + tick);
                int offense = 0;
                foreach (Command command in a.LastTickCommands)
                {
                    if (command.PeerId != 1) continue;
                    Require(tick % period == 0, "decision outside difficulty think period");
                    if (command.Type == CommandType.Gather || command.Type == CommandType.Produce || command.Type == CommandType.Build)
                        economy = true;
                    if (difficulty != AiDifficulty.Hard && command.Type == CommandType.AttackMove)
                    {
                        Require(tick >= warmup, faction + "/" + difficulty + " attacked before grace period");
                        offense++;
                    }
                }
                if (offense > 0)
                {
                    Require(offense <= cap, "wave exceeds difficulty cap");
                    Require(lastWave < 0 || tick - lastWave >= spacing, "reinforcement bypassed wave interval");
                    lastWave = tick; waves++;
                }
                Require(a.GetResources(1) >= 0, "difficulty overspent resources");
                if (difficulty != AiDifficulty.Hard)
                {
                    int workers = 0;
                    for (int id = 0; id < a.EntityCount; id++)
                    {
                        Entity e = a.GetEntity(id);
                        if (e.Alive && e.Owner == 1 && e.Kind == EntityKind.Worker) workers++;
                    }
                    int target = difficulty == AiDifficulty.Easy ? 4 : 5;
                    Require(workers <= target + (faction == Faction.TreeSpirits ? World.AiSeedWorkers : 0),
                        "worker target ignored difficulty");
                }
            }
            Require(economy, "opening grace period stopped the economy");
            if (difficulty != AiDifficulty.Hard) Require(waves > 0, faction + "/" + difficulty + " never attacked after grace period");
            Console.WriteLine("AI difficulty " + faction + "/" + difficulty +
                (difficulty == AiDifficulty.Hard ? ": legacy/default policy repeat matched" : ": waves=" + waves + " last=" + lastWave));
        }

        private static void CheckDefense()
        {
            World easy = new World(5);
            easy.SetPeerFaction(0, Faction.Hellfire);
            easy.SetPeerFaction(1, Faction.Hellfire);
            easy.SpawnBuilding(1, Role.Base, At(10, 10), complete: true);
            int guard = easy.SpawnUnit(1, Role.Melee, At(12, 10));
            easy.SpawnUnit(0, Role.Melee, At(18, 10));
            easy.SetPeerAiDifficulty(1, AiDifficulty.Easy);
            easy.SetPeerAi(1, true);
            easy.Step(Array.Empty<Command>());
            bool defended = false;
            foreach (Command command in easy.LastTickCommands)
                if (command.PeerId == 1 && command.EntityId == guard && command.Type == CommandType.AttackMove) defended = true;
            Require(defended, "opening grace period disabled home defense");
            World hard = MatchScenario.Build(5, Faction.Hellfire, Faction.Hellfire);
            ulong before = hard.Hash();
            hard.SetPeerAiDifficulty(1, AiDifficulty.Easy);
            Require(hard.Hash() != before, "difficulty missing from state hash");
        }

        private static FixVec2 At(int x, int y) => new FixVec2(Fix.FromInt(x), Fix.FromInt(y));

        private static void CheckMixedWavesAndBuildingApproaches()
        {
            foreach (AiDifficulty difficulty in new[] { AiDifficulty.Easy, AiDifficulty.Normal })
            {
                var world = new World(7);
                world.SetPeerFaction(1, Faction.Hellfire);
                world.SpawnBuilding(1, Role.Base, At(5, 5), complete: true);
                int enemy = world.SpawnBuilding(0, Role.Base, At(55, 55), complete: true);
                int firstFighter = world.EntityCount;
                for (int i = 0; i < 7; i++) world.SpawnUnit(1, Role.Melee, At(7 + i, 8));
                int support = world.SpawnUnit(1, Role.Signature, 2, At(7, 10));
                world.SetPeerAiDifficulty(1, difficulty);
                world.SetPeerAi(1, true);
                int warmup = difficulty == AiDifficulty.Easy ? 2400 : 1200;
                for (int tick = 0; tick <= warmup; tick++) world.Step(Array.Empty<Command>());
                int sent = 0;
                bool supportSent = false;
                foreach (Command command in world.LastTickCommands)
                {
                    if (command.PeerId != 1) continue;
                    if (command.Type == CommandType.AttackMove || command.Type == CommandType.Move) sent++;
                    if (command.EntityId == support && command.Type == CommandType.Move) supportSent = true;
                }
                Require(supportSent && sent <= (difficulty == AiDifficulty.Easy ? 3 : 5),
                    difficulty + " full fighter roster starved support or exceeded wave cap");

                // Isolate the acquired-building state produced by combat at a
                // cliff: the next think must replace straight chasing with a
                // pathing Attack even while the next wave is on cooldown.
                var entities = (List<Entity>)typeof(World).GetField("entities", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
                Entity fighter = entities[firstFighter];
                fighter.Mode = OrderMode.AttackMove;
                fighter.TargetId = enemy;
                entities[firstFighter] = fighter;
                bool repaired = false;
                int period = difficulty == AiDifficulty.Easy ? 40 : 20;
                for (int tick = 0; tick < period; tick++)
                {
                    // Combat may clear an out-of-acquisition target before the
                    // think; restore only the pre-think fixture state.
                    if (world.Tick % period == 0)
                    {
                        fighter = entities[firstFighter]; fighter.Mode = OrderMode.AttackMove; fighter.TargetId = enemy;
                        entities[firstFighter] = fighter;
                    }
                    world.Step(Array.Empty<Command>());
                    foreach (Command command in world.LastTickCommands)
                        if (command.EntityId == firstFighter && command.Type == CommandType.Attack && command.Arg == enemy) repaired = true;
                }
                Require(repaired, difficulty + " omitted building path repair during wave cooldown");
            }
        }
    }
}
