using MusePico.Dialogue;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A master's dialogue card closed: their voice stops with it (Saul, 5 Oct). Every master voice goes through
    /// <see cref="VoiceGate"/>, so one stop silences whichever is sounding, whatever played it.
    /// </summary>
    public static class VoiceOnClose
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Listen()
        {
            DialogueContext.LineClosed -= Stop;
            DialogueContext.LineClosed += Stop;
        }

        static void Stop(string masterId)
        {
            if (!VoiceGate.Speaking) return;
            VoiceGate.StopAll();
            Debug.Log("[Voice] " + masterId + "'s card closed: their voice stopped");
        }
    }
}
