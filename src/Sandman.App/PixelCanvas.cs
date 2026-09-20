using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sandman.App
{
    /// <summary>
    /// High-performance canvas control that scales low-resolution bitmaps using
    /// NearestNeighbor pixel interpolation for crisp, sharp retro pixel graphics.
    /// </summary>
    public class PixelCanvas : Control
    {
        private Image? _image;

        public event Action<Graphics, RectangleF>? PaintOverlay;

        public PixelCanvas()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Opaque, true);
            BackColor = Color.Black;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Image? Image
        {
            get => _image;
            set
            {
                _image = value;
                Invalidate();
            }
        }

        public RectangleF GetViewportRect()
        {
            if (_image == null || Width <= 0 || Height <= 0)
                return RectangleF.Empty;

            float aspectCanvas = (float)Width / Height;
            float aspectGrid = (float)_image.Width / _image.Height;

            float drawW, drawH, offsetX, offsetY;
            if (aspectCanvas > aspectGrid)
            {
                drawH = Height;
                drawW = drawH * aspectGrid;
                offsetX = (Width - drawW) / 2.0f;
                offsetY = 0;
            }
            else
            {
                drawW = Width;
                drawH = drawW / aspectGrid;
                offsetX = 0;
                offsetY = (Height - drawH) / 2.0f;
            }

            return new RectangleF(offsetX, offsetY, drawW, drawH);
        }

        public Point ScreenToGrid(Point screenPt, int gridWidth, int gridHeight)
        {
            var rect = GetViewportRect();
            if (rect.Width <= 0 || rect.Height <= 0)
                return new Point(0, 0);

            int gx = (int)((screenPt.X - rect.X) / rect.Width * gridWidth);
            int gy = (int)((screenPt.Y - rect.Y) / rect.Height * gridHeight);

            return new Point(Math.Clamp(gx, 0, gridWidth - 1), Math.Clamp(gy, 0, gridHeight - 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            if (_image == null || Width <= 0 || Height <= 0)
                return;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            var rect = GetViewportRect();
            if (rect.Width > 0 && rect.Height > 0)
            {
                g.DrawImage(_image, rect, new RectangleF(0, 0, _image.Width, _image.Height), GraphicsUnit.Pixel);

                if (PaintOverlay != null)
                {
                    g.InterpolationMode = InterpolationMode.Default;
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    PaintOverlay.Invoke(g, rect);
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Do not paint background separately to prevent flickering
        }
    }
}
