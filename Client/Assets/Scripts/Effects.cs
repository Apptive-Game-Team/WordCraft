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
    /// the two calls that start an effect allocates once the pool is big enough.
    ///
    /// Drawn with the body material and lit like a body: a tracer is a sliver of
    /// cut paper in the faction's colour, not a glow. .art/STYLE.md bans the
    /// ember and spark scatter that a particle system would reach for, so a
    /// burst is five shards that fly out and shrink to nothing, big enough to
    /// read at the zoom a fight is watched from.
    /// </summary>
    public sealed class Effects
    {
        /// <summary>Tracer length in cells. The issue asks for 0.3 to 0.5.</summary>
        private const float TracerLength = 0.5f;

        /// <summary>Tracer thickness in cells: thick enough to survive the downscale to a 64 px body.</summary>
        private const float TracerWidth = 0.17f;

        /// <summary>How fast a tracer flies, in cells a second. A short shot is clamped up to the floor below.</summary>
        private const float TracerSpeed = 40f;

        /// <summary>Shortest and longest a tracer is in the air. The issue asks for 0.12 to 0.2 seconds.</summary>
        private const float MinFlight = 0.12f;
        private const float MaxFlight = 0.2f;

        /// <summary>How long a burst lasts.</summary>
        private const float BurstSeconds = 0.25f;

        /// <summary>Shards a burst throws. The issue asks for 4 to 6.</summary>
        private const int ShardsPerBurst = 5;

        /// <summary>Shard size in cells at the start of a burst; it shrinks to nothing.</summary>
        private const float ShardSize = 0.24f;

        /// <summary>How far a shard flies out, in cells.</summary>
        private const float BurstReach = 0.75f;

        /// <summary>How far a shard falls over the burst, in cells, on top of flying out.</summary>
        private const float BurstDrop = 0.25f;

        /// <summary>Pools are made this big up front, so the first fight does not pay for them.</summary>
        private const int TracerWarm = 24;
        private const int BurstWarm = 16;

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static Mesh tracerMesh;
        private static Mesh shardMesh;

        private struct Tracer
        {
            public bool Live;
            public Vector3 From, To;
            public float Started, Flight;
            public Color Colour;
        }

        private struct Burst
        {
            public bool Live;
            public Vector3 At;
            public float Started;
            /// <summary>Turns the shard fan about the vertical, so two bursts never throw the same pattern.</summary>
            public float Spin;
        }

        private readonly Transform parent;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private readonly List<Tracer> tracers = new List<Tracer>(TracerWarm);
        private readonly List<MeshRenderer> tracerRenderers = new List<MeshRenderer>(TracerWarm);
        private readonly List<Burst> bursts = new List<Burst>(BurstWarm);
        /// <summary>ShardsPerBurst renderers per burst, burst after burst.</summary>
        private readonly List<MeshRenderer> shardRenderers = new List<MeshRenderer>(BurstWarm * ShardsPerBurst);

        private int burstCounter;

        public Effects(Transform parent)
        {
            this.parent = parent;
            for (int i = 0; i < TracerWarm; i++) AddTracer();
            for (int i = 0; i < BurstWarm; i++) AddBurst();
        }

        /// <summary>
        /// A tracer from <paramref name="from"/> to <paramref name="to"/>, and a
        /// burst where it lands. Returns the flight time, so a caller can time
        /// something to the landing.
        /// </summary>
        public float Shot(Vector3 from, Vector3 to, Color colour)
        {
            int i = FreeTracer();
            float flight = Mathf.Clamp(Vector3.Distance(from, to) / TracerSpeed, MinFlight, MaxFlight);
            tracers[i] = new Tracer
            {
                Live = true, From = from, To = to, Started = ViewClock.Now, Flight = flight, Colour = colour,
            };

            MeshRenderer r = tracerRenderers[i];
            Colour(r, colour);
            Fly(r.transform, from, to, 0f);
            r.enabled = true;
            return flight;
        }

        /// <summary>
        /// Shards bursting out of <paramref name="at"/>, after <paramref name="delay"/>
        /// seconds: a melee blow lands when the swing does, not when it starts.
        /// </summary>
        public void Impact(Vector3 at, Color colour, float delay = 0f)
        {
            int i = FreeBurst();
            burstCounter++;
            bursts[i] = new Burst
            {
                Live = true, At = at, Started = ViewClock.Now + delay, Spin = burstCounter * 47f,
            };

            for (int s = 0; s < ShardsPerBurst; s++)
            {
                MeshRenderer r = shardRenderers[i * ShardsPerBurst + s];
                Colour(r, colour);
                r.enabled = false; // until the delay is over
            }
        }

        /// <summary>One frame at <see cref="ViewClock.Now"/>: tracers fly and land, shards spread and shrink.</summary>
        public void Animate()
        {
            float now = ViewClock.Now;

            for (int i = 0; i < tracers.Count; i++)
            {
                Tracer t = tracers[i];
                if (!t.Live) continue;

                float p = (now - t.Started) / t.Flight;
                MeshRenderer r = tracerRenderers[i];
                if (p >= 1f)
                {
                    t.Live = false;
                    tracers[i] = t;
                    r.enabled = false;
                    Impact(t.To, t.Colour);
                    continue;
                }
                Fly(r.transform, t.From, t.To, Mathf.Max(p, 0f));
            }

            for (int i = 0; i < bursts.Count; i++)
            {
                Burst b = bursts[i];
                if (!b.Live) continue;

                float p = (now - b.Started) / BurstSeconds;
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
                float spread = 1f - (1f - p) * (1f - p);
                float size = ShardSize * (1f - p);
                for (int s = 0; s < ShardsPerBurst; s++)
                {
                    MeshRenderer r = shardRenderers[i * ShardsPerBurst + s];
                    if (over)
                    {
                        r.enabled = false;
                        continue;
                    }

                    float yaw = b.Spin + s * (360f / ShardsPerBurst);
                    float rise = (s % 2 == 0) ? 0.55f : 0.15f;
                    Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, rise, 1f).normalized;
                    Transform shard = r.transform;
                    shard.position = b.At + outward * (BurstReach * spread) + Vector3.down * (BurstDrop * p * p);
                    shard.rotation = Quaternion.Euler(p * 300f + s * 70f, yaw, p * 200f);
                    shard.localScale = Vector3.one * Mathf.Max(size, 0.001f);
                    r.enabled = true;
                }
            }
        }

        /// <summary>Everything in flight disappears: a new match, or a harness done with a shot.</summary>
        public void Clear()
        {
            for (int i = 0; i < tracers.Count; i++)
            {
                tracers[i] = default;
                tracerRenderers[i].enabled = false;
            }
            for (int i = 0; i < bursts.Count; i++) bursts[i] = default;
            for (int i = 0; i < shardRenderers.Count; i++) shardRenderers[i].enabled = false;
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

        private void AddTracer()
        {
            tracers.Add(default);
            MeshRenderer r = Renderer("Tracer", TracerMesh);
            r.transform.localScale = new Vector3(TracerWidth, TracerWidth, TracerLength);
            tracerRenderers.Add(r);
        }

        private void AddBurst()
        {
            bursts.Add(default);
            for (int s = 0; s < ShardsPerBurst; s++) shardRenderers.Add(Renderer("Shard", ShardMesh));
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

        /// <summary>A tracer <paramref name="p"/> of the way along its flight, nose first.</summary>
        private static void Fly(Transform t, Vector3 from, Vector3 to, float p)
        {
            Vector3 along = to - from;
            t.position = from + along * p;
            if (along.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(along);
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
