using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The one place that turns the simulation's flat grid into a solid in three
    /// dimensions, and the only place allowed to answer "how high is the ground
    /// here". Everything the view draws on the map — a body, a ring, a marker, a
    /// mesh chunk, the pointer — asks this, so the field has exactly one shape
    /// and nothing can disagree with the mesh the player is looking at.
    ///
    /// The mapping, once, here: the simulation is two dimensional and its axes are
    /// x and y. The view is three dimensional and lays that grid flat, so sim x is
    /// world x, sim y is world z, and world y is height. Nothing below this line
    /// should ever read a sim coordinate as a world one; <see cref="At"/> and
    /// <see cref="Plane"/> are the whole of the conversion.
    ///
    /// Reads the world and writes nothing to it, like the rest of the view.
    /// </summary>
    public static class Field
    {
        /// <summary>
        /// World units one height level stands up. A cliff is two levels
        /// (Sim/World.cs StepPassable), so this is half of what a cliff has to be
        /// before it reads as one: a unit is about 1.8 cells across and a wall
        /// shorter than a body does not look like a wall, it looks like a kerb the
        /// pathfinder is being precious about. Three levels of mountain come out
        /// at 2.55, which is a ridge that still lets the camera see over it.
        /// </summary>
        public const float LevelHeight = 0.85f;

        /// <summary>
        /// How far below its cell's own level a lake's water surface sits. Water
        /// is impassable, so the simulation never asks what level it is; the view
        /// sinks it so a lake is a hollow with water in it rather than a blue
        /// floor flush with the grass beside it.
        /// </summary>
        public const float WaterDrop = 0.30f;

        /// <summary>How much further down the lake bed is, under that surface.</summary>
        public const float WaterDepth = 0.45f;

        /// <summary>
        /// A rock cell's top, past its level. The spine the scenario paints is
        /// impassable and the pathfinder cannot route through it, so its height is
        /// free for the view to spend: this lifts it into a ridge that reads as a
        /// mountain from across the map instead of as a grey floor.
        /// </summary>
        public const float RockLift = 0.55f;

        /// <summary>Cells on a side. The simulation's grid, named here so a mesh builder need not reach into Sim.</summary>
        public const int Cells = World.GridSize;

        /// <summary>The world this was shaped from. A new one means a new map.</summary>
        private static World shaped;

        /// <summary>
        /// Four corner heights per cell, in world units, in <see cref="Corner"/>
        /// order. Per cell and not per grid corner on purpose: a cliff is a place
        /// where two cells meet at different heights and the ground between them
        /// is vertical, so the corner they share is two different heights and a
        /// single value per grid corner cannot hold both. Four per cell holds a
        /// smooth slope and a sheer face in the same array, and the mesh builder
        /// reads exactly what the height query reads.
        /// </summary>
        private static float[] corners = new float[World.GridCells * 4];

        /// <summary>Per cell, the surface a body or a marker stands on. Water and rock carry their own rule.</summary>
        private static float[] surface = new float[World.GridCells];

        /// <summary>Whether anything has been shaped yet. The start screen has no map.</summary>
        public static bool Ready => shaped != null;

        /// <summary>
        /// Shapes the field for a world, or returns at once if it is already the
        /// one that was shaped. Terrain and height never change after the map is
        /// built (Sim/World.cs SetTerrain, SetHeight), so this runs once a match.
        /// </summary>
        public static void Build(World world)
        {
            if (ReferenceEquals(world, shaped)) return;
            shaped = world;

            for (int cell = 0; cell < World.GridCells; cell++)
            {
                int cx = cell % Cells;
                int cy = cell / Cells;
                for (int c = 0; c < 4; c++) corners[cell * 4 + c] = CornerHeight(world, cx, cy, c);
                surface[cell] = SurfaceOf(world, cell);
            }
        }

        /// <summary>
        /// A cell's four corners, in the order the mesh builder and the height
        /// query both read them. Laid out by the sign of the offset from the
        /// cell's own origin, so corner c covers (cx + c&amp;1, cy + c>>1):
        /// 0 is -x -z, 1 is +x -z, 2 is -x +z, 3 is +x +z.
        /// </summary>
        public static float Corner(int cell, int corner) => corners[cell * 4 + corner];

        /// <summary>The height one corner of one cell stands at, in world units.</summary>
        /// <remarks>
        /// A corner averages the levels of the cells that touch it, which is what
        /// turns a one level difference into a slope a unit walks up rather than a
        /// step it teleports onto. The average is taken only over cells within one
        /// level of this one: past that the simulation calls the step a cliff and
        /// refuses it, and the view has to agree, so the cell stays flat at its own
        /// level right up to its edge and the neighbour does the same at its own.
        /// The gap between the two is the cliff face, and FieldMesh fills it.
        ///
        /// Rock and water are not averaged into anything and do not average
        /// anything in. They carry their own surface rule (<see cref="SurfaceOf"/>)
        /// and letting them bend a neighbouring slope would drag the grass down
        /// into the lake and up into the ridge.
        /// </remarks>
        private static float CornerHeight(World world, int cx, int cy, int corner)
        {
            int ox = (corner & 1) == 0 ? -1 : 1;
            int oy = (corner & 2) == 0 ? -1 : 1;

            int own = world.HeightAt(cy * Cells + cx);
            bool plain = world.TerrainAt(cy * Cells + cx) == TileKind.Open;

            float sum = own;
            int count = 1;
            for (int i = 1; i < 4; i++)
            {
                // The three other cells that share this corner: one across x, one
                // across y, one diagonal.
                int nx = cx + ((i & 1) != 0 ? ox : 0);
                int ny = cy + ((i & 2) != 0 ? oy : 0);
                if (nx < 0 || ny < 0 || nx >= Cells || ny >= Cells) continue;

                int neighbour = ny * Cells + nx;
                if (plain && world.TerrainAt(neighbour) != TileKind.Open) continue;

                int level = world.HeightAt(neighbour);
                if (Abs(level - own) > 1) continue; // a cliff, not a slope
                sum += level;
                count++;
            }

            return sum / count * LevelHeight;
        }

        /// <summary>
        /// The one surface a cell offers whatever is standing on it. Open ground
        /// is its own corners, so a body on a slope sits on the slope. Water is
        /// the lake's surface, because a marker dropped on a lake belongs on the
        /// water and not on the bed under it. Rock is its lifted top.
        /// </summary>
        private static float SurfaceOf(World world, int cell)
        {
            float level = world.HeightAt(cell) * LevelHeight;
            switch (world.TerrainAt(cell))
            {
                case TileKind.Water: return level - WaterDrop;
                case TileKind.Rock: return level + RockLift;
                default: return level;
            }
        }

        /// <summary>The lake bed under a water cell, which is the only thing that draws below the surface.</summary>
        public static float Bed(int cell) => surface[cell] - WaterDepth;

        /// <summary>A cell's flat top, for the cells that have one: water and rock.</summary>
        public static float Flat(int cell) => surface[cell];

        /// <summary>
        /// How high the ground is at a point on the plane, in world units.
        ///
        /// Open ground is interpolated across the cell's four corners, so a unit
        /// walking a slope rises smoothly instead of hopping at every cell
        /// boundary. Water and rock are flat, because their surface is a rule and
        /// not a set of corners.
        /// </summary>
        public static float Height(float x, float z)
        {
            if (shaped == null) return 0f;

            int cx = Clamp((int)Mathf.Floor(x), 0, Cells - 1);
            int cy = Clamp((int)Mathf.Floor(z), 0, Cells - 1);
            int cell = cy * Cells + cx;

            if (shaped.TerrainAt(cell) != TileKind.Open) return surface[cell];

            float fx = Clamp01(x - cx);
            float fz = Clamp01(z - cy);
            float c0 = corners[cell * 4 + 0];
            float c1 = corners[cell * 4 + 1];
            float c2 = corners[cell * 4 + 2];
            float c3 = corners[cell * 4 + 3];

            // Not bilinear. The mesh draws a tilted cell as two triangles, and a
            // bilinear patch over the same four corners bows away from them by up
            // to a fifth of a unit in the middle of a cell with one raised corner —
            // enough for a body standing there to float or sink. So this walks the
            // same two triangles, split along the same diagonal, and a body stands
            // on the facet the player sees.
            if (MainDiagonal(c0, c1, c2, c3))
            {
                return fx >= fz
                    ? c0 + (c1 - c0) * fx + (c3 - c1) * fz
                    : c0 + (c2 - c0) * fz + (c3 - c2) * fx;
            }
            return fx + fz <= 1f
                ? c0 + (c1 - c0) * fx + (c2 - c0) * fz
                : c3 + (c1 - c3) * (1f - fz) + (c2 - c3) * (1f - fx);
        }

        /// <summary>
        /// Which way a tilted cell is split: true for the diagonal from corner 0
        /// to corner 3, false for corner 1 to corner 2. Whichever joins the two
        /// corners closest in height, so the crease between the two facets is as
        /// shallow as the corners allow and a ramp reads as one surface rather
        /// than a pyramid. The one rule, shared by <see cref="Height"/> and the
        /// mesh builder, so they cannot pick different folds.
        /// </summary>
        public static bool MainDiagonal(float h0, float h1, float h2, float h3) =>
            Mathf.Abs(h0 - h3) <= Mathf.Abs(h1 - h2);

        /// <summary>Same question, asked with a plane point.</summary>
        public static float Height(Vector2 plane) => Height(plane.x, plane.y);

        /// <summary>
        /// A plane point as a point in the world, sitting on the ground. This and
        /// <see cref="Plane"/> are the whole of the two-to-three dimension
        /// conversion; nothing else should be assembling a Vector3 out of a sim
        /// coordinate.
        /// </summary>
        public static Vector3 At(Vector2 plane) => new Vector3(plane.x, Height(plane.x, plane.y), plane.y);

        /// <summary>The same, lifted clear of the ground — a decal off the surface, a bar over a head.</summary>
        public static Vector3 At(Vector2 plane, float lift) =>
            new Vector3(plane.x, Height(plane.x, plane.y) + lift, plane.y);

        /// <summary>A world point back on the plane the simulation works in.</summary>
        public static Vector2 Plane(Vector3 world) => new Vector2(world.x, world.z);

        /// <summary>
        /// Which way is up at a point, for sitting a body or a decal flat on a
        /// slope. Read off the cell's corners by difference rather than from a
        /// stored normal, so it cannot drift from <see cref="Height"/>.
        /// </summary>
        public static Vector3 Normal(float x, float z)
        {
            const float step = 0.25f;
            float dx = Height(x + step, z) - Height(x - step, z);
            float dz = Height(x, z + step) - Height(x, z - step);
            return new Vector3(-dx, 2f * step, -dz).normalized;
        }

        /// <summary>The level the simulation gave a cell, for a mesh that wants the band and not the height.</summary>
        public static int Level(int cell) => shaped == null ? 0 : shaped.HeightAt(cell);

        /// <summary>What a cell is made of, so a mesh builder need not hold its own copy of the world.</summary>
        public static TileKind Kind(int cell) => shaped == null ? TileKind.Open : shaped.TerrainAt(cell);

        /// <summary>
        /// Whether the step between two edge-sharing cells is a cliff — the same
        /// question Sim/World.cs StepPassable asks, asked by the view so a mesh
        /// can put a face where the simulation put a wall. The simulation stays
        /// the authority: this reads its heights and does not re-decide anything.
        /// </summary>
        public static bool Cliff(int a, int b) => shaped != null && Abs(shaped.HeightAt(a) - shaped.HeightAt(b)) > 1;

        /// <summary>
        /// Where a screen point lands on the ground, as a plane point. This is what
        /// replaced Camera.ScreenToWorldPoint everywhere the view used to read a
        /// cursor: that call answers for a plane at a fixed depth, which is the
        /// right answer for an orthographic camera pointed down a flat map and the
        /// wrong one for a tilted camera over a map with hills in it.
        ///
        /// Marched rather than solved. The surface is piecewise bilinear with
        /// vertical faces cut into it, so there is no closed form, and a march that
        /// samples the same <see cref="Height"/> every other caller reads cannot
        /// disagree with the mesh the player is clicking on. Coarse steps to find
        /// the crossing, then a handful of bisections to land on it.
        /// </summary>
        public static bool Pointer(Camera cam, Vector2 screen, out Vector2 plane)
        {
            plane = Vector2.zero;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screen);

            // Nothing on the map stands above this, so a ray already under it on
            // its way down has not missed anything by starting here.
            const float ceiling = World.MaxHeight * LevelHeight + RockLift + 2f;
            float t = 0f;
            if (ray.origin.y > ceiling)
            {
                if (ray.direction.y >= 0f) return false; // pointed at the sky
                t = (ray.origin.y - ceiling) / -ray.direction.y;
            }

            const float step = 0.35f;
            const float far = 400f;

            float prev = t;
            float prevGap = Gap(ray, t);
            if (prevGap <= 0f)
            {
                // Started inside the ground, which happens when the camera is low
                // on a hillside. The point under the cursor is where the camera is.
                plane = Plane(ray.GetPoint(t));
                return true;
            }

            for (t += step; t < far; t += step)
            {
                float gap = Gap(ray, t);
                if (gap > 0f)
                {
                    prev = t;
                    prevGap = gap;
                    continue;
                }

                // Crossed. Bisect between the last point above ground and this one.
                float lo = prev;
                float hi = t;
                for (int i = 0; i < 20; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Gap(ray, mid) > 0f) lo = mid; else hi = mid;
                }
                plane = Plane(ray.GetPoint((lo + hi) * 0.5f));
                plane.x = Mathf.Clamp(plane.x, 0f, Cells);
                plane.y = Mathf.Clamp(plane.y, 0f, Cells);
                return true;
            }

            _ = prevGap;
            return false;
        }

        /// <summary>How far the ray is above the ground at t. Negative means under it.</summary>
        private static float Gap(Ray ray, float t)
        {
            Vector3 p = ray.GetPoint(t);
            return p.y - Height(p.x, p.z);
        }

        private static int Abs(int v) => v < 0 ? -v : v;

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
