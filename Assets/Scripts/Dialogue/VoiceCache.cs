using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Synthesised lines kept on disk (persistentDataPath/voice-cache), keyed by master and exact text, so a line
    /// the masters have said once is never paid for again. Raw float PCM with a small header; nothing secret is
    /// stored, only audio. A corrupt or partial file is simply ignored and replaced.
    /// </summary>
    public static class VoiceCache
    {
        static string Dir => Path.Combine(Application.persistentDataPath, "voice-cache");

        static string PathFor(string masterId, string text)
        {
            using (var sha = SHA1.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(masterId + "\n" + text));
                return Path.Combine(Dir, BitConverter.ToString(h).Replace("-", "").ToLowerInvariant() + ".pcm");
            }
        }

        public static AudioClip Load(string masterId, string text)
        {
            try
            {
                var path = PathFor(masterId, text);
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length < 12) return null;
                var channels = BitConverter.ToInt32(bytes, 0);
                var frequency = BitConverter.ToInt32(bytes, 4);
                var count = BitConverter.ToInt32(bytes, 8);
                if (channels < 1 || frequency < 1000 || count < 1 || bytes.Length < 12 + count * 4) return null;
                var data = new float[count];
                Buffer.BlockCopy(bytes, 12, data, 0, count * 4);
                var clip = AudioClip.Create("voice-" + masterId, count / channels, channels, frequency, false);
                clip.SetData(data, 0);
                return clip;
            }
            catch (Exception ex) { Debug.LogWarning("[VoiceCache] load failed: " + ex.Message); return null; }
        }

        public static void Save(string masterId, string text, AudioClip clip)
        {
            try
            {
                var count = clip.samples * clip.channels;
                var data = new float[count];
                if (!clip.GetData(data, 0)) return;
                Directory.CreateDirectory(Dir);
                var bytes = new byte[12 + count * 4];
                Buffer.BlockCopy(BitConverter.GetBytes(clip.channels), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(clip.frequency), 0, bytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(count), 0, bytes, 8, 4);
                Buffer.BlockCopy(data, 0, bytes, 12, count * 4);
                File.WriteAllBytes(PathFor(masterId, text), bytes);
            }
            catch (Exception ex) { Debug.LogWarning("[VoiceCache] save failed: " + ex.Message); }
        }
    }
}
