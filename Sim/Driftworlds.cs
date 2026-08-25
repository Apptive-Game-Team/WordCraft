namespace WordCraft.Sim
{
    /// <summary>
    /// 차원 유랑종 통로: the 통로 does not make units, it lets them cross. A Produce
    /// aimed at one names a point on the map, and if that point lies inside
    /// <see cref="ArrivalRadius"/> of a friendly 굴절 기둥 or 경계 운반자 the body
    /// stands up there instead of at the building. 무효면 통로 자리에 출현한다.
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
    /// 성장, which is one faction's workers. Neither can reach a 차원 유랑종 body,
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
        /// Entry 0. Named rather than left to default, for the reason
        /// TowerbackSlot is: 차원 유랑종 fields one production building today, and a
        /// second entry added to that slot later would be a different building that
        /// this rule has said nothing about.
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
        /// 반경 6칸, per docs/FACTION-MECHANICS.md, and the same number for the
        /// 굴절 기둥 and the 경계 운반자 both — the document gives them one radius,
        /// and one that walks is the only difference between them.
        /// </summary>
        public static readonly Fix ArrivalRadius = Fix.FromInt(6);

        /// <summary>
        /// True for a 통로 and nothing else. Read off faction, kind, role and entry
        /// rather than carried on the entity, for the reason IsWarlord is: none of
        /// the four ever changes for a given body, so making it state would add a
        /// hashed field that can only ever hold one value.
        /// </summary>
        public bool IsPassage(Entity b) =>
            b.Kind == EntityKind.Building && b.Role == PassageRole && b.Slot == PassageSlot &&
            b.Owner >= 0 && b.Owner < MaxPeers && factions[b.Owner] == PassageFaction;

        /// <summary>
        /// True for a body lending this peer an arrival radius right now. A standing
        /// 굴절 기둥 or a living 경계 운반자, and the word right now is the whole of
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
            if (e.Owner < 0 || e.Owner >= MaxPeers || factions[e.Owner] != PassageFaction) return false;
            if (e.Kind == EntityKind.Building)
            {
                return e.Role == PillarRole && e.Slot == PillarSlot && e.BuildTicksLeft <= 0;
            }
            return e.Kind == EntityKind.Unit && e.Role == CarrierRole && e.Slot == CarrierSlot;
        }

        /// <summary>
        /// Whether this peer may arrive a body at this point, asked of the world as
        /// it stands on this tick. The one place the rule is written, and it is
        /// called at both moments the mechanic decides — a second copy of it is the
        /// one defect that could make the two answers differ for a reason that is
        /// not about the world.
        ///
        /// Distance goes through WithinRange, which compares SqrMagnitude against
        /// the square of the radius in fixed point and takes no square root. Every
        /// range question in the simulation already asks it that way, and a second
        /// distance calculation invented here would be a second rounding for two
        /// peers to disagree over.
        ///
        /// The scan is by ascending entity id and stops at the first anchor in
        /// range, which costs nothing to say because the answer is a bool: there is
        /// no anchor to name, so the order it is found in cannot reach the result.
        ///
        /// Public because the view will tint a placement cursor with it once the
        /// arrival point is something a player picks. The answer that counts is the
        /// one taken inside the simulation; a view that asked a moment earlier is
        /// looking at a world one tick stale, which is the caveat CanBuild carries.
        /// </summary>
        public bool ArrivalValid(int peer, FixVec2 point)
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

            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!ProvidesArrival(e, peer)) continue;
                if (WithinRange(point, e.Position, ArrivalRadius)) return true;
            }
            return false;
        }

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
        /// Nothing but a 통로 keeps an arrival point. A Base can produce and
        /// 차원 유랑종's is 정박한 세계, not a 통로 — 통로는 유닛을 만들지 않는다 is
        /// about one building, so this rule is too.
        /// </summary>
        private void AimArrival(ref Entity b, int peer, FixVec2 point)
        {
            if (!IsPassage(b)) return;

            b.HasArrivalPoint = ArrivalValid(peer, point);
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
            b.HasArrivalPoint && ArrivalValid(b.Owner, b.ArrivalPoint)
                ? b.ArrivalPoint
                : b.Position + RallyOffset;
    }
}
