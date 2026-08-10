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
        private static Sprite sprite;

        /// <summary>
        /// False outside a match. The start screen draws the map behind its menu
        /// and the result screen is the customary post-game reveal; neither is a
        /// place where hiding the map tells the player anything.
        /// </summary>
        public static bool Active { get; private set; }

        /// <summary>Bumped whenever <see cref="state"/> changes, so the minimap can cache.</summary>
        public static int Version { get; private set; }

        /// <summary>
        /// The mask, one texel a cell, drawn over the field as one sprite. Point
        /// filtered like the terrain under it: this style is cut paper, and a
        /// blurred fog edge would be the one thing docs/UI-STYLE.md bans outright,
        /// a gradient.
        /// </summary>
        // ponytail: a hard cell edge is the ceiling. A soft border wants a blur or
        // a shader and a render texture; take that bill the day the blocky edge is
        // the thing people notice rather than the thing they never mention.
        public static Sprite Field()
        {
            if (sprite != null) return sprite;

            texture = new Texture2D(Cells, Cells, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            // One texel to one cell, pivoted in the middle, so it lands on the map
            // at scale one exactly like the baked terrain.
            sprite = Sprite.Create(texture, new Rect(0f, 0f, Cells, Cells), new Vector2(0.5f, 0.5f), 1f);
            return sprite;
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
                Reveal(MatchRunner.ToView(e.Position), Sight(seeing, e));
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
        /// </summary>
        private static bool Static(Entity e) =>
            e.Kind == EntityKind.Building || e.Kind == EntityKind.ResourceNode;

        /// <summary>
        /// Sight, in cells, taken from numbers that already exist rather than a
        /// table invented for the fog.
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
        /// </summary>
        private static float Sight(Faction faction, Entity e) =>
            Static(e)
                ? Mathf.Max(Reach(faction, e.Role), MatchView.Cells(e.Role))
                : Reach(World.AcquireRange);

        private static float Reach(Faction faction, Role role) =>
            Reach(FactionData.Stats(faction, role).Range);

        private static float Reach(Fix f) => f.Raw / (float)Fix.One;

        /// <summary>A disc of light around a point, clipped to the map.</summary>
        private static void Reveal(Vector2 p, float radius)
        {
            int r = Mathf.CeilToInt(radius);
            int cx = (int)p.x;
            int cy = (int)p.y;
            float rr = radius * radius;

            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= Cells) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= Cells) continue;
                    float dx = x + 0.5f - p.x;
                    float dy = y + 0.5f - p.y;
                    if (dx * dx + dy * dy > rr) continue;
                    state[y * Cells + x] = Seen;
                }
            }
        }

        /// <summary>
        /// The mask, repainted on the cadence and not on the frame. Texture row 0
        /// is the bottom one and the sprite is drawn unflipped, so a cell's texel
        /// is at its own coordinates with no flip anywhere.
        /// </summary>
        private static void Paint()
        {
            Field();
            for (int i = 0; i < state.Length; i++)
            {
                pixels[i] = state[i] == Hidden ? UiStyle.FogUnseen
                    : state[i] == Memory ? UiStyle.FogSeen
                    : Color.clear;
            }
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);
        }
    }
}
