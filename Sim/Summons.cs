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
        /// a row that lives until it is killed. 소환 수명 raises every row that
        /// lapses to SummonLifetimeResearched; read at spawn, so a body already
        /// on the field keeps the tick it was given.
        /// </summary>
        public int LifetimeFor(int peer, UnitStats s)
        {
            if (s.Lifetime == 0) return 0;
            return UpgradeLevelOf(peer, UpgradeEffect.SummonLifetime) > 0
                ? FactionData.SummonLifetimeResearched
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
        /// Runs after SpawnerSystem and before CombatSystem: every body whose
        /// ExpireTick has come, by ascending id, is killed. A body that lapses
        /// this tick does not fight this tick. Tick at or past rather than equal,
        /// so a body cannot outlive its tick by having been skipped once.
        /// </summary>
        private void ExpirySystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.ExpireTick == 0 || Tick < e.ExpireTick) continue;
                Kill(ref e);
                entities[i] = e;
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
