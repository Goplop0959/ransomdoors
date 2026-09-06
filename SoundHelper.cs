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

        /// <summary>
        /// Fire-and-forget one-shot sfx, rooted in AudioEngine so the GC can
        /// never cut playback short. Volume 0-1.
        /// </summary>
        public static void PlayOneShot(Stream resourceStream, float volume = 1.0f)
        {
            try { AudioEngine.PlayOneShot(resourceStream, volume); } catch { }
        }
    }
}
