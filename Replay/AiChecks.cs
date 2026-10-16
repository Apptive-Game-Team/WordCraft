using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;
using static WordCraft.Replay.AbilityChecks;

namespace WordCraft.Replay
{
    internal static class AiChecks
    {
        private static readonly IReadOnlyList<Command> Idle = Array.Empty<Command>();

        internal static void Check()
        {
            CompositionTables();
            LiveSoloAcceptance();
            FundedFactionsUseComposition();
            AllFactionsPlayDeterministically();
            AreaCastsNeedACluster();
            VisibleAirPrioritizesAntiAir();
            SpecialActivesAreUsed();
        }

        private static void LiveSoloAcceptance()
        {
            string failures = "";
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                Faction faction = (Faction)f;
                World w = MatchScenario.Build(0xC0FFEE, Faction.WaterSlimes, faction);
                w.SetPeerAi(1, true);
                var roles = new bool[4];
                var entries = new HashSet<int>();
                int grows = 0, arrivals = 0, casts = 0, capRun = 0, longestCap = 0;
                bool compositionWitness = false;
                for (int tick = 0; tick < 2000 && !w.MatchOver; tick++)
                {
                    // A defending opponent gathers with its starting workers,
                    // buys basic fighters and holds at home. Nothing is injected.
                    // Its defended cluster gives area actives a real target.
                    var orders = new List<Command>();
                    for (int id = 0; id < w.EntityCount; id++)
                    {
                        Entity enemy = w.GetEntity(id);
                        if (enemy.Alive && enemy.Owner == 0 && enemy.Kind == EntityKind.Unit)
                            orders.Add(new Command(w.Tick, 0, 1000000 + w.Tick * 1000 + id, CommandType.HoldPosition, id, FixVec2.Zero));
                        if (!enemy.Alive || enemy.Owner != 0) continue;
                        if (enemy.Kind == EntityKind.Worker && enemy.GatherNodeId < 0)
                        {
                            int nearest = -1;
                            Fix distance = Fix.Zero;
                            for (int node = 0; node < w.EntityCount; node++)
                            {
                                Entity resource = w.GetEntity(node);
                                if (!resource.Alive || resource.Kind != EntityKind.ResourceNode || resource.Resource <= 0) continue;
                                Fix d = (resource.Position - enemy.Position).SqrMagnitude;
                                if (nearest < 0 || d < distance) { nearest = node; distance = d; }
                            }
                            if (nearest >= 0) orders.Add(new Command(w.Tick, 0, 2000000 + id, CommandType.Gather, id, FixVec2.Zero, nearest));
                        }
                        if (enemy.Kind == EntityKind.Building && enemy.Role == Role.Base && enemy.QueueCount == 0)
                            orders.Add(new Command(w.Tick, 0, 3000000 + id, CommandType.Produce, id, FixVec2.Zero, Command.RosterArg(Role.Melee, 0)));
                    }
                    var before = new Entity[w.EntityCount];
                    for (int id = 0; id < before.Length; id++) before[id] = w.GetEntity(id);
                    w.Step(orders);
                    capRun = w.GetPopulation(1) >= w.PopulationCap(1) ? capRun + 1 : 0;
                    longestCap = Math.Max(longestCap, capRun);
                    foreach (Command cmd in w.LastTickCommands)
                    {
                        if (cmd.PeerId != 1) continue;
                        Entity e = cmd.EntityId >= 0 ? w.GetEntity(cmd.EntityId) : default;
                        if (cmd.Type == CommandType.Grow && e.MorphTicksLeft > 0) grows++;
                        if (cmd.Type == CommandType.Produce && e.HasArrivalPoint) arrivals++;
                        if (cmd.Type == CommandType.Cast && cmd.EntityId < before.Length)
                        {
                            Entity previous = before[cmd.EntityId];
                            AbilityRow ability = FactionData.Ability(faction, cmd.Arg);
                            if (e.AbilityCooldown > previous.AbilityCooldown || e.AbilityTicksLeft > previous.AbilityTicksLeft ||
                                e.Deployed != previous.Deployed || (ability.Kind == AbilityKind.Harden && previous.Alive && !e.Alive)) casts++;
                        }
                    }
                    var currentRoles = new bool[4];
                    var currentEntries = new HashSet<int>();
                    for (int id = 0; id < w.EntityCount; id++)
                    {
                        Entity e = w.GetEntity(id);
                        if (!e.Alive || e.Owner != 1 || e.Kind != EntityKind.Unit) continue;
                        roles[(int)World.AiRoleOf(faction, e.Role, e.Slot)] = true;
                        entries.Add(Command.RosterArg(e.Role, e.Slot));
                        currentRoles[(int)World.AiRoleOf(faction, e.Role, e.Slot)] = true;
                        currentEntries.Add(Command.RosterArg(e.Role, e.Slot));
                    }
                    // Humans have no melee entries: their three-body witness
                    // instead requires distinct ranged/support roster entries.
                    compositionWitness |= currentEntries.Count >= 3 && currentRoles[2] && currentRoles[3] &&
                        (faction == Faction.Humans || currentRoles[1]);
                }
                Console.WriteLine($"AI solo {faction}: ticks={w.Tick} entries={entries.Count} simultaneous={compositionWitness} melee={roles[1]} ranged={roles[2]} support={roles[3]} grow={grows} arrival={arrivals} cast={casts} cap={longestCap}");
                if (!compositionWitness) failures += faction + " simultaneous composition; ";
                if (faction == Faction.TreeSpirits && grows == 0) failures += "tree growth; ";
                if (faction == Faction.Driftworlds && arrivals == 0) failures += "drift arrival; ";
                if (faction != Faction.Driftworlds && casts == 0) failures += faction + " active; ";
                if (longestCap > 100) failures += faction + " supply; ";
            }
            Require(failures.Length == 0, failures);
        }

        private static void FundedFactionsUseComposition()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                Faction faction = (Faction)f;
                World w = Two(faction, Faction.TreeSpirits);
                w.GrantResources(0, 10000);
                int building = 0;
                for (int role = 0; role < FactionData.RoleCount; role++)
                {
                    Role kind = (Role)role;
                    if (!FactionData.IsBuilding(kind)) continue;
                    for (int slot = 0; slot < FactionData.SlotCount(faction, kind); slot++)
                        if (FactionData.Has(faction, kind, slot))
                        {
                            w.SpawnBuilding(0, kind, slot, At(3 + building % 6 * 3, 3 + building / 6 * 3), complete: true);
                            building++;
                        }
                }
                w.SpawnBuilding(1, Role.Base, At(60, 60), complete: true);
                for (int i = 0; i < World.AiWorkerTarget; i++) w.SpawnWorker(0, At(5 + i, 15));
                w.SetPeerAi(0, true);
                var roles = new bool[4];
                for (int tick = 0; tick < 600 && !w.MatchOver; tick++)
                {
                    w.Step(Idle);
                    for (int id = 0; id < w.EntityCount; id++)
                    {
                        Entity e = w.GetEntity(id);
                        if (e.Owner == 0 && e.Kind == EntityKind.Unit)
                            roles[(int)World.AiRoleOf(faction, e.Role, e.Slot)] = true;
                    }
                }
                AiComposition composition = FactionData.AiCompositionOf(faction);
                for (int role = 1; role <= 3; role++)
                    Require(composition.Percent((AiRole)role) == 0 || roles[role], faction + " never fielded " + (AiRole)role);
            }
        }

        private static void Require(bool ok, string message)
        {
            if (!ok) throw new Exception("AI: " + message);
        }

        private static void CompositionTables()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                AiComposition c = FactionData.AiCompositionOf((Faction)f);
                Require(c.MeleePercent + c.RangedPercent + c.SupportPercent == 100, "composition shares must sum to 100");
                Require(c.SupportCap > 0 && c.AntiAirPercent > 0, "support cap and air response missing");
            }
            Require(World.AiRoleOf(Faction.Humans, Role.Melee, 0) == AiRole.None, "blank human melee is considered usable");
        }

        private static void AllFactionsPlayDeterministically()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                Faction faction = (Faction)f;
                World a = MatchScenario.Build(0xC0FFEE, faction, faction);
                World b = MatchScenario.Build(0xC0FFEE, faction, faction);
                for (int peer = 0; peer < 2; peer++) { a.SetPeerAi(peer, true); b.SetPeerAi(peer, true); }
                bool grew = false, arrived = false, attacked = false;
                for (int tick = 0; tick < 2000 && !a.MatchOver; tick++)
                {
                    a.Step(Idle); b.Step(Idle);
                    Require(a.Hash() == b.Hash(), faction + " diverged at " + tick);
                    foreach (Command cmd in a.LastTickCommands)
                    {
                        grew |= cmd.Type == CommandType.Grow;
                        attacked |= cmd.Type == CommandType.AttackMove || cmd.Type == CommandType.Attack;
                        if (cmd.Type == CommandType.Produce && !cmd.Target.Equals(FixVec2.Zero))
                            arrived |= a.GetEntity(cmd.EntityId).HasArrivalPoint;
                    }
                }
                Require(attacked, faction + " never attacked");
                if (faction == Faction.TreeSpirits) Require(grew, "tree spirits never grew a unit");
                if (faction == Faction.Driftworlds) Require(arrived, "driftworlds never queued a valid arrival");
            }
        }

        private static void VisibleAirPrioritizesAntiAir()
        {
            World w = Two(Faction.Hellfire, Faction.TreeSpirits);
            w.GrantResources(0, 10000);
            w.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            w.SpawnBuilding(0, Role.Production, At(8, 5), complete: true);
            w.SpawnBuilding(0, Role.Tech, At(11, 5), complete: true);
            w.SpawnBuilding(0, Role.Supply, At(14, 5), complete: true);
            w.SpawnBuilding(1, Role.Base, At(60, 60), complete: true);
            for (int i = 0; i < World.AiWorkerTarget; i++) w.SpawnWorker(0, At(5 + i, 9));
            for (int i = 0; i < World.AiSquadSize; i++) w.SpawnUnit(0, Role.Melee, 0, At(6 + i, 11));
            w.SpawnUnit(1, Role.Signature, 0, At(10, 12)); // flying wind spirit
            w.SetPeerAi(0, true);
            w.Step(Idle);
            bool ordered = false;
            foreach (Command cmd in w.LastTickCommands)
            {
                if (cmd.Type != CommandType.Produce) continue;
                Entity producer = w.GetEntity(cmd.EntityId);
                if (producer.QueueCount == 0 || producer.ProduceRole == Role.Worker) continue;
                // The base cannot produce anti-air; the production building can.
                // Its first order must answer the visible flier.
                if (producer.Role != Role.Production) continue;
                Require(FactionData.Stats(Faction.Hellfire, producer.ProduceRole, producer.ProduceSlot).HitsAir,
                    "an anti-air-capable producer ignored the visible air threat");
                ordered = true;
                break;
            }
            Require(ordered, "air response never queued a fighter");
        }

        private static void AreaCastsNeedACluster()
        {
            // Exercise the decision boundary for every cluster-targeted active,
            // with real research gates, cooldowns and command execution.
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                AbilityRow row = FactionData.AbilityRowAt(r);
                if (row.Kind != AbilityKind.StrikeZone && row.Kind != AbilityKind.DelayedStrike &&
                    row.Kind != AbilityKind.TideStrike && row.Kind != AbilityKind.SandZone) continue;
                for (int count = 2; count <= 3; count++)
                {
                    World w = Two(row.Faction, Faction.TreeSpirits);
                    if (row.Unlock >= 0) Research(w, 0, FactionData.Upgrade(row.Faction, row.Unlock).Effect);
                    w.SpawnBuilding(1, Role.Base, At(60, 60), complete: true);
                    int caster = w.SpawnUnit(0, row.Role, row.Slot, At(30, 30));
                    for (int n = 0; n < count; n++) w.SpawnUnit(1, Role.None, At(32, 30), hpOverride: 10000);
                    if (row.CastTarget == CastTarget.WaterFieldCell)
                        w.SetWaterFieldForScenario(Cell(32, 30), w.Tick + 100);
                    w.SetPeerAi(0, true);
                    bool cast = false;
                    for (int tick = 0; tick < World.AiThinkTicks; tick++)
                    {
                        w.Step(Idle);
                        foreach (Command cmd in w.LastTickCommands)
                            cast |= cmd.Type == CommandType.Cast && cmd.EntityId == caster;
                    }
                    Require(cast == (count == 3), row.Name + " cluster threshold " + count);
                    if (cast) Require(w.GetEntity(caster).AbilityCooldown > 0, row.Name + " cast was refused");
                }
            }
        }

        private static void SpecialActivesAreUsed()
        {
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                AbilityRow row = FactionData.AbilityRowAt(r);
                if (row.Kind != AbilityKind.LayField && row.Kind != AbilityKind.Harden &&
                    row.Kind != AbilityKind.Awaken && row.Kind != AbilityKind.Deploy) continue;
                World w = Two(row.Faction, Faction.TreeSpirits);
                if (row.Unlock >= 0) Research(w, 0, FactionData.Upgrade(row.Faction, row.Unlock).Effect);
                w.GrantResources(0, 1000);
                w.SpawnBuilding(0, Role.Base, At(3, 3), complete: true);
                w.SpawnBuilding(1, Role.Base, At(60, 60), complete: true);
                int caster = row.Role == Role.Worker ? w.SpawnWorker(0, At(30, 30)) :
                    w.SpawnUnit(0, row.Role, row.Slot, At(30, 30));
                if (row.Kind == AbilityKind.LayField) w.SpawnUnit(0, Role.Melee, At(31, 30));
                if (row.Kind == AbilityKind.Harden)
                {
                    w.SetHpForScenario(caster, 1);
                    w.SpawnUnit(1, Role.None, At(32, 30), hpOverride: 10000);
                }
                if (row.Kind == AbilityKind.Awaken) w.SetRemnantForScenario(Cell(32, 30), w.Tick + 100);
                if (row.Kind == AbilityKind.Deploy) w.SpawnUnit(1, Role.None, At(37, 30), hpOverride: 10000);
                w.SetPeerAi(0, true);
                bool cast = false;
                for (int tick = 0; tick < World.AiThinkTicks; tick++)
                {
                    w.Step(Idle);
                    foreach (Command cmd in w.LastTickCommands)
                        cast |= cmd.Type == CommandType.Cast && cmd.EntityId == caster;
                }
                Require(cast, row.Name + " never issued");
                Entity result = w.GetEntity(caster);
                Require(!result.Alive || result.AbilityCooldown > 0 ||
                    (row.Kind == AbilityKind.Deploy && (result.Deployed || result.AbilityTicksLeft > 0)),
                    row.Name + " was refused");
            }
        }
    }
}
