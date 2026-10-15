namespace WordCraft.Sim
{
    /// <summary>
    /// The simulated opponent. It runs inside Step like every other system, reads
    /// the same world every peer holds, and issues the same Commands a player
    /// issues. It never writes an entity, never spends a resource, and never
    /// touches anything outside the simulation.
    ///
    /// That placement is the whole point. Here it is covered by the replay harness
    /// and the hash comparison, and it drops into a networked match unchanged
    /// because every peer computes its input rather than receiving it. In the view
    /// it would be covered by neither.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// Ticks between decisions. A period on the tick counter rather than a
        /// countdown field: one less thing to hash, and "every tenth tick" is the
        /// same tick on every peer by construction.
        /// </summary>
        public const int AiThinkTicks = 10;

        /// <summary>Workers it builds before it starts building fighters.</summary>
        public const int AiWorkerTarget = 6;

        /// <summary>Fighters standing idle before it sends them anywhere.</summary>
        public const int AiSquadSize = 4;

        /// <summary>Headroom under the population cap at which it puts down supply.</summary>
        public const int AiSupplyLead = 3;

        /// <summary>How far out from its base it will look for a free cell to build on.</summary>
        private const int AiBuildRadius = 6;

        /// <summary>Which peers the simulation plays for. Hashed: it decides who issues commands.</summary>
        private readonly bool[] aiPeers = new bool[MaxPeers];

        /// <summary>
        /// Per-peer command sequence. Hashed, because canonical order inside a tick
        /// is PeerId then Seq: two peers that disagreed about this number would
        /// execute the same commands in a different order.
        /// </summary>
        private readonly int[] aiSeq = new int[MaxPeers];

        /// <summary>Scenario setup only. Never call this from a system.</summary>
        public void SetPeerAi(int peer, bool controlled) => aiPeers[peer] = controlled;

        public bool IsPeerAi(int peer) => aiPeers[peer];

        /// <summary>
        /// Runs before this tick's commands are sorted, so what it issues is ordered
        /// against a human peer's input by the same PeerId, Seq rule and takes the
        /// same path through Apply. Peers are walked in ascending id, never from a
        /// keyed collection.
        /// </summary>
        private void AiSystem()
        {
            if (Tick % AiThinkTicks != 0) return;
            for (int peer = 0; peer < MaxPeers; peer++)
            {
                if (aiPeers[peer]) AiThink(peer);
            }
        }

        /// <summary>
        /// What this peer needs most, in order. A flat ladder re-run from scratch
        /// every think: nothing is remembered between ticks, so nothing here is
        /// state and the only hashed field the opponent owns is its sequence
        /// number. Orders it cannot pay for are refused by the command layer,
        /// which is the same answer a player clicking too early gets.
        /// </summary>
        // ponytail: a fixed ladder with no memory. It cannot scout, react to a
        // rush, retreat, or defend its own base, and it re-decides everything from
        // scratch every ten ticks. Give it a stored plan the day it needs to
        // commit to something across ticks; that plan is hashed state on that day.
        private void AiThink(int peer)
        {
            // 1. Workers. An idle worker is the only mistake that compounds.
            for (int i = 0; i < entities.Count; i++)
            {
                Entity w = entities[i];
                if (!w.Alive || w.Owner != peer || w.Kind != EntityKind.Worker) continue;
                if (w.GatherNodeId >= 0 || w.CarryAmount > 0) continue;

                int node = FindNearestNode(w.Position);
                if (node >= 0) AiIssue(peer, CommandType.Gather, i, FixVec2.Zero, node);
            }

            // 2. Supply before the cap, not after it. A cap reached is production
            //    stopped, and a supply building takes 50 ticks to stand up. Ahead
            //    of research and production, and while one is due, none stands
            //    under construction and the bank is short of its price, the
            //    ladder saves rather than spends: since docs/TECH-TREE.md 묶음 3
            //    the base makes fighters as well as workers for five factions out
            //    of six, research and production spent the bank every think, and
            //    the 75 a supply building costs was never there.
            //
            //    Only while short of the price and with a worker to earn it. A
            //    bank that already holds the price is not saving for anything: if
            //    AiBuild found no cell for the building, holding production would
            //    freeze the peer for nothing. A peer with no worker earns nothing,
            //    and saving would freeze it for good. Decided afresh every think,
            //    so nothing here is state.
            int workers = AiCountWorkers(peer);
            bool supplyDue = population[peer] + AiSupplyLead >= PopulationCap(peer) &&
                             PopulationCap(peer) < PopulationHardCap &&
                             !AiHolds(peer, Role.Supply, unfinishedOnly: true);
            if (supplyDue) AiBuild(peer, Role.Supply);
            bool saving = supplyDue && workers > 0 &&
                          resources[peer] < FactionData.BuildCost(factions[peer], Role.Supply, 0);

            // 3. Research, ahead of production. The ladder spends every coin it
            //    has on bodies every think, so research asked after production
            //    would only ever see an empty bank. Its orders are applied in the
            //    order they were issued, so the price is taken first and the
            //    production orders behind it are refused for what is left.
            if (!saving) AiResearch(peer);

            // 4. Production. Every finished building of its own with an empty queue
            //    gets an order, so nothing that can build anything sits still.
            //    What it may order there is the roster's answer: a building makes
            //    what is on its list and nothing else, so workers come out of the
            //    building that makes workers and fighters out of the ones that
            //    make fighters.
            for (int i = 0; i < entities.Count && !saving; i++)
            {
                Entity b = entities[i];
                if (!b.Alive || b.Owner != peer || b.Kind != EntityKind.Building) continue;
                if (b.BuildTicksLeft > 0 || b.QueueCount > 0) continue;

                if (workers < AiWorkerTarget)
                {
                    // Workers first, the way it always was: until the target is
                    // met a building that cannot make one waits rather than
                    // spending the bank the next worker needs.
                    if (!CanProduce(peer, i, Role.Worker, 0)) continue;
                    AiIssue(peer, CommandType.Produce, i, FixVec2.Zero, Command.RosterArg(Role.Worker, 0));
                    // Counted as made the moment it is queued, or two buildings both
                    // order the last worker and the economy overshoots by a building.
                    workers++;
                    continue;
                }

                if (!AiFighterAt(peer, i, out Role role, out int slot)) continue; // nothing here it can make
                AiIssue(peer, CommandType.Produce, i, FixVec2.Zero, Command.RosterArg(role, slot));
            }

            // 5. Somewhere else to build from, so losing the base is not losing the
            //    match on the spot.
            if (!AiHolds(peer, Role.Production, unfinishedOnly: false)) AiBuild(peer, Role.Production);

            // 6. A 기술 건물, once there is a finished production building to open
            //    tier 2 for it. CanBuild is what says so, and it also says no
            //    until the bank holds the price, so this costs nothing to ask.
            if (!AiHolds(peer, Role.Tech, unfinishedOnly: false)) AiBuild(peer, Role.Tech);

            // 7. Attack with what it has.
            AiAttack(peer);
        }

        /// <summary>
        /// Every finished 기술 건물 of its own that is not already researching
        /// starts the lowest-index row it can start and afford, where afford
        /// means the bank holds more than that level costs. Index order is the
        /// whole of the plan: 공격력, then 방어력, then the two rows that are its
        /// faction's own, then the docs/TECH-TREE.md 묶음 3 rows. Most of those
        /// are researched in a second or third research building (Tech 1 and
        /// after) that the ladder never builds, so CanResearch refuses them and
        /// the loop moves on. A row it cannot pay for yet is skipped, not waited on:
        /// with 75 banked it buys 방어력 level 1 for 50 rather than hold out for
        /// 공격력 level 2 at 100, so the levels climb unevenly.
        ///
        /// Strictly more than the price, not equal to it, so the order never
        /// empties the bank to the last coin.
        /// </summary>
        // ponytail: index order and nothing else. It researches 방어력 against an
        // opponent with no weapons and 도착 반경 with no 굴절 기둥 standing. Give
        // it a reason to pick a row the day the ladder is meant to be a plan.
        private void AiResearch(int peer)
        {
            int rows = FactionData.UpgradeCount(factions[peer]);
            for (int i = 0; i < entities.Count; i++)
            {
                Entity b = entities[i];
                if (!b.Alive || b.Owner != peer || b.Kind != EntityKind.Building) continue;
                if (b.Role != Role.Tech || b.BuildTicksLeft > 0 || b.ResearchIndex >= 0) continue;

                for (int row = 0; row < rows; row++)
                {
                    if (!CanResearch(peer, i, row) || resources[peer] <= ResearchCost(peer, row)) continue;
                    AiIssue(peer, CommandType.Research, i, FixVec2.Zero, row);
                    // One building, one order per think. A second 기술 건물 asked
                    // in the same think would see the same bank and the same free
                    // row, and Apply would refuse whichever came second.
                    return;
                }
            }
        }

        /// <summary>
        /// The combat roles the ladder looks through, in the order it prefers them
        /// when two entries open at the same tier. Melee first, which is what it
        /// always asked for. Static data rather than state: never written, walked
        /// by index, and the same list on every peer.
        /// </summary>
        private static readonly Role[] AiFighterRoles = { Role.Melee, Role.Ranged, Role.Signature };

        /// <summary>
        /// The fighter this building should make: of every entry of the three
        /// combat roles that this building can make right now and the bank can
        /// pay for, the one at the highest tier. Ties go to the first in
        /// AiFighterRoles order and then the lowest entry. False when there is
        /// none, which the caller reads as nothing to order here.
        ///
        /// CanProduce is the test, the same gate TryQueueUnit takes, so the ladder
        /// cannot come to a different opinion about what this building makes than
        /// the command layer that answers it. That is what keeps it off 인간's
        /// blank melee row and off 지옥불 자손, which no building makes, and what
        /// sends it to build the 기술 건물 before it can ask for anything behind
        /// one: step 6 below builds it, and the think after it stands, this asks
        /// for the tier-3 entry it opened.
        ///
        /// The bank is asked here, unlike anywhere else on the ladder, because the
        /// highest tier is also where the most expensive bodies are. 인간's 공수
        /// 특공대 is 65 and a bank short of that cannot pay for it: asking for it
        /// regardless would be refused every think, and the production building
        /// would sit idle until the bank caught up.
        ///
        /// An entry whose row carries no weapon is never taken, and that is
        /// asked before the bank. AiIsFreeFighter never sends an unarmed body, so
        /// a ladder that bought one at the highest tier spent the rest of the
        /// match on bodies that never attack: 인간 at T3 bought seven 마도 정찰기
        /// in an arena game against 세계수 정령 and sent none of them. The same
        /// rule keeps it off 지옥불 군단장 and 경계 운반자.
        /// </summary>
        // ponytail: still a fixed preference with no memory. It wants the highest
        // tier it can afford and never weighs one body against what it is walking
        // into, because there is nothing here that looks at the opponent at all.
        // Give it a reason to prefer one the day the ladder is meant to be a plan.
        // ponytail: 지옥불 군단장 and 경계 운반자 are skipped for carrying no
        // weapon, although the 자손 one spawns and the arrival radius the other
        // lends would matter to a match. Teach the ladder what an unarmed body is
        // worth in 묶음 9 of docs/TECH-TREE.md.
        private bool AiFighterAt(int peer, int buildingId, out Role role, out int slot)
        {
            Faction faction = factions[peer];
            role = Role.None;
            slot = 0;
            int bestTier = 0;
            for (int r = 0; r < AiFighterRoles.Length; r++)
            {
                Role candidate = AiFighterRoles[r];
                int count = FactionData.SlotCount(faction, candidate);
                for (int s = 0; s < count; s++)
                {
                    if (!CanProduce(peer, buildingId, candidate, s)) continue;
                    if (!FactionData.Stats(faction, candidate, s).HasWeapon) continue;
                    if (FactionData.Production(faction, candidate, s).Resources > resources[peer]) continue;
                    int tier = FactionData.Tier(faction, candidate, s);
                    // Strictly higher, so the first entry at a tier keeps it.
                    if (tier <= bestTier) continue;
                    bestTier = tier;
                    role = candidate;
                    slot = s;
                }
            }
            return role != Role.None;
        }

        /// <summary>
        /// Sends every free fighter at the enemy once enough of them are standing
        /// around. Counted first and ordered second: half a squad sent is a squad
        /// that dies twice.
        /// </summary>
        // ponytail: one wave, one target, no regrouping and no retreat. Everything
        // walks at the enemy base and whatever it meets on the way is the fight.
        // Split it into groups with their own targets the day one wave stops being
        // the whole plan.
        private void AiAttack(int peer)
        {
            int target = AiEnemyTarget(peer);
            if (target < 0) return;

            int ready = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                if (AiIsFreeFighter(peer, entities[i])) ready++;
            }
            if (ready < AiSquadSize) return;

            FixVec2 where = entities[target].Position;
            for (int i = 0; i < entities.Count; i++)
            {
                if (AiIsFreeFighter(peer, entities[i])) AiIssue(peer, CommandType.AttackMove, i, where);
            }
        }

        /// <summary>
        /// Alive, armed, this peer's, and under no order of its own. Armed is asked
        /// of the roster rather than assumed from the kind: an unarmed body counted
        /// here would pad the squad out to AiSquadSize with something that cannot
        /// fight, and then be sent an AttackMove the command layer refuses.
        /// </summary>
        private bool AiIsFreeFighter(int peer, Entity e) =>
            e.Alive && e.Owner == peer && e.Kind == EntityKind.Unit &&
            e.Role != Role.Worker && e.Speed.Raw != 0 && e.Mode == OrderMode.None &&
            Armed(e);

        /// <summary>
        /// What the squad walks at: the enemy base first, any other enemy building
        /// after it, and failing both whatever is left standing. Lowest id inside
        /// each rank, never a keyed lookup, so the choice cannot depend on
        /// enumeration order.
        /// </summary>
        private int AiEnemyTarget(int peer)
        {
            int baseId = -1, building = -1, anything = -1;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner < 0 || e.Owner == peer) continue;
                if (e.Kind == EntityKind.ResourceNode) continue;

                if (e.Kind == EntityKind.Building)
                {
                    if (e.Role == Role.Base && baseId < 0) baseId = i;
                    else if (building < 0) building = i;
                }
                else if (anything < 0)
                {
                    anything = i;
                }
            }
            return baseId >= 0 ? baseId : building >= 0 ? building : anything;
        }

        /// <summary>Where this peer builds from: its lowest-id base, or any building it still holds.</summary>
        private int AiHome(int peer)
        {
            int fallback = -1;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner != peer || e.Kind != EntityKind.Building) continue;
                if (e.Role == Role.Base) return i;
                if (fallback < 0) fallback = i;
            }
            return fallback;
        }

        private bool AiHolds(int peer, Role role, bool unfinishedOnly)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner != peer || e.Kind != EntityKind.Building) continue;
                if (e.Role != role) continue;
                if (unfinishedOnly && e.BuildTicksLeft == 0) continue;
                return true;
            }
            return false;
        }

        private int AiCountWorkers(int peer)
        {
            int n = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (e.Alive && e.Owner == peer && e.Kind == EntityKind.Worker) n++;
            }
            return n;
        }

        /// <summary>
        /// Rings outward from the base, taking the first cell CanBuild accepts.
        /// The scan order is fixed and the test is the simulation's own, so every
        /// peer running this arrives at the same cell with no tie-break of its own.
        /// </summary>
        // ponytail: rings around the base and nothing else. It can box its own
        // workers behind a wall of supply buildings, and now that the map has
        // terrain it walks past chokes it cannot see. CanBuild refuses the cells it
        // may not have, so the ring is safe, merely blind. Give it real placement
        // the day where it builds is supposed to be a decision.
        private void AiBuild(int peer, Role role)
        {
            int home = AiHome(peer);
            if (home < 0) return;

            FixVec2 from = entities[home].Position;
            for (int r = 2; r <= AiBuildRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Abs(dx) != r && Abs(dy) != r) continue; // the ring, not the disc

                        var at = new FixVec2(from.X + Fix.FromInt(dx), from.Y + Fix.FromInt(dy));
                        if (!CanBuild(peer, role, at)) continue;
                        // ponytail: entry 0 of whatever it wants. It cannot see that
                        // 인간 fields three defense buildings. Give it a choice of
                        // entry the day the ladder is meant to be a plan rather than
                        // a fallback.
                        AiIssue(peer, CommandType.Build, -1, at, Command.RosterArg(role, 0));
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Appends to this tick's command list, which is sorted and applied after
        /// the AI has finished thinking. Nothing here reaches past the command
        /// layer: an order this peer cannot afford is refused exactly as a
        /// player's would be.
        /// </summary>
        private void AiIssue(int peer, CommandType type, int entityId, FixVec2 target, int arg = 0)
        {
            tickCommands.Add(new Command(Tick, peer, aiSeq[peer]++, type, entityId, target, arg));
        }

        private static int Abs(int v) => v < 0 ? -v : v;
    }
}
