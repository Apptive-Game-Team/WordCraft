using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Renders the field to PNG files from the command line, so that what the
    /// player will see can be looked at without a player, a window or a second
    /// person. Three kinds of shot a run:
    ///
    ///   - five of the scripted match: the home base, the lane and its high
    ///     ground, the whole map, a close-up of bodies, the flank;
    ///   - lineup-&lt;faction&gt;: every roster entry of one faction in a row;
    ///   - strip-&lt;faction&gt;-&lt;motion&gt;: up to four bodies doing one motion,
    ///     eight frames across 0.9 seconds side by side, so an animation can be
    ///     judged from one still image.
    ///
    ///   Unity -batchmode -quit -projectPath Client \
    ///         -executeMethod WordCraft.View.ViewShots.Run \
    ///         -shotsOutput ../shots -shotsTicks 900
    ///
    /// Not -nographics: this needs a GPU to render. The scene is assembled by
    /// hand from the same pieces the view assembles at runtime — Field, FieldMesh,
    /// Sky, Models — and then a camera is pointed at it by CameraRig's own pose
    /// math, so a shot is the game's renderer and not a second one that happens to
    /// agree with it. The scripted log is stepped first, so the shots hold a match
    /// in progress rather than two bases staring at each other.
    ///
    /// Reads the world and writes nothing to it, like the rest of the view.
    /// </summary>
    public static class ViewShots
    {
        private const int Width = 1920;
        private const int Height = 1080;

        /// <summary>Where the camera looks for each shot: frame on the plane, distance, heading.</summary>
        private static readonly (string Name, Vector2 Frame, float Distance, float Yaw)[] Shots =
        {
            ("base", new Vector2(10f, 10f), 30f, 0f),
            ("lane", new Vector2(30f, 36f), 26f, 35f),
            ("overview", new Vector2(32f, 32f), 58f, 0f),
            ("close", new Vector2(10f, 9f), 14f, -30f),
            ("flank", new Vector2(52f, 8f), 22f, 150f),
        };

        public static void Run()
        {
            string output = Arg("-shotsOutput");
            if (string.IsNullOrEmpty(output)) output = "shots";
            int ticks = int.TryParse(Arg("-shotsTicks"), out int parsed) ? parsed : 900;

            try
            {
                Directory.CreateDirectory(output);
                World world = Advance(ticks);
                Shoot(world, output);
                int lineups = Lineups(output);
                int strips = Strips(output);
                int matches = MatchStrips(output);
                Debug.Log("OK: " + (Shots.Length + lineups + strips + matches) + " shots written to " + Path.GetFullPath(output) +
                          " (" + Shots.Length + " field, " + lineups + " lineup, " + strips + " strip, " + matches + " match strip)");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("FAIL: " + e);
                EditorApplication.Exit(1);
            }
            finally
            {
                ViewClock.Release();
            }
        }

        /// <summary>The scripted match, some way in, so there is something on the field to look at.</summary>
        private static World Advance(int ticks)
        {
            World world = MatchScenario.Build(new WordCraft.Net.MatchConfig().Seed,
                ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            List<Command>[] log = ScriptedLog.Build();
            var idle = new List<Command>();
            for (int t = 0; t < ticks; t++) world.Step(t < log.Length ? log[t] : idle);
            return world;
        }

        private static void Shoot(World world, string output)
        {
            using (var studio = new Studio(world, Width, Height))
            {
                Bodies(world, studio.Root.transform);
                foreach (var shot in Shots)
                {
                    CameraRig.Aim(studio.Cam, shot.Frame, shot.Distance, shot.Yaw, hud: false);
                    studio.Render(0);
                    studio.Write(Path.Combine(output, shot.Name + ".png"));
                }
            }
        }

        /// <summary>Every live body, dressed the way MatchView.Dress dresses it, standing where it stands.</summary>
        private static void Bodies(World world, Transform parent)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive) continue;

                bool neutral = world.IsNeutralRock(e);
                Faction faction = e.Owner >= 0
                    ? world.FactionOf(e.Owner)
                    : neutral ? Faction.RockGolems : Faction.TreeSpirits;

                BodyRig body = Models.Build(parent, faction, e.Role, e.Slot, e.Kind, e.Owner, neutral);
                body.Tint(neutral ? UiStyle.Neutral : Color.white);
                body.Show(true);
                body.Progress(1f);
                body.Place(Field.At(MatchRunner.ToView(e.Position)));
                // Nothing moved, so nothing faces anywhere: turn each body a little
                // by id, or a whole army stands in parade formation facing north.
                body.Face((i * 47) % 360);
            }
        }

        // ------------------------------------------------------------------
        // Lineups: every roster entry of one faction standing in a row
        // ------------------------------------------------------------------

        private const int LineupWidth = 2560;
        private const int LineupHeight = 880;

        /// <summary>Cells left empty between two neighbours in a lineup.</summary>
        private const float LineupGap = 0.8f;

        /// <summary>The sim row everything stands on, in cells. Mid map, so the camera never looks off the edge.</summary>
        private const float Row = 32f;

        /// <summary>
        /// One shot per faction of every roster entry it fields — units, the
        /// worker, buildings, the extras behind entry 0 — in Role order and slot
        /// order within a role, on an empty flat map. Not the scenario: a
        /// scenario only shows what its script happens to build, and this is the
        /// whole cast. Buildings stand finished.
        /// </summary>
        private static int Lineups(string output)
        {
            int written = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                World world = Empty(faction);

                var entries = new List<(Role Role, int Slot)>();
                for (int r = 1; r < FactionData.RoleCount; r++)
                {
                    var role = (Role)r;
                    int slots = FactionData.SlotCount(faction, role);
                    for (int slot = 0; slot < slots; slot++)
                    {
                        if (FactionData.Has(faction, role, slot)) entries.Add((role, slot));
                    }
                }

                float span = -LineupGap;
                foreach (var entry in entries) span += MatchView.Cells(entry.Role) + LineupGap;
                float x = World.GridSize * 0.5f - span * 0.5f;
                foreach (var entry in entries)
                {
                    float width = MatchView.Cells(entry.Role);
                    Spawn(world, entry.Role, entry.Slot, new Vector2(x + width * 0.5f, Row));
                    x += width + LineupGap;
                }

                using (var studio = new Studio(world, LineupWidth, LineupHeight))
                {
                    List<BodyRig> rigs = Dress(world, studio.Root.transform);
                    for (int i = 0; i < rigs.Count; i++)
                    {
                        if (world.GetEntity(i).Kind != EntityKind.Building) Turn(rigs[i], FacingYaw);
                    }
                    Settle(rigs);

                    Frame(studio, new Vector2(World.GridSize * 0.5f, Row + 1.2f), span + 4f, 0f);
                    studio.Render(0);
                    studio.Write(Path.Combine(output, "lineup-" + Slug(faction) + ".png"));
                    written++;
                }
            }
            return written;
        }

        // ------------------------------------------------------------------
        // Strips: one motion, eight frames, side by side
        // ------------------------------------------------------------------

        private const int FrameWidth = 640;
        private const int FrameHeight = 360;
        private const int Frames = 8;

        /// <summary>The issue's window: every strip covers this many seconds from the moment the motion starts.</summary>
        private const float StripSeconds = 0.9f;

        /// <summary>How often the rigs are stepped between two frames. A player's frame, so smoothing behaves as it does in a match.</summary>
        private const float StepSeconds = 1f / 60f;

        /// <summary>Idle time run before a motion starts, so speed, facing and breath are settled rather than at their first frame.</summary>
        private const float PrerollSeconds = 1f;

        /// <summary>How long before frame 0 an attack strip calls Attack: about half the wind-up.</summary>
        private const float AttackLead = 0.05f;

        /// <summary>When the motion starts on the held clock. Far from zero, so nothing reads a start time of zero as "never".</summary>
        private const float StartSeconds = 100f;

        /// <summary>Cells between two bodies in a strip.</summary>
        private const float StripSpacing = 2.6f;

        /// <summary>
        /// Three quarters to the right: STYLE.md's "characters face right" seen
        /// through a camera that looks along +z. Pure profile (90) hides the
        /// face; this keeps the face and still shows a lunge as sideways motion.
        /// </summary>
        private const float FacingYaw = 110f;

        /// <summary>How long the walk keeps walking before it stops, so the strip shows a start, a turn and a stop.</summary>
        private const float WalkSeconds = 0.75f;

        /// <summary>
        /// The motions every faction gets a strip of. The first five are the
        /// issue's; the rest are this change's own checks. A faction that grows
        /// also gets a "growth" strip.
        /// </summary>
        private static readonly string[] Motions = { "walk", "attack", "hit", "death", "idle", "spawn", "tower" };

        private static int Strips(string output)
        {
            int written = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                List<(Role Role, int Slot)> walkers = Walkers(faction);
                List<(Role Role, int Slot)> towers = Towers(faction);

                foreach (string motion in Motions)
                {
                    List<(Role Role, int Slot)> cast = motion == "tower" ? towers : walkers;
                    if (cast.Count == 0) continue;
                    Strip(output, faction, motion, cast);
                    written++;
                }

                List<(Role Role, int Slot)> growth = Growths(faction);
                if (growth.Count > 0)
                {
                    Strip(output, faction, "growth", growth);
                    written++;
                }
            }
            return written;
        }

        /// <summary>
        /// Up to four bodies that walk, for the motion strips: entry 0 of every
        /// fighting role first, so the strip shows as many gaits and strikes as
        /// the faction has, then the extras, then the worker.
        /// </summary>
        private static List<(Role Role, int Slot)> Walkers(Faction faction)
        {
            var list = new List<(Role Role, int Slot)>();
            Role[] fighting = { Role.Melee, Role.Ranged, Role.Signature };
            foreach (Role role in fighting)
            {
                if (FactionData.Has(faction, role, 0)) list.Add((role, 0));
            }
            foreach (Role role in fighting)
            {
                int slots = FactionData.SlotCount(faction, role);
                for (int slot = 1; slot < slots; slot++)
                {
                    if (FactionData.Has(faction, role, slot)) list.Add((role, slot));
                }
            }
            if (FactionData.Has(faction, Role.Worker, 0)) list.Add((Role.Worker, 0));
            if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
            return list;
        }

        /// <summary>
        /// 세계수 정령 성장 as pairs: a worker, then the entry it grows into, for
        /// every row of the growth table this faction has. The strip swells the
        /// worker and brings the grown body up in its place, which is what
        /// MatchView does when a body's roster entry changes in sight.
        /// </summary>
        private static List<(Role Role, int Slot)> Growths(Faction faction)
        {
            var list = new List<(Role Role, int Slot)>();
            for (int i = 0; i < FactionData.GrowthTargetCount; i++)
            {
                var row = FactionData.GrowthTarget(i);
                if (row.Faction != faction) continue;
                list.Add((Role.Worker, 0));
                list.Add((row.Role, row.Slot));
            }
            return list;
        }

        /// <summary>Every defense entry: the buildings that turn, fire and kick.</summary>
        private static List<(Role Role, int Slot)> Towers(Faction faction)
        {
            var list = new List<(Role Role, int Slot)>();
            int slots = FactionData.SlotCount(faction, Role.Defense);
            for (int slot = 0; slot < slots; slot++)
            {
                if (FactionData.Has(faction, Role.Defense, slot)) list.Add((Role.Defense, slot));
            }
            return list;
        }

        private static void Strip(string output, Faction faction, string motion, List<(Role Role, int Slot)> cast)
        {
            World world = Empty(faction);
            bool towers = motion == "tower";
            // A growth strip's cast is worker and grown body in pairs, and each
            // pair stands on one spot: the grown body replaces the worker there.
            bool growth = motion == "growth";
            int places = growth ? cast.Count / 2 : cast.Count;
            float spacing = towers ? 4.5f : growth ? StripSpacing * 1.4f : StripSpacing;
            float left = World.GridSize * 0.5f - (places - 1) * spacing * 0.5f;
            var homes = new List<Vector2>(cast.Count);
            for (int i = 0; i < cast.Count; i++)
            {
                var home = new Vector2(left + (growth ? i / 2 : i) * spacing, Row);
                Spawn(world, cast[i].Role, cast[i].Slot, home);
                homes.Add(home);
            }

            using (var studio = new Studio(world, FrameWidth * Frames, FrameHeight, FrameWidth, FrameHeight))
            {
                List<BodyRig> rigs = Dress(world, studio.Root.transform);
                var effects = new Effects(studio.Root.transform);
                var speeds = new float[rigs.Count];
                for (int i = 0; i < rigs.Count; i++)
                {
                    if (!towers) Turn(rigs[i], FacingYaw);
                    if (growth && i % 2 == 1) rigs[i].Spawn(1e6f); // not there until the worker has swelled
                    // Fix is cells a tick; a strip walks at the roster's own pace.
                    speeds[i] = FactionData.Stats(faction, cast[i].Role, cast[i].Slot).Speed.Raw / (float)Fix.One * World.TicksPerSecond;
                }

                float now = StartSeconds - PrerollSeconds;
                var shot = new Shooting { World = world, Faction = faction, Cast = cast, Effects = effects };
                // An attack starts a moment before frame 0, so frame 0 holds its
                // wind-up rather than the rest pose the wind-up starts from.
                float lead = motion == "attack" ? AttackLead : 0f;
                Step(rigs, homes, motion, speeds, shot, ref now, StartSeconds - lead);
                Begin(rigs, motion, shot);
                Step(rigs, homes, motion, speeds, shot, ref now, StartSeconds);
                // Once at the start, so frame 0 is the motion's first pose and
                // not the last idle one.
                for (int i = 0; i < rigs.Count; i++) rigs[i].Animate();
                effects.Animate();

                for (int frame = 0; frame < Frames; frame++)
                {
                    float at = StartSeconds + frame * StripSeconds / (Frames - 1);
                    Step(rigs, homes, motion, speeds, shot, ref now, at);
                    if (frame == 0) Frame(studio, Framing(homes, motion, speeds, out float width), width, 0f);
                    studio.Render(frame * FrameWidth);
                }

                studio.Write(Path.Combine(output, "strip-" + Slug(faction) + "-" + motion + ".png"));
            }
        }

        /// <summary>The one-shot start of a motion, at <see cref="StartSeconds"/>.</summary>
        private static void Begin(List<BodyRig> rigs, string motion, Shooting shot)
        {
            Color colour = BodyPalette.Shot(shot.Faction);
            for (int i = 0; i < rigs.Count; i++)
            {
                switch (motion)
                {
                    case "attack":
                    case "tower":
                        Fire(rigs[i], i, shot);
                        break;
                    case "hit":
                        rigs[i].Hit();
                        // On the side facing the camera, where a shot from in front would land.
                        shot.Effects.Impact(rigs[i].Chest + Vector3.back * rigs[i].Footprint * 0.35f, colour);
                        break;
                    case "death":
                    {
                        bool rubble = MatchView.Rubble(shot.Faction, shot.World.GetEntity(i).Kind);
                        rigs[i].Die(rubble);
                        if (rubble) shot.Effects.Crumble(rigs[i].transform.position, rigs[i].Size, UiStyle.Remnant, rigs[i].transform.eulerAngles.y);
                        break;
                    }
                    case "growth":
                        if (i % 2 == 0) rigs[i].Swell();
                        else rigs[i].Spawn(BodyRig.SwellSeconds);
                        break;
                    case "spawn":
                        rigs[i].Spawn();
                        break;
                }
            }
        }

        /// <summary>What a strip needs to fire a body's weapon the way MatchView.Shoot does.</summary>
        private sealed class Shooting
        {
            /// <summary>The strip's world. Rig i is entity i, because Dress builds them in id order.</summary>
            public World World;
            public Faction Faction;
            public List<(Role Role, int Slot)> Cast;
            public Effects Effects;
        }

        /// <summary>
        /// One attack, with what MatchView.Shoot would put in the air for it: a
        /// tracer to a point ahead for a weapon that reaches past two cells, a
        /// burst a cell ahead when the lunge lands for one that does not. A tower
        /// shoots along its aim instead of its body's facing.
        /// </summary>
        private static void Fire(BodyRig rig, int i, Shooting shot)
        {
            rig.Attack();
            Color colour = BodyPalette.Shot(shot.Faction);
            bool tower = FactionData.IsBuilding(shot.Cast[i].Role);
            Vector3 forward = tower ? Quaternion.Euler(0f, TowerYaw(ViewClock.Now - StartSeconds), 0f) * Vector3.forward : rig.transform.forward;
            bool ranged = MatchView.Throws(FactionData.Stats(shot.Faction, shot.Cast[i].Role, shot.Cast[i].Slot));
            if (ranged)
            {
                Vector3 to = rig.transform.position + forward * (tower ? 4f : 2.4f);
                to.y = rig.Chest.y - (tower ? rig.ChestLift * 0.6f : 0f);
                shot.Effects.Shot(rig.Muzzle, to, colour, tower ? 0f : BodyRig.StrikeDelay);
            }
            else
            {
                shot.Effects.Impact(rig.Chest + forward * 1.1f, colour, BodyRig.LungeLandDelay);
            }
        }

        /// <summary>Where a tower strip's circling target is, as a yaw from the tower, this long into the strip.</summary>
        private static float TowerYaw(float since) => 150f + Mathf.Max(since, 0f) * 100f;

        /// <summary>
        /// Steps every rig from the clock's current time to <paramref name="until"/>
        /// in player-sized frames: places it where the motion has it, turns a
        /// tower toward its moving target, and animates.
        /// </summary>
        private static void Step(List<BodyRig> rigs, List<Vector2> homes, string motion, float[] speeds,
                                 Shooting shot, ref float now, float until)
        {
            while (now < until - 1e-4f)
            {
                float step = Mathf.Min(StepSeconds, until - now);
                now += step;
                ViewClock.Hold(now, step);
                float since = now - StartSeconds;

                for (int i = 0; i < rigs.Count; i++)
                {
                    Vector2 at = homes[i];
                    if (motion == "walk" && since > 0f) at += Walk(since, speeds[i]);
                    rigs[i].Place(Field.At(at));

                    if (motion == "tower")
                    {
                        // A target circling the tower, a quarter turn a strip, so
                        // the crown has to keep turning to follow it.
                        rigs[i].Face(TowerYaw(since));
                        if (since >= StripSeconds * 0.5f && since - step < StripSeconds * 0.5f) Fire(rigs[i], i, shot);
                    }
                    rigs[i].Animate();
                }
                shot.Effects.Animate();
            }
        }

        /// <summary>
        /// Where a walker is <paramref name="since"/> seconds into the walk strip,
        /// relative to home: a third of the walk straight right, a third on a
        /// quarter circle turning toward the camera, a third straight at it, then
        /// a stop. A start, a turn and a stop are the three things a gait has to
        /// get right.
        /// </summary>
        private static Vector2 Walk(float since, float speed)
        {
            float leg = WalkSeconds / 3f;
            float travelled = Mathf.Min(since, WalkSeconds) * speed;
            float straight = leg * speed;
            float radius = straight / (Mathf.PI * 0.5f);

            if (travelled <= straight) return new Vector2(travelled, 0f);
            float turned = travelled - straight;
            if (turned <= straight)
            {
                float angle = turned / radius;
                return new Vector2(straight + Mathf.Sin(angle) * radius, -(1f - Mathf.Cos(angle)) * radius);
            }
            return new Vector2(straight + radius, -radius - (turned - straight));
        }

        /// <summary>Where to point the camera so the whole motion stays in frame, and how many cells wide that is.</summary>
        private static Vector2 Framing(List<Vector2> homes, string motion, float[] speeds, out float width)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < homes.Count; i++)
            {
                Include(ref min, ref max, homes[i]);
                if (motion == "walk") Include(ref min, ref max, homes[i] + Walk(WalkSeconds, speeds[i]));
            }
            float margin = motion == "tower" ? 3f : 2.2f;
            width = max.x - min.x + margin * 2f;
            // Pushed away from the camera by about a body's height, so the frame
            // centres on the bodies rather than on the ground at their feet.
            return new Vector2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f + 1.8f);
        }

        private static void Include(ref Vector2 min, ref Vector2 max, Vector2 p)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        // ------------------------------------------------------------------
        // Match strips: the real MatchView over the scripted match
        // ------------------------------------------------------------------

        /// <summary>Held-clock frames per simulation tick: a 60 fps player over 20 ticks a second.</summary>
        private const int FramesPerTick = 3;

        /// <summary>Ticks between two frames of a match strip: eight frames cover 0.7 s from two ticks before the moment.</summary>
        private const int TicksPerFrame = 2;

        /// <summary>Cells across a match strip's frame: wide enough for a shooter and its target six cells apart.</summary>
        private const float MatchStripCells = 15f;

        /// <summary>Ticks the view runs before a match strip's first frame, so every body has been seen and placed.</summary>
        private const int MatchPreroll = 20;

        /// <summary>
        /// The strips above drive BodyRig and Effects directly, so they never
        /// run MatchView's own reading of the world: a death noticed from Alive,
        /// a cooldown read as a shot, a hit waiting for its tracer, the fog, a
        /// produced body rising. These do. They step the scripted match one tick
        /// every three held-clock frames through MatchView.Draw — the view's own
        /// LateUpdate — over a <see cref="MatchRunner"/> whose state is set by
        /// reflection, because the editor calls none of MatchRunner's lifecycle
        /// and its setters are private. Three moments, found by scanning the
        /// log: the first ranged shot, the first unit death, the first produced
        /// unit. Peer 0's fog is on, as in a match.
        /// </summary>
        private static int MatchStrips(string output)
        {
            List<Command>[] log = ScriptedLog.Build();
            Moments(log, out int shotTick, out Vector2 shotAt, out int killTick, out Vector2 killAt,
                    out int spawnTick, out Vector2 spawnAt);
            Debug.Log("match strips: first ranged shot at tick " + shotTick + ", first unit death at tick " + killTick +
                      ", first produced unit at tick " + spawnTick);

            int written = 0;
            if (shotTick >= 0) { MatchStrip(output, "fight", log, shotTick, shotAt); written++; }
            if (killTick >= 0) { MatchStrip(output, "kill", log, killTick, killAt); written++; }
            if (spawnTick >= 0) { MatchStrip(output, "spawn", log, spawnTick, spawnAt); written++; }
            return written;
        }

        private static World Scripted() =>
            MatchScenario.Build(new WordCraft.Net.MatchConfig().Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);

        /// <summary>
        /// Runs the log once and notes the tick and place of the first ranged
        /// shot, the first death of a unit, and the first unit produced. Read
        /// the way MatchView reads them: a cooldown going up, Alive going false,
        /// an id appearing.
        /// </summary>
        private static void Moments(List<Command>[] log, out int shotTick, out Vector2 shotAt,
                                    out int killTick, out Vector2 killAt, out int spawnTick, out Vector2 spawnAt)
        {
            shotTick = killTick = spawnTick = -1;
            shotAt = killAt = spawnAt = Vector2.zero;
            World world = Scripted();
            var cooldowns = new List<int>();
            var alive = new List<bool>();
            for (int i = 0; i < world.EntityCount; i++)
            {
                cooldowns.Add(world.GetEntity(i).AttackCooldown);
                alive.Add(world.GetEntity(i).Alive);
            }

            for (int t = 0; t < log.Length; t++)
            {
                world.Step(log[t]);
                for (int i = 0; i < world.EntityCount; i++)
                {
                    Entity e = world.GetEntity(i);
                    if (i >= cooldowns.Count)
                    {
                        cooldowns.Add(e.AttackCooldown);
                        alive.Add(e.Alive);
                        if (spawnTick < 0 && e.Kind == EntityKind.Unit)
                        {
                            spawnTick = world.Tick;
                            spawnAt = MatchRunner.ToView(e.Position);
                        }
                        continue;
                    }

                    if (shotTick < 0 && e.Owner >= 0 && e.AttackCooldown > cooldowns[i] && e.TargetId >= 0 &&
                        MatchView.Throws(FactionData.Stats(world.FactionOf(e.Owner), e.Role, e.Slot)))
                    {
                        shotTick = world.Tick;
                        shotAt = (MatchRunner.ToView(e.Position) + MatchRunner.ToView(world.GetEntity(e.TargetId).Position)) * 0.5f;
                    }
                    if (killTick < 0 && alive[i] && !e.Alive && e.Kind == EntityKind.Unit)
                    {
                        killTick = world.Tick;
                        killAt = MatchRunner.ToView(e.Position);
                        Debug.Log("match strips: entity " + i + " (" + e.Role + ", peer " + e.Owner + ") dies at tick " + killTick);
                    }
                    cooldowns[i] = e.AttackCooldown;
                    alive[i] = e.Alive;
                }
            }
        }

        private static void MatchStrip(string output, string name, List<Command>[] log, int moment, Vector2 focus)
        {
            World world = Scripted();
            int first = moment - TicksPerFrame;
            int from = Mathf.Max(first - MatchPreroll, 0);
            var idle = new List<Command>();
            while (world.Tick < from) world.Step(world.Tick < log.Length ? log[world.Tick] : idle);

            var runnerObject = new GameObject("MatchRunner (shots)");
            MatchRunner runner = runnerObject.AddComponent<MatchRunner>();
            MatchView view = null;
            var target = new RenderTexture(FrameWidth, FrameHeight, 24) { antiAliasing = 4 };
            var image = new Texture2D(FrameWidth * Frames, FrameHeight, TextureFormat.RGB24, mipChain: false);

            try
            {
                var seam = new RunnerSeam(runner);
                seam.Start(world);
                view = MatchView.Detached(runner);

                float now = StartSeconds;
                // Before the first frame: one draw so every body exists, then the
                // preroll a tick at a time.
                DrawFrame(view, seam, ref now, 1f);
                while (world.Tick < first)
                {
                    world.Step(world.Tick < log.Length ? log[world.Tick] : idle);
                    seam.Snapshot();
                    for (int f = 1; f <= FramesPerTick; f++) DrawFrame(view, seam, ref now, f / (float)FramesPerTick);
                }

                Camera cam = view.Cam;
                cam.targetTexture = target;
                cam.ResetAspect();
                float halfAcross = Mathf.Tan(CameraRig.FieldOfView * 0.5f * Mathf.Deg2Rad) * (FrameWidth / (float)FrameHeight);
                CameraRig.Aim(cam, focus + new Vector2(0f, 0.6f), MatchStripCells / (2f * halfAcross), 0f, hud: false);

                for (int frame = 0; frame < Frames; frame++)
                {
                    if (frame > 0)
                    {
                        for (int k = 0; k < TicksPerFrame; k++)
                        {
                            world.Step(world.Tick < log.Length ? log[world.Tick] : idle);
                            seam.Snapshot();
                            for (int f = 1; f <= FramesPerTick; f++) DrawFrame(view, seam, ref now, f / (float)FramesPerTick);
                        }
                    }
                    cam.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), frame * FrameWidth, 0);
                    RenderTexture.active = null;
                }

                image.Apply();
                File.WriteAllBytes(Path.Combine(output, "strip-match-" + name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                if (view != null)
                {
                    if (view.Cam != null)
                    {
                        view.Cam.targetTexture = null;
                        UnityEngine.Object.DestroyImmediate(view.Cam.gameObject);
                    }
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                }
                FieldMesh.Drop();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(runnerObject);
                foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    UnityEngine.Object.DestroyImmediate(light.gameObject);
                }
            }
        }

        /// <summary>
        /// One player frame: the clock a sixtieth on, the draw position
        /// <paramref name="alpha"/> of the way through the tick, every enabled
        /// body animated as Update would, then the view drawn as LateUpdate would.
        /// </summary>
        private static void DrawFrame(MatchView view, RunnerSeam seam, ref float now, float alpha)
        {
            now += StepSeconds;
            ViewClock.Hold(now, StepSeconds);
            seam.Alpha(alpha);
            foreach (BodyRig rig in view.GetComponentsInChildren<BodyRig>())
            {
                if (rig.enabled) rig.Animate();
            }
            view.Draw();
        }

        /// <summary>
        /// The MatchRunner state MatchView reads, set from outside: the world,
        /// the local peer, the phase, the two position snapshots the draw
        /// position interpolates between, and the time of the last step that
        /// sets how far between them. All private to MatchRunner, which this
        /// change may not edit, so reflection; a rename there fails here loudly
        /// rather than silently.
        /// </summary>
        private sealed class RunnerSeam
        {
            private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
            private readonly MatchRunner runner;
            private readonly List<Vector2> previous, current;
            private readonly FieldInfo lastStep;

            public RunnerSeam(MatchRunner runner)
            {
                this.runner = runner;
                previous = (List<Vector2>)Field("previous").GetValue(runner);
                current = (List<Vector2>)Field("current").GetValue(runner);
                lastStep = Field("lastStepMs");
            }

            public void Start(World world)
            {
                Set("World", world);
                Set("LocalPeer", 0);
                Set("Phase", Phase.Match);
                PropertyInfo instance = typeof(MatchRunner).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                instance.GetSetMethod(true).Invoke(null, new object[] { runner });
                previous.Clear();
                current.Clear();
                Snapshot();
            }

            /// <summary>What StepOnce does after a tick: the old current becomes previous, and the world is read again.</summary>
            public void Snapshot()
            {
                previous.Clear();
                previous.AddRange(current);
                current.Clear();
                World world = runner.World;
                for (int i = 0; i < world.EntityCount; i++) current.Add(MatchRunner.ToView(world.GetEntity(i).Position));
                while (previous.Count < current.Count) previous.Add(current[previous.Count]);
            }

            /// <summary>Sets the last step so that MatchRunner.Alpha reads <paramref name="alpha"/> now.</summary>
            public void Alpha(float alpha)
            {
                long nowMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
                lastStep.SetValue(runner, nowMs - (long)(alpha * 1000f / World.TicksPerSecond));
            }

            private static FieldInfo Field(string name) =>
                typeof(MatchRunner).GetField(name, Hidden) ?? throw new MissingFieldException("MatchRunner", name);

            private void Set(string property, object value)
            {
                PropertyInfo info = typeof(MatchRunner).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
                                    ?? throw new MissingMemberException("MatchRunner", property);
                info.GetSetMethod(true).Invoke(runner, new[] { value });
            }
        }

        // ------------------------------------------------------------------
        // Shared
        // ------------------------------------------------------------------

        /// <summary>A fresh flat map with one faction on both seats and nothing on it.</summary>
        private static World Empty(Faction faction)
        {
            var world = new World(new WordCraft.Net.MatchConfig().Seed);
            world.SetPeerFaction(0, faction);
            world.SetPeerFaction(1, faction);
            return world;
        }

        private static int Spawn(World world, Role role, int slot, Vector2 at)
        {
            FixVec2 p = MatchRunner.ToSim(at);
            if (FactionData.IsBuilding(role)) return world.SpawnBuilding(0, role, slot, p, complete: true);
            if (role == Role.Worker && slot == 0) return world.SpawnWorker(0, p);
            return world.SpawnUnit(0, role, slot, p);
        }

        /// <summary>Every entity's body, finished and placed, in id order.</summary>
        private static List<BodyRig> Dress(World world, Transform parent)
        {
            var rigs = new List<BodyRig>(world.EntityCount);
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                BodyRig body = Models.Build(parent, world.FactionOf(e.Owner), e.Role, e.Slot, e.Kind, e.Owner, neutral: false);
                body.Show(true);
                body.Progress(1f);
                body.Place(Field.At(MatchRunner.ToView(e.Position)));
                rigs.Add(body);
            }
            return rigs;
        }

        /// <summary>Faces a body at once rather than over the turn rate, which a still shot has no frames for.</summary>
        private static void Turn(BodyRig rig, float yaw)
        {
            rig.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            rig.Face(yaw);
        }

        /// <summary>A second of idle on the held clock, so every rig shows a pose rather than its assembly.</summary>
        private static void Settle(List<BodyRig> rigs)
        {
            for (float t = StartSeconds - PrerollSeconds; t < StartSeconds; t += StepSeconds)
            {
                ViewClock.Hold(t, StepSeconds);
                for (int i = 0; i < rigs.Count; i++) rigs[i].Animate();
            }
        }

        /// <summary>
        /// Points the camera at <paramref name="centre"/> from the player's own
        /// pose math, pulled back until <paramref name="cells"/> fit across the
        /// frame.
        /// </summary>
        private static void Frame(Studio studio, Vector2 centre, float cells, float yaw)
        {
            float aspect = studio.FrameAspect;
            float halfAcross = Mathf.Tan(CameraRig.FieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
            float distance = Mathf.Max(8f, cells / (2f * halfAcross));
            CameraRig.Aim(studio.Cam, centre, distance, yaw, hud: false);
        }

        private static string Slug(Faction faction) => faction.ToString().ToLowerInvariant();

        /// <summary>
        /// The scene every shot is taken in: the field built from one world, the
        /// sky over it, no fog, and a camera rendering into an image that is
        /// written out frame by frame. Torn down completely on dispose, so the
        /// next shot's world starts from nothing.
        /// </summary>
        private sealed class Studio : IDisposable
        {
            public readonly GameObject Root;
            public readonly Camera Cam;
            private readonly RenderTexture target;
            private readonly Texture2D image;
            private readonly int frameWidth, frameHeight;

            public float FrameAspect => frameWidth / (float)frameHeight;

            public Studio(World world, int width, int height) : this(world, width, height, width, height) { }

            public Studio(World world, int width, int height, int frameWidth, int frameHeight)
            {
                this.frameWidth = frameWidth;
                this.frameHeight = frameHeight;
                Root = new GameObject("Shots");
                Cam = new GameObject("ShotCamera").AddComponent<Camera>();
                Cam.orthographic = false;
                Cam.fieldOfView = CameraRig.FieldOfView;
                Cam.nearClipPlane = 0.5f;
                Cam.farClipPlane = 400f;

                target = new RenderTexture(frameWidth, frameHeight, 24) { antiAliasing = 4 };
                image = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);

                Sky.Build(Cam);
                Field.Build(world);
                FieldMesh.Build(world, Root.transform);

                // No fog in a shot: the point is to see the field, not one peer's
                // view of it. The texture is still bound, because the shaders sample
                // it whatever the parameter says.
                Shader.SetGlobalTexture("_WcFog", Fog.Texture);
                Shader.SetGlobalVector("_WcFogParams", new Vector4(1f / World.GridSize, 0f, 0f, 0f));
                Sky.Update(Cam);

                Cam.targetTexture = target;
                Cam.ResetAspect();
            }

            /// <summary>Renders one frame and copies it into the image at <paramref name="x"/> pixels from the left.</summary>
            public void Render(int x)
            {
                Cam.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, frameWidth, frameHeight), x, 0);
                RenderTexture.active = null;
            }

            public void Write(string path)
            {
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }

            public void Dispose()
            {
                Cam.targetTexture = null;
                FieldMesh.Drop();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(Root);
                UnityEngine.Object.DestroyImmediate(Cam.gameObject);
                foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    UnityEngine.Object.DestroyImmediate(light.gameObject);
                }
            }
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
