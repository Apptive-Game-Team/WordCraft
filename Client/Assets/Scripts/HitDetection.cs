using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The hp-drop half of what Hits watches for, pulled out so it can run
    /// without a Camera or a scene (issue #96). Recording each entity's last
    /// seen hp and flagging the local peer's own drops is plain arithmetic over
    /// World; it never needed UnityEngine, only a place to answer "is this
    /// world point already visible" that used to be Camera.WorldToViewportPoint
    /// baked in. That question is now the caller's to answer, so Replay can
    /// compile this file directly (see Replay/Replay.csproj, the same way it
    /// already compiles MatchScenario.cs and ScriptedLog.cs) and check the
    /// rules headless.
    ///
    /// Issue #104 adds the on-screen half: a hit inside the view a player is
    /// already looking at had no signal of its own, only the shrinking bar
    /// Minimap and Alert already found insufficient for off-screen ones. It
    /// wants the same "who got hit this tick" answer this file already
    /// computes for the off-screen case, not a second walk of every entity —
    /// see <see cref="HitIds"/>.
    /// </summary>
    public static class HitDetection
    {
        // Hp last seen, per entity id. Entities are appended and never removed
        // and a new one arrives at full hp (CLAUDE.md: ids are never reused),
        // so seeding from the entity itself cannot fake a hit.
        private static readonly List<int> lastHp = new List<int>();

        /// <summary>
        /// Entity ids of the local peer's own entities whose hp fell since the
        /// last tick this was asked for, unfiltered by where they are on
        /// screen. The scan every caller shares: OffScreen below turns this
        /// into world points and drops the ones a camera can already see; the
        /// on-screen flash (issue #104) wants exactly the ones OffScreen
        /// throws away, so it reads this list directly instead of re-deriving
        /// "on screen" as the negation of a filter built to exclude it.
        /// </summary>
        private static readonly List<int> hitIds = new List<int>();

        private static readonly List<FixVec2> points = new List<FixVec2>();

        /// <summary>The world the state above belongs to. A new one makes it a lie.</summary>
        private static World shown;

        /// <summary>
        /// Tick <see cref="hitIds"/> was last computed for. Hp cannot fall a
        /// second time before the simulation advances another tick, so a call
        /// at the same tick reads the list already built rather than walking
        /// every entity again — the same trick Minimap already plays on
        /// Fog.Version.
        /// </summary>
        private static int computedTick = -1;

        /// <summary>
        /// The ids <see cref="Scan"/> built this tick. Screen filtering is
        /// cheap enough to redo on every call — a tick's hits are a handful of
        /// entities, never World.EntityCount — so only the hp scan itself is
        /// cached, and whichever of OffScreen or this is asked first in a tick
        /// does not rob the other of a fresh answer.
        /// </summary>
        public static IReadOnlyList<int> HitIds(World world, int localPeer)
        {
            Scan(world, localPeer);
            return hitIds;
        }

        /// <summary>
        /// World points the local peer's own entities were hit at since the
        /// last tick this was asked for, excluding anything <paramref
        /// name="isOnScreen"/> already answers true for.
        /// </summary>
        public static IReadOnlyList<FixVec2> OffScreen(World world, int localPeer, Func<FixVec2, bool> isOnScreen)
        {
            Scan(world, localPeer);

            points.Clear();
            for (int i = 0; i < hitIds.Count; i++)
            {
                FixVec2 p = world.GetEntity(hitIds[i]).Position;
                if (isOnScreen(p)) continue;
                points.Add(p);
            }
            return points;
        }

        private static void Scan(World world, int localPeer)
        {
            if (!ReferenceEquals(world, shown))
            {
                // A restart hands out ids from zero again, so hp held across
                // one belongs to nothing on the new one.
                shown = world;
                lastHp.Clear();
                computedTick = -1;
            }

            if (computedTick == world.Tick) return;
            computedTick = world.Tick;

            hitIds.Clear();
            for (int i = lastHp.Count; i < world.EntityCount; i++) lastHp.Add(world.GetEntity(i).Hp);

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                int was = lastHp[i];
                lastHp[i] = e.Hp;

                if (e.Hp >= was || e.Owner != localPeer) continue;
                hitIds.Add(i);
            }
        }
    }
}
