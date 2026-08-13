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
    /// </summary>
    public static class HitDetection
    {
        // Hp last seen, per entity id. Entities are appended and never removed
        // and a new one arrives at full hp (CLAUDE.md: ids are never reused),
        // so seeding from the entity itself cannot fake a hit.
        private static readonly List<int> lastHp = new List<int>();

        private static readonly List<FixVec2> points = new List<FixVec2>();

        /// <summary>The world the state above belongs to. A new one makes it a lie.</summary>
        private static World shown;

        /// <summary>
        /// Tick the list below was last computed for. Hp cannot fall a second
        /// time before the simulation advances another tick, so a call at the
        /// same tick returns the cached list rather than walking every entity
        /// again — the same trick Minimap already plays on Fog.Version.
        /// </summary>
        private static int computedTick = -1;

        /// <summary>
        /// World points the local peer's own entities were hit at since the
        /// last tick this was asked for, excluding anything <paramref
        /// name="isOnScreen"/> already answers true for. Owned by whoever calls
        /// it first in a given tick; every later caller in that same tick gets
        /// the same list back and <paramref name="isOnScreen"/> is not asked
        /// again until the next one.
        /// </summary>
        public static IReadOnlyList<FixVec2> OffScreen(World world, int localPeer, Func<FixVec2, bool> isOnScreen)
        {
            if (!ReferenceEquals(world, shown))
            {
                // A restart hands out ids from zero again, so hp held across
                // one belongs to nothing on the new one.
                shown = world;
                lastHp.Clear();
                computedTick = -1;
            }

            if (computedTick == world.Tick) return points;
            computedTick = world.Tick;

            points.Clear();
            for (int i = lastHp.Count; i < world.EntityCount; i++) lastHp.Add(world.GetEntity(i).Hp);

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                int was = lastHp[i];
                lastHp[i] = e.Hp;

                if (e.Hp >= was || e.Owner != localPeer) continue;
                if (isOnScreen(e.Position)) continue;
                points.Add(e.Position);
            }
            return points;
        }
    }
}
