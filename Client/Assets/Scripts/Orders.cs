using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Turns clicks into commands. Every path out of here is Session.Issue, which
    /// queues the command for Tick + InputDelay on both peers; nothing here
    /// touches the world, so a dropped order is a missing command and never a
    /// simulation that disagrees with the peer's.
    ///
    ///   right click       move, or gather when a worker is aimed at a node
    ///   B then left click place a building
    ///   P                 queue a unit at every selected finished building
    /// </summary>
    public sealed class Orders : MonoBehaviour
    {
        public static Orders Instance { get; private set; }

        private const float PickRadius = 1.1f;

        /// <summary>True while the next left click places a building.</summary>
        public bool BuildMode { get; private set; }

        private MatchRunner runner;
        private Selection selection;
        private Camera cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Orders").AddComponent<Orders>();

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            runner = MatchRunner.Instance;
            selection = Selection.Instance;
        }

        private void Update()
        {
            if (runner == null || selection == null) return;
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            if (runner.Session.State != SessionState.Running)
            {
                BuildMode = false;
                selection.Blocked = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.B)) BuildMode = !BuildMode;
            if (Input.GetKeyDown(KeyCode.Escape)) BuildMode = false;
            // The left button belongs to placement while it is armed, so a
            // placement click does not also redo the selection underneath it.
            selection.Blocked = BuildMode;

            if (Input.GetKeyDown(KeyCode.P)) Produce();

            if (BuildMode && Input.GetMouseButtonDown(0))
            {
                runner.Session.Issue(CommandType.Build, -1, MatchRunner.ToSim(MouseWorld()));
                BuildMode = false;
                selection.Blocked = false;
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                if (BuildMode) BuildMode = false;
                else RightClick(MouseWorld());
            }
        }

        private void RightClick(Vector2 point)
        {
            World world = runner.World;
            int hit = runner.EntityAt(point, PickRadius, mineOnly: false);
            bool node = hit >= 0 && world.GetEntity(hit).Kind == EntityKind.ResourceNode;

            // Aiming at something is still a Move: the simulation has no attack
            // command, CombatSystem acquires anything inside AcquireRange on its
            // own, so walking a unit at an enemy is how a fight is ordered.
            FixVec2 target = hit >= 0 ? world.GetEntity(hit).Position : MatchRunner.ToSim(point);

            for (int i = 0; i < selection.Selected.Count; i++)
            {
                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                if (!e.Alive || e.Owner != runner.LocalPeer) continue;

                if (node && e.Kind == EntityKind.Worker)
                {
                    runner.Session.Issue(CommandType.Gather, id, FixVec2.Zero, hit);
                }
                else if (e.Speed.Raw != 0)
                {
                    runner.Session.Issue(CommandType.Move, id, target);
                }
            }
        }

        private void Produce()
        {
            World world = runner.World;
            for (int i = 0; i < selection.Selected.Count; i++)
            {
                int id = selection.Selected[i];
                Entity e = world.GetEntity(id);
                if (!e.Alive || e.Kind != EntityKind.Building || e.Owner != runner.LocalPeer) continue;
                // The simulation rejects a queue that is full or unaffordable, so
                // the view does not need to know the rules to stay in step.
                runner.Session.Issue(CommandType.Produce, id, FixVec2.Zero);
            }
        }

        private Vector2 MouseWorld() => cam.ScreenToWorldPoint(Input.mousePosition);
    }
}
