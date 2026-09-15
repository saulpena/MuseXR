namespace MusePico.Generation
{
    public enum GenerationPhase
    {
        Idle,
        Submitting,
        Queued,
        Generating,
        Downloading,
        Importing,
        Done,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// One number for a progress bar, out of four stages that report progress very differently.
    ///
    /// This matters more in a headset than it sounds. A generation is 45-90 seconds of nothing
    /// visible, and a bar that sits at zero and then jumps to done reads as a hang — someone
    /// wearing a headset cannot glance at a log to reassure themselves. Tripo reports 0-100 for
    /// the generate step only; the download reports bytes; queueing and import report nothing at
    /// all. Mapping them onto one monotonic 0..1 is arithmetic, so it is pinned by tests rather
    /// than tuned by feel.
    ///
    /// The weights reflect measured reality, not equal thirds: generation dominates at roughly
    /// 45-90 s against a download of a few seconds and an import under a second.
    /// </summary>
    public struct GenerationProgress
    {
        public GenerationPhase Phase;
        /// <summary>0..1 across the whole operation, never decreasing.</summary>
        public float Fraction;
        public string Message;
        public float ElapsedSeconds;
        /// <summary>Tasks ahead of this one in the queue, when Tripo reports it.</summary>
        public int QueueAhead;

        const float SubmitShare = 0.04f;
        const float QueueShare = 0.06f;
        const float GenerateShare = 0.76f;
        const float DownloadShare = 0.11f;
        const float ImportShare = 0.03f;

        /// <summary>
        /// Overall fraction for a phase. <paramref name="within"/> is that phase's own 0..1 where
        /// it has one, and is ignored where it does not.
        /// </summary>
        public static float FractionFor(GenerationPhase phase, float within)
        {
            if (within < 0f) within = 0f;
            if (within > 1f) within = 1f;

            switch (phase)
            {
                case GenerationPhase.Idle:
                    return 0f;
                case GenerationPhase.Submitting:
                    return SubmitShare * within;
                case GenerationPhase.Queued:
                    // Queue depth is not a fraction of anything knowable, so this creeps rather
                    // than claiming progress it cannot measure.
                    return SubmitShare + QueueShare * within;
                case GenerationPhase.Generating:
                    return SubmitShare + QueueShare + GenerateShare * within;
                case GenerationPhase.Downloading:
                    return SubmitShare + QueueShare + GenerateShare + DownloadShare * within;
                case GenerationPhase.Importing:
                    return SubmitShare + QueueShare + GenerateShare + DownloadShare + ImportShare * within;
                case GenerationPhase.Done:
                    return 1f;
                default:
                    return 0f;
            }
        }

        public bool IsTerminal =>
            Phase == GenerationPhase.Done || Phase == GenerationPhase.Failed || Phase == GenerationPhase.Cancelled;

        public bool IsRunning => Phase != GenerationPhase.Idle && !IsTerminal;

        /// <summary>Wording for a panel someone is reading through a lens, so: short.</summary>
        public string Headline()
        {
            switch (Phase)
            {
                case GenerationPhase.Idle: return "Ready";
                case GenerationPhase.Submitting: return "Sending…";
                case GenerationPhase.Queued: return QueueAhead > 0 ? "Queued, " + QueueAhead + " ahead" : "Queued…";
                case GenerationPhase.Generating: return "Sculpting… " + Round(ElapsedSeconds) + "s";
                case GenerationPhase.Downloading: return "Downloading…";
                case GenerationPhase.Importing: return "Building mesh…";
                case GenerationPhase.Done: return "Done in " + Round(ElapsedSeconds) + "s";
                case GenerationPhase.Failed: return string.IsNullOrEmpty(Message) ? "Failed" : "Failed: " + Message;
                case GenerationPhase.Cancelled: return "Cancelled";
                default: return Phase.ToString();
            }
        }

        static int Round(float seconds) => (int)(seconds + 0.5f);
    }
}
