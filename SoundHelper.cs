using NAudio.Wave;

namespace rans0m
{
    public class SoundHelper
    {
        /// <summary>
        /// Creates a WaveOut instance from an audio stream.
        /// Supports both WAV (WaveFileReader) and MP3 (Mp3FileReader) transparently.
        /// Ensures stream is at position 0 and keeps reader alive via WaveOut disposal.
        /// </summary>
        public static WaveOut Create(Stream wavStream)
        {
            if (wavStream == null) throw new ArgumentNullException(nameof(wavStream));
            try { if (wavStream.CanSeek) wavStream.Position = 0; } catch { }

            WaveStream reader;
            // Try WAV first, fallback to MP3 if needed (spawn.mp3 etc)
            try
            {
                reader = new WaveFileReader(wavStream);
            }
            catch
            {
                try { if (wavStream.CanSeek) wavStream.Position = 0; } catch { }
                reader = new Mp3FileReader(wavStream);
            }

            WaveOut waveOut = new WaveOut();
            // Ensure reader is disposed when playback stops/finished
            waveOut.PlaybackStopped += (s, e) =>
            {
                try { reader.Dispose(); } catch { }
                try { waveOut.Dispose(); } catch { }
                try { wavStream.Dispose(); } catch { }
            };
            try
            {
                waveOut.Init(reader);
            }
            catch
            {
                try { reader.Dispose(); } catch { }
                throw;
            }
            return waveOut;
        }

        /// <summary>
        /// Safe Play helper that handles exceptions from NAudio
        /// </summary>
        public static void SafePlay(WaveOut? waveOut)
        {
            if (waveOut == null) return;
            try { waveOut.Play(); } catch { try { waveOut.Dispose(); } catch { } }
        }

        // Cache of decoded resource bytes -> raw stream bytes, so big WAV layers
        // (layer1/2/3) aren't re-decoded from resources on every ransom.
        private static readonly Dictionary<int, byte[]> _bytesCache = new();
        private static readonly object _cacheLock = new();

        private static byte[] SnapshotBytes(Stream s)
        {
            try { if (s.CanSeek) s.Position = 0; } catch { }
            using var ms = new MemoryStream();
            try { s.CopyTo(ms); } catch { }
            try { if (s.CanSeek) s.Position = 0; } catch { }
            return ms.ToArray();
        }

        /// <summary>
        /// Fire-and-forget one-shot sfx. Does NOT dispose early (previous
        /// `using var sfx` cut playback short) - disposal happens on
        /// PlaybackStopped. Volume 0-1.
        /// </summary>
        public static void PlayOneShot(Stream resourceStream, float volume = 1.0f)
        {
            try
            {
                if (resourceStream == null) return;
                int key;
                byte[] bytes;
                lock (_cacheLock)
                {
                    key = resourceStream.GetHashCode();
                    if (!_bytesCache.TryGetValue(key, out bytes!))
                    {
                        bytes = SnapshotBytes(resourceStream);
                        _bytesCache[key] = bytes;
                    }
                }
                var ms = new MemoryStream(bytes, writable: false);
                var w = Create(ms); // Create() disposes ms on PlaybackStopped
                try { w.Volume = Math.Clamp(volume, 0f, 1f); } catch { }
                try { w.Play(); } catch { try { w.Dispose(); } catch { } }
            }
            catch { }
        }
    }
}
