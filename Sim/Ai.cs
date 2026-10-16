using System.Collections.Generic;

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

        /// <summary>
        /// Workers past AiWorkerTarget a faction that grows fighters out of its
        /// workers keeps coming, docs/TECH-TREE.md 묶음 9 성장: 세계수 정령's base
        /// goes on making 풀씨 정령 up to the target plus this, and every worker
        /// over the target is grown. Small, because a spare worker still gathers
        /// while the bank is short of the growth it is waiting for.
        /// </summary>
        public const int AiSeedWorkers = 2;

        /// <summary>Fighters standing idle before it sends them anywhere.</summary>
        public const int AiSquadSize = 4;

        /// <summary>Headroom under the population cap at which it puts down supply.</summary>
        public const int AiSupplyLead = 3;

        /// <summary>How many hostile bodies an area active has to catch before it is cast.</summary>
        public const int AiCastMinTargets = 3;

        /// <summary>굳기 is cast by a body at or under 1/this of its hp, with a hostile this close.</summary>
        public const int AiHardenHpDivisor = 3;

        public static readonly Fix AiHardenReach = Fix.FromInt(3);

        /// <summary>
        /// The longest reach that still counts as melee for the composition
        /// table. Every melee row reaches 2 and every ranged row 3 or more.
        /// </summary>
        public static readonly Fix AiMeleeReach = Fix.FromInt(2);

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
        /// The commands the last Step applied, in canonical order, the simulated
        /// peers' included. Read-only and for the harness: it is the scratch list
        /// Step sorts every tick, so nothing in it outlives the next Step and none
        /// of it is state. Once the match is over Step stops refilling it, and it
        /// keeps the last tick that ran.
        /// </summary>
        public IReadOnlyList<Command> LastTickCommands => tickCommands;

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
        /// What one peer's army is right now, counted fresh every think: bodies
        /// per composition role, the ones that can hit air, whether an air enemy
        /// is in sight, and whether a wave is out. A local value, never stored,
        /// so it is not state.
        /// </summary>
        private struct AiArmy
        {
            public int Melee;
            public int Ranged;
            public int Support;
            public int AntiAir;
            public bool AirThreat;
            public bool WaveOut;

            public int Total => Melee + Ranged + Support;

            public int Count(AiRole role) =>
                role == AiRole.Melee ? Melee : role == AiRole.Ranged ? Ranged : role == AiRole.Support ? Support : 0;

            public void Add(AiRole role)
            {
                if (role == AiRole.Melee) Melee++;
                else if (role == AiRole.Ranged) Ranged++;
                else if (role == AiRole.Support) Support++;
            }
        }

        /// <summary>
        /// What this peer needs most, in order. A flat ladder re-run from scratch
        /// every think: nothing is remembered between ticks, so nothing here is
        /// state and the only hashed field the opponent owns is its sequence
        /// number. Orders it cannot pay for are refused by the command layer,
        /// which is the same answer a player clicking too early gets.
        ///
        /// The bank is tracked as a local through one think: every order that
        /// costs takes its price off it, so a later step sees what an earlier one
        /// left rather than the bank the think started with, and orders issued
        /// in one think are applied in the order they were issued.
        /// </summary>
        // ponytail: a ladder with no memory. It cannot scout, react to a rush,
        // retreat, or defend its own base, and it re-decides everything from
        // scratch every ten ticks. Give it a stored plan the day it needs to
        // commit to something across ticks; that plan is hashed state on that day.
        private void AiThink(int peer)
        {
            // 1. Workers. An idle worker is the only mistake that compounds.
            for (int i = 0; i < entities.Count; i++)
            {
                Entity w = entities[i];
                if (!w.Alive || w.Owner != peer || w.Kind != EntityKind.Worker) continue;
                if (w.GatherNodeId >= 0 || w.CarryAmount > 0 || IsGrowing(w)) continue;

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
            if (supplyDue) AiBuild(peer, Role.Supply, 0);
            bool saving = supplyDue && workers > 0 &&
                          resources[peer] < FactionData.BuildCost(factions[peer], Role.Supply, 0);
            int bank = resources[peer];
            AiArmy army = AiCountArmy(peer);

            // 3. Actives, docs/TECH-TREE.md 묶음 9 액티브 능력. First, because a
            //    cast is about a fight happening now; all but 깨우기 cost nothing.
            AiCast(peer, saving, ref bank);

            // 4. The next building: the production building, the 기술 건물, an
            //    arrival anchor, then whatever the composition's next entry is
            //    waiting for. Placed when the bank holds its price; otherwise its
            //    price is held back from everything below, so the bank can grow
            //    to it. Every step below spends what is left.
            int spend = bank;
            int reserved = saving ? 0 : AiPlaceBuilding(peer, army, ref spend);

            // 5. The body the army is shortest of: its price is held back from
            //    research and growth, so those spend only what is spare.
            int unitPrice = 0;
            int first = saving ? -1 : AiShortRoleBuilding(peer, army, out unitPrice);
            spend -= unitPrice;

            // 6. Research with what is spare, docs/TECH-TREE.md 묶음 9 연구.
            if (!saving) AiResearch(peer, ref spend);

            // 7. Growth: spare workers and free fighters grown into what the
            //    composition is short of. Only 세계수 정령 has a growth table.
            if (!saving) AiGrow(peer, workers, ref army, ref spend);

            // 8. Production. Every finished building of its own with an empty queue
            //    gets an order, so nothing that can build anything sits still.
            spend += unitPrice;
            if (!saving) AiProduce(peer, workers, reserved, first, ref army, ref spend);

            // 9. Attack with what it has.
            AiAttack(peer);
        }

        // ---------------------------------------------------------------------
        // Composition

        /// <summary>
        /// What this roster entry is to the opponent's composition table, by rule
        /// rather than by a second table, so a row added to the roster is sorted
        /// the day it lands. Only bodies a building or a growth can make count:
        ///
        /// - A body that does not walk is never wanted: the ladder sends its army
        ///   at the enemy, and 거대 덩쿨 roots where it grew.
        /// - An entry holding an aimed active (칼바람 회오리, 유성우, 해일 부름,
        ///   간헐천, 깨우기, 모래폭풍, 낙뢰) is support, armed or not.
        /// - An unarmed entry is support when it gives the army something it can
        ///   use on the move: a heal, 표식, 덩굴 지대, offspring, or an arrival
        ///   radius. Any other unarmed entry is None and never bought.
        /// - Otherwise melee at a reach of 2 or less, ranged past it.
        /// </summary>
        // ponytail: 도깨비불's 과생장, 층위 관측자's 층위 고정, 공방 기술자's 수리
        // and 샘물 운반자's water field help only bodies that stand beside them,
        // so the ladder never buys them. Give it a reason to the day it groups an
        // army around something.
        public static AiRole AiRoleOf(Faction faction, Role role, int slot)
        {
            if (role != Role.Melee && role != Role.Ranged && role != Role.Signature) return AiRole.None;
            if (!FactionData.Has(faction, role, slot)) return AiRole.None;
            UnitStats s = FactionData.Stats(faction, role, slot);
            if (s.Speed.Raw == 0) return AiRole.None;
            if (HoldsAimedActive(faction, role, slot)) return AiRole.Support;
            if (!s.HasWeapon) return SupportWorth(faction, role, slot) ? AiRole.Support : AiRole.None;
            return s.Range <= AiMeleeReach ? AiRole.Melee : AiRole.Ranged;
        }

        private static bool HoldsAimedActive(Faction faction, Role role, int slot)
        {
            int rows = FactionData.AbilityCount(faction);
            for (int i = 0; i < rows; i++)
            {
                AbilityRow row = FactionData.Ability(faction, i);
                if (row.Role != role || row.Slot != slot || !FactionData.IsCast(row.Kind)) continue;
                if (row.CastTarget != CastTarget.NoTarget) return true;
            }
            return false;
        }

        private static bool SupportWorth(Faction faction, Role role, int slot)
        {
            if (FactionData.Spawner(faction, role, slot).Spawns) return true;
            if (FactionData.Arrival(faction, role, slot).Radius > 0) return true;
            int rows = FactionData.AbilityCount(faction);
            for (int i = 0; i < rows; i++)
            {
                AbilityRow row = FactionData.Ability(faction, i);
                if (row.Role != role || row.Slot != slot) continue;
                if (row.Kind == AbilityKind.HealAura || row.Kind == AbilityKind.MarkAura ||
                    row.Kind == AbilityKind.SlowAura)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Counts the army: every living unit of its own by the entry it is, every
        /// growing body by the entry it is growing into, and every queued body by
        /// the entry on the queue, so an order given last think is not given
        /// twice. Air threat is an enemy flier within AcquireRange of anything it
        /// owns, docs/TECH-TREE.md 묶음 9 대공.
        /// </summary>
        private AiArmy AiCountArmy(int peer)
        {
            var army = new AiArmy();
            Faction faction = factions[peer];
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner != peer) continue;
                if (e.Kind == EntityKind.Building)
                {
                    if (e.QueueCount <= 0) continue;
                    AiRole queued = AiRoleOf(faction, e.ProduceRole, e.ProduceSlot);
                    for (int q = 0; q < e.QueueCount; q++) army.Add(queued);
                    if (queued != AiRole.None && HitsAir(faction, e.ProduceRole, e.ProduceSlot))
                        army.AntiAir += e.QueueCount;
                    continue;
                }
                if (e.Kind != EntityKind.Unit && !(e.Kind == EntityKind.Worker && IsGrowing(e))) continue;

                Role role = IsGrowing(e) ? e.MorphRole : e.Role;
                int slot = IsGrowing(e) ? e.MorphSlot : e.Slot;
                AiRole ai = AiRoleOf(faction, role, slot);
                army.Add(ai);
                if (ai != AiRole.None && HitsAir(faction, role, slot)) army.AntiAir++;
                if (e.Mode == OrderMode.AttackMove && Armed(e)) army.WaveOut = true;
            }
            army.AirThreat = AiSeesAir(peer);
            return army;
        }

        private static bool HitsAir(Faction faction, Role role, int slot)
        {
            UnitStats s = FactionData.Stats(faction, role, slot);
            return s.HasWeapon && s.HitsAir;
        }

        private bool AiSeesAir(int peer)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity flier = entities[i];
                if (!flier.Alive || flier.Kind != EntityKind.Unit || !AiHostile(peer, flier) || !Flies(flier)) continue;
                for (int j = 0; j < entities.Count; j++)
                {
                    Entity own = entities[j];
                    if (!own.Alive || own.Owner != peer || own.Kind == EntityKind.ResourceNode) continue;
                    if (WithinRange(own.Position, flier.Position, AcquireRange)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The composition role ranked rank-th by how far the army is short of
        /// it: the target share of one more body than it has, less what it has,
        /// both in hundredths. Ties go to melee, then ranged, then support. A
        /// role with no share, or support at its cap, is never ranked first; it
        /// answers None in its place.
        /// </summary>
        private static AiRole AiShortRole(AiComposition c, AiArmy army, int rank)
        {
            int melee = AiShortfall(c, army, AiRole.Melee);
            int ranged = AiShortfall(c, army, AiRole.Ranged);
            int support = AiShortfall(c, army, AiRole.Support);
            // Three values, so the sort is written out: a rank is the number of
            // roles strictly ahead of it, with the tie order breaking equals.
            for (int r = 1; r <= 3; r++)
            {
                var role = (AiRole)r;
                int mine = role == AiRole.Melee ? melee : role == AiRole.Ranged ? ranged : support;
                int ahead = 0;
                for (int o = 1; o <= 3; o++)
                {
                    if (o == r) continue;
                    int theirs = o == 1 ? melee : o == 2 ? ranged : support;
                    if (theirs > mine || (theirs == mine && o < r)) ahead++;
                }
                if (ahead == rank) return mine == int.MinValue ? AiRole.None : role;
            }
            return AiRole.None;
        }

        private static int AiShortfall(AiComposition c, AiArmy army, AiRole role)
        {
            int percent = c.Percent(role);
            if (percent <= 0) return int.MinValue;
            if (role == AiRole.Support && army.Support >= c.SupportCap) return int.MinValue;
            return percent * (army.Total + 1) - army.Count(role) * 100;
        }

        /// <summary>
        /// Whether it should buy bodies that hit air before anything else: an air
        /// enemy is in sight and fewer than the table's share of its bodies can
        /// reach one. With none owned that is always true, which is the
        /// docs/TECH-TREE.md 묶음 9 대공 rule.
        /// </summary>
        private static bool AiWantsAntiAir(AiComposition c, AiArmy army) =>
            army.AirThreat && army.AntiAir * 100 < c.AntiAirPercent * (army.Total + 1);

        /// <summary>
        /// Whether the opponent would take this entry for this role at all, before
        /// any question of whether it can be made now. With antiAir set the role
        /// is ignored and the entry has to carry a weapon that reaches air;
        /// otherwise an armed entry has to reach the ground, which keeps it off
        /// 재날개 악마 until something flies.
        /// </summary>
        private static bool AiTakes(Faction faction, Role role, int slot, AiRole want, bool antiAir)
        {
            AiRole ai = AiRoleOf(faction, role, slot);
            if (ai == AiRole.None) return false;
            UnitStats s = FactionData.Stats(faction, role, slot);
            if (antiAir) return s.HasWeapon && s.HitsAir;
            if (ai != want) return false;
            return !s.HasWeapon || s.HitsGround;
        }

        /// <summary>
        /// Whether candidate beats best for this role: among support the entry
        /// holding an aimed active first; then the higher tier; then, at one
        /// tier, the entry holding an active (성벽 골렘 over 구르는 바위), so the
        /// actives of docs/TECH-TREE.md 묶음 9 have bodies to cast them. Strictly
        /// better, so the first entry walked keeps a tie.
        /// </summary>
        private static bool AiBetter(Faction faction, AiRole want, Role role, int slot, Role bestRole, int bestSlot)
        {
            if (bestRole == Role.None) return true;
            if (want == AiRole.Support)
            {
                bool caster = HoldsAimedActive(faction, role, slot);
                bool bestCaster = HoldsAimedActive(faction, bestRole, bestSlot);
                if (caster != bestCaster) return caster;
            }
            int tier = FactionData.Tier(faction, role, slot), bestTier = FactionData.Tier(faction, bestRole, bestSlot);
            if (tier != bestTier) return tier > bestTier;
            return HoldsActive(faction, role, slot) && !HoldsActive(faction, bestRole, bestSlot);
        }

        private static bool HoldsActive(Faction faction, Role role, int slot)
        {
            int rows = FactionData.AbilityCount(faction);
            for (int i = 0; i < rows; i++)
            {
                AbilityRow row = FactionData.Ability(faction, i);
                if (row.Role == role && row.Slot == slot && FactionData.IsCast(row.Kind)) return true;
            }
            return false;
        }

        /// <summary>The roster roles a body can be, walked in this order.</summary>
        private static readonly Role[] AiBodyRoles = { Role.Melee, Role.Ranged, Role.Signature };

        // ---------------------------------------------------------------------
        // Production

        /// <summary>
        /// Every finished building of its own with an empty queue gets an order.
        /// Workers first, the way it always was. Then, if it wants bodies that hit
        /// air, the best of those this building makes; otherwise the best entry of
        /// the role the army is shortest of, then the next role, and so on.
        ///
        /// CanProduce is the test, the same gate TryQueueUnit takes, so the ladder
        /// cannot come to a different opinion about what this building makes than
        /// the command layer that answers it. That keeps it off 인간's blank melee
        /// row and off 지옥불 자손, which no building makes.
        ///
        /// 차원 유랑종's 통로 is given an arrival point with the order
        /// (AiArrivalPoint), and its 멸종 슬라임 are only bought while a wave is
        /// out (docs/TECH-TREE.md 묶음 9 한시 소환): they lapse 600 ticks after
        /// they arrive, and a body that waited at home for a squad would lapse
        /// before it met anything.
        /// </summary>
        private void AiProduce(int peer, int workers, int reserved, int first, ref AiArmy army, ref int bank)
        {
            Faction faction = factions[peer];
            bool grows = AiGrowsWorkers(faction);
            // The building the short role waits on is asked first, so a cheaper
            // body somewhere else cannot spend its price.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    if ((pass == 0) != (i == first)) continue;
                    Entity b = entities[i];
                    if (!b.Alive || b.Owner != peer || b.Kind != EntityKind.Building) continue;
                    if (b.BuildTicksLeft > 0 || b.QueueCount > 0) continue;

                    bool seed = grows && b.Role == Role.Base && workers < AiWorkerTarget + AiSeedWorkers;
                    if (workers < AiWorkerTarget || seed)
                    {
                        // Until the target is met a building that cannot make a
                        // worker waits rather than spending the bank the next worker
                        // needs. Past it, only a growing faction's base goes on.
                        if (!CanProduce(peer, i, Role.Worker, 0))
                        {
                            if (workers < AiWorkerTarget) continue;
                        }
                        else
                        {
                            // A worker under the target may spend what a building is
                            // being saved for: the economy is what pays for that
                            // building. A seed worker past it may not.
                            int price = FactionData.Production(faction, Role.Worker, 0).Resources;
                            if (price > (workers < AiWorkerTarget ? bank + reserved : bank)) continue;
                            AiIssue(peer, CommandType.Produce, i, FixVec2.Zero, Command.RosterArg(Role.Worker, 0));
                            bank -= price;
                            // Counted as made the moment it is queued, or two buildings
                            // both order the last worker and the economy overshoots.
                            workers++;
                            continue;
                        }
                    }

                    AiOrderFighter(peer, i, i == first, ref army, ref bank);
                }
            }
        }

        /// <summary>
        /// One building's fighter order. If it wants bodies that hit air, the best
        /// of those this building makes; otherwise the best entry of the role the
        /// army is shortest of, then the next role, and so on. With hold set this
        /// is the building the short role waits on: it asks only for that role,
        /// and when the bank cannot pay yet it keeps the price back rather than
        /// buying something cheaper.
        /// </summary>
        private void AiOrderFighter(int peer, int buildingId, bool hold, ref AiArmy army, ref int bank)
        {
            Faction faction = factions[peer];
            AiComposition c = FactionData.AiCompositionOf(faction);
            Role role = Role.None;
            int slot = 0;
            AiRole picked = AiRole.None;
            if (AiWantsAntiAir(c, army) && AiEntryAt(peer, buildingId, AiRole.None, true, army, bank, out role, out slot))
            {
                picked = AiRoleOf(faction, role, slot);
            }
            else if (hold)
            {
                picked = AiShortRole(c, army, 0);
                if (!AiEntryAt(peer, buildingId, picked, false, army, int.MaxValue, out role, out slot)) return;
                int price = FactionData.Production(faction, role, slot).Resources;
                if (price > bank)
                {
                    bank -= price;
                    return;
                }
            }
            else
            {
                for (int rank = 0; rank < 3 && role == Role.None; rank++)
                {
                    AiRole want = AiShortRole(c, army, rank);
                    if (want == AiRole.None) continue;
                    if (AiEntryAt(peer, buildingId, want, false, army, bank, out role, out slot)) picked = want;
                }
            }
            if (role == Role.None) return; // nothing here it can make

            FixVec2 aim = IsPassage(entities[buildingId])
                ? AiArrivalPoint(peer, buildingId, FactionData.Stats(faction, role, slot).Air)
                : FixVec2.Zero;
            AiIssue(peer, CommandType.Produce, buildingId, aim, Command.RosterArg(role, slot));
            bank -= FactionData.Production(faction, role, slot).Resources;
            army.Add(picked);
            if (HitsAir(faction, role, slot)) army.AntiAir++;
        }

        /// <summary>
        /// The idle building that can make the best entry of the role the army is
        /// shortest of, and that entry's price, or -1 when no idle building can
        /// make one. The caller holds the price back from research and growth,
        /// and production asks this building first. Without it every cheaper
        /// body and every research level would spend the bank first, and a 220
        /// 지옥불 군단장 would never be bought.
        /// </summary>
        private int AiShortRoleBuilding(int peer, AiArmy army, out int price)
        {
            price = 0;
            Faction faction = factions[peer];
            AiComposition c = FactionData.AiCompositionOf(faction);
            if (AiWantsAntiAir(c, army)) return -1;
            AiRole want = AiShortRole(c, army, 0);
            if (want == AiRole.None) return -1;
            int bestBuilding = -1;
            Role bestRole = Role.None;
            int bestSlot = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity b = entities[i];
                if (!b.Alive || b.Owner != peer || b.Kind != EntityKind.Building) continue;
                if (b.BuildTicksLeft > 0 || b.QueueCount > 0) continue;
                if (!AiEntryAt(peer, i, want, false, army, int.MaxValue, out Role role, out int slot)) continue;
                if (!AiBetter(faction, want, role, slot, bestRole, bestSlot)) continue;
                bestBuilding = i;
                bestRole = role;
                bestSlot = slot;
            }
            if (bestBuilding >= 0) price = FactionData.Production(faction, bestRole, bestSlot).Resources;
            return bestBuilding;
        }

        /// <summary>
        /// The entry this building should make for this role: of every entry the
        /// opponent takes for it (AiTakes) that this building can make now and the
        /// bank can pay for, the best by AiBetter. False when there is none.
        /// </summary>
        private bool AiEntryAt(int peer, int buildingId, AiRole want, bool antiAir, AiArmy army, int bank,
            out Role role, out int slot)
        {
            Faction faction = factions[peer];
            role = Role.None;
            slot = 0;
            for (int r = 0; r < AiBodyRoles.Length; r++)
            {
                Role candidate = AiBodyRoles[r];
                int count = FactionData.SlotCount(faction, candidate);
                for (int s = 0; s < count; s++)
                {
                    if (!AiTakes(faction, candidate, s, want, antiAir)) continue;
                    if (!CanProduce(peer, buildingId, candidate, s)) continue;
                    if (FactionData.Production(faction, candidate, s).Resources > bank) continue;
                    if (FactionData.Stats(faction, candidate, s).Lifetime > 0 && !army.WaveOut) continue;
                    AiRole judged = antiAir ? AiRoleOf(faction, candidate, s) : want;
                    if (!AiBetter(faction, judged, candidate, s, role, slot)) continue;
                    role = candidate;
                    slot = s;
                }
            }
            return role != Role.None;
        }

        /// <summary>
        /// Where a body this 통로 makes should stand up, docs/TECH-TREE.md 묶음 9
        /// 도착 지점: of the anchors lending this peer a radius, the one nearest
        /// the enemy building nearest this 통로, and on it the point toward that
        /// building one cell inside the radius, walked back toward the anchor a
        /// cell at a time until ArrivalValid takes it. With no anchor, or no
        /// point any anchor covers, the 통로 자리: the order then keeps no point
        /// and the body comes out where every building's do.
        /// </summary>
        private FixVec2 AiArrivalPoint(int peer, int passageId, bool air)
        {
            FixVec2 passage = entities[passageId].Position + RallyOffset;
            int enemy = AiNearestEnemyBuilding(peer, entities[passageId].Position);
            if (enemy < 0) return passage;
            FixVec2 goal = entities[enemy].Position;

            int anchor = -1;
            Fix best = Fix.Zero;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!ProvidesArrival(entities[i], peer)) continue;
                Fix d = (entities[i].Position - goal).SqrMagnitude;
                if (anchor < 0 || d < best) { anchor = i; best = d; }
            }
            if (anchor < 0) return passage;

            FixVec2 from = entities[anchor].Position;
            FixVec2 toward = (goal - from).Normalized();
            int reach = ArrivalRadiusOf(entities[anchor]).ToInt() - 1;
            for (int step = reach; step >= 0; step--)
            {
                FixVec2 point = CellCenter(CellOf(from + toward * Fix.FromInt(step)));
                if (ArrivalValid(peer, point, air)) return point;
            }
            return passage;
        }

        private int AiNearestEnemyBuilding(int peer, FixVec2 from)
        {
            int best = -1;
            Fix bestDist = Fix.Zero;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Kind != EntityKind.Building || !AiHostile(peer, e)) continue;
                Fix d = (e.Position - from).SqrMagnitude;
                if (best < 0 || d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }

        // ---------------------------------------------------------------------
        // Growth

        private static bool AiGrowsWorkers(Faction faction)
        {
            for (int i = 0; i < FactionData.GrowthTargetCount; i++)
            {
                var row = FactionData.GrowthTarget(i);
                if (row.Faction == faction && row.Cost.FromRole == Role.Worker) return true;
            }
            return false;
        }

        /// <summary>
        /// docs/TECH-TREE.md 묶음 9 성장. Every worker past AiWorkerTarget, newest
        /// first, is grown into the best entry of the role the army is shortest of
        /// that a worker can grow into now; then one free fighter whose entry grows
        /// into a walking entry of the same role at the same tier or higher (잎날
        /// 정령 into 폭풍 기수, 번개 정령 into 번개새 무리) is grown. One unit a
        /// think, so a bank that just paid for a growth is not emptied by a second.
        /// </summary>
        private void AiGrow(int peer, int workers, ref AiArmy army, ref int bank)
        {
            Faction faction = factions[peer];
            if (!AiGrowsWorkers(faction)) return;
            AiComposition c = FactionData.AiCompositionOf(faction);

            int spare = workers - AiWorkerTarget;
            for (int i = entities.Count - 1; i >= 0 && spare > 0; i--)
            {
                Entity w = entities[i];
                if (!w.Alive || w.Owner != peer || w.Kind != EntityKind.Worker || IsGrowing(w)) continue;
                spare--;

                bool antiAir = AiWantsAntiAir(c, army);
                Role role = Role.None;
                int slot = 0;
                AiRole picked = AiRole.None;
                if (antiAir && AiGrowthFromWorker(peer, AiRole.None, true, bank, out role, out slot))
                {
                    picked = AiRoleOf(faction, role, slot);
                }
                else
                {
                    for (int rank = 0; rank < 3 && role == Role.None; rank++)
                    {
                        AiRole want = AiShortRole(c, army, rank);
                        if (want == AiRole.None) continue;
                        if (AiGrowthFromWorker(peer, want, false, bank, out role, out slot)) picked = want;
                    }
                }
                if (role == Role.None) break; // nothing it can pay for; the rest keep gathering

                AiIssue(peer, CommandType.Grow, i, FixVec2.Zero, Command.RosterArg(role, slot));
                bank -= FactionData.Growth(faction, role, slot).Resources;
                army.Add(picked);
                if (HitsAir(faction, role, slot)) army.AntiAir++;
            }

            for (int i = 0; i < entities.Count; i++)
            {
                Entity u = entities[i];
                if (!AiIsFreeFighter(peer, u)) continue;
                for (int g = 0; g < FactionData.GrowthTargetCount; g++)
                {
                    var row = FactionData.GrowthTarget(g);
                    if (row.Faction != faction || row.Cost.FromRole != u.Role || row.Cost.FromSlot != u.Slot) continue;
                    if (AiRoleOf(faction, row.Role, row.Slot) != AiRoleOf(faction, u.Role, u.Slot)) continue;
                    if (FactionData.Tier(faction, row.Role, row.Slot) < FactionData.Tier(faction, u.Role, u.Slot)) continue;
                    if (!MeetsTierAndPrerequisite(peer, row.Role, row.Slot) || row.Cost.Resources > bank) continue;
                    AiIssue(peer, CommandType.Grow, i, FixVec2.Zero, Command.RosterArg(row.Role, row.Slot));
                    bank -= row.Cost.Resources;
                    return;
                }
            }
        }

        /// <summary>
        /// The best entry a worker can grow into now for this role (or, with
        /// antiAir, any entry that hits air), the way AiEntryAt picks one a
        /// building can make: the same AiTakes and AiBetter, with the growth
        /// table's own gate and price in place of CanProduce and the production
        /// price.
        /// </summary>
        private bool AiGrowthFromWorker(int peer, AiRole want, bool antiAir, int bank, out Role role, out int slot)
        {
            Faction faction = factions[peer];
            role = Role.None;
            slot = 0;
            for (int g = 0; g < FactionData.GrowthTargetCount; g++)
            {
                var row = FactionData.GrowthTarget(g);
                if (row.Faction != faction || row.Cost.FromRole != Role.Worker) continue;
                if (!AiTakes(faction, row.Role, row.Slot, want, antiAir)) continue;
                if (!MeetsTierAndPrerequisite(peer, row.Role, row.Slot) || row.Cost.Resources > bank) continue;
                AiRole judged = antiAir ? AiRoleOf(faction, row.Role, row.Slot) : want;
                if (!AiBetter(faction, judged, row.Role, row.Slot, role, slot)) continue;
                role = row.Role;
                slot = row.Slot;
            }
            return role != Role.None;
        }

        // ---------------------------------------------------------------------
        // Buildings

        /// <summary>
        /// Places the one building the ladder wants next, or holds its price back
        /// from the rest of this think, and answers how much it held back. In
        /// order: a production building when none stands, so losing the base is
        /// not losing the match on the spot; a 기술 건물, which opens tier 3; an
        /// arrival anchor, for the faction whose defense entry 0 lends one
        /// (차원 유랑종 굴절 기둥), so a 통로 has somewhere to aim; the building
        /// the first body holding an active is waiting for (AiActiveBuilding),
        /// so docs/TECH-TREE.md 묶음 9's actives get a body to cast them; and then
        /// the building the composition is waiting for (AiTechUpBuilding).
        ///
        /// Nothing while anything it owns other than supply is still going up, so
        /// the bank is never sunk into three sites at once. The first four are
        /// always saved for; the composition's building only once the army holds
        /// AiSquadSize bodies.
        /// </summary>
        private int AiPlaceBuilding(int peer, AiArmy army, ref int bank)
        {
            if (AiBuilding(peer)) return 0;
            Faction faction = factions[peer];
            var goal = FactionData.NoBuilding;
            bool always = true;
            if (!AiHolds(peer, Role.Production, unfinishedOnly: false)) goal = (Role.Production, 0);
            else if (!AiHolds(peer, Role.Tech, unfinishedOnly: false)) goal = (Role.Tech, 0);
            else if (FactionData.Arrival(faction, Role.Defense, 0).Radius > 0 &&
                     !AiHolds(peer, Role.Defense, unfinishedOnly: false)) goal = (Role.Defense, 0);
            else goal = AiActiveBuilding(peer);
            if (goal.Role == Role.None)
            {
                goal = AiTechUpBuilding(peer, army);
                always = false;
            }
            if (goal.Role == Role.None) return 0;
            // A building it cannot place yet, for its tier or its prerequisite,
            // is not saved for: the bank would sit on a price it cannot spend.
            if (!MeetsTierAndPrerequisite(peer, goal.Role, goal.Slot)) return 0;

            int price = FactionData.BuildCost(faction, goal.Role, goal.Slot);
            if (price <= bank)
            {
                if (AiBuild(peer, goal.Role, goal.Slot)) bank -= price;
                return 0;
            }
            if (!always && army.Total < AiSquadSize) return 0;
            bank -= price;
            return price;
        }

        /// <summary>
        /// The building in the way of the first entry, in roster order, that
        /// holds an active and that the opponent takes for some role, or
        /// NoBuilding once one of them can be made or grown now. 물 슬라임's
        /// 물방울 생존자 holds 물 지대 깔기 and is a worker, so it asks for nothing;
        /// 돌 골렘's 성벽 골렘 comes out of the production building; the rest wait
        /// on a second research or production building.
        /// </summary>
        private (Role Role, int Slot) AiActiveBuilding(int peer)
        {
            Faction faction = factions[peer];
            int rows = FactionData.AbilityCount(faction);
            var first = FactionData.NoBuilding;
            for (int a = 0; a < rows; a++)
            {
                AbilityRow row = FactionData.Ability(faction, a);
                if (!FactionData.IsCast(row.Kind)) continue;
                if (row.Role == Role.Worker) return FactionData.NoBuilding;
                if (AiRoleOf(faction, row.Role, row.Slot) == AiRole.None) continue;
                if (AiCanMake(peer, row.Role, row.Slot)) return FactionData.NoBuilding;
                if (first.Role != Role.None) continue;
                first = AiMissingBuilding(peer, row.Role, row.Slot);
            }
            return first;
        }

        /// <summary>
        /// The building the composition is waiting for. For each role in order of
        /// how short the army is of it, the goal is the best entry of that role in
        /// the whole roster that a building or a worker's growth makes; when no
        /// building it owns can make it yet and no worker can grow into it, the
        /// first building in its way: the building it comes out of, or the one it
        /// names as its prerequisite, or failing both that building's own
        /// prerequisite. NoBuilding when every goal can be made, or when the gap
        /// is a tier, which the production building and the 기술 건물 close.
        /// </summary>
        private (Role Role, int Slot) AiTechUpBuilding(int peer, AiArmy army)
        {
            Faction faction = factions[peer];
            AiComposition c = FactionData.AiCompositionOf(faction);
            for (int rank = 0; rank < 3; rank++)
            {
                AiRole want = AiShortRole(c, army, rank);
                if (want == AiRole.None) continue;
                if (!AiGoalEntry(faction, want, out Role role, out int slot)) continue;
                if (AiCanMake(peer, role, slot)) continue;
                var missing = AiMissingBuilding(peer, role, slot);
                if (missing.Role != Role.None) return missing;
            }
            return FactionData.NoBuilding;
        }

        private static bool AiGoalEntry(Faction faction, AiRole want, out Role role, out int slot)
        {
            role = Role.None;
            slot = 0;
            for (int r = 0; r < AiBodyRoles.Length; r++)
            {
                Role candidate = AiBodyRoles[r];
                int count = FactionData.SlotCount(faction, candidate);
                for (int s = 0; s < count; s++)
                {
                    if (!AiTakes(faction, candidate, s, want, false)) continue;
                    GrowthCost growth = FactionData.Growth(faction, candidate, s);
                    bool made = FactionData.Production(faction, candidate, s).Produced ||
                                (growth.Grown && growth.FromRole == Role.Worker);
                    if (!made || FactionData.Stats(faction, candidate, s).Lifetime > 0) continue;
                    if (!AiBetter(faction, want, candidate, s, role, slot)) continue;
                    role = candidate;
                    slot = s;
                }
            }
            return role != Role.None;
        }

        private bool AiCanMake(int peer, Role role, int slot)
        {
            GrowthCost growth = FactionData.Growth(factions[peer], role, slot);
            if (growth.Grown && growth.FromRole == Role.Worker) return MeetsTierAndPrerequisite(peer, role, slot);
            for (int i = 0; i < entities.Count; i++)
            {
                if (CanProduce(peer, i, role, slot)) return true;
            }
            return false;
        }

        private (Role Role, int Slot) AiMissingBuilding(int peer, Role role, int slot)
        {
            Faction faction = factions[peer];
            var needs = FactionData.Prerequisite(faction, role, slot);
            if (needs.Role != Role.None && !HasStanding(peer, needs.Role, needs.Slot))
                return AiPlaceable(peer, needs.Role, needs.Slot);
            if (!FactionData.Production(faction, role, slot).Produced) return FactionData.NoBuilding;
            for (int p = 0; p < FactionData.MaxProducers; p++)
            {
                var at = FactionData.ProducedAt(faction, role, slot, p);
                if (at.Role == Role.None) break;
                if (HasStanding(peer, at.Role, at.Slot)) return FactionData.NoBuilding;
            }
            var first = FactionData.ProducedAt(faction, role, slot, 0);
            return first.Role == Role.None ? FactionData.NoBuilding : AiPlaceable(peer, first.Role, first.Slot);
        }

        /// <summary>This building, or the prerequisite standing in its way, or nothing when its tier is the gap.</summary>
        private (Role Role, int Slot) AiPlaceable(int peer, Role role, int slot)
        {
            if (MeetsTierAndPrerequisite(peer, role, slot)) return (role, slot);
            var needs = FactionData.Prerequisite(factions[peer], role, slot);
            if (needs.Role != Role.None && !HasStanding(peer, needs.Role, needs.Slot) &&
                MeetsTierAndPrerequisite(peer, needs.Role, needs.Slot))
                return needs;
            return FactionData.NoBuilding;
        }

        private bool AiBuilding(int peer)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (e.Alive && e.Owner == peer && e.Kind == EntityKind.Building && e.BuildTicksLeft > 0 &&
                    e.Role != Role.Supply)
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------------
        // Research

        /// <summary>
        /// Every finished research building of its own that is not already
        /// researching starts one row, where afford means the bank holds more than
        /// that level costs. A row that unlocks an ability a body it owns, has
        /// queued or can make now holds comes first (docs/TECH-TREE.md 묶음 9 연구: the 칼바람
        /// 회오리 a 바람 정령 is bought for), then index order as before: 공격력,
        /// then 방어력, then the faction's own rows. A row a building cannot run
        /// is refused by CanResearch and the loop moves on; a row it cannot pay
        /// for yet is skipped, not waited on.
        ///
        /// Strictly more than the price, not equal to it, so the order never
        /// empties the bank to the last coin.
        /// </summary>
        // ponytail: index order behind the unlocks. It researches 방어력 against
        // an opponent with no weapons. Give it a reason to pick a row the day the
        // ladder is meant to be a plan.
        private void AiResearch(int peer, ref int bank)
        {
            int rows = FactionData.UpgradeCount(factions[peer]);
            for (int i = 0; i < entities.Count; i++)
            {
                Entity b = entities[i];
                if (!b.Alive || b.Owner != peer || b.Kind != EntityKind.Building) continue;
                if (b.Role != Role.Tech || b.BuildTicksLeft > 0 || b.ResearchIndex >= 0) continue;

                for (int pass = 0; pass < 2; pass++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        if (pass == 0 && !AiUnlocksHeld(peer, row)) continue;
                        if (!CanResearch(peer, i, row) || bank <= ResearchCost(peer, row)) continue;
                        AiIssue(peer, CommandType.Research, i, FixVec2.Zero, row);
                        bank -= ResearchCost(peer, row);
                        // One building, one order per think. A second research
                        // building asked in the same think could pick the same
                        // free row, and Apply would refuse whichever came second.
                        return;
                    }
                }
            }
        }

        /// <summary>Whether this upgrade row unlocks an ability held by a body this peer owns, grows, has queued or can make now.</summary>
        private bool AiUnlocksHeld(int peer, int upgrade)
        {
            Faction faction = factions[peer];
            int rows = FactionData.AbilityCount(faction);
            for (int a = 0; a < rows; a++)
            {
                AbilityRow row = FactionData.Ability(faction, a);
                if (row.Unlock != upgrade) continue;
                for (int i = 0; i < entities.Count; i++)
                {
                    Entity e = entities[i];
                    if (!e.Alive || e.Owner != peer) continue;
                    bool held = (e.Role == row.Role && e.Slot == row.Slot && e.Kind != EntityKind.Building) ||
                                (IsGrowing(e) && e.MorphRole == row.Role && e.MorphSlot == row.Slot) ||
                                (e.QueueCount > 0 && e.ProduceRole == row.Role && e.ProduceSlot == row.Slot);
                    if (held) return true;
                }
                // And one it could make now, so the level is running while the
                // body is bought rather than after it.
                if (AiRoleOf(faction, row.Role, row.Slot) != AiRole.None && AiCanMake(peer, row.Role, row.Slot)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------------
        // Actives

        /// <summary>
        /// docs/TECH-TREE.md 묶음 9 액티브 능력: every active row of its faction,
        /// in table order, and for each every body of its own holding it, by
        /// ascending id. A body casts when AiCastPoint finds the row's condition
        /// met and CanCast takes the order, which is where the cooldown, the
        /// unlock, the range and the price are asked. 깨우기 is the one that
        /// costs, and it is held back while the ladder saves for supply.
        /// </summary>
        private void AiCast(int peer, bool saving, ref int bank)
        {
            Faction faction = factions[peer];
            int rows = FactionData.AbilityCount(faction);
            for (int a = 0; a < rows; a++)
            {
                AbilityRow row = FactionData.Ability(faction, a);
                if (!FactionData.IsCast(row.Kind)) continue;
                int cost = AbilityCost(peer, row);
                if (cost > 0 && (saving || cost > bank)) continue;
                for (int i = 0; i < entities.Count; i++)
                {
                    Entity e = entities[i];
                    if (!e.Alive || e.Owner != peer || e.Role != row.Role || e.Slot != row.Slot) continue;
                    if (e.Kind == EntityKind.Building || !CanUseAbilities(e)) continue;
                    if (!AiCastPoint(peer, i, row, out FixVec2 point)) continue;
                    if (!CanCast(peer, i, a, point)) continue;
                    AiIssue(peer, CommandType.Cast, i, point, a);
                    bank -= cost;
                    if (cost > 0 && cost > bank) break;
                }
            }
        }

        /// <summary>
        /// The condition table of docs/TECH-TREE.md 묶음 9 액티브 능력, one case per
        /// kind, and where the cast is aimed. False when this body should hold it.
        ///
        /// | kind | casts when | aimed at |
        /// |---|---|---|
        /// | 칼바람 회오리, 유성우, 낙뢰, 해일 부름, 모래폭풍 | AiCastMinTargets hostile bodies of its targets within its radius of one of them | that body, within cast range |
        /// | 간헐천 | the same, of a body standing on a water field | that body |
        /// | 물 지대 깔기 | an allied armed unit within its radius + 1, its own cell dry | its own cell |
        /// | 굳기 | at or under 1/AiHardenHpDivisor hp, a hostile within AiHardenReach | itself |
        /// | 깨우기 | debris within cast range | the lowest debris cell |
        /// | 전개 | packed: a hostile its weapon reaches within the deployed reach; deployed: none within it | itself |
        /// </summary>
        private bool AiCastPoint(int peer, int casterId, AbilityRow row, out FixVec2 point)
        {
            Entity e = entities[casterId];
            point = FixVec2.Zero;
            switch (row.Kind)
            {
                case AbilityKind.StrikeZone:
                case AbilityKind.DelayedStrike:
                case AbilityKind.TideStrike:
                case AbilityKind.SandZone:
                    return AiAreaPoint(peer, e, row, out point);

                case AbilityKind.LayField:
                {
                    if (HasWaterField(CellOf(e.Position))) return false;
                    Fix reach = Fix.FromInt(AbilityRadius(peer, row) + 1);
                    for (int i = 0; i < entities.Count; i++)
                    {
                        Entity ally = entities[i];
                        if (i == casterId || !ally.Alive || ally.Owner != peer || ally.Kind != EntityKind.Unit) continue;
                        if (Armed(ally) && WithinRange(e.Position, ally.Position, reach)) return true;
                    }
                    return false;
                }

                case AbilityKind.Harden:
                    return e.Hp * AiHardenHpDivisor <= e.MaxHp && AiHostileNear(peer, e.Position, AiHardenReach, e, false);

                case AbilityKind.Awaken:
                    return AiRemnantPoint(e, row.CastRange, out point);

                case AbilityKind.Deploy:
                {
                    bool near = AiHostileNear(peer, e.Position, Fix.FromInt(row.Amount), e, true);
                    return e.Deployed ? !near : near;
                }
            }
            return false;
        }

        /// <summary>
        /// The hostile body within cast range around which the most hostile
        /// bodies of the row's targets stand within its radius, if that is
        /// AiCastMinTargets or more. The first of equals by ascending id.
        /// </summary>
        private bool AiAreaPoint(int peer, Entity caster, AbilityRow row, out FixVec2 point)
        {
            point = FixVec2.Zero;
            Fix range = Fix.FromInt(row.CastRange);
            Fix radius = Fix.FromInt(AbilityRadius(peer, row));
            int best = AiCastMinTargets - 1;
            bool found = false;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity centre = entities[i];
                if (!centre.Alive || !AiHostile(peer, centre) || !AuraTargets(row.Targets, centre)) continue;
                if (!WithinRange(caster.Position, centre.Position, range)) continue;
                if (row.CastTarget == CastTarget.WaterFieldCell && !HasWaterField(CellOf(centre.Position))) continue;

                int caught = 0;
                for (int j = 0; j < entities.Count; j++)
                {
                    Entity t = entities[j];
                    if (!t.Alive || !AiHostile(peer, t) || !AuraTargets(row.Targets, t)) continue;
                    if (WithinRange(centre.Position, t.Position, radius)) caught++;
                }
                if (caught <= best) continue;
                best = caught;
                point = centre.Position;
                found = true;
            }
            return found;
        }

        private bool AiRemnantPoint(Entity caster, int castRange, out FixVec2 point)
        {
            point = FixVec2.Zero;
            int at = CellOf(caster.Position);
            int cx = at % GridSize, cy = at / GridSize;
            Fix range = Fix.FromInt(castRange);
            for (int y = cy - castRange; y <= cy + castRange; y++)
            {
                for (int x = cx - castRange; x <= cx + castRange; x++)
                {
                    if (x < 0 || y < 0 || x >= GridSize || y >= GridSize) continue;
                    int cell = y * GridSize + x;
                    if (!HasRemnant(cell) || !WithinRange(caster.Position, CellCenter(cell), range)) continue;
                    point = CellCenter(cell);
                    return true;
                }
            }
            return false;
        }

        /// <summary>A hostile unit or worker within reach; with reachable, one this body's weapon can hit.</summary>
        private bool AiHostileNear(int peer, FixVec2 from, Fix reach, Entity self, bool reachable)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity t = entities[i];
                if (!t.Alive || !AiHostile(peer, t)) continue;
                if (t.Kind != EntityKind.Unit && t.Kind != EntityKind.Worker && !(reachable && t.Kind == EntityKind.Building)) continue;
                if (reachable && !CanHit(self, t)) continue;
                if (WithinRange(from, t.Position, reach)) return true;
            }
            return false;
        }

        private bool AiHostile(int peer, Entity e) =>
            e.Owner >= 0 && e.Owner < MaxPeers && e.Owner != peer && e.Kind != EntityKind.ResourceNode;

        // ---------------------------------------------------------------------
        // Attack

        /// <summary>
        /// Sends every free fighter at the enemy once enough of them are standing
        /// around, and every free support body after them with a Move to the same
        /// point. Counted first and ordered second: half a squad sent is a squad
        /// that dies twice, unless no resource node is left to mine, when the
        /// squad it has is all it will get. A free 멸종 슬라임 is sent the think it is free,
        /// squad or not: it lapses 600 ticks after it arrives and was bought for
        /// the wave already out.
        /// </summary>
        // ponytail: one wave, one target, no regrouping and no retreat. Everything
        // walks at the enemy base and whatever it meets on the way is the fight.
        // Split it into groups with their own targets the day one wave stops being
        // the whole plan.
        private void AiAttack(int peer)
        {
            int target = AiEnemyTarget(peer);
            if (target < 0) return;
            FixVec2 where = entities[target].Position;

            // An attack-move that has broken off for a building walks at it in a
            // straight line, and a cliff between the two stops it for good. An
            // Attack order on the same building walks a path to it instead, and
            // ends when the building does.
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner != peer || e.Kind != EntityKind.Unit || e.Mode != OrderMode.AttackMove) continue;
                if (e.TargetId < 0 || e.TargetId >= entities.Count) continue;
                Entity t = entities[e.TargetId];
                if (!t.Alive || t.Kind != EntityKind.Building || WithinRange(e.Position, t.Position, RangeFor(e))) continue;
                AiIssue(peer, CommandType.Attack, i, FixVec2.Zero, e.TargetId);
            }

            int ready = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!AiIsFreeFighter(peer, entities[i])) continue;
                if (AiLapses(entities[i])) AiIssue(peer, CommandType.AttackMove, i, where);
                else ready++;
            }
            // With nothing left in the ground the army it has is the army it
            // will ever have, so waiting for a full squad would wait forever:
            // two peers each holding three fighters at home is a draw.
            if (ready < AiSquadSize && (ready == 0 || FindNearestNode(entities[target].Position) >= 0)) return;

            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (AiIsFreeFighter(peer, e) && !AiLapses(e)) AiIssue(peer, CommandType.AttackMove, i, where);
                else if (AiIsFreeSupport(peer, i)) AiIssue(peer, CommandType.Move, i, where);
            }
        }

        private bool AiLapses(Entity e) => RosterStats(e).Lifetime > 0;

        /// <summary>
        /// Alive, armed, this peer's, able to walk now, and under no order of its
        /// own. Armed is asked of the roster rather than assumed from the kind: an
        /// unarmed body counted here would pad the squad out to AiSquadSize with
        /// something that cannot fight, and then be sent an AttackMove the command
        /// layer refuses. A deployed 견인 대포 is refused one too, so it is not
        /// free until it packs up.
        /// </summary>
        private bool AiIsFreeFighter(int peer, Entity e) =>
            e.Alive && e.Owner == peer && e.Kind == EntityKind.Unit &&
            e.Role != Role.Worker && e.Speed.Raw != 0 && e.Mode == OrderMode.None &&
            Armed(e) && !Anchored(e) && !IsGrowing(e);

        /// <summary>An unarmed support body of its own standing still with no order: one a wave takes along.</summary>
        private bool AiIsFreeSupport(int peer, int id)
        {
            Entity e = entities[id];
            if (!e.Alive || e.Owner != peer || e.Kind != EntityKind.Unit || e.Speed.Raw == 0) return false;
            if (Armed(e) || e.Mode != OrderMode.None || e.Fleeing || IsGrowing(e) || !PathDone(id)) return false;
            return AiRoleOf(factions[peer], e.Role, e.Slot) == AiRole.Support;
        }

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

        // ---------------------------------------------------------------------
        // Shared

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
                if (e.Alive && e.Owner == peer && e.Kind == EntityKind.Worker && !IsGrowing(e)) n++;
            }
            return n;
        }

        /// <summary>
        /// Rings outward from the base, taking the first cell CanBuild accepts for
        /// this entry. The scan order is fixed and the test is the simulation's
        /// own, so every peer running this arrives at the same cell with no
        /// tie-break of its own.
        /// </summary>
        // ponytail: rings around the base and nothing else. It can box its own
        // workers behind a wall of supply buildings, and now that the map has
        // terrain it walks past chokes it cannot see. CanBuild refuses the cells it
        // may not have, so the ring is safe, merely blind. Give it real placement
        // the day where it builds is supposed to be a decision.
        private bool AiBuild(int peer, Role role, int slot)
        {
            int home = AiHome(peer);
            if (home < 0) return false;

            FixVec2 from = entities[home].Position;
            for (int r = 2; r <= AiBuildRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Abs(dx) != r && Abs(dy) != r) continue; // the ring, not the disc

                        var at = new FixVec2(from.X + Fix.FromInt(dx), from.Y + Fix.FromInt(dy));
                        if (!CanBuild(peer, role, slot, at)) continue;
                        AiIssue(peer, CommandType.Build, -1, at, Command.RosterArg(role, slot));
                        return true;
                    }
                }
            }
            return false;
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
