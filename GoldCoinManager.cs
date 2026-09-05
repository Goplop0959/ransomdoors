using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace rans0m
{

    public static class GoldCoinManager
    {
        private const string RegistryValueName = "GoldCoins"; // REG_MULTI_SZ

        /// <summary>
        /// Creates a specified number of .gold files in random user folders.
        /// Each file contains an encrypted JSON object: {"RANSOM_COIN": "randomString"}.
        /// Paths are stored in the registry for later deletion.
        /// Fixed: thread-safe rng, validates dirs writable, skips system dirs, limits retries
        /// </summary>
        public static void CreateRandomCoins(int count)
        {
            List<string> baseDirs = GetUserDirectories();
            if (baseDirs.Count == 0) return;

            List<string> createdPaths = new List<string>();

            for (int i = 0; i < count; i++)
            {
                bool created = false;
                // Try up to 3 different base dirs to improve success rate
                for (int attempt = 0; attempt < 3 && !created; attempt++)
                {
                    try
                    {
                        string baseDir = baseDirs[Global.RngNext(baseDirs.Count)];
                        string targetDir = GetRandomSubfolder(baseDir);
                        try { Directory.CreateDirectory(targetDir); }
                        catch { continue; }

                        // Verify we can write there (avoid OneDrive sync issues etc)
                        if (!HasWriteAccess(targetDir)) continue;

                        string randomString = Guid.NewGuid().ToString("N");
                        // Random coin value among 25,30,50,75,100 per user request (500 total)
                        int[] values = new[] { 25, 30, 50, 75, 100 };
                        int coinValue = values[Global.RngNext(values.Length)];

                        Dictionary<string, string> payload = new()
                        {
                            { "RANSOM_COIN", randomString },
                            { "VALUE", coinValue.ToString() }
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
                        created = true;
                    }
                    catch { } // skip coin on error
                }
            }

            if (createdPaths.Count > 0)
                AppendToRegistryList(createdPaths);
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
