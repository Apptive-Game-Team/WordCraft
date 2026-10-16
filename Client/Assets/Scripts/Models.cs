using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Builds every body on the field as a low-poly solid, from code, at runtime.
    /// No FBX, no prefab, no material asset: issue #159 rules out binary assets,
    /// and a model that is a function of (faction, role, slot) is a model both
    /// peers build identically without shipping anything.
    ///
    /// .art/STYLE.md is the brief. Technique never varies — flat faces, three
    /// value bands from Paper.cginc, no outline, chunky proportions about three
    /// heads tall — and shape language plus palette always does:
    ///
    ///   세계수 정령   round and leafy: spheres, cones, eyes; no armour, no tools.
    ///   지옥불 군단   angular and spiked: tapered slabs, horns, a dark shell with
    ///                 ember only in eye slits, one chest core and a few seams.
    ///   물 슬라임     blobby domes in pale blues with an inner highlight.
    ///   돌 골렘 부족  stacked blocks and boulders, warm tan, moss on the tops.
    ///   차원 유랑종   blunt warm-brown plates with a vivid interior in the gaps.
    ///   인간 마법 문명 right angles and symmetry: cut stone, steel-blue roofs,
    ///                 bronze, wood, banners; no faces.
    ///
    /// The models follow the shipped sprites in Resources/Art/Sprites where a
    /// slot has one, so the HUD icon and the body on the field are the same
    /// creature. Where FACTIONS.md describes a concept with no art yet, the model
    /// follows the description and its 금지 list.
    ///
    /// Owner is a small accent in <see cref="UiStyle.Owner"/> on every body —
    /// a collar, belt, band, banner or shard — see <see cref="BodyPalette"/>.
    ///
    /// A design is built once per (kind, faction, role, slot, owner) and shared:
    /// meshes are never per body.
    /// </summary>
    public static partial class Models
    {
        /// <summary>World units between the top of a body and the health bar over it.</summary>
        private const float TopMargin = 0.25f;

        /// <summary>
        /// Per-face value grain. Three and a half percent: enough that two faces
        /// of one boulder never land on exactly the same value, too little to
        /// read as texture.
        /// </summary>
        private const float Grain = 0.035f;

        private const float NodeFootprint = 1.4f;
        private const float NeutralRockFootprint = 1.2f;
        private const float RemnantFootprint = 1.0f;

        private static readonly Dictionary<long, Design> designs = new Dictionary<long, Design>();
        private static Design remnant;
        private static Material opaque;
        private static Material ghost;
        private static int built;

        /// <summary>The shared opaque body material. Every body on the field draws with this one instance.</summary>
        public static Material Opaque => opaque != null ? opaque : (opaque = CreateMaterial("Shaders/Body", "WordCraft Body"));

        /// <summary>The shared see-through material: a construction site, the placement ghost, fading debris.</summary>
        public static Material Ghost => ghost != null ? ghost : (ghost = CreateMaterial("Shaders/BodyGhost", "WordCraft Body Ghost"));

        /// <summary>
        /// One body for one roster entry, under <paramref name="parent"/> at local
        /// zero, facing +z. A resource node and the map's neutral 꼬마돌 come out
        /// of here too, chosen by kind and the neutral flag rather than by roster.
        /// </summary>
        public static BodyRig Build(Transform parent, Faction faction, Role role, int slot, EntityKind kind, int owner, bool neutral)
        {
            Design design = DesignFor(faction, role, slot, kind, owner, neutral);
            return Attach(parent, design, Label(faction, role, slot, kind, neutral));
        }

        /// <summary>A pile of rubble covering one cell: 돌 골렘 부족's 잔해, which blocks the cell it lies on.</summary>
        public static BodyRig Remnant(Transform parent)
        {
            if (remnant == null || remnant.Hull == null) remnant = RemnantDesign();
            return Attach(parent, remnant, "Remnant");
        }

        private static BodyRig Attach(Transform parent, Design design, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var rig = go.AddComponent<BodyRig>();
            rig.Assemble(design, built++);
            return rig;
        }

        private static Material CreateMaterial(string path, string name)
        {
            Shader shader = Resources.Load<Shader>(path);
            if (shader == null)
            {
                Debug.LogWarning($"Models: Resources/{path}.shader is missing; bodies fall back to Standard and lose the Paper lighting.");
                shader = Shader.Find("Standard");
            }
            return new Material(shader) { name = name };
        }

        private static string Label(Faction faction, Role role, int slot, EntityKind kind, bool neutral)
        {
            if (kind == EntityKind.ResourceNode) return "ResourceNode";
            if (neutral) return "꼬마돌";
            string name = role == Role.None ? "" : FactionData.Name(faction, role, slot);
            return name.Length > 0 ? name : role.ToString();
        }

        // ------------------------------------------------------------------
        // Composition
        // ------------------------------------------------------------------

        private static Design DesignFor(Faction faction, Role role, int slot, EntityKind kind, int owner, bool neutral)
        {
            long key = Key(faction, role, slot, kind, owner, neutral);
            if (designs.TryGetValue(key, out Design cached) && cached.Hull != null) return cached;

            Design made = Compose(faction, role, slot, kind, owner, neutral);
            designs[key] = made;
            return made;
        }

        /// <summary>
        /// The cache key. Owner is in it because the accent is baked into vertex
        /// colour; a neutral or a node has no owner and no faction worth keying.
        /// </summary>
        private static long Key(Faction faction, Role role, int slot, EntityKind kind, int owner, bool neutral)
        {
            if (kind == EntityKind.ResourceNode) return -1;
            if (neutral) return -2;
            long key = (int)kind;
            key = key * 16 + (int)faction;
            key = key * 16 + (int)role;
            key = key * 16 + Mathf.Clamp(slot, 0, 15);
            key = key * 64 + Mathf.Clamp(owner + 1, 0, 63);
            return key;
        }

        private static Design Compose(Faction faction, Role role, int slot, EntityKind kind, int owner, bool neutral)
        {
            if (kind == EntityKind.ResourceNode) return NodeDesign();
            if (neutral || (owner < 0 && kind != EntityKind.Building)) return NeutralRockDesign();

            bool accent = BodyPalette.HasAccent(owner, false);
            Color accentColor = accent ? BodyPalette.Accent(owner) : Color.white;
            int seed = Seed(faction, role, slot);
            string name = Label(faction, role, slot, kind, false);

            if (FactionData.IsBuilding(role) || kind == EntityKind.Building)
            {
                float footprint = Cells(role);
                var building = new Blueprint(seed, 1f, accent, accentColor) { Gait = Gait.Still };
                Building(building, faction, role, slot);
                return Finish(building, name, footprint, footprint, true, false);
            }

            float size = 0.9f * Cells(role) * (role == Role.Signature ? 1.5f : 1f);
            var unit = new Blueprint(seed, size, accent, accentColor);
            Unit(unit, faction, role, slot);

            bool flying = role != Role.None && FactionData.Stats(faction, role, slot).Air;
            if (flying) unit.Gait = Gait.Fly;
            return Finish(unit, name, Cells(role), size, false, flying);
        }

        /// <summary>
        /// A role's footprint in cells: Base 4, Production 3, Defense 2.5, Worker
        /// 1.2, everything else 1.8. The same table as MatchView.Cells, copied
        /// rather than called so the factory does not depend on the file it is
        /// being wired into; if one changes, change both.
        /// </summary>
        private static float Cells(Role role)
        {
            switch (role)
            {
                case Role.Base: return 4.0f;
                case Role.Production: return 3.0f;
                case Role.Defense: return 2.5f;
                case Role.Worker: return 1.2f;
                default: return 1.8f;
            }
        }

        private static int Seed(Faction faction, Role role, int slot) =>
            (((int)faction + 1) * 7919 + (int)role * 104729 + slot * 1299709) & 0x7FFFFFFF;

        private static Design Finish(Blueprint b, string name, float footprint, float size, bool building, bool flying)
        {
            var design = new Design
            {
                Gait = building ? Gait.Still : b.Gait,
                Limbs = b.Limbs,
                Strike = b.Strike,
                LimbsRideHull = b.LimbsRideHull,
                CrownSpin = b.CrownSpin,
                CrownAims = b.CrownAims,
                CrownKick = b.CrownKick,
                Footprint = footprint,
                Size = size,
                Building = building,
                Flying = flying,
                Tint = b.Tint,
                Assembled = b.Assembled,
                Assembly = b.Assembly,
                Debris = b.Debris,
                Mote = b.Mote,
                Glow = b.Glow,
            };

            design.Hull = b.Hull.Bake(name);
            design.Arm = Bake(b.Arm, name + " arm", out design.ArmPivot);
            design.LimbLeft = Bake(b.LimbLeft, name + " limb left", out design.LimbLeftPivot);
            design.LimbRight = Bake(b.LimbRight, name + " limb right", out design.LimbRightPivot);
            design.Crown = Bake(b.Crown, name + " crown", out design.CrownPivot);
            design.HullHeight = Mathf.Max(b.Hull.Highest * b.Hull.Scale, 0.1f);

            float highest = Mathf.Max(Mathf.Max(b.Hull.Highest, b.Arm.Highest),
                                      Mathf.Max(Mathf.Max(b.LimbLeft.Highest, b.LimbRight.Highest), b.Crown.Highest));
            int tiers = 0;
            for (int i = 0; i < b.Tiers.Count; i++)
            {
                if (!b.Tiers[i].Empty) tiers++;
            }
            if (tiers > 0)
            {
                design.Tiers = new Mesh[tiers];
                design.TierPivots = new Vector3[tiers];
                design.TierHeights = new float[tiers];
                int k = 0;
                for (int i = 0; i < b.Tiers.Count; i++)
                {
                    Solids tier = b.Tiers[i];
                    if (tier.Empty) continue;
                    design.Tiers[k] = Bake(tier, name + " tier " + k, out design.TierPivots[k]);
                    design.TierHeights[k] = Mathf.Max((tier.Highest - tier.Origin.y) * tier.Scale, 0.1f);
                    highest = Mathf.Max(highest, tier.Highest);
                    k++;
                }
            }

            switch (design.Gait)
            {
                case Gait.Hop: design.Stride = 0.9f * size; break;
                case Gait.Walk: design.Stride = WalkStride(design, b.Limbs, size); break;
                default: design.Stride = size; break;
            }

            design.Top = highest * b.Hull.Scale + (flying ? BodyRig.HoverHeight : 0f) + TopMargin;
            return design;
        }

        /// <summary>
        /// Ground covered by one full gait cycle, chosen so a planted foot does not
        /// skate. A swinging leg's foot travels 2·hip·sin(swing) back while it is
        /// planted, which is half a cycle, so the cycle covers twice that; a
        /// stepping crawler's foot slides back half its stride, which BodyRig
        /// sets its reach from. A walker with no legs to plant keeps the old
        /// fraction of its height.
        /// </summary>
        private static float WalkStride(Design design, LimbMotion limbs, float size)
        {
            bool legs = design.LimbLeft != null || design.LimbRight != null;
            if (legs && limbs == LimbMotion.Swing)
            {
                float hip = Mathf.Max(design.LimbLeft != null ? design.LimbLeftPivot.y : design.LimbRightPivot.y, 0.05f * size);
                return 4f * hip * Mathf.Sin(BodyRig.SwingDegrees * Mathf.Deg2Rad);
            }
            return (limbs == LimbMotion.Step ? 0.4f : 0.6f) * size;
        }

        private static Mesh Bake(Solids part, string name, out Vector3 pivot)
        {
            pivot = part.Origin * part.Scale;
            return part.Empty ? null : part.Bake(name);
        }

        /// <summary>
        /// One body while it is being written: the static hull, and the parts
        /// that move. Every part shares the body's scale, so all of them are
        /// authored in the same coordinates and only their pivots differ.
        /// </summary>
        private sealed class Blueprint
        {
            public readonly Solids Hull, Arm, LimbLeft, LimbRight, Crown;
            public readonly bool HasAccent;
            public readonly Color Accent;
            public readonly System.Random Random;
            public Gait Gait = Gait.Walk;
            public LimbMotion Limbs = LimbMotion.Swing;
            public Strike Strike = Strike.Lunge;
            public bool LimbsRideHull;
            public float CrownSpin, CrownKick;
            public bool CrownAims;
            public Color Tint = Color.white;
            public Assembly Assembly;
            public bool Assembled;
            public Color Debris, Mote, Glow;

            /// <summary>The hull's later parts, in the order a site puts them up.</summary>
            public readonly List<Solids> Tiers = new List<Solids>(4);
            private readonly int seed;
            private readonly float scale;

            public Blueprint(int seed, float scale, bool hasAccent, Color accent)
            {
                this.seed = seed;
                this.scale = scale;
                Hull = Part(seed, 0, scale);
                Arm = Part(seed, 1, scale);
                LimbLeft = Part(seed, 2, scale);
                LimbRight = Part(seed, 3, scale);
                Crown = Part(seed, 4, scale);
                HasAccent = hasAccent;
                Accent = accent;
                Random = new System.Random(seed ^ 0x5bd1e995);
            }

            private static Solids Part(int seed, int index, float scale) =>
                new Solids(seed * 31 + index, Grain) { Scale = scale };

            /// <summary>
            /// Starts the hull's next part, pivoting on <paramref name="origin"/>:
            /// a building's builder writes the parts a site puts up in turn,
            /// switching to a new one here. The first part is <see cref="Hull"/>.
            /// </summary>
            public Solids Tier(Vector3 origin)
            {
                var tier = new Solids(seed * 31 + 5 + Tiers.Count, Grain) { Scale = scale, Origin = origin };
                Tiers.Add(tier);
                return tier;
            }
        }

        // ------------------------------------------------------------------
        // Shorthand for authoring. Units are authored one unit tall, feet at
        // y = 0, facing +z, right hand at +x. Buildings are authored in world
        // units directly.
        // ------------------------------------------------------------------

        private static Vector3 Point(float x, float y, float z) => new Vector3(x, y, z);
        private static Vector3 Extent(float x, float y, float z) => new Vector3(x, y, z);
        private static Vector2 Pair(float x, float z) => new Vector2(x, z);
        private static Quaternion Turn(float x, float y, float z) => Quaternion.Euler(x, y, z);

        /// <summary>A part lying along the body: local +y becomes +z, so a taper's height runs nose-ward.</summary>
        private static readonly Quaternion Lying = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>Unit vector on the ground at a compass yaw in degrees, 0 = +z.</summary>
        private static Vector3 Around(float yawDegrees)
        {
            float radians = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        /// <summary>Faces <paramref name="yawDegrees"/> and tips the top outward by <paramref name="tilt"/> (inward when negative).</summary>
        private static Quaternion Outward(float yawDegrees, float tilt) =>
            Quaternion.Euler(0f, yawDegrees, 0f) * Quaternion.Euler(tilt, 0f, 0f);

        /// <summary>
        /// STYLE.md's eye: a dark oval with one tiny highlight, upper left. Looks
        /// down +z, or down -z when <paramref name="facing"/> is -1: a building
        /// stands unturned, so a face on it looks at the camera's side, -z.
        /// </summary>
        private static void Eyes(Solids part, Vector3 middle, float spacing, float size, float facing = 1f)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 eye = middle + new Vector3(side * spacing * 0.5f, 0f, 0f);
                part.Ball(eye, Extent(size * 0.62f, size, size * 0.45f), BodyPalette.Eye, Quaternion.identity, 0);
                part.Ball(eye + Extent(-size * 0.2f, size * 0.38f, facing * size * 0.36f), Vector3.one * size * 0.24f,
                          BodyPalette.EyeShine, Quaternion.identity, 0);
            }
        }

        /// <summary>
        /// 지옥불's eyes: two glowing slits, outer ends raised. STYLE.md: "No large
        /// friendly eyes. Eyes are glowing slits."
        /// </summary>
        private static void Slits(Solids part, Vector3 middle, float spacing, float width)
        {
            for (int side = -1; side <= 1; side += 2)
                part.Box(middle + new Vector3(side * spacing * 0.5f, 0f, 0f), Extent(width, width * 0.3f, width * 0.35f),
                         BodyPalette.Hellfire.Ember, Turn(0f, 0f, side * 16f));
        }

        /// <summary>The owner accent as a ring round a round body. Nothing on a neutral.</summary>
        private static void Band(Blueprint b, Solids part, Vector3 bottom, int sides, float radius, float height, float depth = 1f)
        {
            if (b.HasAccent) part.Prism(bottom, sides, radius, radius, height, b.Accent, Quaternion.identity, depth);
        }

        /// <summary>The owner accent as a strap, belt or collar round a boxy body. Nothing on a neutral.</summary>
        private static void Strap(Blueprint b, Solids part, Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            if (!b.HasAccent) return;
            part.Box(center, size, b.Accent, rotation ?? Quaternion.identity);
        }

        /// <summary>The owner accent as a floating shard: two pyramids base to base. For factions that fly no flags.</summary>
        private static void Shard(Blueprint b, Solids part, Vector3 center, float size)
        {
            if (!b.HasAccent) return;
            part.Prism(center, 4, size, 0f, size * 1.5f, b.Accent);
            part.Prism(center, 4, size, 0f, size * 1.2f, b.Accent, Turn(180f, 0f, 0f));
        }

        /// <summary>The owner accent as a flag on a pole, for the factions that build. Faces the camera's side of the map.</summary>
        private static void Pennant(Blueprint b, Solids part, Vector3 foot, float height, Color pole)
        {
            if (!b.HasAccent) return;
            part.Prism(foot, 5, 0.05f, 0.04f, height, pole);
            part.Box(foot + Point(0.24f, height - 0.22f, 0f), Extent(0.44f, 0.34f, 0.035f), b.Accent);
        }

        /// <summary>Two legs hinged at the hip, for a body that walks upright.</summary>
        private static void Legs(Blueprint b, float hip, float halfSpacing, float thickness, Color leg, Color foot)
        {
            Leg(b.LimbLeft, -halfSpacing, hip, thickness, leg, foot);
            Leg(b.LimbRight, halfSpacing, hip, thickness, leg, foot);
        }

        private static void Leg(Solids part, float x, float hip, float thickness, Color leg, Color foot)
        {
            part.Origin = Point(x, hip, 0f);
            part.Taper(Point(x, 0.05f, 0f), Pair(thickness, thickness), Pair(thickness * 1.15f, thickness * 1.15f), hip - 0.03f, leg);
            part.Box(Point(x, 0.04f, 0.035f), Extent(thickness * 1.3f, 0.08f, thickness * 1.6f), foot);
        }

        /// <summary>Two block legs, for the golems: a stacked stone has no ankle.</summary>
        private static void BlockLegs(Blueprint b, float hip, float halfSpacing, Vector3 size, Color leg)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Solids part = side < 0 ? b.LimbLeft : b.LimbRight;
                float x = side * halfSpacing;
                part.Origin = Point(x, hip, 0f);
                part.Box(Point(x, size.y * 0.5f, 0.01f), size, leg);
            }
        }

        /// <summary>One leg of a crawler, drawn at (x, z) into the limb part it belongs to.</summary>
        private delegate void LegAt(Solids part, float x, float z);

        /// <summary>
        /// Four legs in diagonal pairs: left front and right back on the left
        /// limb, right front and left back on the right, so a stepping crawler
        /// lifts one diagonal while the other is planted.
        /// </summary>
        private static void FourLegs(Blueprint b, float x, float front, float back, LegAt leg)
        {
            leg(b.LimbLeft, -x, front);
            leg(b.LimbLeft, x, back);
            leg(b.LimbRight, x, front);
            leg(b.LimbRight, -x, back);
        }

        /// <summary>
        /// Six legs in two tripods: left front, right middle and left back step
        /// together, and the other three together.
        /// </summary>
        private static void SixLegs(Blueprint b, Color color, float front, float middle, float back)
        {
            float[] rows = { front, middle, back };
            for (int i = 0; i < rows.Length; i++)
            {
                bool first = i % 2 == 0;
                MarksmanLeg(first ? b.LimbLeft : b.LimbRight, -1f, rows[i], color);
                MarksmanLeg(first ? b.LimbRight : b.LimbLeft, 1f, rows[i], color);
            }
        }

        /// <summary>
        /// A chain of prisms, each pitched <paramref name="bend"/> degrees further
        /// about x than the last and <paramref name="taper"/> times as thick: a
        /// vine, a curling wave. Returns where the last one ends.
        /// </summary>
        private static Vector3 Curl(Solids part, Vector3 start, int count, int sides, float radius, float taper, float length,
                                    float advance, float pitch, float bend, Color first, Color second)
        {
            Vector3 position = start;
            for (int i = 0; i < count; i++)
            {
                Quaternion rotation = Turn(pitch, 0f, 0f);
                part.Prism(position, sides, radius, radius * taper, length, i % 2 == 0 ? first : second, rotation);
                position += rotation * Point(0f, advance, 0f);
                pitch += bend;
                radius *= taper;
            }
            return position;
        }

        /// <summary>
        /// One wing, fin or vane, lying near flat so it beats about the body's
        /// long axis and reads from a camera above. <paramref name="side"/> is -1
        /// for the left, +1 for the right; the root is the hinge.
        /// </summary>
        private static void Wing(Solids part, float side, Vector3 root, float span, float chord, float tipChord,
                                 float sweep, float dihedral, Color color)
        {
            Vector3 hinge = Point(side * Mathf.Abs(root.x), root.y, root.z);
            part.Origin = hinge;
            part.Taper(hinge, Pair(0.035f, chord), Pair(0.025f, tipChord), span, color,
                       Turn(0f, 0f, -side * (90f - dihedral)), Pair(0f, sweep));
        }

        // ------------------------------------------------------------------
        // Units
        // ------------------------------------------------------------------

        /// <summary>
        /// The unit builder for this entry; failing that, entry 0's of the same
        /// slot; failing that, the generic body.
        /// </summary>
        private static void Unit(Blueprint b, Faction faction, Role role, int slot)
        {
            if (FactionUnit(b, faction, role, slot)) return;
            if (slot > 0 && FactionUnit(b, faction, role, 0)) return;
            GenericUnit(b, faction);
        }

        /// <summary>True when a builder written for this very entry drew it into <paramref name="b"/>; false, and nothing drawn, otherwise.</summary>
        private static bool FactionUnit(Blueprint b, Faction faction, Role role, int slot)
        {
            if (CatalogWaterRock(b, faction, role, slot)) return true;
            if (CatalogHellfire(b, faction, role, slot)) return true;
            switch (faction)
            {
                case Faction.TreeSpirits: return TreeSpiritUnit(b, role, slot);
                case Faction.Hellfire: return HellfireUnit(b, role, slot);
                case Faction.WaterSlimes: return WaterSlimeUnit(b, role, slot);
                case Faction.RockGolems: return RockGolemUnit(b, role, slot);
                case Faction.Driftworlds: return DriftworldUnit(b, role, slot);
                default: return HumanUnit(b, role, slot);
            }
        }

        /// <summary>
        /// Whether a roster entry has a body written for it rather than falling
        /// back to entry 0's body or the generic one. ViewSelfCheck asks this of
        /// every entry, so a row added without a builder fails there instead of
        /// quietly drawing as its neighbour. Builds into a throwaway blueprint;
        /// not for the per-frame path.
        /// </summary>
        public static bool HasBuilder(Faction faction, Role role, int slot)
        {
            var scratch = new Blueprint(0, 1f, false, Color.white);
            if (!FactionData.IsBuilding(role)) return FactionUnit(scratch, faction, role, slot);
            return slot > 0 ? ExtraBuilding(scratch, faction, role, slot) : FactionBuilding(scratch, faction, role);
        }

        /// <summary>
        /// A body for a roster entry no model was written for: a plain biped in
        /// the faction's two main colours. FACTIONS.md marks 인간's melee slot
        /// empty by design, so this is what a stray one would look like rather
        /// than a crash.
        /// </summary>
        private static void GenericUnit(Blueprint b, Faction faction)
        {
            Color main = BodyPalette.Main(faction), second = BodyPalette.Second(faction);
            Legs(b, 0.28f, 0.12f, 0.13f, second, second);
            b.Hull.Taper(Point(0f, 0.26f, 0f), Pair(0.4f, 0.3f), Pair(0.46f, 0.32f), 0.38f, main);
            b.Hull.Ball(Point(0f, 0.8f, 0.02f), Extent(0.2f, 0.19f, 0.19f), main);
            if (faction != Faction.Humans && faction != Faction.Hellfire) Eyes(b.Hull, Point(0f, 0.82f, 0.18f), 0.12f, 0.05f);
            Strap(b, b.Hull, Point(0f, 0.58f, 0f), Extent(0.48f, 0.06f, 0.34f));
            b.Arm.Origin = Point(0.27f, 0.58f, 0f);
            b.Arm.Taper(Point(0.29f, 0.3f, 0.02f), Pair(0.1f, 0.1f), Pair(0.11f, 0.11f), 0.3f, second);
        }

        // ---- 세계수 정령 ----

        private static bool TreeSpiritUnit(Blueprint b, Role role, int slot)
        {
            switch (role)
            {
                case Role.Worker when slot == 0: SeedSpirit(b); return true;
                case Role.Melee when slot == 0: LeafBlade(b); return true;
                case Role.Melee when slot == 1: TreeGuardian(b); return true;
                case Role.Melee when slot == 2: ZapMouse(b); return true;
                case Role.Melee when slot == 3: StormRider(b); return true;
                case Role.Ranged when slot == 0: ThunderSpirit(b); return true;
                case Role.Ranged when slot == 1: VineSpirit(b); return true;
                case Role.Ranged when slot == 2: ThunderBirds(b); return true;
                case Role.Ranged when slot == 3: GiantVine(b); return true;
                case Role.Signature when slot == 0: WindSpirit(b); return true;
                case Role.Signature when slot == 1: WillOWisp(b); return true;
                case Role.Signature when slot == 2: VineColony(b); return true;
                default: return false;
            }
        }

        /// <summary>풀씨 정령: a hopping green seed with two leaves and tiny flowers; no carried tools.</summary>
        private static void SeedSpirit(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;

            hull.Ball(Point(0f, 0.36f, 0f), Extent(0.36f, 0.34f, 0.34f), BodyPalette.TreeSpirits.LeafLight);
            for (int i = 0; i < 6; i++)
            {
                float yaw = 30f + i * 60f;
                hull.Prism(Around(yaw) * 0.27f + Point(0f, 0.02f, 0f), 4, 0.07f, 0f, 0.2f,
                           i % 2 == 0 ? BodyPalette.TreeSpirits.Leaf : BodyPalette.TreeSpirits.LeafShade, Outward(yaw, 35f));
            }
            hull.Prism(Point(0f, 0.66f, 0f), 5, 0.025f, 0.02f, 0.15f, BodyPalette.TreeSpirits.LeafShade);
            TreePointedLeaf(hull, Point(-0.22f, 0.85f, 0f), 0.19f, 0.58f, BodyPalette.TreeSpirits.Leaf, Turn(0f, 0f, 65f));
            TreePointedLeaf(hull, Point(0.22f, 0.85f, 0f), 0.19f, 0.58f, BodyPalette.TreeSpirits.Leaf, Turn(0f, 0f, -65f));
            for (int i = 0; i < 5; i++)
            {
                float yaw = i * 72f;
                TreePointedLeaf(hull, Around(yaw) * 0.3f + Point(0f, 0.14f, 0f), 0.13f, 0.33f,
                    BodyPalette.TreeSpirits.LeafShade, Outward(yaw, 65f));
            }
            Eyes(hull, Point(0f, 0.4f, 0.31f), 0.2f, 0.075f);
            // The seed has foliage and flowers, never a carried tool.
            b.Arm.Origin = Point(0.3f, 0.32f, 0.05f);
            for (int i = 0; i < 3; i++)
            {
                float yaw = 30f + i * 120f;
                hull.Ball(Around(yaw) * 0.28f + Point(0f, 0.16f, 0f),
                          Extent(0.055f, 0.025f, 0.055f), BodyPalette.TreeSpirits.WindLight, Quaternion.identity, 1);
            }
        }

        /// <summary>잎날 정령: a faceless layered leaf whorl with a long trailing tip. Fast raider, slot 0.</summary>
        private static void LeafBlade(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;

            // Leafair is a faceless whorl of overlapping leaves, not a swordsman.
            hull.Prism(Point(0f, 0.12f, 0f), 6, 0.04f, 0.2f, 0.65f, BodyPalette.TreeSpirits.LeafShade);
            for (int i = 0; i < 12; i++)
            {
                float yaw = i * 137.5f;
                float y = 0.21f + i * 0.065f;
                TreePointedLeaf(hull, Around(yaw) * (0.11f + i * 0.006f) + Point(0f, y, 0f),
                    0.14f, 0.57f - i * 0.012f, i % 3 == 0 ? BodyPalette.TreeSpirits.LeafLight : BodyPalette.TreeSpirits.Leaf,
                    Outward(yaw, -40f));
            }
            b.Arm.Origin = Point(0.27f, 0.5f, 0f);
            TreePointedLeaf(hull, Point(0.04f, 0.97f, -0.09f), 0.055f, 0.6f,
                BodyPalette.TreeSpirits.LeafLight, Turn(-24f, 0f, -12f));
        }

        // A thin folded leaf with two pointed ends; broad facets survive the game camera.
        private static void TreePointedLeaf(Solids part, Vector3 center, float width, float length, Color color, Quaternion rotation)
        {
            Vector3 root = center + rotation * Point(0f, -length * 0.5f, 0f);
            part.Prism(root, 4, 0f, width, length * 0.38f, color, rotation, 0.27f);
            part.Prism(root + rotation * Point(0f, length * 0.38f, 0f), 4, width, 0f,
                length * 0.62f, color, rotation, 0.27f);
        }

        private static void TreeFlowingStem(Solids part, Vector3[] path, float radius, Color color)
        {
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 segment = path[i] - path[i - 1];
                float from = radius * (1f - (i - 1f) / (path.Length - 1) * 0.94f);
                float to = radius * (1f - i / (float)(path.Length - 1) * 0.94f);
                part.Prism(path[i - 1], 7, from, to, segment.magnitude + 0.018f, color,
                    Quaternion.FromToRotation(Vector3.up, segment));
            }
        }

        /// <summary>고목 수호자: a living trunk guardian with branch sprouts and heavy wooden fists.</summary>
        private static void TreeGuardian(Blueprint b)
        {
            Color bark = BodyPalette.TreeSpirits.Bark, barkShade = BodyPalette.TreeSpirits.BarkShade;
            Solids hull = b.Hull;

            for (int side = -1; side <= 1; side += 2)
            {
                Solids leg = side < 0 ? b.LimbLeft : b.LimbRight;
                leg.Origin = Point(side * 0.15f, 0.3f, 0f);
                leg.Prism(Point(side * 0.15f, 0.06f, 0f), 6, 0.09f, 0.075f, 0.27f, bark, Turn(0f, 0f, -side * 7f));
                leg.Ball(Point(side * 0.15f, 0.19f, 0.01f), 0.095f, barkShade);
                leg.Lump(Point(side * 0.15f, 0.06f, 0.06f), Extent(0.11f, 0.065f, 0.15f), bark, Turn(0f, side * 15f, 0f));
            }
            hull.Taper(Point(0f, 0.28f, 0f), Pair(0.46f, 0.32f), Pair(0.6f, 0.38f), 0.4f, bark);
            for (int i = -1; i <= 1; i++)
                hull.Prism(Point(i * 0.13f, 0.34f, 0.18f), 5, 0.019f, 0.012f, 0.29f,
                    barkShade, Turn(0f, 0f, i * -9f));
            hull.Prism(Point(0f, 0.7f, 0.03f), 9, 0.2f, 0.18f, 0.36f, bark);
            hull.Prism(Point(0f, 1.058f, 0.03f), 9, 0.145f, 0.145f, 0.008f, Color.Lerp(bark, BodyPalette.TreeSpirits.LeafLight, 0.25f));
            hull.Prism(Point(-0.07f, 1.01f, 0.02f), 5, 0.025f, 0.015f, 0.16f, barkShade, Turn(0f, 0f, 22f));
            hull.Ball(Point(-0.1f, 1.14f, 0.02f), Extent(0.07f, 0.025f, 0.04f), BodyPalette.TreeSpirits.Leaf, Turn(0f, 0f, -25f), 1);
            hull.Lump(Point(-0.27f, 0.72f, -0.02f), Extent(0.13f, 0.1f, 0.13f), BodyPalette.TreeSpirits.LeafLight, Turn(10f, 0f, 0f));
            hull.Lump(Point(0.25f, 0.73f, -0.05f), Extent(0.11f, 0.09f, 0.11f), BodyPalette.TreeSpirits.Leaf, Turn(0f, 40f, 10f));
            Eyes(hull, Point(0f, 0.86f, 0.218f), 0.16f, 0.045f);
            for (int side = -1; side <= 1; side += 2)
            {
                hull.Prism(Point(side * 0.24f, 0.67f, -0.02f), 6, 0.085f, 0.035f, 0.32f, barkShade, Turn(0f, 0f, -side * 65f));
                TreePointedLeaf(hull, Point(side * 0.34f, 0.82f, 0.02f), 0.13f, 0.36f,
                    BodyPalette.TreeSpirits.Leaf, Turn(0f, side * 20f, -side * 55f));
            }
            hull.Prism(Point(-0.37f, 0.3f, 0.02f), 6, 0.08f, 0.075f, 0.38f, bark, Turn(0f, 0f, -6f));
            hull.Ball(Point(-0.37f, 0.48f, 0.02f), 0.095f, barkShade);
            hull.Lump(Point(-0.38f, 0.26f, 0.03f), Extent(0.11f, 0.12f, 0.11f), bark, Turn(0f, 15f, -8f));

            b.Arm.Origin = Point(0.36f, 0.66f, 0.02f);
            b.Arm.Prism(Point(0.37f, 0.32f, 0.02f), 6, 0.08f, 0.09f, 0.36f, bark);
            b.Arm.Ball(Point(0.37f, 0.49f, 0.02f), 0.1f, barkShade);
            b.Arm.Lump(Point(0.38f, 0.27f, 0.04f), Extent(0.12f, 0.13f, 0.12f), bark, Turn(0f, -15f, 8f));
            b.Arm.Lump(Point(0.4f, 0.62f, 0f), Extent(0.09f, 0.06f, 0.09f), BodyPalette.TreeSpirits.Leaf, Turn(0f, 30f, 0f));
        }

        /// <summary>번개 정령: a round yellow spirit with a natural lightning mane, never a held weapon. Slot 0.</summary>
        private static void ThunderSpirit(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;

            hull.Ball(Point(0f, 0.5f, 0f), Extent(0.33f, 0.31f, 0.31f), BodyPalette.TreeSpirits.Bolt);
            hull.Prism(Point(0f, 0.74f, -0.04f), 4, 0.09f, 0f, 0.27f, BodyPalette.TreeSpirits.BoltLight, Turn(-30f, 0f, 0f));
            hull.Prism(Point(-0.1f, 0.72f, -0.02f), 4, 0.07f, 0f, 0.2f, BodyPalette.TreeSpirits.Bolt, Turn(-20f, 0f, 25f));
            hull.Prism(Point(0.1f, 0.72f, -0.02f), 4, 0.07f, 0f, 0.2f, BodyPalette.TreeSpirits.BoltDeep, Turn(-20f, 0f, -25f));
            hull.Prism(Point(0f, 0.1f, -0.02f), 5, 0f, 0.14f, 0.24f, BodyPalette.TreeSpirits.BoltDeep);
            Eyes(hull, Point(0f, 0.53f, 0.285f), 0.2f, 0.07f);
            Color bolt = BodyPalette.TreeSpirits.BoltLight;
            b.Arm.Origin = Point(0.3f, 0.5f, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                hull.Prism(Point(side * 0.22f, 0.6f, -0.06f), 4, 0.075f, 0f, 0.3f, bolt, Turn(-55f, 0f, -side * 58f));
                hull.Prism(Point(side * 0.23f, 0.4f, -0.08f), 4, 0.055f, 0f, 0.24f, bolt, Turn(-100f, 0f, -side * 65f));
                TreePointedLeaf(hull, Point(side * 0.2f, 0.78f, -0.15f), 0.11f, 0.5f,
                    side < 0 ? bolt : BodyPalette.TreeSpirits.Bolt, Turn(-48f, side * 24f, -side * 40f));
            }
        }

        /// <summary>덩쿨 정령: a leaf-headed spirit trailing a vine tail, lashing with a vine. Slot 1, grown not bought.</summary>
        private static void VineSpirit(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;

            hull.Ball(Point(0f, 0.66f, 0f), Extent(0.25f, 0.23f, 0.23f), BodyPalette.TreeSpirits.LeafLight);
            // Continuous tapered S-shaped vine, with a broad leaf framing the face.
            Vector3 vine = Point(0f, 0.49f, -0.02f);
            float vineRadius = 0.095f;
            for (int i = 0; i < 9; i++)
            {
                float t = (i + 1) / 9f;
                Vector3 next = Point(Mathf.Sin(t * Mathf.PI * 2f) * 0.22f, 0.49f - t * 0.46f, -0.02f - t * 0.13f);
                Vector3 segment = next - vine;
                float nextRadius = 0.095f * (1f - t * 0.82f);
                hull.Prism(vine, 6, vineRadius, nextRadius, segment.magnitude + 0.025f,
                           BodyPalette.TreeSpirits.Leaf, Quaternion.FromToRotation(Vector3.up, segment));
                vine = next;
                vineRadius = nextRadius;
            }
            TreePointedLeaf(hull, Point(-0.16f, 0.82f, -0.04f), 0.19f, 0.57f, BodyPalette.TreeSpirits.LeafShade, Turn(0f, 0f, -36f));
            TreePointedLeaf(hull, Point(0.16f, 0.87f, -0.04f), 0.22f, 0.62f, BodyPalette.TreeSpirits.Leaf, Turn(0f, 0f, 48f));
            Eyes(hull, Point(0f, 0.68f, 0.215f), 0.15f, 0.06f);

            b.Arm.Origin = Point(0.18f, 0.55f, 0f);
            TreeFlowingStem(b.Arm, new[] { Point(0.18f, 0.55f, 0f), Point(0.32f, 0.53f, 0.15f),
                Point(0.4f, 0.6f, 0.3f), Point(0.35f, 0.72f, 0.37f), Point(0.24f, 0.75f, 0.32f) },
                0.065f, BodyPalette.TreeSpirits.Leaf);
            TreePointedLeaf(b.Arm, Point(0.35f, 0.69f, 0.31f), 0.08f, 0.25f,
                BodyPalette.TreeSpirits.LeafLight, Turn(0f, 20f, -60f));
        }

        /// <summary>바람 정령: a large pale teardrop with a curling tail and swept wisps. The faction's signature.</summary>
        private static void WindSpirit(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;

            hull.Ball(Point(0f, 0.62f, 0.04f), Extent(0.3f, 0.3f, 0.28f), BodyPalette.TreeSpirits.WindLight);
            TreeFlowingStem(hull, new[] { Point(0f, 0.48f, -0.06f), Point(0f, 0.3f, -0.19f),
                Point(0.04f, 0.18f, -0.4f), Point(0.1f, 0.16f, -0.62f), Point(0.14f, 0.25f, -0.82f),
                Point(0.12f, 0.41f, -0.91f), Point(0.06f, 0.48f, -0.84f) }, 0.23f, BodyPalette.TreeSpirits.Wind);
            hull.Ball(Point(0f, 0.9f, -0.1f), Extent(0.1f, 0.05f, 0.2f), BodyPalette.TreeSpirits.WindLight, Turn(25f, 0f, 0f), 1);
            hull.Ball(Point(-0.17f, 0.84f, -0.08f), Extent(0.07f, 0.04f, 0.16f), BodyPalette.TreeSpirits.Wind, Turn(25f, -25f, 0f), 1);
            hull.Ball(Point(0.17f, 0.84f, -0.08f), Extent(0.07f, 0.04f, 0.16f), BodyPalette.TreeSpirits.Wind, Turn(25f, 25f, 0f), 1);
            Eyes(hull, Point(0f, 0.65f, 0.3f), 0.18f, 0.06f);
            for (int side = -1; side <= 1; side += 2)
                TreeFlowingStem(hull, new[] { Point(side * 0.2f, 0.75f, 0f), Point(side * 0.32f, 0.78f, -0.22f),
                    Point(side * 0.38f, 0.85f, -0.48f), Point(side * 0.34f, 0.99f, -0.62f),
                    Point(side * 0.25f, 1.02f, -0.56f) }, 0.1f, BodyPalette.TreeSpirits.WindLight);

            b.Arm.Origin = Point(0.27f, 0.58f, 0f);
            b.Arm.Ball(Point(0.36f, 0.52f, 0.1f), Extent(0.07f, 0.05f, 0.18f), BodyPalette.TreeSpirits.WindDeep, Turn(-20f, 20f, 0f), 1);
        }

        /// <summary>찌릿쥐: a round yellow mouse scurrying on four stub feet, spiky fur down its back, a zigzag tail. Slot 2.</summary>
        private static void ZapMouse(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;
            Color bolt = BodyPalette.TreeSpirits.Bolt, deep = BodyPalette.TreeSpirits.BoltDeep, light = BodyPalette.TreeSpirits.BoltLight;

            hull.Ball(Point(0f, 0.28f, -0.06f), Extent(0.28f, 0.22f, 0.36f), bolt);
            hull.Ball(Point(0f, 0.34f, 0.24f), Extent(0.22f, 0.2f, 0.2f), bolt);
            hull.Ball(Point(0f, 0.3f, 0.38f), Extent(0.09f, 0.07f, 0.08f), light);
            hull.Ball(Point(0f, 0.32f, 0.46f), 0.03f, BodyPalette.Eye);
            for (int k = 0; k < 5; k++)
                hull.Prism(Point(0f, 0.44f, -0.32f + k * 0.12f), 4, 0.07f, 0f, 0.15f, k % 2 == 0 ? deep : bolt, Turn(-45f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)
            {
                hull.Ball(Point(side * 0.17f, 0.57f, 0.2f), Extent(0.13f, 0.19f, 0.055f), bolt, Turn(0f, 0f, -side * 20f), 1);
                hull.Ball(Point(side * 0.17f, 0.59f, 0.25f), Extent(0.075f, 0.12f, 0.018f), deep, Turn(0f, 0f, -side * 20f), 1);
            }
            Eyes(hull, Point(0f, 0.4f, 0.41f), 0.17f, 0.05f);
            hull.Box(Point(0f, 0.36f, -0.44f), Extent(0.05f, 0.16f, 0.05f), deep, Turn(-35f, 0f, 0f));
            hull.Box(Point(0f, 0.48f, -0.5f), Extent(0.05f, 0.16f, 0.05f), deep, Turn(35f, 0f, 0f));
            hull.Box(Point(0f, 0.6f, -0.56f), Extent(0.07f, 0.16f, 0.07f), light, Turn(-35f, 0f, 0f));

            FourLegs(b, 0.15f, 0.16f, -0.24f, (part, x, z) => part.Ball(Point(x, 0.06f, z), Extent(0.07f, 0.06f, 0.09f), deep));

            b.Arm.Origin = Point(0f, 0.3f, 0.36f);
            b.Arm.Box(Point(0.17f, 0.3f, 0.44f), Extent(0.03f, 0.12f, 0.03f), light, Turn(0f, 0f, -35f));
            b.Arm.Box(Point(-0.17f, 0.3f, 0.44f), Extent(0.03f, 0.12f, 0.03f), light, Turn(0f, 0f, 35f));
        }

        /// <summary>폭풍 기수: a small yellow spirit riding a cloud colt, a long bolt for a lance. No armour, per STYLE.md. Slot 3.</summary>
        private static void StormRider(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;
            Color light = BodyPalette.TreeSpirits.WindLight, wind = BodyPalette.TreeSpirits.Wind, deep = BodyPalette.TreeSpirits.WindDeep;

            hull.Ball(Point(0f, 0.32f, 0f), Extent(0.3f, 0.2f, 0.4f), light);
            hull.Ball(Point(-0.2f, 0.25f, -0.16f), 0.17f, wind);
            hull.Ball(Point(0.2f, 0.24f, -0.12f), 0.16f, light);
            hull.Ball(Point(0f, 0.24f, -0.42f), 0.14f, wind);
            hull.Ball(Point(0.05f, 0.32f, -0.56f), 0.08f, deep);
            hull.Ball(Point(0f, 0.44f, 0.38f), Extent(0.17f, 0.15f, 0.17f), light);
            // A raised neck and flowing mane separate the mount from a flat cloud platform.
            hull.Ball(Point(0f, 0.45f, 0.26f), Extent(0.16f, 0.26f, 0.15f), wind, Turn(25f, 0f, 0f), 1);
            TreeFlowingStem(hull, new[] { Point(0f, 0.6f, 0.3f), Point(0f, 0.65f, 0.1f),
                Point(0f, 0.6f, -0.12f), Point(0f, 0.51f, -0.3f) }, 0.12f, deep);
            hull.Prism(Point(0f, 0.56f, 0.4f), 4, 0.04f, 0f, 0.15f, deep, Turn(-30f, 0f, 0f));
            Eyes(hull, Point(0f, 0.47f, 0.53f), 0.14f, 0.045f);
            Band(b, hull, Point(0f, 0.3f, 0f), 10, 0.31f, 0.05f, 1.3f);

            hull.Ball(Point(0f, 0.66f, -0.04f), Extent(0.17f, 0.17f, 0.16f), BodyPalette.TreeSpirits.Bolt);
            hull.Prism(Point(0f, 0.8f, -0.08f), 4, 0.07f, 0f, 0.2f, BodyPalette.TreeSpirits.BoltLight, Turn(-35f, 0f, 0f));
            hull.Prism(Point(-0.06f, 0.79f, -0.06f), 4, 0.05f, 0f, 0.14f, BodyPalette.TreeSpirits.BoltDeep, Turn(-25f, 0f, 25f));
            Eyes(hull, Point(0f, 0.68f, 0.13f), 0.1f, 0.04f);

            b.Arm.Origin = Point(0.16f, 0.64f, 0f);
            b.Arm.Prism(Point(0.18f, 0.6f, -0.14f), 5, 0.025f, 0.018f, 0.66f, BodyPalette.TreeSpirits.BoltLight, Turn(80f, 0f, 0f));
            b.Arm.Prism(Point(0.18f, 0.71f, 0.5f), 4, 0.05f, 0f, 0.14f, BodyPalette.TreeSpirits.Bolt, Turn(80f, 0f, 0f));
        }

        /// <summary>번개새 무리: three yellow birds flying as one, the lead one beating its wings. Airborne, slot 2.</summary>
        private static void ThunderBirds(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color bolt = BodyPalette.TreeSpirits.Bolt, deep = BodyPalette.TreeSpirits.BoltDeep;

            Bird(hull, Point(0f, 0.46f, 0.08f), 1.25f);
            Bird(hull, Point(-0.36f, 0.3f, -0.32f), 0.75f);
            Bird(hull, Point(0.36f, 0.62f, -0.34f), 0.75f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 small = side < 0 ? Point(-0.36f, 0.33f, -0.34f) : Point(0.36f, 0.65f, -0.36f);
                hull.Taper(small + Point(-0.06f, 0f, 0f), Pair(0.02f, 0.13f), Pair(0.02f, 0.05f), 0.2f, bolt, Turn(0f, 0f, 70f));
                hull.Taper(small + Point(0.06f, 0f, 0f), Pair(0.02f, 0.13f), Pair(0.02f, 0.05f), 0.2f, deep, Turn(0f, 0f, -70f));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Solids wing = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(wing, side, Point(0.12f, 0.5f, 0.04f), 0.42f, 0.24f, 0.08f, -0.12f, 18f, bolt);
                wing.Box(Point(side * 0.44f, 0.6f, -0.04f), Extent(0.14f, 0.03f, 0.08f), BodyPalette.TreeSpirits.WindDeep, Turn(0f, 0f, -side * 18f));
                for (int feather = 0; feather < 3; feather++)
                    TreePointedLeaf(wing, Point(side * (0.3f + feather * 0.095f), 0.54f + feather * 0.025f, -0.14f - feather * 0.04f),
                        0.075f, 0.38f - feather * 0.04f, feather == 1 ? bolt : BodyPalette.TreeSpirits.WindDeep,
                        Turn(-72f, side * 25f, -side * 38f));
            }
        }

        /// <summary>One of 번개새 무리's birds: a yellow body, a beak, a zigzag crest, round eyes. Faces +z.</summary>
        private static void Bird(Solids hull, Vector3 at, float scale)
        {
            Color bolt = BodyPalette.TreeSpirits.Bolt, deep = BodyPalette.TreeSpirits.BoltDeep;
            hull.Ball(at, Extent(0.13f, 0.11f, 0.19f) * scale, bolt);
            Vector3 head = at + Point(0f, 0.08f, 0.15f) * scale;
            hull.Ball(head, 0.1f * scale, BodyPalette.TreeSpirits.BoltLight);
            hull.Prism(head + Point(0f, -0.01f, 0.08f) * scale, 4, 0.04f * scale, 0f, 0.09f * scale, deep, Lying);
            hull.Prism(head + Point(0f, 0.08f, -0.02f) * scale, 4, 0.035f * scale, 0f, 0.1f * scale, deep, Turn(-40f, 0f, 0f));
            hull.Prism(at + Point(0f, 0.02f, -0.16f) * scale, 4, 0.06f * scale, 0f, 0.16f * scale, BodyPalette.TreeSpirits.WindDeep, Turn(-110f, 0f, 0f));
            Eyes(hull, head + Point(0f, 0.02f, 0.08f) * scale, 0.1f * scale, 0.035f * scale);
        }

        /// <summary>거대 덩쿨: a vine column twisting up from its roots to a budded head, rooted where it grew. Slot 3, the siege.</summary>
        private static void GiantVine(Blueprint b)
        {
            // Walk with no legs: rooted where it grew, it never moves and must not hover.
            b.Gait = Gait.Walk;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color leaf = BodyPalette.TreeSpirits.Leaf, shade = BodyPalette.TreeSpirits.LeafShade, light = BodyPalette.TreeSpirits.LeafLight;

            for (int i = 0; i < 4; i++)
            {
                float yaw = 45f + i * 90f;
                hull.Taper(Around(yaw) * 0.08f, Pair(0.12f, 0.12f), Pair(0.04f, 0.04f), 0.32f, BodyPalette.TreeSpirits.LeafDeep, Outward(yaw, 68f));
            }
            Band(b, hull, Point(0f, 0.04f, 0f), 7, 0.2f, 0.06f);

            Vector3 position = Point(0f, 0.02f, 0f);
            float radius = 0.17f;
            for (int i = 0; i < 6; i++)
            {
                float yaw = i * 70f;
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * 0.22f + Around(yaw) * 0.07f).normalized);
                hull.Prism(position, 6, radius, radius * 0.88f, 0.25f, i % 2 == 0 ? leaf : shade, rotation);
                position += rotation * Point(0f, 0.22f, 0f);
                radius *= 0.88f;
                if (i % 2 == 1)
                    hull.Ball(position + Around(yaw + 90f) * 0.13f, Extent(0.13f, 0.03f, 0.07f), light,
                              Quaternion.Euler(0f, yaw, 0f) * Turn(0f, 0f, 22f), 1);
            }
            hull.Ball(position + Point(0f, 0.08f, 0f), Extent(0.16f, 0.15f, 0.16f), light);
            for (int side = -1; side <= 1; side += 2)
                TreePointedLeaf(hull, position + Point(side * 0.18f, 0.13f, -0.03f), 0.18f, 0.55f,
                    side < 0 ? shade : leaf, Turn(0f, side * 25f, -side * 50f));
            Eyes(hull, position + Point(0f, 0.1f, 0.14f), 0.11f, 0.045f);

            b.Arm.Origin = position + Point(0f, 0.12f, 0f);
            Curl(b.Arm, position + Point(0f, 0.16f, -0.02f), 4, 5, 0.06f, 0.8f, 0.16f, 0.15f, -10f, 45f, leaf, shade);
        }

        /// <summary>도깨비불: a pale blue flame with eyes, its tongues streaming back. Airborne, weaponless; the side tongues beat like fins.</summary>
        private static void WillOWisp(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color light = BodyPalette.TreeSpirits.WindLight, wind = BodyPalette.TreeSpirits.Wind, deep = BodyPalette.TreeSpirits.WindDeep;

            hull.Ball(Point(0f, 0.36f, 0f), Extent(0.2f, 0.2f, 0.2f), wind);
            hull.Ball(Point(0f, 0.34f, 0.08f), 0.14f, light);
            hull.Prism(Point(0f, 0.42f, -0.06f), 6, 0.17f, 0f, 0.46f, wind, Turn(-48f, 0f, 0f));
            hull.Prism(Point(-0.08f, 0.4f, -0.06f), 5, 0.1f, 0f, 0.32f, deep, Turn(-58f, 0f, 32f));
            hull.Prism(Point(0.08f, 0.4f, -0.06f), 5, 0.1f, 0f, 0.32f, deep, Turn(-58f, 0f, -32f));
            hull.Prism(Point(0f, 0.3f, -0.12f), 5, 0.08f, 0f, 0.3f, deep, Turn(-100f, 0f, 0f));
            TreeFlowingStem(hull, new[] { Point(0f, 0.39f, -0.05f), Point(0.06f, 0.56f, -0.25f),
                Point(0.08f, 0.71f, -0.42f), Point(0.04f, 0.76f, -0.6f), Point(-0.04f, 0.7f, -0.69f) }, 0.13f, light);
            Eyes(hull, Point(0f, 0.37f, 0.2f), 0.13f, 0.05f);
            Band(b, hull, Point(0f, 0.22f, 0f), 10, 0.17f, 0.04f);

            for (int side = -1; side <= 1; side += 2)
            {
                Solids fin = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(fin, side, Point(0.15f, 0.36f, -0.04f), 0.2f, 0.16f, 0.03f, -0.14f, 30f, wind);
            }
        }

        /// <summary>덩굴 군집: a ball of knotted vines with eyes peeking out, leaves on top. Bounces along slowly; weaponless.</summary>
        private static void VineColony(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color leaf = BodyPalette.TreeSpirits.Leaf, shade = BodyPalette.TreeSpirits.LeafShade, deep = BodyPalette.TreeSpirits.LeafDeep;

            hull.Ball(Point(0f, 0.42f, 0f), Extent(0.42f, 0.38f, 0.4f), shade);
            // Thick crossing vines, not solid disk bands that obscure the living core.
            for (int i = 0; i < 4; i++)
            {
                Quaternion turn = Turn(28f + i * 33f, i * 47f, i * 19f);
                var path = new Vector3[10];
                for (int j = 0; j < path.Length; j++)
                {
                    float angle = j * 36f * Mathf.Deg2Rad;
                    path[j] = Point(0f, 0.42f, 0f) + turn * Point(Mathf.Cos(angle) * 0.44f, 0f, Mathf.Sin(angle) * 0.42f);
                }
                TreeFlowingStem(hull, path, 0.085f, i % 2 == 0 ? leaf : deep);
            }
            hull.Lump(Point(0.02f, 0.8f, 0.02f), Extent(0.12f, 0.08f, 0.12f), leaf, Turn(0f, 30f, 0f));
            hull.Ball(Point(-0.12f, 0.88f, 0f), Extent(0.15f, 0.035f, 0.08f), BodyPalette.TreeSpirits.LeafLight, Turn(0f, 0f, -30f), 1);
            hull.Ball(Point(0.13f, 0.86f, -0.04f), Extent(0.13f, 0.035f, 0.07f), BodyPalette.TreeSpirits.LeafLight, Turn(0f, 0f, 28f), 1);
            Eyes(hull, Point(0f, 0.48f, 0.4f), 0.18f, 0.06f);
            Band(b, hull, Point(0f, 0.1f, 0f), 10, 0.29f, 0.05f);

            b.Arm.Origin = Point(0.36f, 0.42f, 0f);
            b.Arm.Prism(Point(0.38f, 0.4f, 0.02f), 5, 0.04f, 0.02f, 0.3f, leaf, Turn(50f, 0f, -20f));
            b.Arm.Ball(Point(0.44f, 0.6f, 0.24f), Extent(0.07f, 0.03f, 0.05f), BodyPalette.TreeSpirits.LeafLight);
        }

        // ---- 지옥불 군단 ----

        private static bool HellfireUnit(Blueprint b, Role role, int slot)
        {
            switch (role)
            {
                case Role.Worker when slot == 0: EmberDemon(b); return true;
                case Role.Melee when slot == 0: MagmaDemon(b); return true;
                case Role.Melee when slot == 1: FireDemon(b); return true;
                case Role.Melee when slot == 2: RiftHound(b); return true;
                case Role.Melee when slot == 3: EmberWhelp(b); return true;
                case Role.Ranged when slot == 0: WarlordSpawn(b); return true;
                case Role.Ranged when slot == 1: RiftWarden(b); return true;
                case Role.Ranged when slot == 2: LavaCatapult(b); return true;
                case Role.Ranged when slot == 3: AshWingDemon(b); return true;
                case Role.Signature when slot == 0: Warlord(b); return true;
                case Role.Signature when slot == 1: BroodDemon(b); return true;
                case Role.Signature when slot == 2: MeteorInvoker(b); return true;
                default: return false;
            }
        }

        /// <summary>잿불 악마: a low spined lizard-demon on four legs, hauling a lump of ash ore.</summary>
        private static void EmberDemon(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Lunge;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal, facet = BodyPalette.Hellfire.Facet;
            Solids hull = b.Hull;

            hull.Taper(Point(0f, 0.36f, -0.38f), Pair(0.36f, 0.3f), Pair(0.3f, 0.24f), 0.6f, shell, Lying);
            hull.Taper(Point(0f, 0.4f, 0.2f), Pair(0.28f, 0.24f), Pair(0.16f, 0.1f), 0.3f, shell, Lying);
            hull.Box(Point(0f, 0.52f, -0.08f), Extent(0.28f, 0.05f, 0.42f), facet, Turn(-4f, 0f, 0f));
            for (int k = 0; k < 4; k++)
                hull.Prism(Point(0f, 0.53f, -0.32f + k * 0.14f), 4, 0.06f, 0f, 0.17f - k * 0.015f,
                           k % 2 == 0 ? charcoal : facet, Turn(-25f, 0f, 0f));
            hull.Prism(Point(-0.08f, 0.48f, 0.34f), 4, 0.04f, 0f, 0.16f, facet, Turn(-45f, 0f, 20f));
            hull.Prism(Point(0.08f, 0.48f, 0.34f), 4, 0.04f, 0f, 0.16f, facet, Turn(-45f, 0f, -20f));
            hull.Taper(Point(0f, 0.36f, -0.36f), Pair(0.12f, 0.1f), Pair(0.02f, 0.02f), 0.42f, charcoal, Turn(-105f, 0f, 0f));
            Slits(hull, Point(0f, 0.43f, 0.5f), 0.11f, 0.065f);
            hull.Lump(Point(0f, 0.62f, -0.18f), Extent(0.11f, 0.09f, 0.11f), BodyPalette.Hellfire.Ash, Turn(0f, 30f, 0f));
            Strap(b, hull, Point(0f, 0.39f, 0.18f), Extent(0.33f, 0.31f, 0.06f));

            FourLegs(b, 0.18f, 0.1f, -0.28f, (part, x, z) => CrawlerLeg(part, x, z, charcoal, facet));
        }

        private static void CrawlerLeg(Solids part, float x, float z, Color leg, Color claw) =>
            CrawlerLeg(part, x, z, leg, claw, Pair(0.1f, 0.13f), Pair(0.08f, 0.08f), 0.3f);

        /// <summary>A crawler's leg of a given foot, knee and height, with a claw at its toe.</summary>
        private static void CrawlerLeg(Solids part, float x, float z, Color leg, Color claw, Vector2 foot, Vector2 knee, float height)
        {
            part.Taper(Point(x, 0f, z), foot, knee, height, leg);
            part.Prism(Point(x, 0.02f, z + 0.05f), 4, 0.03f, 0f, 0.08f, claw, Turn(80f, 0f, 0f));
        }

        /// <summary>용암 갑각 악마: a hulking biped of shell plates, ember seams between them, one huge fist. Asymmetric on purpose.</summary>
        private static void MagmaDemon(Blueprint b)
        {
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember;
            Solids hull = b.Hull;

            Legs(b, 0.3f, 0.17f, 0.17f, charcoal, shell);
            hull.Taper(Point(0f, 0.28f, 0f), Pair(0.48f, 0.34f), Pair(0.54f, 0.37f), 0.21f, shell);
            hull.Box(Point(0f, 0.51f, 0f), Extent(0.5f, 0.05f, 0.34f), ember);
            hull.Taper(Point(0f, 0.52f, 0f), Pair(0.58f, 0.38f), Pair(0.68f, 0.42f), 0.24f, shell, Turn(0f, 0f, 3f));
            hull.Box(Point(-0.35f, 0.78f, 0f), Extent(0.22f, 0.13f, 0.3f), facet, Turn(0f, 0f, 14f));
            hull.Box(Point(0.35f, 0.79f, 0f), Extent(0.2f, 0.12f, 0.3f), facet, Turn(0f, 0f, -18f));
            hull.Taper(Point(0f, 0.75f, 0.06f), Pair(0.2f, 0.2f), Pair(0.15f, 0.15f), 0.16f, charcoal);
            hull.Prism(Point(-0.08f, 0.9f, 0.06f), 4, 0.04f, 0f, 0.17f, facet, Turn(-15f, 0f, 30f));
            hull.Prism(Point(0.08f, 0.9f, 0.06f), 4, 0.04f, 0f, 0.13f, facet, Turn(-15f, 0f, -35f));
            Slits(hull, Point(0f, 0.84f, 0.165f), 0.09f, 0.05f);
            hull.Ball(Point(0f, 0.64f, 0.205f), 0.06f, ember);
            hull.Taper(Point(-0.42f, 0.36f, 0.02f), Pair(0.17f, 0.17f), Pair(0.2f, 0.2f), 0.38f, shell);
            hull.Box(Point(-0.42f, 0.3f, 0.04f), Extent(0.24f, 0.2f, 0.24f), charcoal);
            Strap(b, hull, Point(0f, 0.31f, 0f), Extent(0.52f, 0.06f, 0.38f));

            b.Arm.Origin = Point(0.4f, 0.74f, 0f);
            b.Arm.Taper(Point(0.42f, 0.34f, 0.02f), Pair(0.17f, 0.17f), Pair(0.2f, 0.2f), 0.4f, shell);
            b.Arm.Box(Point(0.42f, 0.28f, 0.05f), Extent(0.25f, 0.21f, 0.25f), charcoal);
            b.Arm.Box(Point(0.42f, 0.33f, 0.18f), Extent(0.2f, 0.03f, 0.02f), ember);
        }

        /// <summary>지옥불 군단장의 자손: a small horned imp on jagged wings. Airborne, slot 0.</summary>
        private static void WarlordSpawn(Blueprint b)
        {
            b.Gait = Gait.Fly;
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Recoil;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal, facet = BodyPalette.Hellfire.Facet;
            Solids hull = b.Hull;

            hull.Taper(Point(0f, 0.2f, 0f), Pair(0.22f, 0.18f), Pair(0.32f, 0.26f), 0.3f, shell);
            hull.Taper(Point(0f, 0.5f, 0.02f), Pair(0.26f, 0.24f), Pair(0.2f, 0.18f), 0.2f, charcoal);
            hull.Prism(Point(-0.08f, 0.68f, 0.02f), 4, 0.04f, 0f, 0.18f, facet, Turn(-20f, 0f, 35f));
            hull.Prism(Point(0.08f, 0.68f, 0.02f), 4, 0.04f, 0f, 0.18f, facet, Turn(-20f, 0f, -35f));
            Slits(hull, Point(0f, 0.6f, 0.14f), 0.09f, 0.05f);
            hull.Ball(Point(0f, 0.38f, 0.14f), 0.05f, BodyPalette.Hellfire.Ember);
            hull.Taper(Point(0f, 0.22f, -0.08f), Pair(0.06f, 0.06f), Pair(0.01f, 0.01f), 0.3f, charcoal, Turn(-140f, 0f, 0f));
            hull.Prism(Point(-0.07f, 0.21f, 0.02f), 4, 0.04f, 0f, 0.14f, charcoal, Turn(180f, 0f, 0f));
            hull.Prism(Point(0.07f, 0.21f, 0.02f), 4, 0.04f, 0f, 0.14f, charcoal, Turn(180f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.49f, 0.01f), Extent(0.3f, 0.04f, 0.28f));

            for (int side = -1; side <= 1; side += 2)
            {
                Solids wing = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(wing, side, Point(0.13f, 0.42f, -0.06f), 0.5f, 0.3f, 0.06f, -0.12f, 18f, shell);
                wing.Prism(Point(side * 0.42f, 0.55f, -0.1f), 3, 0.04f, 0f, 0.14f, facet, Turn(-30f, 0f, -side * 40f));
            }
        }

        /// <summary>균열 파수병: a hunched spined walker with an arm-cannon. Slot 1, holds the ground.</summary>
        private static void RiftWarden(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember;
            Solids hull = b.Hull;

            Legs(b, 0.28f, 0.16f, 0.15f, charcoal, shell);
            hull.Taper(Point(0f, 0.26f, -0.02f), Pair(0.46f, 0.34f), Pair(0.58f, 0.4f), 0.38f, shell, Turn(12f, 0f, 0f));
            for (int k = 0; k < 3; k++)
                hull.Prism(Point(-0.13f + k * 0.13f, 0.6f, -0.1f), 4, 0.06f, 0f, 0.18f,
                           k == 1 ? facet : charcoal, Turn(-30f, 0f, (k - 1) * -15f));
            hull.Taper(Point(0f, 0.58f, 0.16f), Pair(0.2f, 0.2f), Pair(0.15f, 0.13f), 0.14f, charcoal);
            Slits(hull, Point(0f, 0.66f, 0.26f), 0.08f, 0.045f);
            hull.Ball(Point(0f, 0.48f, 0.24f), 0.05f, ember);
            hull.Taper(Point(-0.36f, 0.3f, 0.06f), Pair(0.12f, 0.12f), Pair(0.14f, 0.14f), 0.3f, shell);
            Strap(b, hull, Point(0f, 0.3f, 0f), Extent(0.48f, 0.06f, 0.36f));

            b.Arm.Origin = Point(0.34f, 0.58f, 0f);
            b.Arm.Box(Point(0.37f, 0.5f, 0f), Extent(0.15f, 0.2f, 0.15f), shell);
            b.Arm.Prism(Point(0.38f, 0.38f, -0.1f), 7, 0.11f, 0.13f, 0.46f, charcoal, Lying);
            b.Arm.Prism(Point(0.38f, 0.38f, 0.12f), 7, 0.14f, 0.14f, 0.05f, facet, Lying);
            b.Arm.Prism(Point(0.38f, 0.38f, 0.36f), 7, 0.085f, 0.085f, 0.03f, ember, Lying);
        }

        /// <summary>지옥불 군단장: a broad spined manta with an ember eye-ring and trailing tendrils. Airborne, weaponless.</summary>
        private static void Warlord(Blueprint b)
        {
            b.Gait = Gait.Fly;
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Pulse;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember;
            Solids hull = b.Hull;

            hull.Taper(Point(0f, 0.32f, -0.42f), Pair(0.46f, 0.24f), Pair(0.28f, 0.16f), 0.84f, shell, Lying);
            hull.Box(Point(0f, 0.45f, -0.06f), Extent(0.34f, 0.06f, 0.5f), facet, Turn(-6f, 0f, 0f));
            for (int k = 0; k < 4; k++)
            {
                float z = -0.3f + k * 0.17f;
                hull.Prism(Point(-0.11f, 0.47f, z), 4, 0.05f, 0f, 0.2f, charcoal, Turn(-35f, 0f, 12f));
                hull.Prism(Point(0.11f, 0.47f, z), 4, 0.05f, 0f, 0.2f, charcoal, Turn(-35f, 0f, -12f));
            }
            hull.Prism(Point(0f, 0.47f, 0.18f), 8, 0.12f, 0.12f, 0.05f, ember);
            hull.Prism(Point(0f, 0.5f, 0.18f), 8, 0.06f, 0.06f, 0.03f, charcoal);
            Slits(hull, Point(0f, 0.36f, 0.43f), 0.14f, 0.06f);
            for (int k = -1; k <= 1; k++)
                hull.Taper(Point(k * 0.12f, 0.26f, -0.38f), Pair(0.06f, 0.06f), Pair(0.01f, 0.01f), 0.42f, charcoal, Turn(-120f, 0f, k * 12f));
            Strap(b, hull, Point(0f, 0.32f, 0.26f), Extent(0.4f, 0.22f, 0.05f));

            for (int side = -1; side <= 1; side += 2)
            {
                Solids wing = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(wing, side, Point(0.2f, 0.32f, -0.02f), 0.62f, 0.6f, 0.14f, -0.25f, 8f, shell);
                wing.Prism(Point(side * 0.45f, 0.38f, 0.1f), 4, 0.04f, 0f, 0.16f, facet, Turn(-60f, 0f, 0f));
                wing.Prism(Point(side * 0.68f, 0.42f, -0.05f), 4, 0.035f, 0f, 0.14f, facet, Turn(-60f, 0f, 0f));
            }
        }

        /// <summary>화염 악마: a lean, hunched biped with a crest of shell blades swept back off its head and long claws. Slot 1.</summary>
        private static void FireDemon(Blueprint b)
        {
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember;
            Solids hull = b.Hull;

            Legs(b, 0.26f, 0.13f, 0.12f, charcoal, shell);
            hull.Taper(Point(0f, 0.24f, -0.04f), Pair(0.34f, 0.26f), Pair(0.44f, 0.3f), 0.32f, shell, Turn(20f, 0f, 0f));
            hull.Taper(Point(0f, 0.52f, 0.1f), Pair(0.2f, 0.2f), Pair(0.13f, 0.13f), 0.16f, charcoal, Turn(14f, 0f, 0f));
            for (int k = 0; k < 4; k++)
                hull.Prism(Point(0f, 0.66f - k * 0.07f, 0.1f - k * 0.12f), 4, 0.055f, 0f, 0.28f - k * 0.03f,
                           k % 2 == 0 ? facet : shell, Turn(-62f, 0f, 0f));
            hull.Prism(Point(-0.07f, 0.64f, 0.14f), 4, 0.035f, 0f, 0.2f, facet, Turn(-20f, 0f, 32f));
            Slits(hull, Point(0f, 0.61f, 0.21f), 0.08f, 0.045f);
            hull.Ball(Point(0f, 0.42f, 0.16f), 0.045f, ember);
            hull.Taper(Point(-0.24f, 0.5f, 0.06f), Pair(0.08f, 0.08f), Pair(0.05f, 0.05f), 0.3f, charcoal, Turn(155f, 0f, -12f));
            hull.Prism(Point(-0.26f, 0.22f, 0.2f), 4, 0.03f, 0f, 0.12f, facet, Turn(120f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.27f, -0.02f), Extent(0.36f, 0.05f, 0.28f));

            b.Arm.Origin = Point(0.24f, 0.52f, 0.06f);
            b.Arm.Taper(Point(0.24f, 0.5f, 0.06f), Pair(0.09f, 0.09f), Pair(0.06f, 0.06f), 0.3f, charcoal, Turn(155f, 0f, 12f));
            for (int k = -1; k <= 1; k++)
                b.Arm.Prism(Point(0.27f + k * 0.04f, 0.24f, 0.2f), 4, 0.025f, 0f, 0.14f, facet, Turn(120f, 0f, 0f));
        }

        /// <summary>균열 사냥개: a lean four-legged hound, long snout, uneven horns, a whip of a tail. Slot 2, the chaser.</summary>
        private static void RiftHound(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Lunge;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal, facet = BodyPalette.Hellfire.Facet;
            Solids hull = b.Hull;

            hull.Taper(Point(0f, 0.4f, -0.34f), Pair(0.22f, 0.22f), Pair(0.3f, 0.3f), 0.56f, shell, Lying);
            hull.Taper(Point(0f, 0.5f, 0.2f), Pair(0.22f, 0.2f), Pair(0.12f, 0.1f), 0.36f, charcoal, Turn(68f, 0f, 0f));
            for (int k = 0; k < 3; k++)
                hull.Prism(Point(0f, 0.53f, -0.24f + k * 0.16f), 4, 0.05f, 0f, 0.15f, k == 1 ? facet : charcoal, Turn(-35f, 0f, 0f));
            hull.Prism(Point(-0.07f, 0.6f, 0.24f), 4, 0.04f, 0f, 0.26f, facet, Turn(-62f, 0f, 22f));
            hull.Prism(Point(0.07f, 0.6f, 0.24f), 4, 0.035f, 0f, 0.14f, facet, Turn(-55f, 0f, -20f));
            Slits(hull, Point(0f, 0.6f, 0.36f), 0.09f, 0.04f);
            hull.Taper(Point(0f, 0.44f, -0.32f), Pair(0.07f, 0.07f), Pair(0.01f, 0.01f), 0.42f, charcoal, Turn(-125f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.46f, 0.18f), Extent(0.26f, 0.26f, 0.05f));

            FourLegs(b, 0.13f, 0.14f, -0.26f, (part, x, z) => CrawlerLeg(part, x, z, charcoal, facet, Pair(0.08f, 0.1f), Pair(0.1f, 0.12f), 0.4f));

            b.Arm.Origin = Point(0f, 0.44f, 0.3f);
            b.Arm.Taper(Point(0f, 0.42f, 0.28f), Pair(0.14f, 0.05f), Pair(0.07f, 0.03f), 0.24f, shell, Lying);
            b.Arm.Prism(Point(-0.04f, 0.44f, 0.46f), 4, 0.02f, 0f, 0.06f, BodyPalette.Hellfire.Ash);
            b.Arm.Prism(Point(0.04f, 0.44f, 0.46f), 4, 0.02f, 0f, 0.06f, BodyPalette.Hellfire.Ash);
        }

        /// <summary>잿불 새끼: a small two-legged imp, two stub horns, a cracked chest with one small light. Hatched, not bought.</summary>
        private static void EmberWhelp(Blueprint b)
        {
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal, facet = BodyPalette.Hellfire.Facet;
            Solids hull = b.Hull;

            Legs(b, 0.16f, 0.09f, 0.09f, charcoal, shell);
            hull.Taper(Point(0f, 0.15f, 0f), Pair(0.26f, 0.2f), Pair(0.3f, 0.22f), 0.2f, shell);
            hull.Box(Point(0f, 0.26f, 0.11f), Extent(0.03f, 0.12f, 0.02f), facet, Turn(0f, 0f, 20f));
            hull.Ball(Point(0f, 0.27f, 0.115f), 0.03f, BodyPalette.Hellfire.Ember);
            hull.Taper(Point(0f, 0.35f, 0.02f), Pair(0.2f, 0.18f), Pair(0.14f, 0.13f), 0.13f, charcoal);
            hull.Prism(Point(-0.06f, 0.47f, 0.02f), 4, 0.03f, 0f, 0.1f, facet, Turn(-15f, 0f, 30f));
            hull.Prism(Point(0.06f, 0.47f, 0.02f), 4, 0.03f, 0f, 0.1f, facet, Turn(-15f, 0f, -30f));
            Slits(hull, Point(0f, 0.42f, 0.1f), 0.07f, 0.035f);
            hull.Taper(Point(0f, 0.18f, -0.08f), Pair(0.05f, 0.05f), Pair(0.01f, 0.01f), 0.18f, charcoal, Turn(-130f, 0f, 0f));
            hull.Taper(Point(-0.17f, 0.32f, 0.02f), Pair(0.06f, 0.06f), Pair(0.05f, 0.05f), 0.16f, charcoal, Turn(160f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.17f, 0f), Extent(0.28f, 0.04f, 0.22f));

            b.Arm.Origin = Point(0.17f, 0.32f, 0.02f);
            b.Arm.Taper(Point(0.17f, 0.32f, 0.02f), Pair(0.06f, 0.06f), Pair(0.05f, 0.05f), 0.16f, charcoal, Turn(160f, 0f, 0f));
            b.Arm.Prism(Point(0.17f, 0.15f, 0.08f), 4, 0.025f, 0f, 0.08f, facet, Turn(130f, 0f, 0f));
        }

        /// <summary>용암 투석 악마: a low four-legged demon under a great curved launching shell, a lava pool in its back. Slot 2, the siege.</summary>
        private static void LavaCatapult(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Recoil;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember;
            Solids hull = b.Hull;

            hull.Taper(Point(0f, 0.26f, -0.34f), Pair(0.5f, 0.34f), Pair(0.42f, 0.3f), 0.62f, shell, Lying);
            hull.Prism(Point(0f, 0.42f, -0.12f), 6, 0.17f, 0.15f, 0.06f, charcoal);
            hull.Prism(Point(0f, 0.47f, -0.12f), 6, 0.12f, 0.12f, 0.02f, ember);
            hull.Box(Point(-0.21f, 0.44f, -0.12f), Extent(0.12f, 0.05f, 0.5f), facet, Turn(0f, 0f, 18f));
            hull.Box(Point(0.21f, 0.44f, -0.12f), Extent(0.12f, 0.05f, 0.5f), facet, Turn(0f, 0f, -18f));
            hull.Taper(Point(0f, 0.24f, 0.24f), Pair(0.22f, 0.18f), Pair(0.13f, 0.1f), 0.2f, charcoal, Lying);
            hull.Prism(Point(-0.07f, 0.32f, 0.36f), 4, 0.035f, 0f, 0.12f, facet, Turn(-50f, 0f, 25f));
            hull.Prism(Point(0.07f, 0.32f, 0.36f), 4, 0.035f, 0f, 0.12f, facet, Turn(-50f, 0f, -25f));
            Slits(hull, Point(0f, 0.28f, 0.43f), 0.09f, 0.04f);
            Strap(b, hull, Point(0f, 0.27f, 0.24f), Extent(0.46f, 0.3f, 0.05f));

            FourLegs(b, 0.22f, 0.14f, -0.28f, (part, x, z) => CrawlerLeg(part, x, z, charcoal, facet));

            b.Arm.Origin = Point(0f, 0.44f, -0.34f);
            Vector3 position = Point(0f, 0.42f, -0.34f);
            float pitch = -12f, width = 0.26f;
            for (int i = 0; i < 4; i++)
            {
                Quaternion rotation = Turn(pitch, 0f, 0f);
                b.Arm.Taper(position, Pair(width, width * 0.6f), Pair(width * 0.86f, width * 0.52f), 0.22f, i % 2 == 0 ? shell : facet, rotation);
                position += rotation * Point(0f, 0.2f, 0f);
                pitch += 34f;
                width *= 0.86f;
            }
            b.Arm.Prism(position + Point(0f, -0.04f, 0f), 6, 0.08f, 0.13f, 0.08f, charcoal);
            b.Arm.Ball(position + Point(0f, 0.04f, 0f), 0.07f, ember);
        }

        /// <summary>재날개 악마: a slender demon on two long angular wings of ash membrane, a pointed head, claws hanging. Airborne, slot 3.</summary>
        private static void AshWingDemon(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Recoil;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ash = BodyPalette.Hellfire.Ash;
            Solids hull = b.Hull;

            hull.Taper(Point(0f, 0.38f, -0.28f), Pair(0.12f, 0.12f), Pair(0.2f, 0.18f), 0.42f, shell, Lying);
            hull.Taper(Point(0f, 0.42f, 0.12f), Pair(0.14f, 0.13f), Pair(0.02f, 0.03f), 0.3f, charcoal, Turn(78f, 0f, 0f));
            hull.Prism(Point(-0.05f, 0.47f, 0.14f), 4, 0.03f, 0f, 0.16f, facet, Turn(-70f, 0f, 20f));
            hull.Prism(Point(0.05f, 0.47f, 0.14f), 4, 0.03f, 0f, 0.16f, facet, Turn(-70f, 0f, -20f));
            Slits(hull, Point(0f, 0.46f, 0.24f), 0.07f, 0.035f);
            hull.Ball(Point(0f, 0.34f, 0.08f), 0.035f, BodyPalette.Hellfire.Ember);
            hull.Taper(Point(0f, 0.37f, -0.26f), Pair(0.06f, 0.06f), Pair(0.01f, 0.01f), 0.5f, charcoal, Turn(-96f, 0f, 0f));
            hull.Prism(Point(0f, 0.38f, -0.74f), 3, 0.06f, 0f, 0.12f, facet, Turn(-90f, 0f, 0f));
            hull.Prism(Point(-0.06f, 0.3f, 0f), 4, 0.03f, 0f, 0.16f, charcoal, Turn(160f, 0f, 0f));
            hull.Prism(Point(0.06f, 0.3f, 0f), 4, 0.03f, 0f, 0.16f, charcoal, Turn(160f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.38f, -0.02f), Extent(0.22f, 0.2f, 0.05f));

            for (int side = -1; side <= 1; side += 2)
            {
                Solids wing = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(wing, side, Point(0.08f, 0.44f, 0f), 0.8f, 0.44f, 0.08f, -0.34f, 20f, ash);
                wing.Prism(Point(side * 0.36f, 0.56f, 0.12f), 4, 0.035f, 0f, 0.14f, facet, Turn(-60f, 0f, 0f));
                wing.Prism(Point(side * 0.66f, 0.66f, 0.02f), 4, 0.035f, 0f, 0.16f, facet, Turn(-60f, 0f, 0f));
            }
        }

        /// <summary>산란 악마: a squat, wide-bellied demon on short thick legs, a cluster of egg sacs on its back, a glowing crack under the belly.</summary>
        private static void BroodDemon(Blueprint b)
        {
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember, ash = BodyPalette.Hellfire.Ash;
            Solids hull = b.Hull;

            Legs(b, 0.2f, 0.2f, 0.17f, charcoal, shell);
            hull.Taper(Point(0f, 0.16f, 0f), Pair(0.6f, 0.46f), Pair(0.68f, 0.5f), 0.36f, shell);
            hull.Box(Point(0f, 0.24f, 0.24f), Extent(0.32f, 0.04f, 0.04f), ember, Turn(0f, 0f, -6f));
            hull.Taper(Point(0f, 0.52f, -0.04f), Pair(0.66f, 0.48f), Pair(0.4f, 0.3f), 0.2f, charcoal);
            hull.Taper(Point(0f, 0.4f, 0.24f), Pair(0.24f, 0.18f), Pair(0.18f, 0.12f), 0.16f, charcoal, Turn(22f, 0f, 0f));
            hull.Prism(Point(-0.08f, 0.54f, 0.3f), 4, 0.045f, 0f, 0.2f, facet, Turn(-20f, 0f, 38f));
            hull.Prism(Point(0.08f, 0.54f, 0.3f), 4, 0.04f, 0f, 0.13f, facet, Turn(-20f, 0f, -30f));
            Slits(hull, Point(0f, 0.49f, 0.36f), 0.09f, 0.045f);
            Vector3[] eggs = { Point(-0.18f, 0.78f, -0.14f), Point(0.17f, 0.76f, -0.2f), Point(0f, 0.84f, 0.02f), Point(0.02f, 0.72f, -0.36f), Point(-0.24f, 0.66f, -0.36f) };
            for (int i = 0; i < eggs.Length; i++)
            {
                hull.Ball(eggs[i], Extent(0.14f, 0.12f, 0.14f) * (i == 2 ? 1.15f : 1f), i % 2 == 0 ? ash : shell, Turn(0f, i * 40f, 0f), 1);
                hull.Box(eggs[i] + Point(0f, 0.04f, i == 2 ? 0.14f : 0.12f), Extent(0.03f, 0.1f, 0.02f), i == 2 ? ember : facet, Turn(0f, 0f, 15f));
            }
            hull.Taper(Point(-0.38f, 0.44f, 0.06f), Pair(0.13f, 0.13f), Pair(0.15f, 0.15f), 0.24f, shell, Turn(170f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.2f, 0f), Extent(0.64f, 0.06f, 0.5f));

            b.Arm.Origin = Point(0.38f, 0.46f, 0.04f);
            b.Arm.Taper(Point(0.38f, 0.46f, 0.06f), Pair(0.13f, 0.13f), Pair(0.15f, 0.15f), 0.24f, shell, Turn(170f, 0f, 0f));
            b.Arm.Box(Point(0.38f, 0.2f, 0.1f), Extent(0.17f, 0.12f, 0.17f), charcoal);
        }

        /// <summary>운석 술사: a tall thin figure in a triangular shell cape, one arm raised, a light in its chest. Weaponless caster.</summary>
        private static void MeteorInvoker(Blueprint b)
        {
            b.Strike = Strike.Pulse;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal, facet = BodyPalette.Hellfire.Facet;
            Solids hull = b.Hull;

            Legs(b, 0.3f, 0.08f, 0.09f, charcoal, shell);
            hull.Taper(Point(0f, 0.1f, -0.06f), Pair(0.56f, 0.34f), Pair(0.18f, 0.14f), 0.66f, shell);
            hull.Prism(Point(-0.27f, 0.1f, -0.04f), 4, 0.05f, 0f, 0.24f, facet, Turn(0f, 0f, 20f));
            hull.Prism(Point(0.27f, 0.1f, -0.04f), 4, 0.05f, 0f, 0.24f, facet, Turn(0f, 0f, -20f));
            hull.Taper(Point(0f, 0.36f, 0.04f), Pair(0.2f, 0.14f), Pair(0.26f, 0.16f), 0.36f, charcoal);
            hull.Ball(Point(0f, 0.58f, 0.12f), 0.045f, BodyPalette.Hellfire.Ember);
            hull.Prism(Point(-0.14f, 0.72f, 0f), 4, 0.05f, 0f, 0.18f, facet, Turn(0f, 0f, 35f));
            hull.Prism(Point(0.14f, 0.72f, 0f), 4, 0.05f, 0f, 0.14f, facet, Turn(0f, 0f, -35f));
            hull.Taper(Point(0f, 0.74f, 0.02f), Pair(0.14f, 0.14f), Pair(0.09f, 0.09f), 0.15f, charcoal);
            hull.Prism(Point(0.02f, 0.86f, 0f), 4, 0.05f, 0f, 0.36f, facet, Turn(-22f, 0f, -12f));
            hull.Prism(Point(-0.04f, 0.86f, 0f), 4, 0.03f, 0f, 0.14f, shell, Turn(-10f, 0f, 30f));
            Slits(hull, Point(0f, 0.81f, 0.09f), 0.06f, 0.035f);
            hull.Taper(Point(-0.16f, 0.66f, 0.02f), Pair(0.06f, 0.06f), Pair(0.04f, 0.04f), 0.3f, charcoal, Turn(170f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.68f, 0.03f), Extent(0.26f, 0.05f, 0.18f));

            b.Arm.Origin = Point(0.16f, 0.66f, 0.02f);
            b.Arm.Taper(Point(0.16f, 0.64f, 0.02f), Pair(0.07f, 0.07f), Pair(0.05f, 0.05f), 0.34f, charcoal, Turn(0f, 0f, -18f));
            for (int k = -1; k <= 1; k++)
                b.Arm.Prism(Point(0.27f + k * 0.03f, 0.95f, 0.02f), 4, 0.02f, 0f, 0.09f, facet, Turn(0f, 0f, -18f - k * 20f));
            b.Arm.Lump(Point(0.32f, 1.1f, 0.03f), Extent(0.08f, 0.07f, 0.08f), facet, Turn(20f, 30f, 0f));
        }

        // ---- 물 슬라임 ----

        private static bool WaterSlimeUnit(Blueprint b, Role role, int slot)
        {
            switch (role)
            {
                case Role.Worker when slot == 0: Droplet(b); return true;
                case Role.Melee when slot == 0: BubbleSpirit(b); return true;
                case Role.Melee when slot == 1: WaterScreenSlime(b); return true;
                case Role.Melee when slot == 2: RiptideSlime(b); return true;
                case Role.Ranged when slot == 0: AquaArcher(b); return true;
                case Role.Ranged when slot == 1: TideHerald(b); return true;
                case Role.Ranged when slot == 2: SpitSlime(b); return true;
                case Role.Ranged when slot == 3: CannonSlime(b); return true;
                case Role.Signature when slot == 0: CloudDragon(b); return true;
                case Role.Signature when slot == 1: RainCloud(b); return true;
                case Role.Signature when slot == 2: GeyserMage(b); return true;
                case Role.Signature when slot == 3: SpringCarrier(b); return true;
                default: return false;
            }
        }

        /// <summary>물방울 생존자: a hopping droplet dome carrying a bubble.</summary>
        private static void Droplet(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;

            hull.Dome(Vector3.zero, Extent(0.38f, 0.5f, 0.36f), 10, 3, BodyPalette.WaterSlimes.Pale);
            hull.Prism(Point(0f, 0.44f, -0.02f), 8, 0.13f, 0f, 0.26f, BodyPalette.WaterSlimes.Pale, Turn(-18f, 0f, 0f));
            hull.Ball(Point(-0.14f, 0.32f, 0.25f), Extent(0.06f, 0.1f, 0.05f), BodyPalette.WaterSlimes.Foam);
            Eyes(hull, Point(0f, 0.27f, 0.3f), 0.17f, 0.065f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.39f, 0.06f, 0.95f);

            b.Arm.Origin = Point(0.3f, 0.22f, 0.05f);
            b.Arm.Ball(Point(0.38f, 0.2f, 0.14f), 0.1f, BodyPalette.WaterSlimes.Foam);
        }

        /// <summary>거품 정령: a blue dome clustered with bubbles, bashing with a bubble shield.</summary>
        private static void BubbleSpirit(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;

            hull.Dome(Vector3.zero, Extent(0.4f, 0.56f, 0.38f), 10, 3, BodyPalette.WaterSlimes.Sky);
            hull.Ball(Point(-0.28f, 0.48f, -0.12f), 0.12f, BodyPalette.WaterSlimes.Foam);
            hull.Ball(Point(0.26f, 0.6f, -0.14f), 0.1f, BodyPalette.WaterSlimes.Pale);
            hull.Ball(Point(0f, 0.66f, -0.18f), 0.13f, BodyPalette.WaterSlimes.Pale);
            hull.Ball(Point(-0.14f, 0.8f, -0.1f), 0.07f, BodyPalette.WaterSlimes.Foam);
            hull.Ball(Point(-0.15f, 0.36f, 0.27f), Extent(0.06f, 0.09f, 0.05f), BodyPalette.WaterSlimes.Foam);
            Eyes(hull, Point(0f, 0.3f, 0.33f), 0.18f, 0.065f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.41f, 0.06f, 0.95f);

            b.Arm.Origin = Point(0.22f, 0.32f, 0.15f);
            b.Arm.Ball(Point(0.14f, 0.34f, 0.44f), Extent(0.25f, 0.25f, 0.09f), BodyPalette.WaterSlimes.Pale);
            b.Arm.Ball(Point(0.08f, 0.4f, 0.5f), Extent(0.08f, 0.08f, 0.03f), BodyPalette.WaterSlimes.Foam);
        }

        /// <summary>물결 궁수: a droplet-headed archer on stubby legs, bow held forward. Slot 0.</summary>
        private static void AquaArcher(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color deep = BodyPalette.WaterSlimes.Deep, foam = BodyPalette.WaterSlimes.Foam;

            Legs(b, 0.2f, 0.09f, 0.1f, BodyPalette.WaterSlimes.Blue, deep);
            hull.Taper(Point(0f, 0.18f, 0f), Pair(0.3f, 0.26f), Pair(0.24f, 0.2f), 0.26f, BodyPalette.WaterSlimes.Sky);
            hull.Ball(Point(0f, 0.6f, 0.02f), Extent(0.25f, 0.24f, 0.23f), BodyPalette.WaterSlimes.Pale);
            hull.Prism(Point(0f, 0.76f, -0.06f), 8, 0.13f, 0f, 0.3f, BodyPalette.WaterSlimes.Pale, Turn(-35f, 0f, 0f));
            Eyes(hull, Point(0f, 0.6f, 0.235f), 0.15f, 0.06f);
            hull.Ball(Point(0f, 0.32f, 0.12f), Extent(0.04f, 0.06f, 0.02f), deep);
            Band(b, hull, Point(0f, 0.4f, 0f), 8, 0.15f, 0.05f);

            b.Arm.Origin = Point(0.17f, 0.38f, 0f);
            b.Arm.Box(Point(0.2f, 0.38f, 0.27f), Extent(0.03f, 0.22f, 0.04f), deep);
            b.Arm.Box(Point(0.2f, 0.54f, 0.22f), Extent(0.03f, 0.15f, 0.04f), deep, Turn(-35f, 0f, 0f));
            b.Arm.Box(Point(0.2f, 0.22f, 0.22f), Extent(0.03f, 0.15f, 0.04f), deep, Turn(35f, 0f, 0f));
            b.Arm.Box(Point(0.2f, 0.38f, 0.165f), Extent(0.012f, 0.4f, 0.012f), foam);
            b.Arm.Box(Point(0.2f, 0.38f, 0.2f), Extent(0.02f, 0.02f, 0.3f), foam);
        }

        /// <summary>해일 인도자: a tall bell of water with two raised wave arms. Slot 1.</summary>
        private static void TideHerald(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color pale = BodyPalette.WaterSlimes.Pale, foam = BodyPalette.WaterSlimes.Foam;

            hull.Dome(Vector3.zero, Extent(0.38f, 0.72f, 0.36f), 10, 4, BodyPalette.WaterSlimes.Sky);
            hull.Ball(Point(-0.14f, 0.5f, 0.24f), Extent(0.06f, 0.1f, 0.05f), foam);
            hull.Prism(Point(-0.3f, 0.42f, 0f), 6, 0.08f, 0.05f, 0.36f, pale, Turn(0f, 0f, 28f));
            hull.Ball(Point(-0.48f, 0.78f, 0.02f), Extent(0.1f, 0.08f, 0.1f), foam);
            Eyes(hull, Point(0f, 0.42f, 0.3f), 0.16f, 0.06f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.39f, 0.06f, 0.95f);

            b.Arm.Origin = Point(0.3f, 0.42f, 0f);
            b.Arm.Prism(Point(0.3f, 0.42f, 0f), 6, 0.08f, 0.05f, 0.36f, pale, Turn(0f, 0f, -28f));
            b.Arm.Ball(Point(0.48f, 0.78f, 0.02f), Extent(0.1f, 0.08f, 0.1f), foam);
        }

        /// <summary>구름 용: a cloud body with a small dragon head and blue wings. The faction's signature.</summary>
        private static void CloudDragon(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color cloud = BodyPalette.WaterSlimes.Cloud, foam = BodyPalette.WaterSlimes.Foam, sky = BodyPalette.WaterSlimes.Sky;

            hull.Ball(Point(0f, 0.42f, -0.05f), Extent(0.32f, 0.24f, 0.36f), cloud);
            hull.Ball(Point(-0.22f, 0.36f, -0.2f), 0.2f, cloud);
            hull.Ball(Point(0.22f, 0.34f, -0.22f), 0.19f, foam);
            hull.Ball(Point(0f, 0.32f, -0.44f), 0.16f, cloud);
            hull.Ball(Point(0.06f, 0.34f, -0.6f), 0.1f, foam);
            hull.Ball(Point(0f, 0.64f, 0.28f), Extent(0.2f, 0.18f, 0.2f), foam);
            hull.Ball(Point(0f, 0.59f, 0.44f), Extent(0.12f, 0.09f, 0.1f), foam);
            hull.Prism(Point(0f, 0.79f, 0.25f), 5, 0.04f, 0f, 0.16f, sky, Turn(-25f, 0f, 0f));
            Eyes(hull, Point(0f, 0.67f, 0.45f), 0.17f, 0.05f);
            Band(b, hull, Point(0f, 0.46f, 0.22f), 8, 0.15f, 0.05f);

            Wing(b.LimbLeft, -1f, Point(0.2f, 0.52f, -0.08f), 0.34f, 0.26f, 0.1f, -0.05f, 20f, sky);
            Wing(b.LimbRight, 1f, Point(0.2f, 0.52f, -0.08f), 0.34f, 0.26f, 0.1f, -0.05f, 20f, sky);
        }

        /// <summary>물막이 슬라임: a wide, low dome with a wall of water raised on its right shoulder. Slot 1, screens the ranks behind.</summary>
        private static void WaterScreenSlime(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;
            Color pale = BodyPalette.WaterSlimes.Pale, foam = BodyPalette.WaterSlimes.Foam;

            hull.Dome(Vector3.zero, Extent(0.5f, 0.42f, 0.42f), 10, 3, BodyPalette.WaterSlimes.Sky);
            hull.Ball(Point(-0.2f, 0.28f, 0.3f), Extent(0.06f, 0.09f, 0.05f), foam);
            Eyes(hull, Point(-0.06f, 0.24f, 0.37f), 0.17f, 0.06f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.51f, 0.06f, 0.84f);

            b.Arm.Origin = Point(0.3f, 0.3f, 0.05f);
            b.Arm.Taper(Point(0.34f, 0.12f, 0.06f), Pair(0.16f, 0.56f), Pair(0.1f, 0.46f), 0.62f, pale, Turn(0f, 0f, -6f));
            b.Arm.Taper(Point(0.31f, 0.72f, 0.06f), Pair(0.1f, 0.46f), Pair(0.07f, 0.36f), 0.22f, pale, Turn(0f, 0f, 38f));
            b.Arm.Ball(Point(0.2f, 0.86f, 0.06f), Extent(0.09f, 0.08f, 0.2f), foam);
            b.Arm.Ball(Point(0.42f, 0.44f, 0.2f), Extent(0.03f, 0.12f, 0.03f), foam);
        }

        /// <summary>급류 슬라임: a low, long slime streaming a wave tail behind it, a fin raised on its back. Slot 2, the flanker.</summary>
        private static void RiptideSlime(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;
            Color pale = BodyPalette.WaterSlimes.Pale, foam = BodyPalette.WaterSlimes.Foam, blue = BodyPalette.WaterSlimes.Blue;

            hull.Ball(Point(0f, 0.22f, 0.06f), Extent(0.26f, 0.21f, 0.36f), blue);
            hull.Ball(Point(0f, 0.2f, -0.3f), Extent(0.18f, 0.13f, 0.18f), BodyPalette.WaterSlimes.Sky);
            hull.Ball(Point(0.05f, 0.22f, -0.5f), Extent(0.12f, 0.1f, 0.14f), pale);
            hull.Prism(Point(0.08f, 0.26f, -0.58f), 5, 0.08f, 0f, 0.26f, pale, Turn(-60f, 0f, -15f));
            hull.Prism(Point(0f, 0.36f, -0.02f), 4, 0.08f, 0f, 0.3f, pale, Turn(-58f, 0f, 0f));
            hull.Ball(Point(-0.12f, 0.32f, 0.3f), Extent(0.05f, 0.06f, 0.04f), foam);
            Eyes(hull, Point(0f, 0.3f, 0.38f), 0.16f, 0.055f);
            Band(b, hull, Point(0f, 0.08f, 0.06f), 10, 0.24f, 0.05f, 1.4f);

            b.Arm.Origin = Point(0f, 0.12f, 0.36f);
            b.Arm.Ball(Point(0f, 0.08f, 0.44f), Extent(0.22f, 0.06f, 0.08f), foam);
        }

        /// <summary>침 뱉는 슬라임: a round slime with a short spout of a mouth pushed forward, a stream of droplets in front of it. Slot 2.</summary>
        private static void SpitSlime(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color foam = BodyPalette.WaterSlimes.Foam, deep = BodyPalette.WaterSlimes.Deep;

            hull.Dome(Vector3.zero, Extent(0.38f, 0.48f, 0.36f), 10, 3, BodyPalette.WaterSlimes.Blue);
            hull.Prism(Point(0f, 0.24f, 0.24f), 8, 0.1f, 0.13f, 0.2f, BodyPalette.WaterSlimes.Sky, Lying);
            hull.Prism(Point(0f, 0.24f, 0.44f), 8, 0.09f, 0.09f, 0.01f, deep, Lying);
            hull.Ball(Point(-0.14f, 0.36f, 0.26f), Extent(0.05f, 0.08f, 0.04f), foam);
            hull.Ball(Point(-0.1f, 0.62f, -0.04f), 0.05f, foam);
            hull.Ball(Point(0.04f, 0.68f, 0.02f), 0.035f, BodyPalette.WaterSlimes.Pale);
            Eyes(hull, Point(0f, 0.42f, 0.29f), 0.2f, 0.065f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.39f, 0.06f, 0.95f);

            b.Arm.Origin = Point(0f, 0.24f, 0.44f);
            b.Arm.Ball(Point(0f, 0.25f, 0.54f), Extent(0.07f, 0.06f, 0.06f), BodyPalette.WaterSlimes.Pale);
            b.Arm.Ball(Point(0.03f, 0.27f, 0.66f), 0.05f, foam);
            b.Arm.Ball(Point(-0.02f, 0.29f, 0.76f), 0.035f, foam);
        }

        /// <summary>물대포 슬라임: a big round slime with a thick water column pushed out of its body, no barrel. Slot 3, the siege.</summary>
        private static void CannonSlime(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color pale = BodyPalette.WaterSlimes.Pale, foam = BodyPalette.WaterSlimes.Foam;

            hull.Dome(Vector3.zero, Extent(0.46f, 0.6f, 0.44f), 10, 3, BodyPalette.WaterSlimes.Sky);
            hull.Ball(Point(-0.22f, 0.44f, 0.26f), Extent(0.06f, 0.1f, 0.05f), foam);
            Eyes(hull, Point(-0.14f, 0.42f, 0.36f), 0.15f, 0.06f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.47f, 0.06f, 0.95f);

            b.Arm.Origin = Point(0.12f, 0.3f, 0.2f);
            b.Arm.Prism(Point(0.12f, 0.28f, 0.18f), 8, 0.15f, 0.18f, 0.4f, pale, Lying);
            b.Arm.Prism(Point(0.12f, 0.28f, 0.58f), 8, 0.2f, 0.2f, 0.06f, foam, Lying);
            b.Arm.Ball(Point(0.12f, 0.46f, 0.6f), 0.06f, foam);
            b.Arm.Ball(Point(0.28f, 0.36f, 0.62f), 0.045f, foam);
            b.Arm.Ball(Point(-0.04f, 0.38f, 0.62f), 0.045f, BodyPalette.WaterSlimes.Blue);
        }

        /// <summary>비구름: a flat, wide rain cloud in light blues with eyes, drops hanging under it, two puffs beating at its sides. Airborne, weaponless.</summary>
        private static void RainCloud(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color sky = BodyPalette.WaterSlimes.Sky, blue = BodyPalette.WaterSlimes.Blue, deep = BodyPalette.WaterSlimes.Deep;

            // Flat, wide puffs in light values over a flat blue underside: a cloud, not a slime.
            hull.Ball(Point(0f, 0.36f, 0f), Extent(0.5f, 0.1f, 0.32f), blue);
            hull.Ball(Point(0f, 0.46f, 0f), Extent(0.46f, 0.14f, 0.3f), BodyPalette.WaterSlimes.Pale);
            hull.Ball(Point(-0.3f, 0.52f, 0.02f), Extent(0.2f, 0.13f, 0.2f), BodyPalette.WaterSlimes.Foam);
            hull.Ball(Point(-0.04f, 0.58f, -0.04f), Extent(0.24f, 0.15f, 0.22f), BodyPalette.WaterSlimes.Cloud);
            hull.Ball(Point(0.26f, 0.54f, 0.04f), Extent(0.2f, 0.12f, 0.2f), BodyPalette.WaterSlimes.Foam);
            hull.Ball(Point(0.44f, 0.44f, -0.02f), Extent(0.14f, 0.09f, 0.14f), sky);
            hull.Ball(Point(-0.48f, 0.42f, 0f), Extent(0.12f, 0.08f, 0.12f), sky);
            for (int i = 0; i < 6; i++)
            {
                float x = -0.3f + i * 0.12f, z = i % 2 == 0 ? 0.08f : -0.1f;
                hull.Prism(Point(x, 0.1f + (i % 2) * 0.06f, z), 6, 0.025f, 0f, 0.12f, deep, Turn(180f, 0f, 0f));
                hull.Ball(Point(x, 0.08f + (i % 2) * 0.06f, z), 0.03f, deep);
            }
            Eyes(hull, Point(0f, 0.42f, 0.3f), 0.16f, 0.05f);
            Band(b, hull, Point(0f, 0.3f, 0f), 10, 0.36f, 0.04f, 0.7f);

            for (int side = -1; side <= 1; side += 2)
            {
                Solids puff = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(puff, side, Point(0.46f, 0.42f, 0f), 0.18f, 0.22f, 0.14f, -0.04f, 12f, BodyPalette.WaterSlimes.Pale);
            }
        }

        /// <summary>간헐천 술사: a tall standing slime with a round water spout rising off its head and bending right. Weaponless caster.</summary>
        private static void GeyserMage(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color foam = BodyPalette.WaterSlimes.Foam;

            hull.Dome(Vector3.zero, Extent(0.36f, 0.6f, 0.34f), 10, 4, BodyPalette.WaterSlimes.Pale);
            hull.Prism(Point(0f, 0.02f, 0f), 10, 0.44f, 0.42f, 0.05f, BodyPalette.WaterSlimes.Sky, Quaternion.identity, 0.95f);
            hull.Ball(Point(-0.14f, 0.36f, 0.24f), Extent(0.05f, 0.09f, 0.04f), foam);
            Eyes(hull, Point(0f, 0.3f, 0.31f), 0.14f, 0.055f);
            Band(b, hull, Point(0f, 0.08f, 0f), 10, 0.355f, 0.05f, 0.95f);

            b.Arm.Origin = Point(0f, 0.56f, 0f);
            Vector3 position = Point(0f, 0.52f, -0.02f);
            float roll = 0f, radius = 0.1f;
            for (int i = 0; i < 5; i++)
            {
                Quaternion rotation = Turn(0f, 0f, roll);
                b.Arm.Prism(position, 8, radius, radius * 0.92f, 0.16f, i % 2 == 0 ? BodyPalette.WaterSlimes.Sky : BodyPalette.WaterSlimes.Blue, rotation);
                position += rotation * Point(0f, 0.15f, 0f);
                roll -= 24f;
                radius *= 0.92f;
            }
            b.Arm.Ball(position, 0.1f, foam);
            b.Arm.Ball(position + Point(0.08f, -0.12f, 0f), 0.04f, foam);
        }

        /// <summary>샘물 운반자: a low, wide slime with a hollow in its back holding a pool of spring water. Weaponless.</summary>
        private static void SpringCarrier(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color pale = BodyPalette.WaterSlimes.Pale, foam = BodyPalette.WaterSlimes.Foam;

            hull.Dome(Vector3.zero, Extent(0.5f, 0.32f, 0.44f), 10, 3, BodyPalette.WaterSlimes.Sky);
            for (int i = 0; i < 10; i++)
            {
                float yaw = i * 36f;
                hull.Ball(Around(yaw) * 0.3f + Point(0f, 0.3f, -0.04f), Extent(0.1f, 0.08f, 0.1f), i % 2 == 0 ? pale : foam);
            }
            hull.Prism(Point(0f, 0.3f, -0.04f), 10, 0.25f, 0.25f, 0.04f, BodyPalette.WaterSlimes.Deep);
            hull.Ball(Point(-0.06f, 0.35f, -0.08f), Extent(0.06f, 0.02f, 0.04f), foam);
            Eyes(hull, Point(0f, 0.2f, 0.42f), 0.18f, 0.055f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.51f, 0.06f, 0.88f);
        }

        // ---- 돌 골렘 부족 ----

        private static bool RockGolemUnit(Blueprint b, Role role, int slot)
        {
            switch (role)
            {
                case Role.Worker when slot == 0: MiniRock(b); return true;
                case Role.Melee when slot == 0: MossGolem(b); return true;
                case Role.Melee when slot == 1: RollingRock(b); return true;
                case Role.Melee when slot == 2: WallGolem(b); return true;
                case Role.Melee when slot == 3: AwakenedStone(b); return true;
                case Role.Ranged when slot == 0: RockMage(b); return true;
                case Role.Ranged when slot == 1: PebbleSlinger(b); return true;
                case Role.Ranged when slot == 2: FloatingSentinel(b); return true;
                case Role.Ranged when slot == 3: SandShaper(b); return true;
                case Role.Signature when slot == 0: AncientGolem(b); return true;
                case Role.Signature when slot == 1: MossSage(b); return true;
                case Role.Signature when slot == 2: SporeCluster(b); return true;
                default: return false;
            }
        }

        /// <summary>
        /// 꼬마돌: a stone block with eyes on two block legs. Also the map's
        /// neutral 꼬마돌, untinted here and tinted by UiStyle.Neutral there.
        /// </summary>
        private static void MiniRock(Blueprint b)
        {
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan;

            BlockLegs(b, 0.17f, 0.12f, Extent(0.13f, 0.17f, 0.15f), BodyPalette.RockGolems.Stone);
            hull.Taper(Point(0f, 0.14f, 0f), Pair(0.42f, 0.34f), Pair(0.5f, 0.42f), 0.15f, BodyPalette.RockGolems.StoneShade);
            hull.Taper(Point(0f, 0.3f, 0f), Pair(0.56f, 0.46f), Pair(0.6f, 0.5f), 0.48f, tan);
            hull.Taper(Point(0f, 0.78f, 0f), Pair(0.6f, 0.5f), Pair(0.46f, 0.36f), 0.12f, tan);
            Eyes(hull, Point(0f, 0.6f, 0.255f), 0.18f, 0.065f);
            hull.Dome(Point(-0.06f, 0.9f, -0.02f), Extent(0.2f, 0.06f, 0.15f), 7, 2, BodyPalette.RockGolems.Moss);
            Strap(b, hull, Point(0f, 0.3f, 0f), Extent(0.58f, 0.05f, 0.48f));

            b.Arm.Origin = Point(0.31f, 0.5f, 0f);
            b.Arm.Box(Point(0.34f, 0.38f, 0.02f), Extent(0.12f, 0.22f, 0.13f), BodyPalette.RockGolems.Stone);
        }

        /// <summary>이끼바위 골렘: stacked blocks in a wide stance, small head sunk between mossy shoulders.</summary>
        private static void MossGolem(Blueprint b)
        {
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone, moss = BodyPalette.RockGolems.Moss;

            BlockLegs(b, 0.3f, 0.18f, Extent(0.19f, 0.3f, 0.21f), stone);
            hull.Box(Point(0f, 0.33f, 0f), Extent(0.42f, 0.12f, 0.3f), BodyPalette.RockGolems.StoneShade);
            hull.Taper(Point(0f, 0.38f, 0f), Pair(0.52f, 0.36f), Pair(0.7f, 0.42f), 0.36f, tan);
            hull.Box(Point(0f, 0.8f, 0.06f), Extent(0.2f, 0.16f, 0.2f), tan);
            Eyes(hull, Point(0f, 0.82f, 0.165f), 0.08f, 0.035f);
            hull.Lump(Point(-0.38f, 0.72f, 0f), Extent(0.16f, 0.14f, 0.16f), stone, Turn(0f, 25f, 0f));
            hull.Lump(Point(0.38f, 0.72f, 0f), Extent(0.16f, 0.14f, 0.16f), stone, Turn(0f, -25f, 0f));
            hull.Dome(Point(-0.38f, 0.8f, 0f), Extent(0.15f, 0.06f, 0.15f), 7, 2, moss);
            hull.Dome(Point(0.38f, 0.8f, 0f), Extent(0.15f, 0.06f, 0.15f), 7, 2, moss);
            hull.Dome(Point(0f, 0.88f, 0.04f), Extent(0.1f, 0.04f, 0.1f), 6, 1, moss);
            hull.Box(Point(-0.42f, 0.45f, 0.02f), Extent(0.16f, 0.3f, 0.18f), stone);
            hull.Box(Point(-0.42f, 0.25f, 0.04f), Extent(0.2f, 0.16f, 0.2f), tan);
            Strap(b, hull, Point(0f, 0.4f, 0f), Extent(0.56f, 0.05f, 0.38f));

            b.Arm.Origin = Point(0.42f, 0.66f, 0f);
            b.Arm.Box(Point(0.42f, 0.48f, 0.02f), Extent(0.16f, 0.3f, 0.18f), stone);
            b.Arm.Box(Point(0.42f, 0.28f, 0.04f), Extent(0.22f, 0.18f, 0.22f), tan);
            b.Arm.Dome(Point(0.42f, 0.63f, 0.02f), Extent(0.1f, 0.04f, 0.1f), 6, 1, moss);
        }

        /// <summary>바위 술사: a hooded stone figure, eyes in the hood's shade, a stone staff tipped with a rune crystal.</summary>
        private static void RockMage(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color stone = BodyPalette.RockGolems.Stone, shade = BodyPalette.RockGolems.StoneShade;

            BlockLegs(b, 0.14f, 0.1f, Extent(0.12f, 0.14f, 0.14f), shade);
            hull.Taper(Point(0f, 0.08f, 0f), Pair(0.5f, 0.4f), Pair(0.3f, 0.26f), 0.5f, stone);
            hull.Taper(Point(0f, 0.56f, -0.02f), Pair(0.34f, 0.32f), Pair(0.12f, 0.12f), 0.36f, shade);
            hull.Box(Point(0f, 0.68f, 0.12f), Extent(0.18f, 0.16f, 0.06f), BodyPalette.RockGolems.MossDeep);
            Eyes(hull, Point(0f, 0.69f, 0.155f), 0.08f, 0.035f);
            hull.Box(Point(0f, 0.38f, 0.17f), Extent(0.07f, 0.07f, 0.03f), BodyPalette.RockGolems.Rune, Turn(0f, 0f, 45f));
            hull.Dome(Point(-0.16f, 0.54f, 0f), Extent(0.1f, 0.04f, 0.1f), 6, 1, BodyPalette.RockGolems.Moss);
            Strap(b, hull, Point(0f, 0.52f, 0f), Extent(0.36f, 0.05f, 0.3f));

            b.Arm.Origin = Point(0.24f, 0.42f, 0.06f);
            b.Arm.Box(Point(0.26f, 0.4f, 0.08f), Extent(0.09f, 0.1f, 0.09f), stone);
            b.Arm.Box(Point(0.28f, 0.45f, 0.12f), Extent(0.04f, 0.74f, 0.04f), shade);
            b.Arm.Prism(Point(0.28f, 0.82f, 0.12f), 6, 0.05f, 0.05f, 0.1f, BodyPalette.RockGolems.Rune);
            b.Arm.Prism(Point(0.28f, 0.92f, 0.12f), 6, 0.05f, 0f, 0.08f, BodyPalette.RockGolems.Rune);
        }

        /// <summary>고대 골렘: a heap of mossy boulders in the shape of a giant. Rounded where 이끼바위 골렘 is cut.</summary>
        private static void AncientGolem(Blueprint b)
        {
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone;
            Color moss = BodyPalette.RockGolems.Moss, mossDeep = BodyPalette.RockGolems.MossDeep;

            for (int side = -1; side <= 1; side += 2)
            {
                Solids leg = side < 0 ? b.LimbLeft : b.LimbRight;
                leg.Origin = Point(side * 0.2f, 0.3f, 0f);
                leg.Lump(Point(side * 0.2f, 0.12f, 0.02f), Extent(0.15f, 0.13f, 0.17f), stone, Turn(0f, side * 20f, 0f));
                leg.Lump(Point(side * 0.2f, 0.27f, 0f), Extent(0.13f, 0.1f, 0.13f), tan, Turn(10f, 0f, 0f));
            }
            hull.Lump(Point(0f, 0.55f, 0f), Extent(0.4f, 0.3f, 0.3f), tan, Turn(0f, 10f, 0f));
            hull.Lump(Point(-0.18f, 0.4f, 0.1f), Extent(0.2f, 0.16f, 0.18f), stone, Turn(20f, 0f, 10f));
            hull.Lump(Point(0.2f, 0.42f, -0.08f), Extent(0.2f, 0.17f, 0.2f), stone, Turn(0f, 40f, 0f));
            hull.Lump(Point(-0.38f, 0.74f, 0f), Extent(0.18f, 0.15f, 0.18f), stone, Turn(0f, 15f, 0f));
            hull.Lump(Point(0.38f, 0.74f, 0f), Extent(0.18f, 0.15f, 0.18f), tan, Turn(0f, -35f, 0f));
            hull.Lump(Point(0f, 0.87f, 0.08f), Extent(0.12f, 0.1f, 0.12f), tan, Turn(0f, 30f, 0f));
            Eyes(hull, Point(0f, 0.88f, 0.18f), 0.08f, 0.032f);
            hull.Dome(Point(0f, 0.8f, -0.05f), Extent(0.32f, 0.08f, 0.22f), 8, 2, moss);
            hull.Dome(Point(-0.38f, 0.84f, 0f), Extent(0.16f, 0.07f, 0.15f), 7, 2, mossDeep);
            hull.Dome(Point(0.38f, 0.84f, 0f), Extent(0.16f, 0.07f, 0.15f), 7, 2, moss);
            hull.Lump(Point(-0.44f, 0.5f, 0.02f), Extent(0.12f, 0.14f, 0.12f), stone, Turn(0f, 5f, 0f));
            hull.Lump(Point(-0.44f, 0.27f, 0.04f), Extent(0.15f, 0.12f, 0.15f), tan, Turn(30f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.38f, 0f), Extent(0.6f, 0.06f, 0.42f));

            b.Arm.Origin = Point(0.44f, 0.7f, 0f);
            b.Arm.Lump(Point(0.44f, 0.5f, 0.02f), Extent(0.12f, 0.14f, 0.12f), stone, Turn(0f, 50f, 0f));
            b.Arm.Lump(Point(0.44f, 0.26f, 0.05f), Extent(0.17f, 0.14f, 0.17f), tan, Turn(15f, 20f, 0f));
            b.Arm.Dome(Point(0.44f, 0.6f, 0.02f), Extent(0.09f, 0.04f, 0.09f), 6, 1, moss);
        }

        /// <summary>구르는 바위: one round boulder with seams and a moss patch, eyes in its face. Bounds along rather than walks. Slot 1.</summary>
        private static void RollingRock(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;
            Color stone = BodyPalette.RockGolems.Stone, shade = BodyPalette.RockGolems.StoneShade;

            hull.Ball(Point(0f, 0.42f, 0f), 0.42f, stone);
            hull.Box(Point(0.1f, 0.5f, 0.38f), Extent(0.03f, 0.3f, 0.04f), shade, Turn(0f, 0f, 30f));
            hull.Box(Point(-0.3f, 0.36f, 0.26f), Extent(0.03f, 0.22f, 0.04f), shade, Quaternion.Euler(0f, -40f, 0f) * Turn(0f, 0f, -20f));
            hull.Box(Point(0.32f, 0.62f, -0.12f), Extent(0.04f, 0.2f, 0.03f), shade, Quaternion.Euler(0f, 70f, 0f) * Turn(0f, 0f, 35f));
            hull.Ball(Point(-0.2f, 0.62f, 0.2f), Extent(0.12f, 0.08f, 0.1f), BodyPalette.RockGolems.Tan, Quaternion.identity, 0);
            hull.Dome(Point(0.04f, 0.74f, -0.16f), Extent(0.24f, 0.08f, 0.2f), 7, 2, BodyPalette.RockGolems.Moss);
            Eyes(hull, Point(0f, 0.46f, 0.395f), 0.16f, 0.05f);
            Band(b, hull, Point(0f, 0.36f, 0f), 12, 0.43f, 0.06f);
        }

        /// <summary>성벽 골렘: a broad, short body carrying a flat stone slab on its back, wider than itself and over its head. Slot 2.</summary>
        private static void WallGolem(Blueprint b)
        {
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone, moss = BodyPalette.RockGolems.Moss;

            BlockLegs(b, 0.22f, 0.24f, Extent(0.22f, 0.22f, 0.24f), stone);
            hull.Box(Point(0f, 0.25f, 0f), Extent(0.64f, 0.1f, 0.34f), BodyPalette.RockGolems.StoneShade);
            hull.Box(Point(0f, 0.5f, 0f), Extent(0.96f, 0.46f, 0.38f), tan);
            hull.Lump(Point(-0.28f, 0.5f, 0.17f), Extent(0.16f, 0.14f, 0.08f), stone, Turn(0f, 20f, 10f));
            hull.Lump(Point(0.06f, 0.44f, 0.18f), Extent(0.18f, 0.13f, 0.08f), BodyPalette.RockGolems.StoneShade, Turn(0f, -10f, 0f));
            hull.Lump(Point(0.3f, 0.56f, 0.17f), Extent(0.14f, 0.12f, 0.08f), stone, Turn(10f, 30f, 0f));
            hull.Box(Point(0f, 0.8f, 0.04f), Extent(0.2f, 0.15f, 0.2f), tan);
            Eyes(hull, Point(0f, 0.82f, 0.145f), 0.08f, 0.032f);
            hull.Dome(Point(-0.3f, 0.73f, 0f), Extent(0.2f, 0.06f, 0.17f), 7, 2, moss);
            hull.Dome(Point(0.32f, 0.73f, -0.02f), Extent(0.16f, 0.05f, 0.15f), 7, 2, BodyPalette.RockGolems.MossDeep);
            hull.Box(Point(-0.56f, 0.44f, 0.02f), Extent(0.16f, 0.24f, 0.2f), stone);
            // The wall itself: one flat slab on its back, wider than the body and over its head.
            hull.Box(Point(0f, 0.68f, -0.27f), Extent(1.12f, 0.86f, 0.14f), BodyPalette.RockGolems.StoneShade, Turn(-6f, 0f, 0f));
            hull.Box(Point(0.18f, 0.66f, -0.19f), Extent(0.03f, 0.6f, 0.02f), stone, Turn(-6f, 0f, 12f));
            hull.Dome(Point(-0.2f, 1.1f, -0.29f), Extent(0.36f, 0.06f, 0.1f), 7, 2, moss);
            Strap(b, hull, Point(0f, 0.31f, 0f), Extent(0.98f, 0.05f, 0.4f));

            b.Arm.Origin = Point(0.56f, 0.62f, 0f);
            b.Arm.Box(Point(0.56f, 0.44f, 0.02f), Extent(0.16f, 0.24f, 0.2f), stone);
            b.Arm.Box(Point(0.57f, 0.3f, 0.04f), Extent(0.2f, 0.12f, 0.22f), tan);
        }

        /// <summary>깨어난 바위: a small golem just risen out of rubble, a big lower stone split into two feet, light in its crack. Slot 3.</summary>
        private static void AwakenedStone(Blueprint b)
        {
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone;

            BlockLegs(b, 0.12f, 0.12f, Extent(0.17f, 0.12f, 0.2f), BodyPalette.RockGolems.StoneShade);
            hull.Lump(Point(0f, 0.28f, 0f), Extent(0.3f, 0.18f, 0.26f), stone, Turn(0f, 20f, 0f));
            hull.Lump(Point(0f, 0.46f, 0.02f), Extent(0.22f, 0.14f, 0.2f), tan, Turn(10f, -15f, 0f));
            hull.Box(Point(0.04f, 0.32f, 0.24f), Extent(0.03f, 0.16f, 0.03f), BodyPalette.RockGolems.Waking, Turn(0f, 0f, 25f));
            hull.Box(Point(0f, 0.62f, 0.04f), Extent(0.15f, 0.12f, 0.15f), tan);
            Eyes(hull, Point(0f, 0.63f, 0.115f), 0.07f, 0.03f);
            hull.Dome(Point(-0.1f, 0.56f, -0.04f), Extent(0.14f, 0.05f, 0.12f), 6, 1, BodyPalette.RockGolems.Moss);
            hull.Box(Point(-0.27f, 0.36f, 0.02f), Extent(0.1f, 0.16f, 0.12f), stone);
            Strap(b, hull, Point(0f, 0.38f, 0f), Extent(0.44f, 0.04f, 0.36f));

            b.Arm.Origin = Point(0.27f, 0.46f, 0f);
            b.Arm.Box(Point(0.27f, 0.36f, 0.02f), Extent(0.1f, 0.16f, 0.12f), stone);
            b.Arm.Box(Point(0.28f, 0.26f, 0.04f), Extent(0.13f, 0.08f, 0.14f), tan);
        }

        /// <summary>돌팔매 꼬마돌: a small throwing golem holding a round stone up behind its head. Slot 1.</summary>
        private static void PebbleSlinger(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone;

            BlockLegs(b, 0.15f, 0.11f, Extent(0.12f, 0.15f, 0.14f), BodyPalette.RockGolems.StoneShade);
            hull.Taper(Point(0f, 0.15f, 0f), Pair(0.36f, 0.28f), Pair(0.42f, 0.3f), 0.3f, tan);
            hull.Box(Point(0f, 0.55f, 0.02f), Extent(0.22f, 0.18f, 0.2f), tan);
            Eyes(hull, Point(0f, 0.56f, 0.125f), 0.09f, 0.035f);
            hull.Dome(Point(0f, 0.64f, -0.02f), Extent(0.12f, 0.04f, 0.11f), 6, 1, BodyPalette.RockGolems.Moss);
            hull.Dome(Point(0f, 0.44f, -0.08f), Extent(0.18f, 0.05f, 0.12f), 6, 1, BodyPalette.RockGolems.MossDeep);
            hull.Box(Point(-0.24f, 0.3f, 0.04f), Extent(0.1f, 0.18f, 0.12f), stone);
            Strap(b, hull, Point(0f, 0.2f, 0f), Extent(0.38f, 0.05f, 0.3f));

            b.Arm.Origin = Point(0.24f, 0.4f, 0f);
            b.Arm.Box(Point(0.25f, 0.52f, -0.04f), Extent(0.1f, 0.24f, 0.11f), stone, Turn(-20f, 0f, 0f));
            b.Arm.Ball(Point(0.24f, 0.74f, -0.1f), Vector3.one * 0.15f, BodyPalette.RockGolems.StoneShade, Quaternion.identity, 0);
        }

        /// <summary>부유석 파수: a heavy stone hanging low in the air, broad shoulder boulders, a small head, one thick arm. Airborne, slot 2.</summary>
        private static void FloatingSentinel(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone, shade = BodyPalette.RockGolems.StoneShade;

            hull.Prism(Point(0f, 0.08f, 0f), 6, 0.06f, 0.3f, 0.3f, shade);
            hull.Lump(Point(0f, 0.46f, 0f), Extent(0.42f, 0.24f, 0.32f), tan, Turn(0f, 15f, 0f));
            hull.Lump(Point(-0.34f, 0.6f, 0f), Extent(0.18f, 0.16f, 0.2f), stone, Turn(0f, 30f, 10f));
            hull.Lump(Point(0.34f, 0.6f, 0f), Extent(0.18f, 0.16f, 0.2f), stone, Turn(0f, -20f, -10f));
            hull.Box(Point(0f, 0.74f, 0.06f), Extent(0.18f, 0.14f, 0.17f), tan);
            Eyes(hull, Point(0f, 0.75f, 0.15f), 0.08f, 0.032f);
            hull.Dome(Point(-0.04f, 0.66f, -0.1f), Extent(0.3f, 0.07f, 0.2f), 8, 2, BodyPalette.RockGolems.Moss);
            hull.Lump(Point(-0.34f, 0.36f, 0.06f), Extent(0.12f, 0.12f, 0.12f), stone, Turn(20f, 0f, 0f));
            Strap(b, hull, Point(0f, 0.42f, 0f), Extent(0.62f, 0.05f, 0.5f));

            for (int side = -1; side <= 1; side += 2)
            {
                Solids slab = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(slab, side, Point(0.46f, 0.5f, -0.08f), 0.24f, 0.2f, 0.14f, 0f, 20f, shade);
            }

            b.Arm.Origin = Point(0.36f, 0.52f, 0.04f);
            b.Arm.Lump(Point(0.4f, 0.4f, 0.16f), Extent(0.13f, 0.12f, 0.16f), stone, Turn(30f, 0f, 0f));
            b.Arm.Lump(Point(0.42f, 0.32f, 0.32f), Extent(0.15f, 0.13f, 0.15f), tan, Turn(10f, 20f, 0f));
        }

        /// <summary>모래 술사: a stone shaper with its right arm out, a twist of sand blocks hanging over the open hand. Slot 3.</summary>
        private static void SandShaper(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone;
            Color sand = BodyPalette.Lighter(tan, 0.25f);

            BlockLegs(b, 0.22f, 0.13f, Extent(0.15f, 0.22f, 0.17f), stone);
            hull.Taper(Point(0f, 0.22f, 0f), Pair(0.42f, 0.3f), Pair(0.5f, 0.34f), 0.32f, tan);
            hull.Box(Point(0f, 0.66f, 0.03f), Extent(0.18f, 0.16f, 0.18f), tan);
            Eyes(hull, Point(0f, 0.67f, 0.125f), 0.08f, 0.032f);
            hull.Dome(Point(-0.22f, 0.54f, 0f), Extent(0.16f, 0.07f, 0.16f), 7, 2, BodyPalette.RockGolems.Moss);
            hull.Dome(Point(0.22f, 0.54f, 0f), Extent(0.13f, 0.05f, 0.13f), 6, 1, BodyPalette.RockGolems.MossDeep);
            hull.Box(Point(-0.3f, 0.36f, 0.04f), Extent(0.12f, 0.24f, 0.14f), stone);
            Strap(b, hull, Point(0f, 0.27f, 0f), Extent(0.44f, 0.05f, 0.32f));

            b.Arm.Origin = Point(0.28f, 0.5f, 0f);
            b.Arm.Box(Point(0.36f, 0.48f, 0.12f), Extent(0.12f, 0.12f, 0.3f), stone, Turn(0f, 25f, 0f));
            b.Arm.Box(Point(0.44f, 0.5f, 0.27f), Extent(0.14f, 0.1f, 0.12f), tan);
            for (int k = 0; k < 4; k++)
                b.Arm.Box(Point(0.44f + (k % 2 == 0 ? -0.05f : 0.05f), 0.64f + k * 0.1f, 0.28f), Extent(0.1f - k * 0.01f, 0.06f, 0.08f),
                          k % 2 == 0 ? sand : tan, Turn(0f, k * 35f, k % 2 == 0 ? 20f : -20f));
        }

        /// <summary>이끼 현자: a heavy golem grown over with moss, cradling a small rubble stone in both arms. Weaponless.</summary>
        private static void MossSage(Blueprint b)
        {
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone;
            Color moss = BodyPalette.RockGolems.Moss, mossDeep = BodyPalette.RockGolems.MossDeep;

            BlockLegs(b, 0.22f, 0.15f, Extent(0.2f, 0.22f, 0.22f), stone);
            hull.Lump(Point(0f, 0.48f, -0.02f), Extent(0.36f, 0.28f, 0.28f), tan, Turn(0f, 10f, 0f));
            hull.Dome(Point(0f, 0.56f, -0.12f), Extent(0.4f, 0.26f, 0.24f), 9, 3, mossDeep);
            hull.Box(Point(0f, 0.8f, 0.06f), Extent(0.2f, 0.16f, 0.18f), tan);
            hull.Dome(Point(0f, 0.86f, 0.02f), Extent(0.16f, 0.08f, 0.14f), 7, 2, moss);
            Eyes(hull, Point(0f, 0.8f, 0.155f), 0.08f, 0.032f);
            hull.Box(Point(-0.22f, 0.44f, 0.22f), Extent(0.26f, 0.12f, 0.12f), stone, Turn(0f, -30f, 0f));
            hull.Box(Point(0.22f, 0.44f, 0.22f), Extent(0.26f, 0.12f, 0.12f), stone, Turn(0f, 30f, 0f));
            hull.Lump(Point(0f, 0.5f, 0.3f), Extent(0.13f, 0.11f, 0.11f), BodyPalette.RockGolems.StoneShade, Turn(10f, 20f, 0f));
            hull.Dome(Point(0f, 0.59f, 0.3f), Extent(0.07f, 0.03f, 0.06f), 6, 1, moss);
            hull.Dome(Point(-0.34f, 0.62f, 0f), Extent(0.14f, 0.07f, 0.14f), 7, 2, moss);
            hull.Dome(Point(0.34f, 0.62f, 0f), Extent(0.14f, 0.07f, 0.14f), 7, 2, moss);
            Strap(b, hull, Point(0f, 0.3f, 0f), Extent(0.6f, 0.05f, 0.46f));
        }

        /// <summary>이끼 포자 무리: three moss-capped stones drifting together, two broad moss leaves beating at the sides. Airborne, weaponless.</summary>
        private static void SporeCluster(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, moss = BodyPalette.RockGolems.Moss, mossDeep = BodyPalette.RockGolems.MossDeep;

            Vector3[] stones = { Point(0f, 0.36f, 0.06f), Point(-0.22f, 0.28f, -0.14f), Point(0.2f, 0.3f, -0.16f) };
            float[] sizes = { 0.2f, 0.15f, 0.14f };
            for (int i = 0; i < stones.Length; i++)
            {
                hull.Lump(stones[i], Vector3.one * sizes[i], i == 0 ? tan : BodyPalette.RockGolems.Stone, Turn(0f, i * 50f, 10f));
                hull.Dome(stones[i] + Point(0f, sizes[i] * 0.55f, 0f), Extent(sizes[i] * 1.05f, sizes[i] * 0.5f, sizes[i]), 7, 2, i == 1 ? mossDeep : moss);
            }
            Eyes(hull, Point(0f, 0.36f, 0.25f), 0.1f, 0.04f);
            hull.Ball(Point(0.02f, 0.56f, -0.06f), Extent(0.2f, 0.035f, 0.14f), moss, Turn(-25f, 0f, -12f), 1);
            Band(b, hull, Point(0f, 0.26f, 0.06f), 8, 0.2f, 0.04f);

            for (int side = -1; side <= 1; side += 2)
            {
                Solids leaf = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(leaf, side, Point(0.18f, 0.44f, 0f), 0.3f, 0.22f, 0.06f, -0.06f, 25f, mossDeep);
            }
        }

        // ---- 차원 유랑종 ----

        private static bool DriftworldUnit(Blueprint b, Role role, int slot)
        {
            switch (role)
            {
                case Role.Worker when slot == 0: Salamander(b, BodyPalette.Driftworlds.Shell, BodyPalette.Driftworlds.ShellDeep, BodyPalette.Driftworlds.FireWorld, false); return true;
                case Role.Melee when slot == 0: Salamander(b, BodyPalette.Driftworlds.Dusk, BodyPalette.Driftworlds.ShellDeep, BodyPalette.Driftworlds.TealLight, true); return true;
                case Role.Melee when slot == 1: ExtinctSlime(b, BodyPalette.Driftworlds.FireWorld, 0); return true;
                case Role.Melee when slot == 2: ExtinctSlime(b, BodyPalette.Driftworlds.StoneWorld, 1); return true;
                case Role.Ranged when slot == 0: RiftMarksman(b); return true;
                case Role.Ranged when slot == 1: ExtinctSlime(b, BodyPalette.Driftworlds.StormWorld, 2); return true;
                case Role.Ranged when slot == 2: VineShard(b); return true;
                case Role.Ranged when slot == 3: ExtinctWindSlime(b); return true;
                case Role.Signature when slot == 0: BoundaryToad(b); return true;
                case Role.Signature when slot == 1: WanderingIsland(b); return true;
                case Role.Signature when slot == 2: StrataObserver(b); return true;
                case Role.Signature when slot == 3: ExtinctNatureSlime(b); return true;
                default: return false;
            }
        }

        /// <summary>
        /// 화산편 and 폭풍편: a salamander on four legs, two shell plates down its
        /// back parting on a stripe of the world inside. The worker carries a
        /// shard of it; the raider has a jaw and a darker hide.
        /// </summary>
        private static void Salamander(Blueprint b, Color hide, Color plate, Color interior, bool raider)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Lunge;
            Solids hull = b.Hull;

            hull.Taper(Point(0f, 0.3f, -0.36f), Pair(0.32f, 0.26f), Pair(0.3f, 0.24f), 0.62f, hide, Lying);
            hull.Taper(Point(0f, 0.3f, 0.26f), Pair(0.3f, 0.24f), Pair(0.22f, 0.12f), 0.26f, hide, Lying);
            hull.Taper(Point(0f, 0.3f, -0.34f), Pair(0.18f, 0.16f), Pair(0.03f, 0.03f), 0.5f, plate, Turn(-98f, 0f, 0f));
            hull.Box(Point(0f, 0.43f, -0.06f), Extent(0.08f, 0.04f, 0.56f), interior);
            hull.Box(Point(-0.1f, 0.44f, -0.06f), Extent(0.12f, 0.06f, 0.6f), plate, Turn(0f, 0f, 12f));
            hull.Box(Point(0.1f, 0.44f, -0.06f), Extent(0.12f, 0.06f, 0.6f), plate, Turn(0f, 0f, -12f));
            Eyes(hull, Point(0f, 0.4f, 0.4f), 0.2f, 0.045f);
            if (raider)
            {
                hull.Taper(Point(0f, 0.22f, 0.3f), Pair(0.26f, 0.06f), Pair(0.18f, 0.04f), 0.26f, plate, Lying);
                hull.Prism(Point(-0.07f, 0.42f, 0.32f), 4, 0.035f, 0f, 0.12f, interior, Turn(-50f, 0f, 15f));
                hull.Prism(Point(0.07f, 0.42f, 0.32f), 4, 0.035f, 0f, 0.12f, interior, Turn(-50f, 0f, -15f));
            }
            else
            {
                hull.Prism(Point(0f, 0.46f, -0.24f), 5, 0.06f, 0f, 0.2f, BodyPalette.Driftworlds.TealLight, Turn(-20f, 0f, 15f));
            }
            Strap(b, hull, Point(0f, 0.3f, 0.24f), Extent(0.34f, 0.28f, 0.05f));

            SalamanderLeg(b.LimbLeft, -1f, 0.14f, hide, plate);
            SalamanderLeg(b.LimbLeft, 1f, -0.24f, hide, plate);
            SalamanderLeg(b.LimbRight, 1f, 0.14f, hide, plate);
            SalamanderLeg(b.LimbRight, -1f, -0.24f, hide, plate);
        }

        private static void SalamanderLeg(Solids part, float side, float z, Color leg, Color foot)
        {
            part.Box(Point(side * 0.21f, 0.1f, z), Extent(0.1f, 0.2f, 0.1f), leg, Turn(0f, 0f, side * 25f));
            part.Box(Point(side * 0.25f, 0.02f, z + 0.02f), Extent(0.12f, 0.04f, 0.12f), foot);
        }

        /// <summary>
        /// The three extinct slimes the 층위 관측대 calls back: fire (0) a flame on
        /// a cinder pile, rock (1) a flat speckled blob, lightning (2) a yellow
        /// dome with a zigzag crown. Brought in from other worlds, so each wears
        /// its own world's colour rather than the wanderer shell.
        /// </summary>
        private static void ExtinctSlime(Blueprint b, Color body, int which)
        {
            b.Gait = Gait.Hop;
            b.Strike = which == 2 ? Strike.Recoil : Strike.Lunge;
            Solids hull = b.Hull;

            switch (which)
            {
                case 0:
                    hull.Lump(Point(0f, 0.12f, 0f), Extent(0.42f, 0.18f, 0.4f), BodyPalette.Driftworlds.ShellDeep, Turn(0f, 20f, 0f));
                    hull.Dome(Point(0f, 0.2f, 0f), Extent(0.32f, 0.5f, 0.3f), 9, 3, body);
                    hull.Prism(Point(0f, 0.62f, -0.04f), 6, 0.14f, 0f, 0.32f, body, Turn(-35f, 0f, 0f));
                    Eyes(hull, Point(0f, 0.42f, 0.27f), 0.15f, 0.06f);
                    Band(b, hull, Point(0f, 0.2f, 0f), 9, 0.33f, 0.05f, 0.95f);
                    break;
                case 1:
                    hull.Dome(Vector3.zero, Extent(0.46f, 0.42f, 0.42f), 10, 3, body);
                    Color spot = BodyPalette.Darker(body, 0.78f);
                    hull.Ball(Point(-0.22f, 0.3f, 0.25f), Extent(0.07f, 0.05f, 0.05f), spot);
                    hull.Ball(Point(0.2f, 0.34f, 0.2f), Extent(0.06f, 0.04f, 0.05f), spot);
                    hull.Ball(Point(0.05f, 0.4f, -0.1f), Extent(0.08f, 0.04f, 0.07f), spot);
                    hull.Ball(Point(-0.26f, 0.22f, -0.2f), Extent(0.07f, 0.05f, 0.06f), spot);
                    Eyes(hull, Point(0f, 0.24f, 0.37f), 0.17f, 0.06f);
                    Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.47f, 0.06f, 0.92f);
                    break;
                default:
                    hull.Dome(Vector3.zero, Extent(0.4f, 0.62f, 0.38f), 10, 3, body);
                    Color crown = BodyPalette.Darker(body, 0.8f);
                    for (int i = 0; i < 8; i++)
                    {
                        float yaw = i * 45f;
                        hull.Box(Around(yaw) * 0.31f + Point(0f, 0.42f, 0f), Extent(0.12f, 0.05f, 0.04f), crown,
                                 Quaternion.Euler(0f, yaw + 90f, 0f) * Turn(0f, 0f, i % 2 == 0 ? 30f : -30f));
                    }
                    Eyes(hull, Point(0f, 0.28f, 0.34f), 0.17f, 0.065f);
                    Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.41f, 0.06f, 0.95f);
                    break;
            }
        }

        /// <summary>틈새 사수: a long, low armoured crawler on six legs, plates parted on a teal interior, a barrel for a snout.</summary>
        private static void RiftMarksman(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade, deep = BodyPalette.Driftworlds.ShellDeep;

            hull.Box(Point(0f, 0.3f, -0.05f), Extent(0.28f, 0.16f, 0.82f), BodyPalette.Driftworlds.Teal);
            float[] plates = { -0.36f, -0.06f, 0.24f };
            for (int i = 0; i < plates.Length; i++)
                hull.Taper(Point(0f, 0.2f, plates[i]), Pair(0.44f, 0.26f), Pair(0.34f, 0.2f), 0.26f, i == 1 ? shade : shell);
            hull.Prism(Point(0f, 0.32f, 0.36f), 6, 0.075f, 0.06f, 0.42f, shade, Lying);
            hull.Prism(Point(0f, 0.32f, 0.76f), 6, 0.07f, 0.07f, 0.04f, BodyPalette.Driftworlds.TealLight, Lying);
            hull.Taper(Point(0f, 0.26f, -0.5f), Pair(0.24f, 0.14f), Pair(0.06f, 0.05f), 0.2f, deep, Turn(-100f, 0f, 0f));
            hull.Ball(Point(-0.1f, 0.38f, 0.4f), 0.03f, BodyPalette.Driftworlds.TealLight);
            hull.Ball(Point(0.1f, 0.38f, 0.4f), 0.03f, BodyPalette.Driftworlds.TealLight);
            Strap(b, hull, Point(0f, 0.465f, 0.24f), Extent(0.3f, 0.03f, 0.22f));

            SixLegs(b, deep, 0.24f, -0.06f, -0.36f);
        }

        private static void MarksmanLeg(Solids part, float side, float z, Color color)
        {
            part.Taper(Point(side * 0.3f, 0f, z), Pair(0.06f, 0.06f), Pair(0.08f, 0.08f), 0.26f, color, Turn(0f, 0f, side * 22f));
        }

        /// <summary>
        /// 경계 운반자: a big crouched lilac toad carrying two shell plates that
        /// part on the world inside. Hops. Non-combat, so its "attack" is a swell.
        /// </summary>
        private static void BoundaryToad(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color lilac = BodyPalette.Driftworlds.Lilac, dusk = BodyPalette.Driftworlds.Dusk, shell = BodyPalette.Driftworlds.Shell;

            hull.Ball(Point(0f, 0.38f, -0.02f), Extent(0.42f, 0.36f, 0.42f), lilac);
            hull.Ball(Point(0f, 0.28f, 0.16f), Extent(0.32f, 0.24f, 0.3f), BodyPalette.Driftworlds.Belly);
            hull.Ball(Point(0f, 0.58f, 0.2f), Extent(0.34f, 0.22f, 0.28f), lilac);
            hull.Ball(Point(-0.2f, 0.74f, 0.26f), 0.1f, lilac);
            hull.Ball(Point(0.2f, 0.74f, 0.26f), 0.1f, lilac);
            Eyes(hull, Point(0f, 0.76f, 0.34f), 0.4f, 0.06f);
            hull.Ball(Point(-0.36f, 0.17f, -0.12f), Extent(0.14f, 0.17f, 0.26f), dusk);
            hull.Ball(Point(0.36f, 0.17f, -0.12f), Extent(0.14f, 0.17f, 0.26f), dusk);
            hull.Taper(Point(-0.2f, 0f, 0.3f), Pair(0.12f, 0.14f), Pair(0.08f, 0.08f), 0.24f, dusk);
            hull.Taper(Point(0.2f, 0f, 0.3f), Pair(0.12f, 0.14f), Pair(0.08f, 0.08f), 0.24f, dusk);
            hull.Box(Point(0f, 0.73f, -0.14f), Extent(0.1f, 0.06f, 0.44f), BodyPalette.Driftworlds.TealLight);
            hull.Box(Point(-0.17f, 0.71f, -0.14f), Extent(0.26f, 0.08f, 0.52f), shell, Turn(0f, 0f, 22f));
            hull.Box(Point(0.17f, 0.71f, -0.14f), Extent(0.26f, 0.08f, 0.52f), BodyPalette.Driftworlds.ShellShade, Turn(0f, 0f, -22f));
            Band(b, hull, Point(0f, 0.44f, 0.18f), 10, 0.35f, 0.05f, 0.8f);
        }

        /// <summary>덩쿨편: a round-backed crawler on six legs, two domed plates parting on the world inside, a vine of that world rising from the gap and another for a snout.</summary>
        private static void VineShard(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade, deep = BodyPalette.Driftworlds.ShellDeep;

            hull.Box(Point(0f, 0.3f, -0.02f), Extent(0.3f, 0.2f, 0.52f), BodyPalette.Driftworlds.Teal);
            hull.Dome(Point(0f, 0.22f, -0.2f), Extent(0.34f, 0.3f, 0.24f), 8, 2, shell);
            hull.Dome(Point(0f, 0.22f, 0.16f), Extent(0.3f, 0.26f, 0.2f), 8, 2, shade);
            hull.Box(Point(0f, 0.44f, -0.02f), Extent(0.14f, 0.05f, 0.08f), BodyPalette.Driftworlds.Lilac);
            hull.Prism(Point(0.04f, 0.42f, -0.02f), 4, 0.04f, 0f, 0.1f, BodyPalette.Driftworlds.TealLight);
            // A crest of the vine's world, rising out of the gap and curling forward.
            Color crest = BodyPalette.Driftworlds.NatureWorld;
            Vector3 top = Curl(hull, Point(0f, 0.4f, -0.04f), 5, 5, 0.055f, 0.86f, 0.17f, 0.16f, -24f, 26f, crest, crest);
            hull.Ball(top, Extent(0.09f, 0.03f, 0.06f), crest, Turn(0f, 0f, 20f), 1);
            hull.Ball(Point(-0.08f, 0.62f, -0.12f), Extent(0.08f, 0.025f, 0.05f), crest, Turn(0f, 0f, -30f), 1);
            hull.Taper(Point(0f, 0.24f, 0.32f), Pair(0.22f, 0.14f), Pair(0.16f, 0.1f), 0.1f, deep, Lying);
            hull.Ball(Point(-0.07f, 0.3f, 0.42f), 0.03f, BodyPalette.Driftworlds.TealLight);
            hull.Ball(Point(0.07f, 0.3f, 0.42f), 0.03f, BodyPalette.Driftworlds.TealLight);
            Strap(b, hull, Point(0f, 0.32f, 0.3f), Extent(0.3f, 0.2f, 0.04f));

            SixLegs(b, deep, 0.16f, -0.04f, -0.24f);

            b.Arm.Origin = Point(0f, 0.26f, 0.42f);
            Color vine = BodyPalette.Driftworlds.NatureWorld;
            Vector3 position = Curl(b.Arm, Point(0f, 0.26f, 0.4f), 4, 5, 0.045f, 0.85f, 0.13f, 0.12f, 80f, -32f, vine, vine);
            b.Arm.Ball(position, Extent(0.06f, 0.025f, 0.04f), vine);
            b.Arm.Ball(Point(0.08f, 0.32f, 0.54f), Extent(0.05f, 0.02f, 0.04f), vine);
        }

        /// <summary>멸종한 바람 슬라임: a pale teal slime under two shell plates, wisps of its wind trailing behind. Airborne, a temporary summon.</summary>
        private static void ExtinctWindSlime(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color body = BodyPalette.Driftworlds.TealLight, wisp = BodyPalette.Driftworlds.WindWorld;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade;

            hull.Ball(Point(0f, 0.4f, 0.04f), Extent(0.26f, 0.22f, 0.28f), body);
            hull.Box(Point(-0.1f, 0.58f, -0.06f), Extent(0.26f, 0.07f, 0.4f), shell, Turn(0f, 0f, 18f));
            hull.Box(Point(0.12f, 0.57f, -0.04f), Extent(0.24f, 0.07f, 0.36f), shade, Turn(0f, 0f, -20f));
            hull.Box(Point(0f, 0.2f, -0.04f), Extent(0.3f, 0.06f, 0.34f), shade);
            Eyes(hull, Point(0f, 0.42f, 0.3f), 0.15f, 0.055f);
            hull.Ball(Point(0.06f, 0.42f, -0.3f), Extent(0.12f, 0.08f, 0.18f), wisp);
            hull.Ball(Point(0.13f, 0.48f, -0.5f), Extent(0.07f, 0.05f, 0.12f), wisp);
            hull.Ball(Point(-0.1f, 0.34f, -0.36f), Extent(0.08f, 0.06f, 0.14f), body);
            Band(b, hull, Point(0f, 0.3f, 0.04f), 10, 0.24f, 0.04f, 1.08f);

            for (int side = -1; side <= 1; side += 2)
            {
                Solids plate = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(plate, side, Point(0.22f, 0.44f, 0f), 0.2f, 0.22f, 0.12f, -0.06f, 18f, shade);
            }
        }

        /// <summary>
        /// 떠도는 섬: a broad shell cap over a narrowing underside, a ring of the
        /// world inside showing between them, roots hanging down into the air.
        /// Airborne and weaponless; two small plates beat at its sides.
        /// </summary>
        private static void WanderingIsland(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade, deep = BodyPalette.Driftworlds.ShellDeep;

            hull.Dome(Point(0f, 0.52f, 0f), Extent(0.46f, 0.24f, 0.42f), 9, 2, shell);
            hull.Prism(Point(0f, 0.44f, 0f), 9, 0.44f, 0.44f, 0.09f, BodyPalette.Driftworlds.Teal);
            hull.Prism(Point(0f, 0.46f, 0f), 9, 0.4f, 0.4f, 0.04f, BodyPalette.Driftworlds.Dusk);
            for (int i = 0; i < 5; i++)
            {
                float yaw = 160f + i * 22f;
                hull.Prism(Around(yaw) * 0.38f + Point(0f, 0.5f, 0f), 4, 0.05f, 0f, 0.14f, BodyPalette.Driftworlds.TealLight);
            }
            hull.Prism(Point(0f, 0.45f, 0f), 8, 0.42f, 0.1f, 0.3f, shade, Turn(180f, 0f, 0f));
            // Roots splay out and down from under the rim, long enough to be seen from above.
            for (int i = 0; i < 10; i++)
            {
                float yaw = 10f + i * 36f;
                Quaternion hang = Quaternion.Euler(0f, yaw, 0f) * Turn(148f + (i % 2) * 10f, 0f, 0f);
                hull.Taper(Around(yaw) * 0.32f + Point(0f, 0.4f, 0f), Pair(0.045f, 0.045f), Pair(0.012f, 0.012f), 0.24f + (i % 3) * 0.06f,
                           i % 2 == 0 ? deep : shade, hang);
            }
            hull.Taper(Point(0f, 0.18f, 0f), Pair(0.06f, 0.06f), Pair(0.015f, 0.015f), 0.26f, deep, Turn(180f, 0f, 0f));
            hull.Lump(Point(0.1f, 0.74f, -0.05f), Extent(0.14f, 0.07f, 0.12f), shade, Turn(0f, 30f, 0f));
            Shard(b, hull, Point(-0.12f, 0.86f, 0.05f), 0.06f);

            for (int side = -1; side <= 1; side += 2)
            {
                Solids plate = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(plate, side, Point(0.42f, 0.5f, 0f), 0.16f, 0.2f, 0.1f, 0f, 10f, shade);
            }
        }

        /// <summary>층위 관측자: a six-legged shell walker under a tall head plate split by a slit of stacked strata. Weaponless caster.</summary>
        private static void StrataObserver(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade, deep = BodyPalette.Driftworlds.ShellDeep;

            hull.Taper(Point(0f, 0.22f, -0.32f), Pair(0.4f, 0.3f), Pair(0.34f, 0.26f), 0.5f, shell, Lying);
            hull.Box(Point(0f, 0.4f, -0.12f), Extent(0.36f, 0.06f, 0.36f), shade, Turn(-6f, 0f, 0f));
            Quaternion lean = Turn(-18f, 0f, 0f);
            hull.Taper(Point(0f, 0.28f, 0.14f), Pair(0.34f, 0.18f), Pair(0.14f, 0.1f), 0.66f, shade, lean);
            Color[] strata = { BodyPalette.Driftworlds.Lilac, BodyPalette.Driftworlds.Dusk, BodyPalette.Driftworlds.TealLight, BodyPalette.Driftworlds.Teal };
            for (int k = 0; k < strata.Length; k++)
            {
                Vector3 local = Point(0f, 0.16f + k * 0.11f, 0.09f - k * 0.006f);
                hull.Box(Point(0f, 0.28f, 0.14f) + lean * local, Extent(0.08f, 0.1f, 0.03f), strata[k], lean);
            }
            hull.Taper(Point(0f, 0.22f, 0.22f), Pair(0.2f, 0.12f), Pair(0.16f, 0.1f), 0.12f, deep, Lying);
            Eyes(hull, Point(0f, 0.28f, 0.33f), 0.14f, 0.04f);
            Strap(b, hull, Point(0f, 0.25f, 0.18f), Extent(0.36f, 0.12f, 0.04f));

            SixLegs(b, deep, 0.12f, -0.1f, -0.32f);
        }

        /// <summary>멸종한 자연 슬라임: a leaf-green slime peeking out of a shell of plates, a broad leaf-shaped plate on top. Weaponless healer.</summary>
        private static void ExtinctNatureSlime(Blueprint b)
        {
            b.Gait = Gait.Hop;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade;

            hull.Dome(Vector3.zero, Extent(0.4f, 0.48f, 0.38f), 10, 3, BodyPalette.Driftworlds.NatureWorld);
            hull.Box(Point(-0.27f, 0.26f, -0.16f), Extent(0.12f, 0.44f, 0.36f), shell, Turn(0f, 0f, 12f));
            hull.Box(Point(0.27f, 0.24f, -0.16f), Extent(0.12f, 0.4f, 0.34f), shade, Turn(0f, 0f, -12f));
            hull.Box(Point(0f, 0.26f, -0.32f), Extent(0.46f, 0.44f, 0.1f), shell);
            hull.Box(Point(0f, 0.5f, -0.22f), Extent(0.08f, 0.06f, 0.2f), BodyPalette.Driftworlds.Teal);
            hull.Ball(Point(0f, 0.58f, -0.14f), Extent(0.34f, 0.05f, 0.26f), shell, Turn(-14f, 0f, 0f), 1);
            hull.Box(Point(0f, 0.63f, -0.12f), Extent(0.035f, 0.03f, 0.42f), BodyPalette.Driftworlds.ShellDeep, Turn(-14f, 0f, 0f));
            Eyes(hull, Point(0f, 0.28f, 0.35f), 0.16f, 0.06f);
            Band(b, hull, Point(0f, 0.02f, 0f), 10, 0.41f, 0.06f, 0.95f);
        }

        // ---- 인간 마법 문명 ----

        private static bool HumanUnit(Blueprint b, Role role, int slot)
        {
            switch (role)
            {
                case Role.Worker when slot == 0: Apprentice(b); return true;
                case Role.Ranged when slot == 0: Commando(b); return true;
                case Role.Ranged when slot == 1: Crossbowman(b); return true;
                case Role.Ranged when slot == 2: ArcaneMage(b); return true;
                case Role.Ranged when slot == 3: StoneWalker(b); return true;
                case Role.Ranged when slot == 4: TowedCannon(b); return true;
                case Role.Ranged when slot == 5: ArcaneBomber(b); return true;
                case Role.Ranged when slot == 6: ArcaneRider(b); return true;
                case Role.Signature when slot == 0: Towerback(b); return true;
                case Role.Signature when slot == 1: ScoutDrone(b); return true;
                case Role.Signature when slot == 2: WorkshopEngineer(b); return true;
                case Role.Signature when slot == 3: ManaAmplifier(b); return true;
                default: return false;
            }
        }

        /// <summary>수습 마법생: a hooded apprentice, satchel at the hip, a staff with a mana gem. No face — the hood is dark.</summary>
        private static void Apprentice(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color steel = BodyPalette.Humans.Steel, wood = BodyPalette.Humans.Wood;

            Legs(b, 0.22f, 0.09f, 0.1f, BodyPalette.Humans.SteelDark, wood);
            hull.Taper(Point(0f, 0.2f, 0f), Pair(0.4f, 0.32f), Pair(0.34f, 0.26f), 0.3f, BodyPalette.Humans.StoneMid);
            hull.Taper(Point(0f, 0.32f, -0.03f), Pair(0.44f, 0.34f), Pair(0.3f, 0.26f), 0.32f, steel);
            hull.Taper(Point(0f, 0.62f, -0.02f), Pair(0.32f, 0.3f), Pair(0.2f, 0.2f), 0.26f, steel);
            hull.Prism(Point(0f, 0.84f, -0.08f), 4, 0.08f, 0f, 0.12f, steel, Turn(-30f, 0f, 0f));
            hull.Box(Point(0f, 0.72f, 0.115f), Extent(0.18f, 0.16f, 0.06f), BodyPalette.Humans.Hood);
            hull.Box(Point(-0.24f, 0.32f, 0.04f), Extent(0.1f, 0.16f, 0.18f), wood);
            hull.Box(Point(-0.08f, 0.48f, 0.14f), Extent(0.04f, 0.4f, 0.02f), BodyPalette.Humans.Bronze, Turn(0f, 0f, -40f));
            Strap(b, hull, Point(0f, 0.62f, -0.02f), Extent(0.34f, 0.05f, 0.31f));

            b.Arm.Origin = Point(0.24f, 0.48f, 0.04f);
            b.Arm.Box(Point(0.26f, 0.42f, 0.06f), Extent(0.08f, 0.1f, 0.08f), BodyPalette.Humans.StoneMid);
            b.Arm.Box(Point(0.27f, 0.45f, 0.1f), Extent(0.035f, 0.75f, 0.035f), wood);
            b.Arm.Prism(Point(0.27f, 0.84f, 0.1f), 4, 0.05f, 0f, 0.08f, BodyPalette.Humans.Mana);
            b.Arm.Prism(Point(0.27f, 0.84f, 0.1f), 4, 0.05f, 0f, 0.06f, BodyPalette.Humans.Mana, Turn(180f, 0f, 0f));
        }

        /// <summary>공수 특공대: a helmeted commando with a launcher, a folded paraglider canopy over the pack.</summary>
        private static void Commando(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color steel = BodyPalette.Humans.Steel, bronze = BodyPalette.Humans.Bronze, canvas = BodyPalette.Humans.Canvas;

            Legs(b, 0.24f, 0.1f, 0.11f, BodyPalette.Humans.StoneDark, BodyPalette.Humans.Wood);
            hull.Box(Point(0f, 0.41f, 0f), Extent(0.36f, 0.34f, 0.26f), steel);
            hull.Box(Point(0f, 0.26f, 0f), Extent(0.37f, 0.05f, 0.27f), bronze);
            hull.Box(Point(0f, 0.69f, 0.02f), Extent(0.2f, 0.16f, 0.2f), BodyPalette.Humans.StoneMid);
            hull.Box(Point(0f, 0.76f, 0.02f), Extent(0.26f, 0.14f, 0.26f), bronze);
            hull.Box(Point(0f, 0.68f, 0.12f), Extent(0.18f, 0.06f, 0.03f), BodyPalette.Humans.Hood);
            hull.Box(Point(0f, 0.45f, -0.18f), Extent(0.28f, 0.3f, 0.12f), BodyPalette.Humans.Wood);
            hull.Box(Point(0f, 1.0f, -0.14f), Extent(0.46f, 0.04f, 0.28f), steel);
            hull.Box(Point(-0.31f, 0.94f, -0.14f), Extent(0.2f, 0.04f, 0.28f), canvas, Turn(0f, 0f, 25f));
            hull.Box(Point(0.31f, 0.94f, -0.14f), Extent(0.2f, 0.04f, 0.28f), canvas, Turn(0f, 0f, -25f));
            hull.Box(Point(-0.12f, 0.78f, -0.18f), Extent(0.015f, 0.4f, 0.015f), BodyPalette.Humans.StoneDark, Turn(0f, 0f, 20f));
            hull.Box(Point(0.12f, 0.78f, -0.18f), Extent(0.015f, 0.4f, 0.015f), BodyPalette.Humans.StoneDark, Turn(0f, 0f, -20f));
            Strap(b, hull, Point(-0.21f, 0.57f, 0f), Extent(0.1f, 0.07f, 0.26f));
            Strap(b, hull, Point(0.21f, 0.57f, 0f), Extent(0.1f, 0.07f, 0.26f));

            b.Arm.Origin = Point(0.22f, 0.55f, 0f);
            b.Arm.Box(Point(0.24f, 0.46f, 0.04f), Extent(0.08f, 0.2f, 0.08f), steel);
            b.Arm.Prism(Point(0.24f, 0.4f, -0.08f), 6, 0.05f, 0.05f, 0.4f, BodyPalette.Humans.StoneDark, Lying);
            b.Arm.Prism(Point(0.24f, 0.4f, 0.24f), 6, 0.06f, 0.06f, 0.05f, bronze, Lying);
        }

        /// <summary>
        /// Towerback: a captured 꼬마돌 carrying a human gun tower, held by bronze
        /// bands. STYLE.md asks that the warm living stone and moss of the captive
        /// stay visibly apart from the cool masonry and steel of the tower — that
        /// contrast is the unit's whole story.
        /// </summary>
        private static void Towerback(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.Humans.Stone, bronze = BodyPalette.Humans.Bronze;

            BlockLegs(b, 0.16f, 0.15f, Extent(0.14f, 0.16f, 0.16f), BodyPalette.RockGolems.Stone);
            hull.Lump(Point(0f, 0.34f, 0.04f), Extent(0.38f, 0.25f, 0.3f), tan, Turn(0f, 12f, 0f));
            hull.Lump(Point(-0.35f, 0.25f, 0.04f), Extent(0.12f, 0.2f, 0.16f), BodyPalette.RockGolems.Stone, Turn(0f, 0f, -15f));
            hull.Lump(Point(0.35f, 0.25f, 0.04f), Extent(0.12f, 0.2f, 0.16f), BodyPalette.RockGolems.Stone, Turn(0f, 0f, 15f));
            Eyes(hull, Point(0f, 0.36f, 0.35f), 0.16f, 0.05f);
            hull.Dome(Point(0.18f, 0.5f, 0.18f), Extent(0.12f, 0.04f, 0.1f), 6, 1, BodyPalette.RockGolems.Moss);
            hull.Box(Point(0f, 0.3f, 0.04f), Extent(0.68f, 0.05f, 0.56f), bronze);
            hull.Box(Point(0f, 0.65f, -0.06f), Extent(0.44f, 0.3f, 0.4f), stone);
            hull.Box(Point(0f, 0.66f, 0.145f), Extent(0.45f, 0.025f, 0.025f), BodyPalette.Humans.StoneMid);
            hull.Box(Point(0f, 0.73f, 0.165f), Extent(0.13f, 0.2f, 0.03f), BodyPalette.Humans.Steel);
            hull.Box(Point(0f, 0.73f, 0.185f), Extent(0.055f, 0.075f, 0.018f), bronze, Turn(0f, 0f, 45f));
            for (int side = -1; side <= 1; side += 2)
            {
                hull.Box(Point(side * 0.26f, 0.5f, 0.01f), Extent(0.045f, 0.4f, 0.1f), bronze, Turn(0f, 0f, side * 12f));
                hull.Box(Point(side * 0.24f, 0.72f, -0.06f), Extent(0.08f, 0.24f, 0.42f), BodyPalette.Humans.StoneMid);
            }
            hull.Box(Point(0f, 0.505f, -0.06f), Extent(0.48f, 0.03f, 0.44f), BodyPalette.Humans.Steel);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.17f : 0.17f, z = i < 2 ? -0.22f : 0.1f;
                hull.Box(Point(x, 0.84f, z), Extent(0.09f, 0.08f, 0.09f), BodyPalette.Humans.StoneMid);
            }
            Pennant(b, hull, Point(-0.18f, 0.8f, -0.22f), 0.32f, BodyPalette.Humans.Wood);

            b.Arm.Origin = Point(0f, 0.82f, -0.05f);
            HumanCatalogBarrel(b.Arm, Point(0f, 0.86f, -0.09f), 0.105f, 0.56f, Turn(72f, 0f, 0f));
        }

        // Stepped steel barrel, bronze trunnions and a recessed dark bore.
        // All rings share the barrel axis so recoil never separates the muzzle.
        private static void HumanCatalogBarrel(Solids part, Vector3 start, float radius, float length, Quaternion angle)
        {
            Vector3 axis = angle * Vector3.up;
            Color dark = BodyPalette.Humans.StoneDark, steel = BodyPalette.Humans.Steel;
            Color bronze = BodyPalette.Humans.Bronze;
            part.Prism(start, 12, radius * 1.14f, radius * 0.92f, length, dark, angle);
            for (int ring = 0; ring < 3; ring++)
                part.Prism(start + axis * (length * (0.12f + ring * 0.27f)), 12,
                    radius * 1.18f, radius * 1.18f, length * 0.12f, steel, angle);
            Vector3 mouth = start + axis * length;
            part.Prism(mouth, 12, radius * 1.24f, radius * 1.24f, radius * 0.35f, bronze, angle);
            part.Prism(mouth + axis * (radius * 0.355f), 12, radius * 0.88f, radius * 0.88f,
                radius * 0.025f, BodyPalette.Humans.Hood, angle);
        }

        /// <summary>
        /// 마도 정찰기: no art exists for it yet. A floating steel box with one
        /// mana lens and two bronze vanes — a device, per STYLE.md's humans, with
        /// nothing a reader could take for a face.
        /// </summary>
        private static void ScoutDrone(Blueprint b)
        {
            b.Gait = Gait.Float;
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color bronze = BodyPalette.Humans.Bronze, mana = BodyPalette.Humans.Mana;

            hull.Box(Point(0f, 0.52f, 0f), Extent(0.36f, 0.28f, 0.42f), BodyPalette.Humans.Steel);
            hull.Box(Point(0f, 0.68f, 0f), Extent(0.3f, 0.04f, 0.36f), BodyPalette.Humans.Stone);
            hull.Prism(Point(0f, 0.52f, 0.2f), 8, 0.11f, 0.11f, 0.06f, bronze, Lying);
            hull.Prism(Point(0f, 0.52f, 0.25f), 8, 0.075f, 0.075f, 0.03f, mana, Lying);
            hull.Prism(Point(0f, 0.18f, 0f), 4, 0f, 0.12f, 0.2f, BodyPalette.Humans.StoneDark);
            hull.Ball(Point(0f, 0.16f, 0f), 0.06f, mana);
            Strap(b, hull, Point(0f, 0.52f, 0f), Extent(0.38f, 0.05f, 0.44f));

            Wing(b.LimbLeft, -1f, Point(0.18f, 0.56f, 0f), 0.3f, 0.18f, 0.12f, 0f, 10f, bronze);
            Wing(b.LimbRight, 1f, Point(0.18f, 0.56f, 0f), 0.3f, 0.18f, 0.12f, 0f, 10f, bronze);
        }

        /// <summary>석궁 사수: a hooded marksman in grey plate and a steel tabard, a wide crossbow levelled forward. No face, the hood is dark.</summary>
        private static void Crossbowman(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, stoneMid = BodyPalette.Humans.StoneMid;
            Color steel = BodyPalette.Humans.Steel, wood = BodyPalette.Humans.Wood, bronze = BodyPalette.Humans.Bronze;

            Legs(b, 0.22f, 0.09f, 0.1f, BodyPalette.Humans.StoneDark, wood);
            hull.Taper(Point(0f, 0.2f, 0f), Pair(0.38f, 0.28f), Pair(0.36f, 0.26f), 0.34f, stoneMid);
            hull.Box(Point(0f, 0.36f, 0.135f), Extent(0.16f, 0.3f, 0.03f), steel);
            hull.Box(Point(-0.2f, 0.52f, 0f), Extent(0.12f, 0.08f, 0.24f), stone);
            hull.Box(Point(0.2f, 0.52f, 0f), Extent(0.12f, 0.08f, 0.24f), stone);
            hull.Taper(Point(0f, 0.54f, -0.02f), Pair(0.3f, 0.28f), Pair(0.18f, 0.18f), 0.28f, stone);
            hull.Prism(Point(0f, 0.76f, -0.08f), 4, 0.07f, 0f, 0.14f, stone, Turn(-40f, 0f, 0f));
            hull.Box(Point(0f, 0.64f, 0.11f), Extent(0.15f, 0.13f, 0.05f), BodyPalette.Humans.Hood);
            Strap(b, hull, Point(0f, 0.25f, 0f), Extent(0.39f, 0.05f, 0.29f));

            b.Arm.Origin = Point(0.18f, 0.46f, 0.04f);
            b.Arm.Box(Point(0.1f, 0.44f, 0.22f), Extent(0.06f, 0.06f, 0.42f), wood);
            b.Arm.Box(Point(-0.04f, 0.45f, 0.38f), Extent(0.28f, 0.04f, 0.05f), bronze, Turn(0f, -14f, 0f));
            b.Arm.Box(Point(0.24f, 0.45f, 0.38f), Extent(0.28f, 0.04f, 0.05f), bronze, Turn(0f, 14f, 0f));
            b.Arm.Box(Point(0.1f, 0.47f, 0.32f), Extent(0.48f, 0.012f, 0.012f), BodyPalette.Humans.Canvas);
            b.Arm.Prism(Point(0.1f, 0.48f, 0.42f), 4, 0.025f, 0f, 0.06f, steel, Lying);
        }

        /// <summary>마도사: a robed mage, steel pauldrons, a pointed hood, a tall staff crowned with a large mana crystal.</summary>
        private static void ArcaneMage(Blueprint b)
        {
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, steel = BodyPalette.Humans.Steel;
            Color bronze = BodyPalette.Humans.Bronze, mana = BodyPalette.Humans.Mana;

            Legs(b, 0.14f, 0.08f, 0.08f, BodyPalette.Humans.StoneDark, BodyPalette.Humans.Wood);
            hull.Taper(Point(0f, 0.06f, 0f), Pair(0.5f, 0.42f), Pair(0.28f, 0.24f), 0.52f, stone);
            hull.Box(Point(0f, 0.3f, 0.165f), Extent(0.08f, 0.46f, 0.03f), steel, Turn(-10f, 0f, 0f));
            hull.Box(Point(-0.17f, 0.56f, 0f), Extent(0.14f, 0.08f, 0.26f), steel);
            hull.Box(Point(0.17f, 0.56f, 0f), Extent(0.14f, 0.08f, 0.26f), steel);
            hull.Box(Point(0f, 0.55f, 0.13f), Extent(0.06f, 0.06f, 0.03f), bronze);
            hull.Taper(Point(0f, 0.58f, -0.02f), Pair(0.26f, 0.26f), Pair(0.16f, 0.16f), 0.24f, stone);
            hull.Prism(Point(0f, 0.8f, -0.05f), 4, 0.09f, 0f, 0.24f, steel, Turn(-25f, 0f, 0f));
            hull.Box(Point(0f, 0.67f, 0.1f), Extent(0.14f, 0.13f, 0.05f), BodyPalette.Humans.Hood);
            Strap(b, hull, Point(0f, 0.38f, 0f), Extent(0.34f, 0.05f, 0.3f));

            b.Arm.Origin = Point(0.24f, 0.46f, 0.04f);
            b.Arm.Box(Point(0.25f, 0.42f, 0.06f), Extent(0.08f, 0.1f, 0.08f), stone);
            b.Arm.Box(Point(0.27f, 0.52f, 0.1f), Extent(0.035f, 0.95f, 0.035f), BodyPalette.Humans.Wood);
            b.Arm.Prism(Point(0.27f, 0.98f, 0.1f), 4, 0.04f, 0.07f, 0.06f, bronze);
            b.Arm.Prism(Point(0.27f, 1.08f, 0.1f), 4, 0.08f, 0f, 0.14f, mana);
            b.Arm.Prism(Point(0.27f, 1.08f, 0.1f), 4, 0.08f, 0f, 0.09f, mana, Turn(180f, 0f, 0f));
        }

        /// <summary>석조 보행 포대: a low cut-stone battery on four block legs, a thick front plate, a short cannon on a steel turret.</summary>
        private static void StoneWalker(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, stoneMid = BodyPalette.Humans.StoneMid, steel = BodyPalette.Humans.Steel;
            Color bronze = BodyPalette.Humans.Bronze;

            hull.Box(Point(0f, 0.42f, -0.04f), Extent(0.56f, 0.28f, 0.6f), stone);
            hull.Box(Point(0f, 0.42f, -0.04f), Extent(0.58f, 0.03f, 0.62f), stoneMid);
            hull.Box(Point(0f, 0.42f, 0.29f), Extent(0.62f, 0.34f, 0.08f), stoneMid);
            for (int side = -1; side <= 1; side += 2)
                hull.Box(Point(side * 0.2f, 0.5f, 0.335f), Extent(0.05f, 0.05f, 0.02f), bronze);
            Strap(b, hull, Point(0f, 0.32f, -0.04f), Extent(0.58f, 0.05f, 0.62f));

            FourLegs(b, 0.2f, 0.16f, -0.24f, WalkerLeg);

            b.Arm.Origin = Point(0f, 0.6f, -0.06f);
            b.Arm.Box(Point(0f, 0.64f, -0.06f), Extent(0.34f, 0.14f, 0.34f), steel);
            b.Arm.Prism(Point(0f, 0.68f, 0f), 8, 0.1f, 0.09f, 0.42f, BodyPalette.Humans.StoneDark, Lying);
            b.Arm.Prism(Point(0f, 0.68f, 0.4f), 8, 0.11f, 0.11f, 0.05f, bronze, Lying);
        }

        private static void WalkerLeg(Solids part, float x, float z)
        {
            part.Box(Point(x, 0.16f, z), Extent(0.14f, 0.28f, 0.16f), BodyPalette.Humans.StoneMid);
            part.Box(Point(x, 0.03f, z + 0.02f), Extent(0.17f, 0.06f, 0.2f), BodyPalette.Humans.Steel);
        }

        /// <summary>견인 대포: a long barrel on a wooden carriage between two wheels, a trail bar dragging behind.</summary>
        private static void TowedCannon(Blueprint b)
        {
            // Walk with no legs: the wheels stay on the ground and the cart bobs over it.
            b.Gait = Gait.Walk;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color wood = BodyPalette.Humans.Wood, steel = BodyPalette.Humans.Steel, bronze = BodyPalette.Humans.Bronze;

            for (int side = -1; side <= 1; side += 2)
            {
                hull.Prism(Point(side * 0.26f, 0.24f, 0f), 10, 0.24f, 0.24f, 0.07f, wood, Turn(0f, 0f, -side * 90f));
                hull.Prism(Point(side * 0.32f, 0.24f, 0f), 6, 0.07f, 0.06f, 0.04f, steel, Turn(0f, 0f, -side * 90f));
            }
            hull.Box(Point(0f, 0.24f, 0f), Extent(0.54f, 0.06f, 0.06f), BodyPalette.Humans.SteelDark);
            hull.Box(Point(0f, 0.32f, -0.1f), Extent(0.3f, 0.12f, 0.4f), wood);
            hull.Box(Point(0f, 0.18f, -0.5f), Extent(0.08f, 0.06f, 0.54f), wood, Turn(-16f, 0f, 0f));
            hull.Box(Point(0f, 0.06f, -0.76f), Extent(0.12f, 0.06f, 0.08f), steel);
            Strap(b, hull, Point(0f, 0.32f, -0.22f), Extent(0.32f, 0.13f, 0.06f));

            b.Arm.Origin = Point(0f, 0.4f, -0.1f);
            b.Arm.Box(Point(0f, 0.42f, -0.2f), Extent(0.22f, 0.2f, 0.26f), BodyPalette.Humans.Stone);
            b.Arm.Prism(Point(0f, 0.44f, -0.1f), 10, 0.1f, 0.075f, 0.74f, BodyPalette.Humans.StoneDark, Turn(84f, 0f, 0f));
            b.Arm.Prism(Point(0f, 0.47f, 0.18f), 10, 0.1f, 0.1f, 0.04f, steel, Turn(84f, 0f, 0f));
            b.Arm.Prism(Point(0f, 0.51f, 0.6f), 10, 0.095f, 0.095f, 0.05f, bronze, Turn(84f, 0f, 0f));
        }

        /// <summary>마도 폭격선: a stone-and-steel hull on two wide straight wings, a closed bomb bay underneath, a tail fin. Airborne.</summary>
        private static void ArcaneBomber(Blueprint b)
        {
            b.Limbs = LimbMotion.Flap;
            b.LimbsRideHull = true;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, steel = BodyPalette.Humans.Steel, bronze = BodyPalette.Humans.Bronze;

            hull.Prism(Point(0f, 0.42f, -0.48f), 8, 0.12f, 0.2f, 0.32f, steel, Lying);
            hull.Prism(Point(0f, 0.42f, -0.16f), 8, 0.2f, 0.2f, 0.4f, stone, Lying);
            hull.Prism(Point(0f, 0.42f, 0.24f), 8, 0.2f, 0.08f, 0.22f, steel, Lying);
            hull.Prism(Point(0f, 0.42f, 0.45f), 8, 0.06f, 0.06f, 0.03f, BodyPalette.Humans.Mana, Lying);
            hull.Prism(Point(0f, 0.42f, -0.18f), 8, 0.21f, 0.21f, 0.04f, bronze, Lying);
            hull.Prism(Point(0f, 0.42f, 0.18f), 8, 0.21f, 0.21f, 0.04f, bronze, Lying);
            hull.Box(Point(0f, 0.22f, 0f), Extent(0.18f, 0.08f, 0.3f), BodyPalette.Humans.StoneDark);
            hull.Box(Point(0f, 0.18f, 0f), Extent(0.02f, 0.01f, 0.28f), bronze);
            hull.Box(Point(0f, 0.6f, -0.46f), Extent(0.03f, 0.2f, 0.18f), steel);
            hull.Box(Point(0f, 0.44f, -0.54f), Extent(0.42f, 0.03f, 0.12f), bronze);
            Strap(b, hull, Point(0f, 0.62f, -0.02f), Extent(0.16f, 0.03f, 0.2f));

            for (int side = -1; side <= 1; side += 2)
            {
                Solids wing = side < 0 ? b.LimbLeft : b.LimbRight;
                Wing(wing, side, Point(0.18f, 0.44f, 0f), 0.52f, 0.24f, 0.18f, 0f, 4f, steel);
                wing.Box(Point(side * 0.44f, 0.46f, 0.12f), Extent(0.52f, 0.03f, 0.03f), bronze);
            }
        }

        /// <summary>마도 기수: a rider in steel on a four-legged stone mount with a lens for a head, a short gun levelled forward.</summary>
        private static void ArcaneRider(Blueprint b)
        {
            b.Limbs = LimbMotion.Step;
            b.Strike = Strike.Recoil;
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, stoneMid = BodyPalette.Humans.StoneMid, steel = BodyPalette.Humans.Steel;

            hull.Box(Point(0f, 0.42f, 0f), Extent(0.4f, 0.2f, 0.62f), stone);
            hull.Box(Point(0f, 0.43f, 0f), Extent(0.42f, 0.04f, 0.3f), steel);
            hull.Box(Point(0f, 0.5f, 0.38f), Extent(0.22f, 0.2f, 0.2f), stoneMid);
            hull.Prism(Point(0f, 0.52f, 0.48f), 8, 0.06f, 0.06f, 0.03f, BodyPalette.Humans.Mana, Lying);
            hull.Box(Point(0f, 0.64f, -0.06f), Extent(0.22f, 0.24f, 0.16f), steel);
            hull.Box(Point(0f, 0.84f, -0.06f), Extent(0.15f, 0.15f, 0.15f), stone);
            hull.Box(Point(0f, 0.83f, 0.01f), Extent(0.12f, 0.09f, 0.03f), BodyPalette.Humans.Hood);
            Strap(b, hull, Point(0f, 0.72f, -0.06f), Extent(0.24f, 0.05f, 0.18f));

            FourLegs(b, 0.16f, 0.2f, -0.22f, RiderLeg);

            b.Arm.Origin = Point(0.14f, 0.66f, 0f);
            b.Arm.Box(Point(0.15f, 0.64f, 0.04f), Extent(0.07f, 0.08f, 0.12f), steel);
            b.Arm.Prism(Point(0.15f, 0.66f, 0f), 6, 0.04f, 0.04f, 0.36f, BodyPalette.Humans.StoneDark, Lying);
            b.Arm.Prism(Point(0.15f, 0.66f, 0.34f), 6, 0.05f, 0.05f, 0.04f, BodyPalette.Humans.Bronze, Lying);
        }

        private static void RiderLeg(Solids part, float x, float z)
        {
            part.Box(Point(x, 0.17f, z), Extent(0.1f, 0.3f, 0.12f), BodyPalette.Humans.StoneMid);
            part.Box(Point(x, 0.3f, z), Extent(0.12f, 0.06f, 0.14f), BodyPalette.Humans.Steel);
            part.Box(Point(x, 0.03f, z + 0.02f), Extent(0.13f, 0.06f, 0.16f), BodyPalette.Humans.SteelDark);
        }

        /// <summary>공방 기술자: a capped engineer in a canvas apron, a tool box at the hip, a big wrench in hand. Weaponless.</summary>
        private static void WorkshopEngineer(Blueprint b)
        {
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color steel = BodyPalette.Humans.Steel, wood = BodyPalette.Humans.Wood, bronze = BodyPalette.Humans.Bronze;

            Legs(b, 0.22f, 0.09f, 0.1f, BodyPalette.Humans.SteelDark, wood);
            hull.Taper(Point(0f, 0.2f, 0f), Pair(0.38f, 0.28f), Pair(0.38f, 0.26f), 0.36f, steel);
            hull.Box(Point(0f, 0.34f, 0.14f), Extent(0.26f, 0.34f, 0.03f), BodyPalette.Humans.Canvas);
            hull.Box(Point(0f, 0.68f, 0.02f), Extent(0.22f, 0.18f, 0.2f), BodyPalette.Humans.StoneMid);
            hull.Box(Point(0f, 0.79f, 0.02f), Extent(0.26f, 0.06f, 0.24f), bronze);
            hull.Box(Point(0f, 0.79f, 0.16f), Extent(0.2f, 0.03f, 0.08f), bronze);
            hull.Box(Point(0f, 0.66f, 0.12f), Extent(0.16f, 0.1f, 0.03f), BodyPalette.Humans.Hood);
            hull.Box(Point(-0.29f, 0.26f, 0.04f), Extent(0.14f, 0.14f, 0.24f), wood);
            hull.Box(Point(-0.29f, 0.34f, 0.04f), Extent(0.15f, 0.03f, 0.25f), bronze);
            Strap(b, hull, Point(0f, 0.25f, 0f), Extent(0.4f, 0.05f, 0.29f));

            b.Arm.Origin = Point(0.24f, 0.5f, 0.04f);
            b.Arm.Box(Point(0.26f, 0.42f, 0.06f), Extent(0.08f, 0.1f, 0.08f), steel);
            b.Arm.Box(Point(0.27f, 0.48f, 0.14f), Extent(0.05f, 0.46f, 0.05f), BodyPalette.Humans.StoneDark, Turn(25f, 0f, 0f));
            b.Arm.Box(Point(0.27f, 0.72f, 0.24f), Extent(0.07f, 0.08f, 0.2f), BodyPalette.Humans.Stone, Turn(25f, 0f, 0f));
            b.Arm.Box(Point(0.27f, 0.8f, 0.2f), Extent(0.07f, 0.1f, 0.05f), BodyPalette.Humans.Stone, Turn(25f, 0f, 0f));
            b.Arm.Box(Point(0.27f, 0.76f, 0.34f), Extent(0.07f, 0.1f, 0.05f), BodyPalette.Humans.Stone, Turn(25f, 0f, 0f));
        }

        /// <summary>마나 증폭기: a wheeled steel cart carrying a stone pillar, a guarded crystal and two antennas. Weaponless.</summary>
        private static void ManaAmplifier(Blueprint b)
        {
            // Walk with no legs: the wheels stay on the ground and the cart bobs over it.
            b.Gait = Gait.Walk;
            b.Strike = Strike.Pulse;
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, steel = BodyPalette.Humans.Steel, bronze = BodyPalette.Humans.Bronze;
            Color mana = BodyPalette.Humans.Mana;

            hull.Box(Point(0f, 0.24f, 0f), Extent(0.56f, 0.18f, 0.5f), steel);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.29f : 0.29f, z = i < 2 ? 0.16f : -0.16f;
                hull.Prism(Point(x, 0.12f, z), 8, 0.12f, 0.12f, 0.06f, BodyPalette.Humans.StoneDark, Turn(0f, 0f, x < 0f ? 90f : -90f));
            }
            hull.Box(Point(0f, 0.6f, 0f), Extent(0.3f, 0.54f, 0.3f), stone);
            hull.Box(Point(0f, 0.45f, 0f), Extent(0.32f, 0.04f, 0.32f), bronze);
            hull.Box(Point(0f, 0.6f, 0.155f), Extent(0.14f, 0.2f, 0.02f), mana);
            hull.Box(Point(0f, 0.6f, 0.165f), Extent(0.02f, 0.22f, 0.02f), bronze);
            hull.Box(Point(0f, 0.88f, 0f), Extent(0.34f, 0.04f, 0.34f), BodyPalette.Humans.StoneMid);
            hull.Prism(Point(0f, 0.9f, 0f), 4, 0.08f, 0f, 0.18f, mana);
            for (int side = -1; side <= 1; side += 2)
            {
                hull.Box(Point(side * 0.24f, 0.72f, 0f), Extent(0.04f, 0.6f, 0.04f), steel, Turn(0f, 0f, -side * 8f));
                hull.Box(Point(side * 0.28f, 1.02f, 0f), Extent(0.08f, 0.08f, 0.08f), bronze);
            }
            Strap(b, hull, Point(0f, 0.3f, 0f), Extent(0.58f, 0.05f, 0.52f));
        }

        // ------------------------------------------------------------------
        // Buildings. Authored in world units: a footprint of F cells is F units
        // wide. Heights follow issue #159's brief — Base about 3.2, Production
        // 2.4, Defense about 3.0, Supply about 1.6, Tech about 2.2 — except where
        // FACTIONS.md describes a concept that is low by nature (a nest, a mound,
        // a fissure), and there the concept wins.
        // ------------------------------------------------------------------

        // A building stands unturned and the default camera (yaw 0) looks from -z,
        // so every builder, old and new, puts its door, window, flag or mouth on -z.
        private static void Building(Blueprint b, Faction faction, Role role, int slot)
        {
            Dress(b, faction);
            if (slot > 0 && ExtraBuilding(b, faction, role, slot)) return;
            if (!FactionBuilding(b, faction, role)) GenericBuilding(b, faction, Cells(role));
        }

        /// <summary>True when entry 0 of this slot has a building written for it, now drawn into <paramref name="b"/>.</summary>
        private static bool FactionBuilding(Blueprint b, Faction faction, Role role)
        {
            switch (faction)
            {
                case Faction.TreeSpirits: return TreeSpiritBuilding(b, role);
                case Faction.Hellfire: return HellfireBuilding(b, role);
                case Faction.WaterSlimes: return WaterSlimeBuilding(b, role);
                case Faction.RockGolems: return RockGolemBuilding(b, role);
                case Faction.Driftworlds: return DriftworldBuilding(b, role);
                default: return HumanBuilding(b, role);
            }
        }

        /// <summary>
        /// The buildings behind entry 0 of a slot (issue #195): one builder per
        /// roster row, each in its faction's shape language. False for a row no
        /// builder was written for, which then stands as entry 0 of its slot.
        /// </summary>
        private static bool ExtraBuilding(Blueprint b, Faction faction, Role role, int slot)
        {
            switch (faction)
            {
                case Faction.TreeSpirits: return TreeSpiritExtraBuilding(b, role, slot);
                case Faction.Hellfire: return HellfireExtraBuilding(b, role, slot);
                case Faction.WaterSlimes: return WaterSlimeExtraBuilding(b, role, slot);
                case Faction.RockGolems: return RockGolemExtraBuilding(b, role, slot);
                case Faction.Driftworlds: return DriftworldExtraBuilding(b, role, slot);
                case Faction.Humans: return HumanExtraBuilding(b, role, slot);
                default: return false;
            }
        }

        /// <summary>
        /// What a faction's buildings do beyond their shape (issue #186): how a
        /// site goes up, what colour its rubble and dust are, what rises off it
        /// while it works, and whether a light inside goes out when it falls.
        /// The light is the two factions that draw one in their buildings: the
        /// ember in 지옥불's rifts and the other world in 유랑종's gaps.
        /// </summary>
        private static void Dress(Blueprint b, Faction faction)
        {
            b.Assembled = true;
            b.Mote = BodyPalette.Shot(faction);
            switch (faction)
            {
                case Faction.TreeSpirits:
                    b.Assembly = Assembly.Grow;
                    b.Debris = BodyPalette.TreeSpirits.Bark;
                    break;
                case Faction.Hellfire:
                    b.Assembly = Assembly.Tear;
                    b.Debris = BodyPalette.Hellfire.Shell;
                    b.Glow = BodyPalette.Hellfire.Ember;
                    break;
                case Faction.WaterSlimes:
                    b.Assembly = Assembly.Inflate;
                    b.Debris = BodyPalette.WaterSlimes.Sky;
                    break;
                case Faction.RockGolems:
                    b.Assembly = Assembly.Stack;
                    b.Debris = BodyPalette.RockGolems.Tan;
                    // Tan motes on tan stone vanish; the warm light in its cracks does not.
                    b.Mote = BodyPalette.RockGolems.Waking;
                    break;
                case Faction.Driftworlds:
                    b.Assembly = Assembly.Close;
                    b.Debris = BodyPalette.Driftworlds.Shell;
                    b.Glow = BodyPalette.Driftworlds.TealLight;
                    break;
                default:
                    b.Assembly = Assembly.Floors;
                    b.Debris = BodyPalette.Humans.Stone;
                    break;
            }
        }

        /// <summary>A building no model was written for: a block and a cap in the faction's main colours.</summary>
        private static void GenericBuilding(Blueprint b, Faction faction, float footprint)
        {
            float width = footprint * 0.7f;
            b.Hull.Box(Point(0f, width * 0.45f, 0f), Extent(width, width * 0.9f, width), BodyPalette.Main(faction));
            b.Hull.Prism(Point(0f, width * 0.9f, 0f), 4, width * 0.78f, 0f, width * 0.5f, BodyPalette.Second(faction));
            Strap(b, b.Hull, Point(0f, width * 0.15f, 0f), Extent(width * 1.04f, 0.1f, width * 1.04f));
        }

        // ---- 세계수 정령 ----

        private static bool TreeSpiritBuilding(Blueprint b, Role role)
        {
            Solids hull = b.Hull;
            Color bark = BodyPalette.TreeSpirits.Bark, barkShade = BodyPalette.TreeSpirits.BarkShade;
            Color leaf = BodyPalette.TreeSpirits.Leaf, leafLight = BodyPalette.TreeSpirits.LeafLight;
            Color leafShade = BodyPalette.TreeSpirits.LeafShade, leafDeep = BodyPalette.TreeSpirits.LeafDeep;

            switch (role)
            {
                case Role.Base:
                    // 생명의 나무: flared roots, a thick trunk with a glowing knot, a broad canopy.
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = 45f + i * 90f;
                        hull.Taper(Around(yaw) * 1.0f, Pair(0.42f, 0.6f), Pair(0.16f, 0.2f), 0.9f, barkShade, Outward(yaw, -40f));
                    }
                    hull = b.Tier(Vector3.zero);
                    hull.Prism(Vector3.zero, 7, 0.55f, 0.4f, 1.9f, bark);
                    hull.Taper(Point(-0.25f, 1.6f, 0f), Pair(0.3f, 0.3f), Pair(0.14f, 0.14f), 0.7f, bark, Turn(0f, 0f, 35f));
                    hull.Taper(Point(0.25f, 1.6f, 0f), Pair(0.3f, 0.3f), Pair(0.14f, 0.14f), 0.7f, bark, Turn(0f, 0f, -35f));
                    hull.Ball(Point(0.12f, 0.9f, -0.43f), Extent(0.12f, 0.14f, 0.08f), BodyPalette.TreeSpirits.Bolt);
                    Band(b, hull, Point(0f, 0.45f, 0f), 7, 0.545f, 0.16f);
                    hull = b.Tier(Point(0f, 1.85f, 0f));
                    hull.Ball(Point(0f, 2.5f, 0f), Extent(1.25f, 0.72f, 1.15f), leaf);
                    hull.Ball(Point(-0.85f, 2.25f, 0.25f), Extent(0.7f, 0.5f, 0.65f), leafShade);
                    hull.Ball(Point(0.85f, 2.3f, -0.1f), Extent(0.75f, 0.5f, 0.7f), leaf);
                    hull.Ball(Point(0.15f, 2.92f, 0.1f), Extent(0.75f, 0.42f, 0.7f), leafLight);
                    if (b.HasAccent) hull.Ball(Point(0.35f, 2.62f, -1.08f), 0.24f, b.Accent);
                    break;

                case Role.Production:
                    // 풀씨 둥지: a ring of leaves round a bud, three tall leaves rising from it.
                    hull.Prism(Vector3.zero, 9, 1.0f, 0.95f, 0.14f, leafDeep);
                    for (int i = 0; i < 9; i++)
                    {
                        float yaw = i * 40f;
                        hull.Ball(Around(yaw) * 0.95f + Point(0f, 0.38f, 0f), Extent(0.42f, 0.34f, 0.36f),
                                  i % 2 == 0 ? leaf : leafShade, Quaternion.Euler(0f, yaw, 0f) * Turn(15f, 0f, 0f), 1);
                    }
                    Band(b, hull, Point(0f, 0.1f, 0f), 12, 1.38f, 0.12f);
                    hull = b.Tier(Point(0f, 0.13f, 0f));
                    hull.Ball(Point(0f, 0.55f, 0f), Extent(0.38f, 0.42f, 0.38f), leafLight);
                    hull = b.Tier(Point(0f, 0.75f, 0f));
                    hull.Ball(Point(-0.3f, 1.35f, 0.05f), Extent(0.12f, 0.62f, 0.24f), leaf, Turn(0f, 0f, 22f), 1);
                    hull.Ball(Point(0.32f, 1.3f, -0.05f), Extent(0.12f, 0.58f, 0.24f), leafShade, Turn(0f, 0f, -24f), 1);
                    hull.Ball(Point(0f, 1.6f, -0.25f), Extent(0.12f, 0.75f, 0.26f), leafLight, Turn(-14f, 0f, 0f), 1);
                    break;

                case Role.Defense:
                {
                    // 정령 뇌우목: a twisted bundle of dry branches whose tips face each other over a pale arc.
                    hull.Dome(Vector3.zero, Extent(0.7f, 0.28f, 0.65f), 9, 2, leafDeep);
                    hull = b.Tier(Vector3.zero);
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = 20f + i * 90f;
                        Vector3 start = Around(yaw) * 0.34f;
                        Quaternion lower = Outward(yaw, -7f);
                        hull.Taper(start, Pair(0.3f, 0.3f), Pair(0.2f, 0.2f), 1.5f, i % 2 == 0 ? bark : barkShade, lower);
                        Vector3 knee = start + lower * Point(0f, 1.5f, 0f);
                        hull.Taper(knee, Pair(0.2f, 0.2f), Pair(0.07f, 0.07f), 1.05f, bark, Outward(yaw, 24f));
                    }
                    hull.Lump(Point(0.3f, 1.1f, 0.25f), Extent(0.18f, 0.12f, 0.15f), leaf, Turn(0f, 30f, 0f));
                    Band(b, hull, Point(0f, 0.5f, 0f), 8, 0.52f, 0.14f);

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 2.55f, 0f);
                    Color arc = BodyPalette.TreeSpirits.Arc;
                    crown.Ball(Point(0f, 2.6f, 0f), 0.18f, arc);
                    for (int j = 0; j < 4; j++)
                    {
                        float yaw = 20f + j * 90f;
                        Vector3 direction = Around(yaw);
                        crown.Box(Point(0f, 2.62f, 0f) + direction * 0.22f, Extent(0.05f, 0.05f, 0.24f), arc,
                                  Quaternion.Euler(0f, yaw, 0f) * Turn(-25f, 0f, 0f));
                        crown.Box(Point(0f, 2.62f, 0f) + direction * 0.42f, Extent(0.05f, 0.05f, 0.22f), arc,
                                  Quaternion.Euler(0f, yaw, 0f) * Turn(25f, 0f, 0f));
                    }
                    b.CrownSpin = 45f;
                    break;
                }

                case Role.Supply:
                    // 묘목: a faceted sapling on a moss pad.
                    hull.Dome(Vector3.zero, Extent(0.5f, 0.14f, 0.45f), 8, 2, leafDeep);
                    hull = b.Tier(Vector3.zero);
                    hull.Prism(Vector3.zero, 5, 0.15f, 0.09f, 0.9f, bark, Turn(0f, 0f, -5f));
                    Band(b, hull, Point(0f, 0.2f, 0f), 5, 0.165f, 0.12f);
                    hull = b.Tier(Point(0f, 0.75f, 0f));
                    hull.Lump(Point(-0.05f, 1.15f, 0f), Extent(0.6f, 0.48f, 0.56f), leaf, Turn(10f, 30f, 0f));
                    hull.Lump(Point(0.25f, 1.38f, 0.1f), Extent(0.36f, 0.3f, 0.34f), leafLight, Turn(0f, 70f, 15f));
                    break;

                case Role.Tech:
                    // 뿌리 회당: roots arching in to a leafy cap over a glowing core.
                    hull.Prism(Vector3.zero, 8, 0.85f, 0.82f, 0.1f, leafDeep);
                    hull.Ball(Point(0f, 0.48f, 0f), 0.3f, BodyPalette.TreeSpirits.Bolt);
                    Band(b, hull, Point(0f, 0.08f, 0f), 8, 0.88f, 0.1f);
                    hull = b.Tier(Vector3.zero);
                    for (int i = 0; i < 6; i++)
                    {
                        float yaw = i * 60f;
                        hull.Taper(Around(yaw) * 0.78f, Pair(0.24f, 0.22f), Pair(0.1f, 0.1f), 1.55f,
                                   i % 2 == 0 ? bark : barkShade, Outward(yaw, -30f));
                    }
                    hull = b.Tier(Point(0f, 1.2f, 0f));
                    hull.Ball(Point(0f, 1.5f, 0f), Extent(0.45f, 0.32f, 0.45f), leaf);
                    hull.Prism(Point(0f, 1.7f, 0f), 5, 0.13f, 0f, 0.5f, leafLight);
                    break;

                default:
                    return false;
            }
            return true;
        }

        private static bool TreeSpiritExtraBuilding(Blueprint b, Role role, int slot)
        {
            Solids hull = b.Hull;
            Color bark = BodyPalette.TreeSpirits.Bark, barkShade = BodyPalette.TreeSpirits.BarkShade;
            Color leaf = BodyPalette.TreeSpirits.Leaf, leafLight = BodyPalette.TreeSpirits.LeafLight;
            Color leafShade = BodyPalette.TreeSpirits.LeafShade, leafDeep = BodyPalette.TreeSpirits.LeafDeep;

            switch (role)
            {
                case Role.Production when slot == 1:
                    // 씨앗 요람: root arcs curling up and out round a bowl of leaves, one cream seed in it.
                    hull.Prism(Vector3.zero, 9, 1.1f, 1.05f, 0.14f, leafDeep);
                    Band(b, hull, Point(0f, 0.02f, 0f), 12, 1.12f, 0.08f);
                    hull = b.Tier(Vector3.zero);
                    for (int i = 0; i < 6; i++)
                    {
                        float yaw = 30f + i * 60f;
                        Vector3 foot = Around(yaw) * 0.62f;
                        Quaternion lower = Outward(yaw, 30f);
                        hull.Taper(foot, Pair(0.3f, 0.32f), Pair(0.18f, 0.2f), 0.85f, i % 2 == 0 ? bark : barkShade, lower);
                        Vector3 knee = foot + lower * Point(0f, 0.85f, 0f);
                        hull.Taper(knee, Pair(0.18f, 0.2f), Pair(0.08f, 0.1f), 0.45f, bark, Outward(yaw, 80f));
                        hull.Ball(knee + Around(yaw) * 0.45f + Point(0f, -0.08f, 0f), 0.1f, barkShade);
                    }
                    hull = b.Tier(Point(0f, 0.4f, 0f));
                    for (int i = 0; i < 8; i++)
                    {
                        float yaw = i * 45f;
                        hull.Ball(Around(yaw) * 0.5f + Point(0f, 0.72f, 0f), Extent(0.16f, 0.06f, 0.42f), i % 2 == 0 ? leaf : leafLight,
                                  Quaternion.Euler(0f, yaw, 0f) * Turn(-32f, 0f, 0f), 1);
                    }
                    hull = b.Tier(Point(0f, 0.7f, 0f));
                    hull.Ball(Point(0f, 1.08f, 0f), Extent(0.4f, 0.48f, 0.4f), BodyPalette.TreeSpirits.BoltLight);
                    hull.Prism(Point(0f, 1.45f, 0f), 6, 0.17f, 0f, 0.3f, BodyPalette.TreeSpirits.BoltLight);
                    return true;

                case Role.Defense when slot == 1:
                    // 회복 토템: a carved trunk opening into a cup of wooden petals round a green orb. Unarmed, so no crown.
                    hull.Dome(Vector3.zero, Extent(0.62f, 0.2f, 0.56f), 8, 2, leafDeep);
                    Band(b, hull, Point(0f, 0.04f, 0f), 8, 0.63f, 0.08f);
                    hull = b.Tier(Vector3.zero);
                    for (int i = 0; i < 3; i++)
                    {
                        float yaw = 60f + i * 120f;
                        hull.Taper(Around(yaw) * 0.18f, Pair(0.22f, 0.26f), Pair(0.08f, 0.1f), 0.5f, barkShade, Outward(yaw, -55f));
                    }
                    hull.Prism(Vector3.zero, 7, 0.28f, 0.2f, 1.75f, bark, Turn(0f, 0f, -3f));
                    hull.Prism(Point(0f, 0.7f, 0f), 7, 0.27f, 0.27f, 0.08f, barkShade);
                    hull = b.Tier(Point(0f, 1.65f, 0f));
                    for (int i = 0; i < 5; i++)
                    {
                        float yaw = i * 72f;
                        hull.Taper(Around(yaw) * 0.1f + Point(0f, 1.62f, 0f), Pair(0.3f, 0.08f), Pair(0.4f, 0.05f), 0.6f,
                                   i % 2 == 0 ? bark : barkShade, Outward(yaw, 52f));
                    }
                    hull = b.Tier(Point(0f, 1.75f, 0f));
                    hull.Ball(Point(0f, 2.05f, 0f), 0.33f, leafLight);
                    hull.Ball(Point(-0.12f, 2.18f, 0.2f), 0.08f, BodyPalette.TreeSpirits.BoltLight);
                    return true;

                case Role.Tech when slot == 1:
                {
                    // 바람 토템: a ringed post with two wind-coloured leaf wings, a pinwheel of vanes turning on top.
                    hull.Prism(Vector3.zero, 8, 0.62f, 0.6f, 0.12f, leafDeep);
                    Band(b, hull, Point(0f, 0.03f, 0f), 8, 0.63f, 0.08f);
                    hull = b.Tier(Vector3.zero);
                    hull.Prism(Point(0f, 0.1f, 0f), 8, 0.3f, 0.26f, 1.45f, bark);
                    hull.Prism(Point(0f, 0.45f, 0f), 8, 0.32f, 0.32f, 0.08f, barkShade);
                    hull.Prism(Point(0f, 1.15f, 0f), 8, 0.29f, 0.29f, 0.08f, barkShade);
                    hull.Ball(Point(0f, 0.85f, -0.28f), Extent(0.08f, 0.11f, 0.05f), BodyPalette.TreeSpirits.WindDeep);
                    hull = b.Tier(Point(0f, 1.2f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        hull.Taper(Point(side * 0.24f, 1.22f, 0f), Pair(0.05f, 0.42f), Pair(0.04f, 0.22f), 0.72f,
                                   side < 0 ? BodyPalette.TreeSpirits.Wind : BodyPalette.TreeSpirits.WindLight, Turn(0f, 0f, -side * 62f));
                        hull.Ball(Point(side * 0.62f, 1.25f, 0.05f), Extent(0.04f, 0.1f, 0.04f), BodyPalette.TreeSpirits.WindDeep);
                        hull.Ball(Point(side * 0.45f, 1.15f, 0.05f), Extent(0.04f, 0.09f, 0.04f), leafLight);
                    }

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 1.55f, 0f);
                    crown.Prism(Point(0f, 1.55f, 0f), 6, 0.09f, 0.06f, 0.42f, barkShade);
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = i * 90f;
                        crown.Box(Around(yaw) * 0.22f + Point(0f, 1.9f, 0f), Extent(0.06f, 0.2f, 0.34f),
                                  i % 2 == 0 ? BodyPalette.TreeSpirits.WindLight : BodyPalette.TreeSpirits.Wind,
                                  Quaternion.Euler(0f, yaw, 0f) * Turn(0f, 0f, 35f));
                    }
                    crown.Ball(Point(0f, 1.98f, 0f), 0.08f, BodyPalette.TreeSpirits.WindDeep);
                    b.CrownSpin = 70f;
                    return true;
                }

                case Role.Tech when slot == 2:
                {
                    // 광란의 토템: three carved drums stacked into a pole of faces, a ring of bolts crackling round the top.
                    hull.Prism(Vector3.zero, 8, 0.6f, 0.58f, 0.1f, leafDeep);
                    Band(b, hull, Point(0f, 0.03f, 0f), 8, 0.61f, 0.08f);
                    float[] bottoms = { 0.1f, 0.62f, 1.1f };
                    float[] radii = { 0.42f, 0.36f, 0.3f };
                    float[] heights = { 0.5f, 0.46f, 0.42f };
                    for (int i = 0; i < 3; i++)
                    {
                        hull = b.Tier(Point(0f, bottoms[i], 0f));
                        hull.Prism(Point(0f, bottoms[i], 0f), 6, radii[i], radii[i] * 0.9f, heights[i], i == 1 ? barkShade : bark, Turn(0f, i * 12f, 0f));
                        // On the camera's side, -z, so the faces are seen.
                        float face = -radii[i] * 0.86f;
                        Eyes(hull, Point(0f, bottoms[i] + heights[i] * 0.62f, face), radii[i] * 0.55f, 0.06f, -1f);
                        hull.Box(Point(0f, bottoms[i] + heights[i] * 0.26f, face), Extent(radii[i] * 0.6f, 0.05f, 0.05f), BodyPalette.Eye);
                    }
                    hull.Prism(Point(-0.36f, 0.85f, 0f), 4, 0.06f, 0f, 0.3f, BodyPalette.TreeSpirits.BoltDeep, Turn(0f, 0f, 55f));
                    hull.Prism(Point(0.36f, 0.85f, 0f), 4, 0.06f, 0f, 0.3f, BodyPalette.TreeSpirits.BoltDeep, Turn(0f, 0f, -55f));

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 1.52f, 0f);
                    crown.Ball(Point(0f, 1.7f, 0f), 0.12f, BodyPalette.TreeSpirits.BoltDeep);
                    for (int i = 0; i < 6; i++)
                    {
                        float yaw = i * 60f;
                        crown.Box(Around(yaw) * 0.3f + Point(0f, 1.72f, 0f), Extent(0.06f, 0.24f, 0.06f),
                                  i % 2 == 0 ? BodyPalette.TreeSpirits.Bolt : BodyPalette.TreeSpirits.BoltLight,
                                  Quaternion.Euler(0f, yaw, 0f) * Turn(0f, 0f, i % 2 == 0 ? 28f : -28f));
                    }
                    b.CrownSpin = 90f;
                    return true;
                }

                default:
                    return false;
            }
        }

        // ---- 지옥불 군단 ----

        private static bool HellfireBuilding(Blueprint b, Role role)
        {
            Solids hull = b.Hull;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember, ash = BodyPalette.Hellfire.Ash;

            switch (role)
            {
                case Role.Base:
                    // 균열 제단: two shell horns arching over a rift. A wound, not architecture: no door, no flag.
                    hull.Taper(Vector3.zero, Pair(3.0f, 2.2f), Pair(2.6f, 1.8f), 0.3f, charcoal);
                    for (int i = 0; i < 6; i++)
                    {
                        float yaw = 15f + i * 60f;
                        hull.Prism(Point(Around(yaw).x * 1.3f, 0.2f, Around(yaw).z * 0.95f), 4, 0.16f, 0f, 0.5f + (i % 3) * 0.15f,
                                   i % 2 == 0 ? charcoal : facet, Outward(yaw, 20f));
                    }
                    Strap(b, hull, Point(0f, 0.3f, 0f), Extent(2.66f, 0.08f, 1.86f));
                    hull = b.Tier(Point(0f, 0.05f, 0f));
                    hull.Box(Point(0f, 1.0f, 0f), Extent(1.3f, 1.9f, 0.12f), ember);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        hull = b.Tier(Point(side * 1.1f, 0.2f, 0f));
                        Vector3 foot = Point(side * 1.1f, 0.2f, 0f);
                        Quaternion lower = Turn(0f, 0f, side * 12f);
                        hull.Taper(foot, Pair(0.9f, 1.0f), Pair(0.6f, 0.7f), 1.6f, side < 0 ? shell : charcoal, lower);
                        Vector3 knee = foot + lower * Point(0f, 1.6f, 0f);
                        hull.Taper(knee, Pair(0.6f, 0.7f), Pair(0.15f, 0.25f), 1.4f, side < 0 ? facet : shell, Turn(0f, 0f, side * 48f));
                    }
                    Shard(b, hull, Point(0f, 2.95f, 0f), 0.22f);
                    break;

                case Role.Production:
                    // 악마 산란장: a low wide shell sac, ridged, split along the bottom.
                    hull.Box(Point(0f, 0.18f, -1.08f), Extent(1.5f, 0.06f, 0.2f), ember);
                    Band(b, hull, Point(0f, 0.04f, 0f), 9, 1.33f, 0.1f, 1.15f / 1.3f);
                    hull = b.Tier(Vector3.zero);
                    hull.Dome(Vector3.zero, Extent(1.3f, 1.1f, 1.15f), 9, 3, shell);
                    hull = b.Tier(Point(0f, 0.6f, 0f));
                    for (int i = -1; i <= 1; i++)
                        hull.Taper(Point(i * 0.45f, 0.75f, -0.1f), Pair(0.18f, 1.4f), Pair(0.08f, 1.0f), 0.38f, facet, Turn(0f, 0f, -i * 25f));
                    hull.Prism(Point(-0.2f, 1.0f, 0.1f), 4, 0.14f, 0f, 1.2f, charcoal, Turn(-8f, 0f, 10f));
                    hull.Prism(Point(0.35f, 0.95f, -0.1f), 4, 0.12f, 0f, 0.9f, charcoal, Turn(6f, 0f, -14f));
                    break;

                case Role.Defense:
                    // 용암 아가리: a half-buried shell jaw. The upper jaw is the crown and gapes when it fires.
                    hull.Taper(Vector3.zero, Pair(1.6f, 1.5f), Pair(1.0f, 0.9f), 0.9f, charcoal);
                    hull.Box(Point(0f, 0.92f, -0.1f), Extent(0.85f, 0.06f, 0.6f), ember);
                    Strap(b, hull, Point(0f, 0.45f, 0f), Extent(1.34f, 0.1f, 1.24f));

                    b.Crown.Origin = Point(0f, 0.9f, 0.4f);
                    b.Crown.Taper(Point(0f, 0.9f, 0f), Pair(1.1f, 1.0f), Pair(0.2f, 0.3f), 1.7f, shell, Quaternion.identity, Pair(0f, 0.3f));
                    b.Crown.Taper(Point(0f, 1.4f, 0.05f), Pair(0.6f, 0.5f), Pair(0.1f, 0.15f), 1.0f, facet, Quaternion.identity, Pair(0f, 0.2f));
                    for (int i = 0; i < 4; i++)
                        b.Crown.Prism(Point(-0.33f + i * 0.22f, 0.92f, -0.42f), 4, 0f, 0.07f, 0.17f, ash, Turn(180f, 0f, 0f));
                    b.CrownKick = 24f;
                    break;

                case Role.Supply:
                    // 갈라진 틈: jagged plates heaved up either side of a glowing crack.
                    hull.Box(Point(0f, 0.04f, 0f), Extent(0.16f, 0.1f, 1.3f), ember);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        hull = b.Tier(Point(side * 0.32f, 0f, 0f));
                        hull.Taper(Point(side * 0.32f, 0f, 0.15f), Pair(0.45f, 0.7f), Pair(0.1f, 0.3f), 1.3f, side < 0 ? shell : charcoal, Turn(0f, 0f, -side * 14f));
                        hull.Taper(Point(side * 0.3f, 0f, -0.42f), Pair(0.4f, 0.5f), Pair(0.08f, 0.2f), 0.9f, side < 0 ? charcoal : facet, Turn(0f, 15f, -side * 22f));
                    }
                    Shard(b, hull, Point(0f, 1.45f, 0.1f), 0.12f);
                    Strap(b, b.Tiers[0], Point(-0.32f, 0.3f, 0.15f), Extent(0.42f, 0.08f, 0.66f), Turn(0f, 0f, 14f));
                    break;

                case Role.Tech:
                    // 용암 도가니: stacked shell rings with ember seams, a jagged rim round a lava pool.
                    hull.Prism(Vector3.zero, 6, 0.7f, 0.6f, 0.5f, shell);
                    hull.Prism(Point(0f, 0.5f, 0f), 6, 0.58f, 0.58f, 0.05f, ember);
                    Band(b, hull, Point(0f, 0.15f, 0f), 6, 0.69f, 0.1f);
                    hull = b.Tier(Point(0f, 0.55f, 0f));
                    hull.Prism(Point(0f, 0.55f, 0f), 6, 0.62f, 0.5f, 0.5f, charcoal, Turn(0f, 15f, 0f));
                    hull.Prism(Point(0f, 1.05f, 0f), 6, 0.49f, 0.49f, 0.05f, ember);
                    hull = b.Tier(Point(0f, 1.1f, 0f));
                    hull.Prism(Point(0f, 1.1f, 0f), 6, 0.54f, 0.46f, 0.45f, shell);
                    hull.Prism(Point(0f, 1.52f, 0f), 6, 0.4f, 0.4f, 0.04f, ember);
                    for (int i = 0; i < 5; i++)
                    {
                        float yaw = i * 72f;
                        hull.Prism(Around(yaw) * 0.38f + Point(0f, 1.5f, 0f), 4, 0.1f, 0f, 0.5f + (i % 2) * 0.2f,
                                   i % 2 == 0 ? facet : charcoal, Outward(yaw, 18f));
                    }
                    break;

                default:
                    return false;
            }
            return true;
        }

        private static bool HellfireExtraBuilding(Blueprint b, Role role, int slot)
        {
            Solids hull = b.Hull;
            Color shell = BodyPalette.Hellfire.Shell, charcoal = BodyPalette.Hellfire.Charcoal;
            Color facet = BodyPalette.Hellfire.Facet, ember = BodyPalette.Hellfire.Ember, ash = BodyPalette.Hellfire.Ash;

            switch (role)
            {
                case Role.Production when slot == 1:
                    // 큰 균열: an uneven shell arch round a dark opening rimmed in light, two split spires. Taller than 균열 제단.
                    hull.Taper(Vector3.zero, Pair(2.5f, 1.5f), Pair(2.1f, 1.2f), 0.24f, charcoal);
                    for (int i = 0; i < 6; i++)
                    {
                        float yaw = 30f + i * 60f;
                        hull.Prism(Point(Around(yaw).x * 1.15f, 0.15f, Around(yaw).z * 0.7f), 4, 0.14f, 0f, 0.4f + (i % 3) * 0.12f,
                                   i % 2 == 0 ? charcoal : facet, Outward(yaw, 24f));
                    }
                    Strap(b, hull, Point(0f, 0.08f, 0f), Extent(2.4f, 0.08f, 1.46f));
                    hull = b.Tier(Point(0f, 0.2f, 0f));
                    hull.Box(Point(0f, 1.15f, 0.04f), Extent(1.1f, 1.95f, 0.1f), ember);
                    hull.Box(Point(0f, 1.0f, 0.02f), Extent(0.6f, 1.45f, 0.08f), charcoal);
                    // The rim of the opening, standing proud of the pillars on the camera's side.
                    hull.Box(Point(-0.36f, 1.0f, -0.12f), Extent(0.1f, 1.55f, 0.08f), ember, Turn(0f, 0f, -4f));
                    hull.Box(Point(0.34f, 0.95f, -0.12f), Extent(0.1f, 1.4f, 0.08f), ember, Turn(0f, 0f, 5f));
                    hull.Box(Point(-0.02f, 1.78f, -0.12f), Extent(0.64f, 0.1f, 0.08f), ember, Turn(0f, 0f, -8f));
                    hull = b.Tier(Point(-0.8f, 0.2f, 0f));
                    hull.Taper(Point(-0.8f, 0.2f, 0f), Pair(0.8f, 0.9f), Pair(0.46f, 0.56f), 1.8f, shell, Turn(0f, 0f, -6f));
                    hull.Taper(Point(-0.61f, 1.98f, 0f), Pair(0.46f, 0.56f), Pair(0.06f, 0.1f), 1.3f, facet, Turn(0f, 0f, 16f));
                    hull.Prism(Point(-1.1f, 0.9f, 0f), 4, 0.1f, 0f, 0.5f, facet, Turn(0f, 0f, 50f));
                    hull = b.Tier(Point(0.8f, 0.2f, 0f));
                    hull.Taper(Point(0.8f, 0.2f, 0f), Pair(0.72f, 0.8f), Pair(0.42f, 0.5f), 1.45f, charcoal, Turn(0f, 0f, 9f));
                    hull.Taper(Point(0.57f, 1.63f, 0f), Pair(0.42f, 0.5f), Pair(0.08f, 0.12f), 0.95f, shell, Turn(0f, 0f, -32f));
                    hull.Prism(Point(1.1f, 0.7f, 0f), 4, 0.09f, 0f, 0.42f, facet, Turn(0f, 0f, -55f));
                    hull = b.Tier(Point(-0.4f, 1.9f, 0f));
                    hull.Taper(Point(-0.45f, 1.85f, 0f), Pair(0.36f, 0.5f), Pair(0.2f, 0.3f), 0.95f, shell, Turn(0f, 0f, -64f));
                    return true;

                case Role.Defense when slot == 1:
                    // 집결의 횃불: a shell stake ringed with spikes, a flaring cup on top with an ember tongue. Unarmed: no crown.
                    hull.Taper(Vector3.zero, Pair(1.0f, 0.9f), Pair(0.6f, 0.55f), 0.4f, charcoal);
                    for (int i = 0; i < 3; i++)
                    {
                        float yaw = 30f + i * 120f;
                        hull.Taper(Around(yaw) * 0.45f, Pair(0.3f, 0.36f), Pair(0.06f, 0.08f), 0.7f, facet, Outward(yaw, 28f));
                    }
                    Strap(b, hull, Point(0f, 0.3f, 0f), Extent(0.7f, 0.08f, 0.64f));
                    hull = b.Tier(Point(0f, 0.4f, 0f));
                    hull.Taper(Point(0f, 0.4f, 0f), Pair(0.34f, 0.3f), Pair(0.22f, 0.2f), 1.6f, shell, Turn(0f, 0f, 3f));
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = 45f + i * 90f;
                        hull.Prism(Around(yaw) * 0.12f + Point(0f, 1.0f + (i % 2) * 0.3f, 0f), 4, 0.05f, 0f, 0.22f, i % 2 == 0 ? facet : charcoal, Outward(yaw, 60f));
                    }
                    hull = b.Tier(Point(0f, 1.95f, 0f));
                    hull.Taper(Point(0.08f, 1.95f, 0f), Pair(0.28f, 0.26f), Pair(0.72f, 0.66f), 0.38f, charcoal);
                    for (int i = 0; i < 5; i++)
                    {
                        float yaw = i * 72f;
                        hull.Prism(Around(yaw) * 0.32f + Point(0.08f, 2.28f, 0f), 4, 0.07f, 0f, 0.28f + (i % 2) * 0.12f, i % 2 == 0 ? facet : shell, Outward(yaw, 25f));
                    }
                    hull.Prism(Point(0.08f, 2.3f, 0f), 6, 0.2f, 0f, 0.5f, ember);
                    return true;

                case Role.Defense when slot == 2:
                {
                    // 화염 첨탑: a tall narrow shell spire on three splayed feet, the top aims at the sky with one lit slit.
                    for (int i = 0; i < 3; i++)
                    {
                        float yaw = 30f + i * 120f;
                        hull.Taper(Around(yaw) * 0.5f, Pair(0.5f, 0.45f), Pair(0.16f, 0.16f), 1.1f, i == 0 ? charcoal : shell, Outward(yaw, -24f));
                    }
                    Strap(b, hull, Point(0f, 0.25f, 0f), Extent(0.9f, 0.08f, 0.9f));
                    hull = b.Tier(Point(0f, 0.3f, 0f));
                    hull.Taper(Point(0f, 0.3f, 0f), Pair(0.62f, 0.56f), Pair(0.4f, 0.36f), 1.55f, charcoal);
                    hull.Prism(Point(-0.3f, 1.1f, 0f), 4, 0.07f, 0f, 0.35f, facet, Turn(0f, 0f, 40f));
                    hull.Prism(Point(0.3f, 1.4f, 0f), 4, 0.07f, 0f, 0.3f, facet, Turn(0f, 0f, -40f));

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 1.85f, 0f);
                    crown.Taper(Point(0f, 1.82f, 0f), Pair(0.42f, 0.38f), Pair(0.08f, 0.1f), 1.25f, shell, Turn(10f, 0f, 0f));
                    crown.Box(Point(0f, 2.25f, 0.2f), Extent(0.09f, 0.55f, 0.04f), ember, Turn(10f, 0f, 0f));
                    crown.Prism(Point(-0.18f, 2.0f, 0f), 4, 0.06f, 0f, 0.4f, facet, Turn(0f, 0f, 22f));
                    crown.Prism(Point(0.18f, 2.0f, 0f), 4, 0.06f, 0f, 0.32f, facet, Turn(0f, 0f, -22f));
                    b.CrownAims = true;
                    b.CrownKick = -10f;
                    return true;
                }

                case Role.Tech when slot == 1:
                {
                    // 재 분화구: a thick round shell rim with an ash chimney, three shell blades along the back, ash cinders circling the vent.
                    hull.Prism(Vector3.zero, 8, 0.85f, 0.75f, 0.45f, shell);
                    hull.Prism(Point(0f, 0.45f, 0f), 8, 0.75f, 0.6f, 0.15f, charcoal);
                    Band(b, hull, Point(0f, 0.08f, 0f), 8, 0.84f, 0.1f);
                    hull = b.Tier(Point(0f, 0.5f, 0f));
                    hull.Prism(Point(0f, 0.5f, 0f), 6, 0.32f, 0.22f, 0.85f, charcoal);
                    hull.Prism(Point(0f, 1.35f, 0f), 6, 0.2f, 0.2f, 0.04f, ember);
                    hull = b.Tier(Point(0f, 0.45f, 0.5f));
                    for (int i = -1; i <= 1; i++)
                    {
                        float yaw = i * 45f;
                        hull.Taper(Around(yaw) * 0.58f + Point(0f, 0.4f, 0f), Pair(0.34f, 0.24f), Pair(0.06f, 0.06f), 0.9f + (i == 0 ? 0.35f : 0f),
                                   i == 0 ? facet : shell, Outward(yaw, 18f));
                    }

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 1.45f, 0f);
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = i * 90f;
                        crown.Lump(Around(yaw) * 0.42f + Point(0f, 1.6f + (i % 2) * 0.12f, 0f), Extent(0.1f, 0.08f, 0.1f), ash, Turn(0f, yaw, 20f));
                    }
                    b.CrownSpin = 40f;
                    return true;
                }

                default:
                    return false;
            }
        }

        // ---- 물 슬라임 ----

        private static bool WaterSlimeBuilding(Blueprint b, Role role)
        {
            Solids hull = b.Hull;
            Color foam = BodyPalette.WaterSlimes.Foam, pale = BodyPalette.WaterSlimes.Pale;
            Color sky = BodyPalette.WaterSlimes.Sky, blue = BodyPalette.WaterSlimes.Blue;

            switch (role)
            {
                case Role.Base:
                    // 수맥 웅덩이: round shallow tiers, a gentle spring rising from the middle. No walls, no points.
                    hull.Prism(Vector3.zero, 12, 1.6f, 1.55f, 0.35f, pale);
                    hull.Prism(Point(0f, 0.35f, 0f), 12, 1.32f, 1.32f, 0.05f, sky);
                    Band(b, hull, Point(0f, 0.1f, 0f), 12, 1.6f, 0.12f);
                    hull = b.Tier(Point(0f, 0.35f, 0f));
                    hull.Prism(Point(0f, 0.35f, 0f), 12, 1.0f, 0.95f, 0.45f, pale);
                    hull.Prism(Point(0f, 0.8f, 0f), 12, 0.8f, 0.8f, 0.05f, sky);
                    hull = b.Tier(Point(0f, 0.8f, 0f));
                    hull.Dome(Point(0f, 0.8f, 0f), Extent(0.6f, 0.7f, 0.6f), 12, 3, foam);
                    hull = b.Tier(Point(0f, 1.4f, 0f));
                    hull.Prism(Point(0f, 1.4f, 0f), 8, 0.22f, 0.15f, 1.4f, sky);
                    hull.Ball(Point(0f, 2.85f, 0f), 0.24f, blue);
                    if (b.HasAccent) hull.Ball(Point(0f, 3.2f, 0f), Extent(0.16f, 0.2f, 0.16f), b.Accent);
                    break;

                case Role.Production:
                {
                    // 거품 생성기: a captured human device — stone plinth, a ring frame, a bubble held in it.
                    Color stone = BodyPalette.Humans.Stone, bronze = BodyPalette.Humans.Bronze;
                    hull.Prism(Vector3.zero, 8, 1.0f, 0.9f, 0.4f, stone);
                    hull.Prism(Point(0f, 0.4f, 0f), 8, 0.6f, 0.5f, 0.2f, BodyPalette.Humans.StoneMid);
                    Band(b, hull, Point(0f, 0.26f, 0f), 8, 0.98f, 0.1f);
                    hull = b.Tier(Point(0f, 1.35f, 0f));
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * 30f;
                        float radians = angle * Mathf.Deg2Rad;
                        hull.Box(Point(Mathf.Sin(radians) * 0.78f, 1.35f + Mathf.Cos(radians) * 0.78f, 0f),
                                 Extent(0.42f, 0.14f, 0.2f), i % 3 == 0 ? bronze : stone, Turn(0f, 0f, -angle));
                    }
                    hull.Prism(Point(0f, 2.18f, 0f), 6, 0.1f, 0f, 0.25f, bronze);
                    hull = b.Tier(Point(0f, 1.35f, 0f));
                    hull.Ball(Point(0f, 1.35f, 0f), 0.52f, pale);
                    hull.Ball(Point(-0.2f, 1.55f, -0.32f), Extent(0.1f, 0.13f, 0.08f), foam);
                    break;
                }

                case Role.Defense:
                    // 물기둥 분수: a low water ring with a column standing out of it. The column leans toward its target.
                    hull.Prism(Vector3.zero, 10, 0.95f, 0.95f, 0.25f, pale);
                    hull.Prism(Point(0f, 0.25f, 0f), 10, 0.75f, 0.75f, 0.04f, sky);
                    Band(b, hull, Point(0f, 0.04f, 0f), 10, 0.97f, 0.09f);

                    b.Crown.Origin = Point(0f, 0.25f, 0f);
                    b.Crown.Prism(Point(0f, 0.25f, 0f), 8, 0.34f, 0.29f, 0.9f, sky);
                    b.Crown.Prism(Point(0f, 1.13f, 0.02f), 8, 0.29f, 0.24f, 0.85f, blue, Turn(8f, 0f, 0f));
                    b.Crown.Prism(Point(0f, 1.95f, 0.14f), 8, 0.24f, 0.16f, 0.7f, sky, Turn(18f, 0f, 0f));
                    b.Crown.Ball(Point(0f, 2.65f, 0.33f), 0.2f, foam);
                    b.CrownAims = true;
                    b.CrownKick = 14f;
                    break;

                case Role.Supply:
                    // 물웅덩이: a still pool with a few mounds and one droplet standing up out of it.
                    hull.Prism(Vector3.zero, 10, 0.8f, 0.78f, 0.18f, pale);
                    hull.Prism(Point(0f, 0.18f, 0f), 10, 0.62f, 0.62f, 0.03f, sky);
                    Band(b, hull, Point(0f, 0.03f, 0f), 10, 0.82f, 0.07f);
                    hull = b.Tier(Point(0f, 0.1f, 0f));
                    hull.Dome(Point(-0.45f, 0.1f, 0.35f), Extent(0.18f, 0.22f, 0.18f), 8, 2, foam);
                    hull.Dome(Point(0.5f, 0.1f, -0.2f), Extent(0.14f, 0.16f, 0.14f), 8, 2, pale);
                    hull = b.Tier(Point(0f, 0.18f, 0f));
                    hull.Dome(Point(0f, 0.18f, 0f), Extent(0.28f, 0.85f, 0.28f), 8, 3, sky);
                    hull.Prism(Point(0f, 0.95f, 0f), 8, 0.12f, 0f, 0.5f, sky);
                    break;

                case Role.Tech:
                {
                    // 조수 제단: a wave curling up and over its pool.
                    hull.Prism(Vector3.zero, 10, 0.8f, 0.78f, 0.15f, pale);
                    hull.Prism(Point(0f, 0.15f, 0f), 10, 0.62f, 0.62f, 0.03f, sky);
                    Band(b, hull, Point(0f, 0.03f, 0f), 10, 0.82f, 0.07f);
                    Vector3 position = Point(0f, 0.1f, -0.35f);
                    float pitch = -12f;
                    float radius = 0.32f;
                    for (int i = 0; i < 6; i++)
                    {
                        // The wave rises in two pours: its foot, then its curl.
                        if (i % 3 == 0) hull = b.Tier(position);
                        Quaternion rotation = Turn(pitch, 0f, 0f);
                        float next = radius * 0.78f;
                        float length = 0.6f;
                        hull.Prism(position, 7, radius, next, length, i % 2 == 0 ? sky : blue, rotation);
                        position += rotation * Point(0f, length * 0.92f, 0f);
                        pitch += 30f;
                        radius = next;
                    }
                    hull.Ball(position, 0.12f, foam);
                    break;
                }

                default:
                    return false;
            }
            return true;
        }

        private static bool WaterSlimeExtraBuilding(Blueprint b, Role role, int slot)
        {
            Solids hull = b.Hull;
            Color foam = BodyPalette.WaterSlimes.Foam, pale = BodyPalette.WaterSlimes.Pale;
            Color sky = BodyPalette.WaterSlimes.Sky, blue = BodyPalette.WaterSlimes.Blue;

            switch (role)
            {
                case Role.Production when slot == 1:
                    // 구름 둥지: three round water legs holding up a wide shallow basin, cloud puffs resting in it.
                    hull.Prism(Vector3.zero, 12, 1.35f, 1.3f, 0.12f, pale);
                    Band(b, hull, Point(0f, 0.02f, 0f), 12, 1.37f, 0.07f);
                    hull = b.Tier(Vector3.zero);
                    for (int i = 0; i < 3; i++)
                        hull.Dome(Around(60f + i * 120f) * 0.72f, Extent(0.4f, 1.0f, 0.4f), 9, 3, sky);
                    hull = b.Tier(Point(0f, 0.85f, 0f));
                    hull.Prism(Point(0f, 0.82f, 0f), 12, 1.0f, 1.25f, 0.3f, pale);
                    hull.Prism(Point(0f, 1.12f, 0f), 12, 1.05f, 1.05f, 0.03f, blue);
                    hull = b.Tier(Point(0f, 1.15f, 0f));
                    hull.Ball(Point(0f, 1.38f, 0f), 0.5f, BodyPalette.WaterSlimes.Cloud);
                    hull.Ball(Point(-0.55f, 1.3f, 0.2f), 0.36f, BodyPalette.WaterSlimes.Cloud);
                    hull.Ball(Point(0.55f, 1.28f, -0.15f), 0.38f, foam);
                    hull.Ball(Point(0.15f, 1.72f, 0.1f), 0.3f, foam);
                    return true;

                case Role.Defense when slot == 1:
                    // 솟는 샘: two low water rings and a tall round column standing up out of them. Unarmed: no crown.
                    hull.Prism(Vector3.zero, 12, 1.1f, 1.05f, 0.15f, pale);
                    hull.Prism(Point(0f, 0.15f, 0f), 12, 0.8f, 0.75f, 0.14f, sky);
                    hull.Prism(Point(0f, 0.29f, 0f), 12, 0.6f, 0.6f, 0.02f, blue);
                    Band(b, hull, Point(0f, 0.03f, 0f), 12, 1.11f, 0.08f);
                    hull = b.Tier(Point(0f, 0.25f, 0f));
                    hull.Dome(Point(0f, 0.25f, 0f), Extent(0.46f, 2.0f, 0.46f), 10, 4, sky);
                    hull.Ball(Point(-0.16f, 1.1f, -0.38f), Extent(0.07f, 0.36f, 0.05f), foam);
                    hull = b.Tier(Point(0f, 2.2f, 0f));
                    hull.Ball(Point(0f, 2.36f, 0f), 0.17f, foam);
                    return true;

                case Role.Defense when slot == 2:
                    // 거품 장벽: a low wide mound of three big bubbles in a row. A wall: no crown.
                    hull.Prism(Vector3.zero, 10, 1.15f, 1.1f, 0.08f, sky, Quaternion.identity, 0.6f);
                    hull.Ball(Point(-1.05f, 0.14f, 0.25f), 0.16f, foam);
                    Band(b, hull, Point(0f, 0.01f, 0f), 10, 1.17f, 0.06f, 0.6f);
                    hull = b.Tier(Point(-0.55f, 0f, 0f));
                    hull.Ball(Point(-0.5f, 0.44f, 0f), Extent(0.6f, 0.5f, 0.55f), pale);
                    hull.Ball(Point(-0.68f, 0.62f, -0.36f), Extent(0.1f, 0.12f, 0.06f), foam);
                    hull = b.Tier(Point(0.25f, 0f, 0f));
                    hull.Ball(Point(0.28f, 0.36f, 0.06f), Extent(0.5f, 0.42f, 0.5f), sky);
                    hull.Ball(Point(0.14f, 0.5f, -0.36f), Extent(0.08f, 0.1f, 0.05f), foam);
                    hull = b.Tier(Point(0.85f, 0f, 0f));
                    hull.Ball(Point(0.86f, 0.24f, -0.02f), Extent(0.34f, 0.28f, 0.34f), pale);
                    return true;

                case Role.Tech when slot == 1:
                {
                    // 물결 수련장: a low round pool rimmed with bubbles, a small curling wave turning in the middle.
                    hull.Prism(Vector3.zero, 12, 0.85f, 0.8f, 0.2f, pale);
                    hull.Prism(Point(0f, 0.2f, 0f), 12, 0.68f, 0.68f, 0.02f, blue);
                    Band(b, hull, Point(0f, 0.03f, 0f), 12, 0.86f, 0.08f);
                    hull = b.Tier(Point(0f, 0.2f, 0f));
                    for (int i = 0; i < 10; i++)
                    {
                        float yaw = i * 36f;
                        hull.Ball(Around(yaw) * 0.76f + Point(0f, 0.24f, 0f), 0.13f, i % 2 == 0 ? sky : foam);
                    }

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 0.22f, 0f);
                    Vector3 crest = Curl(crown, Point(0f, 0.2f, -0.2f), 5, 7, 0.2f, 0.8f, 0.36f, 0.33f, -10f, 36f, sky, blue);
                    crown.Ball(crest, 0.08f, foam);
                    b.CrownSpin = 30f;
                    return true;
                }

                default:
                    return false;
            }
        }

        // ---- 돌 골렘 부족 ----

        private static bool RockGolemBuilding(Blueprint b, Role role)
        {
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone, shade = BodyPalette.RockGolems.StoneShade;
            Color moss = BodyPalette.RockGolems.Moss, mossDeep = BodyPalette.RockGolems.MossDeep;

            switch (role)
            {
                case Role.Base:
                    // 이끼바위 성소: boulders leaning in on each other, a shaded recess in front that does not go through.
                    hull.Lump(Point(0f, 0.9f, 0.75f), Extent(0.95f, 0.95f, 0.7f), tan, Turn(-10f, -40f, 0f));
                    hull.Box(Point(0f, 0.55f, -0.62f), Extent(0.7f, 0.9f, 0.1f), mossDeep);
                    hull = b.Tier(Point(-0.9f, 0f, -0.05f));
                    hull.Lump(Point(-0.9f, 0.9f, -0.05f), Extent(0.9f, 1.0f, 0.85f), tan, Turn(0f, -20f, 15f));
                    hull.Dome(Point(-0.9f, 1.65f, -0.05f), Extent(0.55f, 0.18f, 0.5f), 8, 2, moss);
                    hull = b.Tier(Point(0.9f, 0f, 0.1f));
                    hull.Lump(Point(0.9f, 0.85f, 0.1f), Extent(0.85f, 0.95f, 0.85f), stone, Turn(0f, 10f, -15f));
                    hull.Dome(Point(0.85f, 1.6f, 0.1f), Extent(0.5f, 0.16f, 0.5f), 8, 2, mossDeep);
                    hull = b.Tier(Point(0f, 1.65f, 0.15f));
                    hull.Lump(Point(0f, 2.3f, 0.15f), Extent(0.75f, 0.65f, 0.7f), stone, Turn(0f, -15f, 5f));
                    hull.Dome(Point(0f, 2.82f, 0.15f), Extent(0.5f, 0.2f, 0.45f), 8, 2, moss);
                    if (b.HasAccent)
                    {
                        hull.Box(Point(0f, 1.55f, -0.66f), Extent(0.6f, 0.06f, 0.08f), shade);
                        hull.Box(Point(0f, 1.2f, -0.65f), Extent(0.46f, 0.66f, 0.04f), b.Accent);
                    }
                    break;

                case Role.Production:
                    // 각성 바위: an egg of stone, cracked with warm light, moss on its crown. Waking, not assembling.
                    hull.Lump(Point(-0.7f, 0.25f, -0.3f), Extent(0.35f, 0.3f, 0.35f), stone, Turn(0f, -30f, 0f));
                    hull.Lump(Point(0.7f, 0.25f, -0.25f), Extent(0.32f, 0.28f, 0.32f), shade, Turn(0f, 20f, 0f));
                    hull = b.Tier(Vector3.zero);
                    hull.Ball(Point(0f, 1.15f, 0f), Extent(0.95f, 1.15f, 0.9f), tan);
                    hull.Box(Point(0.1f, 1.2f, -0.88f), Extent(0.05f, 0.9f, 0.06f), BodyPalette.RockGolems.Waking, Turn(0f, 0f, 20f));
                    hull.Box(Point(-0.25f, 0.9f, -0.85f), Extent(0.05f, 0.5f, 0.06f), BodyPalette.RockGolems.Waking, Turn(0f, 0f, -35f));
                    hull.Box(Point(0.3f, 1.6f, -0.76f), Extent(0.05f, 0.4f, 0.06f), BodyPalette.RockGolems.Waking, Turn(0f, 0f, -20f));
                    Band(b, hull, Point(0f, 0.5f, 0f), 10, 0.84f, 0.1f, 0.9f / 0.95f);
                    hull = b.Tier(Point(0f, 2.0f, 0f));
                    hull.Dome(Point(0f, 2.05f, 0f), Extent(0.6f, 0.22f, 0.55f), 9, 2, moss);
                    break;

                case Role.Defense:
                    // 굴림바위 언덕: an earth mound with a boulder on top. It rolls rather than fires; no barrel, no sight.
                    hull.Dome(Vector3.zero, Extent(1.15f, 0.7f, 1.05f), 10, 3, shade);
                    hull.Dome(Point(-0.3f, 0.48f, 0.2f), Extent(0.45f, 0.16f, 0.38f), 8, 2, moss);
                    hull.Dome(Point(0.4f, 0.4f, -0.3f), Extent(0.35f, 0.14f, 0.3f), 8, 2, mossDeep);
                    Band(b, hull, Point(0f, 0.02f, 0f), 10, 1.17f, 0.1f, 1.05f / 1.15f);

                    b.Crown.Origin = Point(0f, 1.2f, 0f);
                    b.Crown.Lump(Point(0f, 1.22f, 0f), Extent(0.56f, 0.52f, 0.56f), tan, Turn(15f, 25f, 10f));
                    b.Crown.Dome(Point(0f, 1.6f, -0.05f), Extent(0.3f, 0.12f, 0.28f), 7, 2, moss);
                    b.CrownAims = true;
                    b.CrownKick = 22f;
                    break;

                case Role.Supply:
                    // 선돌: one tall standing slab, moss round its foot.
                    hull.Dome(Vector3.zero, Extent(0.55f, 0.2f, 0.45f), 8, 2, moss);
                    hull.Lump(Point(0.5f, 0.1f, 0.25f), Extent(0.14f, 0.12f, 0.14f), stone, Turn(0f, 20f, 0f));
                    hull = b.Tier(Vector3.zero);
                    hull.Taper(Vector3.zero, Pair(0.55f, 0.3f), Pair(0.42f, 0.24f), 1.85f, tan, Turn(0f, 0f, 4f));
                    hull.Box(Point(-0.1f, 1.1f, -0.14f), Extent(0.04f, 0.5f, 0.03f), shade, Turn(0f, 0f, 14f));
                    hull.Dome(Point(-0.14f, 1.83f, 0f), Extent(0.16f, 0.06f, 0.14f), 6, 1, moss);
                    Strap(b, hull, Point(-0.035f, 0.5f, 0f), Extent(0.54f, 0.1f, 0.31f), Turn(0f, 0f, 4f));
                    break;

                case Role.Tech:
                    // 고대 이끼돌: a dolmen — two leg stones, a capstone draped in moss, a warm glow beneath.
                    hull.Ball(Point(0f, 0.4f, 0f), 0.2f, BodyPalette.RockGolems.Waking);
                    hull = b.Tier(Point(-0.5f, 0f, 0f));
                    hull.Taper(Point(-0.5f, 0f, 0f), Pair(0.35f, 0.5f), Pair(0.3f, 0.42f), 1.1f, stone);
                    Strap(b, hull, Point(-0.5f, 0.6f, 0f), Extent(0.37f, 0.1f, 0.51f));
                    hull = b.Tier(Point(0.5f, 0f, 0f));
                    hull.Taper(Point(0.5f, 0f, 0f), Pair(0.35f, 0.5f), Pair(0.3f, 0.42f), 1.1f, shade);
                    Strap(b, hull, Point(0.5f, 0.6f, 0f), Extent(0.37f, 0.1f, 0.51f));
                    hull = b.Tier(Point(0f, 1.1f, 0f));
                    hull.Box(Point(0f, 1.25f, 0f), Extent(1.55f, 0.3f, 0.75f), tan, Turn(0f, 0f, 3f));
                    hull.Dome(Point(0f, 1.38f, 0f), Extent(0.65f, 0.14f, 0.32f), 9, 2, moss);
                    hull.Box(Point(0.55f, 1.15f, -0.38f), Extent(0.3f, 0.35f, 0.03f), mossDeep);
                    break;

                default:
                    return false;
            }
            return true;
        }

        private static bool RockGolemExtraBuilding(Blueprint b, Role role, int slot)
        {
            Solids hull = b.Hull;
            Color tan = BodyPalette.RockGolems.Tan, stone = BodyPalette.RockGolems.Stone, shade = BodyPalette.RockGolems.StoneShade;
            Color moss = BodyPalette.RockGolems.Moss, mossDeep = BodyPalette.RockGolems.MossDeep;

            switch (role)
            {
                case Role.Production when slot == 1:
                    // 각성의 원: a low ring of standing stones leaning in on a flat slab, the one at the back taller.
                    hull.Dome(Vector3.zero, Extent(1.35f, 0.12f, 1.25f), 10, 2, mossDeep);
                    hull.Prism(Vector3.zero, 8, 0.62f, 0.56f, 0.26f, tan);
                    Band(b, hull, Point(0f, 0.1f, 0f), 8, 0.62f, 0.08f);
                    for (int i = 0; i < 5; i++)
                    {
                        float yaw = i * 72f;
                        bool back = i == 0;
                        float height = back ? 0.95f : 0.55f + (i % 2) * 0.12f;
                        Vector3 foot = Around(yaw) * 0.98f;
                        hull = b.Tier(foot);
                        hull.Lump(foot + Point(0f, height, 0f), Extent(back ? 0.38f : 0.4f, height, 0.34f), i % 2 == 0 ? tan : stone, Outward(yaw, -14f));
                        hull.Dome(foot + Around(yaw) * -0.08f + Point(0f, height * 1.8f, 0f), Extent(0.26f, 0.08f, 0.22f), 7, 2, i % 2 == 0 ? moss : mossDeep);
                    }
                    return true;

                case Role.Defense when slot == 1:
                    // 부유석 첨탑: a thin stone spire on a mossy foot, a great boulder floating well clear of its tip;
                    // the boulder turns on what it drops on.
                    hull.Dome(Vector3.zero, Extent(0.75f, 0.16f, 0.7f), 9, 2, mossDeep);
                    hull.Lump(Point(0.35f, 0.14f, -0.3f), Extent(0.2f, 0.14f, 0.18f), stone, Turn(0f, 20f, 0f));
                    hull.Lump(Point(0f, 0.22f, 0f), Extent(0.42f, 0.22f, 0.4f), shade, Turn(0f, 20f, 0f));
                    Band(b, hull, Point(0f, 0.1f, 0f), 9, 0.42f, 0.08f);
                    hull = b.Tier(Point(0f, 0.35f, 0f));
                    hull.Taper(Point(0f, 0.35f, 0f), Pair(0.36f, 0.32f), Pair(0.12f, 0.1f), 1.45f, stone, Turn(0f, 25f, 2f));
                    hull.Box(Point(-0.06f, 0.95f, -0.12f), Extent(0.03f, 0.4f, 0.02f), shade, Turn(0f, 25f, 10f));
                    hull.Dome(Point(-0.02f, 0.62f, 0f), Extent(0.2f, 0.06f, 0.17f), 6, 1, moss);

                    b.Crown.Origin = Point(0f, 2.6f, 0f);
                    b.Crown.Lump(Point(0f, 2.62f, 0f), Extent(0.66f, 0.4f, 0.6f), tan, Turn(10f, 30f, 5f));
                    b.Crown.Dome(Point(-0.05f, 2.9f, 0f), Extent(0.42f, 0.14f, 0.36f), 8, 2, moss);
                    b.Crown.Lump(Point(0.55f, 2.3f, -0.25f), Extent(0.13f, 0.1f, 0.13f), stone, Turn(0f, 20f, 0f));
                    b.CrownAims = true;
                    b.CrownKick = 18f;
                    return true;

                case Role.Defense when slot == 2:
                    // 잠듦의 고리: low stones leaning in round one round stone asleep in the middle. Unarmed: no crown.
                    hull.Dome(Vector3.zero, Extent(1.05f, 0.14f, 1.0f), 10, 2, mossDeep);
                    hull = b.Tier(Vector3.zero);
                    hull.Ball(Point(0f, 0.38f, 0f), Extent(0.42f, 0.36f, 0.4f), tan);
                    Band(b, hull, Point(0f, 0.14f, 0f), 10, 0.33f, 0.07f);
                    for (int side = -1; side <= 1; side += 2)
                        hull.Box(Point(side * 0.11f, 0.44f, -0.385f), Extent(0.1f, 0.025f, 0.03f), BodyPalette.Eye, Turn(0f, 0f, side * 8f));
                    hull.Dome(Point(0.04f, 0.66f, 0.06f), Extent(0.28f, 0.1f, 0.24f), 8, 2, moss);
                    for (int i = 0; i < 5; i++)
                    {
                        float yaw = 36f + i * 72f;
                        float height = 0.3f + Mathf.Max(0f, Around(yaw).z) * 0.3f;
                        Vector3 foot = Around(yaw) * 0.84f;
                        hull = b.Tier(foot);
                        hull.Lump(foot + Point(0f, height, 0f), Extent(0.26f, height, 0.22f), i % 2 == 0 ? stone : shade, Outward(yaw, -22f));
                        hull.Dome(foot + Point(0f, 0.04f, 0f), Extent(0.3f, 0.1f, 0.26f), 7, 2, moss);
                    }
                    return true;

                case Role.Tech when slot == 1:
                {
                    // 이끼 정원: three stone terraces thick with moss and leaves, three pebbles turning slowly over them.
                    hull.Box(Point(0f, 0.15f, 0f), Extent(1.5f, 0.3f, 1.4f), stone);
                    Strap(b, hull, Point(0f, 0.08f, 0f), Extent(1.54f, 0.08f, 1.44f));
                    hull.Dome(Point(-0.5f, 0.3f, -0.45f), Extent(0.3f, 0.1f, 0.24f), 7, 2, moss);
                    hull = b.Tier(Point(0.1f, 0.3f, 0.12f));
                    hull.Box(Point(0.1f, 0.42f, 0.12f), Extent(1.1f, 0.26f, 1.0f), tan, Turn(0f, 8f, 0f));
                    hull.Dome(Point(-0.25f, 0.55f, -0.2f), Extent(0.3f, 0.12f, 0.24f), 7, 2, mossDeep);
                    hull.Dome(Point(0.45f, 0.55f, 0.3f), Extent(0.26f, 0.1f, 0.22f), 7, 2, moss);
                    hull = b.Tier(Point(0.15f, 0.55f, 0.25f));
                    hull.Box(Point(0.15f, 0.66f, 0.25f), Extent(0.7f, 0.22f, 0.6f), shade, Turn(0f, -6f, 0f));
                    for (int i = 0; i < 5; i++)
                    {
                        float yaw = i * 72f + 20f;
                        hull.Ball(Around(yaw) * 0.22f + Point(0.15f, 0.84f, 0.25f), Extent(0.08f, 0.04f, 0.22f), i % 2 == 0 ? moss : mossDeep,
                                  Quaternion.Euler(0f, yaw, 0f) * Turn(-30f, 0f, 0f), 1);
                    }

                    Solids crown = b.Crown;
                    crown.Origin = Point(0.15f, 1.1f, 0.25f);
                    for (int i = 0; i < 3; i++)
                        crown.Lump(Around(i * 120f) * 0.36f + Point(0.15f, 1.2f + i * 0.06f, 0.25f), Extent(0.09f, 0.07f, 0.09f), i == 1 ? stone : tan, Turn(0f, i * 40f, 15f));
                    b.CrownSpin = 25f;
                    return true;
                }

                default:
                    return false;
            }
        }

        // ---- 차원 유랑종 ----

        private static bool DriftworldBuilding(Blueprint b, Role role)
        {
            Solids hull = b.Hull;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade, deep = BodyPalette.Driftworlds.ShellDeep;
            Color teal = BodyPalette.Driftworlds.Teal, tealLight = BodyPalette.Driftworlds.TealLight;
            Color dusk = BodyPalette.Driftworlds.Dusk, lilac = BodyPalette.Driftworlds.Lilac;

            switch (role)
            {
                case Role.Base:
                    // 정박한 세계: a half-buried shell of matte plates; between them, another world's sky and ground.
                    hull.Dome(Vector3.zero, Extent(1.35f, 1.85f, 1.15f), 10, 3, lilac);
                    hull.Prism(Vector3.zero, 10, 1.3f, 1.05f, 0.5f, teal);
                    for (int i = 0; i < 3; i++)
                        hull.Prism(Point(-0.5f + i * 0.5f, 0.45f, -0.75f), 4, 0.18f, 0f, 0.4f + (i % 2) * 0.2f, tealLight);
                    Band(b, hull, Point(0f, 0.04f, 0f), 10, 1.72f, 0.1f);
                    // The plates close in two rounds, every other one first.
                    Solids first = b.Tier(Vector3.zero), second = b.Tier(Vector3.zero);
                    for (int i = 0; i < 5; i++)
                    {
                        float yaw = 36f + i * 72f;
                        (i % 2 == 0 ? first : second).Taper(Around(yaw) * 1.5f, Pair(1.05f, 0.35f), Pair(0.45f, 0.25f), 2.3f,
                                   i % 2 == 0 ? shell : shade, Outward(yaw, -34f));
                    }
                    hull = b.Tier(Point(0f, 2.0f, 0f));
                    hull.Lump(Point(0f, 2.3f, 0f), Extent(0.62f, 0.35f, 0.55f), shell, Turn(0f, 20f, 0f));
                    Shard(b, hull, Point(0f, 3.0f, 0f), 0.2f);
                    break;

                case Role.Production:
                    // 통로: two plates parting on a vertical crack; through it, the other side.
                    hull.Box(Point(0f, 1.15f, 0f), Extent(0.36f, 2.1f, 0.12f), teal);
                    hull.Box(Point(0f, 1.95f, 0f), Extent(0.3f, 0.6f, 0.13f), lilac);
                    hull.Prism(Point(-0.06f, 0.15f, -0.02f), 4, 0.1f, 0f, 0.35f, tealLight);
                    hull.Prism(Point(0.08f, 0.15f, -0.02f), 4, 0.08f, 0f, 0.25f, tealLight);
                    hull.Lump(Point(-0.95f, 0.18f, -0.4f), Extent(0.28f, 0.2f, 0.25f), deep, Turn(0f, -20f, 0f));
                    b.Tier(Point(-0.48f, 0f, 0f)).Taper(Point(-0.48f, 0f, 0f), Pair(0.8f, 0.55f), Pair(0.35f, 0.3f), 2.65f, shell, Turn(0f, 0f, 8f));
                    hull = b.Tier(Point(0.48f, 0f, 0f));
                    hull.Taper(Point(0.48f, 0f, 0f), Pair(0.8f, 0.55f), Pair(0.35f, 0.3f), 2.5f, shade, Turn(0f, 0f, -8f));
                    Strap(b, hull, Point(0f, 0.3f, 0f), Extent(1.75f, 0.1f, 0.6f));
                    break;

                case Role.Defense:
                {
                    // 굴절 기둥: plate fragments hanging in the air with no support, slowly turning. 금지: 받침대, 기초, 케이블.
                    hull.Lump(Point(-0.4f, 0.08f, 0.3f), Extent(0.18f, 0.1f, 0.16f), deep, Turn(0f, 30f, 0f));
                    hull.Lump(Point(0.35f, 0.07f, -0.25f), Extent(0.15f, 0.08f, 0.14f), deep, Turn(0f, -20f, 0f));
                    Band(b, hull, Point(0f, 0.22f, 0f), 8, 0.5f, 0.06f);

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 0f, 0f);
                    crown.Taper(Point(0f, 0.4f, 0f), Pair(0.9f, 0.6f), Pair(0.7f, 0.5f), 0.45f, shell, Turn(0f, 0f, 0f));
                    crown.Prism(Point(0f, 0.93f, 0f), 6, 0.42f, 0.42f, 0.12f, lilac);
                    crown.Taper(Point(0f, 1.15f, 0f), Pair(0.75f, 0.55f), Pair(0.6f, 0.45f), 0.45f, shade, Turn(0f, 35f, 3f));
                    crown.Prism(Point(0f, 1.68f, 0f), 6, 0.36f, 0.36f, 0.12f, teal);
                    crown.Taper(Point(0f, 1.9f, 0f), Pair(0.6f, 0.45f), Pair(0.45f, 0.35f), 0.4f, shell, Turn(0f, 70f, -3f));
                    crown.Taper(Point(0f, 2.5f, 0f), Pair(0.4f, 0.3f), Pair(0.15f, 0.12f), 0.4f, shade, Turn(0f, 110f, 0f));
                    b.CrownSpin = 22f;
                    break;
                }

                case Role.Supply:
                {
                    // 계류 닻: a hooked claw driven into the ground, a sliver of the interior at its root.
                    hull.Box(Point(0.05f, 0.25f, -0.1f), Extent(0.12f, 0.36f, 0.3f), teal);
                    Strap(b, hull, Point(0f, 0.18f, -0.4f), Extent(0.68f, 0.1f, 0.5f));
                    hull = b.Tier(Point(0f, 0f, -0.4f));
                    Vector3 position = Point(0f, 0f, -0.4f);
                    float pitch = -8f;
                    float width = 0.62f;
                    for (int i = 0; i < 5; i++)
                    {
                        Quaternion rotation = Turn(pitch, 0f, 0f);
                        float next = width * 0.76f;
                        float length = 0.5f;
                        hull.Taper(position, Pair(width, width * 0.7f), Pair(next, next * 0.7f), length, i % 2 == 0 ? shell : shade, rotation);
                        position += rotation * Point(0f, length * 0.95f, 0f);
                        pitch += 34f;
                        width = next;
                    }
                    break;
                }

                case Role.Tech:
                {
                    // 층위 관측대: a ring of standing shards, two with windows onto the inside, a shard turning over the middle.
                    hull.Prism(Vector3.zero, 6, 0.4f, 0.35f, 0.08f, deep);
                    Band(b, hull, Point(0f, 0.02f, 0f), 12, 0.88f, 0.06f);
                    Solids even = b.Tier(Vector3.zero), odd = b.Tier(Vector3.zero);
                    for (int i = 0; i < 6; i++)
                    {
                        float yaw = i * 60f;
                        float height = 1.2f + (i % 3) * 0.4f;
                        Vector3 foot = Around(yaw) * 0.68f;
                        hull = i % 2 == 0 ? even : odd;
                        hull.Taper(foot, Pair(0.36f, 0.22f), Pair(0.12f, 0.1f), height, i % 2 == 0 ? shell : shade,
                                   Outward(yaw, -6f));
                        if (i == 3 || i == 5)
                            hull.Box(foot + Around(yaw) * 0.1f + Point(0f, height * 0.4f, 0f), Extent(0.14f, height * 0.35f, 0.05f),
                                     i == 3 ? lilac : tealLight, Quaternion.Euler(0f, yaw, 0f));
                    }

                    b.Crown.Origin = Point(0f, 1.3f, 0f);
                    b.Crown.Prism(Point(0f, 1.1f, 0f), 5, 0.2f, 0f, 0.6f, tealLight);
                    b.Crown.Prism(Point(0f, 1.1f, 0f), 5, 0.2f, 0f, 0.35f, teal, Turn(180f, 0f, 0f));
                    b.CrownSpin = 35f;
                    break;
                }

                default:
                    return false;
            }
            return true;
        }

        private static bool DriftworldExtraBuilding(Blueprint b, Role role, int slot)
        {
            Solids hull = b.Hull;
            Color shell = BodyPalette.Driftworlds.Shell, shade = BodyPalette.Driftworlds.ShellShade, deep = BodyPalette.Driftworlds.ShellDeep;
            Color teal = BodyPalette.Driftworlds.Teal, tealLight = BodyPalette.Driftworlds.TealLight;
            Color dusk = BodyPalette.Driftworlds.Dusk, lilac = BodyPalette.Driftworlds.Lilac;

            switch (role)
            {
                case Role.Production when slot == 1:
                    // 깊은 통로: two thick plates leaning in to a tall passage, a smaller pair inside it, the other world receding behind.
                    hull.Box(Point(0f, 1.2f, 0.12f), Extent(1.0f, 2.3f, 0.12f), teal);
                    hull.Box(Point(0f, 2.0f, 0.1f), Extent(0.7f, 0.8f, 0.13f), lilac);
                    hull.Box(Point(0f, 1.55f, 0.09f), Extent(0.5f, 0.2f, 0.13f), dusk);
                    for (int i = 0; i < 3; i++)
                        hull.Prism(Point(-0.25f + i * 0.25f, 0.15f, 0.06f), 4, 0.1f - i * 0.02f, 0f, 0.4f - i * 0.08f, tealLight);
                    hull.Lump(Point(-1.1f, 0.15f, -0.4f), Extent(0.26f, 0.18f, 0.24f), deep, Turn(0f, 20f, 0f));
                    Strap(b, hull, Point(0f, 0.08f, 0f), Extent(2.2f, 0.1f, 0.7f));
                    hull = b.Tier(Point(-0.78f, 0f, 0f));
                    hull.Taper(Point(-0.78f, 0f, 0f), Pair(0.9f, 0.7f), Pair(0.32f, 0.36f), 2.9f, shell, Turn(0f, 0f, -12f));
                    hull = b.Tier(Point(0.78f, 0f, 0f));
                    hull.Taper(Point(0.78f, 0f, 0f), Pair(0.9f, 0.7f), Pair(0.32f, 0.36f), 2.75f, shade, Turn(0f, 0f, 12f));
                    b.Tier(Point(-0.36f, 0f, -0.2f)).Taper(Point(-0.36f, 0f, -0.2f), Pair(0.32f, 0.22f), Pair(0.12f, 0.12f), 1.55f, deep, Turn(0f, 0f, 6f));
                    b.Tier(Point(0.36f, 0f, -0.2f)).Taper(Point(0.36f, 0f, -0.2f), Pair(0.32f, 0.22f), Pair(0.12f, 0.12f), 1.4f, shade, Turn(0f, 0f, -6f));
                    return true;

                case Role.Defense when slot == 1:
                {
                    // 위상 닻: three tall narrow plates rising to one height round a sliver of the inside, small plates turning round them. Unarmed.
                    hull.Prism(Vector3.zero, 6, 0.75f, 0.65f, 0.12f, deep);
                    for (int i = 0; i < 3; i++)
                        hull.Prism(Around(60f + i * 120f) * 0.18f + Point(0f, 0.1f, 0f), 5, 0.14f, 0f, 0.7f + (i % 2) * 0.25f, i == 1 ? teal : tealLight);
                    Band(b, hull, Point(0f, 0.02f, 0f), 6, 0.76f, 0.08f);
                    for (int i = 0; i < 3; i++)
                    {
                        float yaw = i * 120f;
                        Vector3 foot = Around(yaw) * 0.42f;
                        hull = b.Tier(foot);
                        hull.Taper(foot, Pair(0.34f, 0.24f), Pair(0.12f, 0.1f), 2.3f, i == 0 ? shade : shell, Outward(yaw, -5f));
                    }

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 1.0f, 0f);
                    for (int i = 0; i < 3; i++)
                    {
                        float yaw = 60f + i * 120f;
                        crown.Taper(Around(yaw) * 0.78f + Point(0f, 0.85f + i * 0.2f, 0f), Pair(0.24f, 0.06f), Pair(0.12f, 0.05f), 0.34f,
                                    i == 1 ? lilac : shade, Quaternion.Euler(0f, yaw + 90f, 0f));
                    }
                    b.CrownSpin = 30f;
                    return true;
                }

                case Role.Defense when slot == 2:
                    // 틈새 포대: plates splayed open toward the target, the world inside where a muzzle would be. The upper plate aims.
                    hull.Lump(Point(-0.6f, 0.1f, -0.5f), Extent(0.2f, 0.12f, 0.18f), deep, Turn(0f, 30f, 0f));
                    hull.Taper(Vector3.zero, Pair(1.5f, 1.3f), Pair(1.15f, 1.0f), 0.55f, shade);
                    hull.Prism(Point(0f, 0.55f, 0.1f), 8, 0.5f, 0.5f, 0.05f, teal);
                    Band(b, hull, Point(0f, 0.08f, 0f), 4, 0.98f, 0.1f);
                    hull = b.Tier(Point(-0.55f, 0.2f, 0f));
                    hull.Taper(Point(-0.6f, 0.2f, 0f), Pair(0.35f, 1.1f), Pair(0.15f, 0.8f), 1.0f, shell, Turn(0f, 0f, 28f));
                    hull = b.Tier(Point(0.55f, 0.2f, 0f));
                    hull.Taper(Point(0.6f, 0.2f, 0f), Pair(0.35f, 1.1f), Pair(0.15f, 0.8f), 0.9f, shell, Turn(0f, 0f, -28f));

                    b.Crown.Origin = Point(0f, 0.8f, -0.35f);
                    b.Crown.Ball(Point(0f, 0.85f, 0.05f), Extent(0.38f, 0.26f, 0.38f), teal);
                    b.Crown.Box(Point(0f, 0.98f, 0.22f), Extent(0.42f, 0.16f, 0.12f), lilac);
                    b.Crown.Prism(Point(0.1f, 0.9f, 0.3f), 4, 0.08f, 0f, 0.2f, tealLight);
                    b.Crown.Taper(Point(0f, 1.0f, -0.42f), Pair(1.2f, 0.3f), Pair(0.86f, 0.2f), 1.2f, shell, Turn(68f, 0f, 0f));
                    b.CrownAims = true;
                    b.CrownKick = -16f;
                    return true;

                case Role.Tech when slot == 1:
                {
                    // 외피 연마장: low plates laid open round a floor of the inside, one raised on the right, a polished plate turning on a peg.
                    hull.Prism(Vector3.zero, 8, 0.8f, 0.78f, 0.1f, teal);
                    hull.Box(Point(-0.1f, 0.11f, 0.15f), Extent(0.5f, 0.02f, 0.3f), lilac);
                    hull.Prism(Point(0.25f, 0.1f, -0.2f), 4, 0.08f, 0f, 0.2f, tealLight);
                    Band(b, hull, Point(0f, 0.02f, 0f), 8, 0.81f, 0.06f);
                    Solids low = b.Tier(Vector3.zero);
                    float[] yaws = { 180f, 235f, 300f };
                    for (int i = 0; i < yaws.Length; i++)
                    {
                        Vector3 foot = Around(yaws[i]) * 0.6f;
                        low.Taper(foot, Pair(0.75f, 0.32f), Pair(0.55f, 0.24f), 0.5f, i == 1 ? shade : shell, Outward(yaws[i], 58f));
                    }
                    hull = b.Tier(Point(0.6f, 0f, 0f));
                    hull.Taper(Point(0.62f, 0f, 0.05f), Pair(0.34f, 0.8f), Pair(0.16f, 0.5f), 1.25f, shade, Turn(0f, 0f, -14f));

                    Solids crown = b.Crown;
                    crown.Origin = Point(-0.1f, 0.12f, 0.15f);
                    crown.Prism(Point(-0.1f, 0.12f, 0.15f), 6, 0.06f, 0.05f, 0.5f, deep);
                    crown.Prism(Point(-0.1f, 0.62f, 0.15f), 6, 0.34f, 0.34f, 0.06f, shell, Turn(22f, 0f, 0f));
                    crown.Prism(Point(-0.1f, 0.66f, 0.15f), 6, 0.2f, 0.2f, 0.03f, BodyPalette.Lighter(shell, 0.3f), Turn(22f, 0f, 0f));
                    b.CrownSpin = 45f;
                    return true;
                }

                default:
                    return false;
            }
        }

        // ---- 인간 마법 문명 ----

        private static bool HumanBuilding(Blueprint b, Role role)
        {
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, stoneMid = BodyPalette.Humans.StoneMid, stoneDark = BodyPalette.Humans.StoneDark;
            Color steel = BodyPalette.Humans.Steel, wood = BodyPalette.Humans.Wood, bronze = BodyPalette.Humans.Bronze;
            Color mana = BodyPalette.Humans.Mana;

            switch (role)
            {
                case Role.Base:
                    // 마나 샘: an octagonal stone basin of mana under a steel-blue pyramid roof on four posts, a banner beside it.
                    hull.Prism(Vector3.zero, 8, 1.5f, 1.45f, 0.5f, stone);
                    hull.Prism(Point(0f, 0.5f, 0f), 8, 1.55f, 1.55f, 0.1f, stoneMid);
                    hull.Prism(Point(0f, 0.5f, 0f), 8, 1.25f, 1.25f, 0.13f, mana);
                    Band(b, hull, Point(0f, 0.18f, 0f), 8, 1.52f, 0.12f);
                    hull = b.Tier(Point(0f, 0.55f, 0f));
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = 45f + i * 90f;
                        hull.Box(Around(yaw) * 1.2f + Point(0f, 1.3f, 0f), Extent(0.2f, 1.5f, 0.2f), wood);
                    }
                    hull = b.Tier(Point(0f, 2.0f, 0f));
                    hull.Prism(Point(0f, 2.0f, 0f), 4, 1.55f, 1.55f, 0.08f, bronze);
                    hull.Prism(Point(0f, 2.08f, 0f), 4, 1.5f, 0f, 0.9f, steel);
                    hull.Prism(Point(0f, 2.9f, 0f), 6, 0.06f, 0.06f, 0.3f, bronze);
                    hull.Ball(Point(0f, 3.3f, 0f), 0.13f, mana);
                    // On the foundation, not the posts: a banner planted with the
                    // first stone, rather than one that floats while posts go up.
                    Pennant(b, b.Hull, Point(1.55f, 0f, -0.9f), 2.7f, wood);
                    break;

                case Role.Production:
                    // 마법 탑, 생산: a square cut-stone tower, coursed, under a steel-blue spire, a lit window over a wooden door.
                    // Three courses of 0.6, the seams under the stoneMid string courses,
                    // so a site goes up a course at a time.
                    hull.Box(Point(0f, 0.3f, 0f), Extent(1.4f, 0.6f, 1.4f), stone);
                    hull.Box(Point(0f, 0.36f, -0.71f), Extent(0.42f, 0.72f, 0.04f), wood);
                    hull.Box(Point(0f, 0.74f, -0.715f), Extent(0.5f, 0.06f, 0.04f), bronze);
                    hull = b.Tier(Point(0f, 0.58f, 0f));
                    hull.Box(Point(0f, 0.6f, 0f), Extent(1.44f, 0.05f, 1.44f), stoneMid);
                    hull.Box(Point(0f, 0.9f, 0f), Extent(1.4f, 0.6f, 1.4f), stone);
                    if (b.HasAccent) hull.Box(Point(0.45f, 1.0f, -0.715f), Extent(0.24f, 0.5f, 0.03f), b.Accent);
                    hull = b.Tier(Point(0f, 1.18f, 0f));
                    hull.Box(Point(0f, 1.2f, 0f), Extent(1.44f, 0.05f, 1.44f), stoneMid);
                    hull.Box(Point(0f, 1.5f, 0f), Extent(1.4f, 0.6f, 1.4f), stone);
                    hull.Box(Point(0f, 1.85f, 0f), Extent(1.56f, 0.12f, 1.56f), stoneMid);
                    hull.Box(Point(0f, 1.42f, -0.71f), Extent(0.22f, 0.32f, 0.03f), mana);
                    Strap(b, hull, Point(0f, 1.7f, 0f), Extent(1.46f, 0.12f, 1.46f));
                    hull = b.Tier(Point(0f, 1.91f, 0f));
                    hull.Prism(Point(0f, 1.91f, 0f), 4, 1.0f, 0f, 0.95f, steel);
                    break;

                case Role.Supply:
                    // 마법 탑, 인구: the same masonry as a round silo under a steel cone. Shorter than the 생산 tower.
                    hull.Prism(Vector3.zero, 8, 0.72f, 0.704f, 0.45f, stone);
                    hull.Box(Point(0f, 0.3f, -0.66f), Extent(0.3f, 0.6f, 0.04f), wood);
                    hull = b.Tier(Point(0f, 0.43f, 0f));
                    hull.Prism(Point(0f, 0.45f, 0f), 8, 0.71f, 0.71f, 0.05f, stoneMid);
                    hull.Prism(Point(0f, 0.45f, 0f), 8, 0.704f, 0.68f, 0.7f, stone);
                    hull.Box(Point(0f, 0.85f, -0.665f), Extent(0.16f, 0.22f, 0.03f), mana);
                    Band(b, hull, Point(0f, 0.9f, 0f), 8, 0.71f, 0.1f);
                    hull = b.Tier(Point(0f, 1.15f, 0f));
                    hull.Prism(Point(0f, 1.15f, 0f), 8, 0.8f, 0.8f, 0.08f, bronze);
                    hull.Prism(Point(0f, 1.23f, 0f), 8, 0.78f, 0f, 0.65f, steel);
                    break;

                case Role.Defense:
                {
                    // 대포: a stone emplacement with a long barrel. 전기 타워 and
                    // 돌 포탑 are entries 1 and 2, in HumanExtraBuilding: three
                    // silhouettes, so a glance tells which.
                    Solids crown = b.Crown;
                    hull.Taper(Vector3.zero, Pair(1.7f, 1.6f), Pair(1.35f, 1.3f), 0.8f, stone);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        hull.Taper(Point(side * 0.71f, 0f, 0f), Pair(0.38f, 1.7f), Pair(0.26f, 1.4f), 0.62f, steel);
                        hull.Box(Point(side * 0.55f, 0.42f, 0.71f), Extent(0.1f, 0.18f, 0.05f), bronze, Turn(0f, 0f, 45f));
                    }
                    hull.Box(Point(0f, 0.36f, 0.72f), Extent(0.25f, 0.5f, 0.04f), steel);
                    hull.Box(Point(0f, 0.4f, 0.75f), Extent(0.12f, 0.16f, 0.04f), bronze, Turn(0f, 0f, 45f));
                    Strap(b, hull, Point(0f, 0.72f, 0f), Extent(1.54f, 0.1f, 1.54f));
                    hull = b.Tier(Point(0f, 0.8f, 0f));
                    hull.Box(Point(0f, 0.9f, 0f), Extent(1.2f, 0.2f, 1.2f), stoneMid);
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = 45f + i * 90f;
                        hull.Box(Around(yaw) * 0.85f + Point(0f, 1.08f, 0f), Extent(0.22f, 0.18f, 0.22f), stoneMid);
                    }

                    crown.Origin = Point(0f, 1.0f, 0f);
                    crown.Box(Point(0f, 1.2f, -0.05f), Extent(0.7f, 0.4f, 0.6f), stoneDark);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        crown.Taper(Point(side * 0.43f, 0.94f, -0.1f), Pair(0.22f, 0.65f), Pair(0.18f, 0.32f), 0.6f, steel);
                        crown.Prism(Point(side * 0.41f, 1.35f, -0.08f), 10, 0.16f, 0.16f, 0.14f, bronze, Turn(0f, 0f, side * -90f));
                    }
                    HumanCatalogBarrel(crown, Point(0f, 1.35f, -0.35f), 0.3f, 1.25f, Turn(68f, 0f, 0f));
                    b.CrownAims = true;
                    b.CrownKick = -12f;
                    break;
                }

                case Role.Tech:
                    // 공방: a long stone workshop framed in timber, a slanted steel roof, a chimney.
                    hull.Box(Point(0f, 0.5f, 0f), Extent(1.5f, 1.0f, 1.1f), stone);
                    hull.Box(Point(-0.72f, 0.5f, -0.56f), Extent(0.08f, 1.0f, 0.04f), wood);
                    hull.Box(Point(0.72f, 0.5f, -0.56f), Extent(0.08f, 1.0f, 0.04f), wood);
                    hull.Box(Point(0f, 1.0f, -0.56f), Extent(1.5f, 0.08f, 0.04f), wood);
                    hull.Box(Point(0f, 0.38f, -0.56f), Extent(0.5f, 0.5f, 0.03f), stoneDark);
                    hull.Box(Point(0f, 0.38f, -0.57f), Extent(0.16f, 0.16f, 0.03f), mana);
                    Strap(b, hull, Point(0f, 0.08f, 0f), Extent(1.54f, 0.12f, 1.14f));
                    if (b.HasAccent) hull.Box(Point(-0.45f, 0.55f, -0.575f), Extent(0.22f, 0.42f, 0.03f), b.Accent);
                    hull = b.Tier(Point(0f, 1.0f, 0f));
                    hull.Taper(Point(0f, 1.0f, 0f), Pair(1.66f, 1.26f), Pair(1.66f, 0.06f), 0.6f, steel);
                    hull.Box(Point(0.5f, 1.5f, -0.25f), Extent(0.22f, 0.8f, 0.22f), stoneMid);
                    break;

                default:
                    return false;
            }
            return true;
        }

        private static bool HumanExtraBuilding(Blueprint b, Role role, int slot)
        {
            Solids hull = b.Hull;
            Color stone = BodyPalette.Humans.Stone, stoneMid = BodyPalette.Humans.StoneMid, stoneDark = BodyPalette.Humans.StoneDark;
            Color steel = BodyPalette.Humans.Steel, wood = BodyPalette.Humans.Wood, bronze = BodyPalette.Humans.Bronze;
            Color mana = BodyPalette.Humans.Mana;

            // Fronts face -z here, the camera's side: a building stands unturned.
            switch (role)
            {
                case Role.Defense when slot == 1:
                {
                    // 전기 타워: a steel pillar with a spinning orb.
                    Solids crown = b.Crown;
                    hull.Prism(Vector3.zero, 8, 1.1f, 1.0f, 0.35f, stone);
                    hull.Prism(Point(0f, 0.35f, 0f), 8, 0.85f, 0.8f, 0.3f, stoneMid);
                    Band(b, hull, Point(0f, 0.16f, 0f), 8, 1.08f, 0.1f);
                    hull = b.Tier(Point(0f, 0.65f, 0f));
                    hull.Prism(Point(0f, 0.65f, 0f), 8, 0.42f, 0.36f, 1.6f, steel);
                    hull.Prism(Point(0f, 1.0f, 0f), 8, 0.45f, 0.45f, 0.08f, bronze);
                    hull.Prism(Point(0f, 1.8f, 0f), 8, 0.4f, 0.4f, 0.08f, bronze);
                    hull.Prism(Point(0f, 2.22f, 0f), 8, 0.3f, 0.36f, 0.12f, stoneMid);
                    hull.Box(Point(0f, 1.4f, 0.4f), Extent(0.1f, 0.3f, 0.03f), BodyPalette.Humans.Mana, Turn(0f, 0f, 20f));

                    crown.Origin = Point(0f, 2.34f, 0f);
                    crown.Ball(Point(0f, 2.62f, 0f), 0.3f, BodyPalette.Humans.Mana);
                    crown.Box(Point(-0.44f, 2.62f, 0f), Extent(0.06f, 0.42f, 0.06f), bronze, Turn(0f, 0f, -18f));
                    crown.Box(Point(0.44f, 2.62f, 0f), Extent(0.06f, 0.42f, 0.06f), bronze, Turn(0f, 0f, 18f));
                    crown.Box(Point(0f, 2.36f, 0f), Extent(0.94f, 0.05f, 0.06f), bronze);
                    b.CrownSpin = 60f;
                    return true;
                }

                case Role.Defense when slot == 2:
                {
                    // 돌 포탑: a round turret with a timber throwing arm.
                    Solids crown = b.Crown;
                    hull.Prism(Vector3.zero, 10, 0.95f, 0.9f, 0.7f, stone);
                    Band(b, hull, Point(0f, 0.5f, 0f), 10, 0.93f, 0.1f);
                    hull = b.Tier(Point(0f, 0.68f, 0f));
                    hull.Prism(Point(0f, 0.7f, 0f), 10, 0.9f, 0.85f, 0.7f, stone);
                    hull.Prism(Point(0f, 0.7f, 0f), 10, 0.92f, 0.92f, 0.05f, stoneMid);
                    for (int i = 0; i < 8; i++)
                    {
                        float yaw = i * 45f + 22.5f;
                        hull.Box(Around(yaw) * 0.78f + Point(0f, 1.5f, 0f), Extent(0.22f, 0.2f, 0.16f), stoneMid, Quaternion.Euler(0f, yaw, 0f));
                    }

                    crown.Origin = Point(0f, 1.42f, 0f);
                    crown.Box(Point(0f, 1.52f, 0f), Extent(0.5f, 0.2f, 0.5f), wood);
                    crown.Box(Point(0f, 1.82f, -0.2f), Extent(0.12f, 0.12f, 0.9f), wood, Turn(-35f, 0f, 0f));
                    crown.Box(Point(0f, 2.08f, -0.58f), Extent(0.24f, 0.08f, 0.24f), bronze, Turn(-35f, 0f, 0f));
                    b.CrownAims = true;
                    b.CrownKick = 40f;
                    return true;
                }

                case Role.Production when slot == 1:
                    // 석조 병영: a long low hall under a steel roof, a gatehouse in front, a fenced yard either side.
                    hull.Box(Point(0f, 0.06f, 0f), Extent(2.6f, 0.12f, 1.9f), stoneMid);
                    Strap(b, hull, Point(0f, 0.06f, 0f), Extent(2.64f, 0.06f, 1.94f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        for (int k = 0; k < 3; k++)
                            hull.Box(Point(side * (0.75f + k * 0.25f), 0.26f, -0.82f), Extent(0.06f, 0.3f, 0.06f), wood);
                        hull.Box(Point(side * 1.0f, 0.3f, -0.82f), Extent(0.56f, 0.05f, 0.04f), wood);
                    }
                    hull = b.Tier(Point(0f, 0.12f, 0.3f));
                    hull.Box(Point(0f, 0.52f, 0.3f), Extent(2.2f, 0.8f, 1.0f), stone);
                    hull.Box(Point(0f, 0.6f, -0.205f), Extent(2.2f, 0.06f, 0.02f), stoneMid);
                    hull = b.Tier(Point(0f, 0.92f, 0.3f));
                    hull.Taper(Point(0f, 0.92f, 0.3f), Pair(2.3f, 1.1f), Pair(2.3f, 0.08f), 0.5f, steel);
                    hull = b.Tier(Point(0f, 0.12f, -0.25f));
                    hull.Box(Point(0f, 0.77f, -0.25f), Extent(0.8f, 1.3f, 0.7f), stone);
                    hull.Box(Point(0f, 0.42f, -0.605f), Extent(0.36f, 0.6f, 0.04f), wood);
                    hull.Box(Point(0f, 0.76f, -0.61f), Extent(0.44f, 0.06f, 0.04f), bronze);
                    for (int i = 0; i < 4; i++)
                    {
                        float yaw = 45f + i * 90f;
                        hull.Box(Around(yaw) * 0.42f + Point(0f, 1.48f, -0.25f), Extent(0.16f, 0.16f, 0.16f), stoneMid);
                    }
                    if (b.HasAccent) hull.Box(Point(0f, 1.08f, -0.61f), Extent(0.26f, 0.36f, 0.03f), b.Accent);
                    return true;

                case Role.Production when slot == 2:
                {
                    // 마도 공창: two open workshops, a cannon carriage in one and a mana forge in the other, a crane turning between them.
                    hull.Box(Point(0f, 0.06f, 0f), Extent(2.6f, 0.12f, 1.9f), stoneMid);
                    Strap(b, hull, Point(0f, 0.06f, 0f), Extent(2.64f, 0.06f, 1.94f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        hull = b.Tier(Point(side * 0.85f, 0.12f, 0f));
                        hull.Box(Point(side * 0.85f, 0.62f, 0.5f), Extent(0.9f, 1.0f, 0.14f), stone);
                        hull.Box(Point(side * 1.24f, 0.62f, 0.05f), Extent(0.14f, 1.0f, 1.0f), stone);
                        hull.Box(Point(side * 0.85f, 1.16f, 0.08f), Extent(1.0f, 0.1f, 1.1f), steel);
                        hull.Box(Point(side * 0.85f, 1.1f, -0.47f), Extent(1.0f, 0.12f, 0.06f), wood);
                        if (side < 0)
                        {
                            hull.Prism(Point(-0.85f, 0.38f, -0.2f), 10, 0.1f, 0.08f, 0.6f, stoneDark, Turn(90f, 0f, 0f));
                            hull.Prism(Point(-1.05f, 0.3f, 0f), 10, 0.18f, 0.18f, 0.05f, wood, Turn(0f, 0f, 90f));
                            hull.Prism(Point(-0.65f, 0.3f, 0f), 10, 0.18f, 0.18f, 0.05f, wood, Turn(0f, 0f, -90f));
                        }
                        else
                        {
                            hull.Box(Point(0.85f, 0.32f, 0.05f), Extent(0.5f, 0.4f, 0.4f), stoneDark);
                            hull.Box(Point(0.85f, 0.34f, -0.16f), Extent(0.24f, 0.16f, 0.02f), mana);
                        }
                    }
                    hull = b.Tier(Point(0f, 0.12f, 0.3f));
                    hull.Box(Point(-0.22f, 1.0f, 0.3f), Extent(0.14f, 1.8f, 0.14f), wood);
                    hull.Box(Point(0.22f, 1.0f, 0.3f), Extent(0.14f, 1.8f, 0.14f), wood);
                    hull.Box(Point(0f, 1.86f, 0.3f), Extent(0.6f, 0.12f, 0.18f), bronze);
                    if (b.HasAccent) hull.Box(Point(0f, 1.5f, 0.22f), Extent(0.3f, 0.4f, 0.03f), b.Accent);

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 1.92f, 0.3f);
                    crown.Box(Point(0f, 2.0f, 0.3f), Extent(0.18f, 0.08f, 0.18f), steel);
                    crown.Box(Point(0f, 2.06f, -0.15f), Extent(0.1f, 0.1f, 1.1f), bronze);
                    crown.Box(Point(0f, 1.78f, -0.6f), Extent(0.025f, 0.5f, 0.025f), stoneDark);
                    crown.Box(Point(0f, 1.5f, -0.6f), Extent(0.14f, 0.1f, 0.1f), steel);
                    b.CrownSpin = 12f;
                    return true;
                }

                case Role.Tech when slot == 1:
                {
                    // 마도 학당: a tall hall under a steep roof between two low wings, a book and a crystal over the door, a crystal turning on the spire.
                    hull.Box(Point(0f, 0.06f, 0f), Extent(1.7f, 0.12f, 1.4f), stoneMid);
                    Strap(b, hull, Point(0f, 0.06f, 0f), Extent(1.74f, 0.06f, 1.44f));
                    hull = b.Tier(Point(0f, 0.12f, 0.05f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        hull.Box(Point(side * 0.56f, 0.46f, 0.05f), Extent(0.5f, 0.68f, 1.0f), stone);
                        hull.Prism(Point(side * 0.56f, 0.8f, 0.05f), 4, 0.42f, 0f, 0.4f, steel);
                        hull.Box(Point(side * 0.56f, 0.5f, -0.455f), Extent(0.14f, 0.24f, 0.02f), mana);
                    }
                    hull = b.Tier(Point(0f, 0.12f, 0f));
                    hull.Box(Point(0f, 0.8f, 0f), Extent(0.7f, 1.36f, 0.8f), stone);
                    hull.Box(Point(0f, 0.36f, -0.41f), Extent(0.26f, 0.48f, 0.03f), wood);
                    hull.Box(Point(0f, 0.86f, -0.415f), Extent(0.34f, 0.22f, 0.03f), BodyPalette.Humans.Canvas);
                    hull.Box(Point(0f, 0.86f, -0.43f), Extent(0.03f, 0.22f, 0.02f), bronze);
                    hull.Box(Point(0f, 1.18f, -0.415f), Extent(0.12f, 0.2f, 0.03f), mana);
                    hull = b.Tier(Point(0f, 1.48f, 0f));
                    hull.Prism(Point(0f, 1.48f, 0f), 4, 0.62f, 0f, 0.75f, steel);
                    hull.Prism(Point(0f, 2.1f, 0f), 6, 0.04f, 0.04f, 0.3f, bronze);

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 2.4f, 0f);
                    crown.Prism(Point(0f, 2.48f, 0f), 4, 0.12f, 0f, 0.22f, mana);
                    crown.Prism(Point(0f, 2.48f, 0f), 4, 0.12f, 0f, 0.14f, mana, Turn(180f, 0f, 0f));
                    crown.Box(Point(0f, 2.48f, 0f), Extent(0.36f, 0.03f, 0.03f), bronze);
                    b.CrownSpin = 50f;
                    return true;
                }

                case Role.Tech when slot == 2:
                {
                    // 결속대: a low platform between two hooked stone posts, bronze chains hanging to an empty slab, a binding ring turning over it.
                    hull.Box(Point(0f, 0.12f, 0f), Extent(1.6f, 0.24f, 1.2f), stone);
                    hull.Box(Point(0f, 0.27f, 0f), Extent(0.8f, 0.06f, 0.6f), stoneMid);
                    for (int i = 0; i < 4; i++)
                        hull.Box(Point(i % 2 == 0 ? -0.72f : 0.72f, 0.2f, i < 2 ? -0.52f : 0.52f), Extent(0.2f, 0.18f, 0.2f), steel);
                    Strap(b, hull, Point(0f, 0.1f, 0f), Extent(1.64f, 0.08f, 1.24f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        hull = b.Tier(Point(side * 0.62f, 0.24f, 0f));
                        hull.Box(Point(side * 0.62f, 0.74f, 0f), Extent(0.26f, 1.0f, 0.3f), stone);
                        hull.Box(Point(side * 0.48f, 1.32f, 0f), Extent(0.4f, 0.18f, 0.3f), stoneMid);
                        hull.Box(Point(side * 0.32f, 1.18f, 0f), Extent(0.1f, 0.18f, 0.3f), stoneMid);
                        hull.Box(Point(side * 0.62f, 0.56f, -0.16f), Extent(0.3f, 0.5f, 0.04f), steel);
                        for (int k = 0; k < 5; k++)
                        {
                            float t = k / 4f;
                            hull.Box(Point(side * Mathf.Lerp(0.32f, 0.14f, t), Mathf.Lerp(1.06f, 0.4f, t), 0f), Extent(0.06f, 0.1f, 0.03f), bronze,
                                     Turn(0f, k % 2 == 0 ? 0f : 90f, side * 20f));
                        }
                    }

                    Solids crown = b.Crown;
                    crown.Origin = Point(0f, 0.85f, 0f);
                    for (int i = 0; i < 8; i++)
                    {
                        float yaw = i * 45f;
                        crown.Box(Around(yaw) * 0.22f + Point(0f, 0.85f, 0f), Extent(0.1f, 0.05f, 0.05f), bronze, Quaternion.Euler(0f, yaw + 90f, 0f));
                    }
                    b.CrownSpin = 30f;
                    return true;
                }

                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // Things that belong to nobody
        // ------------------------------------------------------------------

        /// <summary>
        /// The map's neutral 꼬마돌: 돌 골렘 부족's worker model with no owner
        /// accent, tinted UiStyle.Neutral. It is drawn with a faction's body and
        /// untinted would claim to be that faction's worker — MatchView's sprite
        /// rule, carried over.
        /// </summary>
        private static Design NeutralRockDesign()
        {
            float size = 0.9f * NeutralRockFootprint;
            var b = new Blueprint(Seed(Faction.RockGolems, Role.Worker, 0), size, false, Color.white)
            {
                Tint = UiStyle.Neutral,
            };
            MiniRock(b);
            return Finish(b, "꼬마돌", NeutralRockFootprint, size, false, false);
        }

        /// <summary>
        /// A resource node: tilted crystal prisms in the HUD's node hue growing
        /// out of a dark rock base, so the field and the minimap agree on what a
        /// node looks like. Seeded, so every node on every peer is the same cluster.
        /// </summary>
        private static Design NodeDesign()
        {
            var b = new Blueprint(0x4E4F4445, 1f, false, Color.white) { Gait = Gait.Still };
            Solids hull = b.Hull;
            System.Random random = b.Random;
            Color node = BodyPalette.Node, lit = BodyPalette.Lighter(node, 0.35f), shade = BodyPalette.Darker(node, 0.78f);

            hull.Lump(Point(0f, 0.1f, 0f), Extent(0.55f, 0.2f, 0.5f), BodyPalette.NodeRock, Turn(0f, 20f, 0f));
            hull.Lump(Point(-0.35f, 0.08f, 0.25f), Extent(0.25f, 0.14f, 0.22f), BodyPalette.Darker(BodyPalette.NodeRock, 0.85f), Turn(0f, 50f, 0f));
            hull.Lump(Point(0.38f, 0.07f, -0.2f), Extent(0.22f, 0.12f, 0.2f), BodyPalette.NodeRock, Turn(0f, -30f, 0f));

            const int crystals = 6;
            for (int i = 0; i < crystals; i++)
            {
                bool middle = i == 0;
                float yaw = middle ? 0f : (i - 1) * 72f + (float)random.NextDouble() * 30f;
                float spread = middle ? 0f : 0.18f + (float)random.NextDouble() * 0.12f;
                float tilt = middle ? 4f : 18f + (float)random.NextDouble() * 16f;
                float radius = middle ? 0.15f : 0.08f + (float)random.NextDouble() * 0.04f;
                float height = middle ? 0.8f : 0.35f + (float)random.NextDouble() * 0.3f;
                Color color = middle ? lit : i % 2 == 0 ? node : shade;

                Quaternion rotation = Outward(yaw, tilt);
                Vector3 foot = Around(yaw) * spread + Point(0f, 0.12f, 0f);
                hull.Prism(foot, 6, radius, radius * 0.9f, height, color, rotation);
                hull.Prism(foot + rotation * Point(0f, height, 0f), 6, radius * 0.9f, 0f, radius * 2.2f, lit, rotation);
            }
            return Finish(b, "ResourceNode", NodeFootprint, 1f, true, false);
        }

        /// <summary>
        /// 잔해: a low heap of jittered blocks filling one cell. Low enough to read
        /// as rubble rather than a wall, wide enough to cover the whole cell, since
        /// the cell is blocked edge to edge and the heap must not show a gap.
        /// </summary>
        private static Design RemnantDesign()
        {
            var b = new Blueprint(0x52454D4E, 1f, false, Color.white) { Gait = Gait.Still };
            Solids hull = b.Hull;
            System.Random random = b.Random;
            Color rubble = UiStyle.Remnant;

            hull.Box(Point(0f, 0.05f, 0f), Extent(0.92f, 0.1f, 0.92f), BodyPalette.Darker(rubble, 0.82f));
            const int pieces = 8;
            for (int i = 0; i < pieces; i++)
            {
                float x = ((float)random.NextDouble() - 0.5f) * 0.6f;
                float z = ((float)random.NextDouble() - 0.5f) * 0.6f;
                float size = 0.2f + (float)random.NextDouble() * 0.16f;
                float height = 0.14f + (float)random.NextDouble() * 0.22f;
                Color color = BodyPalette.Darker(rubble, 0.85f + (float)random.NextDouble() * 0.25f);
                Quaternion rotation = Turn(((float)random.NextDouble() - 0.5f) * 30f, (float)random.NextDouble() * 90f,
                                           ((float)random.NextDouble() - 0.5f) * 30f);
                if (i % 3 == 0) hull.Lump(Point(x, 0.1f + height * 0.4f, z), Extent(size * 0.7f, height * 0.6f, size * 0.7f), color, rotation);
                else hull.Box(Point(x, 0.1f + height * 0.4f, z), Extent(size, height, size * 0.85f), color, rotation);
            }
            return Finish(b, "Remnant", RemnantFootprint, 1f, true, false);
        }
    }
}
