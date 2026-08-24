using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Track B's write scope is Client/Assets/Scripts/ (.plan/general/2026-08-11-parallel-milestones.md).
    /// Replay/Program.cs belongs to track A's checks, so this file holds the ones
    /// issue #130 adds and Program.cs only carries the single line that calls
    /// Check() — the same split AlertChecks.cs set up for #96.
    ///
    /// Two things, both of which a batch-run Unity pass cannot see coming.
    ///
    /// The map has to hold 꼬마돌 at all. That is the whole of the first half of
    /// #130, and nothing else in the harness would notice them going away again:
    /// MapIsExactlySymmetric only asks that whatever is placed has a mirror, and a
    /// map with none passes it perfectly.
    ///
    /// And CaptureOrder — the client's own answer to "would this order be taken"
    /// — has to keep saying what World.Apply says. It is asked twice on purpose
    /// (once to draw the cell, once before the click goes out), so it is a second
    /// copy of a rule, and a second copy that drifts is a card that offers an
    /// order the simulation drops in silence. Each case below is asserted against
    /// the simulation actually running the command rather than against a
    /// re-typing of the rule, or this file would only prove it agrees with
    /// itself.
    ///
    /// Orders.cs and CommandCard.cs stay untested here, the way Hits.cs and
    /// Alert.cs stay untested next to HitDetection and AlertWindow: they are
    /// Unity plumbing over this, and a 3x3 table of nine literal cells is not a
    /// thing that can be wrong in a way a check would catch first.
    /// </summary>
    internal static class CaptureOrderChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            TheMapHoldsNeutralRocks();
            OnlyTheCaptureFactionIsOffered();
            TheViewAgreesWithTheSimulation();
            ATakenRockIsNoLongerOffered();
            ProgressTracksTheClock();
        }

        /// <summary>
        /// #130's first half. Pinned to a count rather than to "more than none",
        /// so dropping one of the four pairs shows up here instead of quietly
        /// halving how much of the mechanic a match contains.
        /// </summary>
        private static void TheMapHoldsNeutralRocks()
        {
            World world = Build();
            var found = new List<int>();
            for (int i = 0; i < world.EntityCount; i++)
            {
                if (world.IsNeutralRock(world.GetEntity(i))) found.Add(i);
            }

            Check(found.Count == 4,
                "the map holds " + found.Count + " neutral 꼬마돌, expected 4 — 징발 has nothing to take");

            // Standing on ground somebody can walk to. A rock inside the barrier
            // or a lake is a capture no worker can ever finish, and it would look
            // exactly like a rock that works right up until a player tried.
            for (int i = 0; i < found.Count; i++)
            {
                Entity rock = world.GetEntity(found[i]);
                int cell = World.CellOf(rock.Position);
                Check(world.TerrainAt(cell) == TileKind.Open,
                    "a neutral 꼬마돌 stands on " + world.TerrainAt(cell) + ", which no worker can reach");
            }
        }

        /// <summary>
        /// The card's own question, over every faction rather than over 인간 and
        /// one other: the roster is what decides this and the roster grows.
        /// </summary>
        private static void OnlyTheCaptureFactionIsOffered()
        {
            int offered = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                if (CaptureOrder.Available((Faction)f)) offered++;
            }
            Check(offered == 1, offered + " factions are offered 징발, expected exactly one");
            Check(CaptureOrder.Available(World.CaptureFaction), "the capture faction is not offered 징발");
        }

        /// <summary>
        /// Every way an order can be wrong, each asked of CaptureOrder and then of
        /// the simulation, and required to come back the same. The simulation's
        /// answer is read off the worker: a Capture that was taken leaves
        /// CaptureTargetId naming the rock, and one that was refused leaves it -1.
        /// </summary>
        private static void TheViewAgreesWithTheSimulation()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                World world = Build(faction);
                int worker = FirstOwned(world, 0, EntityKind.Worker);
                int rock = FirstNeutralRock(world);
                int fighter = FirstOwned(world, 0, EntityKind.Unit);
                int node = FirstKind(world, EntityKind.ResourceNode);
                int enemyWorker = FirstOwned(world, 1, EntityKind.Worker);

                Agree(faction, "a worker of the right faction on a 꼬마돌", world, worker, rock);
                Agree(faction, "a fighter on a 꼬마돌", world, fighter, rock);
                Agree(faction, "a worker on a resource node", world, worker, node);
                Agree(faction, "a worker on the enemy's worker", world, worker, enemyWorker);
                Agree(faction, "a worker on nothing at all", world, worker, -1);
                Agree(faction, "a worker on an id past the end", world, worker, world.EntityCount);
                Agree(faction, "the enemy's worker on a 꼬마돌", world, enemyWorker, rock);
            }
        }

        /// <summary>
        /// The case the whole competition is about, and the one a client that read
        /// Owner and Role for itself would get wrong on the tick after a capture
        /// ends. Only the capture faction can reach it, so it is asked once rather
        /// than once per faction.
        /// </summary>
        private static void ATakenRockIsNoLongerOffered()
        {
            World world = Build(World.CaptureFaction);
            int rock = FirstNeutralRock(world);
            int worker = FirstOwned(world, 0, EntityKind.Worker);

            Check(CaptureOrder.Allows(world, 0, worker, rock), "a 꼬마돌 nobody holds is not offered");
            Conscript(world, rock);
            Check(!CaptureOrder.Allows(world, 0, worker, rock),
                "a 꼬마돌 that has already been taken is still offered");
        }

        /// <summary>
        /// One case, asked of the client and then of the simulation. The world is
        /// rebuilt for it, because a Capture that was taken changes the worker and
        /// the next case would be asking about a different body.
        /// </summary>
        private static void Agree(Faction faction, string what, World world, int entity, int target)
        {
            bool offered = CaptureOrder.Allows(world, 0, entity, target);
            bool taken = SimulationTakes(faction, entity, target);
            Check(offered == taken,
                faction + ": the client says " + offered + " and the simulation says " + taken +
                " about " + what);
        }

        /// <summary>
        /// Whether World.Apply keeps a Capture naming this target, run on a world
        /// of its own so nothing a previous case ordered is still standing. Ids
        /// are handed out in MatchScenario.Build order, so the same number names
        /// the same body in the rebuilt world as in the one the client was asked
        /// about.
        /// </summary>
        private static bool SimulationTakes(Faction faction, int entity, int target)
        {
            World world = Build(faction);
            world.Step(new List<Command>
            {
                new Command(0, 0, 0, CommandType.Capture, entity, FixVec2.Zero, target),
            });
            // The target is compared for range as well as for equality: -1 is what
            // a body that is not capturing already carries, so an order naming -1
            // would otherwise read back as an order that was taken.
            return target >= 0 && world.GetEntity(entity).CaptureTargetId == target;
        }

        /// <summary>
        /// Runs a capture through to the transfer, the only way a rock stops being
        /// neutral inside this file.
        /// </summary>
        private static void Conscript(World world, int rock)
        {
            int worker = FirstOwned(world, 0, EntityKind.Worker);
            var order = new List<Command>
            {
                new Command(0, 0, 0, CommandType.Capture, worker, FixVec2.Zero, rock),
            };
            var idle = new List<Command>();

            world.Step(order);
            // Long enough to walk the width of the map and then stand for
            // CaptureTicks. A number, not a loop that stops when it works: a
            // capture that silently stopped happening would otherwise pass by
            // running forever and this check would never say so.
            for (int t = 0; t < 2000 && world.IsNeutralRock(world.GetEntity(rock)); t++) world.Step(idle);
            Check(!world.IsNeutralRock(world.GetEntity(rock)),
                "a worker parked on a 꼬마돌 never finished taking it");
        }

        /// <summary>
        /// The reading the field meter and the panel's line both take. Zero while
        /// the worker is still walking — CaptureTicksLeft is zero then too, and a
        /// bar that read full on the way there would say the capture was done
        /// before it had started.
        /// </summary>
        private static void ProgressTracksTheClock()
        {
            World world = Build(World.CaptureFaction);
            int worker = FirstOwned(world, 0, EntityKind.Worker);
            int rock = FirstNeutralRock(world);

            Check(CaptureOrder.Progress(world.GetEntity(worker)) < 0f,
                "a worker with no capture reports progress");

            world.Step(new List<Command>
            {
                new Command(0, 0, 0, CommandType.Capture, worker, FixVec2.Zero, rock),
            });
            Check(CaptureOrder.Progress(world.GetEntity(worker)) == 0f,
                "a worker still walking to a 꼬마돌 already reports progress");

            var idle = new List<Command>();
            float highest = 0f;
            for (int t = 0; t < 2000 && world.GetEntity(worker).CaptureTargetId >= 0; t++)
            {
                world.Step(idle);
                float now = CaptureOrder.Progress(world.GetEntity(worker));
                if (now > highest) highest = now;
            }

            Check(highest > 0f && highest < 1f,
                "capture progress topped out at " + highest + ", expected a value inside 0 and 1");
        }

        private static World Build() => Build(ScriptedLog.Peer0Faction);

        /// <summary>Both peers the same faction, so peer 0's own answer is the faction's.</summary>
        private static World Build(Faction faction) => MatchScenario.Build(Seed, faction, faction);

        private static int FirstOwned(World world, int peer, EntityKind kind)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Alive && e.Owner == peer && e.Kind == kind) return i;
            }
            throw new Exception("the scenario has no " + kind + " for peer " + peer);
        }

        private static int FirstKind(World world, EntityKind kind)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                if (world.GetEntity(i).Kind == kind) return i;
            }
            throw new Exception("the scenario has no " + kind);
        }

        private static int FirstNeutralRock(World world)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                if (world.IsNeutralRock(world.GetEntity(i))) return i;
            }
            throw new Exception("the scenario has no neutral 꼬마돌");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
