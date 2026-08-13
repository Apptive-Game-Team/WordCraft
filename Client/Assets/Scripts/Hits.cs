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
    ///
    /// The watching itself is HitDetection, which never touches UnityEngine so
    /// Replay can check it headless (issue #96). This file is only the Camera
    /// adapter left behind: it answers "is this world point on screen" with a
    /// viewport test and converts HitDetection's world points to the view space
    /// Minimap and Alert already draw in.
    /// </summary>
    public static class Hits
    {
        private static readonly List<Vector2> viewPoints = new List<Vector2>();

        /// <summary>
        /// World points the local peer's own entities were hit at, off the
        /// screen <paramref name="cam"/> can see, since the last tick this was
        /// asked for. See HitDetection.OffScreen for the once-per-tick caching
        /// this rests on; only the on-screen test and the coordinate space are
        /// this file's own.
        /// </summary>
        public static IReadOnlyList<Vector2> OffScreen(World world, int localPeer, Camera cam)
        {
            IReadOnlyList<FixVec2> hits = HitDetection.OffScreen(world, localPeer,
                p => cam != null && OnScreen(cam, MatchRunner.ToView(p)));

            viewPoints.Clear();
            for (int i = 0; i < hits.Count; i++) viewPoints.Add(MatchRunner.ToView(hits[i]));
            return viewPoints;
        }

        private static bool OnScreen(Camera cam, Vector2 point)
        {
            Vector3 v = cam.WorldToViewportPoint(point);
            return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }
    }
}
