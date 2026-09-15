using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Tripo;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MusePico.Generation
{
    /// <summary>The finished article, plus everything measured on the way.</summary>
    public sealed class GenerationOutcome
    {
        public bool Success;
        public GameObject Root;
        public GeneratedAssetReport Report;
        public string Prompt;
        public string TaskId;
        public string Error;
    }

    /// <summary>
    /// Prompt in, GameObject in the scene out — the whole runtime round trip.
    ///
    /// Submit, poll, download, import, fit, place. It reports progress the whole way because the
    /// round trip is 45-90 seconds with P1 textured, and in a headset that is a long time to look
    /// at nothing.
    ///
    /// <b>One generation at a time, per instance.</b> Not a limitation worth removing: Tripo
    /// meters concurrency per account in per-category pools (5 for the P-series), a second
    /// request from a wearer who pressed the button twice bills twice, and two 60-second
    /// generations racing to place a model at the same anchor is a bug nobody would find funny
    /// mid-demo. <see cref="IsBusy"/> is what the UI disables its button on.
    /// </summary>
    public class RuntimeGenerator : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Where generated models are parented. Defaults to this transform.")]
        public Transform spawnAnchor;

        [Tooltip("Metres the tallest axis should occupy once placed.")]
        public float targetHeight = 1.4f;

        [Tooltip("Height of the surface the model stands on, relative to the anchor.")]
        public float standOnY;

        [Header("Budget")]
        public VrBudgetTier tier = VrBudgetTier.Exhibit;

        [Tooltip("Override the tier's triangle ceiling. 0 uses the tier default. Clamped to P1's 20,000.")]
        public int faceLimitOverride;

        [Tooltip("Textured generation is ~45-90s and costs more. Untextured is ~10-20s.")]
        public bool withTexture = true;

        [Header("Service")]
        [Tooltip("Use the mainland-China endpoint. Accounts are region-bound.")]
        public bool useChinaEndpoint;

        [Tooltip("Give up on one generation after this long. Tripo's own polling guidance caps at 5 minutes.")]
        public float timeoutSeconds = 300f;

        /// <summary>Fires on the main thread every time the phase or fraction moves.</summary>
        public event Action<GenerationProgress> ProgressChanged;

        public GenerationProgress Progress { get; private set; }
        public bool IsBusy { get; private set; }

        ITripoKeySource _keySource;
        CancellationTokenSource _cancel;

        /// <summary>Swap in a proxy-backed source here when this stops being a desk demo.</summary>
        public ITripoKeySource KeySource
        {
            get => _keySource ??= FallbackKeySource.Default();
            set => _keySource = value;
        }

        public string BaseUrl => useChinaEndpoint ? TripoApi.ChinaBaseUrl : TripoApi.GlobalBaseUrl;

        public VrAssetBudget Budget
        {
            get
            {
                var budget = VrAssetBudget.For(tier).WithTexture(withTexture);
                return faceLimitOverride > 0 ? budget.WithFaceLimit(faceLimitOverride) : budget;
            }
        }

        /// <summary>Floor price of the next generation, in credits. 1 credit = $0.01.</summary>
        public int EstimatedCredits => Budget.EstimatedCredits(fromImage: false);

        public void Cancel() => _cancel?.Cancel();

        /// <summary>
        /// Generates from raw user input — typed or dictated — and places the result.
        ///
        /// Never throws. A failure comes back on the outcome, because the caller is a UI panel in
        /// a headset and an unhandled exception there is a black screen with no explanation.
        /// </summary>
        public async Task<GenerationOutcome> GenerateAsync(SubjectKind kind, string userInput)
        {
            if (IsBusy)
                return Failed(null, "A generation is already running.");

            string prompt;
            try
            {
                prompt = GenerationSubject.BuildPrompt(kind, userInput);
            }
            catch (ArgumentException ex)
            {
                return Failed(null, ex.Message);
            }

            var key = await KeySource.GetKeyAsync();
            if (string.IsNullOrEmpty(key))
                return Failed(prompt, "No Tripo API key. " + KeySource.Describe());

            IsBusy = true;
            _cancel = new CancellationTokenSource();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var transport = new TripoWebRequestTransport(key, BaseUrl);
                var client = new TripoClient(transport, BaseUrl);

                var request = new TextToModelRequest { Prompt = prompt };
                Budget.ApplyTo(request);

                Report(GenerationPhase.Submitting, 0.5f, stopwatch);
                var taskId = await client.CreateTaskAsync(request.Path, request.ToJson(), _cancel.Token);

                var task = await PollAsync(client, taskId, stopwatch, _cancel.Token);
                if (task == null)
                    return Failed(prompt, "Timed out after " + (int)timeoutSeconds + "s. Task " + taskId + " may still finish.");

                if (!task.IsSuccess)
                    return Failed(prompt, task.Describe(), taskId);

                var url = task.output?.BestModelUrl;
                if (string.IsNullOrEmpty(url))
                    return Failed(prompt, "Generated, but no model URL came back.", taskId);

                Report(GenerationPhase.Downloading, 0f, stopwatch);
                var downloadTask = client.DownloadAsync(url, _cancel.Token);
                while (!downloadTask.IsCompleted)
                {
                    Report(GenerationPhase.Downloading, transport.Progress, stopwatch);
                    await Task.Yield();
                }
                var bytes = await downloadTask;

                Report(GenerationPhase.Importing, 0.3f, stopwatch);
                var parent = spawnAnchor != null ? spawnAnchor : transform;
                var loaded = await RuntimeGltfLoader.LoadAsync(bytes, parent, Safe(userInput), _cancel.Token);

                if (!loaded.Success)
                    return Failed(prompt, loaded.Error, taskId);

                RuntimeGltfLoader.FitInPlace(loaded.Root, targetHeight, standOnY);

                stopwatch.Stop();
                var report = loaded.Report;
                report.GenerationSeconds = (float)stopwatch.Elapsed.TotalSeconds;
                report.CreditsSpent = task.credits_consumed;

                Progress = new GenerationProgress
                {
                    Phase = GenerationPhase.Done,
                    Fraction = 1f,
                    ElapsedSeconds = report.GenerationSeconds,
                    Message = report.Summarise(),
                };
                ProgressChanged?.Invoke(Progress);

                if (report.Verdict == AssetVerdict.OverBudget)
                {
                    Debug.LogWarning("[Tripo] Generated asset is over the headset budget: " + report.Summarise());
                }

                return new GenerationOutcome
                {
                    Success = true,
                    Root = loaded.Root,
                    Report = report,
                    Prompt = prompt,
                    TaskId = taskId,
                };
            }
            catch (OperationCanceledException)
            {
                Progress = new GenerationProgress { Phase = GenerationPhase.Cancelled, ElapsedSeconds = (float)stopwatch.Elapsed.TotalSeconds };
                ProgressChanged?.Invoke(Progress);
                return new GenerationOutcome { Success = false, Prompt = prompt, Error = "Cancelled." };
            }
            catch (TripoException ex)
            {
                return Failed(prompt, ex.ToString());
            }
            catch (Exception ex)
            {
                return Failed(prompt, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                IsBusy = false;
                _cancel?.Dispose();
                _cancel = null;
            }
        }

        /// <summary>
        /// Polls to a terminal state, mapping Tripo's status onto phases as it goes.
        ///
        /// Deliberately not <see cref="TripoClient.WaitForTaskAsync"/>: that returns only when the
        /// task is finished, and the whole point here is what happens in between. Two seconds is
        /// Tripo's own guidance and their rate limiter starts refusing above one request a second.
        /// </summary>
        async Task<TripoTask> PollAsync(TripoClient client, string taskId, Stopwatch stopwatch, CancellationToken ct)
        {
            var deadline = TimeSpan.FromSeconds(timeoutSeconds);
            var creep = 0f;

            while (stopwatch.Elapsed < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var task = await client.GetTaskAsync(taskId, ct);

                switch (task.Status)
                {
                    case TripoTaskStatus.Queued:
                        creep = Mathf.Min(0.95f, creep + 0.08f);
                        Progress = new GenerationProgress
                        {
                            Phase = GenerationPhase.Queued,
                            Fraction = GenerationProgress.FractionFor(GenerationPhase.Queued, creep),
                            ElapsedSeconds = (float)stopwatch.Elapsed.TotalSeconds,
                            QueueAhead = (int)task.queuing_num,
                        };
                        ProgressChanged?.Invoke(Progress);
                        break;

                    case TripoTaskStatus.Running:
                        Report(GenerationPhase.Generating, task.progress / 100f, stopwatch);
                        break;

                    default:
                        if (task.IsTerminal) return task;
                        break;
                }

                await Task.Delay(2000, ct);
            }

            return null;
        }

        void Report(GenerationPhase phase, float within, Stopwatch stopwatch)
        {
            Progress = new GenerationProgress
            {
                Phase = phase,
                Fraction = GenerationProgress.FractionFor(phase, within),
                ElapsedSeconds = (float)stopwatch.Elapsed.TotalSeconds,
            };
            ProgressChanged?.Invoke(Progress);
        }

        GenerationOutcome Failed(string prompt, string error, string taskId = null)
        {
            Progress = new GenerationProgress { Phase = GenerationPhase.Failed, Message = error };
            ProgressChanged?.Invoke(Progress);
            return new GenerationOutcome { Success = false, Prompt = prompt, Error = error, TaskId = taskId };
        }

        static string Safe(string userInput)
        {
            var name = GenerationSubject.Normalise(userInput);
            return string.IsNullOrEmpty(name) ? "Generated" : name;
        }

        void OnDestroy() => _cancel?.Cancel();
    }
}
