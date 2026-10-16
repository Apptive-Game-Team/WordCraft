using System;
using System.Collections.Generic;
using System.Reflection;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    internal static class ResourceRallyChecks
    {
        private static readonly List<Command> Idle = new List<Command>();
        private static FixVec2 At(int x, int y) => new FixVec2(Fix.FromInt(x), Fix.FromInt(y));
        private static void Require(bool ok, string why) { if (!ok) throw new Exception("resource rally: " + why); }
        private static World Create(Faction faction = Faction.TreeSpirits)
        {
            var world = new World(47);
            world.SetPeerFaction(0, faction);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            world.SpawnResourceNode(At(10, 5), 1000);
            world.GrantResources(0, 1000);
            return world;
        }
        private static void Rally(World world, int arg, int peer = 0) =>
            world.Step(new List<Command> { new Command(world.Tick, peer, 0, CommandType.SetRallyPoint, 0, At(10, 5), arg) });
        private static void Produce(World world, int building = 0, Role role = Role.Worker) =>
            world.Step(new List<Command> { new Command(world.Tick, 0, 0, CommandType.Produce, building, FixVec2.Zero, (int)role) });
        private static void Advance(World world, int ticks) { for (int i = 0; i < ticks; i++) world.Step(Idle); }

        public static void Check()
        {
            foreach (Faction faction in Enum.GetValues(typeof(Faction)))
            {
                World a = Create(faction), b = Create(faction);
                Rally(a, 2); Rally(b, 2);
                Produce(a); Produce(b);
                int paidBank = a.GetResources(0);
                for (int tick = 0; tick < 240; tick++)
                {
                    a.Step(Idle); b.Step(Idle);
                    Require(a.Hash() == b.Hash(), faction + " repeated input log diverged");
                }
                Require(a.EntityCount == 3 && a.GetEntity(2).Kind == EntityKind.Worker,
                    faction + " base failed to produce a worker");
                Require(a.GetEntity(2).GatherNodeId == 1 && a.GetResources(0) > paidBank,
                    faction + " rallied worker never gathered and delivered");
            }

            World ground = Create();
            Rally(ground, 2); Rally(ground, 0); Produce(ground); Advance(ground, 150);
            Require(ground.GetEntity(0).RallyResourceIdPlusOne == 0 && ground.GetEntity(2).GatherNodeId == -1,
                "ground retarget retained resource gathering");

            foreach (int arg in new[] { -1, int.MinValue, 1, 3, int.MaxValue })
            {
                World invalid = Create();
                Rally(invalid, 2); Rally(invalid, arg); Produce(invalid); Advance(invalid, 100);
                Require(invalid.GetEntity(0).RallyResourceIdPlusOne == 0 && invalid.GetEntity(2).GatherNodeId == -1,
                    "invalid resource argument activated gathering: " + arg);
            }

            World foreign = Create(), control = Create();
            Rally(foreign, 2, 1); control.Step(Idle);
            Require(foreign.Hash() == control.Hash(), "foreign peer changed the building rally");

            // Same destination, different resource intent must be detectable immediately.
            World gather = Create(), move = Create();
            Rally(gather, 2); Rally(move, 0);
            Require(gather.GetEntity(0).RallyPoint.Equals(move.GetEntity(0).RallyPoint) && gather.Hash() != move.Hash(),
                "resource rally intent was omitted from state hashing");

            // The resource disappears after an accepted rally but before production finishes.
            // Fixture mutation isolates death/depletion independently of combat timing.
            foreach (bool dead in new[] { false, true })
            {
                World gone = Create();
                Rally(gone, 2); Produce(gone);
                var entities = (List<Entity>)typeof(World).GetField("entities", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gone);
                Entity node = entities[1];
                node.Alive = !dead;
                node.Resource = 0;
                entities[1] = node;
                Advance(gone, 100);
                Entity worker = gone.GetEntity(2);
                Require(worker.GatherNodeId == -1 && !worker.Position.Equals(gone.GetEntity(0).Position + World.RallyOffset),
                    "missing resource did not fall back to movement");
            }

            World fighter = Create();
            int site = fighter.SpawnBuilding(0, Role.Production, At(5, 10), complete: true);
            fighter.Step(new List<Command> { new Command(0, 0, 0, CommandType.SetRallyPoint, site, At(10, 5), 2) });
            Produce(fighter, site, Role.Melee); Advance(fighter, 100);
            Require(fighter.GetEntity(3).Kind == EntityKind.Unit && fighter.GetEntity(3).GatherNodeId == -1,
                "combat unit inherited a worker gathering order");
            Console.WriteLine("OK: resource rallies gather for six factions, reset safely, reject foreign orders and preserve determinism");
        }
    }
}
