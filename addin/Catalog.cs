using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SWToolsHub
{
    public sealed class Catalog
    {
        public int schemaVersion { get; set; }
        public Macro[] macros { get; set; }

        public static Catalog Load(string root)
        {
            var path = Path.Combine(root, "Library", "macros.json");
            var json = File.ReadAllText(path);
            var catalog = new JavaScriptSerializer().Deserialize<Catalog>(json);
            if (catalog == null || catalog.schemaVersion != 1 || catalog.macros == null || catalog.macros.Length > 1000)
                throw new InvalidDataException("Неверный формат локального каталога.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in catalog.macros)
            {
                if (m == null || string.IsNullOrWhiteSpace(m.id) || !ids.Add(m.id) ||
                    string.IsNullOrWhiteSpace(m.name) || string.IsNullOrWhiteSpace(m.module) ||
                    string.IsNullOrWhiteSpace(m.procedure) || m.requiredDocument != "drawing" ||
                    string.IsNullOrWhiteSpace(m.file) || m.file != Path.GetFileName(m.file) || m.file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                    m.file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    !m.file.EndsWith(".swp", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Недопустимая запись в локальном каталоге.");
            }
            return catalog;
        }
    }

    public sealed class Macro
    {
        public string id { get; set; }
        public string name { get; set; }
        public string category { get; set; }
        public string description { get; set; }
        public string details { get; set; }
        public string file { get; set; }
        public string module { get; set; }
        public string procedure { get; set; }
        public string requiredDocument { get; set; }
        public string icon { get; set; }
    }
}
