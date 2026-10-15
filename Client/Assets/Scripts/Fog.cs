using System;
using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// What this peer can see, one byte a cell.
    ///
    /// Fog is a view: the simulation is untouched, every peer still holds the
    /// whole world, and the state hash does not know this file exists. Each peer
    /// computes vision from its own entities and the screen hides the rest.
    ///
    /// The cost of that choice is a rule this layer cannot express: "you cannot
    /// target what you cannot see". A click still reaches an entity the fog is
    /// drawing black, because the pickers ask the simulation and the simulation
    /// knows everything. Buying that rule means moving vision into Sim, where it
    /// becomes hashed state and every peer computes every other peer's sight.
    /// docs/UI-STYLE.md 안개 has the whole argument.
    ///
    /// Three states, and the byte is the state:
    ///
    ///   Hidden   never seen. Black; nothing there is drawn.
    ///   Memory   seen before, not now. Terrain and the static things this peer
    ///            actually saw stay drawn, dimmed, frozen at what it last saw.
    ///   Seen     inside the sight radius of something of this peer's, right now.
    ///
    /// The ground reads the fog from a texture, not from a sprite laid over it:
    /// <see cref="Texture"/> is one texel a cell, and every shader that includes
    /// Paper.cginc samples it by world x and z (the _WcFog globals), so terrain,
    /// water, props and bodies all fall to the same dark at the same edge.
    ///
    /// Sight is height aware, and only here, in the view. Fog is per local peer,
    /// so the two peers of a match are allowed to disagree about it and the state
    /// hash does not care. Two rules, both skipped by flying units:
    ///
    ///   A seer sees <see cref="HeightSight"/> cells further for every height
    ///   level of the cell it stands on: a unit on a ridge sees further than the
    ///   same unit in the valley.
    ///
    ///   A cell two or more levels above the seer's own is not revealed unless it
    ///   is within <see cref="PlateauSight"/> cells of the seer: you cannot see
    ///   onto a plateau from its foot, only the cliff face in front of you.
    ///
    /// Enemy units are never remembered. A unit that has moved on but is still
    /// drawn where it was is a lie the player acts on, and acting on it loses
    /// armies. Buildings do not move, so remembering them is the truth, and
    /// remembering them is the entire reason to scout.
    ///
    /// Reads the world and writes nothing to it, like the rest of the view.
    /// </summary>
    public static class Fog
    {
        public const byte Hidden = 0;
        public const byte Memory = 1;
        public const byte Seen = 2;

        private const int Cells = MatchScenario.MapSize;

        /// <summary>Cells of extra sight per height level of the cell the seer stands on.</summary>
        private const float HeightSight = 0.6f;

        /// <summary>
        /// How close a seer must be to see a cell two or more levels above its own.
        /// Past this the plateau stays dark; inside it the seer is standing at the
        /// foot of the cliff and can see the rim.
        /// </summary>
        private const float PlateauSight = 1.5f;

        /// <summary>A level difference this big hides the higher cell from a ground seer.</summary>
        private const int PlateauLevels = 2;

        /// <summary>What the texture holds for a cell in full light.</summary>
        private const byte SeenValue = 255;

        /// <summary>What the texture holds for a cell that has never been seen. The shader turns it into UiStyle.FogUnseen.</summary>
        private const byte HiddenValue = 0;

        /// <summary>
        /// What the texture holds for a remembered cell. FogSeen is the fog colour
        /// at part alpha laid over the map, so remembered ground keeps one minus
        /// that alpha of its light: 0.45 of 255, which is 115.
        /// </summary>
        private static readonly byte MemoryValue = (byte)Mathf.RoundToInt((1f - UiStyle.FogSeen.a) * 255f);

        /// <summary>One byte a cell. 4 KB for the whole map, and that is the whole structure.</summary>
        private static readonly byte[] state = new byte[Cells * Cells];

        /// <summary>
        /// Per entity id: was this static thing alive the last time this peer had
        /// eyes on it. Not "is the cell explored" — a building raised after the
        /// scout left stands in explored ground and must not appear, and one
        /// watched falling must not linger.
        /// </summary>
        private static readonly List<bool> recalled = new List<bool>();

        private static readonly Color32[] pixels = new Color32[Cells * Cells];

        private static World shown;
        private static int peer;
        private static int lastTick = -UiStyle.FogTicks;

        private static Texture2D texture;

        /// <summary>
        /// False outside a match. The start screen draws the map behind its menu
        /// and the result screen is the customary post-game reveal; neither is a
        /// place where hiding the map tells the player anything.
        /// </summary>
        public static bool Active { get; private set; }

        /// <summary>Bumped whenever <see cref="state"/> changes, so the minimap can cache.</summary>
        public static int Version { get; private set; }

        /// <summary>
        /// The mask, one texel a cell: texel (x, y) is cell (x, y), and texture row
        /// 0 is sim y 0, which is world z 0, so there is no flip anywhere. The value
        /// is in all four channels: 1 seen, about 0.45 remembered, 0 never seen.
        /// Bilinear and clamped, so the edge of the light is a slope across a cell
        /// rather than a staircase. Created on first ask and lit all over, so a
        /// shader that samples it before the first vision pass shows the whole map.
        /// </summary>
        public static Texture2D Texture
        {
            get
            {
                if (texture != null) return texture;

                texture = new Texture2D(Cells, Cells, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "WordCraft Fog",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                Color32 lit = new Color32(SeenValue, SeenValue, SeenValue, SeenValue);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = lit;
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false);
                PushGlobals();
                return texture;
            }
        }

        /// <summary>
        /// Hands the shaders everything they read about the fog: the texture, the
        /// scale that turns a world position into a texture coordinate (one over
        /// the cells on a side), whether fog is on at all, and the colour unseen
        /// ground falls to. Outside a match the on-switch is 0 and the shaders skip
        /// the fog, so the start screen's map and the result screen's reveal show
        /// the whole ground.
        /// </summary>
        private static void PushGlobals()
        {
            Shader.SetGlobalTexture("_WcFog", texture);
            Shader.SetGlobalVector("_WcFogParams", new Vector4(1f / World.GridSize, Active ? 1f : 0f, 0f, 0f));
            Shader.SetGlobalColor("_WcFogTint", UiStyle.FogUnseen);
        }

        /// <summary>
        /// Recomputes vision, at most once every <see cref="UiStyle.FogTicks"/>
        /// ticks. A unit covers a quarter of a cell per tick, so on that cadence
        /// the fog edge is at most one cell behind the army that owns it, and the
        /// per-frame cost of the whole layer is zero: nothing here runs on a frame
        /// that is not also a tick boundary the cadence lets through.
        /// </summary>
        public static void Update(MatchRunner runner)
        {
            World world = runner.World;
            bool active = runner.Phase == Phase.Match;

            // Sight reads heights, so the field has to be shaped for this world.
            // Idempotent: a world that is already shaped returns at once.
            Field.Build(world);

            if (!ReferenceEquals(world, shown))
            {
                // A restart hands out ids from zero again, so everything held here
                // is about a match that is over.
                shown = world;
                Array.Clear(state, 0, state.Length);
                recalled.Clear();
                lastTick = -UiStyle.FogTicks;
                Version++;
            }

            if (Active != active)
            {
                Active = active;
                Version++;
                // The texture is made first, so the globals never point at nothing.
                _ = Texture;
                PushGlobals();
            }
            if (!active) return;

            peer = runner.LocalPeer;
            for (int i = recalled.Count; i < world.EntityCount; i++) recalled.Add(false);

            if (world.Tick - lastTick < UiStyle.FogTicks) return;
            lastTick = world.Tick;

            // Everything lit last pass falls back to remembered, then the army
            // paints this pass's light over it. One sweep of the array plus one
            // disc a unit, rather than a distance test per cell per unit.
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] == Seen) state[i] = Memory;
            }

            // One lookup for the whole sweep: everything revealed below belongs to
            // the local peer, so every sight radius is read off the same roster.
            Faction seeing = world.FactionOf(peer);

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != peer) continue;
                Reveal(MatchRunner.ToView(e.Position), seeing, e);
            }

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (Static(e) && At(e.Position) == Seen) recalled[i] = e.Alive;
            }

            Version++;
            Paint();
        }

        /// <summary>
        /// How a thing should be drawn: <see cref="Seen"/> live, <see cref="Memory"/>
        /// frozen at what was last seen, <see cref="Hidden"/> not at all. The local
        /// peer's own things are never hidden by its own fog, whatever the cell
        /// under them says.
        /// </summary>
        public static byte Show(int id, Entity e)
        {
            if (!Active || e.Owner == peer || At(e.Position) == Seen) return e.Alive ? Seen : Hidden;
            return Static(e) && id < recalled.Count && recalled[id] ? Memory : Hidden;
        }

        /// <summary>The state of the cell a world point falls in. Clamped at the map edge.</summary>
        public static byte At(FixVec2 p)
        {
            Vector2 v = MatchRunner.ToView(p);
            return AtCell((int)v.x, (int)v.y);
        }

        public static byte AtCell(int cx, int cy)
        {
            if (!Active) return Seen;
            const int last = Cells - 1;
            if (cx < 0) cx = 0; else if (cx > last) cx = last;
            if (cy < 0) cy = 0; else if (cy > last) cy = last;
            return state[cy * Cells + cx];
        }

        /// <summary>
        /// Things that cannot move, and so are the only things worth remembering.
        /// Resource nodes are in: a node the scout found is a place, and a place
        /// does not walk away while nobody is looking at it.
        ///
        /// A neutral 꼬마돌 is in for the same reason and drops out of it by
        /// itself: it has no walk at all while it is neutral, and the moment it is
        /// captured this stops being true of it, which is exactly the moment it
        /// becomes a Towerback that walks. Read through World rather than off the
        /// Kind, because a 꼬마돌 is deliberately an ordinary Unit — the transfer
        /// this remembers across is the one World.SpawnNeutralRock gave up a Kind
        /// of its own to avoid having to repath.
        /// </summary>
        private static bool Static(Entity e) =>
            e.Kind == EntityKind.Building || e.Kind == EntityKind.ResourceNode ||
            (shown != null && shown.IsNeutralRock(e));

        /// <summary>
        /// Sight on level ground, in cells, taken from numbers that already exist
        /// rather than a table invented for the fog. Height changes it afterwards,
        /// in <see cref="Reveal"/>.
        ///
        /// Anything that moves sees <see cref="World.AcquireRange"/>, which is how
        /// far the simulation already lets a unit notice a hostile. Sight shorter
        /// than that would have this peer's own units opening fire on things its
        /// own screen is painting black, which is the one incoherence a view-only
        /// fog can actually avoid. Every weapon in the roster reaches 6 or less, so
        /// a unit sees further than it shoots for free and no scout stat is needed.
        ///
        /// Buildings see less, because scouting is what units are for and a base
        /// that watched a third of the map would make walking anywhere optional. A
        /// building sees the ground it stands on — its own footprint, so it is
        /// never fogged under itself — or as far as it shoots, whichever is more.
        /// That puts a defence tower at 6 and everything else between 2 and 4, all
        /// of them under a unit's 10.
        ///
        /// "As far as it shoots" is <see cref="World.RangeFor"/>, the one place the
        /// simulation reads a weapon's reach. A 대포 사거리 that adds a cell to the
        /// cannon adds it to what the cannon sees, and the roster entry is the one
        /// asked (a cannon and a 전기 타워 share a role) rather than the role's
        /// first row.
        /// </summary>
        private static float Sight(Entity e) =>
            Static(e)
                ? Mathf.Max(Reach(shown.RangeFor(e)), MatchView.Cells(e.Role))
                : Reach(World.AcquireRange);

        private static float Reach(Fix f) => f.Raw / (float)Fix.One;

        /// <summary>
        /// A disc of light around a seer, clipped to the map and shaped by height.
        ///
        /// The radius is the seer's <see cref="Sight"/> plus <see cref="HeightSight"/>
        /// for each level of the cell it stands on. Then a cell that sits
        /// <see cref="PlateauLevels"/> or more above the seer's own level is skipped
        /// when it is further than <see cref="PlateauSight"/> away. A building's own
        /// footprint is the exception to that: it is never fogged under its own
        /// feet, so the near distance grows to the footprint for a static seer.
        /// A flying seer takes neither rule.
        /// </summary>
        private static void Reveal(Vector2 p, Faction faction, Entity e)
        {
            int cx = Mathf.Clamp((int)p.x, 0, Cells - 1);
            int cy = Mathf.Clamp((int)p.y, 0, Cells - 1);
            int level = Field.Level(cy * Cells + cx);

            bool air = FactionData.Stats(faction, e.Role).Air;
            float radius = Sight(e);
            float near = PlateauSight;
            if (!air)
            {
                radius += level * HeightSight;
                if (Static(e)) near = Mathf.Max(near, MatchView.Cells(e.Role));
            }

            int r = Mathf.CeilToInt(radius);
            float radiusSquared = radius * radius;
            float nearSquared = near * near;

            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= Cells) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= Cells) continue;
                    float dx = x + 0.5f - p.x;
                    float dy = y + 0.5f - p.y;
                    float distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared > radiusSquared) continue;
                    if (!air && distanceSquared > nearSquared &&
                        Field.Level(y * Cells + x) >= level + PlateauLevels) continue;
                    state[y * Cells + x] = Seen;
                }
            }
        }

        /// <summary>
        /// The mask, repainted on the cadence and not on the frame, then pushed to
        /// the shaders. The value goes in every channel, so whichever one a shader
        /// reads is the visibility.
        /// </summary>
        private static void Paint()
        {
            Texture2D target = Texture;
            for (int i = 0; i < state.Length; i++)
            {
                byte v = state[i] == Hidden ? HiddenValue : state[i] == Memory ? MemoryValue : SeenValue;
                pixels[i] = new Color32(v, v, v, v);
            }
            target.SetPixels32(pixels);
            target.Apply(updateMipmaps: false);
            PushGlobals();
        }
    }
}
