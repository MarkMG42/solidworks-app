using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SWToolsHub.Portable;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
int r = Hotkey.Control | Hotkey.Alt | 0x52, d = Hotkey.Control | Hotkey.Alt | 0x44;
Check(Hotkey.IsValid(r), "Ctrl Alt R");
Check(Hotkey.IsValid(Hotkey.Shift | 0x70), "Shift F1");
Check(Hotkey.IsValid(Hotkey.Control | 0x87), "Ctrl F24");
Check(Hotkey.IsValid(Hotkey.Control | Hotkey.Alt | Hotkey.Shift | 0x30), "all modifiers digit");
foreach (int invalid in new[] { 0, 0x52, Hotkey.Control, Hotkey.Control | 0x20, Hotkey.Control | 0x88, unchecked((int)0x80020052) })
    Check(!Hotkey.IsValid(invalid), "reject invalid " + invalid);
Check(Hotkey.Format(r) == "Ctrl + Alt + R", "display letters");
Check(Hotkey.Format(Hotkey.Shift | 0x7b) == "Shift + F12", "display F12");
Check(Hotkey.NativeModifiers(r) == 0x4003, "native modifiers, no repeat");
var original = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [@"C:\Macros\Рама.swp"] = r };
var backend = new FakeBackend();
using (var service = new HotkeyService(backend)) {
    Check(service.TryApply(original, out _), "initial registration");
    int oldId = backend.Keys.Keys.Single();
    Check(service.Resolve(oldId) == original.Keys.Single(), "message resolves full path");
    var duplicate = new Dictionary<string, int>(original) { [@"C:\Other\Рама.swp"] = r };
    Check(!service.TryApply(duplicate, out _), "duplicate rejected");
    Check(service.Resolve(oldId) != null && backend.Keys.Count == 1, "duplicate leaves active set untouched");
    backend.Blocked.Add(d);
    var changed = new Dictionary<string, int> { [@"C:\Macros\Другой.swp"] = d };
    Check(!service.TryApply(changed, out var error) && error.Contains("Windows"), "OS conflict surfaced");
    Check(service.Snapshot().Single().Value == r && backend.Keys.Count == 1, "OS conflict rollback");
    Check(service.Resolve(oldId) == null, "stale queued message ignored");
    backend.Blocked.Clear();
    Check(service.TryApply(changed, out _), "valid replacement");
    Check(service.Snapshot().Single().Key == changed.Keys.Single(), "path changed correctly");
    var snapshot = service.Snapshot();
    Check(service.TryApply(new Dictionary<string, int>(), out _) && backend.Keys.Count == 0, "suspend during editor");
    Check(service.TryApply(snapshot, out _), "cancel restores registrations");
}
Check(backend.Keys.Count == 0, "closing releases hotkeys");
string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
try {
    string path = Path.Combine(folder, "UserA", "portable.json");
    SettingsFile.Write(path, JsonSerializer.Serialize(new { Folder = "Library", Favorites = new[] { "fav" }, Entries = new { main = "Module.main" }, Hotkeys = original }));
    var loaded = JsonDocument.Parse(File.ReadAllText(path));
    Check(loaded.RootElement.GetProperty("Hotkeys").GetProperty(original.Keys.Single()).GetInt32() == r, "saved binding survives disk reload");
    Check(loaded.RootElement.GetProperty("Entries").GetProperty("main").GetString() == "Module.main", "other settings retained");
    SettingsFile.Write(path, JsonSerializer.Serialize(new { Hotkeys = new Dictionary<string, int>() }));
    Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Hotkeys").EnumerateObject().Count() == 0, "removal persists");
    Check(Directory.GetFiles(Path.GetDirectoryName(path)).Length == 1, "no temporary files left");
    Check(!File.Exists(Path.Combine(folder, "UserB", "portable.json")), "per-user paths isolated");
    // A failed replacement must preserve the existing settings file.
    string blocked = Path.Combine(folder, "directory.json"); Directory.CreateDirectory(blocked);
    bool failed = false; try { SettingsFile.Write(blocked, "{}"); } catch (IOException) { failed = true; }
    Check(failed && Directory.Exists(blocked), "failed replacement preserves destination");
    Check(!Directory.GetFiles(folder).Any(p => p.EndsWith(".tmp")), "failed save cleans temporary file");
} finally { Directory.Delete(folder, true); }
Console.WriteLine($"{count} hotkey/persistence checks passed.");

sealed class FakeBackend : IHotkeyBackend
{
    public Dictionary<int, int> Keys = new();
    public HashSet<int> Blocked = new();
    public bool Register(int id, int value) { if (Blocked.Contains(value) || Keys.ContainsValue(value)) return false; Keys.Add(id, value); return true; }
    public void Unregister(int id) { Keys.Remove(id); }
}
