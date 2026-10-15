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
        /// every speed under one cell, which WalkableStep and the separation
        /// push clamp both assume.
        /// </summary>
        public static readonly Fix MaxMoveSpeed = Fix.Ratio(3, 4);

        // The aura sources standing at the start of the pass that asks, as pairs
        // of entity id and ability row, in ascending id. Scratch, not state: it
        // is rebuilt from the entities at the start of every pass that reads it
        // and read only inside that pass, so nothing about it survives a tick
        // and nothing about it needs hashing. A source killed later in the same
        // pass is skipped by its Alive flag at the moment it is asked.
        private readonly List<int> auraIds = new List<int>();
        private readonly List<int> auraRows = new List<int>();

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

        private static bool IsAura(AbilityKind kind) =>
            kind == AbilityKind.ArmorAura || kind == AbilityKind.MarkAura || kind == AbilityKind.RateAura ||
            kind == AbilityKind.HealAura || kind == AbilityKind.BurnAura || kind == AbilityKind.GrowthAura ||
            kind == AbilityKind.SlowAura;

        /// <summary>
        /// Fills the scratch source list: every body that can use an aura row
        /// right now, by ascending id, and within one body by table order.
        /// </summary>
        private void CollectAuraSources()
        {
            auraIds.Clear();
            auraRows.Clear();
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!CanUseAbilities(e)) continue;
                Faction faction = factions[e.Owner];
                for (int r = 0; r < FactionData.AbilityRowCount; r++)
                {
                    AbilityRow row = FactionData.AbilityRowAt(r);
                    if (row.Faction != faction || row.Role != e.Role || row.Slot != e.Slot) continue;
                    if (!IsAura(row.Kind) || !AbilityUnlocked(e.Owner, row)) continue;
                    auraIds.Add(i);
                    auraRows.Add(r);
                }
            }
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
                if (WithinRange(s.Position, t.Position, Fix.FromInt(AbilityRadius(s.Owner, r)))) return true;
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
                if (WithinRange(s.Position, t.Position, Fix.FromInt(AbilityRadius(s.Owner, r)))) return true;
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
    }
}
