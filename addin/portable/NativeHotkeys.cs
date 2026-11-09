using System;
using System.Runtime.InteropServices;
namespace SWToolsHub.Portable
{
    internal sealed class NativeHotkeys : IHotkeyBackend
    {
        private readonly IntPtr window;
        public NativeHotkeys(IntPtr window) { this.window = window; }
        public bool Register(int id, int value) => RegisterHotKey(window, id, Hotkey.NativeModifiers(value), (uint)(value & 0xffff));
        public void Unregister(int id) { UnregisterHotKey(window, id); }
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
