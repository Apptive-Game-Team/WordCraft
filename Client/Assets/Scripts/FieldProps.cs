using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The small things on the ground: stones and boulders, tufts and ferns,
    /// two kinds of tree, flowers, mushrooms, a fallen log, rubble under the
    /// cliffs, reeds in the shallows and edging stones up the ramps. Without them
    /// open ground is a lawn with a grid hidden in it, and the eye has nothing to
    /// judge scale or slope against.
    ///
    /// What grows where follows the ground, so the props say what kind of place
    /// a cell is: rubble, ferns and mushrooms in the shade at a cliff's foot,
    /// pines and dry grass on the high ground, boulders and the odd stunted pine
    /// up on the ridge, broadleaf trees, flowers and logs in the valley.
    ///
    /// Baked into the same chunk meshes as the ground, with the same material
    /// and vertex colours, so props cost no draw of their own and are lit and
    /// shadowed by exactly the rules the ground is.
    ///
    /// Everything is a function of the cell's (x, y) through FieldMesh.Hash, so
    /// both peers grow the same tree in the same place without a single
    /// World.Random draw. Purely decoration: the simulation does not know these
    /// exist, so nothing here may be big enough to look like it blocks a path.
    /// Every prop on walkable ground except the two trees, which were already a
    /// cell across, stays under a third of a unit's footprint (about 0.6 of a
    /// cell).
    /// </summary>
    public static class FieldProps
    {
        // Odds per hundred cells of each prop, by the kind of ground. Read as
        // cumulative bands of one roll, in the order written. Together about one
        // valley cell in six has something on it: enough to break up the lawn,
        // sparse enough that no patch of ground reads as a thicket a unit should
        // walk round.

        // The valley floor.
        private const int ValleyConifer = 1;
        private const int ValleyBroadleaf = 2;
        private const int ValleyStone = 3;
        private const int ValleyBoulder = 1;
        private const int ValleyTuft = 4;
        private const int ValleyFern = 1;
        private const int ValleyFlowers = 2;
        private const int ValleyMushrooms = 1;
        private const int ValleyLog = 1;

        // High ground: level 2 and up. Drier and more open.
        private const int HighConifer = 4;
        private const int HighDryTuft = 7;
        private const int HighStone = 3;
        private const int HighFlowers = 1;

        // A cliff's foot: open ground with a cliff rising on one side.
        private const int FootRubble = 50;
        private const int FootFern = 12;
        private const int FootMushrooms = 8;

        // The ridge's top: rock no body stands on.
        private const int RidgeBoulder = 14;
        private const int RidgePine = 6;

        /// <summary>Odds per hundred slope cells of a low stone beside the way up.</summary>
        private const int RampStoneOdds = 45;

        /// <summary>
        /// Places nothing may grow near, as (x, y, radius in cells) for peer 0;
        /// each is mirrored through the map centre for peer 1, the way
        /// MatchScenario.At places the second player. These are the scenario's
        /// own spawn points — the base and its starting units round (8, 8), the
        /// home node at (14, 14), the contested nodes at (24, 39) and (39, 24),
        /// and the neutral rocks at (54, 6), (37, 26) and (26, 37) — copied here
        /// because MatchScenario keeps them as private literals. A base or a
        /// node with a tree through it is clutter exactly where a player looks
        /// most. The base gets a wider circle because its units stand off it.
        /// </summary>
        private static readonly int[] KeepClear =
        {
            8, 8, 5,
            14, 14, 3,
            24, 39, 3,
            39, 24, 3,
            54, 6, 3,
            37, 26, 3,
            26, 37, 3,
        };

        /// <summary>
        /// Adds whatever props cell (cx, cy) has to f. Most cells have none.
        /// </summary>
        internal static void Add(Facets f, int cx, int cy)
        {
            int cell = cy * Field.Cells + cx;
            TileKind kind = Field.Kind(cell);
            int roll = FieldMesh.Hash(cx * 31 + 7, cy * 17 + 3) % 100;

            if (kind == TileKind.Water)
            {
                Reeds(f, cx, cy);
                return;
            }
            if (kind == TileKind.Rock)
            {
                Ridge(f, cx, cy, roll);
                return;
            }
            if (Cleared(cx, cy)) return;

            if (Climb(cell) >= 0.3f)
            {
                // A ramp: low flat stones along the way up, nothing tall.
                if (roll < RampStoneOdds) Slab(f, cx, cy);
                return;
            }

            int cliff = CliffSide(cx, cy);
            if (cliff >= 0)
            {
                Foot(f, cx, cy, cliff, roll);
                return;
            }

            if (Field.Level(cell) >= 2) High(f, cx, cy, roll);
            else Valley(f, cx, cy, roll);
        }

        private static void Valley(Facets f, int cx, int cy, int roll)
        {
            int band = 0;
            if (roll < (band += ValleyConifer)) { if (Sheltered(cx, cy)) Conifer(f, cx, cy, 1f); }
            else if (roll < (band += ValleyBroadleaf)) { if (Sheltered(cx, cy)) Broadleaf(f, cx, cy); }
            else if (roll < (band += ValleyStone)) Stone(f, cx, cy);
            else if (roll < (band += ValleyBoulder)) Boulder(f, cx, cy, ridge: false);
            else if (roll < (band += ValleyTuft)) Tuft(f, cx, cy, FieldPalette.Tuft, FieldPalette.TuftTip);
            else if (roll < (band += ValleyFern)) Fern(f, cx, cy);
            else if (roll < (band += ValleyFlowers)) Flowers(f, cx, cy);
            else if (roll < (band += ValleyMushrooms)) Mushrooms(f, cx, cy);
            else if (roll < (band += ValleyLog)) { if (Sheltered(cx, cy)) Log(f, cx, cy); }
        }

        private static void High(Facets f, int cx, int cy, int roll)
        {
            int band = 0;
            if (roll < (band += HighConifer)) { if (Sheltered(cx, cy)) Conifer(f, cx, cy, 1f); }
            else if (roll < (band += HighDryTuft)) Tuft(f, cx, cy, FieldPalette.DryTuft, FieldPalette.DryTuftTip);
            else if (roll < (band += HighStone)) Stone(f, cx, cy);
            else if (roll < (band += HighFlowers)) Flowers(f, cx, cy);
        }

        private static void Foot(Facets f, int cx, int cy, int side, int roll)
        {
            int band = 0;
            if (roll < (band += FootRubble)) Rubble(f, cx, cy, side);
            else if (roll < (band += FootFern)) Fern(f, cx, cy);
            else if (roll < (band += FootMushrooms)) Mushrooms(f, cx, cy);
        }

        /// <summary>
        /// The ridge's top: boulders, and now and then a small pine where the rock
        /// is wide enough round it to hold one. The ridge is impassable, so these
        /// are free to be the biggest stones on the map.
        /// </summary>
        private static void Ridge(Facets f, int cx, int cy, int roll)
        {
            if (roll < RidgeBoulder)
            {
                Boulder(f, cx, cy, ridge: true);
            }
            else if (roll < RidgeBoulder + RidgePine && RockAround(cx, cy))
            {
                Conifer(f, cx, cy, 0.6f);
            }
        }

        /// <summary>
        /// Which edge of an open cell has a cliff rising from it, as an index into
        /// SideX and SideZ, or -1. Starts from a hashed side, so a cell in a
        /// cliff's inside corner does not always pick the same one.
        /// </summary>
        private static int CliffSide(int cx, int cy)
        {
            int cell = cy * Field.Cells + cx;
            int own = Field.Level(cell);
            int first = (int)(FieldMesh.Unit(cx, cy, 70) * 4f) % 4;
            for (int i = 0; i < 4; i++)
            {
                int s = (first + i) % 4;
                int nx = cx + SideX[s];
                int nz = cy + SideZ[s];
                if (nx < 0 || nz < 0 || nx >= Field.Cells || nz >= Field.Cells) continue;
                int neighbour = nz * Field.Cells + nx;
                if (Field.Kind(neighbour) == TileKind.Water) continue;
                if (Field.Level(neighbour) - own >= 2) return s;
            }
            return -1;
        }

        /// <summary>Whether all four of a rock cell's edge neighbours are rock too, so a pine on it does not hang over a cliff.</summary>
        private static bool RockAround(int cx, int cy)
        {
            for (int s = 0; s < 4; s++)
            {
                int nx = cx + SideX[s];
                int nz = cy + SideZ[s];
                if (nx < 0 || nz < 0 || nx >= Field.Cells || nz >= Field.Cells) return false;
                if (Field.Kind(nz * Field.Cells + nx) != TileKind.Rock) return false;
            }
            return true;
        }

        /// <summary>Where a prop's foot goes at plane point (x, z): the ground for open cells, the jittered top for rock.</summary>
        private static float Base(float x, float z)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(x), 0, Field.Cells - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(z), 0, Field.Cells - 1);
            int cell = cy * Field.Cells + cx;
            return Field.Kind(cell) == TileKind.Rock ? FieldMesh.SolidHeight(cell, x, z) - 0.04f : Field.Height(x, z);
        }

        /// <summary>How far a cell's surface climbs across it, in world units. Zero on flat ground.</summary>
        private static float Climb(int cell)
        {
            float c0 = Field.Corner(cell, 0);
            float c1 = Field.Corner(cell, 1);
            float c2 = Field.Corner(cell, 2);
            float c3 = Field.Corner(cell, 3);
            float dx = ((c1 - c0) + (c3 - c2)) * 0.5f;
            float dz = ((c2 - c0) + (c3 - c1)) * 0.5f;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>How far a plane point is from the nearer of the two bases, in cells.</summary>
        internal static float BaseDistance(Vector2 at)
        {
            int last = Field.Cells - 1;
            var own = new Vector2(KeepClear[0] + 0.5f, KeepClear[1] + 0.5f);
            var other = new Vector2(last - KeepClear[0] + 0.5f, last - KeepClear[1] + 0.5f);
            return Mathf.Min(Vector2.Distance(at, own), Vector2.Distance(at, other));
        }

        private static bool Cleared(int cx, int cy)
        {
            int last = Field.Cells - 1;
            for (int i = 0; i < KeepClear.Length; i += 3)
            {
                int r = KeepClear[i + 2];
                if (Near(cx, cy, KeepClear[i], KeepClear[i + 1], r)) return true;
                if (Near(cx, cy, last - KeepClear[i], last - KeepClear[i + 1], r)) return true;
            }
            return false;
        }

        private static bool Near(int x, int y, int px, int py, int radius)
        {
            int dx = x - px;
            int dy = y - py;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>
        /// Whether a tree fits: a tree is a cell across, so it overhangs its
        /// neighbours, and it only goes where the whole 3 by 3 block is open, flat
        /// and one level. A tree half over a cliff edge or leaning into a lake
        /// reads as a mistake.
        /// </summary>
        private static bool Sheltered(int cx, int cy)
        {
            int level = Field.Level(cy * Field.Cells + cx);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || y < 0 || x >= Field.Cells || y >= Field.Cells) return false;
                    int cell = y * Field.Cells + x;
                    if (Field.Kind(cell) != TileKind.Open || Field.Level(cell) != level) return false;
                    float h = Field.Corner(cell, 0);
                    for (int c = 1; c < 4; c++)
                    {
                        if (Mathf.Abs(Field.Corner(cell, c) - h) > 0.01f) return false;
                    }
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ tree

        /// <summary>
        /// A pine: a short trunk under three stacked cones, each narrower and
        /// lighter than the one below, so the top tier takes the highlight the
        /// way the style puts one on top of every form. At most a cell across at
        /// scale 1; the ridge grows them at 0.6.
        /// </summary>
        private static void Conifer(Facets f, int cx, int cy, float size)
        {
            float px = cx + 0.5f + (FieldMesh.Unit(cx, cy, 1) - 0.5f) * 0.2f;
            float pz = cy + 0.5f + (FieldMesh.Unit(cx, cy, 2) - 0.5f) * 0.2f;
            float yaw = FieldMesh.Unit(cx, cy, 3) * 60f;
            float scale = (0.85f + 0.15f * FieldMesh.Unit(cx, cy, 4)) * size;
            var foot = new Vector3(px, Base(px, pz) - 0.02f, pz);

            Prism(f, foot, 0.07f * scale, 0.3f * scale, 6, yaw, FieldPalette.Trunk);

            Cone(f, foot + Vector3.up * (0.22f * scale), 0.50f * scale, 0.50f * scale, 6, yaw, FieldPalette.Foliage);
            Cone(f, foot + Vector3.up * (0.50f * scale), 0.38f * scale, 0.42f * scale, 6, yaw + 20f,
                 Color.Lerp(FieldPalette.Foliage, FieldPalette.FoliageTop, 0.5f));
            Cone(f, foot + Vector3.up * (0.76f * scale), 0.26f * scale, 0.36f * scale, 6, yaw + 40f, FieldPalette.FoliageTop);
        }

        /// <summary>
        /// A broadleaf tree: a trunk under a rounded crown made of two faceted
        /// lumps, the larger low and the smaller set up and to one side, in a
        /// lighter, warmer green than the pine so the two trees read as two kinds
        /// from across the map. About 0.8 of a cell across.
        /// </summary>
        private static void Broadleaf(Facets f, int cx, int cy)
        {
            float px = cx + 0.5f + (FieldMesh.Unit(cx, cy, 1) - 0.5f) * 0.2f;
            float pz = cy + 0.5f + (FieldMesh.Unit(cx, cy, 2) - 0.5f) * 0.2f;
            float yaw = FieldMesh.Unit(cx, cy, 3) * 60f;
            float scale = 0.85f + 0.15f * FieldMesh.Unit(cx, cy, 4);
            var foot = new Vector3(px, Field.Height(px, pz) - 0.02f, pz);

            Prism(f, foot, 0.06f * scale, 0.42f * scale, 5, yaw, FieldPalette.Trunk);
            Lump(f, foot + Vector3.up * (0.52f * scale), 0.38f * scale, 0.20f * scale, 0.26f * scale, 7, yaw);
            Vector3 aside = Spoke(FieldMesh.Unit(cx, cy, 5) * 7f, 7, yaw) * (0.12f * scale);
            Lump(f, foot + aside + Vector3.up * (0.74f * scale), 0.24f * scale, 0.12f * scale, 0.18f * scale, 6, yaw + 25f);
        }

        /// <summary>
        /// A faceted lump, for a crown: a ring of sides at centre, a cone up to a
        /// top point and a shallower cone down to a bottom point. Upper facets in
        /// the light leaf colour and lower in the dark, so it rounds under the
        /// light the way a painted ball would.
        /// </summary>
        private static void Lump(Facets f, Vector3 centre, float radius, float below, float above, int sides, float yaw)
        {
            Vector3 top = centre + Vector3.up * above;
            Vector3 bottom = centre - Vector3.up * below;
            for (int i = 0; i < sides; i++)
            {
                Vector3 r0 = centre + Spoke(i, sides, yaw) * radius;
                Vector3 r1 = centre + Spoke(i + 1, sides, yaw) * radius;
                Vector3 outward = Spoke(i + 0.5f, sides, yaw);
                f.Triangle(r0, r1, top, FieldPalette.LeafTop, outward + Vector3.up);
                f.Triangle(r0, r1, bottom, FieldPalette.Leaf, outward - Vector3.up);
            }
        }

        /// <summary>
        /// A cone's sides, one flat facet per side. No base: the camera looks
        /// down on the field and never sees under a tier, and the tier below hides
        /// it anyway.
        /// </summary>
        private static void Cone(Facets f, Vector3 centre, float radius, float height, int sides, float yaw, Color colour)
        {
            Vector3 apex = centre + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                Vector3 r0 = Spoke(i, sides, yaw);
                Vector3 r1 = Spoke(i + 1, sides, yaw);
                Vector3 outward = Spoke(i + 0.5f, sides, yaw);
                f.Triangle(centre + r0 * radius, centre + r1 * radius, apex, colour, outward);
            }
        }

        /// <summary>A prism's sides, for a trunk. No caps: the ground hides one end and the foliage the other.</summary>
        private static void Prism(Facets f, Vector3 foot, float radius, float height, int sides, float yaw, Color colour)
        {
            Vector3 up = Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                Vector3 b0 = foot + Spoke(i, sides, yaw) * radius;
                Vector3 b1 = foot + Spoke(i + 1, sides, yaw) * radius;
                f.Quad(b0, b1, b1 + up, b0 + up, colour, Spoke(i + 0.5f, sides, yaw));
            }
        }

        /// <summary>The unit vector on the ground plane at step i of a ring of sides, turned by yaw degrees.</summary>
        private static Vector3 Spoke(float i, int sides, float yaw)
        {
            float angle = (i / sides * 360f + yaw) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        // ------------------------------------------------------------------ stone

        /// <summary>
        /// A loose stone: a squat box with its top corners pulled in and nudged
        /// about, so no two stones are the same block and each one's top catches
        /// the light in a few facets. Under 0.3 of a cell across, sunk a little so
        /// it sits in the ground rather than on it.
        /// </summary>
        private static void Stone(Facets f, int cx, int cy)
        {
            float px = cx + 0.25f + 0.5f * FieldMesh.Unit(cx, cy, 1);
            float pz = cy + 0.25f + 0.5f * FieldMesh.Unit(cx, cy, 2);
            float width = 0.16f + 0.12f * FieldMesh.Unit(cx, cy, 4);
            float depth = 0.14f + 0.10f * FieldMesh.Unit(cx, cy, 5);
            float height = 0.10f + 0.10f * FieldMesh.Unit(cx, cy, 6);
            Block(f, cx, cy, px, pz, width, depth, height, 0.7f, FieldPalette.Pebble, FieldPalette.PebbleTop);
        }

        /// <summary>
        /// A boulder: the stone's build at about twice its size and taller for its
        /// width, with a small stone leaning at its foot. Up to half a cell across
        /// on the valley floor, which is still under a third of a unit's
        /// footprint; on the ridge, in the ridge's own colours and a size larger.
        /// </summary>
        private static void Boulder(Facets f, int cx, int cy, bool ridge)
        {
            float px = cx + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 1);
            float pz = cy + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 2);
            float size = ridge ? 1.35f : 1f;
            Color side = ridge ? FieldPalette.RockFace : FieldPalette.Pebble;
            Color top = ridge ? FieldPalette.RockRim : FieldPalette.PebbleTop;
            Block(f, cx, cy, px, pz, (0.30f + 0.12f * FieldMesh.Unit(cx, cy, 4)) * size,
                  (0.26f + 0.10f * FieldMesh.Unit(cx, cy, 5)) * size,
                  (0.22f + 0.10f * FieldMesh.Unit(cx, cy, 6)) * size, 0.6f, side, top);

            Vector3 lean = Spoke(FieldMesh.Unit(cx, cy, 15) * 8f, 8, 0f) * (0.2f * size);
            Block(f, cx + 101, cy, px + lean.x, pz + lean.z, 0.12f * size, 0.10f * size, 0.08f * size, 0.7f, side, top);
        }

        /// <summary>
        /// A pile of fallen rock at a cliff's foot: three to five small stones
        /// heaped against the face on the given side, the biggest nearest the
        /// wall and none more than a quarter of a cell across. Coloured like what fell, so rubble under the ridge is the
        /// ridge's grey and rubble under an earth cliff is earth.
        /// </summary>
        private static void Rubble(Facets f, int cx, int cy, int side)
        {
            int nx = cx + SideX[side];
            int nz = cy + SideZ[side];
            bool rock = Field.Kind(nz * Field.Cells + nx) == TileKind.Rock;
            Color sideColour = rock ? FieldPalette.Pebble : FieldPalette.Rubble;
            Color topColour = rock ? FieldPalette.PebbleTop : FieldPalette.RubbleTop;

            // The edge's middle, slid along it a little, and out from the wall past
            // the strata's bulge.
            float along = (FieldMesh.Unit(cx, cy, 71) - 0.5f) * 0.5f;
            float edgeX = cx + 0.5f + SideX[side] * 0.5f + (SideX[side] == 0 ? along : 0f);
            float edgeZ = cy + 0.5f + SideZ[side] * 0.5f + (SideZ[side] == 0 ? along : 0f);
            int stones = 3 + (int)(FieldMesh.Unit(cx, cy, 72) * 3f);
            for (int i = 0; i < stones; i++)
            {
                float out1 = 0.17f + 0.22f * FieldMesh.Unit(cx + i, cy, 73);
                float slide = (FieldMesh.Unit(cx, cy + i, 74) - 0.5f) * 0.45f;
                float px = edgeX - SideX[side] * out1 + (SideX[side] == 0 ? slide : 0f);
                float pz = edgeZ - SideZ[side] * out1 + (SideZ[side] == 0 ? slide : 0f);
                float size = 1.4f * (1f - 0.5f * (out1 - 0.17f) / 0.22f);
                Block(f, cx * 7 + i, cy * 5 - i, px, pz, (0.09f + 0.07f * FieldMesh.Unit(cx + i, cy, 75)) * size,
                      (0.08f + 0.06f * FieldMesh.Unit(cx, cy + i, 76)) * size,
                      (0.06f + 0.06f * FieldMesh.Unit(cx + i, cy + i, 77)) * size, 0.7f, sideColour, topColour);
            }
        }

        /// <summary>
        /// The stone build every rock prop shares: a box of width × depth ×
        /// height at (px, pz), its top corners pulled in by pull and nudged by the
        /// hash of (hx, hy) so no two are the same block, sunk a little into
        /// whatever it stands on.
        /// </summary>
        private static void Block(Facets f, int hx, int hy, float px, float pz, float width, float depth, float height,
                                  float pull, Color sideColour, Color topColour)
        {
            float yaw = FieldMesh.Unit(hx, hy, 3) * 90f * Mathf.Deg2Rad;
            float ground = Base(px, pz) - 0.04f;

            var right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var ahead = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var centre = new Vector3(px, ground, pz);

            // Corner index bits: 1 is +right, 2 is +ahead, 4 is the top.
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -0.5f : 0.5f;
                float sz = (i & 2) == 0 ? -0.5f : 0.5f;
                bool top = (i & 4) != 0;
                float squeeze = top ? pull : 1f;
                float lift = top ? height + (FieldMesh.Unit(hx + i, hy, 7) - 0.5f) * 0.06f : 0f;
                float nudge = top ? (FieldMesh.Unit(hx, hy + i, 8) - 0.5f) * 0.04f : 0f;
                p[i] = centre + right * (sx * width * squeeze + nudge) + ahead * (sz * depth * squeeze - nudge)
                       + Vector3.up * lift;
            }

            Color side = FieldMesh.Unit(hx, hy, 9) < 0.5f ? sideColour : Color.Lerp(sideColour, topColour, 0.2f);

            Split(f, p[4], p[5], p[7], p[6], topColour, Vector3.up);
            Split(f, p[0], p[1], p[5], p[4], side, -ahead);
            Split(f, p[1], p[3], p[7], p[5], side, right);
            Split(f, p[3], p[2], p[6], p[7], side, ahead);
            Split(f, p[2], p[0], p[4], p[6], side, -right);
        }

        /// <summary>
        /// A low flat stone at the side of a ramp: the loose stone's build, half
        /// its height and a little wider, so it reads as a step or an edging stone
        /// on the way up and never as a block in it. Sits near one edge of the cell
        /// rather than in the middle, where the ruts run.
        /// </summary>
        private static void Slab(Facets f, int cx, int cy)
        {
            float side = FieldMesh.Unit(cx, cy, 12) < 0.5f ? 0.18f : 0.82f;
            float along = 0.25f + 0.5f * FieldMesh.Unit(cx, cy, 13);
            bool acrossX = FieldMesh.Unit(cx, cy, 14) < 0.5f;
            float px = cx + (acrossX ? side : along);
            float pz = cy + (acrossX ? along : side);
            Block(f, cx, cy, px, pz, 0.20f + 0.08f * FieldMesh.Unit(cx, cy, 4), 0.16f + 0.06f * FieldMesh.Unit(cx, cy, 5),
                  0.05f + 0.03f * FieldMesh.Unit(cx, cy, 6), 0.75f, FieldPalette.Pebble, FieldPalette.PebbleTop);
        }

        // ------------------------------------------------------------------ log

        /// <summary>
        /// A fallen log: a six sided trunk lying on its side, bark round it and
        /// the pale cut wood on both ends. 0.55 of a cell long and under 0.2 thick,
        /// on flat ground only, so it lies on the ground and not across a slope.
        /// </summary>
        private static void Log(Facets f, int cx, int cy)
        {
            float px = cx + 0.5f + (FieldMesh.Unit(cx, cy, 1) - 0.5f) * 0.3f;
            float pz = cy + 0.5f + (FieldMesh.Unit(cx, cy, 2) - 0.5f) * 0.3f;
            float yaw = FieldMesh.Unit(cx, cy, 3) * Mathf.PI;
            float length = 0.48f + 0.10f * FieldMesh.Unit(cx, cy, 4);
            float radius = 0.075f + 0.02f * FieldMesh.Unit(cx, cy, 5);
            var axis = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var across = new Vector3(-axis.z, 0f, axis.x);
            var centre = new Vector3(px, Field.Height(px, pz) + radius * 0.75f, pz);
            Vector3 a = centre - axis * (length * 0.5f);
            Vector3 b = centre + axis * (length * 0.5f);

            const int sides = 6;
            for (int i = 0; i < sides; i++)
            {
                float t0 = (i / (float)sides) * Mathf.PI * 2f;
                float t1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;
                Vector3 r0 = (across * Mathf.Cos(t0) + Vector3.up * Mathf.Sin(t0)) * radius;
                Vector3 r1 = (across * Mathf.Cos(t1) + Vector3.up * Mathf.Sin(t1)) * radius;
                Vector3 outward = across * Mathf.Cos((t0 + t1) * 0.5f) + Vector3.up * Mathf.Sin((t0 + t1) * 0.5f);
                f.Quad(a + r0, b + r0, b + r1, a + r1, FieldPalette.Trunk, outward);
                f.Triangle(a, a + r0, a + r1, FieldPalette.LogEnd, -axis);
                f.Triangle(b, b + r0, b + r1, FieldPalette.LogEnd, axis);
            }
        }

        /// <summary>A four sided face that may not be planar, as two flat triangles.</summary>
        private static void Split(Facets f, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, Vector3 facing)
        {
            f.Triangle(a, b, c, colour, facing);
            f.Triangle(a, c, d, colour, facing);
        }

        // ------------------------------------------------------------------ reeds

        /// <summary>Odds per hundred shore water cells of a clump of reeds.</summary>
        private const int ReedOdds = 55;

        private static readonly int[] SideX = { 1, -1, 0, 0 };
        private static readonly int[] SideZ = { 0, 0, 1, -1 };

        /// <summary>
        /// A clump of reeds in the shallows of a water cell that has ground on at
        /// least one side: four to six thin blades rooted in the bed, standing a
        /// short way out from the bank and up through the surface, olive at the
        /// root and straw at the tip. Under 0.3 of a cell across, and in water,
        /// which no body walks.
        /// </summary>
        private static void Reeds(Facets f, int cx, int cy)
        {
            if (FieldMesh.Hash(cx * 13 + 5, cy * 29 + 11) % 100 >= ReedOdds) return;

            int first = (int)(FieldMesh.Unit(cx, cy, 50) * 4f) % 4;
            int side = -1;
            for (int i = 0; i < 4; i++)
            {
                int s = (first + i) % 4;
                int nx = cx + SideX[s];
                int nz = cy + SideZ[s];
                if (nx < 0 || nz < 0 || nx >= Field.Cells || nz >= Field.Cells) continue;
                if (FieldMesh.IsWater(nx, nz)) continue;
                side = s;
                break;
            }
            if (side < 0) return;

            // The bank edge is the cell's border on that side; stand in from it.
            float inset = 0.32f + 0.14f * FieldMesh.Unit(cx, cy, 51);
            float along = 0.2f + 0.6f * FieldMesh.Unit(cx, cy, 52);
            float px = cx + 0.5f + SideX[side] * (0.5f - inset) + (SideX[side] == 0 ? along - 0.5f : 0f);
            float pz = cy + 0.5f + SideZ[side] * (0.5f - inset) + (SideZ[side] == 0 ? along - 0.5f : 0f);

            int cell = cy * Field.Cells + cx;
            float root = Field.Bed(cell);
            float water = Field.Flat(cell);
            int blades = 4 + (int)(FieldMesh.Unit(cx, cy, 53) * 3f);
            float turn = FieldMesh.Unit(cx, cy, 54) * 360f;
            for (int b = 0; b < blades; b++)
            {
                Vector3 outward = Spoke(b, blades, turn);
                float spread = 0.03f + 0.07f * FieldMesh.Unit(cx + b, cy, 55);
                var foot = new Vector3(px + outward.x * spread, root, pz + outward.z * spread);
                float tall = water - root + 0.24f + 0.24f * FieldMesh.Unit(cx, cy + b, 56);
                Vector3 tip = foot + outward * 0.06f + Vector3.up * tall;
                for (int s = 0; s < 3; s++)
                {
                    Vector3 r0 = foot + Spoke(s, 3, turn + b * 50f) * 0.03f;
                    Vector3 r1 = foot + Spoke(s + 1, 3, turn + b * 50f) * 0.03f;
                    f.Triangle(r0, r1, tip, FieldPalette.Reed, FieldPalette.Reed, FieldPalette.ReedTip,
                               Spoke(s + 0.5f, 3, turn + b * 50f));
                }
            }
        }

        // ------------------------------------------------------------------ small plants

        /// <summary>
        /// A tuft: three or four blades, each a thin three sided spike leaning out
        /// from the middle, dark at the root and light at the tip. Under 0.3 of a
        /// cell across. Green in the valley, straw on the high ground.
        /// </summary>
        private static void Tuft(Facets f, int cx, int cy, Color root, Color tipColour)
        {
            float px = cx + 0.25f + 0.5f * FieldMesh.Unit(cx, cy, 1);
            float pz = cy + 0.25f + 0.5f * FieldMesh.Unit(cx, cy, 2);
            float turn = FieldMesh.Unit(cx, cy, 3) * 360f;
            int blades = FieldMesh.Unit(cx, cy, 4) < 0.5f ? 3 : 4;

            for (int b = 0; b < blades; b++)
            {
                Vector3 outward = Spoke(b, blades, turn);
                float x = px + outward.x * 0.06f;
                float z = pz + outward.z * 0.06f;
                var foot = new Vector3(x, Field.Height(x, z) - 0.02f, z);
                float tall = 0.16f + 0.10f * FieldMesh.Unit(cx + b, cy, 5);
                Vector3 tip = foot + outward * 0.05f + Vector3.up * tall;

                for (int s = 0; s < 3; s++)
                {
                    Vector3 r0 = foot + Spoke(s, 3, turn + b * 40f) * 0.03f;
                    Vector3 r1 = foot + Spoke(s + 1, 3, turn + b * 40f) * 0.03f;
                    f.Triangle(r0, r1, tip, root, root, tipColour, Spoke(s + 0.5f, 3, turn + b * 40f));
                }
            }
        }

        /// <summary>
        /// A fern: five to seven fronds fanning out from one root, each a flat
        /// blade that arches up and falls back to the ground, so the plant is low
        /// and wide where a tuft is narrow and upright. Under half a cell across.
        /// </summary>
        private static void Fern(Facets f, int cx, int cy)
        {
            float px = cx + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 1);
            float pz = cy + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 2);
            float turn = FieldMesh.Unit(cx, cy, 3) * 360f;
            int fronds = 5 + (int)(FieldMesh.Unit(cx, cy, 4) * 3f);
            var root = new Vector3(px, Field.Height(px, pz) + 0.01f, pz);

            for (int i = 0; i < fronds; i++)
            {
                Vector3 outward = Spoke(i + 0.3f * FieldMesh.Unit(cx + i, cy, 5), fronds, turn);
                Vector3 side = new Vector3(-outward.z, 0f, outward.x);
                float reach = 0.17f + 0.07f * FieldMesh.Unit(cx, cy + i, 6);
                Vector3 mid = root + outward * (reach * 0.5f) + Vector3.up * (0.10f + 0.04f * FieldMesh.Unit(cx + i, cy + i, 7));
                var tipAt = new Vector3(px + outward.x * reach, 0f, pz + outward.z * reach);
                tipAt.y = Field.Height(tipAt.x, tipAt.z) + 0.03f;
                Vector3 left = mid + side * 0.045f;
                Vector3 right = mid - side * 0.045f;
                f.Triangle(root, right, left, FieldPalette.Fern, outward + Vector3.up * 2f);
                f.Triangle(left, right, tipAt, FieldPalette.FernTip, outward + Vector3.up * 2f);
            }
        }

        /// <summary>
        /// A few flowers: four to six thin stems, each with a small flat head
        /// facing up, all one muted colour per clump. Petal colours are pale and
        /// chalky so a clump reads as flowers without competing with the owner
        /// hues the units carry.
        /// </summary>
        private static void Flowers(Facets f, int cx, int cy)
        {
            float px = cx + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 1);
            float pz = cy + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 2);
            Color petal = FieldPalette.Flower((int)(FieldMesh.Unit(cx, cy, 3) * 3f));
            int stems = 4 + (int)(FieldMesh.Unit(cx, cy, 4) * 3f);
            float turn = FieldMesh.Unit(cx, cy, 5) * 360f;

            for (int i = 0; i < stems; i++)
            {
                Vector3 outward = Spoke(i, stems, turn);
                float spread = 0.04f + 0.10f * FieldMesh.Unit(cx + i, cy, 6);
                float x = px + outward.x * spread;
                float z = pz + outward.z * spread;
                var foot = new Vector3(x, Field.Height(x, z) - 0.01f, z);
                float tall = 0.10f + 0.06f * FieldMesh.Unit(cx, cy + i, 7);
                Vector3 head = foot + Vector3.up * tall;

                // The stem: a two sided blade, edge on to nothing in particular.
                Vector3 w = Spoke(i + 0.25f, stems, turn) * 0.012f;
                f.Triangle(foot - w, foot + w, head, FieldPalette.Tuft, outward);
                f.Triangle(foot - w, foot + w, head, FieldPalette.Tuft, -outward);

                // The head: a flat four pointed star a few hundredths across.
                float size = 0.035f + 0.015f * FieldMesh.Unit(cx + i, cy + i, 8);
                Vector3 a = Spoke(0, 4, turn + i * 30f) * size;
                Vector3 b = Spoke(1, 4, turn + i * 30f) * size;
                f.Quad(head + a, head + b, head - a, head - b, petal, Vector3.up);
            }
        }

        /// <summary>
        /// Two or three mushrooms: a short pale stem under a low cone of cap, in
        /// the shade at a cliff's foot or now and then in the valley. Under 0.2 of
        /// a cell across.
        /// </summary>
        private static void Mushrooms(Facets f, int cx, int cy)
        {
            float px = cx + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 1);
            float pz = cy + 0.3f + 0.4f * FieldMesh.Unit(cx, cy, 2);
            int count = 2 + (int)(FieldMesh.Unit(cx, cy, 3) * 2f);
            float turn = FieldMesh.Unit(cx, cy, 4) * 360f;

            for (int i = 0; i < count; i++)
            {
                Vector3 outward = Spoke(i, count, turn);
                float spread = i == 0 ? 0f : 0.05f + 0.04f * FieldMesh.Unit(cx + i, cy, 5);
                float x = px + outward.x * spread;
                float z = pz + outward.z * spread;
                float scale = i == 0 ? 1f : 0.6f + 0.3f * FieldMesh.Unit(cx, cy + i, 6);
                var foot = new Vector3(x, Field.Height(x, z) - 0.01f, z);
                float stem = 0.07f * scale;
                Prism(f, foot, 0.018f * scale, stem, 5, turn, FieldPalette.MushroomStem);
                Cone(f, foot + Vector3.up * (stem - 0.005f), 0.055f * scale, 0.04f * scale, 6, turn + i * 20f,
                     FieldPalette.MushroomCap);
            }
        }
    }
}
