using System.Drawing;
using System.Drawing.Drawing2D;

namespace HiddenStock.UI;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(13, 15, 20);
    public static readonly Color Surface = Color.FromArgb(22, 25, 33);
    public static readonly Color SurfaceAlt = Color.FromArgb(31, 35, 46);
    public static readonly Color Stroke = Color.FromArgb(48, 54, 70);
    public static readonly Color Text = Color.FromArgb(236, 239, 246);
    public static readonly Color Muted = Color.FromArgb(139, 148, 168);
    public static readonly Color Accent = Color.FromArgb(124, 92, 255);
    public static readonly Color AccentHover = Color.FromArgb(142, 114, 255);
    public static readonly Color Danger = Color.FromArgb(232, 86, 86);
    public static readonly Color Ok = Color.FromArgb(74, 210, 156);

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var max = Math.Min(rect.Width, rect.Height) / 2f;
        var r = Math.Min(radius, max);
        var d = r * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
