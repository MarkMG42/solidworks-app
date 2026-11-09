using System;
using System.Collections.Generic;
using System.Linq;

namespace SWToolsHub.Portable
{
    // Stored as WinForms Keys key-data, but independent of WinForms for portable checks.
    internal static class Hotkey
    {
        public const int Shift = 0x10000, Control = 0x20000, Alt = 0x40000;
        public static bool IsValid(int value)
        {
            int key = value & 0xffff;
            return (value & ~(0xffff | Shift | Control | Alt)) == 0
                && (value & (Shift | Control | Alt)) != 0
                && ((key >= 0x41 && key <= 0x5a) || (key >= 0x30 && key <= 0x39) || (key >= 0x70 && key <= 0x87));
        }
        public static string Format(int value)
        {
            if (!IsValid(value)) return "Некорректное сочетание";
            int key = value & 0xffff;
            string name = key >= 0x70 ? "F" + (key - 0x70 + 1) : ((char)key).ToString();
            return ((value & Control) != 0 ? "Ctrl + " : "") + ((value & Alt) != 0 ? "Alt + " : "")
                + ((value & Shift) != 0 ? "Shift + " : "") + name;
        }
        public static uint NativeModifiers(int value) => 0x4000u // MOD_NOREPEAT
            | ((value & Alt) != 0 ? 1u : 0) | ((value & Control) != 0 ? 2u : 0) | ((value & Shift) != 0 ? 4u : 0);
        public static string Validate(IDictionary<string, int> bindings)
        {
            var seen = new HashSet<int>();
            foreach (var pair in bindings) {
                if (string.IsNullOrWhiteSpace(pair.Key) || !IsValid(pair.Value)) return "Некорректное сочетание: " + pair.Key;
                if (!seen.Add(pair.Value)) return Format(pair.Value) + " назначено нескольким макросам.";
            }
            return null;
        }
    }
    internal interface IHotkeyBackend
    {
        bool Register(int id, int value);
        void Unregister(int id);
    }
    // Owns registrations on the UI thread; failed edits restore the previous active set.
    internal sealed class HotkeyService : IDisposable
    {
        private readonly IHotkeyBackend backend;
        private readonly Dictionary<int, KeyValuePair<string, int>> active = new Dictionary<int, KeyValuePair<string, int>>();
        private int nextId = 1;
        public HotkeyService(IHotkeyBackend backend) { this.backend = backend; }
        public string Resolve(int id) => active.TryGetValue(id, out var pair) ? pair.Key : null;
        public Dictionary<string, int> Snapshot() => active.Values.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        private void Clear() { foreach (int id in active.Keys) backend.Unregister(id); active.Clear(); }
        private bool Add(string path, int value)
        {
            // IDs are not reused: queued messages from old registrations cannot run a different macro.
            if (nextId > 0xbfff) return false;
            int id = nextId++;
            if (!backend.Register(id, value)) return false;
            active.Add(id, new KeyValuePair<string, int>(path, value)); return true;
        }
        public bool TryApply(IDictionary<string, int> bindings, out string error)
        {
            error = Hotkey.Validate(bindings); if (error != null) return false;
            var previous = Snapshot(); Clear();
            foreach (var pair in bindings) {
                if (Add(pair.Key, pair.Value)) continue;
                error = Hotkey.Format(pair.Value) + " недоступно в Windows: сочетание занято другой программой или зарезервировано. Выберите другое.";
                Clear();
                foreach (var old in previous)
                    if (!Add(old.Key, old.Value)) error += " Не удалось восстановить " + Hotkey.Format(old.Value) + "; откройте настройки горячих клавиш повторно.";
                return false;
            }
            return true;
        }
        public void Dispose() { Clear(); }
    }
}
