namespace WordCraft.Sim
{
    /// <summary>
    /// 인간 마법 문명 징발: an 인간 worker walks up to a neutral 꼬마돌, stands
    /// beside it for CaptureTicks, and the body changes hands. It comes back up a
    /// Towerback, which is the only anti-air 인간 will ever field and the only unit
    /// in the game nobody can produce.
    ///
    /// This is the first rule in the simulation that transfers ownership. Every
    /// other one creates a body or destroys one, and the systems that care were
    /// written against those two events: population is counted where a body enters
    /// the world and released where it leaves, target acquisition skips a neutral
    /// outright, and command validation asks whether a peer owns what it is
    /// ordering. A body that changes owner passes none of those points, so the
    /// transfer has to do by hand what a spawn and a death do on their own.
    /// Conscript below is that one place, and it is deliberately the only one.
    ///
    /// What does not need touching, and why. The pathfinder's obstacle pass keys
    /// on EntityKind — resource nodes and buildings block a cell, units never did —
    /// so a body that stays a Unit crosses the transfer without moving in or out
    /// of the blocked grid, and no peer has to repath on the tick it changes hands.
    /// Combat reads Owner every tick from the entity rather than caching it, so the
    /// tick after the transfer the new Towerback is hostile to everyone it was
    /// neutral to and its old attackers keep their orders under the rules already
    /// written for them. There is no vision layer to move.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// The one faction that may capture. Read off the roster's faction rather
        /// than carried anywhere, so nothing can disagree with it.
        /// </summary>
        public const Faction CaptureFaction = Faction.Humans;

        /// <summary>
        /// What a neutral 꼬마돌 stands on the map as. Role.None because it is
        /// nobody's roster entry yet: with no owner there is no faction to look a
        /// row up in, so the stats it stands with are World constants and the role
        /// it carries has to be the one that promises nothing.
        /// </summary>
        public const Role NeutralRockRole = Role.None;

        /// <summary>The slot Towerback occupies once a capture finishes.</summary>
        public const Role TowerbackRole = Role.Signature;

        /// <summary>
        /// Which entry of that slot Towerback is. Named rather than left to
        /// default, for the reason WarlordOffspringSlot is: 인간's signature list
        /// holds 마도 정찰기 behind it, and nothing about a captured body would
        /// have said which of the two it meant.
        /// </summary>
        public const int TowerbackSlot = 0;

        /// <summary>
        /// Scenario setup only. Stands a neutral 꼬마돌 on the map: no owner, no
        /// weapon, no walk, and 80 hp.
        ///
        /// A Unit rather than a kind of its own. Every kind is a set of systems
        /// that treat a body one way, and a neutral rock wants exactly the set a
        /// unit already has: it can be shot, it does not block a path, it takes no
        /// deliveries, it opens no tier, and it is not a base anyone can lose the
        /// match with. A fourth kind would have to be excluded from each of those
        /// by hand, and the capture would then also have to move the body between
        /// two kinds — which is the pathfinding repath this design does not have.
        ///
        /// Owner = -1 is the neutrality resource nodes already use, and it is load
        /// bearing in three places without a line written here: nobody can command
        /// the rock, because OwnedAndAlive matches an owner against a peer id;
        /// nobody auto-acquires it, because AcquireTarget skips Owner &lt; 0; and
        /// nothing reads its roster row, because Flies and Armed refuse to look one
        /// up without an owner. An enemy that wants it dead has to say so with an
        /// attack order, which is the deliberate act the mechanic is about.
        /// </summary>
        public int SpawnNeutralRock(FixVec2 position) =>
            Add(EntityKind.Unit, -1, NeutralRockRole, 0, position, Fix.Zero, NeutralRockHp);

        /// <summary>
        /// True for a 꼬마돌 nobody has taken yet. All three clauses are the test,
        /// not just the owner: it is what a Capture command validates, what the
        /// capture loop re-checks every tick, and therefore the one place that says
        /// what may be captured at all.
        /// </summary>
        public bool IsNeutralRock(Entity e) =>
            e.Alive && e.Kind == EntityKind.Unit && e.Role == NeutralRockRole && e.Owner < 0;

        /// <summary>
        /// Runs beside the gather loop, and immediately after it, because it is the
        /// worker's other loop and the two are mutually exclusive by construction:
        /// both orders route through ClearOrders, so a worker holds a node or a rock
        /// and never both.
        ///
        /// Before combat, so a capture that completes on this tick is a Towerback
        /// for this tick's shooting, exactly as an offspring emitted this tick
        /// fights this tick. That also fixes the interruption boundary at one end:
        /// deaths happen in combat, which is after this, so a capture that has
        /// already finished is finished, and one that has not is caught here on the
        /// next tick with its progress dropped.
        ///
        /// By ascending entity id, so two workers finishing on the same tick take
        /// their rocks in the same order on every peer.
        /// </summary>
        private void CaptureSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity w = entities[i];
                if (!w.Alive || w.Kind != EntityKind.Worker || w.CaptureTargetId < 0) continue;

                CaptureStep(i, ref w);
                entities[i] = w;
            }
        }

        private void CaptureStep(int id, ref Entity w)
        {
            Entity rock = entities[w.CaptureTargetId];
            // One test covers both endings the document asks for and one it does
            // not name. The rock died: 진행도가 사라진다. Another worker finished
            // first: it is no longer neutral, so the same line drops this one.
            //
            // The worker's own death is not tested here at all, because there is
            // nothing to test — a dead worker is skipped by the loop above, its
            // progress was only ever on itself, and Kill has already zeroed it.
            // That is what one-sided state buys.
            if (!IsNeutralRock(rock))
            {
                ClearCapture(ref w);
                return;
            }

            if (!WithinRange(w.Position, rock.Position, InteractRange))
            {
                // Not yet there, or walked off. Either way the clock goes back to
                // the top: partial progress is not preserved anywhere, and this is
                // the same rule the two deaths follow rather than a third one.
                w.CaptureTicksLeft = 0;
                Retarget(id, ref w, rock.Position);
                return;
            }

            if (w.CaptureTicksLeft == 0)
            {
                w.CaptureTicksLeft = CaptureTicks;
                // 그동안 일꾼은 이동하지 않는다. Dropped here rather than expressed
                // as a mode of its own: the walk is what would have moved it, so
                // taking the walk away is the whole rule, and MoveSystem needs to
                // learn nothing. A player who wants the worker back issues any
                // order at all, which clears the capture through ClearOrders.
                Halt(id, ref w);
            }

            w.CaptureTicksLeft--;
            if (w.CaptureTicksLeft > 0) return;

            ClearCapture(ref w);
            Conscript(rock.Id, w.Owner);
        }

        /// <summary>
        /// The transfer itself, and the only place a body changes hands.
        ///
        /// The population count is the half a transfer has to do by hand. A body
        /// enters the world through Add, which counts it, and leaves through Kill,
        /// which releases it; a neutral was never counted because Add skips an
        /// owner outside the peer range. Without this line the Towerback would be
        /// free supply for as long as it stood and would decrement its owner's
        /// count when it died, which is a population that walks downward over a
        /// match — hashed state, so both peers would agree on it and nothing would
        /// ever say why 인간 could field more than its cap.
        ///
        /// The cap is not consulted. A capture at the cap completes anyway, for the
        /// reason a warlord emits at the cap: the mechanic is the whole of what the
        /// faction is, and 60 ticks of a worker standing still, spent and silently
        /// refused, is a rule no player can see. Production is where the cap bites.
        /// </summary>
        private void Conscript(int id, int owner)
        {
            Entity e = entities[id];
            UnitStats s = FactionData.Stats(factions[owner], TowerbackRole, TowerbackSlot);

            e.Owner = owner;
            e.Role = TowerbackRole;
            e.Slot = TowerbackSlot;
            // Full hp, not the damage the rock was carrying. The two rows have
            // nothing in common — 80 against 200 — so carrying damage over means
            // choosing a ratio, which is a division and a second rule to hash
            // nothing for. A rock the enemy shot and failed to finish is worth the
            // full tower; that is what failing to finish it costs.
            e.Hp = s.Hp;
            e.MaxHp = s.Hp;
            e.Speed = s.Speed;
            entities[id] = e;

            if (CountsAgainstPopulation(e.Kind) && owner >= 0 && owner < MaxPeers) population[owner]++;
        }

        /// <summary>
        /// Puts a body back in the not-capturing state. Static and by ref for the
        /// reason ClearOrders is: it is called from inside loops that hold an
        /// entity by value and write it back once.
        /// </summary>
        private static void ClearCapture(ref Entity e)
        {
            e.CaptureTargetId = -1;
            e.CaptureTicksLeft = 0;
        }
    }
}
