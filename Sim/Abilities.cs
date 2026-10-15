namespace WordCraft.Sim
{
    /// <summary>
    /// 오토캐스트, docs/TECH-TREE.md 묶음 7: the abilities that go off on their
    /// own, and the clocks they run on. Two numbers on a body are state,
    /// AbilityCooldown and AbilityTicksLeft, and a body holds at most one
    /// ability that uses them; every other ability here is a question asked of
    /// the bodies standing around, the way an aura is.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>굴러 들이받기 starts on a target at least this many cells off.</summary>
        public static readonly Fix RollMinDistance = Fix.FromInt(3);

        /// <summary>And at most this many.</summary>
        public static readonly Fix RollMaxDistance = Fix.FromInt(6);

        /// <summary>A roll ends once its target is within this many cells.</summary>
        public static readonly Fix RollEndDistance = Fix.FromInt(2);

        /// <summary>How fast a rolling body walks, cells per tick: twelve ticks of it are six cells.</summary>
        public static readonly Fix RollSpeed = Fix.Ratio(1, 2);

        /// <summary>
        /// 잔해 투척 and 잔해 수리 take debris whose cell centre is within this
        /// many cells of the 바위 술사, docs/TECH-TREE.md 돌 골렘 능력.
        /// </summary>
        public static readonly Fix RemnantUseReach = Fix.FromInt(2);

        /// <summary>
        /// Runs after ExpirySystem and before CombatSystem, docs/TECH-TREE.md
        /// 시스템 순서 10. Every body by ascending id: its cooldown comes down one
        /// tick, then the one ability with state it holds runs its clock.
        /// </summary>
        private void AbilitySystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive) continue;
                if (e.AbilityCooldown > 0)
                {
                    e.AbilityCooldown--;
                    entities[i] = e;
                }
                int row = StatefulAbility(e);
                if (row < 0) continue;
                switch (FactionData.AbilityRowAt(row).Kind)
                {
                    case AbilityKind.GaleAfterAttack: CountDownGale(i); break;
                    case AbilityKind.SteadyAim: CountStillTicks(i, row); break;
                    case AbilityKind.RollCharge: Roll(i, row); break;
                    case AbilityKind.RemnantRepair: RepairFromRemnant(i, row); break;
                }
            }
        }

        /// <summary>
        /// The ability with state this body can use right now, or -1: the one
        /// row of a stateful kind its entry holds, unlocked for its owner, on a
        /// body that can use abilities at all (alive, not growing).
        /// </summary>
        private int StatefulAbility(Entity e)
        {
            if (!CanUseAbilities(e)) return -1;
            int row = FactionData.StatefulAbilityOf(factions[e.Owner], e.Role, e.Slot);
            if (row < 0) return -1;
            return AbilityUnlocked(e.Owner, FactionData.AbilityRowAt(row)) ? row : -1;
        }

        /// <summary>
        /// The ability with state this body holds, if it is this kind and running
        /// right now (AbilityTicksLeft above 0), or -1. What MoveSpeedFor asks.
        /// </summary>
        private int RunningAbility(Entity e, AbilityKind kind)
        {
            if (e.AbilityTicksLeft == 0) return -1;
            int row = StatefulAbility(e);
            return row >= 0 && FactionData.AbilityRowAt(row).Kind == kind ? row : -1;
        }

        /// <summary>
        /// 질풍: a tick off the clock CombatSystem set. CombatSystem sets it to
        /// 10 on the tick of an attack and MoveSystem walks that tick fast, so
        /// the body walks fast on that tick and the nine after it.
        /// </summary>
        private void CountDownGale(int id)
        {
            Entity e = entities[id];
            if (e.AbilityTicksLeft == 0) return;
            e.AbilityTicksLeft--;
            entities[id] = e;
        }

        /// <summary>
        /// 정조준: one more tick stood still, up to the row's Ticks, or back to
        /// 0 for a body that is walking. Still is what 굳은 몸 means by it: the
        /// path walked and the body on its Target, as it stands after last
        /// tick's walk.
        /// </summary>
        private void CountStillTicks(int id, int row)
        {
            Entity e = entities[id];
            bool still = PathDone(id) && e.Position.Equals(e.Target);
            int limit = FactionData.AbilityRowAt(row).Ticks;
            int next = !still ? 0 : (e.AbilityTicksLeft < limit ? e.AbilityTicksLeft + 1 : limit);
            if (next == e.AbilityTicksLeft) return;
            e.AbilityTicksLeft = next;
            entities[id] = e;
        }

        /// <summary>정조준: what standing still has added to this body's reach, in cells.</summary>
        private Fix SteadyAimBonus(Entity e)
        {
            int row = StatefulAbility(e);
            if (row < 0) return Fix.Zero;
            AbilityRow r = FactionData.AbilityRowAt(row);
            if (r.Kind != AbilityKind.SteadyAim || e.AbilityTicksLeft < r.Ticks) return Fix.Zero;
            return Fix.FromInt(AbilityAmount(e.Owner, r));
        }

        /// <summary>
        /// 굴러 들이받기. A body mid-roll ends the roll when its target is gone
        /// or within RollEndDistance, or else takes a tick off and ends it when
        /// the ticks run out; on the tick it ends, every hostile ground body
        /// within the row's radius takes the row's amount once, fixed. A body
        /// not rolling, with its cooldown at 0, starts a roll when its target is
        /// RollMinDistance to RollMaxDistance cells off, and the cooldown starts
        /// with the roll. A body under Hold does not roll: it would not move.
        /// Distances are to the TargetId body as it stands now, which is where
        /// last tick's CombatSystem and MoveSystem left it.
        /// </summary>
        private void Roll(int id, int row)
        {
            Entity e = entities[id];
            AbilityRow r = FactionData.AbilityRowAt(row);
            bool target = ValidTarget(e, e.TargetId);
            if (e.AbilityTicksLeft > 0)
            {
                bool ended = !target || WithinRange(e.Position, entities[e.TargetId].Position, RollEndDistance);
                if (!ended)
                {
                    e.AbilityTicksLeft--;
                    ended = e.AbilityTicksLeft == 0;
                }
                if (ended) e.AbilityTicksLeft = 0;
                entities[id] = e;
                if (ended)
                {
                    DamageHostileBodies(e.Owner, r.Targets, e.Position, Fix.FromInt(AbilityRadius(e.Owner, r)),
                        DamageAfterArmor(AbilityAmount(e.Owner, r), 0, 0, 0));
                }
                return;
            }
            if (e.AbilityCooldown > 0 || !target || e.Mode == OrderMode.Hold || e.Speed.Raw == 0) return;
            Fix d = (entities[e.TargetId].Position - e.Position).SqrMagnitude;
            if (d < RollMinDistance * RollMinDistance || d > RollMaxDistance * RollMaxDistance) return;
            e.AbilityTicksLeft = AbilityTicks(e.Owner, r);
            e.AbilityCooldown = r.Cooldown;
            entities[id] = e;
        }

        /// <summary>
        /// The standing debris cell of lowest index whose centre is within
        /// RemnantUseReach of this point, or -1. Rows ascending and cells
        /// ascending within a row is index order, so the first found is the one.
        /// </summary>
        private int RemnantNear(FixVec2 at)
        {
            int center = CellOf(at);
            int cx = center % GridSize, cy = center / GridSize;
            int span = RemnantUseReach.ToInt() + 1;
            for (int y = cy - span; y <= cy + span; y++)
            {
                if (y < 0 || y >= GridSize) continue;
                for (int x = cx - span; x <= cx + span; x++)
                {
                    if (x < 0 || x >= GridSize) continue;
                    int cell = y * GridSize + x;
                    if (HasRemnant(cell) && WithinRange(at, CellCenter(cell), RemnantUseReach)) return cell;
                }
            }
            return -1;
        }

        /// <summary>
        /// docs/TECH-TREE.md 잔해 규칙: using debris writes its expiry to 0, so the
        /// cell opens on this tick and every walk asks WalkableStep again.
        /// </summary>
        private void ConsumeRemnant(int cell) => remnantExpiry[cell] = 0;

        /// <summary>
        /// 잔해 투척, called by CombatSystem as a 바위 술사 fires: consumes the
        /// debris RemnantNear finds and answers the row, so the shot adds the
        /// row's amount and splashes; -1, and nothing consumed, when the body
        /// has no such row or no debris is near.
        /// </summary>
        private int ThrowRemnant(Entity a)
        {
            int row = ActiveAbility(a, AbilityKind.RemnantThrow);
            if (row < 0) return -1;
            int cell = RemnantNear(a.Position);
            if (cell < 0) return -1;
            ConsumeRemnant(cell);
            return row;
        }

        /// <summary>
        /// 잔해 수리. With the cooldown at 0: the allied body of the row's
        /// targets within its radius that has lost the most hp, at least the
        /// row's Limit (lower id on a tie), and the debris RemnantNear finds.
        /// With both, the body is healed by the row's amount up to MaxHp, the
        /// debris is consumed and the cooldown starts; with either missing,
        /// nothing happens and the caster asks again next tick.
        /// </summary>
        private void RepairFromRemnant(int id, int row)
        {
            Entity e = entities[id];
            if (e.AbilityCooldown > 0) return;
            AbilityRow r = FactionData.AbilityRowAt(row);
            Fix radius = Fix.FromInt(AbilityRadius(e.Owner, r));
            int best = -1, bestLost = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity t = entities[i];
                if (!t.Alive || t.Owner != e.Owner || !AuraTargets(r.Targets, t)) continue;
                int lost = t.MaxHp - t.Hp;
                if (lost < r.Limit || lost <= bestLost) continue;
                if (!WithinRange(e.Position, t.Position, radius)) continue;
                best = i;
                bestLost = lost;
            }
            if (best < 0) return;
            int cell = RemnantNear(e.Position);
            if (cell < 0) return;

            ConsumeRemnant(cell);
            e.AbilityCooldown = r.Cooldown;
            entities[id] = e;
            Entity mended = entities[best];
            mended.Hp += AbilityAmount(e.Owner, r);
            if (mended.Hp > mended.MaxHp) mended.Hp = mended.MaxHp;
            entities[best] = mended;
        }

        /// <summary>
        /// Scenario setup only. Never call this from a system. Lets a harness put
        /// two worlds one ability clock apart, or a body in the middle of a roll,
        /// which no order can do.
        /// </summary>
        public void SetAbilityForScenario(int id, int cooldown, int ticksLeft)
        {
            Entity e = entities[id];
            e.AbilityCooldown = cooldown;
            e.AbilityTicksLeft = ticksLeft;
            entities[id] = e;
        }

        /// <summary>
        /// Scenario setup only. Never call this from a system. Lays debris on a
        /// cell without a body falling there, which is what the remnant checks
        /// need: a 바위 술사 beside debris and nothing dead on the field.
        /// </summary>
        public void SetRemnantForScenario(int cell, int expiry) => remnantExpiry[cell] = expiry;
    }
}
