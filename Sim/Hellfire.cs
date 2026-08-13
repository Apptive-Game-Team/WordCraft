namespace WordCraft.Sim
{
    /// <summary>
    /// 지옥불 군단 군단장: the warlord emits one 자손 every WarlordSpawnTicks and
    /// keeps at most WarlordOffspring of them alive. It carries no weapon of its
    /// own, so what it spawns is the whole of what it is worth, and killing the
    /// offspring buys time rather than ground.
    ///
    /// The living count is carried on the warlord and moved by exactly two events:
    /// an emission raises it, a death lowers it. Never a scan. A scan would be a
    /// pass over every entity per warlord per tick, and worse than the cost, it
    /// would put iteration order inside a number that is hashed.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>The slot 지옥불 군단장 occupies in the roster.</summary>
        public const Role WarlordRole = Role.Signature;

        /// <summary>The slot 군단장의 자손 occupies. Spawned only; nothing produces it.</summary>
        public const Role WarlordOffspringRole = Role.Ranged;

        /// <summary>
        /// Which entry of that slot 자손 is. Named rather than left to default,
        /// because the entry behind it is 균열 파수병: a ground unit a 지옥불
        /// player buys and can lose. A warlord emitting that one for free every
        /// hundred ticks is a different game, and nothing about the spawn site
        /// would have said which one it meant.
        /// </summary>
        public const int WarlordOffspringSlot = 0;

        /// <summary>
        /// True for 지옥불 군단장 and nothing else. Read off faction and role
        /// rather than carried on the entity, for the same reason Flies is: it
        /// never changes for a given faction and role, so making it state would add
        /// a hashed field that can only ever hold one value.
        /// </summary>
        public bool IsWarlord(Entity e) =>
            e.Kind == EntityKind.Unit && e.Role == WarlordRole &&
            e.Owner >= 0 && e.Owner < MaxPeers && factions[e.Owner] == Faction.Hellfire;

        /// <summary>
        /// Starts a newly spawned warlord's clock at a full period, so its first
        /// offspring is due WarlordSpawnTicks after it lands rather than on the
        /// tick it arrives. Called from SpawnUnit, which is the one way a unit
        /// enters the world.
        /// </summary>
        private void ArmWarlord(int id)
        {
            Entity e = entities[id];
            if (!IsWarlord(e)) return;
            e.SpawnCooldown = WarlordSpawnTicks;
            entities[id] = e;
        }

        /// <summary>
        /// Runs after production and before combat, so an offspring emitted this
        /// tick fights this tick exactly as a unit finished off a queue does. By
        /// ascending entity id, so two warlords emitting on the same tick hand out
        /// their ids in the same order on every peer.
        /// </summary>
        private void WarlordSpawnSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity w = entities[i];
                if (!w.Alive || !IsWarlord(w)) continue;

                bool emits = false;
                w.SpawnCooldown--;
                if (w.SpawnCooldown <= 0)
                {
                    // The period restarts whether or not there was room. A clock
                    // that stopped at the cap would refill a killed offspring on
                    // the next tick, and killing one would buy nothing at all.
                    w.SpawnCooldown = WarlordSpawnTicks;
                    emits = w.OffspringCount < WarlordOffspring;
                    if (emits) w.OffspringCount++;
                }

                // Written back before the spawn, because SpawnUnit appends to the
                // same list this loop is walking.
                entities[i] = w;
                if (emits) SpawnOffspring(w);
            }
        }

        /// <summary>
        /// 자손 comes out free, at a fixed offset from its parent so the spawn point
        /// never depends on how many already stand there, and it counts against its
        /// owner's population like any other body. The population cap does not hold
        /// it back: the mechanic is a warlord that keeps spawning, and a cap that
        /// silently stopped it would be a second rule nobody can see.
        /// </summary>
        private void SpawnOffspring(Entity warlord)
        {
            int id = SpawnUnit(warlord.Owner, WarlordOffspringRole, WarlordOffspringSlot,
                warlord.Position + RallyOffset);
            Entity child = entities[id];
            child.ParentId = warlord.Id;
            entities[id] = child;
        }

        /// <summary>
        /// Gives a dead offspring's slot back to the warlord that emitted it.
        /// Called from Kill, which is the one place a body stops existing, so a
        /// second way to kill something cannot forget it.
        ///
        /// Writing straight into the parent's slot is safe because a warlord and
        /// its offspring share an owner, and combat only ever kills across owners:
        /// no attacker can be the parent of what it just killed, so the write-back
        /// at the end of the combat loop can never be holding a stale count.
        /// </summary>
        private void ReleaseOffspringSlot(Entity e)
        {
            if (e.ParentId < 0 || e.ParentId >= entities.Count) return;
            Entity parent = entities[e.ParentId];
            if (parent.OffspringCount <= 0) return;
            parent.OffspringCount--;
            entities[e.ParentId] = parent;
        }
    }
}
