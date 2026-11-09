using System;
using System.IO;
using System.Linq;
using SWToolsHub.Portable;
namespace SWToolsHub { public class Macro { public string id, file, name, module, procedure, requiredDocument; } }
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
   Console.WriteLine("8 folder checks passed; no real macros used.");
  } finally { Directory.Delete(root,true); }
 }
}
