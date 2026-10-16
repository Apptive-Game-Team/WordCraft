namespace WordCraft.Sim
{
    /// <summary>
    /// 지옥불 군단 군단장: the warlord emits one 자손 every WarlordSpawnTicks and
    /// keeps at most WarlordOffspring of them alive. It carries no weapon of its
    /// own, so what it spawns is the whole of what it is worth, and killing the
    /// offspring buys time rather than ground. Since docs/TECH-TREE.md 묶음 4 the
    /// warlord is one row of FactionData's spawner table and 산란 악마 is the
    /// second: a 잿불 새끼 every 120 ticks, three alive, on the same three fields.
    ///
    /// The living count is carried on the spawner and moved by exactly two events:
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
        /// Which entry of the signature slot 지옥불 군단장 is. Named for the reason
        /// WarlordOffspringSlot is: docs/TECH-TREE.md puts 산란 악마 and 운석 술사
        /// behind it in the same slot, and neither of them emits 자손.
        /// </summary>
        public const int WarlordSlot = 0;

        /// <summary>
        /// True for 지옥불 군단장 and nothing else. Read off faction, role and
        /// entry rather than carried on the entity, for the same reason Flies is:
        /// it never changes for a given entry, so making it state would add a
        /// hashed field that can only ever hold one value.
        ///
        /// The entry is asked as well as the role. Asked by role alone, every body
        /// docs/TECH-TREE.md adds to 지옥불's signature slot would arm a spawn
        /// clock on the tick it landed.
        /// </summary>
        public bool IsWarlord(Entity e) =>
            e.Kind == EntityKind.Unit && e.Role == WarlordRole && e.Slot == WarlordSlot &&
            e.Owner >= 0 && e.Owner < MaxPeers && factions[e.Owner] == Faction.Hellfire;

        /// <summary>
        /// The spawner row this body emits from, or a row with Spawns false.
        /// docs/TECH-TREE.md M8: 지옥불 군단장 and 산란 악마 today. Read off the
        /// owner's faction, the kind, the role and the entry, for the reason
        /// IsWarlord is: none of them changes for a given body.
        /// </summary>
        public SpawnerRow SpawnerOf(Entity e)
        {
            if (e.Kind != EntityKind.Unit || e.Owner < 0 || e.Owner >= MaxPeers) return new SpawnerRow();
            return FactionData.Spawner(factions[e.Owner], e.Role, e.Slot);
        }

        /// <summary>
        /// Starts a newly spawned spawner's clock at a full period, so its first
        /// child is due one period after it lands rather than on the tick it
        /// arrives. Called from SpawnUnit, which is the one way a unit enters the
        /// world. A body that grows into a spawner would not pass through here;
        /// no growth row names one.
        /// </summary>
        private void ArmSpawner(int id)
        {
            Entity e = entities[id];
            SpawnerRow row = SpawnerOf(e);
            if (!row.Spawns) return;
            e.SpawnCooldown = SpawnTicksFor(e.Owner, row);
            entities[id] = e;
        }

        /// <summary>
        /// The spawner table's system, which was WarlordSpawnSystem while the
        /// warlord was its only row. Runs after production and before combat, so
        /// a child emitted this tick fights this tick exactly as a unit finished
        /// off a queue does. By ascending entity id, so two spawners emitting on
        /// the same tick hand out their ids in the same order on every peer.
        ///
        /// One rule for every row, the warlord's as it always was: the period
        /// restarts whether or not there was room, the population is counted but
        /// its cap is not asked, and a dead child gives its slot back through
        /// ReleaseOffspringSlot.
        /// </summary>
        private void SpawnerSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity w = entities[i];
                if (!w.Alive) continue;
                SpawnerRow row = SpawnerOf(w);
                if (!row.Spawns) continue;

                bool emits = false;
                w.SpawnCooldown--;
                if (w.SpawnCooldown <= 0)
                {
                    // The period restarts whether or not there was room. A clock
                    // that stopped at the cap would refill a killed child on the
                    // next tick, and killing one would buy nothing at all.
                    w.SpawnCooldown = SpawnTicksFor(w.Owner, row);
                    emits = w.OffspringCount < SpawnCapFor(w.Owner, row);
                    if (emits) w.OffspringCount++;
                }

                // Written back before the spawn, because SpawnUnit appends to the
                // same list this loop is walking.
                entities[i] = w;
                if (emits) SpawnChild(w, row);
            }
        }

        /// <summary>
        /// The period of one spawner row for this peer: the row's ticks, or the
        /// researched ticks once the row's research is done. Read when a clock is
        /// armed or restarted, never mid-count.
        /// </summary>
        public int SpawnTicksFor(int peer, SpawnerRow row) =>
            row.TicksEffect != UpgradeEffect.None && UpgradeLevelOf(peer, row.TicksEffect) > 0
                ? row.TicksResearched
                : row.Ticks;

        /// <summary>
        /// The cap of one spawner row for this peer. Read on the emitting tick, so
        /// a level that lands raises the cap of every spawner already standing on
        /// its next period.
        /// </summary>
        public int SpawnCapFor(int peer, SpawnerRow row) =>
            row.CapEffect != UpgradeEffect.None && UpgradeLevelOf(peer, row.CapEffect) > 0
                ? row.CapResearched
                : row.Cap;

        /// <summary>
        /// 군단 확장: the most 자손 one of this peer's warlords keeps alive. The
        /// warlord's row of the spawner table, read through SpawnCapFor.
        /// </summary>
        public int WarlordOffspringCap(int peer) =>
            SpawnCapFor(peer, FactionData.Spawner(Faction.Hellfire, WarlordRole, WarlordSlot));

        /// <summary>
        /// 산란 가속: the period between 자손 for this peer's warlords. The
        /// warlord's row of the spawner table, read through SpawnTicksFor.
        /// </summary>
        public int WarlordSpawnTicksFor(int peer) =>
            SpawnTicksFor(peer, FactionData.Spawner(Faction.Hellfire, WarlordRole, WarlordSlot));

        /// <summary>
        /// A child comes out free, at a fixed offset from its parent so the spawn
        /// point never depends on how many already stand there, and it counts
        /// against its owner's population like any other body. The population cap
        /// does not hold it back: the mechanic is a spawner that keeps spawning,
        /// and a cap that silently stopped it would be a second rule nobody can see.
        /// </summary>
        private void SpawnChild(Entity parent, SpawnerRow row)
        {
            int id = SpawnUnit(parent.Owner, row.ChildRole, row.ChildSlot, parent.Position + RallyOffset);
            Entity child = entities[id];
            child.ParentId = parent.Id;
            entities[id] = child;
        }

        /// <summary>
        /// Gives a dead child's slot back to the spawner that emitted it, a 군단장
        /// or a 산란 악마. Called from Kill, which is the one place a body stops
        /// existing, so a second way to kill something cannot forget it.
        ///
        /// Writing straight into the parent's slot is safe because a spawner and
        /// its children share an owner, and combat only ever kills across owners:
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
