using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// 차원 유랑종 통로 조준: what the client asks before it offers the aim, before
    /// it tints the cursor, and before it draws the anchors the radius belongs to.
    ///
    /// Pulled out on its own, free of UnityEngine, for the reason
    /// <see cref="CaptureOrder"/>, ProductionMenu and SelectionMatch were: the one
    /// part of this feature a graphics device cannot see is whether the view's
    /// answer still matches the simulation's, and that is a thing a headless
    /// harness can hold to. Orders.cs, CommandCard.cs, Hud.cs and MatchView.cs are
    /// the Unity-facing halves that turn these answers into a key, a button, a
    /// tint and a mark.
    ///
    /// Every answer here is read off <see cref="World"/> rather than re-typed.
    /// Sim/Driftworlds.cs made <see cref="World.ArrivalValid"/> public for exactly
    /// this cursor and says why: "the answer that counts is the one taken inside
    /// the simulation; a view that asked a moment earlier is looking at a world one
    /// tick stale, which is the caveat CanBuild carries." So nothing in this file
    /// gates anything. It decides what to draw, and the drawing is a guess.
    ///
    /// The guess is wider here than it is for a placement ghost, and that is the
    /// mechanic rather than a defect. A ghost is asked and answered in the same
    /// click; an arrival point is asked when the cursor sweeps it, again when the
    /// simulation takes the Produce, and a third time when the body stands up.
    /// docs/FACTION-MECHANICS.md and Sim/Driftworlds.cs both say what happens when
    /// the last answer is no — 무효면 통로 자리에 출현한다 — so the view's job is to
    /// keep showing the answer as it stands and never to pretend it is settled.
    /// </summary>
    public static class ArrivalOrder
    {
        /// <summary>
        /// Whether this faction aims at all. Read off World.PassageFaction rather
        /// than named again here, so the card cannot come to disagree with the rule
        /// that decides where a body stands up.
        /// </summary>
        public static bool Available(Faction faction) => faction == World.PassageFaction;

        /// <summary>
        /// 반경, straight off the simulation. The rings MatchView draws are this
        /// number and nothing else, so a radius retuned in Sim moves the drawing
        /// with it.
        /// </summary>
        public static Fix Radius => World.ArrivalRadius;

        /// <summary>
        /// Whether this selected body is one that keeps an arrival point: a live
        /// 통로 belonging to this peer and standing. World.IsPassage is the test
        /// itself, exactly as CaptureOrder leans on World.IsNeutralRock rather than
        /// reading Role and Slot a second time — the build clock included, which is
        /// why a 통로 still going up is offered no aim here without this file ever
        /// naming BuildTicksLeft. It could hold no point if it were offered one:
        /// the Produce that would freeze the point is refused while the site is
        /// still going up.
        ///
        /// The building matters as well as the faction, because the simulation says
        /// so: "Nothing but a 통로 keeps an arrival point. A Base can produce and
        /// 차원 유랑종's is 정박한 세계." An Arrive cell live on a Base would be a
        /// cell that quietly does nothing, which is the failure the dead 징발 cell
        /// exists to avoid.
        /// </summary>
        public static bool Aims(World world, int peer, int entityId)
        {
            if (entityId < 0 || entityId >= world.EntityCount) return false;

            Entity e = world.GetEntity(entityId);
            if (!e.Alive || e.Owner != peer) return false;
            return world.IsPassage(e);
        }

        /// <summary>
        /// Whether a body ordered now would stand up at this point. The simulation's
        /// own rule, asked of the world as the view last saw it — a tick behind the
        /// one the command will meet, and several seconds behind the one the body
        /// will arrive in. Feedback, never a gate.
        /// </summary>
        public static bool Valid(World world, int peer, FixVec2 point) => world.ArrivalValid(peer, point);

        /// <summary>
        /// Every body lending this peer a radius right now, by ascending entity id.
        /// The circles the player aims inside, so the region is something they can
        /// see rather than something they sweep the cursor for.
        ///
        /// World.ProvidesArrival is the test, so a 굴절 기둥 still under construction
        /// draws no ring — which is the same answer the cursor gives over it, for
        /// the same reason, out of the same method.
        ///
        /// Fills a list the caller owns rather than returning one, because this runs
        /// every frame while the aim is armed and a fresh List a frame is garbage
        /// the frame after.
        /// </summary>
        public static void Anchors(World world, int peer, List<int> into)
        {
            into.Clear();
            if (!Available(world.FactionOf(peer))) return;

            for (int i = 0; i < world.EntityCount; i++)
            {
                if (world.ProvidesArrival(world.GetEntity(i), peer)) into.Add(i);
            }
        }
    }
}
