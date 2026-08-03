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

            // 2. Production. Every finished building of its own with an empty queue
            //    gets an order, so nothing that can build anything sits still.
            int workers = AiCountWorkers(peer);
            for (int i = 0; i < entities.Count; i++)
            {
                Entity b = entities[i];
                if (!b.Alive || b.Owner != peer || b.Kind != EntityKind.Building) continue;
                if (b.BuildTicksLeft > 0 || b.QueueCount > 0) continue;
                if (b.Role != Role.Base && b.Role != Role.Production) continue;

                Role want = workers < AiWorkerTarget ? Role.Worker : Role.Melee;
                AiIssue(peer, CommandType.Produce, i, FixVec2.Zero, (int)want);
                // Counted as made the moment it is queued, or two buildings both
                // order the last worker and the economy overshoots by a building.
                if (want == Role.Worker) workers++;
            }

            // 3. Supply before the cap, not after it. A cap reached is production
            //    stopped, and a supply building takes 50 ticks to stand up.
            if (population[peer] + AiSupplyLead >= PopulationCap(peer) &&
                PopulationCap(peer) < PopulationHardCap &&
                !AiHolds(peer, Role.Supply, unfinishedOnly: true))
            {
                AiBuild(peer, Role.Supply);
            }

            // 4. Somewhere else to build from, so losing the base is not losing the
            //    match on the spot.
            if (!AiHolds(peer, Role.Production, unfinishedOnly: false)) AiBuild(peer, Role.Production);

            // 5. Attack with what it has.
            AiAttack(peer);
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

        /// <summary>Alive, armed, this peer's, and under no order of its own.</summary>
        private static bool AiIsFreeFighter(int peer, Entity e) =>
            e.Alive && e.Owner == peer && e.Kind == EntityKind.Unit &&
            e.Role != Role.Worker && e.Speed.Raw != 0 && e.Mode == OrderMode.None;

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
        // workers behind a wall of supply buildings and it has no idea where a
        // choke or an expansion is. Give it real placement the day the map has
        // terrain worth reading.
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
                        AiIssue(peer, CommandType.Build, -1, at, (int)role);
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
