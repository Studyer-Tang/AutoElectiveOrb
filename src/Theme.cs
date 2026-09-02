using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(9, 14, 24);
        public static readonly Color Surface = Color.FromArgb(18, 27, 44);
        public static readonly Color SurfaceHigh = Color.FromArgb(27, 40, 63);
        public static readonly Color Border = Color.FromArgb(51, 67, 91);
        public static readonly Color Text = Color.FromArgb(244, 247, 252);
        public static readonly Color Secondary = Color.FromArgb(154, 171, 195);
        public static readonly Color Blue = Color.FromArgb(99, 102, 241);
        public static readonly Color Cyan = Color.FromArgb(34, 211, 238);
        public static readonly Color Green = Color.FromArgb(16, 185, 129);
        public static readonly Color Red = Color.FromArgb(244, 63, 94);

        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Button Button(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                BackColor = primary ? Color.FromArgb(36, 99, 160) : SurfaceHigh,
                ForeColor = Text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            return button;
        }
    }
}
