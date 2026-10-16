using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class TutorialChecks
    {
        public static void Run(Action<bool, string> check)
        {
            try { Exercise(); check(true, "tutorial legal commands, early actions, reset and world isolation"); }
            catch (Exception error) { check(false, "tutorial: " + error); }
        }

        private static void Require(bool value, string message)
        { if (!value) throw new Exception(message); }

        private sealed class Practice
        {
            internal readonly World World = MatchScenario.Build(0xC0FFEE, Faction.TreeSpirits, Faction.Hellfire);
            internal readonly TutorialProgress Normal;
            internal readonly TutorialProgress Early;
            internal readonly int[] Selection = { 1 };
            private int sequence;
            internal Practice()
            {
                Normal = new TutorialProgress(World, 0);
                Early = new TutorialProgress(World, 0);
                Normal.Observe(World, Selection);
                Require(Normal.Step == 1 && Early.Step == 0, "selection evidence");
            }
            internal void Tick(params Command[] commands)
            {
                World.Step(commands);
                Normal.Observe(World, Selection);
                Early.Observe(World, Array.Empty<int>());
            }
            internal Command Order(CommandType type, int id, int arg = 0, int peer = 0, FixVec2 target = default) =>
                new Command(World.Tick, peer, sequence++, type, id, target, arg);
            internal void Wait(Func<bool> condition, string message, int limit = 3000)
            {
                for (int i = 0; i < limit && !condition(); i++) Tick();
                Require(condition(), message + " at " + World.Tick);
            }
            internal int Build(Role role, int slot)
            {
                Wait(() => World.GetResources(0) >= FactionData.BuildCost(Faction.TreeSpirits, role, slot), "building funds");
                for (int y = 4; y < 24; y += 3)
                    for (int x = 4; x < 24; x += 3)
                    {
                        var point = new FixVec2(Fix.FromInt(x), Fix.FromInt(y));
                        if (!World.CanBuild(0, role, slot, point)) continue;
                        int id = World.EntityCount;
                        Tick(Order(CommandType.Build, -1, Command.RosterArg(role, slot), target: point));
                        Require(World.EntityCount > id, "build command refused");
                        Wait(() => World.GetEntity(id).BuildTicksLeft == 0, "building did not complete");
                        return id;
                    }
                throw new Exception("no legal building site " + role + slot);
            }
        }

        private static void Exercise()
        {
            var p = new Practice();
            var w = p.World;
            p.Tick();
            Require(p.Normal.Step == 1, "starting roster falsely completed objectives");
            Require(p.Normal.FocusEntityId(w) == 5, "gather focus did not choose the nearby resource node");
            p.Tick(p.Order(CommandType.Gather, 1, 5), p.Order(CommandType.Gather, 2, 5));
            Require(p.Normal.Step == 1, "gather click counted before income");
            p.Wait(() => p.Normal.Step == 2, "no actual delivery evidence");
            int production = p.Build(Role.Production, 0);
            Require(p.Normal.Step == 3, "production construction not observed");
            p.Tick(p.Order(CommandType.Produce, production, Command.RosterArg(Role.Melee, 0)));
            Require(p.Normal.Step == 3, "queue start counted as completed production");
            p.Wait(() => p.Normal.Step == 4, "production completion missing");
            p.Tick(p.Order(CommandType.Grow, 2, Command.RosterArg(Role.Ranged, 1)));
            Require(p.Normal.Step == 4, "growth start counted as completed growth");
            p.Wait(() => p.Normal.Step == 5, "growth completion missing");
            int tech = p.Build(Role.Tech, 2);
            Require(p.Normal.Step == 6, "tech construction missing");
            int research = -1;
            for (int i = 0; i < FactionData.UpgradeCount(Faction.TreeSpirits); i++)
            {
                UpgradeRow row = FactionData.Upgrade(Faction.TreeSpirits, i);
                if (row.ResearchAt.Role == Role.Tech && row.ResearchAt.Slot == 2) { research = i; break; }
            }
            Require(research >= 0, "no research row");
            p.Wait(() => w.CanResearch(0, tech, research), "research funding");
            p.Tick(p.Order(CommandType.Research, tech, research));
            Require(p.Normal.Step == 6, "research start counted as completed research");
            p.Wait(() => p.Normal.Step == 7, "research completion missing");
            // Bring one hostile opening fighter to the defending player's army
            // using an ordinary peer-1 move command, without spawning a target.
            p.Tick(p.Order(CommandType.Move, 9, peer: 1, target: new FixVec2(Fix.FromInt(10), Fix.FromInt(10))));
            p.Wait(() => p.Normal.Completed, "no enemy death evidence", 5000);
            Require(p.Early.Step == 0, "unselected early tracker advanced");
            if (!w.GetEntity(p.Selection[0]).Alive)
            {
                p.Early.Observe(w, p.Selection);
                Require(p.Early.Step == 0, "dead worker selection counted");
            }
            // The incoming fighter can kill the original gathering worker. A
            // stale selection must not count, so select a surviving worker or
            // legally produce a replacement before revealing the early evidence.
            int lateWorker = FindWorker(w);
            if (lateWorker < 0)
            {
                int workerPrice = FactionData.Production(Faction.TreeSpirits, Role.Worker, 0).Resources;
                Require(w.GetResources(0) >= workerPrice, "no funds for replacement worker");
                p.Tick(p.Order(CommandType.Produce, 0, Command.RosterArg(Role.Worker, 0)));
                p.Wait(() => FindWorker(w) >= 0, "replacement worker did not complete");
                lateWorker = FindWorker(w);
            }
            p.Early.Observe(w, new[] { lateWorker });
            Require(p.Early.Completed, "early objectives were forgotten");
            World other = MatchScenario.Build(0xC0FFEE, Faction.TreeSpirits, Faction.Hellfire);
            var reset = new TutorialProgress(other, 0);
            reset.Observe(w, p.Selection);
            Require(reset.Step == 0 && reset.FocusEntityId(w) == -1, "foreign world changed progress");
            reset.Observe(other, Array.Empty<int>());
            Require(reset.Step == 0 && !reset.Completed, "new world inherited progress");
        }

        private static int FindWorker(World world)
        {
            for (int id = 0; id < world.EntityCount; id++)
            {
                Entity e = world.GetEntity(id);
                if (e.Alive && e.Owner == 0 && e.Kind == EntityKind.Worker && !World.IsGrowing(e)) return id;
            }
            return -1;
        }
    }
}
