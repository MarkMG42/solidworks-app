using System;
using System.IO;
using System.Runtime.InteropServices;
namespace SWToolsHub.Portable
{
    // Native Windows Common Item Dialog, folder mode. No additional components.
    internal static class FolderPicker
    {
        public static string Choose(IntPtr owner, string initial)
        {
            var dialog = (IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));
            IShellItem start = null, result = null;
            try {
                uint options; dialog.GetOptions(out options);
                dialog.SetOptions(options | 0x20u | 0x40u | 0x800u); // PICKFOLDERS, FORCEFILESYSTEM, PATHMUSTEXIST
                dialog.SetTitle("Выберите папку с макросами .swp");
                dialog.SetOkButtonLabel("Выбрать папку");
                if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out start) >= 0) dialog.SetFolder(start);
                }
                int hr = dialog.Show(owner);
                if (hr == unchecked((int)0x800704C7)) return null;
                Marshal.ThrowExceptionForHR(hr);
                dialog.GetResult(out result);
                IntPtr path; result.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally {
                if (result != null) Marshal.ReleaseComObject(result);
                if (start != null) Marshal.ReleaseComObject(start);
                Marshal.ReleaseComObject(dialog);
            }
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, out IShellItem item);
        // Method order follows the Windows SDK IFileDialog vtable through GetResult.
        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, IntPtr filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
