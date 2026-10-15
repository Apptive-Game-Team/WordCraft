using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The map as solid ground: grass that climbs in ramps, cliff faces where
    /// the simulation put a wall, a ridge of rock down the spine, lakes sunk into
    /// hollows with water over a bed, and the whole board standing on a plinth
    /// like a diorama on a table.
    ///
    /// Built once a match from <see cref="Field"/> and never touched again, for the
    /// same reason the 2D bake was: terrain and height are fixed when the map is
    /// built (Sim/World.cs SetTerrain, SetHeight), so the cost is paid at load and
    /// a frame costs only the draw. Every height here is read from Field, which is
    /// what every body, ring and pointer on the map also reads, so the ground a
    /// unit stands on and the ground the player sees cannot disagree.
    ///
    /// Flat shaded on purpose. No vertex is shared between two faces and every
    /// face carries its own normal, so each facet takes one of Paper.cginc's three
    /// value bands whole and the terrain reads as cut paper. Shared vertices would
    /// smooth the normals across a ridge and turn every band boundary into a
    /// gradient, which is the airbrushed look the style rules out.
    ///
    /// All colour is vertex colour and all opaque terrain is one material, so the
    /// 64 chunks batch and a repaint is a change to <see cref="FieldPalette"/>
    /// rather than to any material.
    ///
    /// Reads the world and writes nothing to it, like the rest of the view.
    /// </summary>
    public static class FieldMesh
    {
        /// <summary>
        /// Cells on a chunk's side. Eight gives 64 chunks: few enough draws to be
        /// nothing, small enough that the camera culls the far side of the map,
        /// and a worst-case chunk stays a few thousand vertices, nowhere near the
        /// 65535 a 16 bit index buffer holds.
        /// </summary>
        public const int ChunkCells = 8;

        /// <summary>
        /// Where the plinth stops, in world units. About two levels under the
        /// lowest ground, deep enough that the board reads as a block standing in
        /// space and not as a sheet of paper cut off by nothing.
        /// </summary>
        public const float Base = -1.5f;

        /// <summary>How far the base slab reaches past the plinth, in cells. A lip, so the plinth stands on something.</summary>
        private const float SlabOverhang = 0.4f;

        /// <summary>How thick the base slab is.</summary>
        private const float SlabThickness = 0.3f;

        /// <summary>
        /// The smallest height step that gets a wall. Below this the two sides of
        /// an edge are the same ground and a face between them would be a sliver
        /// the shadow map flickers on.
        /// </summary>
        private const float Epsilon = 0.01f;

        /// <summary>
        /// How far a rock top corner may move up or down. Enough to crumple the
        /// ridge's top into facets that each catch the light differently, small
        /// enough that the ridge is still one mass and not a field of spikes.
        /// </summary>
        private const float RockJitter = 0.13f;

        /// <summary>
        /// The drop over which a wall goes from its lip colour all the way to its
        /// foot colour. Two levels: a full cliff reaches the dark foot, and the
        /// small steps where a ramp meets a flat stay mostly lip coloured instead
        /// of each being a little dark stripe.
        /// </summary>
        private const float FootDepth = 2f * Field.LevelHeight;

        /// <summary>
        /// The smallest rise that gets strata. Past one level a step is a cliff
        /// (Sim/World.cs StepPassable), and where a plateau corner leans toward its
        /// ramp a cliff can come out a little under a level tall, so the cut is at
        /// 0.9 rather than at 0.85: the seams where a ramp meets a flat stay one
        /// plain face, and every face the simulation calls a wall gets the bands.
        /// </summary>
        private const float StrataMin = 0.9f;

        /// <summary>
        /// Height of one stratum, in world units, measured from world zero and not
        /// from each wall's foot. Absolute so that the bands of two walls side by
        /// side are the same bands: the layers run round a plateau as one rock
        /// rather than restarting at every cell. Two thirds of a level gives a
        /// two level cliff three bands and the ridge five or six.
        /// </summary>
        private const float Stratum = 0.55f;

        /// <summary>How close a stratum line may come to a wall's foot or rim before it is dropped. Keeps every band a band and not a sliver.</summary>
        private const float StrataMargin = 0.12f;

        /// <summary>
        /// How tall the light lip along a cliff's top edge is. A thin strip, so it
        /// reads as the cut edge of the paper catching the sun and not as a band
        /// of its own.
        /// </summary>
        private const float RimDepth = 0.09f;

        /// <summary>How far the lip's lower edge leans out from the wall, mid edge. Tilts the strip toward the sky so it lands in the highlight band.</summary>
        private const float RimLean = 0.05f;

        /// <summary>
        /// How far an inner stratum vertex may move off the wall's plane. Enough
        /// that each band of each cell catches the light at its own angle and the
        /// wall stops reading as the side of a box, small enough that it never
        /// reaches a body standing at its foot.
        /// </summary>
        private const float FaceJitter = 0.11f;

        /// <summary>The world this was built from. A new one means a new map.</summary>
        private static World built;

        private static GameObject root;

        /// <summary>Every mesh made here. A Mesh is an engine object the GC never frees, so a rebuild destroys these by hand.</summary>
        private static readonly List<Mesh> meshes = new List<Mesh>();

        private static Material groundMaterial;
        private static Material waterMaterial;

        /// <summary>
        /// Builds the terrain for a world under parent, or returns at once if it
        /// is already built for that world. A different world drops the old
        /// terrain first. Vertices are written in world units with each chunk at
        /// its parent's origin, so parent should sit at the world origin unscaled.
        /// </summary>
        public static void Build(World world, Transform parent)
        {
            if (world == null) return;
            if (ReferenceEquals(world, built) && root != null) return;

            Drop();
            built = world;

            // Already done by the caller; a second call for the same world returns
            // at once, and it keeps this from ever building from a stale field.
            Field.Build(world);

            Materials();
            root = new GameObject("Terrain");
            if (parent != null) root.transform.SetParent(parent, false);

            var solid = new Facets();
            var surface = new Facets();

            for (int y0 = 0; y0 < Field.Cells; y0 += ChunkCells)
            {
                for (int x0 = 0; x0 < Field.Cells; x0 += ChunkCells)
                {
                    solid.Clear();
                    surface.Clear();

                    int x1 = Mathf.Min(x0 + ChunkCells, Field.Cells);
                    int y1 = Mathf.Min(y0 + ChunkCells, Field.Cells);
                    for (int cy = y0; cy < y1; cy++)
                    {
                        for (int cx = x0; cx < x1; cx++)
                        {
                            Cell(solid, surface, cx, cy);
                            FieldProps.Add(solid, cx, cy);
                        }
                    }

                    string at = (x0 / ChunkCells) + "," + (y0 / ChunkCells);
                    Emit("Ground " + at, solid, groundMaterial, shadows: true);

                    // Its own renderer, not a submesh: shadow casting is per
                    // renderer, and water casts none.
                    Emit("Water " + at, surface, waterMaterial, shadows: false);
                }
            }

            solid.Clear();
            Slab(solid);
            Emit("Base", solid, groundMaterial, shadows: true);
        }

        /// <summary>Destroys everything <see cref="Build"/> made. Safe to call when nothing is built.</summary>
        public static void Drop()
        {
            if (root != null) Release(root);
            root = null;
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null) Release(meshes[i]);
            }
            meshes.Clear();
            built = null;
        }

        // ------------------------------------------------------------------ cells

        /// <summary>
        /// One cell: its top, then the walls on its +x and +z edges and on any map
        /// edge it sits on. Each interior edge is built once, by the cell on its
        /// lower side, so a wall is never doubled. The top of a cell is whatever
        /// <see cref="Top"/> says, so every wall is built from the same heights as
        /// the tops it joins and the two always meet.
        /// </summary>
        private static void Cell(Facets solid, Facets surface, int cx, int cy)
        {
            int cell = cy * Field.Cells + cx;
            TileKind kind = Field.Kind(cell);

            var p0 = new Vector3(cx, Top(cell, 0), cy);
            var p1 = new Vector3(cx + 1, Top(cell, 1), cy);
            var p2 = new Vector3(cx, Top(cell, 2), cy + 1);
            var p3 = new Vector3(cx + 1, Top(cell, 3), cy + 1);
            float grain = Unit(cx, cy, 0);

            switch (kind)
            {
                case TileKind.Water:
                    solid.Quad(p0, p1, p3, p2, FieldPalette.Grained(FieldPalette.Bed, grain), Vector3.up);
                    WaterSurface(surface, cell, cx, cy);
                    break;
                case TileKind.Rock:
                    Surface(solid, p0, p1, p2, p3, FieldPalette.Grained(FieldPalette.RockTop, grain));
                    break;
                default:
                    int level = Field.Level(cell);
                    bool flat = Flat(p0, p1, p2, p3);
                    Color colour = flat ? FieldPalette.Grass(level) : FieldPalette.Slope(level);
                    Surface(solid, p0, p1, p2, p3, FieldPalette.Grained(colour, grain));
                    if (!flat) Ruts(solid, cx, cy, p0, p1, p2, p3);
                    break;
            }

            // Interior edges, each from the cell on its lower side.
            if (cx + 1 < Field.Cells)
            {
                Seam(solid, cell, cell + 1, 1, 3, 0, 2,
                     new Vector2(cx + 1, cy), new Vector2(cx + 1, cy + 1), Vector3.right);
            }
            if (cy + 1 < Field.Cells)
            {
                Seam(solid, cell, cell + Field.Cells, 2, 3, 0, 1,
                     new Vector2(cx, cy + 1), new Vector2(cx + 1, cy + 1), Vector3.forward);
            }

            // The plinth, on whichever map edges this cell is on.
            if (cx == 0) Rim(solid, cell, 0, 2, new Vector2(cx, cy), new Vector2(cx, cy + 1), Vector3.left);
            if (cx == Field.Cells - 1) Rim(solid, cell, 1, 3, new Vector2(cx + 1, cy), new Vector2(cx + 1, cy + 1), Vector3.right);
            if (cy == 0) Rim(solid, cell, 0, 1, new Vector2(cx, cy), new Vector2(cx + 1, cy), Vector3.back);
            if (cy == Field.Cells - 1) Rim(solid, cell, 2, 3, new Vector2(cx, cy + 1), new Vector2(cx + 1, cy + 1), Vector3.forward);
        }

        /// <summary>
        /// The height of a cell's solid top at one corner. Open ground is Field's
        /// own corners. Rock is Field's flat top with a little jitter, taken per
        /// grid point rather than per cell so two rock cells side by side still
        /// meet without a crack. Water's solid top is its bed; the water itself is
        /// a separate surface and not something a wall stops at.
        /// </summary>
        private static float Top(int cell, int corner)
        {
            switch (Field.Kind(cell))
            {
                case TileKind.Water:
                    return Field.Bed(cell);
                case TileKind.Rock:
                    int gx = cell % Field.Cells + (corner & 1);
                    int gy = cell / Field.Cells + (corner >> 1);
                    return Field.Flat(cell) + (Unit(gx, gy, 11) * 2f - 1f) * RockJitter;
                default:
                    return Field.Corner(cell, corner);
            }
        }

        private static bool Flat(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) =>
            Mathf.Abs(p0.y - p1.y) <= Epsilon && Mathf.Abs(p0.y - p2.y) <= Epsilon && Mathf.Abs(p0.y - p3.y) <= Epsilon;

        /// <summary>
        /// A cell's top. Flat cells are one quad. A tilted one is two triangles,
        /// split along whichever diagonal joins the two corners closest in height:
        /// that keeps the fold between the two facets as shallow as the corners
        /// allow, so a ramp reads as one surface with a crease and not as a
        /// pyramid.
        /// </summary>
        private static void Surface(Facets f, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color colour)
        {
            if (Flat(p0, p1, p2, p3))
            {
                f.Quad(p0, p1, p3, p2, colour, Vector3.up);
                return;
            }

            // Field.Height walks the same two triangles, so the rule lives there.
            if (Field.MainDiagonal(p0.y, p1.y, p2.y, p3.y))
            {
                f.Triangle(p0, p1, p3, colour, Vector3.up);
                f.Triangle(p0, p3, p2, colour, Vector3.up);
            }
            else
            {
                f.Triangle(p0, p1, p2, colour, Vector3.up);
                f.Triangle(p1, p3, p2, colour, Vector3.up);
            }
        }

        // ------------------------------------------------------------------ ramps

        /// <summary>The least rise across a cell, in world units, that gets ruts. Half a level: a ramp cell and the cells it leans into, not the slight tilt at a ramp's side.</summary>
        private const float RutMinRise = 0.3f;

        /// <summary>Distance between two ruts, across the climb. Gives a cell two, or three when it climbs diagonally.</summary>
        private const float RutSpacing = 0.5f;

        /// <summary>A rut's width. Thin enough to be a worn line and not a second ground colour.</summary>
        private const float RutWidth = 0.075f;

        /// <summary>How far a rut floats over the ground, so it never fights the facet under it for depth.</summary>
        private const float RutLift = 0.012f;

        /// <summary>
        /// Worn earth lines running up a slope cell, so a ramp reads as the way up
        /// before the light has said anything. Parallel to the climb, with the
        /// climb rounded to one of eight directions, and placed on lines fixed in
        /// world space — every RutSpacing across that direction — so the cells of
        /// one ramp, which all climb the same way, carry the same lines across
        /// their borders and the ruts run as one path from the foot to the top.
        /// </summary>
        private static void Ruts(Facets f, int cx, int cy, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            float dx = ((p1.y - p0.y) + (p3.y - p2.y)) * 0.5f;
            float dz = ((p2.y - p0.y) + (p3.y - p1.y)) * 0.5f;
            if (dx * dx + dz * dz < RutMinRise * RutMinRise) return;

            // The way the whole ramp climbs rather than this cell's own tilt: a
            // ramp running diagonally across the grid is a staircase of cells, and
            // read one at a time they tilt along x, along z and diagonally by
            // turns, which scatters the ruts like hatching. Rounded to one of
            // eight directions so neighbouring cells of one ramp agree exactly.
            Vector2 climb = Climb(cx, cy);
            if (climb.sqrMagnitude < 1e-4f) climb = new Vector2(dx, dz);
            float angle = Mathf.Round(Mathf.Atan2(climb.y, climb.x) / (Mathf.PI * 0.25f)) * (Mathf.PI * 0.25f);
            var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var across = new Vector2(-along.y, along.x);
            int heading = ((int)Mathf.Round(angle / (Mathf.PI * 0.25f)) + 8) % 4;

            // The cell's extent across the climb, and the lines that fall inside it.
            float lo = float.MaxValue;
            float hi = float.MinValue;
            for (int c = 0; c < 4; c++)
            {
                float d = Vector2.Dot(new Vector2(cx + (c & 1), cy + (c >> 1)), across);
                lo = Mathf.Min(lo, d);
                hi = Mathf.Max(hi, d);
            }

            Color colour = FieldPalette.Rut;
            for (int k = Mathf.CeilToInt(lo / RutSpacing); k * RutSpacing <= hi; k++)
            {
                // Each line wanders a little off its slot, the same wander in every cell it crosses.
                float offset = k * RutSpacing + (Unit(k, heading, 31) - 0.5f) * 0.12f;
                if (!Clip(cx, cy, across * offset, along, out float t0, out float t1)) continue;
                if (t1 - t0 < 0.2f) continue;

                Vector2 start = across * offset + along * t0;
                Vector2 side = across * (RutWidth * 0.5f);
                const int steps = 3;
                for (int i = 0; i < steps; i++)
                {
                    Vector2 a = start + along * ((t1 - t0) * i / steps);
                    Vector2 b = start + along * ((t1 - t0) * (i + 1) / steps);
                    f.Quad(OnCell(cx, cy, a - side), OnCell(cx, cy, b - side),
                           OnCell(cx, cy, b + side), OnCell(cx, cy, a + side), colour, Vector3.up);
                }
            }
        }

        /// <summary>
        /// Which way the ground climbs around cell (cx, cy): a Sobel gradient of
        /// the simulation's levels over the 3 by 3 block of open cells round it.
        /// Rock and water stand in at the cell's own level, so the ridge and the
        /// lakes do not pull a ramp sideways. A cliff does count, which is right:
        /// it is the same climb, only too steep to walk.
        /// </summary>
        private static Vector2 Climb(int cx, int cy)
        {
            int own = Field.Level(cy * Field.Cells + cx);
            float gx = 0f;
            float gz = 0f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = cx + dx;
                    int y = cy + dy;
                    int level = own;
                    if (x >= 0 && y >= 0 && x < Field.Cells && y < Field.Cells)
                    {
                        int cell = y * Field.Cells + x;
                        if (Field.Kind(cell) == TileKind.Open) level = Field.Level(cell);
                    }
                    float weight = dx == 0 || dy == 0 ? 2f : 1f;
                    gx += dx * weight * level;
                    gz += dy * weight * level;
                }
            }
            return new Vector2(gx, gz);
        }

        /// <summary>
        /// Where the line origin + t·direction enters and leaves cell (cx, cy).
        /// Each rut vertex is then held inside the cell (<see cref="OnCell"/>), so
        /// a rut ends at a border where a wall drops away instead of hanging
        /// over it.
        /// </summary>
        private static bool Clip(int cx, int cy, Vector2 origin, Vector2 direction, out float t0, out float t1)
        {
            const float inset = 0f;
            t0 = float.MinValue;
            t1 = float.MaxValue;
            for (int axis = 0; axis < 2; axis++)
            {
                float o = axis == 0 ? origin.x : origin.y;
                float d = axis == 0 ? direction.x : direction.y;
                float min = (axis == 0 ? cx : cy) + inset;
                float max = (axis == 0 ? cx : cy) + 1 - inset;
                if (Mathf.Abs(d) < 1e-5f)
                {
                    if (o < min || o > max) return false;
                    continue;
                }
                float a = (min - o) / d;
                float b = (max - o) / d;
                if (a > b) (a, b) = (b, a);
                t0 = Mathf.Max(t0, a);
                t1 = Mathf.Min(t1, b);
            }
            return t1 > t0;
        }

        /// <summary>A plane point inside cell (cx, cy), lifted onto that cell's own surface by <see cref="RutLift"/>.</summary>
        private static Vector3 OnCell(int cx, int cy, Vector2 p)
        {
            float x = Mathf.Clamp(p.x, cx + 0.001f, cx + 0.999f);
            float z = Mathf.Clamp(p.y, cy + 0.001f, cy + 0.999f);
            return new Vector3(x, Field.Height(x, z) + RutLift, z);
        }

        // ------------------------------------------------------------------ walls

        /// <summary>
        /// The wall, if any, along the edge two cells share. a is on the -x or -z
        /// side and b on the other; aFirst/aSecond and bFirst/bSecond are the
        /// corners each cell has at the edge's two ends e0 and e1.
        ///
        /// Built from the height gap alone, so it puts a face wherever Field.Cliff
        /// is true and also wherever a ramp's corner and a flat's corner do not
        /// quite meet, and is consistent with Field.Height by construction. When
        /// the higher side swaps partway along the edge, which two slopes leaning
        /// opposite ways can do, the wall is split at the crossing into two
        /// triangles facing opposite ways.
        /// </summary>
        private static void Seam(Facets f, int a, int b, int aFirst, int aSecond, int bFirst, int bSecond,
                                 Vector2 e0, Vector2 e1, Vector3 towardB)
        {
            float a0 = Top(a, aFirst);
            float a1 = Top(a, aSecond);
            float b0 = Top(b, bFirst);
            float b1 = Top(b, bSecond);
            float d0 = a0 - b0;
            float d1 = a1 - b1;

            bool open0 = Mathf.Abs(d0) > Epsilon;
            bool open1 = Mathf.Abs(d1) > Epsilon;
            if (!open0 && !open1) return;

            if (open0 && open1 && d0 * d1 < 0f)
            {
                float t = d0 / (d0 - d1);
                Vector2 middle = Vector2.Lerp(e0, e1, t);
                float height = Mathf.Lerp(a0, a1, t);
                Face(f, a, b, e0, middle, a0, height, b0, height, towardB);
                Face(f, a, b, middle, e1, height, a1, height, b1, towardB);
                return;
            }

            Face(f, a, b, e0, e1, a0, a1, b0, b1, towardB);
        }

        /// <summary>One stretch of wall between two cells, facing down toward whichever side is lower.</summary>
        private static void Face(Facets f, int a, int b, Vector2 e0, Vector2 e1,
                                 float a0, float a1, float b0, float b1, Vector3 towardB)
        {
            bool aHigher = (a0 - b0) + (a1 - b1) > 0f;
            int high = aHigher ? a : b;
            int low = aHigher ? b : a;

            TileKind highKind = Field.Kind(high);
            TileKind lowKind = Field.Kind(low);

            Color lip = highKind == TileKind.Rock ? FieldPalette.RockFace
                      : highKind == TileKind.Water ? FieldPalette.Bed
                      : FieldPalette.CliffTop;
            Color foot = lowKind == TileKind.Water ? FieldPalette.ShoreFoot
                       : highKind == TileKind.Rock ? FieldPalette.RockFoot
                       : FieldPalette.CliffFoot;

            if (!aHigher)
            {
                (a0, b0) = (b0, a0);
                (a1, b1) = (b1, a1);
                towardB = -towardB;
            }

            float rise = Mathf.Max(a0 - b0, a1 - b1);
            if (rise >= StrataMin && highKind != TileKind.Water && lowKind != TileKind.Water)
            {
                Color rim = highKind == TileKind.Rock ? FieldPalette.RockRim : FieldPalette.CliffRim;
                Strata(f, e0, e1, a0, a1, b0, b1, towardB, lip, foot, rim);
                return;
            }

            Wall(f, e0, e1, a0, a1, b0, b1, towardB, lip, foot);
        }

        // ------------------------------------------------------------------ strata

        private static readonly List<Vector3> columnA = new List<Vector3>(16);
        private static readonly List<Vector3> columnB = new List<Vector3>(16);
        private static readonly List<Vector3> columnC = new List<Vector3>(16);

        /// <summary>
        /// A cliff face built as rock rather than as a board: cut into horizontal
        /// strata at <see cref="Stratum"/>, every band a step lighter or darker
        /// than the one under it, a thin light lip along the top, and every inner
        /// vertex nudged off the plane so the bands break into facets that each
        /// take the light at their own angle.
        ///
        /// Three columns per cell edge: the two corners and the middle. The top and
        /// foot rows never move, because they are where the wall meets the ground
        /// above and below and that ground is Field's. Corner columns move by a
        /// vector hashed from the grid point and the stratum alone, so the two
        /// walls that share a corner — straight on, or turning round a plateau's
        /// corner — compute the same vertex and meet without a crack.
        /// </summary>
        private static void Strata(Facets f, Vector2 e0, Vector2 e1, float top0, float top1, float foot0, float foot1,
                                   Vector3 outward, Color lip, Color footColour, Color rim)
        {
            Vector2 mid = (e0 + e1) * 0.5f;
            Column(columnA, e0, top0, foot0, outward, true);
            Column(columnB, mid, (top0 + top1) * 0.5f, (foot0 + foot1) * 0.5f, outward, false);
            Column(columnC, e1, top1, foot1, outward, true);

            float crest = Mathf.Max(top0, top1);
            Zip(f, columnA, columnB, outward, crest, lip, footColour, rim);
            Zip(f, columnB, columnC, outward, crest, lip, footColour, rim);
        }

        /// <summary>
        /// One column of a strata wall, foot first: the foot, every stratum line
        /// that clears both ends by <see cref="StrataMargin"/>, the lip's lower
        /// edge, the top. A column too short for a lip is just its two ends.
        /// </summary>
        private static void Column(List<Vector3> into, Vector2 at, float top, float foot, Vector3 outward, bool corner)
        {
            into.Clear();
            into.Add(new Vector3(at.x, foot, at.y));
            if (top - foot <= Epsilon) return;

            float rimFoot = top - RimDepth;
            if (rimFoot - foot > StrataMargin)
            {
                int first = Mathf.FloorToInt((foot + StrataMargin) / Stratum) + 1;
                for (int k = first; k * Stratum < rimFoot - StrataMargin; k++)
                {
                    into.Add(new Vector3(at.x, k * Stratum, at.y) + Jitter(at, k, outward, corner));
                }
                Vector3 lean = corner ? Vector3.zero : outward * RimLean;
                into.Add(new Vector3(at.x, rimFoot, at.y) + lean);
            }
            into.Add(new Vector3(at.x, top, at.y));
        }

        /// <summary>
        /// Where one stratum vertex moves. Keyed by the point doubled, so a mid
        /// edge point and a corner both land on whole numbers, and by the stratum
        /// index, so every band moves on its own. Mid edge vertices bulge mostly
        /// out from the wall; corner vertices move in any direction on the ground
        /// plane, since a corner may be shared by a wall facing another way.
        /// </summary>
        private static Vector3 Jitter(Vector2 at, int stratum, Vector3 outward, bool corner)
        {
            int gx = Mathf.RoundToInt(at.x * 2f);
            int gz = Mathf.RoundToInt(at.y * 2f);
            int salt = 20 + stratum * 3;
            float lift = (Unit(gx, gz, salt + 1) - 0.5f) * 0.1f;
            if (corner)
            {
                return new Vector3((Unit(gx, gz, salt) * 2f - 1f) * FaceJitter, lift,
                                   (Unit(gx, gz, salt + 2) * 2f - 1f) * FaceJitter);
            }
            float push = (Unit(gx, gz, salt) * 1.3f - 0.3f) * FaceJitter;
            return outward * push + Vector3.up * lift;
        }

        /// <summary>
        /// Stitches two columns into triangles, foot to top, always stepping up
        /// whichever column's next vertex is lower as a fraction of its own
        /// height. That is what lets two columns with different numbers of strata
        /// — one end of a wall taller than the other — still close without a gap.
        /// </summary>
        private static void Zip(Facets f, List<Vector3> l, List<Vector3> r, Vector3 outward, float crest,
                                Color lip, Color footColour, Color rim)
        {
            int i = 0;
            int j = 0;
            int lastL = l.Count - 1;
            int lastR = r.Count - 1;
            while (i < lastL || j < lastR)
            {
                bool stepLeft = i < lastL && (j == lastR || Fraction(l, i + 1) <= Fraction(r, j + 1));
                Vector3 next = stepLeft ? l[i + 1] : r[j + 1];
                bool lipBand = stepLeft ? i + 1 == lastL : j + 1 == lastR;

                Vector3 a = l[i];
                Vector3 b = r[j];
                Color colour = lipBand ? rim : Band((a.y + b.y + next.y) / 3f, crest, lip, footColour);
                f.Triangle(a, b, next, colour, outward);

                if (stepLeft) i++;
                else j++;
            }
        }

        private static float Fraction(List<Vector3> column, int index)
        {
            float span = column[column.Count - 1].y - column[0].y;
            return span <= Epsilon ? 1f : (column[index].y - column[0].y) / span;
        }

        /// <summary>
        /// A stratum's colour at height y: the wall's lip-to-foot ramp, as the
        /// plain wall has it, then lifted or dropped by
        /// <see cref="FieldPalette.StrataShift"/> on alternate strata. Alternate
        /// by the absolute stratum index, so the light and dark bands line up
        /// from one wall to the next.
        /// </summary>
        private static Color Band(float y, float crest, Color lip, Color footColour)
        {
            Color colour = Color.Lerp(lip, footColour, Mathf.Clamp01((crest - y) / FootDepth));
            int stratum = Mathf.FloorToInt(y / Stratum);
            float scale = (stratum & 1) == 0 ? 1f + FieldPalette.StrataShift : 1f - FieldPalette.StrataShift;
            return new Color(colour.r * scale, colour.g * scale, colour.b * scale, colour.a);
        }

        /// <summary>
        /// The plinth under one map-edge cell: from the cell's solid top straight
        /// down to <see cref="Base"/>. Rock keeps its own colour at the lip, so the
        /// ridge reads as running into the cut edge rather than sitting on it.
        /// </summary>
        private static void Rim(Facets f, int cell, int first, int second, Vector2 e0, Vector2 e1, Vector3 outward)
        {
            Color lip = Field.Kind(cell) == TileKind.Rock ? FieldPalette.RockFace : FieldPalette.Plinth;
            Wall(f, e0, e1, Top(cell, first), Top(cell, second), Base, Base, outward, lip, FieldPalette.PlinthFoot);
        }

        /// <summary>
        /// A vertical face along e0..e1 from top down to foot, facing outward.
        /// Two colours, lip at the top and a foot colour at the bottom that gets
        /// darker the taller the wall is (see <see cref="FootDepth"/>). Where the
        /// wall closes to nothing at one end it is a triangle, not a quad with a
        /// zero-length side.
        /// </summary>
        private static void Wall(Facets f, Vector2 e0, Vector2 e1, float top0, float top1, float foot0, float foot1,
                                 Vector3 outward, Color lip, Color footColour)
        {
            float rise0 = top0 - foot0;
            float rise1 = top1 - foot1;
            bool open0 = rise0 > Epsilon;
            bool open1 = rise1 > Epsilon;
            if (!open0 && !open1) return;

            Color foot = Color.Lerp(lip, footColour, Mathf.Clamp01(Mathf.Max(rise0, rise1) / FootDepth));

            var t0 = new Vector3(e0.x, top0, e0.y);
            var t1 = new Vector3(e1.x, top1, e1.y);
            var b0 = new Vector3(e0.x, foot0, e0.y);
            var b1 = new Vector3(e1.x, foot1, e1.y);

            if (open0 && open1) f.Quad(b0, b1, t1, t0, foot, foot, lip, lip, outward);
            else if (open0) f.Triangle(b0, b1, t0, foot, foot, lip, outward);
            else f.Triangle(b0, b1, t1, foot, foot, lip, outward);
        }

        /// <summary>
        /// The slab the plinth stands on: a thin block a little wider than the map,
        /// seen only as a lip round the foot of the plinth. No underside, because
        /// nothing the camera does looks up at it.
        /// </summary>
        private static void Slab(Facets f)
        {
            float lo = -SlabOverhang;
            float hi = Field.Cells + SlabOverhang;
            float top = Base;
            float bottom = Base - SlabThickness;

            var n0 = new Vector3(lo, top, lo);
            var n1 = new Vector3(hi, top, lo);
            var n2 = new Vector3(lo, top, hi);
            var n3 = new Vector3(hi, top, hi);
            var m0 = new Vector3(lo, bottom, lo);
            var m1 = new Vector3(hi, bottom, lo);
            var m2 = new Vector3(lo, bottom, hi);
            var m3 = new Vector3(hi, bottom, hi);

            f.Quad(n0, n1, n3, n2, FieldPalette.SlabTop, Vector3.up);
            f.Quad(m0, m1, n1, n0, FieldPalette.SlabSide, Vector3.back);
            f.Quad(m1, m3, n3, n1, FieldPalette.SlabSide, Vector3.right);
            f.Quad(m3, m2, n2, n3, FieldPalette.SlabSide, Vector3.forward);
            f.Quad(m2, m0, n0, n2, FieldPalette.SlabSide, Vector3.left);
        }

        // ------------------------------------------------------------------ water

        /// <summary>
        /// A water cell's surface, at Field.Flat. Coloured per grid point rather
        /// than per cell so neighbouring water cells blend into one sheet: foam at
        /// every point that touches something other than water, the deep tone at
        /// points whose every cell is surrounded by water, and open water between.
        /// The same two-band idea the 2D bake had, now a soft ring rather than a
        /// stripe of texels.
        /// </summary>
        private static void WaterSurface(Facets f, int cell, int cx, int cy)
        {
            float y = Field.Flat(cell);
            var p0 = new Vector3(cx, y, cy);
            var p1 = new Vector3(cx + 1, y, cy);
            var p2 = new Vector3(cx, y, cy + 1);
            var p3 = new Vector3(cx + 1, y, cy + 1);
            f.Quad(p0, p1, p3, p2,
                   WaterTone(cx, cy), WaterTone(cx + 1, cy), WaterTone(cx + 1, cy + 1), WaterTone(cx, cy + 1),
                   Vector3.up);
        }

        /// <summary>The water colour at grid point (gx, gy), from the up to four cells that meet there.</summary>
        private static Color WaterTone(int gx, int gy)
        {
            bool deep = true;
            for (int dy = -1; dy <= 0; dy++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int x = gx + dx;
                    int y = gy + dy;
                    if (!IsWater(x, y)) return FieldPalette.Foam;
                    if (!Enclosed(x, y)) deep = false;
                }
            }
            return deep ? FieldPalette.WaterDeep : FieldPalette.Water;
        }

        /// <summary>Whether a cell is water. Off the map is not: the plinth is there, and the water meets it with foam.</summary>
        private static bool IsWater(int x, int y) =>
            x >= 0 && y >= 0 && x < Field.Cells && y < Field.Cells && Field.Kind(y * Field.Cells + x) == TileKind.Water;

        private static bool Enclosed(int x, int y) =>
            IsWater(x - 1, y) && IsWater(x + 1, y) && IsWater(x, y - 1) && IsWater(x, y + 1);

        // ------------------------------------------------------------------ plumbing

        /// <summary>
        /// The two shared materials, made once and kept across rebuilds. A missing
        /// shader falls back to Standard with a warning rather than to Unity's
        /// magenta error shader, so the failure is a line in the log and a map that
        /// is still playable instead of a pink board.
        /// </summary>
        private static void Materials()
        {
            if (groundMaterial == null) groundMaterial = new Material(Load("Shaders/Ground")) { name = "Ground" };
            if (waterMaterial == null) waterMaterial = new Material(Load("Shaders/Water")) { name = "Water" };
        }

        private static Shader Load(string path)
        {
            Shader shader = Resources.Load<Shader>(path);
            if (shader != null) return shader;
            Debug.LogWarning("FieldMesh: shader Resources/" + path + " did not load; falling back to Standard.");
            return Shader.Find("Standard");
        }

        private static void Emit(string name, Facets facets, Material material, bool shadows)
        {
            if (facets.Count == 0) return;

            var mesh = new Mesh { name = name };
            if (facets.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(facets.Vertices);
            mesh.SetNormals(facets.Normals);
            mesh.SetColors(facets.Colours);
            mesh.SetTriangles(facets.Indices, 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);

            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            // Nothing on this field is baked, so probes would only cost a lookup.
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Object.Destroy in play mode, DestroyImmediate in the editor's batch mode, where Destroy refuses to run.</summary>
        private static void Release(Object thing)
        {
            if (Application.isPlaying) Object.Destroy(thing);
            else Object.DestroyImmediate(thing);
        }

        /// <summary>
        /// A cell hash, for grain, jitter and where a prop goes. The same function
        /// Tiles.cs uses and for the same reason: not World.Random, because this is
        /// view noise, the draw count World.Random would spend is simulation state,
        /// and spending one here would desync every peer that drew the map.
        /// </summary>
        internal static int Hash(int x, int y)
        {
            // Mixed twice on purpose. One round leaves the low bit as the parity of
            // x + y, and a low bit that is a parity paints a checkerboard over the
            // whole map.
            int h = (x * 73856093) ^ (y * 19349663);
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return h & 0x7FFFFFFF;
        }

        /// <summary>
        /// A 0..1 draw for (x, y). salt picks an independent draw for the same
        /// cell, so the grain, the jitter and each choice a prop makes do not all
        /// move together.
        /// </summary>
        internal static float Unit(int x, int y, int salt) =>
            (Hash(x + salt * 7919, y - salt * 104729) & 0xFFFF) / 65535f;
    }

    /// <summary>
    /// A mesh under construction, one flat face at a time. Every face gets its
    /// own vertices and its own normal; that is the whole of how the field is
    /// flat shaded. Winding is worked out from the side a face should be seen
    /// from rather than written by hand at every call, because a hand-wound
    /// triangle that comes out backwards is culled and leaves a hole in the map
    /// that only shows from one angle.
    /// </summary>
    internal sealed class Facets
    {
        /// <summary>Twice the squared area under which a face is a sliver and dropped.</summary>
        private const float Degenerate = 1e-10f;

        public readonly List<Vector3> Vertices = new List<Vector3>(4096);
        public readonly List<Vector3> Normals = new List<Vector3>(4096);
        public readonly List<Color> Colours = new List<Color>(4096);
        public readonly List<int> Indices = new List<int>(8192);

        public int Count => Vertices.Count;

        public void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            Colours.Clear();
            Indices.Clear();
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color colour, Vector3 facing) =>
            Triangle(a, b, c, colour, colour, colour, facing);

        /// <summary>
        /// One triangle, front face toward facing. Unity draws the side that
        /// Cross(b - a, c - a) points to, so if that points away the last two
        /// vertices swap.
        /// </summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < Degenerate) return;
            if (Vector3.Dot(normal, facing) < 0f)
            {
                (b, c) = (c, b);
                (cb, cc) = (cc, cb);
                normal = -normal;
            }
            normal.Normalize();

            int start = Vertices.Count;
            Add(a, normal, ca);
            Add(b, normal, cb);
            Add(c, normal, cc);
            Indices.Add(start);
            Indices.Add(start + 1);
            Indices.Add(start + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, Vector3 facing) =>
            Quad(a, b, c, d, colour, colour, colour, colour, facing);

        /// <summary>
        /// A planar quad given as a loop a, b, c, d, front face toward facing. One
        /// normal for all four vertices, taken across the diagonals so a quad with
        /// one collapsed side still gets the right one.
        /// </summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Color ca, Color cb, Color cc, Color cd, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(c - a, d - b);
            if (normal.sqrMagnitude < Degenerate) return;

            int start = Vertices.Count;
            if (Vector3.Dot(normal, facing) < 0f)
            {
                normal = -normal.normalized;
                Add(a, normal, ca);
                Add(d, normal, cd);
                Add(c, normal, cc);
                Add(b, normal, cb);
            }
            else
            {
                normal = normal.normalized;
                Add(a, normal, ca);
                Add(b, normal, cb);
                Add(c, normal, cc);
                Add(d, normal, cd);
            }

            Indices.Add(start);
            Indices.Add(start + 1);
            Indices.Add(start + 2);
            Indices.Add(start);
            Indices.Add(start + 2);
            Indices.Add(start + 3);
        }

        private void Add(Vector3 position, Vector3 normal, Color colour)
        {
            Vertices.Add(position);
            Normals.Add(normal);
            Colours.Add(colour);
        }
    }
}
