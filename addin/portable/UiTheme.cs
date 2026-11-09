using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SWToolsHub.Portable
{
    internal static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(253, 252, 248);
        public static readonly Color Surface = Color.FromArgb(248, 243, 232);
        public static readonly Color Text = Color.FromArgb(36, 35, 31);
        public static readonly Color Muted = Color.FromArgb(112, 109, 97);
        public static readonly Color Card = Color.FromArgb(248, 243, 232);
        public static readonly Color Selected = Color.FromArgb(240, 231, 216);
        public static readonly Color Accent = Color.FromArgb(112, 109, 97);
        public static readonly Color OnCard = Color.FromArgb(36, 35, 31);
        public static readonly Color Border = Color.FromArgb(227, 220, 207);
        public static readonly Color IconInk = Color.FromArgb(112, 109, 97);
        public static readonly Color IconSurface = Color.FromArgb(252, 247, 240);
        public static readonly Color IconBorder = Color.FromArgb(222, 199, 177);
        public static readonly Color IconHover = Color.FromArgb(246, 232, 218);
        public static readonly Color AccentInk = Color.FromArgb(198, 83, 46);
        public static readonly Color AccentFill = Color.FromArgb(243, 178, 138);
        private static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private static string fontPath;
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);
        public static void Initialize()
        {
            fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts", "SourceSans3-Bold.ttf");
            if (File.Exists(fontPath)) { AddFontResourceEx(fontPath, 0x10, IntPtr.Zero); fonts.AddFontFile(fontPath); }
        }
        public static Font CreateFont() => fonts.Families.Length > 0
            ? new Font(fonts.Families[0], 14f, FontStyle.Bold)
            : new Font("Segoe UI", 14f, FontStyle.Bold);
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
        public Color BorderColor { get; set; } = UiTheme.Border;
        public RoundedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.Shape(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16))
            using (var fill = new SolidBrush(BackColor))
            using (var pen = new Pen(BorderColor, 1)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
        }
    }
    internal sealed class RoundedButton : Button
    {
        public string SecondaryText { get; set; }
        public float IconSize { get; set; } = 24;
        public bool IconOnly { get; set; }
        public bool AccentHover { get; set; }
        public bool LeadingIcon { get; set; }
        public bool Borderless { get; set; }
        private bool hovered;
        public RoundedButton() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool card = BackColor.ToArgb() == UiTheme.Card.ToArgb() || BackColor.ToArgb() == UiTheme.Selected.ToArgb();
            bool accentActive = AccentHover && hovered && Enabled;
            var hoverColor = accentActive ? UiTheme.AccentInk : IconOnly ? UiTheme.IconHover : card ? UiTheme.Selected : UiTheme.Background;
            using (var path = UiTheme.Shape(new RectangleF(1, 1, Width - 2, Height - 2), IconOnly ? Math.Min(Width, Height) / 2f : 16))
            using (var fill = new SolidBrush(hovered && Enabled ? hoverColor : BackColor))
            using (var pen = new Pen(accentActive ? UiTheme.AccentInk : Focused ? UiTheme.IconInk : IconOnly ? UiTheme.IconBorder : UiTheme.Border, Focused ? 2 : 1)) {
                e.Graphics.FillPath(fill, path); if (!Borderless || Focused) e.Graphics.DrawPath(pen, path);
            }
            int iconSpace = (int)Math.Round(IconSize) + 8;
            if (Image != null) {
                int normalSize = Math.Max(1, Math.Min((int)Math.Round(IconSize), Math.Min(Width, Height) - 8));
                int size = accentActive ? Math.Min(Math.Min(Width, Height) - 6, (int)Math.Round(normalSize * 1.2)) : normalSize;
                int x = LeadingIcon ? Padding.Left : (Width - size) / 2;
                int y = LeadingIcon ? Math.Max(8, (Height - Font.Height * (string.IsNullOrEmpty(SecondaryText) ? 3 : 2) - 2) / 2) : (Height - size) / 2;
                var target = new Rectangle(x, y, size, size);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (accentActive) {
                    using (var attributes = new ImageAttributes()) {
                        var tint = new ColorMatrix { Matrix00 = 0, Matrix11 = 0, Matrix22 = 0, Matrix40 = 1, Matrix41 = 1, Matrix42 = 1 };
                        attributes.SetColorMatrix(tint);
                        e.Graphics.DrawImage(Image, target, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, attributes);
                    }
                } else if (Enabled) e.Graphics.DrawImage(Image, target);
                else using (var scaled = new Bitmap(Image, new Size(size, size))) ControlPaint.DrawImageDisabled(e.Graphics, scaled, x, y, BackColor);
            }
            var textBounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            if (LeadingIcon) { textBounds.X += iconSpace; textBounds.Width = Math.Max(1, textBounds.Width - iconSpace); }
            var textColor = accentActive ? Color.White : Enabled ? ForeColor : SystemColors.GrayText;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            flags |= TextAlign == ContentAlignment.MiddleCenter ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            if (!string.IsNullOrEmpty(SecondaryText)) {
                int lineHeight = Font.Height;
                var titleBounds = new Rectangle(textBounds.Left, Math.Max(textBounds.Top, (Height - lineHeight * 2 - 2) / 2), textBounds.Width, lineHeight + 2);
                var detailBounds = new Rectangle(titleBounds.Left, titleBounds.Bottom + 2, titleBounds.Width, lineHeight);
                var lineFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(e.Graphics, Text, Font, titleBounds, textColor, lineFlags);
                using (var detailFont = new Font(Font.FontFamily, Math.Max(8, Font.Size - 1), Font.Style))
                    TextRenderer.DrawText(e.Graphics, SecondaryText, detailFont, detailBounds, accentActive ? Color.White : UiTheme.Muted, lineFlags);
            } else TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor, flags);
        }
    }
}
