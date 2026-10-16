using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static partial class Models
    {
        // Subject identity comes from the web catalog and reference PNGs. Team accents, materials,
        // rig parts and movement remain those of the shared model system.
        private static bool CatalogWaterRock(Blueprint b, Faction faction, Role role, int slot)
        {
            if (slot != 0) return false;
            if (faction == Faction.WaterSlimes)
            {
                switch (role)
                {
                    case Role.Worker: CatalogWaterDroplet(b); return true;
                    case Role.Melee: CatalogWaterBubble(b); return true;
                    case Role.Ranged: CatalogWaterArcher(b); return true;
                    case Role.Signature: CatalogWaterDragon(b); return true;
                }
            }
            if (faction == Faction.RockGolems)
            {
                switch (role)
                {
                    case Role.Worker: CatalogRockPebble(b); return true;
                    case Role.Melee: CatalogRockGolem(b); return true;
                    case Role.Ranged: CatalogRockMage(b); return true;
                }
            }
            return false;
        }

        // Shallow face inlays replace protruding black/white beads. The highlight
        // stays inside the eye outline, so it reads as light rather than jewelry.
        private static void CatalogWaterRockEyes(Solids h, Vector3 center, float spacing, float size)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 eye = center + Point(side * spacing * 0.5f, 0f, 0f);
                h.Ball(eye, Extent(size * 0.62f, size, size * 0.13f), BodyPalette.Eye, Quaternion.identity, 1);
                h.Ball(eye + Point(-size * 0.18f, size * 0.32f, size * 0.12f),
                    Extent(size * 0.13f, size * 0.17f, size * 0.04f), BodyPalette.EyeShine, Quaternion.identity, 0);
            }
        }

        private static void CatalogWaterRockMark(Blueprint b, Solids part, Vector3 at, float width)
        {
            if (!b.HasAccent) return;
            part.Box(at, Extent(width, width * 0.4f, 0.025f), b.Accent, Turn(0f, 0f, -12f));
        }

        private static void CatalogWaterDroplet(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids h = b.Hull;
            Color blue = BodyPalette.WaterSlimes.Sky, pale = BodyPalette.WaterSlimes.Pale;
            h.Ball(Point(0f, 0.37f, 0f), Extent(0.39f, 0.35f, 0.35f), blue);
            h.Ball(Point(-0.07f, 0.51f, -0.035f), Extent(0.28f, 0.23f, 0.26f), pale);
            // One swept crest joins the gel body instead of a necklace of beads.
            CatalogWaterTube(h, Point(-0.04f, 0.63f, -0.08f), Point(-0.12f, 0.77f, -0.12f), 0.105f, 0.065f, pale);
            CatalogWaterTube(h, Point(-0.12f, 0.77f, -0.12f), Point(-0.22f, 0.8f, -0.09f), 0.065f, 0.015f, pale);
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 72f;
                Vector3 p = Around(angle) * 0.28f + Point(0f, 0.12f, 0f);
                h.Ball(p, Extent(0.13f, 0.09f, 0.12f), i % 2 == 0 ? pale : blue);
            }
            CatalogWaterRockEyes(h, Point(0f, 0.43f, 0.32f), 0.23f, 0.082f);
            CatalogWaterRockMark(b, h, Point(0f, 0.23f, 0.315f), 0.14f);
            b.Arm.Origin = Point(0.28f, 0.22f, 0.08f);
            b.Arm.Ball(Point(0.34f, 0.24f, 0.18f), 0.105f, pale);
        }

        private static void CatalogWaterBubble(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Lunge;
            Solids h = b.Hull;
            Color pale = BodyPalette.WaterSlimes.Pale, blue = BodyPalette.WaterSlimes.Sky;
            h.Ball(Point(0f, 0.45f, 0.15f), Extent(0.34f, 0.36f, 0.32f), pale);
            h.Ball(Point(-0.035f, 0.28f, -0.13f), Extent(0.29f, 0.23f, 0.38f), blue);
            // Backward curling water tail; the negative-z arc stays behind the face.
            Vector3 previousTail = Point(-0.03f, 0.23f, -0.32f);
            for (int i = 0; i < 13; i++)
            {
                float angle = (-90f - i * 25f) * Mathf.Deg2Rad;
                float radius = 0.29f - Mathf.Max(0, i - 6) * 0.018f;
                Vector3 nextTail = Point(-0.03f, 0.57f + Mathf.Sin(angle) * radius,
                    -0.38f + Mathf.Cos(angle) * radius);
                CatalogWaterTube(h, previousTail, nextTail, 0.12f - i * 0.007f, 0.113f - i * 0.007f, i < 6 ? blue : pale);
                previousTail = nextTail;
            }
            h.Ball(Point(-0.3f, 0.18f, -0.12f), 0.13f, pale);
            h.Ball(Point(0.27f, 0.17f, -0.04f), 0.11f, blue);
            CatalogWaterRockEyes(h, Point(0f, 0.47f, 0.445f), 0.21f, 0.075f);
            CatalogWaterRockMark(b, h, Point(0f, 0.28f, 0.4f), 0.14f);
            b.Arm.Origin = Point(0.22f, 0.3f, 0.14f);
            b.Arm.Ball(Point(0.25f, 0.31f, 0.33f), Extent(0.11f, 0.14f, 0.14f), blue);
        }

        // A segment joins exact endpoints, so the bow limbs, string and arrow
        // cannot turn into disconnected parallel rods when the attack arm recoils.
        private static void CatalogWaterSegment(Solids part, Vector3 a, Vector3 z, float width, Color color)
        {
            Vector3 delta = z - a;
            part.Box((a + z) * 0.5f, Extent(width, delta.magnitude, width), color,
                Quaternion.FromToRotation(Vector3.up, delta));
        }

        private static void CatalogWaterTube(Solids part, Vector3 a, Vector3 end, float startRadius, float endRadius, Color color)
        {
            Vector3 delta = end - a;
            if (delta.sqrMagnitude < 0.000001f) return;
            part.Prism(a, 8, startRadius, endRadius, delta.magnitude + 0.015f, color,
                Quaternion.FromToRotation(Vector3.up, delta));
        }

        private static void CatalogWaterArcher(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Color blue = BodyPalette.WaterSlimes.Blue, pale = BodyPalette.WaterSlimes.Pale;
            Color deep = BodyPalette.WaterSlimes.Deep, foam = BodyPalette.WaterSlimes.Foam;
            Solids h = b.Hull;
            Legs(b, 0.29f, 0.13f, 0.105f, blue, deep);
            h.Taper(Point(0f, 0.23f, 0f), Pair(0.26f, 0.22f), Pair(0.33f, 0.24f), 0.31f, blue);
            h.Prism(Point(0f, 0.51f, 0f), 8, 0.08f, 0.075f, 0.13f, deep);
            h.Ball(Point(0f, 0.77f, 0.025f), Extent(0.25f, 0.24f, 0.215f), pale);
            h.Prism(Point(-0.03f, 0.92f, -0.06f), 7, 0.13f, 0f, 0.22f, pale, Turn(-28f, 0f, 18f));
            CatalogWaterRockEyes(h, Point(0f, 0.77f, 0.22f), 0.17f, 0.06f);
            h.Ball(Point(0f, 0.39f, 0.12f), Extent(0.035f, 0.06f, 0.012f), foam);
            CatalogWaterRockMark(b, h, Point(-0.11f, 0.48f, 0.115f), 0.09f);
            b.Arm.Origin = Point(0.16f, 0.52f, 0.01f);
            CatalogWaterTube(b.Arm, Point(0.16f, 0.52f, 0.01f), Point(0.3f, 0.55f, 0.38f), 0.065f, 0.055f, blue);
            // The rear hand draws the string: a bent arm and long arrow make the
            // archery pose readable before the player sees the unit's name.
            CatalogWaterTube(b.Arm, Point(-0.14f, 0.51f, 0f), Point(-0.21f, 0.5f, 0.17f), 0.055f, 0.045f, blue);
            CatalogWaterTube(b.Arm, Point(-0.21f, 0.5f, 0.17f), Point(0.3f, 0.55f, 0.21f), 0.045f, 0.035f, pale);
            // Broad C-shaped bow at the front, visibly separated from its string.
            Vector3 bottom = Point(0.32f, 0.13f, 0.4f), top = Point(0.32f, 1.01f, 0.4f);
            Vector3 previous = bottom;
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                Vector3 next = Point(0.32f, 0.13f + 0.88f * t, 0.4f + 0.28f * Mathf.Sin(t * Mathf.PI));
                CatalogWaterSegment(b.Arm, previous, next, 0.065f, deep);
                previous = next;
            }
            Vector3 draw = Point(0.32f, 0.55f, 0.21f);
            CatalogWaterSegment(b.Arm, bottom, draw, 0.015f, foam);
            CatalogWaterSegment(b.Arm, draw, top, 0.015f, foam);
            CatalogWaterSegment(b.Arm, draw, Point(0.32f, 0.55f, 0.84f), 0.023f, foam);
            b.Arm.Prism(Point(0.32f, 0.55f, 0.8f), 4, 0.045f, 0f, 0.12f, pale, Turn(90f, 0f, 0f));
        }

        private static void CatalogWaterDragon(Blueprint b)
        {
            b.Gait = Gait.Float; b.Limbs = LimbMotion.Flap; b.LimbsRideHull = true; b.Strike = Strike.Pulse;
            Solids h = b.Hull;
            Color cloud = BodyPalette.WaterSlimes.Cloud, foam = BodyPalette.WaterSlimes.Foam, sky = BodyPalette.WaterSlimes.Sky;
            // Separate chest and haunch masses along an elongated cloud spine.
            h.Ball(Point(0f, 0.4f, -0.21f), Extent(0.26f, 0.24f, 0.51f), cloud);
            h.Ball(Point(-0.12f, 0.45f, 0.02f), Extent(0.25f, 0.26f, 0.28f), foam);
            h.Ball(Point(0.13f, 0.39f, -0.23f), Extent(0.23f, 0.22f, 0.28f), cloud);
            h.Ball(Point(-0.08f, 0.44f, -0.46f), Extent(0.22f, 0.22f, 0.26f), foam);
            h.Ball(Point(0f, 0.68f, 0.25f), Extent(0.25f, 0.24f, 0.25f), foam);
            h.Ball(Point(0f, 0.59f, 0.46f), Extent(0.19f, 0.12f, 0.16f), cloud);
            h.Ball(Point(0f, 0.54f, 0.55f), Extent(0.09f, 0.022f, 0.045f), BodyPalette.WaterSlimes.Deep);
            CatalogWaterRockEyes(h, Point(0f, 0.71f, 0.47f), 0.25f, 0.07f);
            h.Prism(Point(0f, 0.87f, 0.26f), 6, 0.07f, 0f, 0.19f, sky, Turn(-15f, 0f, 0f));
            // The web catalog distinguishes the dragon with yellow dorsal nubs
            // and a small warm nose, beside its cyan horn and wings.
            h.Ball(Point(0f, 0.65f, 0.585f), Extent(0.045f, 0.028f, 0.025f), BodyPalette.TreeSpirits.Bolt);
            for (int i = 0; i < 4; i++)
                h.Prism(Point(0f, 0.69f - i * 0.02f, -0.08f - i * 0.17f), 5, 0.06f, 0f, 0.13f,
                    BodyPalette.TreeSpirits.Bolt, Turn(-15f, 0f, 0f));
            h.Ball(Point(-0.19f, 0.85f, 0.14f), Extent(0.1f, 0.05f, 0.12f), cloud, Turn(0f, 0f, 30f), 1);
            h.Ball(Point(0.19f, 0.85f, 0.14f), Extent(0.1f, 0.05f, 0.12f), cloud, Turn(0f, 0f, -30f), 1);
            h.Ball(Point(0.02f, 0.43f, -0.65f), Extent(0.16f, 0.16f, 0.24f), cloud);
            h.Ball(Point(0.03f, 0.53f, -0.83f), Extent(0.125f, 0.17f, 0.16f), foam);
            h.Ball(Point(0.03f, 0.7f, -0.86f), Extent(0.1f, 0.13f, 0.12f), cloud);
            h.Ball(Point(0.03f, 0.78f, -0.76f), Extent(0.085f, 0.08f, 0.12f), foam);
            CatalogWaterRockMark(b, h, Point(0f, 0.49f, 0.4f), 0.14f);
            Wing(b.LimbLeft, -1f, Point(0.23f, 0.57f, -0.09f), 0.43f, 0.37f, 0.13f, -0.12f, 26f, sky);
            Wing(b.LimbRight, 1f, Point(0.23f, 0.57f, -0.09f), 0.43f, 0.37f, 0.13f, -0.12f, 26f, sky);
            // The soft shoulders cover the moving wing roots at every flap angle.
            h.Ball(Point(-0.22f, 0.56f, -0.09f), Extent(0.13f, 0.1f, 0.17f), cloud);
            h.Ball(Point(0.22f, 0.56f, -0.09f), Extent(0.13f, 0.1f, 0.17f), cloud);
        }

        private static void CatalogRockPebble(Blueprint b)
        {
            b.Strike = Strike.Pulse;
            Color stone = BodyPalette.RockGolems.Tan, shade = BodyPalette.RockGolems.StoneShade;
            Solids h = b.Hull;
            for (int side = -1; side <= 1; side += 2)
            {
                Solids leg = side < 0 ? b.LimbLeft : b.LimbRight;
                leg.Origin = Point(side * 0.14f, 0.16f, 0f);
                leg.Lump(Point(side * 0.14f, 0.08f, 0.04f), Extent(0.12f, 0.08f, 0.14f), shade, Turn(0f, side * 18f, 0f));
            }
            h.Lump(Point(0f, 0.23f, 0f), Extent(0.25f, 0.17f, 0.21f), stone, Turn(0f, 12f, 0f));
            h.Lump(Point(0f, 0.55f, 0f), Extent(0.34f, 0.24f, 0.28f), stone, Turn(5f, -12f, 5f));
            h.Lump(Point(-0.28f, 0.28f, 0.06f), Extent(0.09f, 0.12f, 0.1f), shade, Turn(0f, -20f, -15f));
            CatalogWaterRockEyes(h, Point(0f, 0.57f, 0.27f), 0.18f, 0.041f);
            CatalogWaterRockMark(b, h, Point(0f, 0.23f, 0.21f), 0.13f);
            b.Arm.Origin = Point(0.22f, 0.24f, 0.04f);
            b.Arm.Lump(Point(0.27f, 0.23f, 0.11f), Extent(0.08f, 0.09f, 0.1f), shade, Turn(0f, 25f, 0f));
        }

        private static void CatalogRockGolem(Blueprint b)
        {
            Solids h = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone, moss = BodyPalette.RockGolems.Moss;
            for (int side = -1; side <= 1; side += 2)
            {
                Solids leg = side < 0 ? b.LimbLeft : b.LimbRight;
                leg.Origin = Point(side * 0.19f, 0.31f, 0f);
                leg.Lump(Point(side * 0.19f, 0.17f, 0f), Extent(0.17f, 0.18f, 0.18f), stone, Turn(12f, side * 14f, 0f));
                leg.Lump(Point(side * 0.2f, 0.07f, 0.1f), Extent(0.19f, 0.09f, 0.23f), tan, Turn(0f, side * 10f, 0f));
            }
            h.Lump(Point(0f, 0.51f, 0f), Extent(0.35f, 0.28f, 0.24f), tan, Turn(5f, 18f, 5f));
            h.Lump(Point(-0.16f, 0.42f, 0.12f), Extent(0.18f, 0.16f, 0.15f), stone, Turn(12f, -20f, 8f));
            h.Lump(Point(0.12f, 0.62f, 0.08f), Extent(0.23f, 0.19f, 0.19f), tan, Turn(-12f, 30f, 9f));
            h.Lump(Point(0f, 0.75f, 0.07f), Extent(0.13f, 0.09f, 0.13f), stone, Turn(0f, 8f, 0f));
            h.Ball(Point(0f, 0.86f, 0.08f), Extent(0.15f, 0.16f, 0.15f), tan, Turn(3f, 12f, 0f), 1);
            CatalogWaterRockEyes(h, Point(0f, 0.87f, 0.215f), 0.1f, 0.035f);
            for (int side = -1; side <= 1; side += 2)
            {
                Solids arm = side < 0 ? h : b.Arm;
                if (side > 0) arm.Origin = Point(0.35f, 0.73f, 0f);
                arm.Lump(Point(side * 0.31f, 0.65f, 0f), Extent(0.14f, 0.19f, 0.14f), stone, Turn(0f, side * 12f, 0f));
                arm.Lump(Point(side * 0.38f, 0.72f, -0.025f), Extent(0.25f, 0.19f, 0.23f), stone, Turn(0f, side * 27f, 15f));
                arm.Lump(Point(side * 0.46f, 0.47f, 0.055f), Extent(0.185f, 0.23f, 0.18f), tan, Turn(side * 12f, 10f, side * 10f));
                arm.Lump(Point(side * 0.51f, 0.25f, 0.15f), Extent(0.26f, 0.19f, 0.25f), stone, Turn(18f, side * 30f, 5f));
                arm.Dome(Point(side * 0.38f, 0.87f, -0.045f), Extent(0.19f, 0.055f, 0.17f), 7, 1, moss);
                arm.Ball(Point(side * 0.4f, 0.62f, 0.15f), Extent(0.09f, 0.025f, 0.06f), moss);
            }
            h.Dome(Point(-0.08f, 0.99f, 0.03f), Extent(0.11f, 0.035f, 0.09f), 6, 1, moss);
            h.Ball(Point(0.03f, 1.035f, 0.02f), Extent(0.065f, 0.014f, 0.03f), moss, Turn(0f, 0f, 30f), 1);
            CatalogWaterRockMark(b, h, Point(-0.12f, 0.55f, 0.225f), 0.16f);
        }

        private static void CatalogRockMage(Blueprint b)
        {
            // Keep the recessed face and crystal staff while removing the belt
            // that split the previous robe into two unrelated blocks.
            b.Strike = Strike.Recoil;
            Color stone = BodyPalette.RockGolems.Tan, shade = BodyPalette.RockGolems.StoneShade;
            BlockLegs(b, 0.14f, 0.1f, Extent(0.12f, 0.14f, 0.14f), shade);
            b.Hull.Taper(Point(0f, 0.08f, 0f), Pair(0.5f, 0.4f), Pair(0.3f, 0.26f), 0.5f, BodyPalette.RockGolems.Stone);
            b.Hull.Taper(Point(0f, 0.56f, -0.02f), Pair(0.34f, 0.32f), Pair(0.12f, 0.12f), 0.36f, shade);
            b.Hull.Box(Point(0f, 0.68f, 0.12f), Extent(0.18f, 0.16f, 0.06f), BodyPalette.RockGolems.MossDeep);
            CatalogWaterRockEyes(b.Hull, Point(0f, 0.69f, 0.155f), 0.08f, 0.033f);
            b.Hull.Box(Point(0f, 0.38f, 0.17f), Extent(0.065f, 0.065f, 0.02f), BodyPalette.RockGolems.Rune, Turn(0f, 0f, 45f));
            CatalogWaterRockMark(b, b.Hull, Point(-0.13f, 0.48f, 0.13f), 0.11f);
            b.Arm.Origin = Point(0.24f, 0.42f, 0.06f);
            b.Arm.Lump(Point(0.26f, 0.4f, 0.08f), Extent(0.08f, 0.09f, 0.08f), stone, Turn(0f, 15f, 0f));
            CatalogWaterTube(b.Arm, Point(0.26f, 0.4f, 0.08f), Point(0.37f, 0.43f, 0.12f), 0.065f, 0.055f, stone);
            b.Arm.Box(Point(0.37f, 0.47f, 0.12f), Extent(0.04f, 0.78f, 0.04f), shade);
            b.Arm.Prism(Point(0.37f, 0.8f, 0.12f), 5, 0.025f, 0.095f, 0.14f, BodyPalette.RockGolems.Rune);
            b.Arm.Prism(Point(0.37f, 0.94f, 0.12f), 5, 0.095f, 0f, 0.19f, BodyPalette.RockGolems.Rune);
            b.Hull.Lump(Point(-0.19f, 0.73f, 0.015f), Extent(0.105f, 0.19f, 0.16f), stone, Turn(0f, 18f, -12f));
            b.Hull.Lump(Point(0.19f, 0.76f, 0.01f), Extent(0.1f, 0.17f, 0.15f), stone, Turn(10f, -20f, 12f));
            b.Hull.Lump(Point(-0.025f, 0.93f, 0f), Extent(0.23f, 0.08f, 0.17f), stone, Turn(0f, -12f, 8f));
        }
    }
}
