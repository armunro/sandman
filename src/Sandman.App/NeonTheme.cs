using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sandman.App
{
    public static class NeonTheme
    {
        // Dark Base Colors
        public static readonly Color BgMain = Color.FromArgb(11, 14, 20);
        public static readonly Color BgPanel = Color.FromArgb(17, 22, 33);
        public static readonly Color BgCard = Color.FromArgb(23, 30, 44);
        public static readonly Color BgCardHover = Color.FromArgb(32, 42, 62);
        public static readonly Color BgCardActive = Color.FromArgb(38, 50, 75);
        public static readonly Color BgInput = Color.FromArgb(14, 18, 28);
        public static readonly Color BorderSubtle = Color.FromArgb(38, 48, 70);
        public static readonly Color BorderBright = Color.FromArgb(60, 80, 115);

        // Neon Accents
        public static readonly Color NeonCyan = Color.FromArgb(0, 240, 255);
        public static readonly Color NeonPink = Color.FromArgb(255, 0, 128);
        public static readonly Color NeonGreen = Color.FromArgb(57, 255, 20);
        public static readonly Color NeonOrange = Color.FromArgb(255, 140, 0);
        public static readonly Color NeonYellow = Color.FromArgb(255, 230, 0);
        public static readonly Color NeonPurple = Color.FromArgb(175, 75, 255);
        public static readonly Color NeonRed = Color.FromArgb(255, 50, 80);
        public static readonly Color NeonBlue = Color.FromArgb(40, 150, 255);

        // Text Colors
        public static readonly Color TextPrimary = Color.FromArgb(240, 246, 255);
        public static readonly Color TextMuted = Color.FromArgb(145, 165, 195);
        public static readonly Color TextDim = Color.FromArgb(90, 105, 130);

        // Fonts
        public static readonly Font FontTitle = new("Segoe UI", 10.0f, FontStyle.Bold);
        public static readonly Font FontHeading = new("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font FontBody = new("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font FontBodyBold = new("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font FontSmall = new("Segoe UI", 7.5f, FontStyle.Regular);
        public static readonly Font FontBadge = new("Segoe UI", 7.0f, FontStyle.Bold);
        public static readonly Font FontCode = new("Consolas", 8.5f, FontStyle.Bold);

        public static GraphicsPath CreateRoundedRectangle(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2.0f;
            if (rect.Width < d) d = rect.Width;
            if (rect.Height < d) d = rect.Height;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawNeonBorder(Graphics g, Rectangle rect, Color neonColor, int alpha = 200, float width = 1.5f)
        {
            using var pen = new Pen(Color.FromArgb(alpha, neonColor), width);
            g.DrawRectangle(pen, rect);
        }

        public static Color GetCategoryNeonColor(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "metals" => Color.FromArgb(255, 215, 0),       // Gold / Yellow
                "rocks" => Color.FromArgb(200, 160, 120),       // Earthy
                "liquids" => NeonCyan,                          // Cyan
                "explosives" => NeonRed,                        // Neon Red
                "flammables" => NeonOrange,                     // Neon Orange
                "woods" => Color.FromArgb(180, 120, 70),        // Wood
                "plastics" => NeonPink,                         // Neon Pink
                "tools" => NeonPurple,                          // Neon Purple
                _ => NeonCyan
            };
        }
    }

    public class NeonColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => NeonTheme.BgPanel;
        public override Color MenuStripGradientEnd => NeonTheme.BgPanel;
        public override Color ToolStripDropDownBackground => NeonTheme.BgPanel;
        public override Color ImageMarginGradientBegin => NeonTheme.BgPanel;
        public override Color ImageMarginGradientMiddle => NeonTheme.BgPanel;
        public override Color ImageMarginGradientEnd => NeonTheme.BgPanel;
        public override Color MenuBorder => NeonTheme.BorderBright;
        public override Color MenuItemBorder => NeonTheme.NeonCyan;
        public override Color MenuItemSelected => Color.FromArgb(40, 55, 80);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(40, 55, 80);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(40, 55, 80);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(30, 42, 65);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(30, 42, 65);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(30, 42, 65);
        public override Color SeparatorDark => NeonTheme.BorderSubtle;
        public override Color SeparatorLight => Color.Transparent;
        public override Color StatusStripGradientBegin => NeonTheme.BgPanel;
        public override Color StatusStripGradientEnd => NeonTheme.BgPanel;
        public override Color CheckBackground => Color.FromArgb(30, 60, 90);
        public override Color CheckSelectedBackground => Color.FromArgb(40, 80, 120);
        public override Color CheckPressedBackground => Color.FromArgb(20, 50, 80);
        public override Color ButtonSelectedHighlight => Color.FromArgb(40, 55, 80);
        public override Color ButtonSelectedBorder => NeonTheme.NeonCyan;
    }

    public class NeonToolStripRenderer : ToolStripProfessionalRenderer
    {
        public NeonToolStripRenderer() : base(new NeonColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? NeonTheme.NeonCyan : NeonTheme.TextPrimary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using var brush = new SolidBrush(Color.FromArgb(40, 55, 80));
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
                using var pen = new Pen(NeonTheme.NeonCyan, 1.0f);
                e.Graphics.DrawRectangle(pen, 0, 0, e.Item.Width - 1, e.Item.Height - 1);
            }
            else
            {
                base.OnRenderMenuItemBackground(e);
            }
        }
    }
}
