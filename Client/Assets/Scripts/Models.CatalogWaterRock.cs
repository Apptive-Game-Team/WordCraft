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

        private static void CatalogWaterDroplet(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids h = b.Hull;
            Color blue = BodyPalette.WaterSlimes.Sky, pale = BodyPalette.WaterSlimes.Pale;
            h.Ball(Point(0f, 0.37f, 0f), Extent(0.39f, 0.35f, 0.35f), blue);
            h.Ball(Point(-0.1f, 0.54f, -0.04f), Extent(0.25f, 0.24f, 0.25f), pale);
            // A rounded crown of droplets, not the old pointed cone.
            h.Ball(Point(-0.07f, 0.76f, -0.06f), Extent(0.09f, 0.12f, 0.09f), pale);
            h.Ball(Point(-0.17f, 0.85f, -0.04f), 0.065f, pale);
            for (int i = 0; i < 7; i++)
            {
                float angle = i * 360f / 7;
                Vector3 p = Around(angle) * 0.31f + Point(0f, 0.12f, 0f);
                h.Ball(p, 0.12f + (i % 2) * 0.025f, i % 2 == 0 ? pale : blue);
            }
            Eyes(h, Point(0f, 0.43f, 0.32f), 0.23f, 0.095f);
            h.Ball(Point(-0.17f, 0.58f, 0.22f), Extent(0.055f, 0.085f, 0.025f), BodyPalette.WaterSlimes.Foam);
            Band(b, h, Point(0f, 0.055f, 0f), 10, 0.34f, 0.04f);
            b.Arm.Origin = Point(0.28f, 0.22f, 0.08f);
            b.Arm.Ball(Point(0.34f, 0.24f, 0.18f), 0.105f, pale);
        }

        private static void CatalogWaterBubble(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Lunge;
            Solids h = b.Hull;
            Color pale = BodyPalette.WaterSlimes.Pale, blue = BodyPalette.WaterSlimes.Sky;
            h.Ball(Point(0f, 0.43f, 0.12f), Extent(0.32f, 0.31f, 0.31f), pale);
            h.Ball(Point(-0.05f, 0.27f, -0.08f), Extent(0.3f, 0.23f, 0.33f), blue);
            // Backward curling water tail; the negative-z arc stays behind the face.
            for (int i = 0; i < 10; i++)
            {
                float angle = (-100f + i * 28f) * Mathf.Deg2Rad;
                float radius = 0.28f - i * 0.013f;
                h.Ball(Point(-0.03f, 0.48f + Mathf.Sin(angle) * radius,
                    -0.29f + Mathf.Cos(angle) * radius), 0.115f - i * 0.004f, i < 5 ? blue : pale);
            }
            h.Ball(Point(-0.3f, 0.18f, -0.12f), 0.13f, pale);
            h.Ball(Point(0.27f, 0.17f, -0.04f), 0.11f, blue);
            h.Ball(Point(-0.17f, 0.76f, -0.18f), 0.09f, pale);
            Eyes(h, Point(0f, 0.45f, 0.405f), 0.19f, 0.075f);
            h.Ball(Point(-0.12f, 0.59f, 0.32f), Extent(0.05f, 0.07f, 0.025f), BodyPalette.WaterSlimes.Foam);
            Band(b, h, Point(0f, 0.12f, 0.02f), 10, 0.29f, 0.04f);
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

        private static void CatalogWaterArcher(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Color blue = BodyPalette.WaterSlimes.Blue, pale = BodyPalette.WaterSlimes.Pale;
            Color deep = BodyPalette.WaterSlimes.Deep, foam = BodyPalette.WaterSlimes.Foam;
            Solids h = b.Hull;
            Legs(b, 0.23f, 0.12f, 0.12f, blue, deep);
            h.Ball(Point(0f, 0.34f, 0f), Extent(0.23f, 0.22f, 0.19f), blue);
            h.Ball(Point(0f, 0.63f, 0.02f), Extent(0.28f, 0.26f, 0.24f), pale);
            h.Prism(Point(-0.03f, 0.79f, -0.06f), 6, 0.15f, 0f, 0.24f, pale, Turn(-28f, 0f, 18f));
            Eyes(h, Point(0f, 0.64f, 0.235f), 0.18f, 0.065f);
            h.Ball(Point(0f, 0.35f, 0.188f), Extent(0.045f, 0.065f, 0.016f), foam);
            Band(b, h, Point(0f, 0.25f, 0f), 8, 0.22f, 0.04f);
            b.Arm.Origin = Point(0.22f, 0.43f, 0.03f);
            b.Arm.Ball(Point(0.24f, 0.45f, 0.25f), Extent(0.1f, 0.085f, 0.16f), blue);
            // Broad C-shaped bow at the front, visibly separated from its string.
            Vector3 bottom = Point(0.28f, 0.12f, 0.31f), top = Point(0.28f, 0.9f, 0.31f);
            Vector3 previous = bottom;
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                Vector3 next = Point(0.28f, 0.12f + 0.78f * t, 0.31f + 0.2f * Mathf.Sin(t * Mathf.PI));
                CatalogWaterSegment(b.Arm, previous, next, 0.055f, deep);
                previous = next;
            }
            CatalogWaterSegment(b.Arm, bottom, top, 0.013f, foam);
            CatalogWaterSegment(b.Arm, Point(0.28f, 0.5f, 0.2f), Point(0.28f, 0.5f, 0.67f), 0.024f, foam);
            b.Arm.Prism(Point(0.28f, 0.5f, 0.64f), 4, 0.055f, 0f, 0.12f, pale, Turn(90f, 0f, 0f));
        }

        private static void CatalogWaterDragon(Blueprint b)
        {
            b.Gait = Gait.Float; b.Limbs = LimbMotion.Flap; b.LimbsRideHull = true; b.Strike = Strike.Pulse;
            Solids h = b.Hull;
            Color cloud = BodyPalette.WaterSlimes.Cloud, foam = BodyPalette.WaterSlimes.Foam, sky = BodyPalette.WaterSlimes.Sky;
            h.Ball(Point(0f, 0.4f, -0.08f), Extent(0.32f, 0.27f, 0.38f), cloud);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                h.Ball(Around(angle) * 0.25f + Point(0f, 0.37f + (i % 2) * 0.1f, -0.12f),
                    0.15f + (i % 3) * 0.025f, i % 2 == 0 ? cloud : foam);
            }
            h.Ball(Point(0f, 0.68f, 0.25f), Extent(0.25f, 0.24f, 0.25f), foam);
            h.Ball(Point(0f, 0.59f, 0.46f), Extent(0.19f, 0.12f, 0.16f), cloud);
            h.Ball(Point(0f, 0.54f, 0.55f), Extent(0.09f, 0.022f, 0.045f), BodyPalette.WaterSlimes.Deep);
            Eyes(h, Point(0f, 0.71f, 0.47f), 0.25f, 0.075f);
            h.Prism(Point(0f, 0.87f, 0.26f), 6, 0.07f, 0f, 0.19f, sky, Turn(-15f, 0f, 0f));
            // The web catalog distinguishes the dragon with yellow dorsal nubs
            // and a small warm nose, beside its cyan horn and wings.
            h.Ball(Point(0f, 0.65f, 0.585f), Extent(0.045f, 0.028f, 0.025f), BodyPalette.TreeSpirits.Bolt);
            for (int i = 0; i < 3; i++)
                h.Prism(Point(0f, 0.69f - i * 0.035f, -0.08f - i * 0.14f), 5, 0.055f, 0f, 0.11f,
                    BodyPalette.TreeSpirits.Bolt, Turn(-15f, 0f, 0f));
            h.Ball(Point(-0.19f, 0.85f, 0.14f), Extent(0.1f, 0.05f, 0.12f), cloud, Turn(0f, 0f, 30f), 1);
            h.Ball(Point(0.19f, 0.85f, 0.14f), Extent(0.1f, 0.05f, 0.12f), cloud, Turn(0f, 0f, -30f), 1);
            for (int i = 0; i < 8; i++)
            {
                float angle = (-90f + i * 32f) * Mathf.Deg2Rad;
                float radius = 0.17f - i * 0.01f;
                h.Ball(Point(0.02f, 0.4f + Mathf.Sin(angle) * radius, -0.48f + Mathf.Cos(angle) * radius),
                    0.11f - i * 0.004f, i < 4 ? cloud : foam);
            }
            Band(b, h, Point(0f, 0.45f, 0.2f), 8, 0.19f, 0.04f);
            Wing(b.LimbLeft, -1f, Point(0.23f, 0.57f, -0.09f), 0.31f, 0.3f, 0.12f, -0.08f, 24f, sky);
            Wing(b.LimbRight, 1f, Point(0.23f, 0.57f, -0.09f), 0.31f, 0.3f, 0.12f, -0.08f, 24f, sky);
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
            h.Lump(Point(0f, 0.55f, 0f), Extent(0.4f, 0.25f, 0.29f), stone, Turn(5f, -12f, 5f));
            Eyes(h, Point(0f, 0.57f, 0.27f), 0.18f, 0.045f);
            Strap(b, h, Point(0f, 0.24f, 0f), Extent(0.44f, 0.04f, 0.36f));
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
            h.Lump(Point(0f, 0.86f, 0.08f), Extent(0.15f, 0.16f, 0.15f), tan, Turn(3f, 12f, 0f));
            Eyes(h, Point(0f, 0.87f, 0.215f), 0.1f, 0.037f);
            for (int side = -1; side <= 1; side += 2)
            {
                Solids arm = side < 0 ? h : b.Arm;
                if (side > 0) arm.Origin = Point(0.35f, 0.73f, 0f);
                arm.Lump(Point(side * 0.36f, 0.72f, 0f), Extent(0.21f, 0.18f, 0.2f), stone, Turn(0f, side * 27f, 15f));
                arm.Lump(Point(side * 0.43f, 0.47f, 0.04f), Extent(0.17f, 0.23f, 0.17f), tan, Turn(side * 12f, 10f, side * 10f));
                arm.Lump(Point(side * 0.46f, 0.24f, 0.1f), Extent(0.22f, 0.17f, 0.22f), stone, Turn(18f, side * 30f, 5f));
                arm.Dome(Point(side * 0.36f, 0.86f, -0.02f), Extent(0.16f, 0.055f, 0.15f), 7, 1, moss);
                arm.Ball(Point(side * 0.4f, 0.62f, 0.15f), Extent(0.09f, 0.025f, 0.06f), moss);
            }
            h.Dome(Point(-0.08f, 0.99f, 0.03f), Extent(0.11f, 0.035f, 0.09f), 6, 1, moss);
            h.Ball(Point(0.03f, 1.035f, 0.02f), Extent(0.065f, 0.014f, 0.03f), moss, Turn(0f, 0f, 30f), 1);
            Strap(b, h, Point(0f, 0.38f, 0f), Extent(0.57f, 0.045f, 0.39f));
        }

        private static void CatalogRockMage(Blueprint b)
        {
            // Preserve the existing warm stone hood, face recess and animated
            // turquoise crystal staff; add the catalog's irregular hood stones.
            RockMage(b);
            Color stone = BodyPalette.RockGolems.Tan;
            b.Hull.Lump(Point(-0.2f, 0.73f, -0.025f), Extent(0.085f, 0.16f, 0.15f), stone, Turn(0f, 18f, -12f));
            b.Hull.Lump(Point(0.18f, 0.78f, -0.03f), Extent(0.075f, 0.13f, 0.13f), stone, Turn(10f, -20f, 12f));
            b.Hull.Lump(Point(-0.035f, 0.88f, -0.04f), Extent(0.18f, 0.07f, 0.15f), stone, Turn(0f, -12f, 8f));
        }
    }
}
