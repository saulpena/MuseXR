using System;

namespace MusePico.Tripo
{
    /// <summary>
    /// Task lifecycle, as reported by <c>GET /v3/tasks/{task_id}</c>.
    /// Every generation endpoint is asynchronous — it returns a task id, not a model.
    /// </summary>
    public enum TripoTaskStatus
    {
        Unknown = 0,
        Queued,
        Running,
        Success,
        Failed,
        Cancelled,
        Banned,
        Expired,
    }

    public static class TripoTaskStatusExtensions
    {
        /// <summary>True once the task will not change again — the signal to stop polling.</summary>
        public static bool IsTerminal(this TripoTaskStatus status)
        {
            switch (status)
            {
                case TripoTaskStatus.Success:
                case TripoTaskStatus.Failed:
                case TripoTaskStatus.Cancelled:
                case TripoTaskStatus.Banned:
                case TripoTaskStatus.Expired:
                    return true;
                default:
                    return false;
            }
        }

        public static TripoTaskStatus Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return TripoTaskStatus.Unknown;
            switch (value.Trim().ToLowerInvariant())
            {
                case "queued": return TripoTaskStatus.Queued;
                case "running": return TripoTaskStatus.Running;
                case "success": return TripoTaskStatus.Success;
                case "failed": return TripoTaskStatus.Failed;
                case "cancelled":
                case "canceled": return TripoTaskStatus.Cancelled;
                case "banned": return TripoTaskStatus.Banned;
                case "expired": return TripoTaskStatus.Expired;
                default: return TripoTaskStatus.Unknown;
            }
        }
    }

    /// <summary>
    /// The <c>output</c> object of a finished task. Which fields are populated depends on the task
    /// type, so everything is optional and <see cref="BestModelUrl"/> does the picking.
    ///
    /// v3 renamed these: v2 returned <c>pbr_model</c> / <c>model</c> / <c>base_model</c>, v3
    /// returns <c>model_url</c>. Both are modelled because a v2-era account can still see the old
    /// names on older tasks.
    /// </summary>
    [Serializable]
    public class TripoTaskOutput
    {
        public string model_url;
        public string rendered_image_url;
        public string model;
        public string pbr_model;
        public string base_model;
        public string rendered_image;
        public string rig_type;
        public bool riggable;

        /// <summary>
        /// The model file worth downloading, in preference order. PBR first because that is the
        /// textured one; the bare <c>base_model</c> is the untextured fallback.
        /// </summary>
        public string BestModelUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(model_url)) return model_url;
                if (!string.IsNullOrEmpty(pbr_model)) return pbr_model;
                if (!string.IsNullOrEmpty(model)) return model;
                return string.IsNullOrEmpty(base_model) ? null : base_model;
            }
        }

        public string PreviewImageUrl =>
            !string.IsNullOrEmpty(rendered_image_url) ? rendered_image_url :
            string.IsNullOrEmpty(rendered_image) ? null : rendered_image;
    }

    /// <summary>One task snapshot. Field names match the wire format so JsonUtility can fill it.</summary>
    [Serializable]
    public class TripoTask
    {
        public string task_id;
        public string type;
        public string status;
        public int progress;
        public TripoTaskOutput output;
        public long create_time;
        public long running_left_time;
        public long queuing_num;
        public int error_code;
        public string error_msg;
        public float credits_consumed;

        public TripoTaskStatus Status => TripoTaskStatusExtensions.Parse(status);
        public bool IsTerminal => Status.IsTerminal();
        public bool IsSuccess => Status == TripoTaskStatus.Success;

        /// <summary>
        /// A line fit for a progress bar. Deliberately does not include the task id — these get
        /// logged, and a task id is a reference to billed work, not a secret but not noise either.
        /// </summary>
        public string Describe()
        {
            var s = Status.ToString().ToLowerInvariant();
            if (Status == TripoTaskStatus.Failed)
                return "failed (" + error_code + "): " + (string.IsNullOrEmpty(error_msg) ? "no message" : error_msg);
            if (Status == TripoTaskStatus.Queued && queuing_num > 0) return "queued, " + queuing_num + " ahead";
            if (Status == TripoTaskStatus.Running) return "running " + progress + "%";
            return s;
        }
    }

    /// <summary>Thrown for every non-success outcome, so callers have one thing to catch.</summary>
    public class TripoException : Exception
    {
        /// <summary>Tripo's own error code from the response envelope. 0 when the failure was transport-level.</summary>
        public int Code { get; }
        /// <summary>HTTP status, or 0 when the request never reached the server.</summary>
        public int HttpStatus { get; }
        /// <summary>Tripo's remediation hint, when the envelope carried one.</summary>
        public string Suggestion { get; }

        public TripoException(string message, int code = 0, int httpStatus = 0, string suggestion = null)
            : base(message)
        {
            Code = code;
            HttpStatus = httpStatus;
            Suggestion = suggestion;
        }

        public override string ToString()
        {
            var detail = Message;
            if (HttpStatus != 0) detail += " (HTTP " + HttpStatus + ")";
            if (Code != 0) detail += " (code " + Code + ")";
            if (!string.IsNullOrEmpty(Suggestion)) detail += " — " + Suggestion;
            return detail;
        }
    }
}
