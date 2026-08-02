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
                if (!a.Alive || !CanAttack(a)) continue;

                if (a.AttackCooldown > 0) a.AttackCooldown--;

                if (a.Mode == OrderMode.Attack)
                {
                    // An ordered target is held at any distance and is never traded
                    // for a nearer body: that commitment is the whole difference
                    // between an attack order and walking at the enemy. The order
                    // ends when the named target does, and not before.
                    if (!OrderedTargetAlive(a)) { a.Mode = OrderMode.None; a.TargetId = -1; }
                }
                else if (!ValidTarget(a, a.TargetId))
                {
                    a.TargetId = AcquireTarget(a);
                }

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
                            ReleasePopulation(t);
                        }
                        entities[t.Id] = t;
                    }
                }
                else if (a.Kind != EntityKind.Building && PathDone(i))
                {
                    // Only chase when no move order is outstanding, so combat never
                    // overrides what the player told the unit to do. A building never
                    // chases at all: its Target is hashed, so letting combat write one
                    // would make a turret's state depend on what walked past it.
                    a.Target = t.Position;
                }

                entities[i] = a;
            }
        }

        /// <summary>
        /// Units fight, and so do finished defense buildings. A site still under
        /// construction does not, for the same reason it takes no deliveries.
        /// </summary>
        private static bool CanAttack(Entity e) =>
            e.Kind == EntityKind.Unit ||
            (e.Kind == EntityKind.Building && e.Role == Role.Defense && e.BuildTicksLeft == 0);

        /// <summary>
        /// An ordered target only has to exist and be hostile. Deliberately no
        /// range test: the attacker walks to it, however far that is.
        /// </summary>
        private bool OrderedTargetAlive(Entity attacker)
        {
            if (attacker.TargetId < 0 || attacker.TargetId >= entities.Count) return false;
            Entity t = entities[attacker.TargetId];
            return t.Alive && t.Owner != attacker.Owner;
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
