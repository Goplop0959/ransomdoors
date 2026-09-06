using NAudio.Wave;
using System.Diagnostics;

namespace rans0m
{
    /// <summary>
    /// Central audio engine. Fixes sounds cutting off early:
    ///  1. Every live player is rooted in _active so the GC can never finalize
    ///     a WaveOut mid-playback (the old fire-and-forget locals could die
    ///     mid-sound).
    ///  2. Raw resource bytes are snapshotted once (Preload) so playback never
    ///     blocks on decoding, and each play gets a fresh reader.
    ///  3. Failed plays retry once after dropping the broken player, covering
    ///     Windows suspending the idle audio endpoint during long waits.
    /// One-shots dispose themselves on natural completion; tracked (music
    /// layer) players live until StopTracked, with intentional stops never
    /// triggering the auto-dispose path.
    /// </summary>
    public static class AudioEngine
    {
        private sealed class Slot
        {
            public WaveOut Player = null!;
            public WaveStream Reader = null!;
            public MemoryStream Stream = null!;
            public bool Manual;
            public bool Stopping;
        }

        private static readonly object _lock = new();
        private static readonly HashSet<Slot> _active = new();
        private static readonly Dictionary<WaveOut, Slot> _byPlayer = new();
        private static readonly Dictionary<string, byte[]> _bytes = new();

        private static Slot? FindSlot(WaveOut w)
        {
            lock (_lock) { _byPlayer.TryGetValue(w, out var s); return s; }
        }

        public static void Preload(params (string key, Stream stream)[] items)
        {
            try
            {
                lock (_lock)
                {
                    foreach (var (key, stream) in items)
                    {
                        try
                        {
                            if (!_bytes.ContainsKey(key))
                                _bytes[key] = SnapshotBytes(stream);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static byte[] SnapshotBytes(Stream s)
        {
            try { if (s.CanSeek) s.Position = 0; } catch { }
            using var ms = new MemoryStream();
            try { s.CopyTo(ms); } catch { }
            try { if (s.CanSeek) s.Position = 0; } catch { }
            return ms.ToArray();
        }

        private static byte[] BytesFor(Stream resourceStream)
        {
            lock (_lock)
            {
                int key = resourceStream.GetHashCode();
                // Streams from Resources are fresh instances per access, so the
                // hash key rarely hits; keep the latest snapshot regardless.
                if (!_bytes.TryGetValue("s" + key, out var bytes))
                {
                    bytes = SnapshotBytes(resourceStream);
                    if (_bytes.Count < 16) _bytes["s" + key] = bytes;
                }
                return bytes;
            }
        }

        private static WaveStream OpenReader(Stream s)
        {
            try { if (s.CanSeek) s.Position = 0; } catch { }
            try { return new WaveFileReader(s); }
            catch
            {
                try { if (s.CanSeek) s.Position = 0; } catch { }
                return new Mp3FileReader(s);
            }
        }

        private static void Teardown(Slot slot, bool disposePlayer)
        {
            lock (_lock) { _active.Remove(slot); _byPlayer.Remove(slot.Player); }
            try { slot.Reader.Dispose(); } catch { }
            try { if (disposePlayer) slot.Player.Dispose(); } catch { }
            try { slot.Stream.Dispose(); } catch { }
        }

        private static Slot CreateSlot(byte[] bytes, float volume, bool manual)
        {
            var ms = new MemoryStream(bytes, writable: false);
            WaveStream reader = OpenReader(ms);
            var w = new WaveOut();
            var slot = new Slot { Player = w, Reader = reader, Stream = ms, Manual = manual };
            w.PlaybackStopped += (_, __) =>
            {
                // Natural completion -> full teardown. Intentional Stop() on a
                // tracked player -> only unroot; StopTracked disposes.
                if (slot.Stopping && slot.Manual)
                {
                    lock (_lock) { _active.Remove(slot); _byPlayer.Remove(slot.Player); }
                    return;
                }
                Teardown(slot, disposePlayer: true);
            };
            try
            {
                w.Init(reader);
                try { w.Volume = Math.Clamp(volume, 0f, 1f); } catch { }
                lock (_lock) { _active.Add(slot); _byPlayer[w] = slot; }
                return slot;
            }
            catch
            {
                try { w.Dispose(); } catch { }
                try { reader.Dispose(); } catch { }
                try { ms.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>Fire-and-forget one-shot that always plays to completion.</summary>
        public static void PlayOneShot(Stream resourceStream, float volume = 1.0f)
        {
            try
            {
                if (resourceStream == null) return;
                byte[] bytes = BytesFor(resourceStream);
                Slot slot;
                try { slot = CreateSlot(bytes, volume, manual: false); }
                catch
                {
                    // Endpoint may have gone idle during a long wait: retry once
                    // with brand-new objects (mirrors ensure_ready/reopen).
                    try
                    {
                        byte[] fresh;
                        lock (_lock) { fresh = SnapshotBytes(resourceStream); }
                        slot = CreateSlot(fresh, volume, manual: false);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[Audio] play failed twice: {ex.Message}"); return; }
                }
                try { slot.Player.Play(); }
                catch { Teardown(slot, disposePlayer: true); }
            }
            catch { }
        }

        /// <summary>Long-lived player (music layer): prepared paused, rooted until StopTracked.</summary>
        public static WaveOut? PrepareTracked(Stream resourceStream, float volume = 1.0f)
        {
            try
            {
                if (resourceStream == null) return null;
                var slot = CreateSlot(BytesFor(resourceStream), volume, manual: true);
                return slot.Player;
            }
            catch (Exception ex) { Debug.WriteLine($"[Audio] prepare failed: {ex.Message}"); return null; }
        }

        public static void PlayTracked(WaveOut? w)
        {
            if (w == null) return;
            try
            {
                var slot = FindSlot(w);
                if (slot != null) slot.Stopping = false;
                w.Play();
            }
            catch { }
        }

        public static void StopTracked(WaveOut? w)
        {
            if (w == null) return;
            try
            {
                var slot = FindSlot(w);
                if (slot != null) slot.Stopping = true;
                try { w.Stop(); } catch { }
                if (slot != null)
                {
                    lock (_lock) { _active.Remove(slot); _byPlayer.Remove(w); }
                    try { slot.Reader.Dispose(); } catch { }
                    try { w.Dispose(); } catch { }
                    try { slot.Stream.Dispose(); } catch { }
                }
                else { try { w.Dispose(); } catch { } }
            }
            catch { }
        }

        public static int ActiveCount
        {
            get { lock (_lock) return _active.Count; }
        }
    }
}
