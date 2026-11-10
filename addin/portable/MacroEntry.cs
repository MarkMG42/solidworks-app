using System;
using System.Collections.Generic;
using System.Linq;
namespace SWToolsHub.Portable
{
    internal sealed class MacroEntry
    {
        public string Module { get; private set; }
        public string Procedure { get; private set; }
        public string FullName => Module + "." + Procedure;
        public static MacroEntry Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            int split = value.LastIndexOf('.');
            if (split <= 0 || split == value.Length - 1) return null;
            var module = value.Substring(0, split).Trim();
            var procedure = value.Substring(split + 1).Trim();
            if (module.Length == 0 || procedure.Length == 0) return null;
            return new MacroEntry { Module = module, Procedure = procedure };
        }
        public static MacroEntry AutoSelect(MacroEntry[] methods, string preferred)
        {
            if (methods.Length == 1) return methods[0];
            if (methods.Length == 0) return Parse(preferred);
            return null;
        }
        public static MacroEntry[] FromCom(object value)
        {
            var results = new List<MacroEntry>();
            var array = value as Array;
            if (array != null) foreach (object item in array) { var entry = Parse(item as string); if (entry != null) results.Add(entry); }
            else { var entry = Parse(value as string); if (entry != null) results.Add(entry); }
            return results.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        }
    }
}
