namespace WordCraft.Sim
{
    /// <summary>
    /// 한시 소환, docs/TECH-TREE.md 묶음 6: a body whose roster row carries a
    /// Lifetime lapses that many ticks after it is spawned. One number is state,
    /// Entity.ExpireTick on the body itself; how long a row lives is static.
    /// A lapse is a death and goes through Kill, so it gives the population back
    /// and sets off whatever a death sets off, exactly as a killing blow does.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// How long a body of this row lives when this peer spawns it, or 0 for
        /// a row that lives until it is killed. 소환 수명 adds what it adds to
        /// SummonLifetime (600 → 800, so 200) to the row's own lifetime, so a row
        /// given a lifetime of its own keeps its difference from the others
        /// rather than being flattened to 800. Read at spawn, so a body already
        /// on the field keeps the tick it was given.
        /// </summary>
        public int LifetimeFor(int peer, UnitStats s)
        {
            if (s.Lifetime == 0) return 0;
            return UpgradeLevelOf(peer, UpgradeEffect.SummonLifetime) > 0
                ? s.Lifetime + (FactionData.SummonLifetimeResearched - FactionData.SummonLifetime)
                : s.Lifetime;
        }

        /// <summary>
        /// Called from SpawnUnit, the one way a unit enters the world, so every
        /// body a 통로 lets through, a spawner emits or a scenario lays out gets
        /// its clock on the tick it appears. 도착 틱에 ExpireTick = Tick + Lifetime:
        /// a body waiting in a queue does not exist yet, so its clock is not running.
        /// </summary>
        private void ArmLifetime(int id, UnitStats s)
        {
            Entity e = entities[id];
            if (e.Owner < 0 || e.Owner >= MaxPeers) return;
            int lifetime = LifetimeFor(e.Owner, s);
            if (lifetime == 0) return;
            e.ExpireTick = Tick + lifetime;
            entities[id] = e;
        }

        /// <summary>
        /// Runs after SpawnerSystem and before CombatSystem. First 층위 고정 puts
        /// back the clocks it reaches, then every body whose ExpireTick has come,
        /// by ascending id, is killed. A body that lapses this tick does not
        /// fight this tick. Tick at or past rather than equal, so a body cannot
        /// outlive its tick by having been skipped once.
        /// </summary>
        private void ExpirySystem()
        {
            HoldExpiry();
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.ExpireTick == 0 || Tick < e.ExpireTick) continue;
                Kill(ref e);
                entities[i] = e;
            }
        }

        /// <summary>
        /// 층위 고정, before the expiry check: on a tick its row's period divides,
        /// every allied body that lapses and that a 층위 관측자 reaches has its
        /// ExpireTick put back one tick, by ascending id. Once per body however
        /// many observers reach it: auras do not stack. Every second tick, so the
        /// body lapses at half speed for as long as it stays in reach.
        /// </summary>
        private void HoldExpiry()
        {
            bool due = false;
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                AbilityRow row = FactionData.AbilityRowAt(r);
                if (row.Kind == AbilityKind.ExpiryAura && Tick % row.Period == 0) due = true;
            }
            if (!due) return;

            CollectAuraSources();
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.ExpireTick == 0) continue;
                for (int r = 0; r < FactionData.AbilityRowCount; r++)
                {
                    AbilityRow row = FactionData.AbilityRowAt(r);
                    if (row.Kind != AbilityKind.ExpiryAura || Tick % row.Period != 0) continue;
                    if (!AlliedAuraReaches(r, e, e.Owner)) continue;
                    e.ExpireTick++;
                    entities[i] = e;
                    break;
                }
            }
        }

        /// <summary>
        /// Scenario setup only. Never call this from a system. Lets a harness put
        /// two worlds one expiry tick apart, which no order can do.
        /// </summary>
        public void SetExpireTickForScenario(int id, int expireTick)
        {
            Entity e = entities[id];
            e.ExpireTick = expireTick;
            entities[id] = e;
        }
    }
}
