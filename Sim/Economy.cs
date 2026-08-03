namespace WordCraft.Sim
{
    /// <summary>
    /// Gathering, construction, and production. Every duration counts down in
    /// whole ticks; nothing here converts to or from seconds.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// Workers loop node -> drop-off -> node on their own. Iterating by id
        /// keeps the loop identical on every peer even when two workers finish a
        /// load on the same tick.
        /// </summary>
        private void GatherSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity w = entities[i];
                if (!w.Alive || w.Kind != EntityKind.Worker || w.GatherNodeId < 0) continue;

                if (w.CarryAmount > 0)
                {
                    DeliverStep(i, ref w);
                }
                else
                {
                    GatherStep(i, ref w);
                }
                entities[i] = w;
            }
        }

        private void GatherStep(int id, ref Entity w)
        {
            Entity node = entities[w.GatherNodeId];
            if (!node.Alive || node.Resource <= 0)
            {
                w.GatherNodeId = FindNearestNode(w.Position);
                w.GatherTicksLeft = 0;
                return;
            }

            if (!WithinRange(w.Position, node.Position, InteractRange))
            {
                Retarget(id, ref w, node.Position);
                return;
            }

            if (w.GatherTicksLeft == 0) w.GatherTicksLeft = GatherTicks;
            w.GatherTicksLeft--;
            if (w.GatherTicksLeft > 0) return;

            int take = node.Resource < CarryCapacity ? node.Resource : CarryCapacity;
            node.Resource -= take;
            // A spent node stops blocking the grid, so paths through it reopen.
            if (node.Resource == 0) node.Alive = false;
            entities[node.Id] = node;

            w.CarryAmount = take;
            w.DropOffId = FindNearestDropOff(w.Owner, w.Position);
        }

        private void DeliverStep(int id, ref Entity w)
        {
            if (w.DropOffId < 0 || !entities[w.DropOffId].Alive)
            {
                w.DropOffId = FindNearestDropOff(w.Owner, w.Position);
                if (w.DropOffId < 0) return; // nothing to deliver to; hold the load
            }

            Entity drop = entities[w.DropOffId];
            if (!WithinRange(w.Position, drop.Position, InteractRange))
            {
                Retarget(id, ref w, drop.Position);
                return;
            }

            resources[w.Owner] += w.CarryAmount;
            w.CarryAmount = 0;
            if (w.GatherNodeId >= 0 && !entities[w.GatherNodeId].Alive)
            {
                w.GatherNodeId = FindNearestNode(w.Position);
            }
            if (w.GatherNodeId >= 0) Retarget(id, ref w, entities[w.GatherNodeId].Position);
        }

        /// <summary>
        /// Repaths only when the worker is not already headed there. Without the
        /// Target check a worker that stopped short would recompute a path every tick.
        /// </summary>
        private void Retarget(int id, ref Entity w, FixVec2 destination)
        {
            if (w.Target.Equals(destination)) return;

            entities[id] = w;
            SetDestination(id, destination);
            w = entities[id];
        }

        // ponytail: linear scan over all entities per query. Fine at a few hundred
        // entities; bucket by grid cell if worker counts grow.
        private int FindNearestNode(FixVec2 from)
        {
            int best = -1;
            Fix bestDist = Fix.Zero;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Kind != EntityKind.ResourceNode || e.Resource <= 0) continue;
                Fix d = (e.Position - from).SqrMagnitude;
                // Strictly-less keeps the lowest id on a tie, independent of scan order.
                if (best < 0 || d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }

        private int FindNearestDropOff(int owner, FixVec2 from)
        {
            int best = -1;
            Fix bestDist = Fix.Zero;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Kind != EntityKind.Building || e.Owner != owner) continue;
                if (e.BuildTicksLeft > 0) continue; // a site under construction takes no deliveries
                Fix d = (e.Position - from).SqrMagnitude;
                if (best < 0 || d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }

        /// <summary>
        /// Every rule a Build has to pass, and the only place they are written.
        /// Public because the client tints its placement ghost with it, but the
        /// answer that counts is the one taken here during Apply: a view that
        /// asked a moment earlier is looking at a world one tick stale.
        /// </summary>
        // ponytail: a Build names a peer and a cell, never a worker. A peer can
        // place anywhere on the map with nothing standing there. Read the builder
        // out of Command.EntityId and require it within InteractRange the day
        // placement is supposed to cost a walk.
        public bool CanBuild(int peer, Role role, FixVec2 position)
        {
            if (peer < 0 || peer >= MaxPeers) return false;
            if (!FactionData.IsBuilding(role)) return false;
            // A faction cannot place what its roster does not list, or one peer
            // fields a building the other has no name and no art for.
            if (!FactionData.Has(factions[peer], role)) return false;
            if (FactionData.Tier(role) > TierOf(peer)) return false;
            if (resources[peer] < FactionData.BuildCost(role)) return false;

            // Reject an out-of-bounds request outright instead of silently clamping
            // it into the map, which would place a building the player never asked for.
            if (position.X < Fix.Zero || position.Y < Fix.Zero) return false;
            if (position.X >= Fix.FromInt(GridSize) || position.Y >= Fix.FromInt(GridSize)) return false;

            int cell = CellOf(position);
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive) continue;
                if (e.Kind != EntityKind.Building && e.Kind != EntityKind.ResourceNode) continue;
                if (CellOf(e.Position) == cell) return false;
            }
            return true;
        }

        /// <summary>
        /// Placement is validated inside the simulation, never by a client. Refused
        /// whole: a Build that fails any rule spends nothing and places nothing.
        /// </summary>
        private void TryPlaceBuilding(int peer, Role role, FixVec2 position)
        {
            if (!CanBuild(peer, role, position)) return;

            resources[peer] -= FactionData.BuildCost(role);
            SpawnBuilding(peer, role, CellCenter(CellOf(position)), complete: false);
        }

        /// <summary>
        /// ponytail: construction ticks down on its own, no worker has to stand on
        /// the site. Add a worker-presence check if build-order play ever matters.
        /// </summary>
        private void ConstructionSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Kind != EntityKind.Building || e.BuildTicksLeft <= 0) continue;

                e.BuildTicksLeft--;
                // Integer ramp so hp never depends on a division that rounds
                // differently anywhere. The divisor is this building's own total,
                // or a cheap building would finish its hp on someone else's clock.
                e.Hp = e.MaxHp - (e.MaxHp * e.BuildTicksLeft) / FactionData.BuildTicks(e.Role);
                if (e.Hp < 1) e.Hp = 1;
                entities[i] = e;
            }
        }

        /// <summary>
        /// T1 is the base, T2 a production building, T3 a tech building. Derived
        /// from standing buildings rather than latched, so losing the tech building
        /// closes tier 3 on the tick it falls while the units it opened stay alive.
        /// </summary>
        private int TierOf(int peer)
        {
            int tier = 1;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Kind != EntityKind.Building || e.Owner != peer) continue;
                if (e.BuildTicksLeft > 0) continue; // an unfinished building opens nothing
                if (e.Role == Role.Tech) return 3;  // nothing above 3, so stop looking
                if (e.Role == Role.Production && tier < 2) tier = 2;
            }
            return tier;
        }

        private void TryQueueUnit(int peer, int buildingId, Role role)
        {
            if (!OwnedAndAlive(buildingId, peer)) return;
            Entity b = entities[buildingId];
            if (b.Kind != EntityKind.Building || b.BuildTicksLeft > 0) return;
            if (FactionData.Tier(role) > TierOf(peer)) return;
            if (b.QueueCount >= MaxQueue) return;
            // Refused before anything is spent: a rejected command must leave the
            // peer's resources and queue exactly as it found them.
            // ponytail: the queue does not reserve population, so a queue filled
            // under the cap can still finish over it. Reserve at queue time if
            // players start using the queue to bank units past the cap.
            if (population[peer] >= PopulationCap(peer)) return;
            if (resources[peer] < ProduceCost) return;

            // Cost is taken at queue time, so a peer cannot queue more than it can pay for.
            resources[peer] -= ProduceCost;
            b.QueueCount++;
            // ponytail: one role for the whole queue, so a second order overwrites
            // what the first one was going to build. Give the queue a list of roles
            // when players expect to mix unit types in one building.
            b.ProduceRole = role;
            entities[buildingId] = b;
        }

        /// <summary>
        /// Takes the last unit off the queue and pays it back.
        ///
        /// Refund rule: the whole ProduceCost, no penalty. The cost is taken in
        /// full at queue time, so a full refund is the rule that leaves a peer's
        /// bank exactly where it would have been had the order never been given.
        /// A partial refund would have to be a share of elapsed production time to
        /// be fair, and that is a second number to hash for no gain in play.
        /// </summary>
        // ponytail: takes the tail because the queue is a count, not a list, so
        // there is no slot to name. Give cancel an index the day the queue becomes
        // a list of roles, which is the same day a building can mix unit types.
        private void TryCancelQueuedUnit(int peer, int buildingId)
        {
            if (!OwnedAndAlive(buildingId, peer)) return;
            Entity b = entities[buildingId];
            if (b.Kind != EntityKind.Building || b.QueueCount <= 0) return;

            b.QueueCount--;
            resources[peer] += ProduceCost;
            // The unit in progress is the last to go, so emptying the queue also
            // drops its timer. Left standing it would be hashed state that hands
            // the next order a head start it never paid for.
            if (b.QueueCount == 0) b.ProduceTicksLeft = 0;
            entities[buildingId] = b;
        }

        private void ProductionSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity b = entities[i];
                if (!b.Alive || b.Kind != EntityKind.Building) continue;
                if (b.BuildTicksLeft > 0 || b.QueueCount <= 0) continue;

                if (b.ProduceTicksLeft == 0) b.ProduceTicksLeft = ProduceTicks;
                b.ProduceTicksLeft--;
                if (b.ProduceTicksLeft == 0)
                {
                    b.QueueCount--;
                    entities[i] = b;
                    // Fixed rally offset: the spawn point must not depend on how many
                    // units already stand there.
                    //
                    // A worker has to come out a Worker. Kind is what GatherSystem
                    // and the Gather command both test, so a produced worker built
                    // as a plain Unit would carry worker stats, no weapon, and no
                    // way to ever gather anything.
                    int spawned = b.ProduceRole == Role.Worker
                        ? SpawnWorker(b.Owner, b.Position + RallyOffset)
                        : SpawnUnit(b.Owner, b.ProduceRole, b.Position + RallyOffset);
                    // Walked, not teleported: a rally point costs what crossing the
                    // ground costs, and the unit is catchable on the way.
                    if (b.HasRallyPoint) SetDestination(spawned, b.RallyPoint);
                    continue;
                }
                entities[i] = b;
            }
        }
    }
}
