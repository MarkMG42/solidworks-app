using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace SWToolsHub.Portable
{
    internal sealed class StableViewport : Panel
    {
        public StableViewport() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
    }
    // Suppress intermediate empty states while rebuilding the result rows and laying out scrollbars.
    internal sealed class RedrawBatch : IDisposable
    {
        private readonly Control control;
        private readonly bool locked;
        public RedrawBatch(Control control)
        {
            this.control = control;
            locked = control.IsHandleCreated && control.Visible;
            if (locked) SendMessage(control.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            control.SuspendLayout();
        }
        public void Dispose()
        {
            try { control.ResumeLayout(true); }
            finally {
                if (locked && !control.IsDisposed && control.IsHandleCreated) {
                    SendMessage(control.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
                    RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, 0x0085); // invalidate, erase, all children
                }
            }
        }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    }
}
