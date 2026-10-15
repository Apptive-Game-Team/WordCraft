using System.Collections.Generic;

namespace WordCraft.Sim
{
    /// <summary>
    /// 액티브, docs/TECH-TREE.md 묶음 8: the Cast command and the ten abilities
    /// it uses. Apply refuses or accepts a Cast in the shape it refuses a
    /// Produce, and does the four instant ones on the spot (물 지대 깔기, 해일
    /// 부름, 굳기, 깨우기). The rest set a clock and a point on the caster, and
    /// AbilitySystem runs them: a delayed hit lands on the tick the clock
    /// reaches 0, a zone stands while it runs, and a 전개 transition flips
    /// Deployed when it ends.
    ///
    /// A clock a Cast sets is not counted down on the tick of the Cast. So a
    /// clock of N lands or flips N ticks after the order runs, and a zone of N
    /// is read by CombatSystem and MoveSystem on N ticks, the cast tick first.
    /// The cooldown is counted down on the cast tick like any other tick, so a
    /// cooldown of N lets the next Cast in N ticks later.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>깨우기 stands up 깨어난 바위: 돌 골렘 melee entry 3, never produced.</summary>
        public const Role AwakenedRole = Role.Melee;
        public const int AwakenedSlot = 3;

        /// <summary>해일 부름 lays its field on the cells this many either side of the point's cell: 3×3.</summary>
        public const int TideFieldHalf = 1;

        // The casters whose clock a Cast set this tick, filled by Apply in
        // canonical order and emptied by AbilitySystem in the same tick, so it
        // is scratch and not state: nothing in it survives a tick and nothing
        // about it is hashed. AbilitySystem leaves these clocks alone for the
        // one tick, which is what makes a clock of N mean N ticks.
        private readonly List<int> castsThisTick = new List<int>();

        /// <summary>What a Cast of this row costs this peer: the boosted cost once its research lands (깨우기 비용).</summary>
        public int AbilityCost(int peer, AbilityRow row) =>
            row.Boost >= 0 && row.BoostedCost > 0 && UpgradeLevel(peer, row.Boost) > 0 ? row.BoostedCost : row.Cost;

        /// <summary>
        /// Whether this Cast would be accepted right now, for the client and the
        /// harness: the same questions TryCast asks, in the same order, with no
        /// state touched.
        /// </summary>
        public bool CanCast(int peer, int casterId, int index, FixVec2 target) =>
            CastRefusal(peer, casterId, index, target, out _) == null;

        /// <summary>
        /// Why this Cast would be refused, or null when it would be accepted. The
        /// rules of docs/TECH-TREE.md 능력 공통 규칙 in their order: an owned,
        /// living caster that can use abilities; a row of its owner's table that
        /// is an active and that this caster's entry holds; the unlock research;
        /// the cooldown at 0 (and no 전개 transition running); the point within
        /// the row's range, on a water field for 간헐천 and on debris for 깨우기;
        /// the resources; for 깨우기, room under the population cap.
        /// </summary>
        public string CastRefusal(int peer, int casterId, int index, FixVec2 target, out AbilityRow row)
        {
            row = default;
            if (!OwnedAndAlive(casterId, peer)) return "caster";
            Entity e = entities[casterId];
            if (!CanUseAbilities(e)) return "caster";
            row = FactionData.Ability(factions[peer], index);
            if (!FactionData.IsCast(row.Kind) || row.Role != e.Role || row.Slot != e.Slot ||
                e.Kind == EntityKind.Building || e.Kind == EntityKind.ResourceNode)
                return "row";
            if (!AbilityUnlocked(peer, row)) return "unlock";
            if (e.AbilityCooldown > 0) return "cooldown";
            if (FactionData.CastRunsClock(row.Kind) && e.AbilityTicksLeft > 0) return "cooldown";
            if (row.CastTarget != CastTarget.NoTarget)
            {
                if (!WithinRange(e.Position, target, Fix.FromInt(row.CastRange))) return "range";
                int cell = CellOf(target);
                if (row.CastTarget == CastTarget.WaterFieldCell && !HasWaterField(cell)) return "range";
                if (row.CastTarget == CastTarget.RemnantCell && !HasRemnant(cell)) return "range";
            }
            if (resources[peer] < AbilityCost(peer, row)) return "cost";
            if (row.Kind == AbilityKind.Awaken && population[peer] >= PopulationCap(peer)) return "population";
            return null;
        }

        /// <summary>
        /// Apply's Cast. Refused whole on any rule CastRefusal names; accepted,
        /// the cost is taken and the cooldown starts, and then the row's kind
        /// decides the rest.
        /// </summary>
        private void TryCast(Command c)
        {
            if (CastRefusal(c.PeerId, c.EntityId, c.Arg, c.Target, out AbilityRow r) != null) return;
            int id = c.EntityId;
            Entity e = entities[id];
            int row = FactionData.AbilityOf(factions[e.Owner], e.Role, e.Slot, r.Kind);
            resources[e.Owner] -= AbilityCost(e.Owner, r);
            e.AbilityCooldown = r.Cooldown;

            switch (r.Kind)
            {
                case AbilityKind.StrikeZone:
                case AbilityKind.DelayedStrike:
                case AbilityKind.SandZone:
                    // The point is the order's, as it stands now; nothing that
                    // walks afterwards moves it.
                    e.AbilityTicksLeft = AbilityTicks(e.Owner, r);
                    e.AbilityPoint = c.Target;
                    entities[id] = e;
                    castsThisTick.Add(id);
                    break;

                case AbilityKind.Deploy:
                    // Either way the body stops where it stands: a cannon
                    // deploying walks nowhere, and one packing up starts with no
                    // walk left over from before it deployed.
                    ClearOrders(ref e);
                    Halt(id, ref e);
                    e.AbilityTicksLeft = AbilityTicks(e.Owner, r);
                    entities[id] = e;
                    castsThisTick.Add(id);
                    break;

                case AbilityKind.LayField:
                    // Gathering and carrying go on untouched.
                    entities[id] = e;
                    WriteWaterField(CellOf(e.Position), AbilityRadius(e.Owner, r), Tick + AbilityTicks(e.Owner, r));
                    break;

                case AbilityKind.TideStrike:
                    // At once and with no delay: the field now, and the hit
                    // through the burst queue, which BurstSystem lands later in
                    // this same tick.
                    entities[id] = e;
                    WriteWaterField(CellOf(c.Target), TideFieldHalf, Tick + AbilityTicks(e.Owner, r));
                    pendingBursts.Add(new PendingBurst { SourceId = id, Owner = e.Owner, Row = row, Position = c.Target });
                    break;

                case AbilityKind.Harden:
                    Harden(id, ref e, r);
                    break;

                case AbilityKind.Awaken:
                {
                    entities[id] = e;
                    int cell = CellOf(c.Target);
                    ConsumeRemnant(cell);
                    // Through SpawnUnit, so the body is counted, armed and timed
                    // exactly as a produced one is.
                    SpawnUnit(e.Owner, AwakenedRole, AwakenedSlot, CellCenter(cell));
                    break;
                }
            }
        }

        /// <summary>
        /// 굳기: the caster dies through Kill, so its population, its own debris
        /// and everything else a death does happen first, and then its cell and
        /// the four beside it take debris lapsing the row's ticks from now. Open
        /// terrain inside the map only, and never shorter than what stands
        /// there, through WriteRemnant. A body standing on one of those cells is
        /// not moved: it can still step off, because StepPassable asks only the
        /// cell a step goes into.
        /// </summary>
        private void Harden(int id, ref Entity e, AbilityRow r)
        {
            int expiry = Tick + AbilityTicks(e.Owner, r);
            int at = CellOf(e.Position);
            Kill(ref e);
            entities[id] = e;
            int x = at % GridSize, y = at / GridSize;
            HardenCell(x, y, expiry);
            HardenCell(x, y - 1, expiry);
            HardenCell(x - 1, y, expiry);
            HardenCell(x + 1, y, expiry);
            HardenCell(x, y + 1, expiry);
        }

        private void HardenCell(int x, int y, int expiry)
        {
            if (x < 0 || y < 0 || x >= GridSize || y >= GridSize) return;
            WriteRemnant(y * GridSize + x, expiry);
        }

        /// <summary>Whether this caster's clock was set by a Cast this tick, which AbilitySystem leaves alone.</summary>
        private bool CastThisTick(int id) => castsThisTick.Contains(id);

        /// <summary>
        /// Takes a tick off a cast clock, unless the Cast set it this tick, and
        /// answers whether it just reached 0. A clock already at 0 stays there
        /// and answers false.
        /// </summary>
        private bool CountDownCast(int id, out Entity e)
        {
            e = entities[id];
            if (e.AbilityTicksLeft == 0 || CastThisTick(id)) return false;
            e.AbilityTicksLeft--;
            entities[id] = e;
            return e.AbilityTicksLeft == 0;
        }

        /// <summary>
        /// 칼바람 회오리: a tick off the zone, and on every tick that lands on a
        /// multiple of the row's period, 0 included, its amount fixed to every
        /// hostile body of its targets within its radius of the point, by
        /// ascending id. Sixty ticks at a period of ten is six hits, the 화염탄
        /// rule.
        /// </summary>
        private void RunStrikeZone(int id, int row)
        {
            if (entities[id].AbilityTicksLeft == 0 || CastThisTick(id)) return;
            CountDownCast(id, out Entity e);
            AbilityRow r = FactionData.AbilityRowAt(row);
            if (e.AbilityTicksLeft % r.Period != 0) return;
            DamageHostileBodies(e.Owner, r.Targets, e.AbilityPoint, Fix.FromInt(AbilityRadius(e.Owner, r)),
                DamageAfterArmor(AbilityAmount(e.Owner, r), 0, 0, 0));
        }

        /// <summary>
        /// 유성우, 간헐천, 낙뢰: a tick off the delay, and on the tick it reaches
        /// 0 a burst at the point, landed by BurstSystem later in this tick. The
        /// point is not asked again: 간헐천's water field may have lapsed by now.
        /// </summary>
        private void RunDelayedStrike(int id, int row)
        {
            if (!CountDownCast(id, out Entity e)) return;
            pendingBursts.Add(new PendingBurst { SourceId = id, Owner = e.Owner, Row = row, Position = e.AbilityPoint });
        }

        /// <summary>전개: a tick off the transition, and Deployed flips on the tick it reaches 0.</summary>
        private void RunDeploy(int id)
        {
            if (!CountDownCast(id, out Entity e)) return;
            e.Deployed = !e.Deployed;
            entities[id] = e;
        }

        /// <summary>
        /// 모래폭풍: the most a hostile zone lets this body reach, in cells, or -1
        /// when no zone stands over it. A zone stands while its caster's clock
        /// runs, over the body of its targets within its radius of the point.
        /// Zones do not stack: the first found by ascending caster id answers,
        /// and every zone gives the same number. A body that reaches 2 or less
        /// already is untouched by the cap.
        /// </summary>
        private int SandStormReach(Entity t)
        {
            if (t.Owner < 0 || t.Owner >= MaxPeers) return -1;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity s = entities[i];
                if (s.AbilityTicksLeft == 0 || !s.Alive || s.Owner == t.Owner || s.Owner < 0 || s.Owner >= MaxPeers) continue;
                if (!FactionData.FactionHolds(factions[s.Owner], AbilityKind.SandZone)) continue;
                int row = RunningAbility(s, AbilityKind.SandZone);
                if (row < 0) continue;
                AbilityRow r = FactionData.AbilityRowAt(row);
                if (!AuraTargets(r.Targets, t)) continue;
                if (WithinRange(s.AbilityPoint, t.Position, Fix.FromInt(AbilityRadius(s.Owner, r))))
                    return AbilityAmount(s.Owner, r);
            }
            return -1;
        }

        /// <summary>The 전개 row this body holds, or -1. Read by the deployed reach and splash.</summary>
        private int DeployRow(Entity e)
        {
            if (e.Owner < 0 || e.Owner >= MaxPeers) return -1;
            if (!FactionData.FactionHolds(factions[e.Owner], AbilityKind.Deploy)) return -1;
            return FactionData.AbilityOf(factions[e.Owner], e.Role, e.Slot, AbilityKind.Deploy);
        }

        /// <summary>
        /// Mid-transition, either way: a 전개 clock running. docs/TECH-TREE.md
        /// 전개: 전환 중에는 쏘지도 걷지도 않는다.
        /// </summary>
        public bool Transitioning(Entity e) =>
            e.AbilityTicksLeft > 0 && DeployRow(e) >= 0 && RunningAbility(e, AbilityKind.Deploy) >= 0;

        /// <summary>Deployed or mid-transition: a body that does not walk and takes no order to.</summary>
        public bool Anchored(Entity e) => e.Deployed || Transitioning(e);

        /// <summary>
        /// Scenario setup only. Never call this from a system. Puts a body on a
        /// point or deployed without a Cast, which the hash checks need: two
        /// worlds one field apart and nothing else.
        /// </summary>
        public void SetCastForScenario(int id, FixVec2 point, bool deployed)
        {
            Entity e = entities[id];
            e.AbilityPoint = point;
            e.Deployed = deployed;
            entities[id] = e;
        }
    }
}
