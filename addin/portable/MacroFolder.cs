using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace SWToolsHub.Portable
{
    internal static class MacroFolder
    {
        public static readonly string[] Modules = { "Избранное", "Чертежи", "Сборки", "Лазер" };
        public static string NormalizeRoot(string folder)
        {
            folder = Path.GetFullPath(folder);
            string bundled = Path.Combine(folder, "Library", "Macros");
            if (Directory.Exists(bundled)) return bundled;
            string child = Path.Combine(folder, "Macros");
            if (Directory.Exists(child)) return child;
            var info = new DirectoryInfo(folder);
            if ((Modules.Contains(info.Name, StringComparer.OrdinalIgnoreCase) || info.Name == "Детали")
                && info.Parent != null && string.Equals(info.Parent.Name, "Macros", StringComparison.OrdinalIgnoreCase)) return info.Parent.FullName;
            return folder;
        }
        private static bool IsBundled(string folder)
        {
            var info = new DirectoryInfo(folder);
            if (info.Name.Equals("Macros", StringComparison.OrdinalIgnoreCase) && info.Parent != null && info.Parent.Name.Equals("Library", StringComparison.OrdinalIgnoreCase)) info = info.Parent.Parent;
            return info != null && (info.Name.Equals("SW-Assistant", StringComparison.OrdinalIgnoreCase) || info.Name.Equals("SWToolsHub", StringComparison.OrdinalIgnoreCase));
        }
        public static string ResolveStartup(string savedFolder, string appRoot)
        {
            string local = Path.Combine(appRoot, "Library", "Macros");
            if (string.IsNullOrWhiteSpace(savedFolder)) return local;
            string selected = NormalizeRoot(savedFolder);
            if (string.Equals(selected, local, StringComparison.OrdinalIgnoreCase)) return selected;
            // Relocate an empty old portable library, never a populated/custom library.
            if (IsBundled(selected) && (!Directory.Exists(selected) || Scan(selected).Length == 0)
                && Directory.Exists(local) && Scan(local).Length > 0) return local;
            return selected;
        }
        public static void EnsureModules(string folder)
        {
            foreach (var module in Modules) Directory.CreateDirectory(Path.Combine(folder, module));
        }
        public static Macro[] Scan(string folder)
        {
            var result = new List<Macro>();
            Read(folder, null, result);
            foreach (var module in Modules) {
                var path = Path.Combine(folder, module);
                if (Directory.Exists(path)) Read(path, module, result);
            }
            // Keep existing libraries readable without moving files or invalidating saved paths.
            var legacy = Path.Combine(folder, "Детали");
            if (Directory.Exists(legacy)) Read(legacy, "Детали", result);
            return result.OrderBy(m => m.name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        private static void Read(string folder, string category, List<Macro> result)
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(p => string.Equals(Path.GetExtension(p), ".swp", StringComparison.OrdinalIgnoreCase))) {
                var name = Path.GetFileNameWithoutExtension(path);
                bool auto = name.StartsWith("Авторазмер", StringComparison.OrdinalIgnoreCase);
                bool weld = name.StartsWith("Свердловка", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Сверловка", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Weldment Drilling", StringComparison.OrdinalIgnoreCase);
                result.Add(new Macro { id = Path.GetFullPath(path), file = Path.GetFileName(path), name = name,
                    category = category ?? (auto || weld ? "Чертежи" : "Без раздела"),
                    module = auto ? "FrameCutListDimensionsV3" : weld ? "InsertWeldWithHoles" : null,
                    procedure = auto ? "main" : weld ? "start" : null,
                    requiredDocument = auto || weld || category == "Чертежи" ? "drawing" : "active" });
            }
        }
    }
}
