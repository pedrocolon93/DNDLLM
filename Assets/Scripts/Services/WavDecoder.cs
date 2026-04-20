using System;
using UnityEngine;

namespace DNDLLM.Services
{
    /// <summary>Minimal WAV (RIFF) parser that produces a Unity AudioClip from raw bytes.</summary>
    public static class WavDecoder
    {
        public static AudioClip Decode(byte[] wav, string clipName = "tts")
        {
            if (wav == null || wav.Length < 44) return Fail("too short");

            // RIFF header
            if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return Fail("missing RIFF");
            if (wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E') return Fail("missing WAVE");

            // Walk chunks to find "fmt " and "data"
            int pos = 12;
            int sampleRate = 0, channels = 0, bitsPerSample = 0;
            int dataOffset = -1, dataLength = 0;

            while (pos + 8 <= wav.Length)
            {
                string id = "" + (char)wav[pos] + (char)wav[pos+1] + (char)wav[pos+2] + (char)wav[pos+3];
                int size = BitConverter.ToInt32(wav, pos + 4);
                int body = pos + 8;

                if (id == "fmt ")
                {
                    // ushort audioFormat @ body+0
                    channels       = BitConverter.ToUInt16(wav, body + 2);
                    sampleRate     = BitConverter.ToInt32(wav, body + 4);
                    bitsPerSample  = BitConverter.ToUInt16(wav, body + 14);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = size;
                    break;
                }
                pos = body + size + (size & 1); // chunk sizes are word-aligned
            }

            if (dataOffset < 0 || sampleRate == 0 || channels == 0) return Fail("no fmt/data");
            if (bitsPerSample != 16) return Fail($"only 16-bit PCM supported (got {bitsPerSample})");

            int sampleCount = dataLength / 2;           // int16 samples
            int frameCount  = sampleCount / channels;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = (short)(wav[dataOffset + i*2] | (wav[dataOffset + i*2 + 1] << 8));
                samples[i] = s / 32768f;
            }

            var clip = AudioClip.Create(clipName, frameCount, channels, sampleRate, stream: false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip Fail(string why)
        {
            Debug.LogError($"[WavDecoder] Decode failed: {why}");
            return null;
        }
    }
}
