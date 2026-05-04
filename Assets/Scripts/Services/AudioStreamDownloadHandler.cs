using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DNDLLM.Services
{
    /// <summary>
    /// Custom UnityWebRequest download handler for OpenRouter's SSE chat-completion responses
    /// that include audio deltas at choices[0].delta.audio.data as base64 WAV chunks.
    /// Accumulates bytes and completes a TaskCompletionSource on stream end.
    /// </summary>
    public class AudioStreamDownloadHandler : DownloadHandlerScript
    {
        private readonly StringBuilder _textBuffer = new StringBuilder();
        private readonly List<byte>    _audioBytes = new List<byte>();
        public readonly  TaskCompletionSource<byte[]> Completion =
            new TaskCompletionSource<byte[]>();

        public AudioStreamDownloadHandler() : base(new byte[4096]) { }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0) return true;
            _textBuffer.Append(Encoding.UTF8.GetString(data, 0, dataLength));
            DrainFrames();
            return true;
        }

        protected override void CompleteContent()
        {
            // Flush any trailing partial frame (SSE is \n\n-terminated; tolerate missing final newline).
            DrainFrames(flush: true);
            Completion.TrySetResult(_audioBytes.ToArray());
        }

        private void DrainFrames(bool flush = false)
        {
            string buf = _textBuffer.ToString();
            int cursor = 0;
            while (true)
            {
                int nn = buf.IndexOf("\n\n", cursor, StringComparison.Ordinal);
                if (nn < 0)
                {
                    if (flush && cursor < buf.Length) { HandleFrame(buf.Substring(cursor)); cursor = buf.Length; }
                    break;
                }
                HandleFrame(buf.Substring(cursor, nn - cursor));
                cursor = nn + 2;
            }
            _textBuffer.Remove(0, cursor);
        }

        private void HandleFrame(string frame)
        {
            // Each SSE frame may contain multiple "data: ..." lines. We only care about data lines.
            foreach (var rawLine in frame.Split('\n'))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string payload = line.Substring(5).TrimStart();
                if (payload == "[DONE]" || payload.Length == 0) continue;

                // Payload is a JSON object; find base64 audio data at choices[0].delta.audio.data.
                // Use a tolerant text search rather than JsonUtility (which trips on missing fields).
                int audioIdx = payload.IndexOf("\"audio\"", StringComparison.Ordinal);
                if (audioIdx < 0) continue;
                int dataIdx  = payload.IndexOf("\"data\"", audioIdx, StringComparison.Ordinal);
                if (dataIdx  < 0) continue;
                int colon    = payload.IndexOf(':', dataIdx);
                if (colon    < 0) continue;
                int quote1   = payload.IndexOf('"', colon);
                if (quote1   < 0) continue;
                int quote2   = payload.IndexOf('"', quote1 + 1);
                if (quote2   < 0) continue;

                string b64 = payload.Substring(quote1 + 1, quote2 - quote1 - 1);
                if (b64.Length == 0) continue;

                try
                {
                    byte[] chunk = Convert.FromBase64String(b64);
                    _audioBytes.AddRange(chunk);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AudioStreamDownloadHandler] base64 decode failed: {e.Message}");
                }
            }
        }
    }
}
