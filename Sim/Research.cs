namespace WordCraft.Sim
{
    /// <summary>
    /// 업그레이드 연구: a finished 기술 건물 takes a Research order naming a row of
    /// its owner's upgrade table, charges for the next level in full, counts the
    /// row's ticks down, and raises that row's level for the whole peer.
    ///
    /// Three things are state, and only three: which row a building is
    /// researching, how long it has left, and each peer's level in each row. All
    /// three are hashed. What a level does is not state at all. Every effect is a
    /// question the system that owns the number asks at the moment it needs it —
    /// AttackBonus in the shot, CaptureTicksFor when a capture clock starts — and
    /// the answer is computed from the level. A bonus cached on the bodies it
    /// applies to would have to be refreshed everywhere a body is born, changes
    /// hands or grows, and the tick one peer forgot is the tick the worlds part.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// What one level of 공격력 adds to every hit a peer's weapons land, and
        /// what one level of 방어력 takes off every hit a peer's bodies receive.
        /// </summary>
        public const int AttackPerLevel = 1;
        public const int ArmorPerLevel = 1;

        /// <summary>
        /// Each peer's level in each row of its own faction's table, peer major.
        /// Sized to the widest table, so a faction with fewer rows simply never
        /// writes the tail. Hashed every tick, by peer index and then by row.
        /// </summary>
        private readonly int[,] upgradeLevels = new int[MaxPeers, FactionData.MaxUpgrades];

        /// <summary>This peer's level in one row of its table, or 0 for anything out of range.</summary>
        public int UpgradeLevel(int peer, int index)
        {
            if (peer < 0 || peer >= MaxPeers) return 0;
            if (index < 0 || index >= FactionData.MaxUpgrades) return 0;
            return upgradeLevels[peer, index];
        }

        /// <summary>
        /// This peer's level in the row with this effect, or 0 when its faction
        /// has no such row. That zero is what keeps every effect method below
        /// from needing a faction test of its own: 인간 has no OffspringCap row,
        /// so a 인간 peer's offspring cap is the base one, and nothing has to say so.
        ///
        /// A scan of at most four rows, in index order.
        /// </summary>
        public int UpgradeLevelOf(int peer, UpgradeEffect effect)
        {
            if (peer < 0 || peer >= MaxPeers) return 0;
            Faction faction = factions[peer];
            int count = FactionData.UpgradeCount(faction);
            for (int i = 0; i < count; i++)
            {
                if (FactionData.Upgrade(faction, i).Effect == effect) return upgradeLevels[peer, i];
            }
            return 0;
        }

        /// <summary>
        /// What the next level of this row costs this peer: level 1's price times
        /// the level being bought. 1단계 기본가, 2단계 두 배, 3단계 세 배. The clock
        /// does not scale; only the price does.
        /// </summary>
        public int ResearchCost(int peer, int index)
        {
            if (peer < 0 || peer >= MaxPeers) return 0;
            return FactionData.Upgrade(factions[peer], index).Resources * (UpgradeLevel(peer, index) + 1);
        }

        /// <summary>
        /// Every rule a Research has to pass, and the only place they are written.
        /// Public for the reason CanBuild is: a view will grey out a research card
        /// with it. The answer that counts is the one taken during Apply.
        ///
        /// In the order Produce asks its own: whose and alive, what kind of
        /// building, whether it is busy, whether the row can go any higher,
        /// whether this is the building the row is researched in, the tier, the
        /// building the row requires, and last the price, so a refusal for any
        /// other reason never depends on what the peer happens to have banked.
        /// </summary>
        public bool CanResearch(int peer, int buildingId, int index)
        {
            if (peer < 0 || peer >= MaxPeers) return false;
            if (!OwnedAndAlive(buildingId, peer)) return false;
            Entity b = entities[buildingId];
            if (b.Kind != EntityKind.Building || b.BuildTicksLeft > 0) return false;
            // One row at a time per building. Research has no queue: a second
            // order is refused rather than lined up, so there is never a list
            // of paid-for levels for a death or a cancel to unwind.
            if (b.ResearchIndex >= 0) return false;

            // An index past the table answers a row with MaxLevel 0, so this one
            // line refuses both a row at its last level and a row that is not there.
            UpgradeRow row = FactionData.Upgrade(factions[peer], index);
            if (UpgradeLevel(peer, index) >= row.MaxLevel) return false;
            // And only in the building the row names. This is the one test of
            // which building may research, with no role test in front of it:
            // every row names entry 0 of the Tech slot today, and the day
            // docs/TECH-TREE.md moves a row onto a second research building, the
            // row's ResearchAt moves and this line is what keeps every other
            // building, the first 기술 건물 included, from running it.
            if (b.Role != row.ResearchAt.Role || b.Slot != row.ResearchAt.Slot) return false;
            // And not a row another of this peer's 기술 건물 is already on. Two
            // buildings researching one row would each finish a level from one
            // price, so a peer with two would buy level 2 for level 1's money.
            if (Researching(peer, index)) return false;

            // ponytail: every row is Tier 3 and a finished 기술 건물 is itself
            // what makes TierOf answer 3, so in play this line cannot refuse
            // anything the lines above let through. It is here so a row meant to
            // open later is one number in the table rather than a new rule.
            if (row.Tier > TierOf(peer)) return false;
            // After the tier, the order docs/TECH-TREE.md asks both in. No row
            // names a building today; the column is there for 씨앗 요람 반경 and
            // 집결 반경, which need a building other than the one researching.
            if (row.Requires.Role != Role.None && !HasStanding(peer, row.Requires.Role, row.Requires.Slot)) return false;
            return resources[peer] >= ResearchCost(peer, index);
        }

        /// <summary>True when any of this peer's buildings is researching this row.</summary>
        private bool Researching(int peer, int index)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (e.Alive && e.Owner == peer && e.ResearchIndex == index) return true;
            }
            return false;
        }

        /// <summary>
        /// Refused whole or accepted whole. Accepted means the price of the next
        /// level is gone from the bank on this tick, the way a Produce takes the
        /// price of the unit when it is queued and not when it comes out.
        /// </summary>
        private void TryResearch(int peer, int buildingId, int index)
        {
            if (!CanResearch(peer, buildingId, index)) return;

            resources[peer] -= ResearchCost(peer, index);
            Entity b = entities[buildingId];
            b.ResearchIndex = index;
            b.ResearchTicksLeft = FactionData.Upgrade(factions[peer], index).Ticks;
            entities[buildingId] = b;
        }

        /// <summary>
        /// Runs immediately after ProductionSystem, by ascending entity id, so two
        /// buildings finishing on one tick raise their levels in the same order on
        /// every peer. Before combat, so a 공격력 level that lands on this tick is
        /// in this tick's shots, the same boundary a unit finished off a queue has.
        ///
        /// A building that died this tick never reaches here with a clock: Kill
        /// has already dropped it, and deaths happen in combat, which runs after.
        /// </summary>
        private void ResearchSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity b = entities[i];
                if (!b.Alive || b.ResearchIndex < 0) continue;

                b.ResearchTicksLeft--;
                // At or past zero, for the reason ProductionSystem's test is: a
                // row timed at no ticks at all lands on the spot rather than
                // counting down through negative numbers forever.
                if (b.ResearchTicksLeft <= 0)
                {
                    upgradeLevels[b.Owner, b.ResearchIndex]++;
                    ClearResearch(ref b);
                }
                entities[i] = b;
            }
        }

        /// <summary>
        /// Stops the research running in this building and pays it back in full.
        /// Reached only through CancelProduction on a building whose queue is
        /// empty, which is every 기술 건물 that never queued a unit.
        ///
        /// The whole price, for the reason a cancelled unit is refunded whole:
        /// the price was taken in full when the order was accepted, so a full
        /// refund leaves the bank exactly where it would have been had the order
        /// never been given. The level has not moved while the research ran, so
        /// ResearchCost answers the price that was paid.
        ///
        /// A death is not a cancel. Kill drops the clock and pays nothing, which
        /// is the line the issue draws: losing the building costs the research.
        /// </summary>
        private void TryCancelResearch(int peer, int buildingId)
        {
            if (!OwnedAndAlive(buildingId, peer)) return;
            Entity b = entities[buildingId];
            if (b.Kind != EntityKind.Building || b.ResearchIndex < 0) return;

            resources[peer] += ResearchCost(peer, b.ResearchIndex);
            ClearResearch(ref b);
            entities[buildingId] = b;
        }

        /// <summary>
        /// Puts a building back in the not-researching state. Static and by ref
        /// for the reason ClearGrowth is: it is called from inside loops that hold
        /// an entity by value and write it back once.
        /// </summary>
        private static void ClearResearch(ref Entity e)
        {
            e.ResearchIndex = -1;
            e.ResearchTicksLeft = 0;
        }

        /// <summary>What this peer's 공격력 adds to every hit its weapons land.</summary>
        public int AttackBonus(int peer) => UpgradeLevelOf(peer, UpgradeEffect.Attack) * AttackPerLevel;

        /// <summary>What this peer's 방어력 takes off every hit its bodies receive.</summary>
        public int ArmorBonus(int peer) => UpgradeLevelOf(peer, UpgradeEffect.Armor) * ArmorPerLevel;
    }
}
