using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WordCraft.View
{
    /// <summary>
    /// What a weapon puts in the air: a tracer flying from the shooter to the
    /// target, and the burst of shards where it lands. Issue #165.
    ///
    /// Both are pooled. A fight between two hundred bodies fires several shots a
    /// frame, and a GameObject or a mesh per shot would be garbage every frame;
    /// here every tracer and every shard is made once, the first time the pool
    /// runs short, and reused from then on. Nothing in <see cref="Animate"/> or in
    /// the three calls that start an effect (<see cref="Shot"/>,
    /// <see cref="Impact"/>, <see cref="Crumble"/>) allocates once the pool is
    /// big enough.
    ///
    /// Drawn with the body material and lit like a body: a tracer is a sliver of
    /// cut paper in the faction's colour, not a glow. .art/STYLE.md bans the
    /// ember and spark scatter that a particle system would reach for, so a
    /// burst is seven shards that fly out and shrink to nothing, big enough to
    /// read at the zoom a fight is watched from, with a ring on the ground
    /// under it. A 돌 골렘 body that dies breaks into chunks here too
    /// (<see cref="Crumble"/>), pooled the same way.
    ///
    /// Issue #206 adds the statuses' marks to the same pools: a burning body's
    /// embers and a heal's motes are the working building's mote chip, two or
    /// three at a time and never a scatter; a burn's tick is a smaller burst;
    /// and a chain's jump is the tracer's spindle laid along a four-leg zigzag
    /// (<see cref="Jump"/>), thick enough to survive a 64 px downscale.
    ///
    /// Issue #212 adds the area abilities: a tracer can come down on an arc
    /// (<see cref="Lob"/>) or straight from the sky (<see cref="Fall"/>), can
    /// be a lump of debris instead of a spindle, and can land with a ring the
    /// size of what it hit (<see cref="Blast"/>); and a zone that stands on a
    /// point is a ring of chips circling it (<see cref="Orbit"/>), drawn afresh
    /// every frame from a pool of its own.
    /// </summary>
    public sealed class Effects
    {
        /// <summary>
        /// Tracer length in cells. Longer than the issue's first 0.3 to 0.5: the
        /// strips showed a half-cell sliver vanishing at the zoom a fight is
        /// watched from.
        /// </summary>
        private const float TracerLength = 0.75f;

        /// <summary>Tracer thickness in cells: thick enough to survive the downscale to a 64 px body.</summary>
        private const float TracerWidth = 0.34f;

        /// <summary>The see-through streak behind a tracer: longer and wider, so the line of fire reads before the tracer does.</summary>
        private const float TrailLength = 1.3f;
        private const float TrailWidth = 0.5f;
        private const float TrailAlpha = 0.35f;

        /// <summary>How fast a tracer flies, in cells a second. A short shot is clamped up to the floor below.</summary>
        private const float TracerSpeed = 14f;

        /// <summary>
        /// Shortest and longest a tracer is in the air: long enough to be seen in
        /// two frames an eighth of a second apart.
        /// </summary>
        private const float MinFlight = 0.24f;
        private const float MaxFlight = 0.28f;

        /// <summary>How long a burst lasts.</summary>
        private const float BurstSeconds = 0.25f;

        /// <summary>Shards a burst throws.</summary>
        private const int ShardsPerBurst = 7;

        /// <summary>Shard size in cells at the start of a burst; it shrinks to nothing.</summary>
        private const float ShardSize = 0.42f;

        /// <summary>How far a shard flies out, in cells.</summary>
        private const float BurstReach = 0.95f;

        /// <summary>
        /// How far every third shard is moved toward paper white: the light catch
        /// that makes a burst read as a burst rather than as more of the body.
        /// </summary>
        private const float ShardLight = 0.6f;

        /// <summary>The ground ring under a burst: from this radius to the outer one, in cells, fading out.</summary>
        private const float RingFrom = 0.2f;
        private const float RingTo = 0.85f;
        private const float RingBand = 0.08f;
        private const float RingAlpha = 0.6f;

        /// <summary>A burst further than this above the ground gets no ring: the ring would be under nothing.</summary>
        private const float RingHeight = 1.2f;

        // A golem's chunks: how many, how long they lie before shrinking away,
        // how fast they shrink, and how hard they fall.
        private const int ChunksPerCrumble = 3;
        private const float ChunkLie = 0.55f;
        private const float ChunkShrink = 0.15f;
        private const float ChunkGravity = 14f;

        /// <summary>How far a shard falls over the burst, in cells, on top of flying out.</summary>
        private const float BurstDrop = 0.25f;

        /// <summary>Pools are made this big up front, so the first fight does not pay for them.</summary>
        private const int TracerWarm = 24;
        private const int BurstWarm = 16;
        private const int CrumbleWarm = 4;
        /// <summary>
        /// Working buildings' motes, burning bodies' embers and heals' motes
        /// share this pool: three a building, three a burning body, two a heal,
        /// so a fight with a handful of each stays inside it.
        /// </summary>
        private const int MoteWarm = 32;

        /// <summary>Chain jumps up at once: 연쇄 번개 hops twice, so four shooters firing together.</summary>
        private const int BoltWarm = 8;

        /// <summary>Zone chips up at once: one 모래폭풍 and one 칼바람 회오리 (issue #212).</summary>
        private const int OrbitWarm = 32;

        // A blast's ring (issue #212): out from a third of the radius to the
        // whole of it, the band a tenth of the radius, slower than a hit's.
        private const float BlastFrom = 0.3f;
        private const float BlastBand = 0.1f;
        private const float BlastBandFloor = 0.14f;
        private const float BlastSeconds = 0.5f;
        private const float BlastAlpha = 0.85f;

        /// <summary>A lobbed tracer's flight: slower than a shot, it goes up first (issue #212).</summary>
        private const float LobFlight = 0.42f;

        /// <summary>How high a lob climbs over its flight, as a share of the ground it covers, floored in cells.</summary>
        private const float LobArc = 0.35f;
        private const float LobArcFloor = 1.2f;

        /// <summary>A hurled lump of debris, in cells across: the rubble pile's own chunk, a little smaller.</summary>
        private const float HurlSize = 0.38f;

        // A zone's chips (issue #212): the chip size, how far in from the
        // radius the innermost ring of them runs, and how high they ride.
        private const float OrbitChip = 0.32f;
        private const float OrbitInner = 0.55f;
        private const float OrbitLift = 0.35f;
        private const float OrbitBob = 0.45f;

        // A building's dust ring (issue #186): out from just inside its
        // footprint to past it, a thicker band than a shot's, slower.
        private const float DustFrom = 0.55f;
        private const float DustTo = 1.25f;
        private const float DustBand = 0.16f;
        private const float DustSeconds = 0.55f;

        // A working building's motes: how long one rises, how far, and its size
        // as a share of the building's footprint, floored and capped so a small
        // building's still shows and a base's is not a boulder.
        private const float MoteSeconds = 1.0f;
        private const float MoteRise = 1.3f;
        private const float MoteShare = 0.13f;
        private const float MoteMin = 0.3f;
        private const float MoteMax = 0.5f;

        // A burning body's embers (issue #206): smaller and quicker than a
        // working building's motes, and they rise less far.
        private const float EmberSeconds = 0.8f;
        private const float EmberRise = 0.9f;

        // A heal's motes (issue #206): one size whatever the body, slower than
        // an ember and up further, so a heal reads as lifting, not burning.
        private const float MendSize = 0.42f;
        private const float MendSeconds = 1.1f;
        private const float MendRise = 1.2f;

        // A chain's bolt (issue #206): how many legs the zigzag has, how far
        // its bends stand off the straight line, how thick a leg is, and how
        // long the whole bolt stands before it thins away.
        private const int BoltLegs = 4;
        private const float BoltBend = 0.32f;
        private const float BoltWidth = 0.26f;
        private const float BoltSeconds = 0.3f;

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Mesh tracerMesh;
        private static Mesh shardMesh;
        private static Mesh trailMesh;
        private static Mesh chunkMesh;

        private struct Tracer
        {
            public bool Live;
            public Vector3 From, To;
            public float Started, Flight;
            public Color Colour;
            /// <summary>The entity id it is flying at, reported in <see cref="Landed"/> when it arrives; -1 for none.</summary>
            public int Target;
            /// <summary>Whether it bursts where it lands. Not into the fog: a burst there would show where a hidden body stands.</summary>
            public bool Bursts;
            /// <summary>The landing burst's size, as a share of a weapon's (issue #212). 1 for a shot.</summary>
            public float Scale;
            /// <summary>How high it climbs over its flight, in cells: 0 flies straight (issue #212).</summary>
            public float Arc;
            /// <summary>A lump of debris rather than a spindle (issue #212).</summary>
            public bool Chunk;
            /// <summary>The radius of the ring it lands with, in cells; 0 for none (issue #212).</summary>
            public float Ring;
        }

        private struct Chunk
        {
            public bool Live;
            public Vector3 From, Velocity;
            public float Started, Rest, Radius, Spin;
        }

        private struct Burst
        {
            public bool Live;
            public Vector3 At;
            public float Started;
            /// <summary>Turns the shard fan about the vertical, so two bursts never throw the same pattern.</summary>
            public float Spin;
            /// <summary>False for a building's dust: the ring alone.</summary>
            public bool Shards;
            /// <summary>The ring's radius from start to end and its band, in cells, and how long it runs.</summary>
            public float From, To, Band, Seconds;
            /// <summary>The shards' size and reach as a share of a weapon's burst: 1 for a hit, less for a burn's tick.</summary>
            public float Scale;
            /// <summary>The ring's alpha at the start, fading to nothing.</summary>
            public float Alpha;
        }

        private struct Floater
        {
            public bool Live;
            public Vector3 From;
            public float Started, Size, Spin;
            /// <summary>How long it rises and how far, in seconds and cells.</summary>
            public float Seconds, Rise;
        }

        private struct Bolt
        {
            public bool Live;
            public Vector3 From, To;
            public float Started;
            /// <summary>Which way the zigzag bends first, so two bolts side by side are not one shape twice.</summary>
            public float Side;
            /// <summary>The entity id it jumps to, reported in <see cref="Landed"/> when it appears; -1 for none.</summary>
            public int Target;
            /// <summary>Whether the landing has been reported.</summary>
            public bool Reported;
        }

        private readonly Transform parent;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private readonly List<Tracer> tracers = new List<Tracer>(TracerWarm);
        private readonly List<MeshRenderer> tracerRenderers = new List<MeshRenderer>(TracerWarm);
        private readonly List<Burst> bursts = new List<Burst>(BurstWarm);
        /// <summary>ShardsPerBurst renderers per burst, burst after burst.</summary>
        private readonly List<MeshRenderer> shardRenderers = new List<MeshRenderer>(BurstWarm * ShardsPerBurst);
        private readonly List<MeshRenderer> trailRenderers = new List<MeshRenderer>(TracerWarm);
        private readonly List<Decal> rings = new List<Decal>(BurstWarm);
        private readonly List<Chunk> chunks = new List<Chunk>(CrumbleWarm * ChunksPerCrumble);
        private readonly List<MeshRenderer> chunkRenderers = new List<MeshRenderer>(CrumbleWarm * ChunksPerCrumble);
        private readonly List<Color> ringColours = new List<Color>(BurstWarm);
        private readonly List<Floater> motes = new List<Floater>(MoteWarm);
        private readonly List<MeshRenderer> moteRenderers = new List<MeshRenderer>(MoteWarm);
        private readonly List<Bolt> bolts = new List<Bolt>(BoltWarm);
        /// <summary>BoltLegs renderers per bolt, bolt after bolt.</summary>
        private readonly List<MeshRenderer> legRenderers = new List<MeshRenderer>(BoltWarm * BoltLegs);
        private int boltCounter;

        /// <summary>A tracer slot's lump of debris, parallel to tracers: drawn instead of the spindle for a hurl.</summary>
        private readonly List<MeshRenderer> hurlRenderers = new List<MeshRenderer>(TracerWarm);

        /// <summary>Zone chips, handed out afresh every frame between <see cref="BeginOrbits"/> and <see cref="EndOrbits"/>.</summary>
        private readonly List<MeshRenderer> orbitRenderers = new List<MeshRenderer>(OrbitWarm);
        private int orbitsUsed;

        private int burstCounter;

        private readonly List<int> landed = new List<int>(TracerWarm);

        /// <summary>
        /// The target ids of the tracers that arrived in the last <see cref="Animate"/>,
        /// one entry per tracer. The caller starts the hit reaction from here, so
        /// a ranged flinch begins the moment the shot is seen to land.
        /// </summary>
        public IReadOnlyList<int> Landed => landed;

        public Effects(Transform parent)
        {
            this.parent = parent;
            for (int i = 0; i < TracerWarm; i++) AddTracer();
            for (int i = 0; i < BurstWarm; i++) AddBurst();
            for (int i = 0; i < CrumbleWarm * ChunksPerCrumble; i++) AddChunk();
            for (int i = 0; i < MoteWarm; i++) AddMote();
            for (int i = 0; i < BoltWarm; i++) AddBolt();
            for (int i = 0; i < OrbitWarm; i++) orbitRenderers.Add(Renderer("OrbitChip", ShardMesh));
        }

        /// <summary>
        /// A tracer from <paramref name="from"/> to <paramref name="to"/>, leaving
        /// after <paramref name="delay"/> seconds, and a burst where it lands.
        /// Returns the seconds from now until it lands, so the target's hit
        /// reaction can be timed to the arrival.
        /// </summary>
        public float Shot(Vector3 from, Vector3 to, Color colour, float delay = 0f, int target = -1, bool bursts = true)
        {
            float flight = Mathf.Clamp(Vector3.Distance(from, to) / TracerSpeed, MinFlight, MaxFlight);
            return Launch(new Tracer
            {
                From = from, To = to, Started = ViewClock.Now + delay, Flight = flight, Colour = colour,
                Target = target, Bursts = bursts, Scale = 1f,
            });
        }

        /// <summary>
        /// A tracer that climbs and comes down on <paramref name="to"/> from
        /// above (issue #212): 용암 폭발's stone and 폭격's bomb, and, as a lump
        /// of debris when <paramref name="chunk"/>, 잔해 투척's throw. Lands with
        /// a ring of <paramref name="ring"/> cells round the ground under it when
        /// that is above zero. Returns the seconds until it lands.
        /// </summary>
        public float Lob(Vector3 from, Vector3 to, Color colour, float delay, int target, float ring, bool chunk = false,
                         bool bursts = true)
        {
            Vector3 across = to - from;
            across.y = 0f;
            return Launch(new Tracer
            {
                From = from, To = to, Started = ViewClock.Now + delay, Flight = LobFlight, Colour = colour,
                Target = target, Bursts = bursts, Scale = 1f, Arc = Mathf.Max(LobArcFloor, across.magnitude * LobArc),
                Chunk = chunk, Ring = ring,
            });
        }

        /// <summary>
        /// A tracer dropping straight onto <paramref name="to"/> from
        /// <paramref name="height"/> cells above it over <paramref name="flight"/>
        /// seconds (issue #212): a 유성우 stone. Bursts at <paramref name="scale"/>
        /// of a weapon's size, with a ring of <paramref name="ring"/> cells when
        /// that is above zero. A negative height rises instead: 간헐천's spout.
        /// </summary>
        public float Fall(Vector3 to, float height, Color colour, float delay, float flight, float scale, float ring,
                          bool bursts = true)
        {
            Vector3 from = to + Vector3.up * height;
            if (height < 0f)
            {
                from = to;
                to -= Vector3.up * height;
            }
            return Launch(new Tracer
            {
                From = from, To = to, Started = ViewClock.Now + delay, Flight = flight, Colour = colour,
                Target = -1, Bursts = bursts, Scale = scale, Ring = ring,
            });
        }

        private float Launch(Tracer t)
        {
            int i = FreeTracer();
            t.Live = true;
            tracers[i] = t;
            MeshRenderer body = t.Chunk ? hurlRenderers[i] : tracerRenderers[i];
            Colour(body, t.Colour);
            Color streak = BodyPalette.Lighter(t.Colour, 0.3f);
            streak.a = TrailAlpha;
            TrailColour(trailRenderers[i], streak);
            tracerRenderers[i].enabled = false; // until it leaves
            hurlRenderers[i].enabled = false;
            trailRenderers[i].enabled = false;
            return t.Started - ViewClock.Now + t.Flight;
        }

        /// <summary>
        /// A ring running out from a third of <paramref name="radius"/> to the
        /// whole of it over the ground round <paramref name="foot"/> (issue #212):
        /// the reach of a burst, a blast or a strike, said once as it lands. The
        /// impact ring of a weapon is under a body; this one is the area.
        /// </summary>
        public void Blast(Vector3 foot, float radius, Color colour, float delay = 0f, float alpha = BlastAlpha)
        {
            int i = FreeBurst();
            bursts[i] = new Burst
            {
                Live = true, At = foot, Started = ViewClock.Now + delay, Shards = false,
                From = radius * BlastFrom, To = radius, Band = Mathf.Max(BlastBandFloor, radius * BlastBand),
                Seconds = BlastSeconds, Scale = 1f, Alpha = alpha,
            };
            ringColours[i] = colour;
            rings[i].Visible = false;
        }

        /// <summary>Starts a frame's zones: every chip handed out last frame is free again.</summary>
        public void BeginOrbits() => orbitsUsed = 0;

        /// <summary>
        /// <paramref name="count"/> chips circling <paramref name="centre"/> on
        /// the ground at <paramref name="radius"/> cells and inside it, turning
        /// <paramref name="turn"/> degrees a second, this frame only (issue
        /// #212): a zone that stands on a point for as long as its clock runs.
        /// Every other chip is darker, so the ring reads as turning in a still.
        /// </summary>
        public void Orbit(Vector2 centre, float radius, Color colour, int count, float turn, float seed)
        {
            float now = ViewClock.Now;
            Color shade = BodyPalette.Darker(colour, 0.75f);
            for (int k = 0; k < count; k++)
            {
                if (orbitsUsed >= orbitRenderers.Count) orbitRenderers.Add(Renderer("OrbitChip", ShardMesh));
                MeshRenderer r = orbitRenderers[orbitsUsed++];
                // Three bands from OrbitInner of the radius out to all of it,
                // each turning at its own share of the rate, so the ring churns.
                int band = k % 3;
                float reach = radius * Mathf.Lerp(OrbitInner, 1f, band / 2f);
                float angle = (seed + now * turn * (1f - 0.18f * band) + k * (360f / count)) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float lift = OrbitLift + OrbitBob * (0.5f + 0.5f * Mathf.Sin(now * 3f + k * 1.7f));
                Transform chip = r.transform;
                chip.position = Field.At(centre + dir * reach, lift);
                chip.rotation = Quaternion.Euler(now * 220f + k * 40f, -angle * Mathf.Rad2Deg, now * 160f);
                chip.localScale = Vector3.one * OrbitChip * (band == 2 ? 1.2f : 1f);
                Colour(r, k % 2 == 0 ? colour : shade);
                r.enabled = true;
            }
        }

        /// <summary>Ends a frame's zones: every chip not handed out this frame goes dark.</summary>
        public void EndOrbits()
        {
            for (int i = orbitsUsed; i < orbitRenderers.Count; i++) orbitRenderers[i].enabled = false;
        }

        /// <summary>
        /// A body breaking into chunks where it stands on <paramref name="foot"/>:
        /// lumps thrown a little out of the body that fall, bounce once, lie for
        /// a moment and shrink away. Three for a 돌 골렘 body, leaving the rubble
        /// pile MatchView draws on the cell; two to four of a razed building in
        /// its own colour (issue #186), <paramref name="count"/> by its size.
        /// </summary>
        public void Crumble(Vector3 foot, float size, Color colour, float yaw, float delay = 0f, int count = ChunksPerCrumble)
        {
            for (int k = 0; k < count; k++)
            {
                int i = FreeChunk();
                float around = yaw + 40f + k * (360f / count);
                Vector3 outward = Quaternion.Euler(0f, around, 0f) * Vector3.forward;
                float radius = size * (0.17f - 0.03f * k);
                chunks[i] = new Chunk
                {
                    Live = true,
                    From = foot + outward * (size * 0.1f) + Vector3.up * (size * (0.35f + 0.15f * k)),
                    Velocity = outward * (size * (0.45f + 0.2f * k)) + Vector3.up * (size * 0.5f),
                    Started = ViewClock.Now + delay + 0.04f * k,
                    Rest = foot.y + radius * 0.55f,
                    Radius = radius,
                    Spin = (k % 2 == 0 ? 1f : -1f) * (300f + 120f * k),
                };
                Colour(chunkRenderers[i], BodyPalette.Darker(colour, k % 3 == 1 ? 0.82f : k % 3 == 2 ? 0.92f : 1f));
                chunkRenderers[i].enabled = false;
            }
        }

        /// <summary>
        /// Shards bursting out of <paramref name="at"/>, after <paramref name="delay"/>
        /// seconds: a melee blow lands when the swing does, not when it starts.
        /// </summary>
        public void Impact(Vector3 at, Color colour, float delay = 0f) => Impact(at, colour, delay, 1f);

        /// <summary>
        /// The same burst at <paramref name="scale"/> of a weapon's size: shards,
        /// their reach and the ring under them all shrink together. A burn's
        /// tick is a small one (issue #206).
        /// </summary>
        public void Impact(Vector3 at, Color colour, float delay, float scale)
        {
            int i = FreeBurst();
            burstCounter++;
            bursts[i] = new Burst
            {
                Live = true, At = at, Started = ViewClock.Now + delay, Spin = burstCounter * 47f,
                Shards = true, From = RingFrom * scale, To = RingTo * scale, Band = RingBand, Seconds = BurstSeconds,
                Scale = scale, Alpha = RingAlpha,
            };

            Color light = Color.Lerp(colour, BodyPalette.HitFlash, ShardLight);
            for (int s = 0; s < ShardsPerBurst; s++)
            {
                MeshRenderer r = shardRenderers[i * ShardsPerBurst + s];
                Colour(r, s % 3 == 0 ? light : colour);
                r.enabled = false; // until the delay is over
            }

            Color ring = BodyPalette.Lighter(colour, 0.25f);
            ringColours[i] = ring;
            rings[i].Visible = false;
        }

        /// <summary>
        /// A ring of dust running out over the ground round <paramref name="foot"/>,
        /// sized to a building of <paramref name="radius"/>: the one a finished
        /// building raises as it settles, and a razed one as it lands (issue
        /// #186). The impact ring, without the shards.
        /// </summary>
        public void Dust(Vector3 foot, float radius, Color colour, float delay = 0f, float alpha = RingAlpha)
        {
            int i = FreeBurst();
            bursts[i] = new Burst
            {
                Live = true, At = foot, Started = ViewClock.Now + delay, Shards = false,
                From = radius * DustFrom, To = radius * DustTo, Band = Mathf.Max(RingBand, radius * DustBand),
                Seconds = DustSeconds, Scale = 1f, Alpha = alpha,
            };
            ringColours[i] = colour;
            rings[i].Visible = false;
        }

        /// <summary>
        /// One mote rising off a working building from <paramref name="at"/>:
        /// a chip of its faction's colour that drifts up, turning, and shrinks
        /// away over a second (issue #186). Sized to the building's
        /// <paramref name="footprint"/>.
        /// </summary>
        public void Mote(Vector3 at, Color colour, float footprint) =>
            Rise(at, Color.Lerp(colour, BodyPalette.HitFlash, 0.3f), Mathf.Clamp(footprint * MoteShare, MoteMin, MoteMax),
                 MoteSeconds, MoteRise);

        /// <summary>
        /// One ember rising off a burning body from <paramref name="at"/>: the
        /// mote's chip, <paramref name="size"/> cells across, in the colour as
        /// given, up less far and gone sooner (issue #206). From the motes' pool.
        /// </summary>
        public void Ember(Vector3 at, Color colour, float size) => Rise(at, colour, size, EmberSeconds, EmberRise);

        /// <summary>One mote rising off a body a healing aura just healed (issue #206), in the colour as given.</summary>
        public void Mend(Vector3 at, Color colour) => Rise(at, colour, MendSize, MendSeconds, MendRise);

        private void Rise(Vector3 at, Color colour, float size, float seconds, float rise)
        {
            int i = FreeMote();
            motes[i] = new Floater
            {
                Live = true, From = at, Started = ViewClock.Now, Size = size, Spin = (i * 83f) % 360f,
                Seconds = seconds, Rise = rise,
            };
            Colour(moteRenderers[i], colour);
            moteRenderers[i].enabled = false;
        }

        /// <summary>
        /// A chain's jump from <paramref name="from"/> to <paramref name="to"/>,
        /// after <paramref name="delay"/> seconds (issue #206): the tracer's
        /// spindle laid end to end along a zigzag of <see cref="BoltLegs"/> legs,
        /// all of it there at once, as a jump is, then thinning away. Thick legs
        /// and few bends, because .art/STYLE.md rejects a thin scattered bolt.
        /// </summary>
        /// <remarks>
        /// <paramref name="target"/> is reported in <see cref="Landed"/> the
        /// moment the bolt appears, and <see cref="Incoming"/> counts it until
        /// then, so a body the chain jumped to flinches, flashes and falls when
        /// it is seen to be struck, as a tracer's target does.
        /// </remarks>
        public void Jump(Vector3 from, Vector3 to, Color colour, float delay = 0f, int target = -1)
        {
            int i = FreeBolt();
            boltCounter++;
            bolts[i] = new Bolt
            {
                Live = true, From = from, To = to, Started = ViewClock.Now + delay,
                Side = boltCounter % 2 == 0 ? 1f : -1f, Target = target,
            };
            for (int k = 0; k < BoltLegs; k++)
            {
                MeshRenderer r = legRenderers[i * BoltLegs + k];
                Colour(r, k % 2 == 0 ? colour : BodyPalette.Lighter(colour, 0.35f));
                r.enabled = false;
            }
        }

        /// <summary>One frame at <see cref="ViewClock.Now"/>: tracers fly and land, shards spread and shrink.</summary>
        public void Animate()
        {
            float now = ViewClock.Now;
            landed.Clear();

            for (int i = 0; i < tracers.Count; i++)
            {
                Tracer t = tracers[i];
                if (!t.Live) continue;

                float p = (now - t.Started) / t.Flight;
                MeshRenderer r = t.Chunk ? hurlRenderers[i] : tracerRenderers[i];
                MeshRenderer trail = trailRenderers[i];
                if (p < 0f) continue;
                if (p >= 1f)
                {
                    t.Live = false;
                    tracers[i] = t;
                    r.enabled = false;
                    trail.enabled = false;
                    if (t.Bursts) Impact(t.To, t.Colour, 0f, t.Scale);
                    if (t.Ring > 0f) Blast(Field.At(new Vector2(t.To.x, t.To.z)), t.Ring, BodyPalette.Lighter(t.Colour, 0.2f));
                    if (t.Target >= 0) landed.Add(t.Target);
                    continue;
                }
                Fly(r.transform, t, p);
                if (t.Chunk)
                {
                    // A lump tumbles; it has no nose to lead with and no streak.
                    r.transform.rotation = Quaternion.Euler(p * 540f, p * 200f, p * 320f);
                    r.transform.localScale = Vector3.one * HurlSize;
                    r.enabled = true;
                    continue;
                }
                r.enabled = true;

                // The streak hangs off the tracer's tail, never longer than the
                // ground already covered, so it does not reach back into the shooter.
                float covered = Vector3.Distance(t.From, t.To) * p;
                trail.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                trail.transform.localScale = new Vector3(TrailWidth, TrailWidth, Mathf.Min(TrailLength, covered + TracerLength * 0.5f));
                trail.enabled = true;
            }

            for (int i = 0; i < bursts.Count; i++)
            {
                Burst b = bursts[i];
                if (!b.Live) continue;

                float p = (now - b.Started) / b.Seconds;
                if (p < 0f) continue;
                bool over = p >= 1f;
                if (over)
                {
                    b.Live = false;
                    bursts[i] = b;
                }

                // Out fast and slowing, falling a little, and shrinking to
                // nothing: one ease and one shrink, no fade, since a fade would
                // need the see-through material and its second pass.
                float spread = BodyRig.EaseOut(p);
                float size = ShardSize * b.Scale * (1f - p);

                Decal ring = rings[i];
                bool low = b.At.y - Field.Height(b.At.x, b.At.z) < RingHeight;
                ring.Visible = !over && low;
                if (ring.Visible)
                {
                    ring.Set(new Vector2(b.At.x, b.At.z), Mathf.Lerp(b.From, b.To, spread), b.Band);
                    Color c = ringColours[i];
                    c.a = b.Alpha * (1f - p);
                    ring.Colour = c;
                }
                for (int s = 0; s < ShardsPerBurst; s++)
                {
                    MeshRenderer r = shardRenderers[i * ShardsPerBurst + s];
                    if (over || !b.Shards)
                    {
                        r.enabled = false;
                        continue;
                    }

                    float yaw = b.Spin + s * (360f / ShardsPerBurst);
                    float rise = (s % 2 == 0) ? 0.55f : 0.15f;
                    Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, rise, 1f).normalized;
                    Transform shard = r.transform;
                    shard.position = b.At + outward * (BurstReach * b.Scale * spread) + Vector3.down * (BurstDrop * b.Scale * p * p);
                    shard.rotation = Quaternion.Euler(p * 300f + s * 70f, yaw, p * 200f);
                    shard.localScale = Vector3.one * Mathf.Max(size, 0.001f);
                    r.enabled = true;
                }
            }

            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk c = chunks[i];
                if (!c.Live) continue;

                float t = now - c.Started;
                MeshRenderer r = chunkRenderers[i];
                if (t < 0f) continue;
                if (t >= ChunkLie + ChunkShrink)
                {
                    c.Live = false;
                    chunks[i] = c;
                    r.enabled = false;
                    continue;
                }

                Vector3 at = Ballistic(c, t, out bool resting);
                float shrink = t > ChunkLie ? 1f - (t - ChunkLie) / ChunkShrink : 1f;
                Transform chunk = r.transform;
                chunk.position = at;
                if (!resting) chunk.rotation = Quaternion.Euler(c.Spin * t * 0.6f, c.Spin * 0.3f, c.Spin * t);
                chunk.localScale = Vector3.one * Mathf.Max(c.Radius * shrink, 0.001f);
                r.enabled = true;
            }

            for (int i = 0; i < motes.Count; i++)
            {
                Floater m = motes[i];
                if (!m.Live) continue;

                float p = (now - m.Started) / m.Seconds;
                MeshRenderer r = moteRenderers[i];
                if (p >= 1f)
                {
                    m.Live = false;
                    motes[i] = m;
                    r.enabled = false;
                    continue;
                }

                // Up, easing off, swaying a little; full size by a tenth of the
                // way and shrunk to nothing over the last half.
                float grow = Mathf.Clamp01(p / 0.1f) * (p < 0.5f ? 1f : 1f - (p - 0.5f) / 0.5f);
                Transform mote = r.transform;
                mote.position = m.From + new Vector3(Mathf.Sin(p * 5f + m.Spin) * 0.12f, m.Rise * BodyRig.EaseOut(p), 0f);
                mote.rotation = Quaternion.Euler(20f, m.Spin + p * 240f, 10f);
                mote.localScale = Vector3.one * Mathf.Max(m.Size * grow, 0.001f);
                r.enabled = true;
            }

            for (int i = 0; i < bolts.Count; i++)
            {
                Bolt b = bolts[i];
                if (!b.Live) continue;

                float p = (now - b.Started) / BoltSeconds;
                if (p < 0f) continue;
                if (!b.Reported)
                {
                    b.Reported = true;
                    if (b.Target >= 0) landed.Add(b.Target);
                }
                bool over = p >= 1f;
                if (over) b.Live = false;
                bolts[i] = b;
                // Whole for the first half, then thinning to nothing; the bends
                // flip once at the middle, the one flicker a jump gets.
                float width = BoltWidth * (p < 0.5f ? 1f : 1f - (p - 0.5f) / 0.5f);
                float side = p < 0.5f ? b.Side : -b.Side;
                for (int k = 0; k < BoltLegs; k++)
                {
                    MeshRenderer r = legRenderers[i * BoltLegs + k];
                    if (over)
                    {
                        r.enabled = false;
                        continue;
                    }
                    Vector3 a = Bend(b, k, side), c = Bend(b, k + 1, side);
                    Vector3 along = c - a;
                    if (along.sqrMagnitude < 1e-6f)
                    {
                        r.enabled = false;
                        continue;
                    }
                    // A little longer than the leg, so two legs overlap at the bend.
                    r.transform.SetPositionAndRotation((a + c) * 0.5f, Quaternion.LookRotation(along));
                    r.transform.localScale = new Vector3(Mathf.Max(width, 0.001f), Mathf.Max(width, 0.001f), along.magnitude * 1.2f);
                    r.enabled = true;
                }
            }
        }

        /// <summary>
        /// The <paramref name="k"/>th corner of a bolt's zigzag: the ends on the
        /// line, every corner between them pushed off it to alternate sides,
        /// across the line and a little up, so the bends read from above.
        /// </summary>
        private static Vector3 Bend(Bolt b, int k, float side)
        {
            float t = k / (float)BoltLegs;
            Vector3 point = Vector3.Lerp(b.From, b.To, t);
            if (k == 0 || k == BoltLegs) return point;
            Vector3 along = b.To - b.From;
            Vector3 across = Vector3.Cross(along, Vector3.up);
            if (across.sqrMagnitude < 1e-6f) across = Vector3.right;
            Vector3 off = (across.normalized + Vector3.up * 0.5f) * BoltBend;
            return point + off * (k % 2 == 1 ? side : -side);
        }

        /// <summary>
        /// Seconds until the last tracer flying at <paramref name="target"/>
        /// lands, counting one that has not left yet, or the last chain bolt
        /// jumping to it appears; zero when none is. What a
        /// death waits for, and what tells a hit flash to wait for the landing.
        /// </summary>
        public float Incoming(int target)
        {
            float now = ViewClock.Now, latest = 0f;
            for (int i = 0; i < tracers.Count; i++)
            {
                Tracer t = tracers[i];
                if (!t.Live || t.Target != target) continue;
                latest = Mathf.Max(latest, t.Started + t.Flight - now);
            }
            for (int i = 0; i < bolts.Count; i++)
            {
                Bolt b = bolts[i];
                if (!b.Live || b.Reported || b.Target != target) continue;
                latest = Mathf.Max(latest, b.Started - now);
            }
            return latest;
        }

        /// <summary>
        /// Where a chunk is <paramref name="t"/> seconds after it was thrown: out
        /// and down on a parabola to its resting height, one bounce at a third of
        /// the speed it landed with and less than half the drift, then still.
        /// </summary>
        private static Vector3 Ballistic(Chunk c, float t, out bool resting)
        {
            float g = ChunkGravity;
            float height = c.From.y - c.Rest;
            float vy = c.Velocity.y;
            float land = (vy + Mathf.Sqrt(vy * vy + 2f * g * Mathf.Max(height, 0f))) / g;
            Vector3 drift = new Vector3(c.Velocity.x, 0f, c.Velocity.z);
            resting = false;

            if (t < land)
            {
                Vector3 p = c.From + drift * t;
                p.y = c.From.y + vy * t - 0.5f * g * t * t;
                return p;
            }

            float up = 0.3f * (g * land - vy);
            float hop = 2f * up / g;
            float after = t - land;
            Vector3 landed = c.From + drift * land;
            if (after < hop)
            {
                Vector3 p = landed + drift * (0.4f * after);
                p.y = c.Rest + up * after - 0.5f * g * after * after;
                return p;
            }
            resting = true;
            Vector3 still = landed + drift * (0.4f * hop);
            still.y = c.Rest;
            return still;
        }

        /// <summary>Everything in flight disappears: a new match, or a harness done with a shot.</summary>
        public void Clear()
        {
            for (int i = 0; i < tracers.Count; i++)
            {
                tracers[i] = default;
                tracerRenderers[i].enabled = false;
                hurlRenderers[i].enabled = false;
            }
            orbitsUsed = 0;
            EndOrbits();
            for (int i = 0; i < bursts.Count; i++) bursts[i] = default;
            for (int i = 0; i < shardRenderers.Count; i++) shardRenderers[i].enabled = false;
            for (int i = 0; i < trailRenderers.Count; i++) trailRenderers[i].enabled = false;
            for (int i = 0; i < rings.Count; i++) rings[i].Visible = false;
            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i] = default;
                chunkRenderers[i].enabled = false;
            }
            for (int i = 0; i < motes.Count; i++)
            {
                motes[i] = default;
                moteRenderers[i].enabled = false;
            }
            for (int i = 0; i < bolts.Count; i++) bolts[i] = default;
            for (int i = 0; i < legRenderers.Count; i++) legRenderers[i].enabled = false;
            landed.Clear();
        }

        // ------------------------------------------------------------------
        // Pool
        // ------------------------------------------------------------------

        private int FreeTracer()
        {
            for (int i = 0; i < tracers.Count; i++)
            {
                if (!tracers[i].Live) return i;
            }
            AddTracer();
            return tracers.Count - 1;
        }

        private int FreeBurst()
        {
            for (int i = 0; i < bursts.Count; i++)
            {
                if (!bursts[i].Live) return i;
            }
            AddBurst();
            return bursts.Count - 1;
        }

        private int FreeChunk()
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                if (!chunks[i].Live) return i;
            }
            AddChunk();
            return chunks.Count - 1;
        }

        private int FreeMote()
        {
            for (int i = 0; i < motes.Count; i++)
            {
                if (!motes[i].Live) return i;
            }
            AddMote();
            return motes.Count - 1;
        }

        private int FreeBolt()
        {
            for (int i = 0; i < bolts.Count; i++)
            {
                if (!bolts[i].Live) return i;
            }
            AddBolt();
            return bolts.Count - 1;
        }

        private void AddBolt()
        {
            bolts.Add(default);
            for (int k = 0; k < BoltLegs; k++) legRenderers.Add(Renderer("BoltLeg", TracerMesh));
        }

        private void AddMote()
        {
            motes.Add(default);
            moteRenderers.Add(Renderer("Mote", ShardMesh));
        }

        private void AddTracer()
        {
            tracers.Add(default);
            MeshRenderer r = Renderer("Tracer", TracerMesh);
            r.transform.localScale = new Vector3(TracerWidth, TracerWidth, TracerLength);
            tracerRenderers.Add(r);
            hurlRenderers.Add(Renderer("Hurl", ChunkMesh));

            // Unlit and see-through, like a mark: a streak is the line of fire,
            // not a thing standing in the light.
            var go = new GameObject("Trail");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = TrailMesh;
            var trail = go.AddComponent<MeshRenderer>();
            trail.sharedMaterial = Marks.Ground;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.enabled = false;
            trailRenderers.Add(trail);
        }

        private void AddBurst()
        {
            bursts.Add(default);
            for (int s = 0; s < ShardsPerBurst; s++) shardRenderers.Add(Renderer("Shard", ShardMesh));
            rings.Add(Decal.Make(parent, "ImpactRing", ring: true, Color.clear));
            ringColours.Add(Color.clear);
        }

        private void AddChunk()
        {
            chunks.Add(default);
            MeshRenderer r = Renderer("Chunk", ChunkMesh);
            r.shadowCastingMode = ShadowCastingMode.On;
            chunkRenderers.Add(r);
        }

        private MeshRenderer Renderer(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Models.Opaque;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            return r;
        }

        private void Colour(MeshRenderer r, Color colour)
        {
            block.Clear();
            block.SetColor(TintId, colour);
            r.SetPropertyBlock(block);
        }

        private void TrailColour(MeshRenderer r, Color colour)
        {
            block.Clear();
            block.SetColor(ColorId, colour);
            r.SetPropertyBlock(block);
        }

        /// <summary>
        /// A tracer <paramref name="p"/> of the way along its flight, nose first:
        /// on a straight line, or up and over on a parabola <see cref="Tracer.Arc"/>
        /// cells high at the middle, the nose along the way it is moving.
        /// </summary>
        private static void Fly(Transform transform, Tracer t, float p)
        {
            Vector3 along = t.To - t.From;
            transform.position = t.From + along * p + Vector3.up * (4f * t.Arc * p * (1f - p));
            Vector3 heading = along + Vector3.up * (4f * t.Arc * (1f - 2f * p));
            if (heading.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(heading);
        }

        // ------------------------------------------------------------------
        // Meshes
        // ------------------------------------------------------------------

        /// <summary>
        /// A unit long, unit wide spindle along +z, fat a third of the way from
        /// its nose: two four-sided cones base to base. White, so the property
        /// block's tint is its colour.
        /// </summary>
        private static Mesh TracerMesh
        {
            get
            {
                if (tracerMesh != null) return tracerMesh;
                var solids = new Solids(0x54524143, 0f);
                Quaternion lying = Quaternion.Euler(90f, 0f, 0f);
                solids.Prism(new Vector3(0f, 0f, -0.5f), 4, 0f, 0.5f, 0.68f, Color.white, lying);
                solids.Prism(new Vector3(0f, 0f, 0.18f), 4, 0.5f, 0f, 0.32f, Color.white, lying);
                tracerMesh = solids.Bake("Tracer");
                return tracerMesh;
            }
        }

        /// <summary>
        /// The streak: two quads crossed along -z, a unit long behind the origin
        /// and tapering from a unit wide at the tracer to a fifth of that at the
        /// tail, so it reads from any camera angle. Cull Off on the decal shader
        /// draws both faces.
        /// </summary>
        private static Mesh TrailMesh
        {
            get
            {
                if (trailMesh != null) return trailMesh;
                trailMesh = new Mesh { name = "Trail" };
                trailMesh.SetVertices(new[]
                {
                    new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.1f, 0f, -1f), new Vector3(-0.1f, 0f, -1f),
                    new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.1f, -1f), new Vector3(0f, -0.1f, -1f),
                });
                var white = new Color[8];
                for (int i = 0; i < white.Length; i++) white[i] = Color.white;
                trailMesh.SetColors(white);
                trailMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, 0);
                trailMesh.RecalculateBounds();
                return trailMesh;
            }
        }

        /// <summary>A unit-radius boulder, the bare twenty-face ball the rubble pile is made of.</summary>
        private static Mesh ChunkMesh
        {
            get
            {
                if (chunkMesh != null) return chunkMesh;
                var solids = new Solids(0x43484E4B, 0.05f);
                solids.Lump(Vector3.zero, new Vector3(1f, 0.75f, 0.9f), Color.white, Quaternion.Euler(0f, 20f, 10f));
                chunkMesh = solids.Bake("Chunk");
                return chunkMesh;
            }
        }

        /// <summary>A unit-sized four-sided chip: a pyramid on a shallower one, so it tumbles visibly.</summary>
        private static Mesh ShardMesh
        {
            get
            {
                if (shardMesh != null) return shardMesh;
                var solids = new Solids(0x53484152, 0f);
                solids.Prism(Vector3.zero, 3, 0.55f, 0f, 0.8f, Color.white);
                solids.Prism(Vector3.zero, 3, 0.55f, 0f, 0.35f, Color.white, Quaternion.Euler(180f, 0f, 0f));
                shardMesh = solids.Bake("Shard");
                return shardMesh;
            }
        }
    }
}
