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
    SettingsFile.Write(path, JsonSerializer.Serialize(new { Folder = "Library", Favorites = new[] { "fav" }, Entries = new { main = "Module.main" }, Descriptions = new Dictionary<string, string> { [original.Keys.Single()] = "Описание\nПодготовка чертежа" }, Hotkeys = original }));
    var loaded = JsonDocument.Parse(File.ReadAllText(path));
    Check(loaded.RootElement.GetProperty("Hotkeys").GetProperty(original.Keys.Single()).GetInt32() == r, "saved binding survives disk reload");
    Check(loaded.RootElement.GetProperty("Descriptions").GetProperty(original.Keys.Single()).GetString() == "Описание\nПодготовка чертежа", "multiline description persists by full macro path");
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
    string user = Path.Combine(folder, "MigrateUser");
    string oldDir = Path.Combine(user, "SWToolsHub");
    string oldSettings = Path.Combine(oldDir, "portable.json");
    string app = Path.Combine(folder, "PortableApp"); Directory.CreateDirectory(app);
    string newSettings = SettingsFile.PortablePath(app, "UserA");
    SettingsFile.MigrateUserSettings(user, newSettings);
    Check(!Directory.Exists(oldDir) && !Directory.Exists(Path.Combine(user, "SW-Assistant")), "fresh startup does not create legacy directory");
    SettingsFile.Write(oldSettings, "{\"Folder\":\"old-library\",\"Hotkeys\":{\"test\":131154}}");
    string before = File.ReadAllText(oldSettings);
    SettingsFile.MigrateUserSettings(user, newSettings);
    Check(File.ReadAllText(newSettings) == before, "migration preserves complete settings payload");
    Check(!Directory.Exists(oldDir), "migration removes empty legacy directory");
    SettingsFile.MigrateUserSettings(user, newSettings);
    Check(File.ReadAllText(newSettings) == before, "migration can repeat without data loss");
    SettingsFile.Write(oldSettings, before);
    SettingsFile.MigrateUserSettings(user, newSettings);
    Check(!Directory.Exists(oldDir), "identical legacy settings safely cleaned up");
    SettingsFile.Write(oldSettings, "{\"Folder\":\"different-library\"}");
    SettingsFile.MigrateUserSettings(user, newSettings);
    Check(File.ReadAllText(newSettings) == before, "existing new settings are not overwritten");
    var backup = Directory.GetFiles(Path.GetDirectoryName(newSettings), "*.legacy.*.json");
    Check(backup.Length == 1 && File.ReadAllText(backup[0]).Contains("different-library"), "conflicting legacy settings kept as backup");
    Check(!Directory.Exists(oldDir), "conflict leaves no empty old directory");
    Check(Path.GetDirectoryName(newSettings) == app, "settings stored directly beside EXE without extra folder");
    Check(SettingsFile.PortablePath(app, "UserA") != SettingsFile.PortablePath(app, "UserB"), "portable settings remain isolated per user");
    string intermediate = Path.Combine(user, "SW-Assistant", "portable.json");
    SettingsFile.Write(intermediate, before);
    SettingsFile.MigrateUserSettings(user, newSettings);
    Check(!Directory.Exists(Path.GetDirectoryName(intermediate)), "intermediate SW-Assistant settings directory removed when empty");
    SettingsFile.Write(oldSettings, before);
    string macroPath = Path.Combine(oldDir, "user.swp");
    File.WriteAllText(macroPath, "user data");
    SettingsFile.MigrateUserSettings(user, newSettings);
    Check(File.ReadAllText(macroPath) == "user data", "unknown files and macro paths are never moved or deleted");
    Check(!File.Exists(oldSettings), "only settings migrated when user files present");
} finally { Directory.Delete(folder, true); }
var view = new LibraryViewState();
Check(view.NeedsUpdate(null, true, new[] { "A.swp", "B.swp" }), "first search renders");
Check(!view.NeedsUpdate(null, true, new[] { "A.swp", "B.swp" }), "typing with same results skips layout");
Check(view.NeedsUpdate(null, true, new[] { "A.swp" }), "narrowed results render");
Check(view.NeedsUpdate(null, true, Array.Empty<string>()), "empty result transition renders");
Check(!view.NeedsUpdate(null, true, Array.Empty<string>()), "typing into empty result does not rebuild");
Check(view.NeedsUpdate(null, false, Array.Empty<string>()), "clearing search updates heading");
Check(view.NeedsUpdate("Сборки", false, Array.Empty<string>()), "section switch renders even when empty");
view.Invalidate();
Check(view.NeedsUpdate("Сборки", false, Array.Empty<string>()), "favorites or catalog refresh invalidates cache");
Check(LibraryViewState.FullWidth(900, false, 17) == LibraryViewState.FullWidth(883, true, 17), "scrollbar toggle does not change layout width");
Check(LibraryViewState.FullWidth(720, false, 17) == LibraryViewState.FullWidth(703, true, 17), "scrollbar does not cross responsive breakpoint");
Check(DocumentStatus.Name(@"C:\CAD\УГВС-300М.SLDASM", "wrong") == "УГВС-300М", "saved assembly uses active full path");
Check(DocumentStatus.Name(@"C:\CAD\УГВС-300М.SLDDRW", "wrong") == "УГВС-300М", "drawing name without CAD suffix");
Check(DocumentStatus.Name("", "Сборка1 *") == "Сборка1", "unsaved active title without changed marker");
Check(DocumentStatus.Name("", "УГВС.300М") == "УГВС.300М", "dots in unsaved document name retained");
Check(DocumentStatus.Name(null, null) == "Без имени", "missing name fallback");
Check(DocumentStatus.TypeName(1) == "Деталь" && DocumentStatus.TypeName(2) == "Сборка" && DocumentStatus.TypeName(3) == "Чертёж", "active document types");
Check(DocumentStatus.Name(@"D:\Other\Другая модель.sldprt", "old title") == "Другая модель", "switching active model replaces old name");
Console.WriteLine($"{count} hotkey/persistence checks passed.");

sealed class FakeBackend : IHotkeyBackend
{
    public Dictionary<int, int> Keys = new();
    public HashSet<int> Blocked = new();
    public bool Register(int id, int value) { if (Blocked.Contains(value) || Keys.ContainsValue(value)) return false; Keys.Add(id, value); return true; }
    public void Unregister(int id) { Keys.Remove(id); }
}
