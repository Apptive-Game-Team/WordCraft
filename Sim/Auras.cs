using System.Collections.Generic;

namespace WordCraft.Sim
{
    /// <summary>
    /// The ability table read at the moment a number is needed, docs/TECH-TREE.md
    /// 능력 공통 규칙: whether a body's ability is unlocked, what a research has
    /// done to its numbers, and whether an aura reaches a given body.
    ///
    /// Auras do not stack. A body is inside an aura when any one source of that
    /// row reaches it, and the effect lands once however many do. 화상 오라 is
    /// the one exception and is run per source by StatusSystem.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>What a slowed body's speed is multiplied by: 휘감기 and 덩굴 지대 alike, and once even under both.</summary>
        public static readonly Fix SlowFactor = Fix.Ratio(1, 2);

        /// <summary>
        /// No body walks faster than this, whatever multiplies its speed. Keeps
        /// every step under one cell, which WalkableStep assumes. The separation
        /// push is clamped to half the hashed e.Speed, not to this speed, so a
        /// slowed body is pushed as far as it would be unslowed: the push answers
        /// how much ground two footprints overlap, not how fast the body walks,
        /// and half of any roster speed is already under half a cell.
        /// </summary>
        public static readonly Fix MaxMoveSpeed = Fix.Ratio(3, 4);

        // The aura sources standing at the start of the pass that asks: entity
        // id, ability row and the position the source stood on then, in
        // ascending id. Scratch, not state: it is rebuilt from the entities at
        // the start of every pass that reads it and read only inside that pass,
        // so nothing about it survives a tick and nothing about it needs hashing.
        //
        // The position is taken once because a source can walk inside the pass
        // that asks: a 덩굴 군집 steps in MoveSystem, and a body with a lower id
        // would measure it before that step and one with a higher id after. Every
        // body measures from where the source stood when the pass began. Life is
        // still read live: a source killed later in the same pass is skipped by
        // its Alive flag at the moment it is asked.
        private readonly List<int> auraIds = new List<int>();
        private readonly List<int> auraRows = new List<int>();
        private readonly List<FixVec2> auraPositions = new List<FixVec2>();

        /// <summary>Whether this peer can use this ability: no unlock research, or its level is above 0.</summary>
        public bool AbilityUnlocked(int peer, AbilityRow row) =>
            row.Unlock < 0 || UpgradeLevel(peer, row.Unlock) > 0;

        private bool Boosted(int peer, AbilityRow row) =>
            row.Boost >= 0 && UpgradeLevel(peer, row.Boost) > 0;

        /// <summary>This ability's reach in cells for this peer: the boosted radius once its research lands.</summary>
        public int AbilityRadius(int peer, AbilityRow row) =>
            Boosted(peer, row) && row.BoostedRadius > 0 ? row.BoostedRadius : row.Radius;

        /// <summary>This ability's amount for this peer: the boosted amount once its research lands.</summary>
        public int AbilityAmount(int peer, AbilityRow row) =>
            Boosted(peer, row) && row.BoostedAmount > 0 ? row.BoostedAmount : row.Amount;

        /// <summary>This ability's duration for this peer: the boosted duration once its research lands.</summary>
        public int AbilityTicks(int peer, AbilityRow row) =>
            Boosted(peer, row) && row.BoostedTicks > 0 ? row.BoostedTicks : row.Ticks;

        /// <summary>
        /// The ability row of this kind this body can use right now, or -1. A
        /// body uses an ability when it is alive and owned, its entry holds a row
        /// of the kind, the row is unlocked for its owner, it is not growing (성장
        /// 중에는 아무것도 못 한다), and a building is finished.
        /// </summary>
        private int ActiveAbility(Entity e, AbilityKind kind)
        {
            if (!CanUseAbilities(e)) return -1;
            int row = FactionData.AbilityOf(factions[e.Owner], e.Role, e.Slot, kind);
            if (row < 0) return -1;
            return AbilityUnlocked(e.Owner, FactionData.AbilityRowAt(row)) ? row : -1;
        }

        private bool CanUseAbilities(Entity e) =>
            e.Alive && e.Owner >= 0 && e.Owner < MaxPeers && !IsGrowing(e) &&
            (e.Kind != EntityKind.Building || e.BuildTicksLeft == 0) &&
            e.Kind != EntityKind.ResourceNode;

        /// <summary>
        /// Fills the scratch source list: every body that can use an aura row
        /// right now, by ascending id, and within one body by table order.
        /// </summary>
        private void CollectAuraSources()
        {
            auraIds.Clear();
            auraRows.Clear();
            auraPositions.Clear();
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!CanUseAbilities(e)) continue;
                Faction faction = factions[e.Owner];
                for (int r = 0; r < FactionData.AbilityRowCount; r++)
                {
                    AbilityRow row = FactionData.AbilityRowAt(r);
                    if (row.Faction != faction || row.Role != e.Role || row.Slot != e.Slot) continue;
                    if (!FactionData.IsAura(row.Kind) || !AbilityUnlocked(e.Owner, row)) continue;
                    auraIds.Add(i);
                    auraRows.Add(r);
                    auraPositions.Add(e.Position);
                }
            }
            // 모래폭풍 zones are read in the same passes, so they are gathered here too.
            CollectSandStorms();
        }

        /// <summary>Whether this body is one an aura of these targets reaches, before the question of distance and side.</summary>
        private bool AuraTargets(AbilityTargets targets, Entity t)
        {
            bool body = t.Kind == EntityKind.Unit || t.Kind == EntityKind.Worker;
            bool finished = t.Kind == EntityKind.Building && t.BuildTicksLeft == 0;
            switch (targets)
            {
                case AbilityTargets.Bodies: return body;
                case AbilityTargets.BodiesAndBuildings: return body || finished;
                case AbilityTargets.RangedBodies: return t.Kind == EntityKind.Unit && t.Role == Role.Ranged;
                case AbilityTargets.DefenseBuildings: return finished && t.Role == Role.Defense;
                case AbilityTargets.GroundBodies: return body && !Flies(t);
                case AbilityTargets.Ground: return (body || t.Kind == EntityKind.Building) && !Flies(t);
                case AbilityTargets.Everything: return body || t.Kind == EntityKind.Building;
                case AbilityTargets.BuildingsAndDevices:
                    return finished || (t.Kind == EntityKind.Unit && t.Owner >= 0 && t.Owner < MaxPeers &&
                                        FactionData.IsDevice(factions[t.Owner], t.Role, t.Slot));
                default: return false;
            }
        }

        /// <summary>
        /// Whether a source of this row owned by this peer reaches this body. The
        /// first source found answers, and one is all it takes: auras do not stack.
        /// </summary>
        private bool AlliedAuraReaches(int row, Entity t, int peer)
        {
            AbilityRow r = FactionData.AbilityRowAt(row);
            if (!AuraTargets(r.Targets, t)) return false;
            for (int k = 0; k < auraIds.Count; k++)
            {
                if (auraRows[k] != row) continue;
                Entity s = entities[auraIds[k]];
                if (!s.Alive || s.Owner != peer) continue;
                if (WithinRange(auraPositions[k], t.Position, Fix.FromInt(AbilityRadius(s.Owner, r)))) return true;
            }
            return false;
        }

        /// <summary>Whether a source of this row hostile to this body reaches it. Neutral bodies are nobody's enemy.</summary>
        private bool HostileAuraReaches(int row, Entity t)
        {
            if (t.Owner < 0) return false;
            AbilityRow r = FactionData.AbilityRowAt(row);
            if (!AuraTargets(r.Targets, t)) return false;
            for (int k = 0; k < auraIds.Count; k++)
            {
                if (auraRows[k] != row) continue;
                Entity s = entities[auraIds[k]];
                if (!s.Alive || s.Owner == t.Owner) continue;
                if (WithinRange(auraPositions[k], t.Position, Fix.FromInt(AbilityRadius(s.Owner, r)))) return true;
            }
            return false;
        }

        /// <summary>Whether any hostile row of this kind reaches this body.</summary>
        private bool InHostileAura(AbilityKind kind, Entity t)
        {
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                if (FactionData.AbilityRowAt(r).Kind == kind && HostileAuraReaches(r, t)) return true;
            }
            return false;
        }

        /// <summary>
        /// The ticks this attacker waits after shooting this target: AttackTicksFor,
        /// then 피 냄새 (its own period while the target is under half hp), then
        /// each allied rate aura that reaches it (집결 for bodies, 증폭 for finished
        /// defense buildings), each once however many sources reach. A rate aura
        /// takes its amount off and stops at its floor, and never makes a period
        /// longer than it was.
        /// </summary>
        private int AttackTicksAgainst(Entity a, Entity t)
        {
            int ticks = AttackTicksFor(a);
            int scent = ActiveAbility(a, AbilityKind.WoundedRate);
            if (scent >= 0 && t.Hp * 2 < t.MaxHp)
            {
                int wounded = AbilityAmount(a.Owner, FactionData.AbilityRowAt(scent));
                if (wounded < ticks) ticks = wounded;
            }
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                AbilityRow row = FactionData.AbilityRowAt(r);
                if (row.Kind != AbilityKind.RateAura || !AlliedAuraReaches(r, a, a.Owner)) continue;
                int faster = ticks - AbilityAmount(a.Owner, row);
                if (faster < row.Limit) faster = row.Limit;
                if (faster < ticks) ticks = faster;
            }
            return ticks;
        }

        /// <summary>
        /// 표식: what a hostile mark aura adds to this shot. The attacker's own
        /// peer has to own the source, so it is the marking side's weapons that
        /// hit harder, and only bodies are marked. Once however many reach.
        /// </summary>
        private int MarkBonus(Entity a, Entity t)
        {
            if (t.Owner < 0) return 0;
            int bonus = 0;
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                AbilityRow row = FactionData.AbilityRowAt(r);
                if (row.Kind != AbilityKind.MarkAura || !AlliedAuraReaches(r, t, a.Owner)) continue;
                bonus += AbilityAmount(a.Owner, row);
            }
            return bonus;
        }

        /// <summary>
        /// The armor auras and passives add to this body at the moment it is hit:
        /// 물막이 once if any allied source reaches it, 거품 벽 one per other
        /// 거품 정령 of its owner within the radius up to the cap, counted by
        /// ascending id as 일제 사격 is, and 굳은 몸 while it is not walking (its
        /// path walked and its position on its Target).
        /// </summary>
        private int AuraArmor(Entity t)
        {
            if (t.Owner < 0 || t.Owner >= MaxPeers) return 0;
            int armor = 0;
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                AbilityRow row = FactionData.AbilityRowAt(r);
                if (row.Kind != AbilityKind.ArmorAura || !AlliedAuraReaches(r, t, t.Owner)) continue;
                armor += AbilityAmount(t.Owner, row);
            }

            int wall = ActiveAbility(t, AbilityKind.ArmorPerAlly);
            if (wall >= 0)
            {
                AbilityRow row = FactionData.AbilityRowAt(wall);
                Fix radius = Fix.FromInt(AbilityRadius(t.Owner, row));
                int allies = 0;
                for (int i = 0; i < entities.Count && allies < row.Limit; i++)
                {
                    Entity o = entities[i];
                    if (!o.Alive || o.Id == t.Id || o.Owner != t.Owner) continue;
                    if (o.Kind != t.Kind || o.Role != t.Role || o.Slot != t.Slot) continue;
                    if (WithinRange(o.Position, t.Position, radius)) allies++;
                }
                armor += allies * AbilityAmount(t.Owner, row);
            }

            int still = ActiveAbility(t, AbilityKind.ArmorWhileStill);
            if (still >= 0 && PathDone(t.Id) && t.Position.Equals(t.Target))
                armor += AbilityAmount(t.Owner, FactionData.AbilityRowAt(still));
            return armor;
        }

        /// <summary>
        /// Fixed damage around a point: every living body of these targets that
        /// some peer other than this one owns, within the radius, by ascending
        /// id, and Kill for each it brings to 0. Neutral bodies are nobody's
        /// enemy. What 화상 오라 and the bursts share; the caller has already
        /// decided the amount, so no armor or level is asked here.
        ///
        /// An area hit (묶음 7) passes the shooter and the body its shot struck:
        /// the struck body is skipped, it took the full hit already, and only
        /// what the shooter's weapon can reach is hit, per docs/TECH-TREE.md 광역.
        /// </summary>
        private void DamageHostileBodies(int owner, AbilityTargets targets, FixVec2 from, Fix radius, int damage,
            int shooterId = -1, int struckId = -1)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity t = entities[i];
                if (!t.Alive || t.Owner < 0 || t.Owner == owner || i == struckId) continue;
                if (!AuraTargets(targets, t)) continue;
                if (shooterId >= 0 && !CanHit(entities[shooterId], t)) continue;
                if (!WithinRange(from, t.Position, radius)) continue;
                t.Hp -= damage;
                if (t.Hp <= 0) Kill(ref t);
                entities[i] = t;
            }
        }
    }
}
