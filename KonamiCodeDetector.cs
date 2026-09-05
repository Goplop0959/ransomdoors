using System.Diagnostics;

namespace rans0m
{
    /// <summary>
    /// Detects the Konami code with Shift as Start:
    /// Up Up Down Down Left Right Left Right B A Shift
    /// </summary>
    public static class KonamiCodeDetector
    {
        private static readonly Keys[] Sequence = new[]
        {
            Keys.Up, Keys.Up,
            Keys.Down, Keys.Down,
            Keys.Left, Keys.Right,
            Keys.Left, Keys.Right,
            Keys.B, Keys.A,
            Keys.ShiftKey // Shift is Start substitute
        };

        private static int _position = 0;
        private static DateTime _lastKeyTime = DateTime.MinValue;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
        private static readonly object LockObj = new();

        public static event Action? CodeEntered;

        private static bool IsShift(Keys k)
        {
            return k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.Shift;
        }

        private static bool KeysEqual(Keys a, Keys b)
        {
            if (a == b) return true;
            // Treat all shift variants as equivalent for the final key
            if (IsShift(a) && IsShift(b)) return true;
            return false;
        }

        public static void OnKeyPressed(Keys key)
        {
            lock (LockObj)
            {
                var now = DateTime.UtcNow;
                if (_position > 0 && (now - _lastKeyTime) > Timeout)
                {
                    _position = 0;
                }
                _lastKeyTime = now;

                Keys expected = Sequence[_position];
                // Normalize incoming shift variants to ShiftKey for comparison if expected is shift
                bool isShiftExpected = IsShift(expected);
                bool isShiftPressed = IsShift(key);

                bool matches;
                if (isShiftExpected && isShiftPressed) matches = true;
                else matches = key == expected;

                if (matches)
                {
                    _position++;
                    if (_position >= Sequence.Length)
                    {
                        _position = 0;
                        Debug.WriteLine("[Konami] Code entered!");
                        try { CodeEntered?.Invoke(); } catch { }
                    }
                }
                else
                {
                    // If this key is the start of the sequence (Up), keep 1 progress instead of 0
                    if (key == Keys.Up)
                        _position = 1; // first Up matched
                    else
                        _position = 0;
                    // Special case: if expected was Up and we got Up, but we already reset, check again?
                    // Already handled by above
                }
            }
        }

        public static void Reset()
        {
            lock (LockObj) _position = 0;
        }
    }
}
