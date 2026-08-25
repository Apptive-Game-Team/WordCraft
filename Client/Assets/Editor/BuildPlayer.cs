using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// Builds the player from the command line, so that seeing a change run does
    /// not require opening the editor or pushing a tag.
    ///
    ///   Unity -batchmode -nographics -quit -projectPath Client \
    ///         -executeMethod WordCraft.View.BuildPlayer.Run \
    ///         -buildOutput ../play -buildTarget Win64
    ///
    /// The release workflow does not call this: game-ci's unity-builder brings its
    /// own build step, and two entry points that must agree is one more thing to
    /// keep in step. What this shares with the workflow is the name — the binary
    /// is WordCraft either way, so a locally built player and a released one are
    /// the same thing to anyone looking at a folder.
    ///
    /// Refuses to build against a stale simulation, which is the one way this can
    /// go wrong quietly. Sim and Net reach the player as compiled assemblies that
    /// dotnet build vendors into Assets/Plugins, and Unity has no idea they came
    /// from source: forget the dotnet build and the player is a correct build of
    /// last week's rules, with nothing anywhere saying so. See <see cref="Fresh"/>.
    /// </summary>
    // ponytail: development builds only. A release build wants IL2CPP, a stripping
    // level and a signing story, and every one of those is a decision the release
    // workflow already owns. This is the "does it run" build.
    public static class BuildPlayer
    {
        private const string Name = "WordCraft";
        private const string Scene = "Assets/Scenes/Match.unity";

        public static void Run()
        {
            string output = Arg("-buildOutput");
            if (string.IsNullOrEmpty(output))
            {
                Fail("no -buildOutput given");
                return;
            }

            if (!Fresh()) return;

            BuildTarget target = Target(Arg("-buildTarget"));
            // Directory.CreateDirectory rather than a check: an output folder left
            // over from a previous build is the normal case, not an error.
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Path.Combine(output, Name + Extension(target)),
                target = target,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Fail(summary.result + " after " + summary.totalErrors + " error(s)");
                return;
            }

            Debug.Log("OK: built " + options.locationPathName + " (" + summary.totalSize + " bytes)");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// That the vendored assemblies are not older than the source they were
        /// built from. The check this file exists to carry, because it is the one
        /// failure that produces a player that runs perfectly and plays by rules
        /// nobody chose.
        ///
        /// A timestamp rather than a hash: dotnet build already decides what to
        /// recompile by timestamp, so this asks exactly the question the build
        /// system itself asks and cannot answer differently. Off by a second on a
        /// filesystem that rounds is a rebuild, not a wrong answer.
        ///
        /// Only Sim and Net are checked. Everything else in the player is compiled
        /// by Unity from source on this very run and cannot be stale.
        /// </summary>
        private static bool Fresh()
        {
            // Assets/Editor -> Assets -> Client -> the repository root.
            string root = Directory.GetParent(Application.dataPath).Parent.FullName;

            foreach (string assembly in new[] { "WordCraft.Sim", "WordCraft.Net" })
            {
                string dll = Path.Combine(Application.dataPath, "Plugins", assembly + ".dll");
                if (!File.Exists(dll))
                {
                    Fail("no " + assembly + ".dll in Assets/Plugins — run dotnet build first");
                    return false;
                }

                string source = Path.Combine(root, assembly.Substring("WordCraft.".Length));
                if (!Directory.Exists(source)) continue;

                DateTime built = File.GetLastWriteTimeUtc(dll);
                foreach (string cs in Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories))
                {
                    if (File.GetLastWriteTimeUtc(cs) <= built) continue;
                    Fail(assembly + ".dll is older than " + Path.GetFileName(cs) +
                         " — run dotnet build first, or the player ships the previous rules");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Unity's own -buildTarget, read back rather than trusted to have taken
        /// effect: switching platforms in batchmode is asynchronous enough that
        /// naming the target on the options is the only thing that decides it.
        /// </summary>
        private static BuildTarget Target(string name)
        {
            switch (name)
            {
                case "Win64": case "StandaloneWindows64": return BuildTarget.StandaloneWindows64;
                case "OSXUniversal": case "StandaloneOSX": return BuildTarget.StandaloneOSX;
                case "Linux64": case "StandaloneLinux64": return BuildTarget.StandaloneLinux64;
                // Whatever the editor is already set to, which on a developer's
                // machine is the platform they are on.
                default: return EditorUserBuildSettings.activeBuildTarget;
            }
        }

        /// <summary>
        /// What the platform expects on the end. macOS wants the .app on the path
        /// itself; Linux takes a bare name.
        /// </summary>
        private static string Extension(BuildTarget target) =>
            target == BuildTarget.StandaloneWindows64 ? ".exe"
            : target == BuildTarget.StandaloneOSX ? ".app"
            : string.Empty;

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag) return args[i + 1];
            }
            return null;
        }

        private static void Fail(string why)
        {
            Debug.LogError("FAIL: " + why);
            EditorApplication.Exit(1);
        }
    }
}
