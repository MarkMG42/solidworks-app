using System;
using System.IO;
using System.Linq;
namespace SWToolsHub.Portable
{
    internal static class SettingsFile
    {
        public static string UserPath(string localData) => Path.Combine(localData, "SW-Assistant", "portable.json");
        public static void MigrateUserSettings(string localData)
        {
            string oldFolder = Path.Combine(localData, "SWToolsHub");
            string newFolder = Path.Combine(localData, "SW-Assistant");
            if (!Directory.Exists(oldFolder)) return;
            string oldFile = Path.Combine(oldFolder, "portable.json");
            string newFile = UserPath(localData);
            if (File.Exists(oldFile)) {
                Directory.CreateDirectory(newFolder);
                if (!File.Exists(newFile)) File.Move(oldFile, newFile);
                else if (File.ReadAllBytes(oldFile).SequenceEqual(File.ReadAllBytes(newFile))) File.Delete(oldFile);
                else File.Move(oldFile, Path.Combine(newFolder, "portable.legacy." + Guid.NewGuid().ToString("N") + ".json"));
            }
            // Never delete user macros or unknown files remaining in the old folder.
            if (!Directory.EnumerateFileSystemEntries(oldFolder).Any()) Directory.Delete(oldFolder);
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
