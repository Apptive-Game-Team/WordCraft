using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static partial class Models
    {
        // Subject identity: theevilent.com/arcane-casters/magic/fire_spirit
        // and magma_spirit. Keep the shared Paper renderer and animated limbs.
        private static bool CatalogHellfire(Blueprint b, Faction faction, Role role, int slot)
        {
            if (faction != Faction.Hellfire || role != Role.Melee) return false;
            if (slot == 0) { CatalogMagma(b); return true; }
            if (slot == 1) { CatalogFireCrawler(b); return true; }
            return false;
        }

        private static void CatalogFireCrawler(Blueprint b)
        {
            b.Gait = Gait.Walk;
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Recoil;
            Color shell = BodyPalette.Hellfire.Shell, dark = BodyPalette.Hellfire.Charcoal;
            Color plate = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember;
            Solids hull = b.Hull;
            hull.Ball(Point(0f, 0.43f, -0.08f), Extent(0.3f, 0.19f, 0.46f), dark);
            // Overlapping carapace rather than a humanoid torso or carried weapon.
            for (int i = 0; i < 5; i++)
            {
                float z = -0.37f + i * 0.16f;
                float width = i == 0 || i == 4 ? 0.24f : 0.32f;
                hull.Lump(Point(0f, 0.55f, z), Extent(width, 0.12f, 0.14f),
                    i % 2 == 0 ? shell : plate, Turn(-16f, 0f, 0f));
                for (int side = -1; side <= 1; side += 2)
                    hull.Prism(Point(side * width * 0.8f, 0.56f, z), 4, 0.055f, 0f,
                        0.17f, dark, Turn(-55f, 0f, side * -30f));
            }
            hull.Taper(Point(0f, 0.4f, -0.48f), Pair(0.14f, 0.12f), Pair(0.015f, 0.015f),
                0.34f, shell, Turn(-100f, 0f, 0f));
            Slits(hull, Point(0f, 0.51f, 0.38f), 0.15f, 0.04f);
            b.Arm.Origin = Point(0f, 0.43f, 0.3f);
            b.Arm.Prism(Point(0f, 0.43f, 0.28f), 7, 0.14f, 0.11f, 0.3f, dark, Lying);
            b.Arm.Prism(Point(0f, 0.43f, 0.565f), 7, 0.08f, 0.08f, 0.012f, ember, Lying);
            b.Arm.Prism(Point(0f, 0.43f, 0.58f), 7, 0.044f, 0.044f, 0.012f, dark, Lying);
            FourLegs(b, 0.22f, 0.15f, -0.29f,
                (part, x, z) => CrawlerLeg(part, x, z, dark, plate,
                    Pair(0.055f, 0.14f), Pair(0.06f, 0.07f), 0.35f));
            // A small team-colored shell seam preserves ownership without a belt.
            if (b.HasAccent) hull.Box(Point(0f, 0.64f, -0.08f), Extent(0.18f, 0.018f, 0.035f), b.Accent);
        }

        private static void CatalogMagma(Blueprint b)
        {
            Color shell = BodyPalette.Hellfire.Shell, dark = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember;
            Solids hull = b.Hull;
            Legs(b, 0.26f, 0.16f, 0.18f, dark, shell);
            hull.Ball(Point(0f, 0.53f, 0f), Extent(0.3f, 0.3f, 0.23f), ember);
            for (int side = -1; side <= 1; side += 2)
            {
                hull.Lump(Point(side * 0.16f, 0.59f, 0.1f), Extent(0.17f, 0.21f, 0.17f), shell, Turn(0f, side * 12f, side * 14f));
                hull.Lump(Point(side * 0.27f, 0.77f, -0.02f), Extent(0.2f, 0.18f, 0.21f), dark, Turn(0f, side * 24f, 0f));
                hull.Prism(Point(side * 0.24f, 0.85f, -0.08f), 5, 0.07f, 0f, 0.17f, facet, Turn(-25f, 0f, side * -22f));
            }
            hull.Lump(Point(0f, 0.87f, 0.06f), Extent(0.16f, 0.16f, 0.15f), dark, Turn(0f, 15f, 0f));
            Slits(hull, Point(0f, 0.88f, 0.2f), 0.13f, 0.045f);
            // Massive faceted stone arms, separated by molten joints.
            CatalogMagmaArm(hull, -1f, shell, dark, ember);
            b.Arm.Origin = Point(0.34f, 0.74f, 0f);
            CatalogMagmaArm(b.Arm, 1f, shell, dark, ember);
            if (b.HasAccent) hull.Box(Point(0f, 0.35f, 0.19f), Extent(0.15f, 0.04f, 0.035f), b.Accent);
        }

        private static void CatalogMagmaArm(Solids part, float side, Color shell, Color dark, Color ember)
        {
            part.Ball(Point(side * 0.37f, 0.58f, 0f), Extent(0.13f, 0.2f, 0.14f), ember);
            part.Lump(Point(side * 0.4f, 0.58f, 0.015f), Extent(0.17f, 0.23f, 0.17f), shell, Turn(0f, side * 16f, side * 9f));
            part.Lump(Point(side * 0.44f, 0.31f, 0.09f), Extent(0.23f, 0.2f, 0.23f), dark, Turn(0f, side * 22f, 0f));
            for (int k = -1; k <= 1; k++)
                part.Lump(Point(side * 0.44f + k * 0.11f, 0.27f, 0.27f), Extent(0.065f, 0.1f, 0.07f), shell, Quaternion.identity);
        }
    }
}
