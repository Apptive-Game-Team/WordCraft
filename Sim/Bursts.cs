using System.Collections.Generic;

namespace WordCraft.Sim
{
    /// <summary>
    /// 사망 시 효과, docs/TECH-TREE.md 능력 공통 규칙 and 묶음 6: Kill and
    /// ProductionSystem only queue a burst, and BurstSystem lands every burst
    /// queued this tick. Nothing deals damage inside Kill, so a death in the
    /// middle of one system's pass never changes what the rest of that pass
    /// sees: the body beside a 찌릿쥐 that falls in CombatSystem still takes
    /// its own turn in CombatSystem.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>One queued burst: the body it came from, its owner, its ability row, and where it went off.</summary>
        private struct PendingBurst
        {
            public int SourceId;
            public int Owner;
            public int Row;
            public FixVec2 Position;
        }

        // Scratch, not state. Filled during a tick by Kill and ProductionSystem
        // and emptied by BurstSystem in the same tick, so nothing in it survives
        // a tick and nothing about it is hashed. Every Kill runs before
        // BurstSystem in Step's order; a Kill added after it would leave entries
        // behind, and the harness asserts PendingBurstCount is 0 after every step.
        private readonly List<PendingBurst> pendingBursts = new List<PendingBurst>();

        /// <summary>For the harness only: bursts still queued. Zero after every Step.</summary>
        public int PendingBurstCount => pendingBursts.Count;

        /// <summary>Called first thing in Kill: queues this body's death burst, if its row carries one.</summary>
        private void QueueDeathBurst(Entity e) => QueueBurst(e, AbilityKind.DeathBurst);

        /// <summary>Called by ProductionSystem for a body that just came off a queue.</summary>
        private void QueueArrivalBurst(int id) => QueueBurst(entities[id], AbilityKind.ArrivalBurst);

        private void QueueBurst(Entity e, AbilityKind kind)
        {
            int row = ActiveAbility(e, kind);
            if (row < 0) return;
            pendingBursts.Add(new PendingBurst { SourceId = e.Id, Owner = e.Owner, Row = row, Position = e.Position });
        }

        /// <summary>
        /// Runs after StatusSystem and before FleeSystem. Lands the bursts in the
        /// order they were queued: each deals its row's amount to every hostile
        /// body of its targets within its radius of where it went off, by
        /// ascending id. Fixed damage, per docs/TECH-TREE.md 피해 계산: armor and
        /// levels do not touch it, and DamageAfterArmor keeps only its floor of
        /// one. A body a burst kills queues its own burst at the tail, and the
        /// same pass lands it, so a chain of deaths ends inside this tick.
        /// </summary>
        private void BurstSystem()
        {
            for (int k = 0; k < pendingBursts.Count; k++)
            {
                PendingBurst burst = pendingBursts[k];
                AbilityRow row = FactionData.AbilityRowAt(burst.Row);
                Fix radius = Fix.FromInt(AbilityRadius(burst.Owner, row));
                int damage = DamageAfterArmor(AbilityAmount(burst.Owner, row), 0, 0, 0);
                for (int i = 0; i < entities.Count; i++)
                {
                    Entity t = entities[i];
                    if (!t.Alive || t.Owner < 0 || t.Owner == burst.Owner) continue;
                    if (!AuraTargets(row.Targets, t)) continue;
                    if (!WithinRange(burst.Position, t.Position, radius)) continue;
                    t.Hp -= damage;
                    if (t.Hp <= 0) Kill(ref t);
                    entities[i] = t;
                }
            }
            pendingBursts.Clear();
        }
    }
}
