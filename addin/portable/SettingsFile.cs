using System;
using System.IO;
using System.Linq;
namespace SWToolsHub.Portable
{
    internal static class SettingsFile
    {
        public static string PortablePath(string appRoot, string userId)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create()) {
                string key = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(userId))).Replace("-", "").Substring(0, 16);
                return Path.Combine(appRoot, "portable." + key + ".json");
            }
        }
        public static void MigrateUserSettings(string localData, string target)
        {
            foreach (string name in new[] { "SW-Assistant", "SWToolsHub" }) {
                string oldFolder = Path.Combine(localData, name);
                if (!Directory.Exists(oldFolder)) continue;
                string oldFile = Path.Combine(oldFolder, "portable.json");
                if (File.Exists(oldFile)) {
                    if (!File.Exists(target)) File.Move(oldFile, target);
                    else if (File.ReadAllBytes(oldFile).SequenceEqual(File.ReadAllBytes(target))) File.Delete(oldFile);
                    else File.Move(oldFile, Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target) + ".legacy." + Guid.NewGuid().ToString("N") + ".json"));
                }
                if (!Directory.EnumerateFileSystemEntries(oldFolder).Any()) Directory.Delete(oldFolder);
            }
        }
        public static void Write(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, json);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
