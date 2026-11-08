using System;
using System.IO;
using System.Text.Json;
using SWToolsHub;

// Portable serialization adapter only: production uses .NET Framework JavaScriptSerializer.
namespace System.Web.Script.Serialization {
    public sealed class JavaScriptSerializer {
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
    }
}
class Program {
    static int Main(string[] args) {
        string root = Path.Combine(Path.GetTempPath(), "swtools-catalog-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "Library"));
        int count = 0;
        var source = File.ReadAllText(args[0]);
        void Check(string json, bool valid) {
            File.WriteAllText(Path.Combine(root, "Library", "macros.json"), json);
            bool passed = false;
            try { Catalog.Load(root); passed = true; } catch (Exception) { }
            if (passed != valid) throw new Exception("Неверный результат проверки каталога #" + count);
            count++;
        }
        try {
            Check(source, true);
            Check(source.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"), false);
            Check(source.Replace("weldment-drilling", "autosize"), false);
            Check(source.Replace("Авторазмер Рамы.swp", "../outside.swp"), false);
            Check(source.Replace("Авторазмер Рамы.swp", "..\\\\outside.swp"), false);
            Check(source.Replace("Авторазмер Рамы.swp", "C:outside.swp"), false);
            Check(source.Replace("Авторазмер Рамы.swp", "macro.exe"), false);
            Check(source.Replace("drawing", "part"), false);
            Check("{broken", false);
            Check("{\"schemaVersion\":1,\"macros\":[null]}", false);
            File.Delete(Path.Combine(root, "Library", "macros.json"));
            try { Catalog.Load(root); throw new Exception("Отсутствующий каталог принят"); }
            catch (FileNotFoundException) { count++; }
            Console.WriteLine(count + " проверок каталога пройдено. COM и WebView2 здесь не выполнялись.");
            return 0;
        } finally { Directory.Delete(root, true); }
    }
}
