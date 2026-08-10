namespace WordCraft.Sim
{
    /// <summary>
    /// Target acquisition, cooldown, damage, death. All timing in whole ticks.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// 물 슬라임 일제 사격: how far an allied archer counts from, in cells.
        /// </summary>
        public static readonly Fix VolleyRadius = Fix.FromInt(3);

        /// <summary>Most damage the massed bonus can ever add, per docs/FACTION-MECHANICS.md.</summary>
        public const int VolleyMaxBonus = 4;

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

                if (a.TargetId < 0)
                {
                    // Nothing hostile left in range, so an attack-move goes back to
                    // being a move. Retarget repaths only when the destination is
                    // not already the standing one, so an arrived unit costs nothing.
                    if (a.Mode == OrderMode.AttackMove) Retarget(i, ref a, a.OrderPoint);
                    entities[i] = a;
                    continue;
                }

                // Read straight from the table rather than caching on the entity:
                // one copy of a number cannot disagree with itself across peers.
                UnitStats s = FactionData.Stats(factions[a.Owner], a.Role);

                Entity t = entities[a.TargetId];
                if (WithinRange(a.Position, t.Position, s.Range))
                {
                    if (a.AttackCooldown == 0)
                    {
                        a.AttackCooldown = s.AttackTicks;
                        t.Hp -= s.Damage + VolleyBonus(a);
                        if (t.Hp <= 0) Kill(ref t);
                        entities[t.Id] = t;
                    }
                }
                else if (a.Kind != EntityKind.Building && a.Mode != OrderMode.Hold &&
                         (a.Mode == OrderMode.AttackMove || PathDone(i)))
                {
                    // A holder is left out for the same reason a building is: Target
                    // is hashed, and a chase written here would move it.
                    //
                    // Only chase when no move order is outstanding, so combat never
                    // overrides what the player told the unit to do. A building never
                    // chases at all: its Target is hashed, so letting combat write one
                    // would make a turret's state depend on what walked past it.
                    //
                    // An attack-move is the one order that wants to be overridden:
                    // breaking off the route to close is the whole point. Its route
                    // is dropped rather than recomputed, so pursuit stays a straight
                    // line and costs no pathfinding per tick; OrderPoint is what
                    // rebuilds the route once the fighting is over.
                    if (a.Mode == OrderMode.AttackMove)
                    {
                        paths[i].Clear();
                        a.PathIndex = 0;
                    }
                    a.Target = t.Position;
                }

                entities[i] = a;
            }
        }

        /// <summary>
        /// 물 슬라임 일제 사격: +1 damage for every other allied archer standing
        /// within VolleyRadius, up to VolleyMaxBonus. Formation is firepower.
        ///
        /// Counted at the moment of the shot rather than carried on the entity, so
        /// the mechanic adds no hashed field at all: a cached bonus would be state
        /// that has to be refreshed everywhere a unit moves or dies, and the tick
        /// one peer forgot is the tick the two worlds part company.
        /// </summary>
        // ponytail: a linear scan per shot, so O(n^2) across a tick of archers.
        // That is the ceiling AcquireTarget already lives under, and both go to a
        // grid bucket broad phase on the same day.
        private int VolleyBonus(Entity archer)
        {
            if (archer.Kind != EntityKind.Unit || archer.Role != Role.Ranged) return 0;
            if (archer.Owner < 0 || archer.Owner >= MaxPeers) return 0;
            if (factions[archer.Owner] != Faction.WaterSlimes) return 0;

            // Ascending entity id, never a keyed collection. A count does not care
            // about order, but the loop that produces it is not allowed to become
            // one that does, and the cap makes it care: stopping early has to stop
            // on the same archer everywhere.
            int allies = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity o = entities[i];
                if (!o.Alive || o.Id == archer.Id) continue;
                if (o.Owner != archer.Owner) continue;
                if (o.Kind != EntityKind.Unit || o.Role != Role.Ranged) continue;
                if (!WithinRange(o.Position, archer.Position, VolleyRadius)) continue;
                if (++allies == VolleyMaxBonus) break;
            }
            return allies;
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
