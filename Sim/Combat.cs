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

        /// <summary>The radius once 밀집 반경 is researched.</summary>
        public static readonly Fix VolleyRadiusResearched = Fix.FromInt(4);

        /// <summary>The cap once 밀집 강화 is researched.</summary>
        public const int VolleyMaxBonusResearched = 6;

        /// <summary>
        /// What 골렘 사거리 and 대포 사거리 add to the reach of the bodies they
        /// name, in cells.
        /// </summary>
        public static readonly Fix RangeUpgrade = Fix.FromInt(1);

        private void CombatSystem()
        {
            // 물막이, 표식, 집결 and 증폭 are read per shot, so their sources
            // are gathered once. A source killed earlier in this pass stops
            // counting the moment it falls: each read checks Alive.
            CollectAuraSources();
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
                // Through the attacker's own roster entry, so two units of one
                // role do not fire each other's weapon.
                UnitStats s = RosterStats(a);

                Entity t = entities[a.TargetId];
                if (WithinRange(a.Position, t.Position, RangeFor(a)))
                {
                    if (a.AttackCooldown == 0)
                    {
                        // 피 냄새, 집결 and 증폭 are read here, when the cooldown
                        // is set, against the target's hp before this shot.
                        a.AttackCooldown = AttackTicksAgainst(a, t);
                        // 잔해 투척: debris near the shooter makes this one shot
                        // harder and splash, per docs/TECH-TREE.md 피해 계산.
                        int thrown = ThrowRemnant(a);
                        int throwBonus = thrown >= 0 ? AbilityAmount(a.Owner, FactionData.AbilityRowAt(thrown)) : 0;
                        // 공격력 belongs to the shooter's owner and 방어력 to the
                        // target's. A neutral target has no owner, so no level.
                        // 표식 joins the attack side, and 물막이, 거품 벽 and 굳은 몸
                        // the armor side, per docs/TECH-TREE.md 피해 계산, and
                        // so does 물 지대 for a 물 슬라임 body standing on one.
                        int dealt = DamageAfterArmor(s.Damage + VolleyBonus(a) + MarkBonus(a, t) + throwBonus, AttackBonus(a.Owner),
                            ArmorFor(t) + AuraArmor(t) + FieldArmor(t), ArmorBonus(t.Owner));
                        // Where the hit landed, kept before Kill so a chain
                        // starts from the body even when the body is gone.
                        FixVec2 hitAt = t.Position;
                        t.Hp -= dealt;
                        if (t.Hp <= 0) Kill(ref t);
                        else ApplyHitStatus(a, ref t);
                        entities[t.Id] = t;
                        ChainFrom(a, t.Id, hitAt, dealt);
                        SplashFrom(a, t.Id, hitAt, dealt, ActiveAbility(a, AbilityKind.SplashOnHit));
                        SplashFrom(a, t.Id, hitAt, dealt, thrown);
                        // 전개: a deployed 견인 대포 splashes on the ground round its target.
                        if (a.Deployed) SplashFrom(a, t.Id, hitAt, dealt, DeployRow(a));
                        // 질풍: every attack sets the clock back to its full length.
                        int gale = ActiveAbility(a, AbilityKind.GaleAfterAttack);
                        if (gale >= 0) a.AbilityTicksLeft = AbilityTicks(a.Owner, FactionData.AbilityRowAt(gale));
                    }
                }
                else if (a.Kind != EntityKind.Building && a.Mode != OrderMode.Hold && a.Speed.Raw != 0 && !a.Deployed &&
                         (a.Mode == OrderMode.AttackMove || PathDone(i)))
                {
                    // A holder is left out for the same reason a building is: Target
                    // is hashed, and a chase written here would move it. So is a
                    // body with no speed at all, 거대 덩쿨 per docs/TECH-TREE.md:
                    // MoveSystem never walks it, and a Target written here would be
                    // hashed state that changes and moves nothing.
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
        /// 연쇄 번개 and 연쇄 방전, docs/TECH-TREE.md 피해 계산 연쇄: after the
        /// shot, the damage jumps to the nearest hostile body within the row's
        /// radius that this weapon can hit and that this shot has not hit yet,
        /// halving each hop (floor, at least 1). The first hop is measured from
        /// where the first target stood when it was hit, each later hop from
        /// the body hit last. Nearest is the smallest squared distance, the lower
        /// id on a tie, as in AcquireTarget.
        ///
        /// The ids already hit sit in an array on the stack, sized for the most
        /// hops any row takes, and live for this one shot: nothing about a chain
        /// is state. The hop's damage is fixed off the first hit's final damage,
        /// so the bodies it jumps to are not asked for armor again.
        /// </summary>
        private void ChainFrom(Entity a, int firstId, FixVec2 hitAt, int dealt)
        {
            int ability = ActiveAbility(a, AbilityKind.ChainOnHit);
            if (ability < 0) return;
            AbilityRow row = FactionData.AbilityRowAt(ability);
            Fix reach = Fix.FromInt(AbilityRadius(a.Owner, row));

            System.Span<int> hit = stackalloc int[FactionData.MaxChainHops + 1];
            hit[0] = firstId;
            int count = 1;
            FixVec2 from = hitAt;
            int damage = dealt;
            for (int hop = 0; hop < row.Limit; hop++)
            {
                damage = damage / 2 < 1 ? 1 : damage / 2;
                int best = -1;
                Fix bestDist = Fix.Zero;
                for (int i = 0; i < entities.Count; i++)
                {
                    Entity c = entities[i];
                    if (!c.Alive || c.Kind == EntityKind.ResourceNode) continue;
                    if (c.Owner < 0 || c.Owner == a.Owner) continue;
                    if (!CanHit(a, c)) continue;
                    bool already = false;
                    for (int h = 0; h < count; h++) if (hit[h] == i) already = true;
                    if (already) continue;
                    Fix d = (c.Position - from).SqrMagnitude;
                    if (d > reach * reach) continue;
                    if (best < 0 || d < bestDist) { best = i; bestDist = d; }
                }
                if (best < 0) return;

                Entity v = entities[best];
                from = v.Position;
                v.Hp -= damage;
                if (v.Hp <= 0) Kill(ref v);
                entities[best] = v;
                hit[count++] = best;
            }
        }

        /// <summary>
        /// 용암 폭발, 폭격 and the splash of 잔해 투척, docs/TECH-TREE.md 피해 계산
        /// 광역: every other hostile body of the row's targets within its radius
        /// of where the struck body stood when it was hit, and that this weapon
        /// can reach, takes half the shot's final damage, floor, at least 1, by
        /// ascending id. Fixed off the shot like a chain hop: the bodies it lands
        /// on are not asked for armor again. No friendly damage. Nothing when
        /// row is -1.
        /// </summary>
        private void SplashFrom(Entity a, int struckId, FixVec2 hitAt, int dealt, int row)
        {
            if (row < 0) return;
            AbilityRow r = FactionData.AbilityRowAt(row);
            int half = dealt / 2 < 1 ? 1 : dealt / 2;
            DamageHostileBodies(a.Owner, r.Targets, hitAt, Fix.FromInt(AbilityRadius(a.Owner, r)), half, a.Id, struckId);
        }

        /// <summary>
        /// What one hit takes off: the weapon and every bonus behind it, less the
        /// target's armor and every bonus behind that, and never less than one.
        ///
        /// The floor is the rule that keeps armor a discount rather than an
        /// immunity. Without it three levels of 방어력 on a body hit by a weapon
        /// of three would make that weapon do nothing at all, and a fight between
        /// the two would never end — a stalemate that hashes the same on both
        /// peers and that nobody can play out of.
        ///
        /// Public and static so the harness can pin the floor directly: no roster
        /// row today is light enough for researched armor to reach it in a fight.
        /// </summary>
        public static int DamageAfterArmor(int damage, int attackBonus, int armor, int armorBonus)
        {
            int dealt = damage + attackBonus - (armor + armorBonus);
            return dealt < 1 ? 1 : dealt;
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
            if (!IsArcher(archer)) return 0;
            if (archer.Owner < 0 || archer.Owner >= MaxPeers) return 0;
            if (factions[archer.Owner] != Faction.WaterSlimes) return 0;

            // Ascending entity id, never a keyed collection. A count does not care
            // about order, but the loop that produces it is not allowed to become
            // one that does, and the cap makes it care: stopping early has to stop
            // on the same archer everywhere.
            Fix radius = VolleyRadiusFor(archer.Owner);
            int cap = VolleyMaxBonusFor(archer.Owner);
            int allies = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity o = entities[i];
                if (!o.Alive || o.Id == archer.Id) continue;
                if (o.Owner != archer.Owner) continue;
                if (!IsArcher(o)) continue;
                if (!WithinRange(o.Position, archer.Position, radius)) continue;
                if (++allies == cap) break;
            }
            return allies;
        }

        /// <summary>The slot 물결 궁수 occupies, and which entry of it.</summary>
        public const Role ArcherRole = Role.Ranged;

        /// <summary>
        /// Entry 0. 해일 인도자 is entry 1 of the same slot and is no archer, and
        /// docs/TECH-TREE.md puts 침 뱉는 슬라임 and 물대포 슬라임 behind it too:
        /// 일제 사격 is 물결 궁수's, counted among and given to 물결 궁수 alone.
        /// </summary>
        public const int ArcherSlot = 0;

        /// <summary>
        /// A 물결 궁수: the one entry 일제 사격 counts and the one it is given to.
        /// Faction is the caller's to check, and VolleyBonus checks it once for
        /// the archer, which is the same owner every counted ally has.
        /// </summary>
        private static bool IsArcher(Entity e) =>
            e.Kind == EntityKind.Unit && e.Role == ArcherRole && e.Slot == ArcherSlot;

        /// <summary>밀집 반경: how far an allied archer counts from, for this peer.</summary>
        public Fix VolleyRadiusFor(int peer) =>
            UpgradeLevelOf(peer, UpgradeEffect.VolleyRadius) > 0 ? VolleyRadiusResearched : VolleyRadius;

        /// <summary>밀집 강화: the most the massed bonus adds, for this peer.</summary>
        public int VolleyMaxBonusFor(int peer) =>
            UpgradeLevelOf(peer, UpgradeEffect.VolleyCap) > 0 ? VolleyMaxBonusResearched : VolleyMaxBonus;

        /// <summary>
        /// How far this body's weapon reaches right now: its roster row's Range,
        /// or the researched reach once its owner has the row that names this
        /// entry (덩쿨 사거리, 물결 궁수 사거리, 결속 강화 and the rest of
        /// docs/TECH-TREE.md 묶음 3), and a cell more for the two rows a range
        /// upgrade names by role — 돌 골렘's melee bodies under 골렘 사거리, 인간
        /// 대포 under 대포 사거리. The one place a weapon's reach is read, so the
        /// shot and anything that later asks how far a body can hit cannot disagree.
        ///
        /// 골렘 사거리 covers every entry of the melee slot. 대포 사거리 covers
        /// entry 0 of the defense slot and not the 전기 타워 and 돌 포탑 behind
        /// it, for the reason buildOverrides is keyed by entry: the upgrade is
        /// named after one building.
        /// </summary>
        public Fix RangeFor(Entity e)
        {
            Fix range = UnstormedRangeFor(e);
            // 모래폭풍, docs/TECH-TREE.md 묶음 8: inside a hostile zone a body
            // reaches no further than the zone's amount. Last, so it caps every
            // bonus above; a reach already under it is left alone.
            if (range.Raw == 0) return range;
            int storm = SandStormReach(e);
            return storm >= 0 && range > Fix.FromInt(storm) ? Fix.FromInt(storm) : range;
        }

        /// <summary>RangeFor before a 모래폭풍 caps it: the roster, the research, 전개, 정조준 and 물기둥 분수.</summary>
        private Fix UnstormedRangeFor(Entity e)
        {
            if (e.Owner < 0 || e.Owner >= MaxPeers) return Fix.Zero;
            // 전개, docs/TECH-TREE.md 묶음 8: deployed, the 견인 대포 reaches the row's
            // amount, and a cell more once 대포 사거리 lands. The research is named
            // after the 대포 building, but a deployed cannon is the same gun.
            if (e.Deployed)
            {
                int deploy = DeployRow(e);
                if (deploy >= 0)
                {
                    Fix deployed = Fix.FromInt(AbilityAmount(e.Owner, FactionData.AbilityRowAt(deploy)));
                    return UpgradeLevelOf(e.Owner, UpgradeEffect.CannonRange) > 0 ? deployed + RangeUpgrade : deployed;
                }
            }
            Fix range = EntryUpgraded(e, UpgradedStat.Range, out Fix researched) ? researched : RosterStats(e).Range;
            Faction faction = factions[e.Owner];
            if (faction == Faction.RockGolems && e.Kind == EntityKind.Unit && e.Role == Role.Melee &&
                UpgradeLevelOf(e.Owner, UpgradeEffect.GolemMeleeRange) > 0)
            {
                return range + RangeUpgrade;
            }
            if (faction == Faction.Humans && e.Kind == EntityKind.Building && e.Role == Role.Defense &&
                e.Slot == 0 && UpgradeLevelOf(e.Owner, UpgradeEffect.CannonRange) > 0)
            {
                return range + RangeUpgrade;
            }
            // 정조준, docs/TECH-TREE.md 묶음 7: 40 ticks still and the reach is 2 longer.
            // 물기둥 분수 on a water field reaches 1 further.
            return range + SteadyAimBonus(e) + FieldRangeBonus(e);
        }

        /// <summary>
        /// Units fight, and so do finished defense buildings. A site still under
        /// construction does not, for the same reason it takes no deliveries.
        ///
        /// Nor does a body mid-growth: 성장 중에는 공격하지 않는다. A growing worker
        /// was kept out by its kind alone; since docs/TECH-TREE.md M6 a unit grows
        /// too (잎날 정령 → 폭풍 기수), and it is a Unit for the whole growth.
        ///
        /// A body whose roster row carries no weapon fights under no circumstances,
        /// whatever its kind. This is the one gate every path goes through —
        /// acquisition, the chase, and the Attack and AttackMove orders all ask it
        /// first — so 지옥불 군단장 cannot be walked into the front line by the
        /// simulation or ordered there by a player. Admitted here it would acquire
        /// a target, close on it, and stand in weapon range firing nothing.
        ///
        /// Nor does a 견인 대포 mid-transition, docs/TECH-TREE.md 전개: 전환
        /// 중에는 쏘지도 걷지도 않는다. Deployed or not, it fires again on the
        /// tick the transition ends.
        /// </summary>
        private bool CanAttack(Entity e) =>
            Armed(e) && !IsGrowing(e) && !Transitioning(e) &&
            (e.Kind == EntityKind.Unit ||
             (e.Kind == EntityKind.Building && e.Role == Role.Defense && e.BuildTicksLeft == 0));

        /// <summary>
        /// Whether this entity's roster row carries a weapon. Read off the table
        /// rather than carried on the entity, for the same reason Flies is: it
        /// never changes for a given faction, role and entry, so making it state
        /// would add a hashed field that can only ever hold one value and give two
        /// peers something new to disagree about.
        /// </summary>
        public bool Armed(Entity e) =>
            e.Owner >= 0 && e.Owner < MaxPeers && RosterStats(e).HasWeapon;

        /// <summary>
        /// Whether this attacker's weapon reaches this target at all, before any
        /// question of distance. Melee cannot touch what flies, 대포 cannot touch
        /// what flies, and Towerback cannot touch what walks, all per
        /// docs/FACTION-MECHANICS.md 공중. Each is a property of the weapon rather
        /// than of the order: an attack command naming something out of reach is
        /// refused the same way an acquisition is, or a melee squad would walk
        /// under a flier forever waiting to swing.
        ///
        /// Which half of the roster row is read is decided by the target and
        /// nothing else, so there is one line in the simulation that says what a
        /// weapon may point at. Both halves are asked here rather than only the
        /// air one, because ground reach stopped being universal the moment 대포
        /// arrived, and a rule that only knew about air would have let it fire.
        /// </summary>
        private bool CanHit(Entity attacker, Entity target)
        {
            UnitStats s = RosterStats(attacker);
            return Flies(target) ? s.HitsAir : s.HitsGround;
        }

        /// <summary>
        /// An ordered target only has to exist, be hostile, and be reachable by
        /// this weapon. Deliberately no range test: the attacker walks to it,
        /// however far that is.
        /// </summary>
        private bool OrderedTargetAlive(Entity attacker)
        {
            if (attacker.TargetId < 0 || attacker.TargetId >= entities.Count) return false;
            Entity t = entities[attacker.TargetId];
            return t.Alive && t.Owner != attacker.Owner && CanHit(attacker, t);
        }

        private bool ValidTarget(Entity attacker, int targetId)
        {
            if (targetId < 0 || targetId >= entities.Count) return false;
            Entity t = entities[targetId];
            if (!t.Alive || t.Owner == attacker.Owner) return false;
            if (!CanHit(attacker, t)) return false;
            return WithinRange(attacker.Position, t.Position, AcquisitionRange(attacker));
        }

        /// <summary>
        /// How far this body notices and keeps a hostile: AcquireRange, or its own
        /// weapon's reach when that is longer. 거대 덩쿨 reaches 11 once 거대 덩쿨
        /// 사거리 is researched (docs/TECH-TREE.md 묶음 3), and a reach past the
        /// distance it acquires at would only ever be used on an ordered target.
        /// Every other row reaches 10 or less, so for them this is AcquireRange.
        /// </summary>
        public Fix AcquisitionRange(Entity e)
        {
            Fix reach = RangeFor(e);
            return reach > AcquireRange ? reach : AcquireRange;
        }

        /// <summary>
        /// Nearest hostile entity inside AcquisitionRange. Ties break to the lower
        /// entity id, which is why the scan is a plain ascending loop and not a
        /// lookup in any hashed collection.
        /// </summary>
        // ponytail: O(n^2) across all attackers each tick. Swap in a grid bucket
        // broad phase if unit counts pass the tick budget.
        private int AcquireTarget(Entity attacker)
        {
            int best = -1;
            Fix bestDist = Fix.Zero;
            Fix range = AcquisitionRange(attacker);
            for (int i = 0; i < entities.Count; i++)
            {
                Entity t = entities[i];
                if (!t.Alive || t.Kind == EntityKind.ResourceNode) continue;
                if (t.Owner == attacker.Owner || t.Owner < 0) continue;
                if (!CanHit(attacker, t)) continue;

                Fix d = (t.Position - attacker.Position).SqrMagnitude;
                if (d > range * range) continue;
                // Strictly-less: the first, lowest-id entity wins an exact tie.
                if (best < 0 || d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }
    }
}
