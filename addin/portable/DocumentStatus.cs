using System;
namespace SWToolsHub.Portable
{
    internal static class DocumentStatus
    {
        public static string TypeName(int type) => type == 1 ? "Деталь" : type == 2 ? "Сборка" : type == 3 ? "Чертёж" : "Документ";
        public static string Name(string path, string title)
        {
            string value = string.IsNullOrWhiteSpace(path) ? title : path.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(value)) return "Без имени";
            value = value.Substring(value.LastIndexOf('/') + 1).Trim().TrimEnd('*').TrimEnd();
            foreach (string extension in new[] { ".sldasm", ".sldprt", ".slddrw" })
                if (value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return value.Substring(0, value.Length - extension.Length);
            return value;
        }
    }
}
