using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MusePico.EditorTools
{
    /// <summary>
    /// Copies API keys from the machine's environment into the build, and takes them straight back
    /// out again.
    ///
    /// The requirement this satisfies: <b>the key has to reach the headset, and must never reach
    /// the repository.</b> Those pull in opposite directions — an APK cannot read your environment
    /// variables, so something has to carry the value across, and anything checked in is the wrong
    /// something.
    ///
    /// So the key exists on disk only for the duration of a build:
    ///
    ///   1. Before the build, each variable is read from the environment and written to
    ///      <c>Assets/StreamingAssets/&lt;name&gt;.key</c>.
    ///   2. Unity packages StreamingAssets into the APK.
    ///   3. After the build — success or failure — the files are deleted again.
    ///
    /// Two independent guards, because "the key is only there for 90 seconds" is not a safety
    /// argument if someone commits during those 90 seconds:
    ///
    ///   - <c>.gitignore</c> already excludes <c>Assets/StreamingAssets/*.key</c>.
    ///   - <see cref="OnPostprocessBuild"/> deletes them even when the build throws, and
    ///     <see cref="CleanNow"/> is on the menu for the case where the Editor was killed mid-build.
    ///
    /// <b>This puts a real key inside the APK, where anyone with the file can extract it.</b> That
    /// is an accepted trade for a hackathon build only judges will install. It is not a shipping
    /// arrangement: for that the app talks to a proxy that holds the key, which is also the only
    /// place a spend cap can live. Use a disposable key, cap it, rotate it afterwards.
    /// </summary>
    public class KeyInjectionBuildStep : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        /// <summary>Environment variable → file name under StreamingAssets.</summary>
        public static readonly Dictionary<string, string> Keys = new Dictionary<string, string>
        {
            { "TRIPO_API_KEY", "tripo.key" },       // generation
            { "OPENAI_API_KEY", "openai.key" },     // transcription and the masters' readings
            { "MINIMAX_API_KEY", "minimax.key" },   // the masters' voices
        };

        const string StreamingAssets = "Assets/StreamingAssets";

        /// <summary>Runs before every other build step, so the files exist when Unity packages them.</summary>
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;

            Directory.CreateDirectory(StreamingAssets);

            var injected = new List<string>();
            var missing = new List<string>();

            foreach (var pair in Keys)
            {
                var value = Read(pair.Key);
                if (string.IsNullOrWhiteSpace(value)) { missing.Add(pair.Key); continue; }

                File.WriteAllText(Path.Combine(StreamingAssets, pair.Value), value.Trim());
                // Length only. The whole point of this class is that the value never gets read out.
                injected.Add(pair.Key + " (" + value.Trim().Length + " chars)");
            }

            if (injected.Count > 0)
            {
                AssetDatabase.Refresh();
                Debug.Log("[Keys] Injected into the build: " + string.Join(", ", injected) +
                          ". They will be deleted again when the build finishes.");
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning("[Keys] Not set in this process, so the build will ship without them: " +
                                 string.Join(", ", missing) +
                                 ". Unity reads the environment at launch — if you set one since, restart Unity.");
            }
        }

        public void OnPostprocessBuild(BuildReport report) => Clean("after the build");

        [MenuItem("MusePico/Keys/Remove injected keys now")]
        public static void CleanNow() => Clean("on request");

        [MenuItem("MusePico/Keys/What would be injected?")]
        public static void Report()
        {
            var lines = new List<string>();
            foreach (var pair in Keys)
            {
                // Read(), not the process environment — otherwise this report would disagree with
                // what the build actually does, which is worse than having no report.
                var value = Read(pair.Key);
                lines.Add(string.IsNullOrWhiteSpace(value)
                    ? pair.Key + ": not set"
                    : pair.Key + ": set, " + value.Trim().Length + " chars → StreamingAssets/" + pair.Value);
            }

            var onDisk = Directory.Exists(StreamingAssets) ? Directory.GetFiles(StreamingAssets, "*.key") : Array.Empty<string>();
            lines.Add(onDisk.Length == 0
                ? "No key files on disk right now (correct between builds)."
                : "LEFT ON DISK: " + string.Join(", ", onDisk) + " — run Remove injected keys now.");

            // Console only, deliberately. A modal dialog in a menu item blocks Unity's main
            // thread, which also blocks the MCP bridge — so a menu item meant for diagnosis
            // becomes the thing that needs diagnosing. It cost a hang the first time.
            Debug.Log("[Keys]\n" + string.Join("\n", lines));
        }

        /// <summary>
        /// Reads a variable from the process first, then from the User and Machine scopes.
        ///
        /// The fallback is the whole point. A process inherits its environment block at launch, so
        /// a key set AFTER the Editor started is invisible to it — and the documented workaround
        /// was "restart Unity", which is a real interruption for something this mechanical. It
        /// also bit for real: the first MuseumSalon build shipped without MINIMAX_API_KEY for
        /// exactly this reason, and the only symptom was three masters reading in silence.
        ///
        /// The User and Machine scopes read the registry live, so a key set a minute ago is picked
        /// up by the next build. Windows-only, hence the guard: on other platforms those targets
        /// are not meaningful and the process environment is the whole story.
        /// </summary>
        static string Read(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;

            if (Application.platform != RuntimePlatform.WindowsEditor) return null;

            try
            {
                value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(value)) return value;

                return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            }
            catch (Exception)
            {
                // Reading another scope is a convenience, never a requirement.
                return null;
            }
        }

        static void Clean(string when)
        {
            if (!Directory.Exists(StreamingAssets)) return;

            var removed = new List<string>();
            foreach (var pair in Keys)
            {
                var path = Path.Combine(StreamingAssets, pair.Value);
                if (File.Exists(path)) { File.Delete(path); removed.Add(pair.Value); }

                var meta = path + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }

            if (removed.Count == 0) return;
            AssetDatabase.Refresh();
            Debug.Log("[Keys] Removed " + string.Join(", ", removed) + " " + when + ".");
        }
    }
}
