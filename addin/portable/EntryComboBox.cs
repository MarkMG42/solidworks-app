using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SWToolsHub.Portable
{
    // Keep the native editable ComboBox and its keyboard/accessibility behavior.
    // Only the field frame, edit inset and drop-down arrow are customized.
    internal sealed class EntryComboBox : ComboBox
    {
        private bool expanded;
        private bool consumeMouseUp;
        private int Px(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));
        public EntryComboBox() { FlatStyle = FlatStyle.Flat; DropDownStyle = ComboBoxStyle.DropDown; }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); LayoutEdit(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutEdit(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); LayoutEdit(); }
        protected override void OnDropDown(EventArgs e)
        {
            expanded = true; base.OnDropDown(e); LayoutEdit(); Invalidate(); Update();
        }
        protected override void OnDropDownClosed(EventArgs e)
        {
            expanded = false; consumeMouseUp = false; base.OnDropDownClosed(e); Invalidate(); Update();
        }
        private void LayoutEdit()
        {
            if (!IsHandleCreated || IsDisposed) return;
            var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
            if (GetComboBoxInfo(Handle, ref info) && info.Edit != IntPtr.Zero)
                SetWindowPos(info.Edit, IntPtr.Zero, Px(12), info.ItemRect.Top,
                    Math.Max(1, Width - Px(44) - Px(12)), Math.Max(1, info.ItemRect.Bottom - info.ItemRect.Top), 0x0014);
        }
        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_PAINT = 0x000F, WM_PRINTCLIENT = 0x0318;
            if (m.Msg == WM_LBUTTONDOWN && Enabled && ((int)m.LParam.ToInt64() & 0xffff) >= Width - Px(40)) {
                consumeMouseUp = true; Focus(); DroppedDown = !DroppedDown; return;
            }
            if (m.Msg == WM_LBUTTONUP && consumeMouseUp) { consumeMouseUp = false; return; }
            base.WndProc(ref m);
            if (!IsHandleCreated || IsDisposed || Width < 4 || Height < 4) return;
            if (m.Msg == WM_PAINT) using (var graphics = Graphics.FromHwnd(Handle)) DrawChrome(graphics);
            else if (m.Msg == WM_PRINTCLIENT && m.WParam != IntPtr.Zero)
                using (var graphics = Graphics.FromHdc(m.WParam)) DrawChrome(graphics);
        }
        private void DrawChrome(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(BackColor)) graphics.FillRectangle(fill, Width - Px(44), 0, Px(44), Height);
            using (var erase = new Pen(BackColor, Px(3))) graphics.DrawRectangle(erase, 0, 0, Width - 1, Height - 1);
            using (var frame = UiTheme.Shape(new RectangleF(.5f, .5f, Width - 1, Height - 1), Px(8)))
            using (var pen = new Pen(UiTheme.Border)) graphics.DrawPath(pen, frame);
            float x = Width - Px(24), y = Height / 2f, direction = expanded ? -1 : 1;
            using (var pen = new Pen(Enabled ? UiTheme.Muted : SystemColors.GrayText, 1.4f * DeviceDpi / 96f)) {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                graphics.DrawLines(pen, new[] { new PointF(x - Px(4), y - direction * Px(2)), new PointF(x, y + direction * Px(2)), new PointF(x + Px(4), y - direction * Px(2)) });
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct ComboInfo
        {
            public int Size; public NativeRect ItemRect, ButtonRect; public int ButtonState; public IntPtr Combo, Edit, List;
        }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetComboBoxInfo(IntPtr handle, ref ComboInfo info);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
