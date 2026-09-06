using System.Diagnostics;
using System.Reflection;

namespace rans0m
{
    /// <summary>
    /// The release ships as ONE standalone exe. All runtime sidecar files
    /// (coin faces, honey pot, generated gif/ico/bmp, restore.json) live in a
    /// created folder in %TEMP% instead of beside the exe. Embedded PNGs are
    /// unpacked there on startup when missing or stale.
    /// </summary>
    public static class AssetManager
    {
        public static string Dir { get; } =
            Path.Combine(Path.GetTempPath(), "Ransom_A-90");

        private static readonly string[] BundledFiles = new[]
        {
            "Gold_5.png", "Gold_10.png", "Gold_25.png",
            "Gold_50.png", "Gold_75.png", "Gold_100.png",
            "Honey_Pot.png", "Random_A-90.gif"
        };

        private static string? FindManifestName(string fileName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var full in asm.GetManifestResourceNames())
                {
                    if (full.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase)
                        || full.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                        return full;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Create the temp folder and unpack any missing/stale assets.</summary>
        public static void EnsureAssets()
        {
            try { Directory.CreateDirectory(Dir); } catch { return; }
            Assembly asm;
            try { asm = Assembly.GetExecutingAssembly(); } catch { return; }
            foreach (var file in BundledFiles)
            {
                try
                {
                    string dest = Path.Combine(Dir, file);
                    string? manifest = FindManifestName(file);
                    if (manifest == null) continue;
                    bool rewrite = true;
                    if (File.Exists(dest))
                    {
                        try
                        {
                            using var existing = File.OpenRead(dest);
                            using var embedded = asm.GetManifestResourceStream(manifest);
                            rewrite = embedded == null || existing.Length != embedded.Length;
                        }
                        catch { rewrite = true; }
                    }
                    if (!rewrite) continue;
                    using var src = asm.GetManifestResourceStream(manifest);
                    if (src == null) continue;
                    string tmp = dest + ".tmp";
                    using (var dst = File.Create(tmp))
                        src.CopyTo(dst);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                }
                catch (Exception ex) { Debug.WriteLine($"[Assets] unpack {file} failed: {ex.Message}"); }
            }
        }

        public static string AssetPath(string fileName) => Path.Combine(Dir, fileName);
    }
}
