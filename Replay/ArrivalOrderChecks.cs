using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// 차원 유랑종 통로 조준, held to the simulation. The counterpart of
    /// <see cref="CaptureOrderChecks"/>, and the same split for the same reason:
    /// Client/Assets/Scripts/ArrivalOrder.cs is UnityEngine-free on purpose so a
    /// headless harness can ask it things, and Program.cs carries one line that
    /// calls Check().
    ///
    /// The one part of the aim a batch-run Unity pass cannot see is whether the
    /// view's five answers still say what the simulation says. Every one of them
    /// reads off <see cref="World"/> today, which is what makes them right and
    /// nothing at all which keeps them right: a clause re-typed here later, a
    /// radius pinned to a literal so a ring could be drawn a frame earlier, an
    /// anchor list that forgot an unfinished 굴절 기둥 anchors nothing — each of
    /// those draws the wrong thing in silence. A cursor tinted green over a cell
    /// the body will not come out on, or grey over one it will.
    ///
    /// So nothing below compares a view answer with a re-reading of the rule it
    /// came from, which would only prove the file agrees with itself. Each one is
    /// asserted against the simulation actually doing the thing:
    ///
    /// - <see cref="ArrivalOrder.Available"/> against whether a faction's 굴절 기둥
    ///   anchors anything at all.
    /// - <see cref="ArrivalOrder.Aims"/> against whether a Produce given to that
    ///   body freezes the point onto it.
    /// - <see cref="ArrivalOrder.Valid"/> against where the body actually stands up
    ///   fifty ticks later.
    /// - <see cref="ArrivalOrder.Radius"/> against the boundary the simulation
    ///   admits, both sides of it.
    /// - <see cref="ArrivalOrder.Anchors"/> against whether each body, alone in a
    ///   world, makes the ground under it a valid arrival.
    ///
    /// Orders.cs, CommandCard.cs, Hud.cs and MatchView.cs stay untested here for
    /// the reason Orders.cs stays untested next to CaptureOrder: they are Unity
    /// plumbing over these answers, and a key binding is not a thing a headless
    /// check would catch first.
    /// </summary>
    internal static class ArrivalOrderChecks
    {
        private const ulong Seed = 0xC0FFEE;
        private const int Bank = 1000;

        public static void Check()
        {
            OnlyTheArrivingFactionIsOfferedTheAim();
            TheDrawnRadiusIsTheOneTheSimulationDecidesBy();
            TheViewAgreesAboutWhichBodyHoldsTheAim();
            TheViewAgreesAboutWhereTheBodyStandsUp();
            TheAnchorsAreTheBodiesThatCoverTheGroundUnderThem();
            TheAnchorListIsBuiltFreshAndInIdOrder();
            ADeadPassageAimsAtNothingAndADeadPillarAnchorsNothing();
            APassageStillGoingUpHoldsNoAim();
        }

        /// <summary>
        /// The card's own question, over every faction rather than over 차원 유랑종
        /// and one other: the roster is what decides this and the roster grows.
        ///
        /// The simulation's answer is not World.PassageFaction read a second time.
        /// It is a 굴절 기둥 standing on the map for each faction in turn and the
        /// simulation asked whether the ground beside it may be arrived at — the
        /// faction test lives inside ArrivalValid, so a faction that anchors
        /// nothing is a faction with no aim to offer.
        /// </summary>
        private static void OnlyTheArrivingFactionIsOfferedTheAim()
        {
            int offered = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                if (ArrivalOrder.Available((Faction)f)) offered++;
            }
            Check(offered == 1, offered + " factions are offered 통로 조준, expected exactly one");
            Check(ArrivalOrder.Available(World.PassageFaction), "the arriving faction is not offered 통로 조준");

            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                var world = new World(Seed);
                world.SetPeerFaction(0, faction);
                world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), complete: true);

                bool anchors = world.ArrivalValid(0, At(33, 30));
                Check(ArrivalOrder.Available(faction) == anchors,
                    faction + ": the client offers 통로 조준 = " + ArrivalOrder.Available(faction) +
                    " and the simulation anchors an arrival = " + anchors);
            }
        }

        /// <summary>
        /// 반경, and both sides of it. Not ArrivalOrder.Radius compared with
        /// World.ArrivalRadius, which is the same expression twice: the rings
        /// MatchView draws are this number, so what has to hold is that a point on
        /// the ring is a point the simulation arrives at and a point outside it is
        /// not.
        ///
        /// A sixteenth of a cell past, which is the same pair
        /// TheArrivalRadiusIsSixCells pins the constant with. A ring drawn short
        /// fails the outside half — it would promise nothing where the simulation
        /// still says yes — and a ring drawn long fails the inside half.
        /// </summary>
        private static void TheDrawnRadiusIsTheOneTheSimulationDecidesBy()
        {
            RingBoundary(carrier: false);
            RingBoundary(carrier: true);
        }

        /// <summary>
        /// Both anchors, because the document gives them one radius and one of them
        /// walks: a ring drawn against the building alone would say nothing about
        /// the 경계 운반자, which is the one a player watches move.
        /// </summary>
        private static void RingBoundary(bool carrier)
        {
            string what = carrier ? "경계 운반자" : "굴절 기둥";
            World world = Anchored(carrier);
            FixVec2 anchor = world.GetEntity(Pillar).Position;

            var onTheRing = new FixVec2(anchor.X + ArrivalOrder.Radius, anchor.Y);
            var justOutside = new FixVec2(anchor.X + ArrivalOrder.Radius + Fix.Ratio(1, 16), anchor.Y);

            Check(world.ArrivalValid(0, onTheRing),
                "a point on the ring the view draws around the " + what + " is refused by the " +
                "simulation: ArrivalOrder.Radius reaches further than World.ArrivalRadius");
            Check(!world.ArrivalValid(0, justOutside),
                "a point a sixteenth of a cell outside the ring the view draws around the " + what +
                " is still arrived at: ArrivalOrder.Radius falls short of World.ArrivalRadius");
        }

        // 차원 유랑종 조준 픽스처. Both peers arrive, so the enemy's own 통로 is a
        // body that passes every test but the owner's. Ids are handed out in spawn
        // order and never reused, so these numbers name these bodies.
        private const int Base = 0;
        private const int Passage = 1;
        private const int Pillar = 2;
        private const int Worker = 3;
        private const int EnemyBase = 4;
        private const int EnemyPassage = 5;
        private const int EnemyPillar = 6;

        /// <summary>Three cells off this peer's own 굴절 기둥.</summary>
        private static FixVec2 Aim(int peer) => peer == 0 ? At(33, 30) : At(45, 44);

        /// <summary>
        /// A 통로 to order from and a 정박한 세계 behind it, because a 통로 supports
        /// no population of its own and a queue over the cap is refused before any
        /// of the arrival rule is reached. Every body in it is standing: an
        /// unfinished building is refused by production long before the aim is
        /// reached, and a fixture holding one would be asking this file about a
        /// rule that lives somewhere else.
        ///
        /// The two peers stand far enough apart that neither 굴절 기둥 is in the
        /// other's reach of six. They both shoot, and a fixture whose bodies were
        /// trading damage would change under a check that only meant to step once.
        /// </summary>
        private static World Sweep()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Driftworlds);
            world.SetPeerFaction(1, Faction.Driftworlds);

            world.SpawnBuilding(0, Role.Base, At(10, 10), complete: true);                     // Base
            world.SpawnBuilding(0, World.PassageRole, World.PassageSlot, At(14, 10), true);    // Passage
            world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true);      // Pillar
            world.SpawnWorker(0, At(12, 12));                                                  // Worker
            world.SpawnBuilding(1, Role.Base, At(52, 52), complete: true);                     // EnemyBase
            world.SpawnBuilding(1, World.PassageRole, World.PassageSlot, At(56, 52), true);    // EnemyPassage
            world.SpawnBuilding(1, World.PillarRole, World.PillarSlot, At(48, 44), true);      // EnemyPillar

            world.GrantResources(0, Bank);
            world.GrantResources(1, Bank);
            return world;
        }

        /// <summary>
        /// Which body the aim belongs to, asked of the client and then of the
        /// simulation, over every body in the fixture and over both peers.
        ///
        /// The simulation's answer is read off the building: an accepted Produce
        /// freezes the point onto a 통로 and onto nothing else, so
        /// Entity.HasArrivalPoint one tick later is the simulation saying which
        /// body holds an aim. Every other gate the order passes through — the cap,
        /// the price, the tier, the building standing — is satisfied by the fixture,
        /// so the only thing left for the two answers to differ about is the body.
        /// </summary>
        private static void TheViewAgreesAboutWhichBodyHoldsTheAim()
        {
            Agree(0, "this peer's own 통로", Passage);
            Agree(0, "this peer's 정박한 세계", Base);
            Agree(0, "this peer's 굴절 기둥", Pillar);
            Agree(0, "this peer's worker", Worker);
            Agree(0, "the enemy's 통로", EnemyPassage);
            Agree(1, "the enemy's own 통로, asked of the enemy", EnemyPassage);
            Agree(1, "this peer's 통로, asked of the enemy", Passage);

            // Out of range answers no rather than throwing, which is what a click on
            // empty ground and a selection cleared on the tick the body died both
            // hand it.
            World world = Sweep();
            Check(!ArrivalOrder.Aims(world, 0, -1), "the client says nothing selected holds an aim");
            Check(!ArrivalOrder.Aims(world, 0, world.EntityCount),
                "the client says an id past the end of the world holds an aim");
        }

        /// <summary>
        /// One body, asked of the client and then of the simulation. The world is
        /// rebuilt for the simulation's half, because an accepted Produce spends
        /// mana and fills a queue and the next case would be asking about a
        /// different world.
        /// </summary>
        private static void Agree(int peer, string what, int entityId)
        {
            World world = Sweep();
            bool drawn = ArrivalOrder.Aims(world, peer, entityId);
            bool frozen = SimulationFreezesThePoint(peer, entityId);
            Check(drawn == frozen,
                "peer " + peer + ": the client says " + drawn + " and the simulation says " + frozen +
                " about whether " + what + " holds the arrival point");
        }

        /// <summary>
        /// Whether one Produce aimed at a point this peer's anchor covers leaves the
        /// point standing on the body it was given to.
        /// </summary>
        private static bool SimulationFreezesThePoint(int peer, int entityId)
        {
            World world = Sweep();
            FixVec2 aim = Aim(peer);
            Check(world.ArrivalValid(peer, aim),
                "peer " + peer + "'s own anchor does not cover its aim, so this check proves nothing");

            world.Step(ProduceAt(entityId, peer, aim));
            return world.GetEntity(entityId).HasArrivalPoint;
        }

        /// <summary>
        /// The cursor's tint against where the body actually comes out. Not
        /// ArrivalOrder.Valid compared with World.ArrivalValid, which is one
        /// expression written twice: a green cell has to be a cell the body stands
        /// on and a grey one has to be the 통로 자리, and that is only visible by
        /// running the order out to the arrival.
        ///
        /// Six points, chosen so both answers appear and so every clause that can
        /// refuse one is reached: inside the ring, on the ring, a sixteenth past
        /// it, far from any anchor, off the west edge and off the east edge.
        /// </summary>
        private static void TheViewAgreesAboutWhereTheBodyStandsUp()
        {
            World measure = Anchored(carrier: false);
            FixVec2 pillar = measure.GetEntity(Pillar).Position;

            Arrives("three cells inside the ring", At(33, 30));
            Arrives("exactly on the ring", new FixVec2(pillar.X + ArrivalOrder.Radius, pillar.Y));
            Arrives("a sixteenth of a cell outside the ring",
                new FixVec2(pillar.X + ArrivalOrder.Radius + Fix.Ratio(1, 16), pillar.Y));
            Arrives("far from every anchor", At(5, 50));
            Arrives("off the west edge", new FixVec2(Fix.FromInt(-1), At(30, 30).Y));
            Arrives("off the east edge", new FixVec2(Fix.FromInt(World.GridSize), At(30, 30).Y));
        }

        /// <summary>
        /// One point: what the cursor would be tinted, and then where the body it
        /// orders is standing when it stands up.
        /// </summary>
        private static void Arrives(string what, FixVec2 point)
        {
            World world = Anchored(carrier: false);
            bool green = ArrivalOrder.Valid(world, 0, point);

            FixVec2 passageSpot = world.GetEntity(Passage).Position + World.RallyOffset;
            Check(!point.Equals(passageSpot),
                "the point " + what + " is the 통로 자리 itself, so this check proves nothing");

            world.Step(ProduceAt(Passage, 0, point));
            Check(world.GetEntity(Passage).QueueCount == 1,
                "the order aimed " + what + " was refused, so the arrival proves nothing");

            Entity body = StepToArrival(world, what);
            bool landedOnIt = body.Position.Equals(point);
            Check(green == landedOnIt,
                "a point " + what + ": the client tints the cursor " + (green ? "green" : "grey") +
                " and the body stood up at " + Show(body.Position) +
                (landedOnIt ? " which is the point" : " which is not the point, but " +
                    (body.Position.Equals(passageSpot) ? "the 통로 자리" : "somewhere else entirely")));
        }

        /// <summary>
        /// Every anchor, one at a time, each alone in a world so the simulation's
        /// answer is about that body and no other. The ground under a body is a
        /// valid arrival exactly when that body is what anchors it, so
        /// World.ArrivalValid at the body's own position is the simulation saying
        /// whether this body lends a radius — without ProvidesArrival being read a
        /// second time here.
        /// </summary>
        private static void TheAnchorsAreTheBodiesThatCoverTheGroundUnderThem()
        {
            Anchor("a standing 굴절 기둥",
                w => w.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true));
            Anchor("a 굴절 기둥 still going up",
                w => w.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), false));
            Anchor("a 경계 운반자",
                w => w.SpawnUnit(0, World.CarrierRole, World.CarrierSlot, At(30, 30)));
            Anchor("a 폭풍편", w => w.SpawnUnit(0, Role.Melee, 0, At(30, 30)));
            Anchor("a worker", w => w.SpawnWorker(0, At(30, 30)));
            Anchor("a 층위 관측대", w => w.SpawnBuilding(0, Role.Tech, At(30, 30), true));
            Anchor("the enemy's 굴절 기둥",
                w => w.SpawnBuilding(1, World.PillarRole, World.PillarSlot, At(30, 30), true));

            // The two bodies the fixture always holds. Neither anchors anything, and
            // a 통로 that anchored its own arrival would put every unaimed body on
            // top of the building that made it.
            World world = Passages();
            var into = new List<int>();
            ArrivalOrder.Anchors(world, 0, into);
            Check(!into.Contains(Base), "the 정박한 세계 is drawn as an arrival anchor");
            Check(!into.Contains(Passage), "the 통로 anchors its own arrival");

            // A faction that does not arrive draws no rings at all, which is the
            // Available guard inside Anchors and not a thing the loop would answer:
            // 인간's defense slot 0 is a 대포 standing on the same role and entry.
            var other = new World(Seed);
            other.SetPeerFaction(0, Faction.Humans);
            other.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true);
            ArrivalOrder.Anchors(other, 0, into);
            Check(into.Count == 0,
                "a 인간 peer is drawn " + into.Count + " arrival rings, and the simulation arrives none of them");
            Check(!other.ArrivalValid(0, At(30, 30)),
                "a 인간 defense building anchors an arrival, so the check above proves nothing");
        }

        /// <summary>
        /// One candidate body, alone with a 통로 and a 정박한 세계 that anchor
        /// nothing themselves.
        /// </summary>
        private static void Anchor(string what, Func<World, int> spawn)
        {
            World world = Passages();
            int id = spawn(world);

            var into = new List<int>();
            ArrivalOrder.Anchors(world, 0, into);
            bool drawn = into.Contains(id);
            bool covers = world.ArrivalValid(0, world.GetEntity(id).Position);

            Check(drawn == covers,
                what + ": the client draws a ring = " + drawn +
                " and the simulation arrives a body on the ground under it = " + covers);
        }

        /// <summary>
        /// The list is the caller's, so what is already in it has to go, and the ids
        /// come out in the order the simulation scans them. Both are contracts the
        /// callers lean on: MatchView keeps one list for the life of the match and
        /// draws whatever is in it, so a list that only ever grew would draw a ring
        /// around a 굴절 기둥 that fell minutes ago.
        /// </summary>
        private static void TheAnchorListIsBuiltFreshAndInIdOrder()
        {
            World world = Passages();
            int pillar = world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true);
            // Still going up, so it is not one of the two. Whether it is drawn a
            // ring is asserted above, where it is the only body in the world; here
            // it is what makes the count of two mean something.
            world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(40, 30), false);
            int carrier = world.SpawnUnit(0, World.CarrierRole, World.CarrierSlot, At(50, 30));

            var into = new List<int> { 999, -7 };
            ArrivalOrder.Anchors(world, 0, into);

            Check(into.Count == 2,
                "the anchor list holds " + into.Count + " ids where two anchors are standing" +
                (into.Contains(999) ? ", and what the caller left in it is still there" : ""));
            Check(into[0] == pillar && into[1] == carrier,
                "the anchor list reads " + Show(into) + " where it should read " +
                pillar + ", " + carrier + " by ascending id");
        }

        // Five 지옥불 turrets, standing north of whatever they are aimed at and
        // nowhere near anything else in the fixture. Nine damage every twenty ticks
        // each, so 45 lands every twenty ticks and the 200 hp 굴절 기둥 is down
        // inside a hundred. Positions rather than a loop, for the reason
        // Program.cs's PillarSiege is a table: each one has to be inside reach of
        // six of the body it is killing and outside reach of everything else.
        private static readonly int[][] SiegeOffsets =
        {
            new[] { 0, -4 }, new[] { -1, -4 }, new[] { 1, -4 }, new[] { -2, -3 }, new[] { 2, -3 },
        };

        /// <summary>
        /// A body that has fallen aims at nothing and anchors nothing, which is the
        /// Alive clause of both answers and the one clause a fixture of standing
        /// bodies never reaches. It is also the case the mechanic is written around
        /// — 기둥이 부서지면 그 지역에 대한 접근이 통째로 끊긴다 — so a view that kept
        /// drawing the ring would keep promising a region the simulation has already
        /// taken away.
        ///
        /// Both halves are asserted before the siege as well as after, or a fixture
        /// whose siege never landed would pass by having answered no twice.
        /// </summary>
        private static void ADeadPassageAimsAtNothingAndADeadPillarAnchorsNothing()
        {
            World pillarWorld = Passages();
            int pillar = pillarWorld.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true);
            var into = new List<int>();
            ArrivalOrder.Anchors(pillarWorld, 0, into);
            Check(into.Contains(pillar), "the standing 굴절 기둥 is not drawn a ring, so its death proves nothing");

            Besiege(pillarWorld, 30, 30, pillar, "굴절 기둥");
            ArrivalOrder.Anchors(pillarWorld, 0, into);
            Check(!into.Contains(pillar), "a 굴절 기둥 that has been destroyed is still drawn a ring");
            Check(!pillarWorld.ArrivalValid(0, At(33, 30)),
                "the simulation still arrives bodies at the dead 굴절 기둥, so the check above proves nothing");

            // The 통로 itself, with the 정박한 세계 moved out of reach so the siege
            // has one thing to shoot at.
            var passageWorld = new World(Seed);
            passageWorld.SetPeerFaction(0, Faction.Driftworlds);
            passageWorld.SetPeerFaction(1, Faction.Hellfire);
            passageWorld.SpawnBuilding(0, Role.Base, At(50, 50), complete: true);                  // Base
            passageWorld.SpawnBuilding(0, World.PassageRole, World.PassageSlot, At(20, 20), true); // Passage
            passageWorld.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 40), true);
            passageWorld.GrantResources(0, Bank);

            Check(ArrivalOrder.Aims(passageWorld, 0, Passage),
                "the standing 통로 holds no aim, so its death proves nothing");
            Besiege(passageWorld, 20, 20, Passage, "통로");
            Check(!ArrivalOrder.Aims(passageWorld, 0, Passage), "a 통로 that has been destroyed still holds the aim");

            int before = passageWorld.GetResources(0);
            passageWorld.Step(ProduceAt(Passage, 0, At(33, 40)));
            Check(passageWorld.GetEntity(Passage).QueueCount == 0 && passageWorld.GetResources(0) == before,
                "the destroyed 통로 took an order, so the client agreeing it holds no aim proves nothing");
        }

        /// <summary>
        /// A 통로 that is still going up holds no aim, which is the one answer of the
        /// five that used to be the view's alone. World.IsPassage carried no build
        /// clock while Economy's TryQueueUnit refused BuildTicksLeft &gt; 0 before
        /// AimArrival was ever reached, so the card offered the aim over a building
        /// the simulation would freeze no point on.
        ///
        /// Harmless in the sense that the Produce was refused at both ends, and a
        /// real gap all the same: it is the client promising a setting that the
        /// building it is drawn on cannot hold. Sim/Driftworlds.cs answers it now
        /// rather than this file — a site that is not standing yet keeps no arrival
        /// point for the same reason it supports no population and opens no tier.
        ///
        /// Both halves, before the site stands and after. A check that asked only
        /// the unfinished one would pass by having two answers of no, and would go
        /// on passing if IsPassage were narrowed to nothing at all.
        ///
        /// The second half is a guard rather than a rule of its own, and says so:
        /// TheViewAgreesAboutWhichBodyHoldsTheAim already asserts that a standing
        /// 통로 holds the aim, so nothing breaks this line without breaking that one
        /// first. What it is here for is the fixture — the 통로 above is refused
        /// because the build clock has not run out, and this is where that is
        /// distinguished from being refused for any of the other reasons a fixture
        /// can be wrong.
        /// </summary>
        private static void APassageStillGoingUpHoldsNoAim()
        {
            World world = Rising();
            Check(world.GetEntity(Passage).BuildTicksLeft > 0,
                "the 통로 in this fixture is already standing, so nothing below is about a site going up");
            Check(world.ArrivalValid(0, Aim(0)),
                "no anchor covers the aim, so a refused point would prove nothing about the 통로");

            bool drawnRising = ArrivalOrder.Aims(world, 0, Passage);
            int before = world.GetResources(0);
            world.Step(ProduceAt(Passage, 0, Aim(0)));
            Entity rising = world.GetEntity(Passage);
            Check(rising.QueueCount == 0 && world.GetResources(0) == before,
                "the 통로 still going up took the order, so the aim it does not hold proves nothing");
            Check(drawnRising == rising.HasArrivalPoint,
                "a 통로 still going up: the client says " + drawnRising +
                " and the simulation says " + rising.HasArrivalPoint +
                " about whether it holds the arrival point");

            // The same building once ConstructionSystem has put it down, so the no
            // above is about the build clock and not about the fixture.
            World standing = Rising();
            var idle = new List<Command>();
            int guard = FactionData.BuildTicks(Faction.Driftworlds, World.PassageRole, World.PassageSlot) + 5;
            for (int t = 0; t < guard && standing.GetEntity(Passage).BuildTicksLeft > 0; t++) standing.Step(idle);
            Check(standing.GetEntity(Passage).BuildTicksLeft == 0,
                "the 통로 never finished, so the refusal above proves nothing");

            bool drawnStanding = ArrivalOrder.Aims(standing, 0, Passage);
            standing.Step(ProduceAt(Passage, 0, Aim(0)));
            Entity done = standing.GetEntity(Passage);
            Check(done.QueueCount == 1,
                "the finished 통로 refused the order, so the aim it holds proves nothing");
            Check(drawnStanding && done.HasArrivalPoint,
                "the same 통로 once it has finished is offered no aim — the client says " +
                drawnStanding + " and the simulation says " + done.HasArrivalPoint +
                " — so the two noes above are IsPassage refusing everything rather than " +
                "the build clock");
        }

        /// <summary>
        /// A 정박한 세계 and a 굴절 기둥 standing, and the 통로 still going up.
        /// The ids are the ones Sweep() hands out for the first three bodies,
        /// because the 통로 is spawned in the same order — unfinished rather than
        /// absent.
        /// </summary>
        private static World Rising()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Driftworlds);
            world.SetPeerFaction(1, Faction.Hellfire);

            world.SpawnBuilding(0, Role.Base, At(10, 10), complete: true);                    // Base
            world.SpawnBuilding(0, World.PassageRole, World.PassageSlot, At(14, 10), false);  // Passage
            world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true);     // Pillar
            world.GrantResources(0, Bank);
            return world;
        }

        /// <summary>
        /// Stands 지옥불 turrets around a cell and steps until what is on it is
        /// down. A tick budget rather than a loop that stops when it works: a siege
        /// that had quietly stopped landing would otherwise run forever, and this
        /// would never say so.
        /// </summary>
        private static void Besiege(World world, int x, int y, int victim, string what)
        {
            for (int i = 0; i < SiegeOffsets.Length; i++)
            {
                world.SpawnBuilding(1, Role.Defense, At(x + SiegeOffsets[i][0], y + SiegeOffsets[i][1]), true);
            }

            var idle = new List<Command>();
            for (int t = 0; t < 600 && world.GetEntity(victim).Alive; t++) world.Step(idle);
            Check(!world.GetEntity(victim).Alive,
                "the siege never brought the " + what + " down, so nothing below is about a dead body");
        }

        /// <summary>
        /// A 정박한 세계 and a 통로, and nothing that anchors anything. The floor
        /// every anchor fixture is built on, so the only body lending a radius is
        /// the one the case put there.
        /// </summary>
        private static World Passages()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Driftworlds);
            world.SetPeerFaction(1, Faction.Hellfire);

            world.SpawnBuilding(0, Role.Base, At(10, 10), complete: true);                   // Base
            world.SpawnBuilding(0, World.PassageRole, World.PassageSlot, At(14, 10), true);  // Passage
            world.GrantResources(0, Bank);
            return world;
        }

        /// <summary>The same world with a 굴절 기둥 or a 경계 운반자 on (30, 30).</summary>
        private static World Anchored(bool carrier)
        {
            World world = Passages();
            if (carrier) world.SpawnUnit(0, World.CarrierRole, World.CarrierSlot, At(30, 30));
            else world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true);
            return world;
        }

        private static List<Command> ProduceAt(int building, int peer, FixVec2 arrival) =>
            new List<Command>
            {
                new Command(0, peer, 0, CommandType.Produce, building, arrival,
                    Command.RosterArg(Role.Melee, 0))
            };

        /// <summary>
        /// Steps idle until the queue puts one body down, and answers the body. The
        /// id is the count before the step, because ids are handed out in order and
        /// never reused.
        /// </summary>
        private static Entity StepToArrival(World world, string what)
        {
            var idle = new List<Command>();
            int standing = world.EntityCount;
            int budget = FactionData.Production(Faction.Driftworlds, Role.Melee, 0).Ticks + 5;
            for (int t = 0; t < budget && world.EntityCount == standing; t++) world.Step(idle);

            Check(world.EntityCount == standing + 1,
                "the body aimed " + what + " never arrived: " + world.EntityCount +
                " bodies where " + (standing + 1) + " were due");
            return world.GetEntity(standing);
        }

        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }

        private static string Show(FixVec2 p) => "(" + p.X.ToInt() + ", " + p.Y.ToInt() + ")";

        private static string Show(List<int> ids)
        {
            var text = "";
            for (int i = 0; i < ids.Count; i++) text += (i > 0 ? ", " : "") + ids[i];
            return text.Length == 0 ? "nothing" : text;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
