namespace WordCraft.Sim
{
    /// <summary>
    /// Target acquisition, cooldown, damage, death. All timing in whole ticks.
    /// </summary>
    public sealed partial class World
    {
        private void CombatSystem()
        {
            // Attackers act in ascending id order and damage lands immediately, so
            // two units that would kill each other on the same tick resolve the same
            // way everywhere: the lower id fires first.
            for (int i = 0; i < entities.Count; i++)
            {
                Entity a = entities[i];
                if (!a.Alive || a.Kind != EntityKind.Unit) continue;

                if (a.AttackCooldown > 0) a.AttackCooldown--;

                if (!ValidTarget(a, a.TargetId)) a.TargetId = AcquireTarget(a);
                if (a.TargetId < 0) { entities[i] = a; continue; }

                // Read straight from the table rather than caching on the entity:
                // one copy of a number cannot disagree with itself across peers.
                UnitStats s = FactionData.Stats(a.Role);

                Entity t = entities[a.TargetId];
                if (WithinRange(a.Position, t.Position, s.Range))
                {
                    if (a.AttackCooldown == 0)
                    {
                        a.AttackCooldown = s.AttackTicks;
                        t.Hp -= s.Damage;
                        if (t.Hp <= 0)
                        {
                            t.Hp = 0;
                            t.Alive = false;
                        }
                        entities[t.Id] = t;
                    }
                }
                else if (PathDone(i))
                {
                    // Only chase when no move order is outstanding, so combat never
                    // overrides what the player told the unit to do.
                    a.Target = t.Position;
                }

                entities[i] = a;
            }
        }

        private bool ValidTarget(Entity attacker, int targetId)
        {
            if (targetId < 0 || targetId >= entities.Count) return false;
            Entity t = entities[targetId];
            if (!t.Alive || t.Owner == attacker.Owner) return false;
            return WithinRange(attacker.Position, t.Position, AcquireRange);
        }

        /// <summary>
        /// Nearest hostile entity inside AcquireRange. Ties break to the lower
        /// entity id, which is why the scan is a plain ascending loop and not a
        /// lookup in any hashed collection.
        /// </summary>
        // ponytail: O(n^2) across all attackers each tick. Swap in a grid bucket
        // broad phase if unit counts pass the tick budget.
        private int AcquireTarget(Entity attacker)
        {
            int best = -1;
            Fix bestDist = Fix.Zero;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity t = entities[i];
                if (!t.Alive || t.Kind == EntityKind.ResourceNode) continue;
                if (t.Owner == attacker.Owner || t.Owner < 0) continue;

                Fix d = (t.Position - attacker.Position).SqrMagnitude;
                if (d > AcquireRange * AcquireRange) continue;
                // Strictly-less: the first, lowest-id entity wins an exact tie.
                if (best < 0 || d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }
    }
}
