using System;
using System.IO;
using System.Linq;
using SWToolsHub.Portable;
namespace SWToolsHub { public class Macro { public string id, file, name, module, procedure, requiredDocument, category; } }
class Tests {
 static void Main() {
  var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
  void Check(bool ok) { if (!ok) throw new Exception("Folder check failed"); }
  try {
   Check(MacroFolder.Scan(root).Length == 0);
   foreach (var name in new [] {"Свердловка (2).swp", "Авторазмер Рамы.swp", "Новый.SWP", "ignore.txt"}) File.WriteAllBytes(Path.Combine(root,name), Array.Empty<byte>());
   var scan = MacroFolder.Scan(root); Check(scan.Length == 3);
   Check(scan.Single(m => m.name == "Свердловка (2)").procedure == "start");
   Check(scan.Single(m => m.name == "Авторазмер Рамы").module == "FrameCutListDimensionsV3");
   Check(scan.Single(m => m.name == "Новый").module == null);
   Check(scan.All(m => File.Exists(m.id)));
   File.Delete(Path.Combine(root,"Свердловка (2).swp")); Check(MacroFolder.Scan(root).Length == 2);
   Directory.CreateDirectory(Path.Combine(root,"nested")); File.WriteAllBytes(Path.Combine(root,"nested/hidden.swp"),Array.Empty<byte>());Check(MacroFolder.Scan(root).Length == 2);
   MacroFolder.EnsureModules(root);
   Check(MacroFolder.Modules.All(m => Directory.Exists(Path.Combine(root, m))));
   foreach (var module in MacroFolder.Modules) File.WriteAllBytes(Path.Combine(root, module, "Одинаковый.swp"), Array.Empty<byte>());
   scan = MacroFolder.Scan(root); Check(scan.Length == 6);
   Check(scan.Select(m => m.id).Distinct().Count() == 6);
   Check(scan.Single(m => m.category == "Избранное").name == "Одинаковый");
   Check(scan.Count(m => m.category == "Чертежи") == 2);
   Check(scan.Single(m => m.category == "Лазер").requiredDocument == "active");
   Directory.CreateDirectory(Path.Combine(root, "Избранное", "nested"));
   File.WriteAllBytes(Path.Combine(root, "Избранное", "nested", "hidden.swp"), Array.Empty<byte>());
   Check(MacroFolder.Scan(root).Length == 6);
   Check(scan.Single(m => m.name == "Новый").category == "Без раздела");
   MacroFolder.EnsureModules(root); Check(MacroFolder.Scan(root).Length == 6);
   Check(!Directory.Exists(Path.Combine(root, "Детали")));
   Directory.CreateDirectory(Path.Combine(root, "Детали"));
   File.WriteAllBytes(Path.Combine(root, "Детали", "Старый.swp"), Array.Empty<byte>());
   Check(MacroFolder.Scan(root).Length == 7);
   Check(MacroFolder.Scan(root).Single(m => m.name == "Старый").category == "Детали");
   var methods = MacroEntry.FromCom(new string[] {"Module1.main", "Module1.other", "Module1.main", "Module2.start", "invalid"});
   Check(methods.Length == 3);
   Check(methods.Single(e => e.Module == "Module2").Procedure == "start");
   Check(MacroEntry.FromCom(null).Length == 0);
   Check(MacroEntry.FromCom("InsertWeldWithHoles.start")[0].Procedure == "start");
   var array = Array.CreateInstance(typeof(object), new[] {2}, new[] {1});
   array.SetValue("A.main",1); array.SetValue("B.start",2);
   Check(MacroEntry.FromCom(array).Length == 2);
   Check(MacroEntry.Parse(".main") == null && MacroEntry.Parse("A.") == null);
   Check(MacroEntry.Parse("  InsertWeldWithHoles.start  ").Module == "InsertWeldWithHoles");
   Console.WriteLine("27 folder and procedure checks passed; no real macros used.");
  } finally { Directory.Delete(root,true); }
 }
}
