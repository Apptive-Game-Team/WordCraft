using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The small things on the ground: a loose stone, a tuft of grass, the odd
    /// low tree. Without them open ground is a lawn with a grid hidden in it,
    /// and the eye has nothing to judge scale or slope against.
    ///
    /// Baked into the same chunk meshes as the ground, with the same material
    /// and vertex colours, so props cost no draw of their own and are lit and
    /// shadowed by exactly the rules the ground is.
    ///
    /// Everything is a function of the cell's (x, y) through FieldMesh.Hash, so
    /// both peers grow the same tree in the same place without a single
    /// World.Random draw. Purely decoration: the simulation does not know these
    /// exist, so nothing here may be big enough to look like it blocks a path.
    /// </summary>
    public static class FieldProps
    {
        // Odds per hundred open cells. Together about one cell in ten has
        // something on it: enough to break up the lawn, sparse enough that no
        // patch of ground reads as a thicket a unit should walk round.
        private const int TreeOdds = 2;
        private const int StoneOdds = 4;
        private const int TuftOdds = 5;

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
        /// Adds whatever props cell (cx, cy) has to f. Most cells have none; only
        /// open ground has any.
        /// </summary>
        internal static void Add(Facets f, int cx, int cy)
        {
            int cell = cy * Field.Cells + cx;
            if (Field.Kind(cell) != TileKind.Open) return;
            if (Cleared(cx, cy)) return;

            int roll = FieldMesh.Hash(cx * 31 + 7, cy * 17 + 3) % 100;
            if (Climb(cell) >= 0.3f)
            {
                // A ramp: low flat stones along the way up, nothing tall.
                if (roll < RampStoneOdds) Slab(f, cx, cy);
                return;
            }
            if (roll < TreeOdds)
            {
                if (Sheltered(cx, cy)) Tree(f, cx, cy);
            }
            else if (roll < TreeOdds + StoneOdds)
            {
                Stone(f, cx, cy);
            }
            else if (roll < TreeOdds + StoneOdds + TuftOdds)
            {
                Tuft(f, cx, cy);
            }
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
        /// A low tree: a short trunk under three stacked cones, each narrower and
        /// lighter than the one below, so the top tier takes the highlight the
        /// way the style puts one on top of every form. At most a cell across.
        /// </summary>
        private static void Tree(Facets f, int cx, int cy)
        {
            float px = cx + 0.5f + (FieldMesh.Unit(cx, cy, 1) - 0.5f) * 0.2f;
            float pz = cy + 0.5f + (FieldMesh.Unit(cx, cy, 2) - 0.5f) * 0.2f;
            float yaw = FieldMesh.Unit(cx, cy, 3) * 60f;
            float scale = 0.85f + 0.15f * FieldMesh.Unit(cx, cy, 4);
            var foot = new Vector3(px, Field.Height(px, pz) - 0.02f, pz);

            Prism(f, foot, 0.07f * scale, 0.3f * scale, 6, yaw, FieldPalette.Trunk);

            Cone(f, foot + Vector3.up * (0.22f * scale), 0.50f * scale, 0.50f * scale, 6, yaw, FieldPalette.Foliage);
            Cone(f, foot + Vector3.up * (0.50f * scale), 0.38f * scale, 0.42f * scale, 6, yaw + 20f,
                 Color.Lerp(FieldPalette.Foliage, FieldPalette.FoliageTop, 0.5f));
            Cone(f, foot + Vector3.up * (0.76f * scale), 0.26f * scale, 0.36f * scale, 6, yaw + 40f, FieldPalette.FoliageTop);
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
            Block(f, cx, cy, px, pz, width, depth, height, 0.7f);
        }

        /// <summary>
        /// The stone build every rock prop shares: a box of width × depth ×
        /// height at (px, pz), its top corners pulled in by pull and nudged by the
        /// cell's hash so no two are the same block, sunk a little into the ground.
        /// </summary>
        private static void Block(Facets f, int cx, int cy, float px, float pz, float width, float depth, float height, float pull)
        {
            float yaw = FieldMesh.Unit(cx, cy, 3) * 90f * Mathf.Deg2Rad;
            float ground = Field.Height(px, pz) - 0.04f;

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
                float lift = top ? height + (FieldMesh.Unit(cx + i, cy, 7) - 0.5f) * 0.06f : 0f;
                float nudge = top ? (FieldMesh.Unit(cx, cy + i, 8) - 0.5f) * 0.04f : 0f;
                p[i] = centre + right * (sx * width * squeeze + nudge) + ahead * (sz * depth * squeeze - nudge)
                       + Vector3.up * lift;
            }

            Color side = FieldMesh.Unit(cx, cy, 9) < 0.5f
                ? FieldPalette.Pebble
                : Color.Lerp(FieldPalette.Pebble, FieldPalette.PebbleTop, 0.2f);

            Split(f, p[4], p[5], p[7], p[6], FieldPalette.PebbleTop, Vector3.up);
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
                  0.05f + 0.03f * FieldMesh.Unit(cx, cy, 6), 0.75f);
        }

        /// <summary>A four sided face that may not be planar, as two flat triangles.</summary>
        private static void Split(Facets f, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, Vector3 facing)
        {
            f.Triangle(a, b, c, colour, facing);
            f.Triangle(a, c, d, colour, facing);
        }

        // ------------------------------------------------------------------ tuft

        /// <summary>
        /// A tuft: three or four blades, each a thin three sided spike leaning out
        /// from the middle, dark at the root and light at the tip. Under 0.3 of a
        /// cell across.
        /// </summary>
        private static void Tuft(Facets f, int cx, int cy)
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
                var root = new Vector3(x, Field.Height(x, z) - 0.02f, z);
                float tall = 0.16f + 0.10f * FieldMesh.Unit(cx + b, cy, 5);
                Vector3 tip = root + outward * 0.05f + Vector3.up * tall;

                for (int s = 0; s < 3; s++)
                {
                    Vector3 r0 = root + Spoke(s, 3, turn + b * 40f) * 0.03f;
                    Vector3 r1 = root + Spoke(s + 1, 3, turn + b * 40f) * 0.03f;
                    f.Triangle(r0, r1, tip, FieldPalette.Tuft, FieldPalette.Tuft, FieldPalette.TuftTip,
                               Spoke(s + 0.5f, 3, turn + b * 40f));
                }
            }
        }
    }
}
