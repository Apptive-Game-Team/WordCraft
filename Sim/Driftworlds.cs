namespace WordCraft.Sim
{
    /// <summary>
    /// 차원 유랑종 통로: the 통로 does not make units, it lets them cross. A Produce
    /// aimed at one names a point on the map, and if that point lies inside the
    /// radius of a friendly anchor (굴절 기둥, 경계 운반자, 위상 닻, 떠도는 섬, each by
    /// its own row of FactionData's arrival table) the body stands up there
    /// instead of at the building. 무효면 통로 자리에 출현한다.
    ///
    /// Every mechanic before this one decided at a single moment. This one decides
    /// twice — once when the order is given and once when the body arrives — and
    /// the whole of the design is what has to be true for the second answer to be
    /// the same on both peers. What follows is that audit: everything that can
    /// change between the two moments, and what answers it.
    ///
    /// The arrival point itself cannot change. It is frozen onto the 통로 by the
    /// order that named it and read back by the arrival that uses it, so the only
    /// thing the second check re-asks is what the world says about that point.
    ///
    /// A 굴절 기둥 can be destroyed, and this is the case docs/FACTION-MECHANICS.md
    /// names: 기둥이 부서지면 그 지역에 대한 접근이 통째로 끊긴다. Deaths happen in
    /// CombatSystem, which is one fixed step in Step's system order, and the arrival
    /// is read in ProductionSystem, which is another. Production runs before combat,
    /// so a pillar that falls on tick T anchored the arrival that landed on tick T
    /// and anchors nothing on T+1. That boundary is a consequence of the system
    /// order and not a rule written here — which is exactly why it is the same on
    /// both peers, and why moving either system past the other would move it.
    ///
    /// A 굴절 기둥 can also be finished. ConstructionSystem runs before
    /// ProductionSystem, so a site that completes on tick T anchors an arrival
    /// landing on tick T. An unfinished site anchors nothing, which is the rule
    /// PopulationCap, TierOf and FindNearestDropOff already keep about a building
    /// that is not standing yet.
    ///
    /// A 경계 운반자 moves, and it is the case where the two answers most obviously
    /// differ: 이동식 반경 6칸 means the radius walks with the body, so a point that
    /// was inside it when the order was given can be outside it a minute later with
    /// nobody attacking anything. MoveSystem is what moved it, it runs last in the
    /// tick, and the position it wrote is hashed — so the position the second check
    /// reads is a number both peers have already agreed on for that tick.
    ///
    /// Nothing else about an anchor can change at all, and that is worth stating
    /// rather than assuming. Owner moves only through 인간 징발, which transfers a
    /// neutral 꼬마돌 and nothing else. Role and Slot move only through 세계수 정령
    /// 성장, which is one faction's bodies. Neither can reach a 차원 유랑종 body,
    /// so an anchor is an anchor for its whole life and the only questions left are
    /// where it is standing and whether it is alive.
    ///
    /// The producing 통로 can itself die, and ProductionSystem's own loop skips a
    /// dead building before any of this is reached: nothing arrives, which is what
    /// losing the 통로 mid-queue should cost.
    ///
    /// And the thing the second check must not do is choose. It asks whether any
    /// anchor is in range, never which one, so there is no nearest-anchor tie to
    /// break and no order-dependent answer to get wrong. That is the same reason
    /// the document puts an invalid arrival at the 통로 rather than at the nearest
    /// valid point: "가장 가까운" is a judgement that needs tie-breaking, and this
    /// mechanic needs none.
    ///
    /// Requiring both answers rather than only the second is what keeps the first
    /// check load-bearing. A point that no anchor covered when the order was given
    /// is not rescued by a 굴절 기둥 raised while the body was crossing: the verdict
    /// was taken then, and <see cref="Entity.HasArrivalPoint"/> is the whole of what
    /// was kept from it.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// The one faction that arrives rather than produces. Read off the roster's
        /// faction rather than carried anywhere, so nothing can disagree with it.
        /// </summary>
        public const Faction PassageFaction = Faction.Driftworlds;

        /// <summary>The slot 통로 occupies, and which entry of it.</summary>
        public const Role PassageRole = Role.Production;

        /// <summary>
        /// Entry 0. The rule itself reads FactionData's arrival table, where 깊은
        /// 통로 (entry 1) is the second 통로; this name is for the harness and the
        /// view, which mean the first.
        /// </summary>
        public const int PassageSlot = 0;

        /// <summary>The slot 굴절 기둥 occupies, and which entry of it.</summary>
        public const Role PillarRole = Role.Defense;

        /// <summary>Entry 0, for the reason PassageSlot is.</summary>
        public const int PillarSlot = 0;

        /// <summary>The slot 경계 운반자 occupies, and which entry of it.</summary>
        public const Role CarrierRole = Role.Signature;

        /// <summary>Entry 0, for the reason PassageSlot is.</summary>
        public const int CarrierSlot = 0;

        /// <summary>
        /// The slot 틈새 사수 occupies, and which entry of it: the one body the
        /// production table times at the 통로's own clock, and so the one 도착
        /// 가속 speeds up.
        /// </summary>
        public const Role MarksmanRole = Role.Ranged;

        /// <summary>
        /// Entry 0, for the reason PassageSlot is. The three 멸종 슬라임 sit behind
        /// entries of the melee and ranged slots and are not this body.
        /// </summary>
        public const int MarksmanSlot = 0;

        /// <summary>
        /// 반경 6칸, the 굴절 기둥's row of FactionData's arrival table, which the
        /// 경계 운반자's row shares. Read off the table rather than written again
        /// here, so the view's ring and the simulation's check have one source.
        /// </summary>
        public static readonly Fix ArrivalRadius =
            Fix.FromInt(FactionData.Arrival(PassageFaction, PillarRole, PillarSlot).Radius);

        /// <summary>The radius of that row once 도착 반경 is researched, off the same row.</summary>
        public static readonly Fix ArrivalRadiusResearched =
            Fix.FromInt(FactionData.Arrival(PassageFaction, PillarRole, PillarSlot).RadiusResearched);

        /// <summary>
        /// 도착 가속: the 통로's own clock once researched. The unresearched clock is
        /// FactionData.PassageArrivalTicks, which is where the table prices it.
        /// </summary>
        public const int ArrivalTicksResearched = 40;

        /// <summary>
        /// 도착 반경: the radius this peer's 굴절 기둥 and 경계 운반자 lend, the two
        /// rows of the arrival table at 6 cells that the #164 research widens. Kept
        /// for the view, which draws that ring; the simulation asks each anchor its
        /// own radius through ArrivalRadiusOf.
        /// </summary>
        public Fix ArrivalRadiusFor(int peer)
        {
            if (peer < 0 || peer >= MaxPeers) return ArrivalRadius;
            return RadiusOf(peer, FactionData.Arrival(PassageFaction, PillarRole, PillarSlot));
        }

        /// <summary>
        /// The radius this body lends its owner right now, by its own row of the
        /// arrival table: 6 for a 굴절 기둥 or a 경계 운반자, 3 for a 위상 닻, 4 for a
        /// 떠도는 섬, and zero for everything else. A row's research widens that row
        /// and no other, read on the tick it is asked, so a level that lands while a
        /// body is crossing widens the second check and never narrows it.
        ///
        /// Whether the body is standing or alive is ProvidesArrival's question, not
        /// this one's.
        /// </summary>
        public Fix ArrivalRadiusOf(Entity e)
        {
            if (e.Owner < 0 || e.Owner >= MaxPeers) return Fix.Zero;
            return RadiusOf(e.Owner, FactionData.Arrival(factions[e.Owner], e.Role, e.Slot));
        }

        private Fix RadiusOf(int peer, ArrivalRow row) =>
            Fix.FromInt(row.RadiusEffect != UpgradeEffect.None && UpgradeLevelOf(peer, row.RadiusEffect) > 0
                ? row.RadiusResearched
                : row.Radius);

        /// <summary>
        /// 도착 가속: the 통로's clock, 도착 | 통로에서 60틱 in the document's table,
        /// for this peer.
        /// </summary>
        public int ArrivalTicksFor(int peer) =>
            UpgradeLevelOf(peer, UpgradeEffect.ArrivalTicks) > 0 ? ArrivalTicksResearched : FactionData.PassageArrivalTicks;

        /// <summary>
        /// How long the body at the head of this building's queue takes. The
        /// table's clock for everything, except 틈새 사수 coming out of a 통로: it
        /// crosses at the 통로's own clock, ArrivalTicksFor, which is the number
        /// 도착 가속 moves. Named by role and entry, the way the pillar and the
        /// carrier are, so a body priced at 60 ticks for some other reason is
        /// never sped up by accident.
        /// </summary>
        private int ProduceTicksOf(Entity b)
        {
            if (IsPassage(b) && b.ProduceRole == MarksmanRole && b.ProduceSlot == MarksmanSlot)
            {
                return ArrivalTicksFor(b.Owner);
            }
            return FactionData.Production(factions[b.Owner], b.ProduceRole, b.ProduceSlot).Ticks;
        }

        /// <summary>
        /// True for a standing 통로 or 깊은 통로, the rows of the arrival table marked
        /// Passage, and nothing else. Faction, kind, role and entry
        /// are read off the body rather than carried on it, for the reason IsWarlord
        /// is: none of the four ever changes for a given body, so making any of them
        /// state would add a hashed field that can only ever hold one value.
        ///
        /// The build clock is the fifth clause and the one that does change, which
        /// is why it is read here rather than stored: BuildTicksLeft is already
        /// hashed, so asking it costs nothing and answers about the world as it
        /// stands on this tick, exactly as ProvidesArrival's own build clause does.
        ///
        /// A site that is not standing yet keeps no arrival point. ProvidesArrival
        /// writes the list — no population, no tier, no deliveries, no radius — and
        /// an arrival point is the fifth thing on it rather than a rule of its own.
        /// The point exists to say where a body this building makes will stand up,
        /// and an unfinished building makes none: TryQueueUnit refuses
        /// BuildTicksLeft &gt; 0 before AimArrival is reached, so a point frozen onto
        /// a site still going up would be a setting about production that cannot
        /// happen. Writing it here rather than at that one caller is the same
        /// lesson ProvidesArrival's faction clause is: a rule kept by the order its
        /// callers happen to test things in is not a rule the method has, and the
        /// view calls this one with no TryQueueUnit anywhere above it.
        /// </summary>
        public bool IsPassage(Entity b) =>
            b.Kind == EntityKind.Building && b.BuildTicksLeft <= 0 &&
            b.Owner >= 0 && b.Owner < MaxPeers &&
            FactionData.Arrival(factions[b.Owner], b.Role, b.Slot).Passage;

        /// <summary>
        /// True for a body lending this peer an arrival radius right now: a living
        /// body or a standing building whose row of the arrival table has a radius
        /// (굴절 기둥, 경계 운반자, 위상 닻, 떠도는 섬), and the word right now is the whole of
        /// it: this is asked twice about the same point and is free to answer
        /// differently, which is the mechanic rather than a hazard.
        ///
        /// An unfinished 굴절 기둥 anchors nothing. A site that is not standing yet
        /// supports no population, opens no tier and takes no deliveries, and a
        /// radius is the fourth thing on that list rather than a rule of its own.
        ///
        /// The faction is asked first, exactly as IsPassage and CanFlee ask it, and
        /// asking it here rather than leaving it to the caller is the whole of what
        /// makes the rest of this method mean anything. Role and Slot alone do not
        /// name a 경계 운반자: CarrierRole is Role.Signature at entry 0 and so is
        /// 지옥불's WarlordRole, so a 군단장 stands on this body's exact
        /// coordinate, and PillarRole is Role.Defense at entry 0 where 인간 keeps a
        /// 대포. ArrivalValid refuses a non-arriving peer before its scan and hid
        /// both of those, but that is one caller's ordering rather than a property
        /// of this answer — and this method is public, so ArrivalOrder.Anchors asks
        /// it with nothing above it.
        /// </summary>
        public bool ProvidesArrival(Entity e, int peer)
        {
            if (!e.Alive || e.Owner != peer) return false;
            if (e.Owner < 0 || e.Owner >= MaxPeers) return false;
            // The arrival table is keyed by faction, so asking it is the faction
            // test: a 지옥불 군단장 at Signature 0 finds no row.
            if (FactionData.Arrival(factions[e.Owner], e.Role, e.Slot).Radius <= 0) return false;
            if (e.Kind == EntityKind.Building) return e.BuildTicksLeft <= 0;
            return e.Kind == EntityKind.Unit;
        }

        /// <summary>
        /// Whether this peer may arrive a walking body at this point: the
        /// three-argument rule below with air false. A water or rock cell, a cell
        /// under debris, and a cell a building or a resource node stands on all
        /// answer no here, whatever the anchors say.
        ///
        /// Public because the view tints its aim cursor with it. The answer that
        /// counts is the one taken inside the simulation; a view that asked a moment
        /// earlier is looking at a world one tick stale, which is the caveat
        /// CanBuild carries. A view aiming a flying body is told no on those cells
        /// too, which is the safe direction to be wrong in.
        /// </summary>
        public bool ArrivalValid(int peer, FixVec2 point) => ArrivalValid(peer, point, air: false);

        /// <summary>
        /// Whether this peer may arrive a body that flies or walks at this point,
        /// asked of the world as it stands on this tick. The one place the rule is
        /// written, and it is called at both moments the mechanic decides — a
        /// second copy of it is the one defect that could make the two answers
        /// differ for a reason that is not about the world.
        ///
        /// A walking body needs a cell it could stand on (docs/TECH-TREE.md 도착
        /// 규칙): GroundCellOpen, the cells CanBuild refuses and the pathfinder
        /// blocks. Otherwise the body stands up at the 통로, the same ending every
        /// other invalid point gets. A flying body is not asked, for the reason
        /// IsPassable lets air through. The cell is asked here rather than beside
        /// the call in ArrivalOf, so a body aimed at debris is refused when the
        /// order is given, and one aimed at a cell that debris covered while it
        /// crossed lands at the 통로.
        ///
        /// Distance goes through WithinRange, which compares SqrMagnitude against
        /// the square of each anchor's own radius in fixed point and takes no
        /// square root. Every range question in the simulation already asks it that
        /// way, and a second distance calculation invented here would be a second
        /// rounding for two peers to disagree over.
        ///
        /// The scan is by ascending entity id and stops at the first anchor in
        /// range, which costs nothing to say because the answer is a bool: there is
        /// no anchor to name, so the order it is found in cannot reach the result.
        /// </summary>
        public bool ArrivalValid(int peer, FixVec2 point, bool air)
        {
            if (peer < 0 || peer >= MaxPeers) return false;
            // 아군 굴절 기둥이나 경계 운반자. Only 차원 유랑종 fields either, so the
            // faction test is what keeps every other faction's Produce — which
            // carries a Target too, and has carried FixVec2.Zero in every log ever
            // recorded — out of this rule entirely.
            if (factions[peer] != PassageFaction) return false;

            // Off the map is refused rather than clamped, exactly as CanBuild
            // refuses a placement off the map: CellOf would clamp a body onto the
            // edge, and a body standing somewhere nobody asked for is worse than an
            // order that came out at the 통로.
            if (point.X < Fix.Zero || point.Y < Fix.Zero) return false;
            if (point.X >= Fix.FromInt(GridSize) || point.Y >= Fix.FromInt(GridSize)) return false;

            if (!air && !GroundCellOpen(CellOf(point))) return false;

            // Each anchor by its own row's radius. Still a bool and still the first
            // anchor in range: a radius per anchor changes how far each one reaches
            // and nothing about which one answers.
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!ProvidesArrival(e, peer)) continue;
                if (WithinRange(point, e.Position, ArrivalRadiusOf(e))) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether the entry on this building's queue flies, which decides whether
        /// its arrival cell is asked at all. Off the roster, like Flies.
        /// </summary>
        private bool QueuedBodyFlies(Entity b) =>
            FactionData.Stats(factions[b.Owner], b.ProduceRole, b.ProduceSlot).Air;

        /// <summary>
        /// The first of the two checks: what the queue keeps of the point the order
        /// named. A point no anchor covered is not kept at all, so the body comes
        /// out at the 통로 — the same ending an arrival invalidated in flight gets,
        /// because it is the same verdict about the same point.
        ///
        /// Not a refusal, and that is a decision rather than an omission. Refusing
        /// the Produce outright would be a second consequence for one verdict, and
        /// it would fall on every client and every recorded log that sends
        /// FixVec2.Zero on a Produce today: a 차원 유랑종 peer would stop producing
        /// altogether until something taught it to aim. The order is accepted and
        /// paid for either way; what the point decides is where the body stands up.
        ///
        /// Overwritten by each accepted Produce rather than held for the queue that
        /// paid for it, which is what a rally point does and for the same reason:
        /// aiming costs nothing, so re-aiming refunds nothing and there is no price
        /// for the two to disagree about. Every body still on the queue arrives at
        /// the point named last, and each of them is checked again on its own tick.
        ///
        /// Nothing but a 통로 or a 깊은 통로 keeps an arrival point: the rows of
        /// the arrival table marked Passage. A Base can produce and 차원 유랑종's
        /// is 정박한 세계, which has no such row, so its bodies come out at its
        /// RallyOffset as every other building's do.
        /// </summary>
        private void AimArrival(ref Entity b, int peer, FixVec2 point)
        {
            if (!IsPassage(b)) return;

            // Asked for the body this order queued. A queue holds one entry, so
            // every body on it flies or walks alike.
            b.HasArrivalPoint = ArrivalValid(peer, point, QueuedBodyFlies(b));
            // Cleared rather than left standing when the point is refused, for the
            // reason ClearGrowth clears what a finished growth was becoming: the
            // field is hashed, and a refused point kept on the building would put a
            // 통로's whole history into the hash. Two buildings alike in every way a
            // system can see would hash apart over a point neither of them will
            // ever use.
            b.ArrivalPoint = b.HasArrivalPoint ? point : FixVec2.Zero;
        }

        /// <summary>
        /// The second of the two checks, and where the body actually stands up. Read
        /// in ProductionSystem on the tick the queue finishes one, which is the
        /// single fixed point in the system order at which 도착 happens.
        ///
        /// Both failures land in the same place, which is the whole of 무효면 통로
        /// 자리에 출현한다: a point that was never valid and a point that stopped
        /// being valid are one rule, not two. The 통로 자리 is the same offset every
        /// other building's units come out on, so a 차원 유랑종 player who aims
        /// nothing plays exactly as everyone else does.
        /// </summary>
        private FixVec2 ArrivalOf(Entity b) =>
            b.HasArrivalPoint && ArrivalValid(b.Owner, b.ArrivalPoint, QueuedBodyFlies(b))
                ? b.ArrivalPoint
                : b.Position + RallyOffset;
    }
}
