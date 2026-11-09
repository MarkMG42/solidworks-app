using System;
using System.IO;
using System.Linq;
namespace SWToolsHub.Portable
{
    internal static class MacroFolder
    {
        public static Macro[] Scan(string folder)
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(p => string.Equals(Path.GetExtension(p), ".swp", StringComparison.OrdinalIgnoreCase))
                .Select(p => {
                    var name = Path.GetFileNameWithoutExtension(p);
                    bool auto = name.StartsWith("Авторазмер", StringComparison.OrdinalIgnoreCase);
                    bool weld = name.StartsWith("Свердловка", StringComparison.OrdinalIgnoreCase);
                    return new Macro { id = Path.GetFullPath(p), file = Path.GetFileName(p), name = name,
                        module = auto ? "FrameCutListDimensionsV3" : weld ? "InsertWeldWithHoles" : null,
                        procedure = auto ? "main" : weld ? "start" : null,
                        requiredDocument = auto || weld ? "drawing" : "active" };
                }).OrderBy(m => m.name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
    }
}
