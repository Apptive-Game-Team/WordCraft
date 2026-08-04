using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Everything the client makes a noise about: a pool of AudioSources, one
    /// looping bed, and a pass over the world that works out what just happened
    /// by comparing it with what it saw last frame.
    ///
    /// **No part of this reaches the simulation.** It is a reader like the rest of
    /// the view, and it is a reader the same way Minimap is: hp falling between
    /// frames is a hit, Alive going false is a death, a building appearing is a
    /// build. None of that is state the simulation is asked to grow a field for,
    /// and no callback is hung off World.Step to make it easier — a callback
    /// inside Step runs once per tick executed, and a peer catching up executes a
    /// different number of ticks per frame than one that is up to date. The two
    /// would then hear different things, which is harmless, from a hook that has
    /// to be maintained inside the one file where nothing may differ, which is
    /// not. Diffing costs one pass over a few dozen entities and cannot desync
    /// anything, because the simulation never learns it happened.
    ///
    /// Two events are not diffed, because they are not simulation events at all:
    /// a command going out and a menu button being pressed are things the player
    /// did, and Orders and Hud say so directly.
    ///
    /// Created in code from an empty scene like every other component here, so
    /// adding sound adds no scene and no prefab.
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        /// <summary>Where the imported WordOnline clips live, relative to a Resources folder.</summary>
        private const string Folder = "Audio/";

        // ---- mix ----

        private const string MasterKey = "wordcraft.volume.master";
        private const string MusicKey = "wordcraft.volume.music";
        private const string EffectsKey = "wordcraft.volume.effects";

        /// <summary>
        /// Loud enough to be heard, quiet enough not to be the first thing a new
        /// player turns off. Music starts well under the effects rather than off,
        /// because a bed nobody ever enables is a bed nobody wrote.
        /// </summary>
        private const float MasterDefault = 0.8f;
        private const float MusicDefault = 0.35f;
        private const float EffectsDefault = 1.0f;

        // ---- throttle ----
        //
        // Forty units fighting is forty hp drops a tick. Playing all of them is a
        // wall of noise and a pool that spends the whole battle restarting clips,
        // so three limits stack: how many can sound at once, how many may start in
        // one frame, and how often the combat channel is allowed to fire at all.

        /// <summary>One-shots that can sound at once. Past this a new one is dropped.</summary>
        private const int Voices = 8;

        /// <summary>One-shots a frame may start. The nearest to the camera win; see Fire.</summary>
        private const int MaxPerFrame = 3;

        /// <summary>Seconds between combat hits, whatever the battle is doing.</summary>
        private const float HitInterval = 0.08f;

        /// <summary>Full volume out to this many camera half-heights from the middle of the view.</summary>
        private const float NearBand = 0.5f;

        /// <summary>Silent, and dropped without touching a voice, past this many.</summary>
        private const float FarBand = 2f;

        private static Sound instance;

        private static float master = MasterDefault;
        private static float music = MusicDefault;
        private static float effects = EffectsDefault;

        /// <summary>Everything, including the bed. The one a player reaches for first.</summary>
        public static float Master
        {
            get => master;
            set { Set(MasterKey, ref master, value); }
        }

        public static float Music
        {
            get => music;
            set { Set(MusicKey, ref music, value); }
        }

        public static float Effects
        {
            get => effects;
            set { Set(EffectsKey, ref effects, value); }
        }

        private static void Set(string key, ref float level, float value)
        {
            value = Mathf.Clamp01(value);
            if (level == value) return;
            level = value;

            // Set but not saved: a drag is sixty of these a second and Save writes
            // a file. Unity flushes on quit, and Hud saves when the mixer closes.
            PlayerPrefs.SetFloat(key, value);
        }

        // ---- clips ----

        private AudioClip command, menu, hit, unitDeath, buildingDeath, placed, queued, won, lost;
        private AudioClip matchBed, menuBed;

        private AudioSource[] voices;
        private AudioSource bed;

        /// <summary>Frame the command click last played on. One order is one click.</summary>
        private int commandFrame = -1;

        private float nextHit;

        // What the world looked like last frame, per entity id. A drop, a death and
        // a queue that grew are the three things worth hearing that the simulation
        // states rather than announces.
        private readonly List<int> hp = new List<int>();
        private readonly List<bool> alive = new List<bool>();
        private readonly List<int> queue = new List<int>();

        /// <summary>The world the lists above describe. A new one makes them a lie.</summary>
        private World shown;

        /// <summary>Whether the result screen was already up, so the verdict sounds once.</summary>
        private bool wasResult;

        /// <summary>
        /// What wants to be played this frame, before the cap picks. Reused rather
        /// than allocated, because this runs every frame of every battle.
        /// </summary>
        private readonly List<Cue> cues = new List<Cue>();

        private struct Cue
        {
            public AudioClip Clip;
            public float Gain;
            public float Pan;

            /// <summary>Cells from the middle of the view. What the cap sorts on.</summary>
            public float Distance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Sound").AddComponent<Sound>();

        private void Awake()
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

            master = PlayerPrefs.GetFloat(MasterKey, MasterDefault);
            music = PlayerPrefs.GetFloat(MusicKey, MusicDefault);
            effects = PlayerPrefs.GetFloat(EffectsKey, EffectsDefault);

            // The scene is empty and MatchView's camera is a bare Camera component,
            // so nothing else in this client has ever added one of these.
            gameObject.AddComponent<AudioListener>();

            command = Load("field_confirm_v2");
            menu = Load("wood_button_click_v4");
            hit = Load("hit");
            unitDeath = Load("light_explode");
            buildingDeath = Load("explosion_v1");
            placed = Load("magic_drop");
            queued = Load("magic_shoot");
            won = Load("heal");
            lost = Load("magic_explosion");

            matchBed = Load("in-game-bgm", warm: false);
            menuBed = Load("lobby_marimba_round_v1", warm: false);

            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++) voices[i] = NewSource(loop: false);
            bed = NewSource(loop: true);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>
        /// LateUpdate for the same reason MatchView uses it: the frame's ticks have
        /// run by now, so a diff taken here is against the state the player is about
        /// to be shown rather than the one before it.
        /// </summary>
        private void LateUpdate()
        {
            MatchRunner runner = MatchRunner.Instance;
            if (runner == null) return;

            Bed(runner.Phase);
            Watch(runner);
        }

        // ---- the player's own actions ----

        /// <summary>
        /// A command went out. This is the sound that matters: without it a player
        /// cannot tell a click that registered from one that did not without
        /// watching the screen for the result.
        ///
        /// Deduped by frame. An order given to forty selected units is forty calls
        /// to Session.Issue and one thing the player did.
        /// </summary>
        public static void Command()
        {
            if (instance == null || instance.commandFrame == Time.frameCount) return;
            instance.commandFrame = Time.frameCount;
            instance.Ui(instance.command);
        }

        /// <summary>A menu button on the start or result screen.</summary>
        public static void Menu()
        {
            if (instance != null) instance.Ui(instance.menu);
        }

        // ---- what the simulation did ----

        private void Watch(MatchRunner runner)
        {
            World world = runner.World;

            // A restart hands out ids from zero again, so nothing held across one is
            // about the match being played now. Seed and play nothing: the first
            // frame of a match has no previous frame to differ from.
            if (!ReferenceEquals(world, shown))
            {
                shown = world;
                wasResult = runner.Phase == Phase.Result;
                hp.Clear();
                alive.Clear();
                queue.Clear();
                Remember(world);
                return;
            }

            cues.Clear();

            // Entities only ever change while ticks are running, and ticks only run
            // during a match. Skipping the rest of the time also keeps the base that
            // ended the match from exploding over the verdict.
            if (runner.Phase == Phase.Match) Battle(world, runner.LocalPeer);

            Verdict(runner);
            Fire();
        }

        /// <summary>Catches the lists up to the world without deciding anything happened.</summary>
        private void Remember(World world)
        {
            for (int i = hp.Count; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                hp.Add(e.Hp);
                alive.Add(e.Alive);
                queue.Add(e.QueueCount);
            }
        }

        /// <summary>
        /// One pass over every entity, comparing it with last frame. Everything
        /// found goes into the cue list rather than straight to a voice, because the
        /// cap has to see the whole frame before it can prefer the nearest.
        /// </summary>
        private void Battle(World world, int localPeer)
        {
            bool anyHit = false;
            float nearest = 0f;
            Vector2 hitAt = Vector2.zero;

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);

                if (i >= hp.Count)
                {
                    // A building appears the tick the simulation accepts the Build
                    // command, which is the earliest honest confirmation there is:
                    // the ghost under the cursor was a guess, this is the answer.
                    if (e.Kind == EntityKind.Building && e.Owner == localPeer) Mine(placed);
                    continue; // Remember below seeds it; there is no previous frame
                }

                int wasHp = hp[i];
                bool wasAlive = alive[i];
                int wasQueue = queue[i];
                hp[i] = e.Hp;
                alive[i] = e.Alive;
                queue[i] = e.QueueCount;

                // A node runs out rather than dies, and a worker taking from it is
                // not landing a blow. Neither is a casualty.
                if (e.Kind == EntityKind.ResourceNode) continue;

                if (e.QueueCount > wasQueue && e.Owner == localPeer) Mine(queued);

                if (wasAlive && !e.Alive)
                {
                    // 돌 골렘 부족 잔해 gets no clip of its own. Debris lands on the
                    // frame the golem falls, at the golem's own cell, and this line
                    // is already sounding that death there; a second clip on the
                    // same frame at the same place is one event heard twice, and
                    // the frame cap would drop one of them at random anyway. There
                    // is no stone or rubble clip in Resources/Audio either, and
                    // borrowing magic_drop would make one sound mean two things.
                    Add(e.Kind == EntityKind.Building ? buildingDeath : unitDeath,
                        MatchRunner.ToView(e.Position));
                    continue;
                }

                if (e.Hp >= wasHp) continue;

                // Only the nearest hit of the frame is a candidate at all. The rest
                // would be the same clip on top of itself a millisecond later.
                Vector2 p = MatchRunner.ToView(e.Position);
                float d = Range(p);
                if (anyHit && d >= nearest) continue;
                anyHit = true;
                nearest = d;
                hitAt = p;
            }

            Remember(world);

            if (anyHit && Time.unscaledTime >= nextHit)
            {
                nextHit = Time.unscaledTime + HitInterval;
                Add(hit, hitAt);
            }
        }

        /// <summary>
        /// Won or lost, once, when the result screen goes up. A halt is neither, and
        /// gets the losing sound rather than silence: the match stopped, and a
        /// screen that changes without a sound is the thing this whole file is for.
        /// </summary>
        private void Verdict(MatchRunner runner)
        {
            bool over = runner.Phase == Phase.Result;
            if (over == wasResult) return;
            wasResult = over;
            if (!over) return;

            Ui(!runner.Halted && runner.World.Winner == runner.LocalPeer ? won : lost);
        }

        // ---- playing ----

        /// <summary>
        /// The bed for the screen the player is on. Swapped only when it changes, so
        /// a match does not restart its own music every frame.
        /// </summary>
        private void Bed(Phase phase)
        {
            AudioClip want = phase == Phase.Match ? matchBed : menuBed;
            if (want == null)
            {
                bed.Stop();
                return;
            }

            if (bed.clip != want)
            {
                bed.clip = want;
                bed.Play();
            }
            bed.volume = master * music; // set every frame, so a drag is heard as it drags
        }

        /// <summary>
        /// Something the player did, heard wherever the camera is. Distance zero, so
        /// the cap prefers it over any fight: a confirmation the player is waiting
        /// for must not be the thing that gets dropped.
        /// </summary>
        private void Mine(AudioClip clip)
        {
            if (clip != null) cues.Add(new Cue { Clip = clip, Gain = 1f, Pan = 0f, Distance = 0f });
        }

        /// <summary>
        /// Something that happened at a place. Volume falls off across a band
        /// measured in camera half-heights rather than cells, so zooming out to
        /// watch the whole map hears the whole map and zooming into one fight hears
        /// one fight. Past the band it is dropped here and never costs a voice.
        /// </summary>
        private void Add(AudioClip clip, Vector2 point)
        {
            Camera cam = Camera.main;
            if (clip == null || cam == null) return;

            Vector3 c = cam.transform.position;
            float half = cam.orthographicSize;
            float d = Range(point);
            if (d >= half * FarBand) return;

            cues.Add(new Cue
            {
                Clip = clip,
                Gain = d <= half * NearBand
                    ? 1f
                    : 1f - (d - half * NearBand) / (half * (FarBand - NearBand)),
                // A cheap sense of which side of the screen it was on. Half a view
                // wide is hard left or hard right.
                Pan = Mathf.Clamp((point.x - c.x) / (half * cam.aspect), -1f, 1f),
                Distance = d,
            });
        }

        /// <summary>
        /// Up to MaxPerFrame of the frame's cues, nearest the camera first, and the
        /// rest dropped. Dropped rather than queued, because a hit heard a second
        /// after it landed is worse than one never heard, and a queue is a way to
        /// keep spending voices long after the battle that filled it ended.
        /// </summary>
        // ponytail: MaxPerFrame linear scans instead of a sort, because the list is
        // a handful of entries on the worst frame of a battle and a sort would
        // allocate. Sort the day something puts hundreds of cues in here.
        private void Fire()
        {
            for (int played = 0; played < MaxPerFrame; played++)
            {
                int best = -1;
                for (int i = 0; i < cues.Count; i++)
                {
                    if (cues[i].Clip == null) continue; // already taken
                    if (best < 0 || cues[i].Distance < cues[best].Distance) best = i;
                }
                if (best < 0) return;

                Cue cue = cues[best];
                cues[best] = default;
                OneShot(cue.Clip, cue.Gain, cue.Pan);
            }
        }

        /// <summary>A click or a verdict: heard wherever the camera is, past the cap.</summary>
        private void Ui(AudioClip clip)
        {
            if (clip != null) OneShot(clip, 1f, 0f);
        }

        private void OneShot(AudioClip clip, float gain, float pan)
        {
            AudioSource voice = Free();
            if (voice == null) return; // every voice busy: drop it, never queue it

            voice.clip = clip;
            voice.volume = master * effects * gain;
            voice.panStereo = pan;
            voice.Play();
        }

        private AudioSource Free()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].isPlaying) return voices[i];
            }
            return null;
        }

        /// <summary>Cells from the middle of the view. Only the cap and Add read it.</summary>
        private static float Range(Vector2 point)
        {
            Camera cam = Camera.main;
            if (cam == null) return float.MaxValue;
            Vector3 c = cam.transform.position;
            return Vector2.Distance(point, new Vector2(c.x, c.y));
        }

        /// <summary>
        /// 2D sources with the distance worked out here. Unity's 3D falloff wants a
        /// listener that tracks the camera and a rolloff curve, and no built-in
        /// curve can be a multiple of the orthographic size, which is what makes
        /// this one right at both ends of the zoom.
        /// </summary>
        private AudioSource NewSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>
        /// A clip by file name. Warmed on the way past, because the importer leaves
        /// preloadAudioData off and the first play of an unloaded clip would decode
        /// mid-fight. The two beds stream and are not warmed.
        /// </summary>
        private static AudioClip Load(string file, bool warm = true)
        {
            var clip = Resources.Load<AudioClip>(Folder + file);
            if (clip == null)
            {
                // Silent-by-accident is the failure this file exists to remove, so
                // a missing clip says so rather than playing nothing quietly.
                Debug.LogError("missing audio clip: " + Folder + file);
                return null;
            }

            if (warm) clip.LoadAudioData();
            return clip;
        }
    }
}
