using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SWToolsHub.Portable
{
    internal static class UiTheme
    {
        private static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private static string fontPath;
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);
        public static void Initialize()
        {
            fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts", "SourceSans3-Semibold.ttf");
            if (File.Exists(fontPath)) { AddFontResourceEx(fontPath, 0x10, IntPtr.Zero); fonts.AddFontFile(fontPath); }
        }
        public static Font CreateFont() => fonts.Families.Length > 0
            ? new Font(fonts.Families[0], 11.5f, FontStyle.Regular)
            : new Font("Segoe UI", 11.5f, FontStyle.Bold);
        public static void Shutdown()
        {
            fonts.Dispose();
            if (fontPath != null) RemoveFontResourceEx(fontPath, 0x10, IntPtr.Zero);
        }
        public static GraphicsPath Shape(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure();
            return path;
        }
    }
    internal sealed class RoundedPanel : Panel
    {
        public Color BorderColor { get; set; } = Color.FromArgb(52, 55, 60);
        public RoundedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.Shape(new RectangleF(.5f, .5f, Width - 1, Height - 1), 12))
            using (var fill = new SolidBrush(BackColor))
            using (var pen = new Pen(BorderColor)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
        }
    }
    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        public RoundedButton() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.Shape(new RectangleF(.5f, .5f, Width - 1, Height - 1), 10))
            using (var fill = new SolidBrush(hovered && Enabled ? ControlPaint.Light(BackColor, .12f) : BackColor))
            using (var pen = new Pen(Focused ? Color.FromArgb(150, 155, 164) : Color.FromArgb(43, 46, 51))) {
                e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
            }
            if (Image != null) {
                int x = (Width - Image.Width) / 2, y = (Height - Image.Height) / 2;
                if (Enabled) e.Graphics.DrawImage(Image, x, y); else ControlPaint.DrawImageDisabled(e.Graphics, Image, x, y, BackColor);
            }
            var textBounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            flags |= TextAlign == ContentAlignment.MiddleCenter ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : SystemColors.GrayText, flags);
        }
    }
}
