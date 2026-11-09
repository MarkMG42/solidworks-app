using System;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SWToolsHub.Portable
{
    internal static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(253, 252, 248);
        public static readonly Color Surface = Color.FromArgb(253, 252, 248);
        public static readonly Color Text = Color.FromArgb(36, 35, 31);
        public static readonly Color Muted = Color.FromArgb(112, 109, 97);
        public static readonly Color Card = Color.FromArgb(253, 252, 248);
        public static readonly Color Selected = Color.FromArgb(253, 252, 248);
        public static readonly Color Accent = Color.FromArgb(112, 109, 97);
        public static readonly Color OnCard = Color.FromArgb(36, 35, 31);
        public static readonly Color Border = Color.FromArgb(227, 220, 207);
        public static readonly Color IconInk = Color.FromArgb(112, 109, 97);
        public static readonly Color IconSurface = Color.FromArgb(253, 252, 248);
        public static readonly Color IconBorder = Color.FromArgb(222, 199, 177);
        public static readonly Color IconHover = Color.FromArgb(253, 252, 248);
        public static readonly Color AccentInk = Color.FromArgb(240, 100, 36);
        public static readonly Color AccentFill = Color.FromArgb(243, 178, 138);
        private static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private static readonly List<string> fontPaths = new List<string>();
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);
        public static void Initialize()
        {
            foreach (var file in new[] { "SourceSans3-Regular.ttf", "SourceSans3-Semibold.ttf" }) {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts", file);
                if (File.Exists(path)) { AddFontResourceEx(path, 0x10, IntPtr.Zero); fonts.AddFontFile(path); fontPaths.Add(path); }
            }
        }
        public const int ControlHeight = 42, RowHeight = 56, ControlRadius = 10;
        public static Font CreateFont() => CreateRegularFont();
        public static Font CreateRegularFont()
        {
            foreach (var family in fonts.Families)
                if (family.Name == "Source Sans 3" && family.IsStyleAvailable(FontStyle.Regular)) return new Font(family, 11f, FontStyle.Regular);
            return new Font("Segoe UI", 11f, FontStyle.Regular);
        }
        public static Font CreateSemiBoldFont(float size = 14f)
        {
            foreach (var family in fonts.Families)
                if (family.Name.IndexOf("Semibold", StringComparison.OrdinalIgnoreCase) >= 0) return new Font(family, size, FontStyle.Regular);
            return new Font("Segoe UI Semibold", size, FontStyle.Regular);
        }
        public static void StyleAction(RoundedButton button, bool primary = false)
        {
            var ownedFont = primary ? CreateSemiBoldFont(11f) : CreateRegularFont();
            button.Font = ownedFont; button.Disposed += (s, e) => ownedFont.Dispose();
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? AccentInk : Background; button.ForeColor = primary ? Color.White : Text;
            button.Cursor = Cursors.Hand; button.AccentHover = true; button.CornerRadius = ControlRadius;
            button.Padding = new Padding(16, 8, 16, 8); button.Margin = new Padding(4);
            button.AutoSize = false; button.TextAlign = ContentAlignment.MiddleCenter;
            button.Size = new Size(Math.Max(112, TextRenderer.MeasureText(button.Text, ownedFont).Width + 32), ControlHeight);
        }
        public static void Shutdown()
        {
            fonts.Dispose();
            foreach (var path in fontPaths) RemoveFontResourceEx(path, 0x10, IntPtr.Zero);
            fontPaths.Clear();
        }
        public static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb((int)Math.Round(from.R + (to.R - from.R) * amount), (int)Math.Round(from.G + (to.G - from.G) * amount), (int)Math.Round(from.B + (to.B - from.B) * amount));
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
        public int CornerRadius { get; set; } = 16;
        public RoundedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.Shape(new RectangleF(.5f, .5f, Width - 1, Height - 1), CornerRadius))
            using (var fill = new SolidBrush(BackColor))
            using (var pen = new Pen(BorderColor, 1)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
        }
    }
    internal sealed class RoundedButton : Button
    {
        public string SecondaryText { get; set; }
        public int CornerRadius { get; set; } = 16;
        public float IconSize { get; set; } = 24;
        public bool IconOnly { get; set; }
        public bool AccentHover { get; set; } = true;
        public bool LeadingIcon { get; set; }
        public bool Borderless { get; set; }
        private bool hovered;
        private bool stopping;
        private float hoverProgress;
        private float animationStart, animationTarget;
        private readonly Stopwatch animationClock = new Stopwatch();
        private void AnimateTo(float target)
        {
            animationStart = hoverProgress; animationTarget = target;
            animationClock.Restart(); hoverAnimation.Start();
        }
        private readonly Timer hoverDelay = new Timer { Interval = 40 };
        private readonly Timer hoverAnimation = new Timer { Interval = 10 };
        public RoundedButton()
        {
            DoubleBuffered = true; ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            hoverDelay.Tick += (s, e) => { hoverDelay.Stop(); if (hovered && Enabled) { AnimateTo(1); } };
            hoverAnimation.Tick += (s, e) => {
                float t = Math.Min(1f, (float)animationClock.Elapsed.TotalMilliseconds / 100f);
                float eased = t * t * (3 - 2 * t);
                hoverProgress = animationStart + (animationTarget - animationStart) * eased;
                Invalidate(); if (t >= 1) { hoverAnimation.Stop(); animationClock.Stop(); }
            };
        }
        protected override void OnMouseEnter(EventArgs e) { if (!stopping) { hovered = true; hoverDelay.Stop(); hoverDelay.Start(); } base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { if (!stopping) { hovered = false; hoverDelay.Stop(); AnimateTo(0); } base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled && !stopping) { hoverDelay.Stop(); hoverAnimation.Stop(); hovered = false; hoverProgress = 0; Invalidate(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { stopping = true; hoverDelay.Dispose(); hoverAnimation.Dispose(); }
            base.Dispose(disposing);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool card = BackColor.ToArgb() == UiTheme.Card.ToArgb() || BackColor.ToArgb() == UiTheme.Selected.ToArgb();
            float progress = Enabled ? hoverProgress : 0;
            float accentProgress = AccentHover ? progress : 0;
            bool accentActive = accentProgress > 0;
            var hoverColor = AccentHover ? (BackColor.ToArgb() == UiTheme.AccentInk.ToArgb() ? Color.FromArgb(210, 76, 22) : UiTheme.AccentInk) : IconOnly ? UiTheme.IconHover : card ? UiTheme.Selected : UiTheme.Background;
            float available = Math.Max(1, Math.Min(Width, Height) - 2);
            float diameter = Math.Min(available, Math.Max(1, Math.Min(Width, Height) * (36f / 46f) - 2));
            var shapeBounds = IconOnly ? new RectangleF((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter) : new RectangleF(1, 1, Width - 2, Height - 2);
            using (var path = UiTheme.Shape(shapeBounds, IconOnly ? 8 : CornerRadius))
            using (var fill = new SolidBrush(UiTheme.Blend(BackColor, hoverColor, progress)))
            using (var pen = new Pen(UiTheme.Blend(UiTheme.Border, UiTheme.AccentInk, accentProgress), 1)) {
                if (!IconOnly || accentActive) e.Graphics.FillPath(fill, path);
                if (!IconOnly && !Borderless) e.Graphics.DrawPath(pen, path);
            }
            int iconSpace = (int)Math.Round(IconSize) + 8;
            if (Image != null) {
                float normalSize = Math.Max(1, Math.Min(IconSize, Math.Min(Width, Height) - 8));
                float size = Math.Min(Math.Min(Width, Height) - 6, normalSize * (1 + .2f * accentProgress));
                float x = LeadingIcon ? Padding.Left : (Width - size) / 2f;
                float y = LeadingIcon ? Math.Max(8, (Height - Font.Height * (string.IsNullOrEmpty(SecondaryText) ? 3 : 2) - 2) / 2f) : (Height - size) / 2f;
                var target = new RectangleF(x, y, size, size);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (accentActive) {
                    using (var attributes = new ImageAttributes()) {
                        var tint = new ColorMatrix { Matrix00 = 1 - accentProgress, Matrix11 = 1 - accentProgress, Matrix22 = 1 - accentProgress, Matrix40 = accentProgress, Matrix41 = accentProgress, Matrix42 = accentProgress };
                        attributes.SetColorMatrix(tint);
                        e.Graphics.DrawImage(Image, new[] { new PointF(x, y), new PointF(x + size, y), new PointF(x, y + size) }, new RectangleF(0, 0, Image.Width, Image.Height), GraphicsUnit.Pixel, attributes);
                    }
                } else if (Enabled) e.Graphics.DrawImage(Image, target);
                else using (var scaled = new Bitmap(Image, new Size((int)Math.Ceiling(size), (int)Math.Ceiling(size)))) ControlPaint.DrawImageDisabled(e.Graphics, scaled, (int)x, (int)y, BackColor);
            }
            var textBounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            if (LeadingIcon) { textBounds.X += iconSpace; textBounds.Width = Math.Max(1, textBounds.Width - iconSpace); }
            var textColor = Enabled ? UiTheme.Blend(ForeColor, Color.White, accentProgress) : SystemColors.GrayText;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            flags |= TextAlign == ContentAlignment.MiddleCenter ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            if (!string.IsNullOrEmpty(SecondaryText)) {
                int lineHeight = Font.Height;
                var titleBounds = new Rectangle(textBounds.Left, Math.Max(textBounds.Top, (Height - lineHeight * 2 - 2) / 2), textBounds.Width, lineHeight + 2);
                var detailBounds = new Rectangle(titleBounds.Left, titleBounds.Bottom + 2, titleBounds.Width, lineHeight);
                var lineFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(e.Graphics, Text, Font, titleBounds, textColor, lineFlags);
                using (var detailFont = new Font(Font.FontFamily, Math.Max(9, Font.Size - 1), FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics, SecondaryText, detailFont, detailBounds, UiTheme.Blend(UiTheme.Muted, Color.White, accentProgress), lineFlags);
            } else TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor, flags);
        }
    }
}
