using System.Diagnostics;

namespace rans0m
{
    public static class FileLogger
    {
        private static readonly object _lock = new();
        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "debug.log");

        public static void Log(string msg)
        {
            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
                Debug.WriteLine(line);
                lock (_lock)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Clear()
        {
            try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
        }
    }
}
