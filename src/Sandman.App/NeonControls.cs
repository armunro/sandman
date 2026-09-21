using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Sandman.Core.Config;
using Sandman.Core.Models;

namespace Sandman.App
{
    /// <summary>
    /// Custom styled button for tool selection with neon glowing accents and hotkey indicator.
    /// </summary>
    public class NeonToolButton : Control
    {
        private bool _isActive;
        private bool _isHovered;
        private string _hotkey = "";
        private Color _accentColor = NeonTheme.NeonCyan;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ActiveTool Tool { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Hotkey
        {
            get => _hotkey;
            set { _hotkey = value; Invalidate(); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; Invalidate(); }
        }

        public NeonToolButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Size = new Size(110, 34);
            Cursor = Cursors.Hand;
            Font = NeonTheme.FontBodyBold;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // Background
            Color bgColor = _isActive ? NeonTheme.BgCardActive : (_isHovered ? NeonTheme.BgCardHover : NeonTheme.BgCard);
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, rect);
            }

            // Left neon accent bar if active
            if (_isActive)
            {
                using var barBrush = new SolidBrush(_accentColor);
                g.FillRectangle(barBrush, new Rectangle(0, 0, 4, Height));
                using var glowPen = new Pen(Color.FromArgb(200, _accentColor), 1.5f);
                g.DrawRectangle(glowPen, rect);
            }
            else if (_isHovered)
            {
                using var hoverPen = new Pen(Color.FromArgb(120, _accentColor), 1.0f);
                g.DrawRectangle(hoverPen, rect);
            }
            else
            {
                using var subtlePen = new Pen(NeonTheme.BorderSubtle, 1.0f);
                g.DrawRectangle(subtlePen, rect);
            }

            // Text
            var textColor = _isActive ? NeonTheme.TextPrimary : (_isHovered ? NeonTheme.TextPrimary : NeonTheme.TextMuted);
            TextRenderer.DrawText(g, (string)Text, Font, new Rectangle(10, 0, Width - 38, Height), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Hotkey badge
            if (!string.IsNullOrEmpty(_hotkey))
            {
                var hkRect = new Rectangle(Width - 28, (Height - 18) / 2, 22, 18);
                using (var hkBg = new SolidBrush(_isActive ? Color.FromArgb(60, _accentColor) : Color.FromArgb(15, 20, 30)))
                {
                    g.FillRectangle(hkBg, hkRect);
                }
                using (var hkPen = new Pen(_isActive ? _accentColor : NeonTheme.BorderSubtle, 1.0f))
                {
                    g.DrawRectangle(hkPen, hkRect);
                }
                var hkColor = _isActive ? _accentColor : NeonTheme.TextDim;
                TextRenderer.DrawText(g, _hotkey, NeonTheme.FontBadge, hkRect, hkColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>
    /// Category filter pill button with neon active highlight.
    /// </summary>
    public class NeonCategoryPill : Control
    {
        private bool _isActive;
        private bool _isHovered;
        private Color _accentColor = NeonTheme.NeonCyan;
        private int _count = 0;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string CategoryName { get; set; } = "All";

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Count
        {
            get => _count;
            set { _count = value; Invalidate(); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; Invalidate(); }
        }

        public NeonCategoryPill()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Size = new Size(88, 28);
            Cursor = Cursors.Hand;
            Font = NeonTheme.FontHeading;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color bgColor = _isActive ? Color.FromArgb(40, _accentColor.R / 3, _accentColor.G / 3, _accentColor.B / 3) : (_isHovered ? NeonTheme.BgCardHover : NeonTheme.BgCard);
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, rect);
            }

            if (_isActive)
            {
                using var pen = new Pen(_accentColor, 1.5f);
                g.DrawRectangle(pen, rect);
            }
            else if (_isHovered)
            {
                using var pen = new Pen(Color.FromArgb(120, _accentColor), 1.0f);
                g.DrawRectangle(pen, rect);
            }
            else
            {
                using var pen = new Pen(NeonTheme.BorderSubtle, 1.0f);
                g.DrawRectangle(pen, rect);
            }

            string label = _count > 0 ? $"{CategoryName} ({_count})" : CategoryName;
            Color textColor = _isActive ? _accentColor : (_isHovered ? NeonTheme.TextPrimary : NeonTheme.TextMuted);
            TextRenderer.DrawText(g, label, Font, rect, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>
    /// Interactive card for materials with color swatch, category badge, and Primary/Secondary selection rings.
    /// </summary>
    public class NeonMaterialCard : Control
    {
        private bool _isHovered;
        private bool _isPrimary;
        private bool _isSecondary;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MaterialRuntime MaterialRuntime { get; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPrimary
        {
            get => _isPrimary;
            set { _isPrimary = value; Invalidate(); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSecondary
        {
            get => _isSecondary;
            set { _isSecondary = value; Invalidate(); }
        }

        public NeonMaterialCard(MaterialRuntime mat)
        {
            MaterialRuntime = mat;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Size = new Size(140, 44);
            Cursor = Cursors.Hand;
            Margin = new Padding(3);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // Card Background
            Color bgColor = (_isPrimary || _isSecondary) ? NeonTheme.BgCardActive : (_isHovered ? NeonTheme.BgCardHover : NeonTheme.BgCard);
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, rect);
            }

            // Swatch on Left
            var swatchRect = new Rectangle(6, 6, 30, 30);
            var matColor = Color.FromArgb(MaterialRuntime.BaseR, MaterialRuntime.BaseG, MaterialRuntime.BaseB);
            using (var swatchBrush = new SolidBrush(matColor))
            {
                g.FillRectangle(swatchBrush, swatchRect);
            }
            using (var swatchPen = new Pen(Color.FromArgb(100, 255, 255, 255), 1.0f))
            {
                g.DrawRectangle(swatchPen, swatchRect);
            }

            // Material Name
            var nameRect = new Rectangle(42, 4, Width - 46, 18);
            Color nameColor = (_isPrimary || _isSecondary || _isHovered) ? NeonTheme.TextPrimary : NeonTheme.TextMuted;
            TextRenderer.DrawText(g, (string)MaterialRuntime.Definition.Name, NeonTheme.FontHeading, nameRect, nameColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // State & Category Tag
            var stateStr = MaterialRuntime.Definition.State.ToString().ToUpperInvariant();
            if (stateStr == "MOVABLESOLID") stateStr = "SAND";
            var tagRect = new Rectangle(42, 22, Width - 46, 16);
            string subText = MaterialRuntime.Definition.Category + " · " + stateStr;
            TextRenderer.DrawText(g, subText, NeonTheme.FontSmall, tagRect, NeonTheme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // Selection badges & borders
            if (_isPrimary && _isSecondary)
            {
                // Both Left and Right click
                using var pen = new Pen(NeonTheme.NeonCyan, 2.0f);
                g.DrawRectangle(pen, rect);
                DrawBadge(g, "L/R", NeonTheme.NeonYellow, Width - 32, 2);
            }
            else if (_isPrimary)
            {
                using var pen = new Pen(NeonTheme.NeonCyan, 2.0f);
                g.DrawRectangle(pen, rect);
                DrawBadge(g, "L", NeonTheme.NeonCyan, Width - 20, 2);
            }
            else if (_isSecondary)
            {
                using var pen = new Pen(NeonTheme.NeonPink, 2.0f);
                g.DrawRectangle(pen, rect);
                DrawBadge(g, "R", NeonTheme.NeonPink, Width - 20, 2);
            }
            else if (_isHovered)
            {
                using var pen = new Pen(NeonTheme.BorderBright, 1.0f);
                g.DrawRectangle(pen, rect);
            }
            else
            {
                using var pen = new Pen(NeonTheme.BorderSubtle, 1.0f);
                g.DrawRectangle(pen, rect);
            }
        }

        private static void DrawBadge(Graphics g, string text, Color color, int x, int y)
        {
            var badgeRect = new Rectangle(x, y, text.Length > 1 ? 28 : 16, 14);
            using (var brush = new SolidBrush(Color.FromArgb(60, color)))
            {
                g.FillRectangle(brush, badgeRect);
            }
            using (var pen = new Pen(color, 1.0f))
            {
                g.DrawRectangle(pen, badgeRect);
            }
            TextRenderer.DrawText(g, text, NeonTheme.FontBadge, badgeRect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>
    /// Hotbar slot (1-9, 0) for rapid switching.
    /// </summary>
    public class NeonHotbarSlot : Control
    {
        private bool _isHovered;
        private bool _isSelected;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SlotNumber { get; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ushort MaterialIndex { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string MaterialName { get; set; } = "Empty";

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color MaterialColor { get; set; } = Color.Gray;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; Invalidate(); }
        }

        public NeonHotbarSlot(int slotNumber)
        {
            SlotNumber = slotNumber;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Size = new Size(100, 36);
            Cursor = Cursors.Hand;
            Margin = new Padding(2);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color bgColor = _isSelected ? NeonTheme.BgCardActive : (_isHovered ? NeonTheme.BgCardHover : NeonTheme.BgCard);
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, rect);
            }

            // Key label
            string keyStr = SlotNumber == 10 ? "0" : SlotNumber.ToString();
            var keyRect = new Rectangle(4, 8, 18, 20);
            using (var keyBg = new SolidBrush(Color.FromArgb(20, 26, 38)))
            {
                g.FillRectangle(keyBg, keyRect);
            }
            using (var keyPen = new Pen(_isSelected ? NeonTheme.NeonCyan : NeonTheme.BorderSubtle, 1.0f))
            {
                g.DrawRectangle(keyPen, keyRect);
            }
            TextRenderer.DrawText(g, keyStr, NeonTheme.FontCode, keyRect, _isSelected ? NeonTheme.NeonCyan : NeonTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Material Color swatch
            var swatchRect = new Rectangle(26, 8, 20, 20);
            using (var swBrush = new SolidBrush(MaterialColor))
            {
                g.FillRectangle(swBrush, swatchRect);
            }
            using (var swPen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.0f))
            {
                g.DrawRectangle(swPen, swatchRect);
            }

            // Material Name
            var nameRect = new Rectangle(50, 4, Width - 52, Height - 8);
            Color nameColor = _isSelected ? NeonTheme.NeonCyan : (_isHovered ? NeonTheme.TextPrimary : NeonTheme.TextMuted);
            TextRenderer.DrawText(g, MaterialName, NeonTheme.FontBodyBold, nameRect, nameColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // Border
            if (_isSelected)
            {
                using var pen = new Pen(NeonTheme.NeonCyan, 1.5f);
                g.DrawRectangle(pen, rect);
            }
            else if (_isHovered)
            {
                using var pen = new Pen(NeonTheme.BorderBright, 1.0f);
                g.DrawRectangle(pen, rect);
            }
            else
            {
                using var pen = new Pen(NeonTheme.BorderSubtle, 1.0f);
                g.DrawRectangle(pen, rect);
            }
        }
    }

    /// <summary>
    /// Flat neon styled button for toolbar and actions.
    /// </summary>
    public class NeonButton : Control
    {
        private bool _isHovered;
        private bool _isPressed;
        private Color _accentColor = NeonTheme.NeonCyan;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; Invalidate(); }
        }

        public NeonButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Size = new Size(80, 30);
            Cursor = Cursors.Hand;
            Font = NeonTheme.FontHeading;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            _isPressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isPressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isPressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color bgColor = !Enabled ? NeonTheme.BgMain : (_isPressed ? NeonTheme.BgCardActive : (_isHovered ? NeonTheme.BgCardHover : NeonTheme.BgCard));
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, rect);
            }

            if (Enabled && (_isHovered || _isPressed))
            {
                using var pen = new Pen(_accentColor, 1.5f);
                g.DrawRectangle(pen, rect);
            }
            else
            {
                using var pen = new Pen(NeonTheme.BorderSubtle, 1.0f);
                g.DrawRectangle(pen, rect);
            }

            Color textColor = Enabled ? (_isHovered ? _accentColor : NeonTheme.TextPrimary) : NeonTheme.TextDim;
            TextRenderer.DrawText(g, (string)Text, Font, rect, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>
    /// Neon Search input box with placeholder and clear button.
    /// </summary>
    public class NeonSearchBox : Panel
    {
        private readonly TextBox _textBox;
        private readonly Button _clearBtn;
        private bool _isFocused;

        public event EventHandler? SearchTextChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SearchText
        {
            get => _textBox.Text == "Search materials (Ctrl+F)..." ? "" : _textBox.Text.Trim();
            set
            {
                _textBox.Text = value;
                UpdatePlaceholderState();
            }
        }

        public NeonSearchBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);

            Height = 32;
            BackColor = NeonTheme.BgInput;
            Padding = new Padding(6, 4, 6, 4);

            _textBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = NeonTheme.BgInput,
                ForeColor = NeonTheme.TextDim,
                Text = "Search materials (Ctrl+F)...",
                Font = NeonTheme.FontBody,
                Dock = DockStyle.Fill
            };

            _clearBtn = new Button
            {
                Text = "×",
                Dock = DockStyle.Right,
                Width = 22,
                FlatStyle = FlatStyle.Flat,
                ForeColor = NeonTheme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Visible = false
            };
            _clearBtn.FlatAppearance.BorderSize = 0;
            _clearBtn.Click += (s, e) =>
            {
                _textBox.Text = "";
                _textBox.Focus();
            };

            _textBox.Enter += (s, e) =>
            {
                _isFocused = true;
                if (_textBox.Text == "Search materials (Ctrl+F)...")
                {
                    _textBox.Text = "";
                    _textBox.ForeColor = NeonTheme.TextPrimary;
                }
                Invalidate();
            };

            _textBox.Leave += (s, e) =>
            {
                _isFocused = false;
                UpdatePlaceholderState();
                Invalidate();
            };

            _textBox.TextChanged += (s, e) =>
            {
                bool hasText = _textBox.Text.Length > 0 && _textBox.Text != "Search materials (Ctrl+F)...";
                _clearBtn.Visible = hasText;
                if (_textBox.ForeColor != NeonTheme.TextDim)
                {
                    SearchTextChanged?.Invoke(this, EventArgs.Empty);
                }
            };

            _textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    _textBox.Text = "";
                    this.Parent?.Focus();
                    e.Handled = true;
                }
            };

            Controls.Add(_textBox);
            Controls.Add(_clearBtn);
            _clearBtn.BringToFront();
        }

        public void FocusInput()
        {
            _textBox.Focus();
            _textBox.SelectAll();
        }

        private void UpdatePlaceholderState()
        {
            if (string.IsNullOrWhiteSpace(_textBox.Text))
            {
                _textBox.ForeColor = NeonTheme.TextDim;
                _textBox.Text = "Search materials (Ctrl+F)...";
                _clearBtn.Visible = false;
            }
            else
            {
                _textBox.ForeColor = NeonTheme.TextPrimary;
                _clearBtn.Visible = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            if (_isFocused)
            {
                using var pen = new Pen(NeonTheme.NeonCyan, 1.5f);
                g.DrawRectangle(pen, rect);
            }
            else
            {
                using var pen = new Pen(NeonTheme.BorderSubtle, 1.0f);
                g.DrawRectangle(pen, rect);
            }
        }
    }

    /// <summary>
    /// Section header with neon accent dot and clean typography.
    /// </summary>
    public class NeonSectionHeader : Control
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor { get; set; } = NeonTheme.NeonCyan;

        public NeonSectionHeader(string text, Color accent)
        {
            Text = text;
            AccentColor = accent;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Height = 22;
            Dock = DockStyle.Top;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Dot
            using (var dotBrush = new SolidBrush(AccentColor))
            {
                g.FillEllipse(dotBrush, 4, 7, 7, 7);
            }

            // Title
            var textRect = new Rectangle(16, 0, Width - 20, Height);
            TextRenderer.DrawText(g, (string)Text.ToUpperInvariant(), NeonTheme.FontHeading, textRect, NeonTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Subtle divider line
            int textWidth = TextRenderer.MeasureText((string)Text.ToUpperInvariant(), NeonTheme.FontHeading).Width;
            int lineStartX = 20 + textWidth + 8;
            if (lineStartX < Width - 10)
            {
                using var linePen = new Pen(NeonTheme.BorderSubtle, 1.0f);
                g.DrawLine(linePen, lineStartX, Height / 2, Width - 6, Height / 2);
            }
        }
    }
}
