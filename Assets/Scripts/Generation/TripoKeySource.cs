using System;
using System.IO;
using System.Threading.Tasks;
using MusePico.Tripo;
using UnityEngine;
using UnityEngine.Networking;

namespace MusePico.Generation
{
    /// <summary>
    /// Where the API key comes from once the code is running on a headset rather than in the
    /// Editor.
    ///
    /// <b>This is the one genuinely unresolved question in runtime generation, and it is a
    /// security question, not a plumbing one.</b> In the Editor the key is an environment
    /// variable and never leaves the machine. On device there is no environment to read, so a
    /// client that calls Tripo directly has to carry the key inside the APK — where anyone with
    /// the file can pull it out, and the account it belongs to is a spending account.
    ///
    /// The three options, honestly:
    ///
    ///   <b>Editor</b> — environment variable. Correct, costs nothing, works only in Play Mode.
    ///
    ///   <b>StreamingAssets</b> — a key file placed beside the build. Convenient for testing on a
    ///   headset on a desk. The file is gitignored, but it IS inside the APK: treat any key used
    ///   this way as disposable, cap its spend, and rotate it after the demo. This is not a
    ///   shipping configuration.
    ///
    ///   <b>Proxy</b> — the app calls a server we control, the server holds the key. The only
    ///   correct answer for anything a stranger can install, and the shape muse-infinity already
    ///   uses: its Tripo routes sit behind <c>INTEGRATION_ADMIN_TOKEN</c> precisely so the browser
    ///   never sees a provider key. A proxy also gives the two things a client cannot do for
    ///   itself — a spend cap and request moderation.
    ///
    /// The seam is here so switching to the proxy later changes one line, not the pipeline.
    /// </summary>
    public interface ITripoKeySource
    {
        Task<string> GetKeyAsync();
        string Describe();
    }

    /// <summary>Reads an environment variable. Works in the Editor; on Android there is no environment.</summary>
    public sealed class EnvironmentKeySource : ITripoKeySource
    {
        readonly string _variable;

        public EnvironmentKeySource(string variable = TripoApi.ApiKeyEnvironmentVariable) =>
            _variable = variable;

        public Task<string> GetKeyAsync() => Task.FromResult(Read(_variable));

        public string Describe()
        {
            var key = Read(_variable);
            return string.IsNullOrWhiteSpace(key)
                ? _variable + " is not set in this process, nor in the User or Machine environment."
                : _variable + " is set (" + key.Trim().Length + " characters).";
        }

        /// <summary>
        /// Process environment first, then the User and Machine scopes in the Editor on Windows.
        ///
        /// <b>The fallback is the whole point.</b> A process inherits its environment block at
        /// launch, so a variable set AFTER the Editor started is invisible to it — and the failure
        /// is silent: <c>MuseumDialogue</c> just builds no voice service and the masters read in
        /// text while everything reports healthy. Measured 14 Sep 2026: MINIMAX_API_KEY was set at
        /// User scope and 126 characters long, and this returned null because Unity had been
        /// launched earlier, so a Play Mode salon answered correctly and in total silence.
        ///
        /// <c>KeyInjectionBuildStep.Read</c> has had this fallback all along, which is why a BUILD
        /// picked the key up while the Editor did not — the same question answered two different
        /// ways in two places. Now they agree.
        ///
        /// The registry scopes exist only on Windows, and reading them is a convenience rather
        /// than a requirement, so any failure falls through to the process value.
        /// </summary>
        static string Read(string variable)
        {
            var key = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(key)) return key.Trim();

#if UNITY_EDITOR_WIN
            try
            {
                key = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(key)) return key.Trim();

                key = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
            }
            catch (Exception)
            {
                // Never let a registry read break key resolution.
            }
#endif
            return null;
        }
    }

    /// <summary>
    /// Reads a key from <c>StreamingAssets/tripo.key</c>.
    ///
    /// On Android StreamingAssets lives inside the APK and is not a filesystem path, so it has to
    /// be fetched over <c>UnityWebRequest</c> rather than read with <c>File</c> — a detail that
    /// silently returns nothing if you get it wrong, on device only, where it is hardest to
    /// debug.
    /// </summary>
    public sealed class StreamingAssetsKeySource : ITripoKeySource
    {
        public const string DefaultFileName = "tripo.key";

        readonly string _fileName;
        string _cached;

        public StreamingAssetsKeySource(string fileName = DefaultFileName) => _fileName = fileName;

        public async Task<string> GetKeyAsync()
        {
            if (!string.IsNullOrEmpty(_cached)) return _cached;

            var path = Path.Combine(Application.streamingAssetsPath, _fileName);

            if (path.Contains("://"))
            {
                using (var www = UnityWebRequest.Get(path))
                {
                    var operation = www.SendWebRequest();
                    var completion = new TaskCompletionSource<bool>();
                    operation.completed += _ => completion.TrySetResult(true);
                    await completion.Task;

                    if (www.result != UnityWebRequest.Result.Success) return null;
                    _cached = Clean(www.downloadHandler.text);
                }
            }
            else
            {
                if (!File.Exists(path)) return null;
                _cached = Clean(File.ReadAllText(path));
            }

            return _cached;
        }

        public string Describe() =>
            "StreamingAssets/" + _fileName + " (inside the APK — development only, use a capped key and rotate it).";

        static string Clean(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().Split('\n')[0].Trim();
    }

    /// <summary>Tries each source in order and takes the first that produces a key.</summary>
    public sealed class FallbackKeySource : ITripoKeySource
    {
        readonly ITripoKeySource[] _sources;

        public FallbackKeySource(params ITripoKeySource[] sources) => _sources = sources;

        public async Task<string> GetKeyAsync()
        {
            foreach (var source in _sources)
            {
                if (source == null) continue;
                var key = await source.GetKeyAsync();
                if (!string.IsNullOrEmpty(key)) return key;
            }
            return null;
        }

        public string Describe()
        {
            var parts = new System.Text.StringBuilder();
            foreach (var source in _sources)
            {
                if (source == null) continue;
                if (parts.Length > 0) parts.Append(" | ");
                parts.Append(source.Describe());
            }
            return parts.ToString();
        }

        /// <summary>Environment first (Editor), then the key file (device).</summary>
        public static FallbackKeySource Default() =>
            new FallbackKeySource(new EnvironmentKeySource(), new StreamingAssetsKeySource());

        /// <summary>The same two sources, for the speech provider's key.</summary>
        public static FallbackKeySource ForOpenAi() =>
            new FallbackKeySource(
                new EnvironmentKeySource("OPENAI_API_KEY"),
                new StreamingAssetsKeySource("openai.key"));
    }
}
