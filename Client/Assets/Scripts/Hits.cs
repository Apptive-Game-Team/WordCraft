using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Something of the local peer's taking damage somewhere the camera is not
    /// looking, read off hp falling between ticks. A hit is not simulation
    /// state — Sim may not grow a field to say it happened — so this is the
    /// one place that watches for it.
    ///
    /// Minimap turns the result into a fading mark on the map; the alert turns
    /// it into a sentence. Both want the same answer, so both read this
    /// instead of each walking every entity a second time for it.
    /// </summary>
    public static class Hits
    {
        // Hp last seen, per entity id. Entities are appended and never removed
        // and a new one arrives at full hp (CLAUDE.md: ids are never reused),
        // so seeding from the entity itself cannot fake a hit.
        private static readonly List<int> lastHp = new List<int>();

        private static readonly List<Vector2> points = new List<Vector2>();

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
        /// World points the local peer's own entities were hit at, off the
        /// screen <paramref name="cam"/> can see, since the last tick this was
        /// asked for. Owned by whoever calls it first in a given tick; every
        /// later caller in that same tick gets the same list back.
        /// </summary>
        public static IReadOnlyList<Vector2> OffScreen(World world, int localPeer, Camera cam)
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
                Vector2 p = MatchRunner.ToView(e.Position);
                if (cam != null && OnScreen(cam, p)) continue;
                points.Add(p);
            }
            return points;
        }

        private static bool OnScreen(Camera cam, Vector2 point)
        {
            Vector3 v = cam.WorldToViewportPoint(point);
            return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }
    }
}
