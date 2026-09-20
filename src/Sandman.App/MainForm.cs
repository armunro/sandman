using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sandman.Core.Config;
using Sandman.Core.Models;
using Sandman.Core.Simulation;

namespace Sandman.App
{
    public enum ActiveTool
    {
        CircleBrush,
        SquareBrush,
        SprayBrush,
        Line,
        Rectangle,
        CircleShape,
        FillBucket,
        Pipette,
        Eraser,
        HeatGun,
        ColdGun,
        SparkIgniter
    }

    public partial class MainForm : Form
    {
        private const int GridWidth = 280;
        private const int GridHeight = 220;

        private MaterialRegistry _registry = null!;
        private SandGrid _grid = null!;
        private SimulationEngine _engine = null!;

        private Bitmap _renderBitmap = null!;
        private int[] _pixelBuffer = null!;
        private GCHandle _pixelBufferHandle;

        private System.Windows.Forms.Timer _simTimer = null!;

        // Interaction state
        private ActiveTool _currentTool = ActiveTool.CircleBrush;
        private ushort _primaryMaterialIndex;
        private ushort _secondaryMaterialIndex;
        private int _brushSize = 4;
        private ViewMode _currentViewMode = ViewMode.Normal;

        private bool _isMouseDown = false;
        private MouseButtons _mouseButton;
        private Point _lastMouseGridPos = new(-1, -1);
        private Point _dragStartGridPos = new(-1, -1);
        private Point _currentHoverGridPos = new(-1, -1);
        private bool _isMouseInsideCanvas = false;

        // Performance / FPS
        private int _frameCount = 0;
        private int _fps = 0;
        private DateTime _lastFpsTime = DateTime.Now;

        // UI Controls
        private PixelCanvas _canvas = null!;
        private StatusStrip _statusStrip = null!;
        private ToolStripStatusLabel _statusLabel = null!;
        private ToolStripStatusLabel _fpsLabel = null!;
        private ToolStripStatusLabel _particleLabel = null!;
        private ToolStripStatusLabel _hotkeyHintLabel = null!;

        // Tool buttons list for active state updates
        private readonly List<NeonToolButton> _toolButtons = new();

        // Material panel controls
        private FlowLayoutPanel _materialFlowPanel = null!;
        private readonly List<NeonCategoryPill> _categoryPills = new();
        private readonly List<NeonMaterialCard> _materialCards = new();
        private NeonSearchBox _searchBox = null!;
        private string _activeCategory = "All";
        private string _activeFilter = "";

        // Hotbar controls
        private readonly List<NeonHotbarSlot> _hotbarSlots = new();

        // Top bar HUD & Controls
        private NeonButton _playPauseButton = null!;
        private Label _brushSizeLabel = null!;
        private TrackBar _brushSizeTrackBar = null!;
        private CheckBox _fallThroughCheckBox = null!;
        private ToolStripMenuItem _fallThroughMenuItem = null!;
        private NumericUpDown _ambientTempNumeric = null!;
        private ComboBox _decayRateCombo = null!;

        // Material HUD Cards (Primary / Secondary)
        private Panel _primaryHudCard = null!;
        private Panel _primaryHudSwatch = null!;
        private Label _primaryHudName = null!;
        private Label _primaryHudInfo = null!;

        private Panel _secondaryHudCard = null!;
        private Panel _secondaryHudSwatch = null!;
        private Label _secondaryHudName = null!;
        private Label _secondaryHudInfo = null!;

        public MainForm()
        {
            InitializeComponentCustom();
            InitSimulation();
        }

        private void InitSimulation()
        {
            _registry = MaterialRegistry.CreateDefault();
            _grid = new SandGrid(GridWidth, GridHeight, _registry);
            _engine = new SimulationEngine(_grid);

            // Initialize rendering buffer
            _pixelBuffer = new int[GridWidth * GridHeight];
            _pixelBufferHandle = GCHandle.Alloc(_pixelBuffer, GCHandleType.Pinned);
            _renderBitmap = new Bitmap(GridWidth, GridHeight, GridWidth * 4, PixelFormat.Format32bppArgb, _pixelBufferHandle.AddrOfPinnedObject());
            _canvas.Image = _renderBitmap;
            _canvas.PaintOverlay += Canvas_PaintOverlay;
            _canvas.MouseEnter += (s, e) => { _isMouseInsideCanvas = true; _canvas.Invalidate(); };
            _canvas.MouseLeave += (s, e) => { _isMouseInsideCanvas = false; _canvas.Invalidate(); };

            // Default materials
            _primaryMaterialIndex = _registry.GetIndex("sand");
            _secondaryMaterialIndex = _registry.GetIndex("water");

            InitHotbar();
            PopulateCategories();
            PopulateMaterialPalette();
            UpdateSelectedMaterialHUD();

            // Timer for simulation loop
            _simTimer = new System.Windows.Forms.Timer();
            _simTimer.Interval = 16; // ~60 fps
            _simTimer.Tick += SimTimer_Tick;
            _simTimer.Start();
        }

        private void InitializeComponentCustom()
        {
            this.Text = "Sandman · Neon Physics Sandbox (Falling Sand Game)";
            this.Size = new Size(1280, 860);
            this.MinimumSize = new Size(780, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = NeonTheme.BgMain;
            this.ForeColor = NeonTheme.TextPrimary;
            this.DoubleBuffered = true;

            // 1. Sleek Neon Menu Strip
            var menuStrip = new MenuStrip
            {
                BackColor = NeonTheme.BgPanel,
                ForeColor = NeonTheme.TextPrimary,
                Renderer = new NeonToolStripRenderer(),
                Padding = new Padding(6, 2, 0, 2)
            };

            // File Menu
            var fileMenu = new ToolStripMenuItem("&File");
            var loadYamlItem = new ToolStripMenuItem("&Load Materials YAML...", null, (s, e) => OpenYamlDialog());
            var reloadDefaultItem = new ToolStripMenuItem("&Reload Default Materials", null, (s, e) => ReloadDefaultMaterials());
            var saveSnapshotItem = new ToolStripMenuItem("&Save Snapshot Image...", null, (s, e) => SaveSnapshotImage());
            var exitItem = new ToolStripMenuItem("E&xit", null, (s, e) => Close());
            fileMenu.DropDownItems.AddRange(new ToolStripItem[] { loadYamlItem, reloadDefaultItem, new ToolStripSeparator(), saveSnapshotItem, new ToolStripSeparator(), exitItem });

            // Simulation Menu
            var simMenu = new ToolStripMenuItem("&Simulation");
            var playPauseItem = new ToolStripMenuItem("&Play / Pause (Space)", null, (s, e) => TogglePlayPause());
            var stepItem = new ToolStripMenuItem("&Step One Tick (.)", null, (s, e) => StepSingleTick());
            var clearItem = new ToolStripMenuItem("&Clear Canvas (C)", null, (s, e) => ClearGrid());
            _fallThroughMenuItem = new ToolStripMenuItem("&Fall Out of Canvas (Open Borders)", null, (s, e) => SetFallThroughBottom(_fallThroughMenuItem.Checked))
            {
                CheckOnClick = true,
                Checked = false
            };
            var ambMenu = new ToolStripMenuItem("&Ambient Temperature");
            ambMenu.DropDownItems.Add("Deep Freeze (-50°C)", null, (s, e) => SetAmbientTemperature(-50.0f, true));
            ambMenu.DropDownItems.Add("Freezing (0°C)", null, (s, e) => SetAmbientTemperature(0.0f, true));
            ambMenu.DropDownItems.Add("Room Temperature (20°C - Default)", null, (s, e) => SetAmbientTemperature(20.0f, true));
            ambMenu.DropDownItems.Add("Hot Summer (40°C)", null, (s, e) => SetAmbientTemperature(40.0f, true));
            ambMenu.DropDownItems.Add("Boiling (100°C)", null, (s, e) => SetAmbientTemperature(100.0f, true));
            ambMenu.DropDownItems.Add("Inferno (500°C)", null, (s, e) => SetAmbientTemperature(500.0f, true));
            ambMenu.DropDownItems.Add(new ToolStripSeparator());
            ambMenu.DropDownItems.Add("Apply Current Ambient Temp to All Air", null, (s, e) => _grid.SetAmbientTemperature(_grid.AmbientTemperature, true));

            var decayMenu = new ToolStripMenuItem("&Temperature Decay Rate");
            decayMenu.DropDownItems.Add("0x - Disabled / Conserve All Heat", null, (s, e) => SetDecayRate(0.0f));
            decayMenu.DropDownItems.Add("0.1x - Very Slow Decay", null, (s, e) => SetDecayRate(0.1f));
            decayMenu.DropDownItems.Add("0.25x - Slow Decay", null, (s, e) => SetDecayRate(0.25f));
            decayMenu.DropDownItems.Add("0.5x - Gentle Decay", null, (s, e) => SetDecayRate(0.5f));
            decayMenu.DropDownItems.Add("1.0x - Normal Decay (Default)", null, (s, e) => SetDecayRate(1.0f));
            decayMenu.DropDownItems.Add("2.0x - Fast Decay", null, (s, e) => SetDecayRate(2.0f));
            decayMenu.DropDownItems.Add("4.0x - Rapid Decay", null, (s, e) => SetDecayRate(4.0f));

            simMenu.DropDownItems.AddRange(new ToolStripItem[] {
                playPauseItem, stepItem, clearItem, new ToolStripSeparator(),
                _fallThroughMenuItem, new ToolStripSeparator(),
                ambMenu, decayMenu
            });

            // View Menu
            var viewMenu = new ToolStripMenuItem("&View");
            var normalViewItem = new ToolStripMenuItem("&Normal Color View", null, (s, e) => SetViewMode(ViewMode.Normal));
            var thermalViewItem = new ToolStripMenuItem("&Thermal Heatmap Overlay", null, (s, e) => SetViewMode(ViewMode.ThermalHeatmap));
            var stateViewItem = new ToolStripMenuItem("&State of Matter View", null, (s, e) => SetViewMode(ViewMode.StateOfMatter));
            viewMenu.DropDownItems.AddRange(new ToolStripItem[] { normalViewItem, thermalViewItem, stateViewItem });

            // Demos Menu
            var demosMenu = new ToolStripMenuItem("&Demos");
            demosMenu.DropDownItems.Add("Volcano & Lava Interaction", null, (s, e) => DemoScenes.LoadVolcano(_grid));
            demosMenu.DropDownItems.Add("Explosives & Chain Detonation", null, (s, e) => DemoScenes.LoadExplosivesShowcase(_grid));
            demosMenu.DropDownItems.Add("Fuses & Detonation Speeds", null, (s, e) => DemoScenes.LoadFusesDemo(_grid));
            demosMenu.DropDownItems.Add("Binary Explosives Reaction", null, (s, e) => DemoScenes.LoadBinaryExplosivesDemo(_grid));
            demosMenu.DropDownItems.Add("Metallurgy & Melting Lab", null, (s, e) => DemoScenes.LoadMetallurgyLab(_grid));
            demosMenu.DropDownItems.Add("Fluid Density Stratification", null, (s, e) => DemoScenes.LoadFluidDynamics(_grid));

            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, simMenu, viewMenu, demosMenu });
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            // 2. Top Control & HUD Header Bar (Responsive Wrapping Flow Panel)
            var topPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = NeonTheme.BgPanel,
                Padding = new Padding(8, 4, 8, 4)
            };

            // Simulation Controls
            _playPauseButton = new NeonButton
            {
                Text = "⏸ PAUSE",
                Width = 80,
                Height = 32,
                AccentColor = NeonTheme.NeonGreen,
                Margin = new Padding(2, 2, 4, 2)
            };
            _playPauseButton.Click += (s, e) => TogglePlayPause();

            var stepButton = new NeonButton
            {
                Text = "⏭ STEP",
                Width = 70,
                Height = 32,
                AccentColor = NeonTheme.NeonCyan,
                Margin = new Padding(2, 2, 4, 2)
            };
            stepButton.Click += (s, e) => StepSingleTick();

            var clearButton = new NeonButton
            {
                Text = "🗑 CLEAR",
                Width = 74,
                Height = 32,
                AccentColor = NeonTheme.NeonRed,
                Margin = new Padding(2, 2, 6, 2)
            };
            clearButton.Click += (s, e) => ClearGrid();

            _fallThroughCheckBox = new CheckBox
            {
                Text = "Fall Out",
                AutoSize = true,
                ForeColor = NeonTheme.TextPrimary,
                Font = NeonTheme.FontBodyBold,
                Checked = false,
                Margin = new Padding(4, 6, 8, 2),
                Cursor = Cursors.Hand
            };
            var fallTip = new ToolTip();
            fallTip.SetToolTip(_fallThroughCheckBox, "Allow sand, liquids, gases, and particles to exit the canvas through all sides (Open Borders).");
            _fallThroughCheckBox.CheckedChanged += (s, e) => SetFallThroughBottom(_fallThroughCheckBox.Checked);

            // Speed Selector
            var speedLabel = new Label { Text = "SPEED:", AutoSize = true, Font = NeonTheme.FontHeading, ForeColor = NeonTheme.TextMuted, Margin = new Padding(4, 7, 2, 2) };
            var speedCombo = new ComboBox
            {
                Width = 58,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = NeonTheme.BgInput,
                ForeColor = NeonTheme.NeonCyan,
                Font = NeonTheme.FontBodyBold,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 4, 8, 2)
            };
            speedCombo.Items.AddRange(new object[] { "1x", "2x", "4x", "8x" });
            speedCombo.SelectedIndex = 0;
            speedCombo.SelectedIndexChanged += (s, e) =>
            {
                _engine.StepsPerTick = speedCombo.SelectedIndex switch
                {
                    1 => 2,
                    2 => 4,
                    3 => 8,
                    _ => 1
                };
            };

            // View Mode Selector
            var viewLabel = new Label { Text = "VIEW:", AutoSize = true, Font = NeonTheme.FontHeading, ForeColor = NeonTheme.TextMuted, Margin = new Padding(4, 7, 2, 2) };
            var viewCombo = new ComboBox
            {
                Width = 96,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = NeonTheme.BgInput,
                ForeColor = NeonTheme.NeonCyan,
                Font = NeonTheme.FontBodyBold,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 4, 8, 2)
            };
            viewCombo.Items.AddRange(new object[] { "Normal", "Heatmap", "State" });
            viewCombo.SelectedIndex = 0;
            viewCombo.SelectedIndexChanged += (s, e) => SetViewMode((ViewMode)viewCombo.SelectedIndex);

            // Brush Size Controls
            _brushSizeLabel = new Label { Text = $"BRUSH: {_brushSize}px", AutoSize = true, Font = NeonTheme.FontHeading, ForeColor = NeonTheme.NeonCyan, Margin = new Padding(4, 7, 2, 2) };
            _brushSizeTrackBar = new TrackBar
            {
                Minimum = 1,
                Maximum = 30,
                Value = _brushSize,
                Width = 80,
                TickStyle = TickStyle.None,
                Height = 24,
                Margin = new Padding(0, 4, 8, 0)
            };
            _brushSizeTrackBar.ValueChanged += (s, e) =>
            {
                _brushSize = _brushSizeTrackBar.Value;
                _brushSizeLabel.Text = $"BRUSH: {_brushSize}px";
            };

            // Ambient Temperature Controls
            var ambLabel = new Label { Text = "AMBIENT:", AutoSize = true, Font = NeonTheme.FontHeading, ForeColor = NeonTheme.TextMuted, Margin = new Padding(4, 7, 2, 2) };
            _ambientTempNumeric = new NumericUpDown
            {
                Minimum = -273,
                Maximum = 3000,
                Value = 20,
                Increment = 5,
                Width = 62,
                Height = 26,
                BackColor = NeonTheme.BgInput,
                ForeColor = NeonTheme.NeonCyan,
                Font = NeonTheme.FontBodyBold,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(2, 4, 8, 2)
            };
            var ambTip = new ToolTip();
            ambTip.SetToolTip(_ambientTempNumeric, "Ambient Room/Environment Temperature (°C)");
            _ambientTempNumeric.ValueChanged += (s, e) =>
            {
                _grid.AmbientTemperature = (float)_ambientTempNumeric.Value;
            };

            // Decay Rate Controls
            var decayLabel = new Label { Text = "DECAY:", AutoSize = true, Font = NeonTheme.FontHeading, ForeColor = NeonTheme.TextMuted, Margin = new Padding(4, 7, 2, 2) };
            _decayRateCombo = new ComboBox
            {
                Width = 86,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = NeonTheme.BgInput,
                ForeColor = NeonTheme.NeonCyan,
                Font = NeonTheme.FontBodyBold,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 4, 8, 2)
            };
            _decayRateCombo.Items.AddRange(new object[] { "0x (Off)", "0.1x", "0.25x", "0.5x", "1.0x (Norm)", "2.0x", "4.0x" });
            _decayRateCombo.SelectedIndex = 4;
            var decayTip = new ToolTip();
            decayTip.SetToolTip(_decayRateCombo, "Temperature Decay Rate (Multiplier for ambient thermal dissipation / cooling)");
            _decayRateCombo.SelectedIndexChanged += (s, e) =>
            {
                _engine.TemperatureDecayRate = _decayRateCombo.SelectedIndex switch
                {
                    0 => 0.0f,
                    1 => 0.1f,
                    2 => 0.25f,
                    3 => 0.5f,
                    4 => 1.0f,
                    5 => 2.0f,
                    6 => 4.0f,
                    _ => 1.0f
                };
            };

            // Active Material HUD Badges (Primary L-Click / Secondary R-Click)
            CreateMaterialHudCards(out _primaryHudCard, out _primaryHudSwatch, out _primaryHudName, out _primaryHudInfo,
                                   out _secondaryHudCard, out _secondaryHudSwatch, out _secondaryHudName, out _secondaryHudInfo,
                                   out var swapBtn);

            topPanel.Controls.AddRange(new Control[] {
                _playPauseButton, stepButton, clearButton,
                _fallThroughCheckBox,
                speedLabel, speedCombo,
                viewLabel, viewCombo,
                _brushSizeLabel, _brushSizeTrackBar,
                ambLabel, _ambientTempNumeric,
                decayLabel, _decayRateCombo,
                _primaryHudCard, swapBtn, _secondaryHudCard
            });

            this.Controls.Add(topPanel);

            // 3. Status Strip
            _statusStrip = new StatusStrip
            {
                BackColor = NeonTheme.BgPanel,
                ForeColor = NeonTheme.TextPrimary,
                Renderer = new NeonToolStripRenderer(),
                Padding = new Padding(6, 2, 6, 2)
            };
            _statusLabel = new ToolStripStatusLabel("Ready · Hover over canvas to inspect elements")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = NeonTheme.TextPrimary
            };
            _particleLabel = new ToolStripStatusLabel("Particles: 0")
            {
                Width = 130,
                ForeColor = NeonTheme.NeonGreen,
                Font = NeonTheme.FontHeading
            };
            _fpsLabel = new ToolStripStatusLabel("FPS: 60")
            {
                Width = 80,
                ForeColor = NeonTheme.NeonCyan,
                Font = NeonTheme.FontHeading
            };
            _hotkeyHintLabel = new ToolStripStatusLabel("[B] Brush  [E] Eraser  [1-9] Hotbar  [X] Swap  [Ctrl+F] Search")
            {
                ForeColor = NeonTheme.TextDim,
                Font = NeonTheme.FontSmall
            };

            _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _hotkeyHintLabel, _particleLabel, _fpsLabel });
            this.Controls.Add(_statusStrip);

            // 4. Bottom Quick-Access Hotbar (Slots 1-9, 0)
            var hotbarPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = NeonTheme.BgPanel,
                Padding = new Padding(10, 4, 10, 4)
            };
            var hotbarFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            var hotbarLabel = new Label
            {
                Text = "HOTBAR:",
                AutoSize = true,
                Font = NeonTheme.FontHeading,
                ForeColor = NeonTheme.TextMuted,
                Padding = new Padding(0, 10, 8, 0)
            };
            hotbarFlow.Controls.Add(hotbarLabel);

            for (int i = 1; i <= 10; i++)
            {
                var slot = new NeonHotbarSlot(i);
                int slotIndex = i;
                slot.MouseDown += (s, e) =>
                {
                    if (slot.MaterialIndex != MaterialRegistry.EmptyIndex)
                    {
                        if (e.Button == MouseButtons.Left)
                        {
                            _primaryMaterialIndex = slot.MaterialIndex;
                        }
                        else if (e.Button == MouseButtons.Right)
                        {
                            _secondaryMaterialIndex = slot.MaterialIndex;
                        }
                        UpdateSelectedMaterialHUD();
                        UpdateMaterialCardsSelection();
                        UpdateHotbarSelection();
                    }
                };
                _hotbarSlots.Add(slot);
                hotbarFlow.Controls.Add(slot);
            }
            hotbarPanel.Controls.Add(hotbarFlow);
            this.Controls.Add(hotbarPanel);

            // 5. Left Tool Panel (Categorized Tools)
            var leftToolBar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 140,
                BackColor = NeonTheme.BgPanel,
                Padding = new Padding(8, 6, 8, 6)
            };

            var toolFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            // Group: BRUSHES & SHAPES
            toolFlow.Controls.Add(new NeonSectionHeader("Draw & Shapes", NeonTheme.NeonCyan));
            AddToolButton(toolFlow, "Circle Brush", ActiveTool.CircleBrush, "B", NeonTheme.NeonCyan);
            AddToolButton(toolFlow, "Square Brush", ActiveTool.SquareBrush, "Q", NeonTheme.NeonCyan);
            AddToolButton(toolFlow, "Spray Brush", ActiveTool.SprayBrush, "S", NeonTheme.NeonCyan);
            AddToolButton(toolFlow, "Line Tool", ActiveTool.Line, "L", NeonTheme.NeonCyan);
            AddToolButton(toolFlow, "Box Shape", ActiveTool.Rectangle, "R", NeonTheme.NeonCyan);
            AddToolButton(toolFlow, "Circle Shape", ActiveTool.CircleShape, "O", NeonTheme.NeonCyan);
            AddToolButton(toolFlow, "Fill Bucket", ActiveTool.FillBucket, "F", NeonTheme.NeonCyan);

            // Group: UTILITIES
            toolFlow.Controls.Add(new NeonSectionHeader("Utilities", NeonTheme.NeonYellow));
            AddToolButton(toolFlow, "Color Picker", ActiveTool.Pipette, "P", NeonTheme.NeonYellow);
            AddToolButton(toolFlow, "Eraser", ActiveTool.Eraser, "E", NeonTheme.NeonRed);

            // Group: THERMODYNAMICS
            toolFlow.Controls.Add(new NeonSectionHeader("Thermal Tools", NeonTheme.NeonOrange));
            AddToolButton(toolFlow, "Heat Gun +", ActiveTool.HeatGun, "H", NeonTheme.NeonOrange);
            AddToolButton(toolFlow, "Cold Gun -", ActiveTool.ColdGun, "K", NeonTheme.NeonBlue);
            AddToolButton(toolFlow, "Igniter Spark", ActiveTool.SparkIgniter, "I", NeonTheme.NeonYellow);

            leftToolBar.Controls.Add(toolFlow);
            this.Controls.Add(leftToolBar);

            // 6. Right Side Panel: Material Finder & Categorized Palette
            var rightPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 340,
                BackColor = NeonTheme.BgPanel,
                Padding = new Padding(8, 6, 8, 6)
            };

            // Search Header
            var searchHeader = new NeonSectionHeader("Find Materials", NeonTheme.NeonCyan);
            rightPanel.Controls.Add(searchHeader);

            _searchBox = new NeonSearchBox
            {
                Dock = DockStyle.Top,
                Margin = new Padding(0, 4, 0, 6)
            };
            _searchBox.SearchTextChanged += (s, e) =>
            {
                _activeFilter = _searchBox.SearchText;
                FilterMaterialPalette();
            };
            rightPanel.Controls.Add(_searchBox);

            // Category Filter Pills Container
            var catHeader = new NeonSectionHeader("Categories", NeonTheme.NeonPink);
            rightPanel.Controls.Add(catHeader);

            var categoryFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 68,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = NeonTheme.BgPanel,
                Padding = new Padding(0, 2, 0, 2)
            };

            string[] categories = { "All", "Metals", "Rocks", "Liquids", "Explosives", "Flammables", "Woods", "Plastics", "Tools" };
            foreach (var cat in categories)
            {
                var pill = new NeonCategoryPill
                {
                    CategoryName = cat,
                    AccentColor = NeonTheme.GetCategoryNeonColor(cat),
                    IsActive = cat == "All",
                    Width = 98,
                    Height = 26,
                    Margin = new Padding(2)
                };
                pill.Click += (s, e) =>
                {
                    _activeCategory = pill.CategoryName;
                    foreach (var p in _categoryPills)
                    {
                        p.IsActive = (p.CategoryName == _activeCategory);
                    }
                    FilterMaterialPalette();
                };
                _categoryPills.Add(pill);
                categoryFlow.Controls.Add(pill);
            }
            rightPanel.Controls.Add(categoryFlow);

            // Palette container
            var paletteHeader = new NeonSectionHeader("Materials Palette", NeonTheme.NeonGreen);
            rightPanel.Controls.Add(paletteHeader);

            _materialFlowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = NeonTheme.BgMain,
                Padding = new Padding(4)
            };
            rightPanel.Controls.Add(_materialFlowPanel);
            _materialFlowPanel.BringToFront();

            this.Controls.Add(rightPanel);

            // 7. Central Viewport Canvas
            var canvasPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = NeonTheme.BgMain,
                Padding = new Padding(6)
            };

            _canvas = new PixelCanvas
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };

            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseWheel += Canvas_MouseWheel;

            canvasPanel.Controls.Add(_canvas);
            this.Controls.Add(canvasPanel);
            canvasPanel.BringToFront();

            // Keyboard shortcuts
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
        }

        private void CreateMaterialHudCards(
            out Panel pCard, out Panel pSwatch, out Label pName, out Label pInfo,
            out Panel sCard, out Panel sSwatch, out Label sName, out Label sInfo,
            out Button swapBtn)
        {
            // Primary (L-Click) Card
            var primaryCard = new Panel
            {
                Width = 135,
                Height = 32,
                BackColor = NeonTheme.BgCard,
                Margin = new Padding(4, 2, 2, 2),
                Padding = new Padding(2),
                Cursor = Cursors.Hand
            };
            primaryCard.Paint += (s, e) =>
            {
                if (s is Control c)
                {
                    using var pen = new Pen(NeonTheme.NeonCyan, 1.5f);
                    e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
                }
            };

            pSwatch = new Panel { Width = 24, Height = 24, Location = new Point(4, 4) };
            var pBadge = new Label { Text = "L", Font = NeonTheme.FontBadge, ForeColor = Color.Black, BackColor = NeonTheme.NeonCyan, Size = new Size(13, 13), Location = new Point(0, 0), TextAlign = ContentAlignment.MiddleCenter };
            pSwatch.Controls.Add(pBadge);

            pName = new Label { Text = "Sand", Font = NeonTheme.FontBodyBold, ForeColor = NeonTheme.NeonCyan, Location = new Point(32, 1), Size = new Size(100, 15) };
            pInfo = new Label { Text = "Solid · 20°C", Font = NeonTheme.FontSmall, ForeColor = NeonTheme.TextDim, Location = new Point(32, 16), Size = new Size(100, 14) };
            primaryCard.Controls.AddRange(new Control[] { pSwatch, pName, pInfo });
            pCard = primaryCard;

            // Swap Button
            swapBtn = new Button
            {
                Text = "⇄",
                Width = 26,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = NeonTheme.BgCard,
                ForeColor = NeonTheme.NeonCyan,
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 4, 2, 2)
            };
            swapBtn.FlatAppearance.BorderColor = NeonTheme.BorderBright;
            var tip = new ToolTip();
            tip.SetToolTip(swapBtn, "Swap Left and Right materials (Hotkey: X)");
            swapBtn.Click += (s, e) => SwapMaterials();

            // Secondary (R-Click) Card
            var secondaryCard = new Panel
            {
                Width = 135,
                Height = 32,
                BackColor = NeonTheme.BgCard,
                Margin = new Padding(2, 2, 4, 2),
                Padding = new Padding(2),
                Cursor = Cursors.Hand
            };
            secondaryCard.Paint += (s, e) =>
            {
                if (s is Control c)
                {
                    using var pen = new Pen(NeonTheme.NeonPink, 1.5f);
                    e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
                }
            };

            sSwatch = new Panel { Width = 24, Height = 24, Location = new Point(4, 4) };
            var sBadge = new Label { Text = "R", Font = NeonTheme.FontBadge, ForeColor = Color.White, BackColor = NeonTheme.NeonPink, Size = new Size(13, 13), Location = new Point(0, 0), TextAlign = ContentAlignment.MiddleCenter };
            sSwatch.Controls.Add(sBadge);

            sName = new Label { Text = "Water", Font = NeonTheme.FontBodyBold, ForeColor = NeonTheme.NeonPink, Location = new Point(32, 1), Size = new Size(100, 15) };
            sInfo = new Label { Text = "Liquid · 20°C", Font = NeonTheme.FontSmall, ForeColor = NeonTheme.TextDim, Location = new Point(32, 16), Size = new Size(100, 14) };
            secondaryCard.Controls.AddRange(new Control[] { sSwatch, sName, sInfo });
            sCard = secondaryCard;
        }

        private void AddToolButton(FlowLayoutPanel panel, string name, ActiveTool tool, string hotkey, Color accent)
        {
            var btn = new NeonToolButton
            {
                Text = name,
                Tool = tool,
                Hotkey = hotkey,
                AccentColor = accent,
                Width = 124,
                Height = 32,
                IsActive = _currentTool == tool,
                Margin = new Padding(0, 2, 0, 2)
            };

            btn.Click += (s, e) => SelectTool(tool);
            _toolButtons.Add(btn);
            panel.Controls.Add(btn);
        }

        private void SelectTool(ActiveTool tool)
        {
            _currentTool = tool;
            foreach (var b in _toolButtons)
            {
                b.IsActive = (b.Tool == tool);
            }
            _canvas.Invalidate();
        }

        private void InitHotbar()
        {
            string[] defaultHotbar = { "sand", "water", "fire", "stone", "gunpowder", "acid", "oil", "lava", "wall", "wood" };
            for (int i = 0; i < _hotbarSlots.Count && i < defaultHotbar.Length; i++)
            {
                var mat = _registry.TryGetMaterial(defaultHotbar[i]);
                if (mat != null)
                {
                    _hotbarSlots[i].MaterialIndex = mat.Index;
                    _hotbarSlots[i].MaterialName = mat.Definition.Name;
                    _hotbarSlots[i].MaterialColor = Color.FromArgb(mat.BaseR, mat.BaseG, mat.BaseB);
                }
            }
            UpdateHotbarSelection();
        }

        private void UpdateHotbarSelection()
        {
            foreach (var slot in _hotbarSlots)
            {
                slot.IsSelected = (slot.MaterialIndex == _primaryMaterialIndex);
            }
        }

        private void PopulateCategories()
        {
            var materials = _registry.Materials.Where(m => m.Index != MaterialRegistry.EmptyIndex).ToList();
            foreach (var pill in _categoryPills)
            {
                if (pill.CategoryName == "All")
                {
                    pill.Count = materials.Count;
                }
                else
                {
                    pill.Count = materials.Count(m => m.Definition.Category.Equals(pill.CategoryName, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        private void PopulateMaterialPalette()
        {
            _materialCards.Clear();
            _materialFlowPanel.SuspendLayout();
            _materialFlowPanel.Controls.Clear();

            var materials = _registry.Materials;
            foreach (var mat in materials)
            {
                if (mat.Index == MaterialRegistry.EmptyIndex) continue;

                var card = new NeonMaterialCard(mat);
                ushort matIdx = mat.Index;

                var tip = new ToolTip();
                string tipText = $"{mat.Definition.Name} [{mat.Definition.Category}]\n" +
                                 $"State: {mat.Definition.State} | Density: {mat.Definition.Density:F2}\n" +
                                 $"Temp: {mat.Definition.DefaultTemperature}°C\n";
                if (mat.Definition.MeltingPoint.HasValue) tipText += $"Melt: {mat.Definition.MeltingPoint}°C -> {mat.Definition.MeltTarget}\n";
                if (mat.Definition.FreezingPoint.HasValue) tipText += $"Freeze: {mat.Definition.FreezingPoint}°C -> {mat.Definition.FreezeTarget}\n";
                if (mat.Definition.BoilingPoint.HasValue) tipText += $"Boil: {mat.Definition.BoilingPoint}°C -> {mat.Definition.BoilTarget}\n";
                if (mat.Definition.IsFlammable) tipText += $"Flammable: Ignites at {mat.Definition.IgnitionTemperature}°C\n";
                if (mat.Definition.IsExplosive) tipText += $"Explosive: Radius {mat.Definition.ExplosionRadius}, Force {mat.Definition.ExplosionForce}\n";
                tip.SetToolTip(card, tipText);

                card.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        _primaryMaterialIndex = matIdx;
                    }
                    else if (e.Button == MouseButtons.Right)
                    {
                        _secondaryMaterialIndex = matIdx;
                    }
                    UpdateSelectedMaterialHUD();
                    UpdateMaterialCardsSelection();
                    UpdateHotbarSelection();
                };

                card.DoubleClick += (s, e) =>
                {
                    _primaryMaterialIndex = matIdx;
                    _secondaryMaterialIndex = matIdx;
                    UpdateSelectedMaterialHUD();
                    UpdateMaterialCardsSelection();
                    UpdateHotbarSelection();
                };

                _materialCards.Add(card);
            }

            FilterMaterialPalette();
            _materialFlowPanel.ResumeLayout();
        }

        private void FilterMaterialPalette()
        {
            _materialFlowPanel.SuspendLayout();
            _materialFlowPanel.Controls.Clear();

            string filter = _activeFilter.Trim();

            foreach (var card in _materialCards)
            {
                var def = card.MaterialRuntime.Definition;

                // Category filter
                bool matchCat = _activeCategory == "All" || def.Category.Equals(_activeCategory, StringComparison.OrdinalIgnoreCase);

                // Search query filter
                bool matchText = string.IsNullOrEmpty(filter) ||
                                 def.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 def.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 def.State.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 def.Id.Contains(filter, StringComparison.OrdinalIgnoreCase);

                if (matchCat && matchText)
                {
                    card.IsPrimary = (card.MaterialRuntime.Index == _primaryMaterialIndex);
                    card.IsSecondary = (card.MaterialRuntime.Index == _secondaryMaterialIndex);
                    _materialFlowPanel.Controls.Add(card);
                }
            }

            _materialFlowPanel.ResumeLayout();
        }

        private void UpdateMaterialCardsSelection()
        {
            foreach (var card in _materialCards)
            {
                card.IsPrimary = (card.MaterialRuntime.Index == _primaryMaterialIndex);
                card.IsSecondary = (card.MaterialRuntime.Index == _secondaryMaterialIndex);
            }
        }

        private void UpdateSelectedMaterialHUD()
        {
            var pMat = _registry.GetMaterial(_primaryMaterialIndex);
            var sMat = _registry.GetMaterial(_secondaryMaterialIndex);

            _primaryHudSwatch.BackColor = Color.FromArgb(pMat.BaseR, pMat.BaseG, pMat.BaseB);
            _primaryHudName.Text = pMat.Definition.Name;
            _primaryHudInfo.Text = $"{pMat.Definition.State} · {pMat.Definition.DefaultTemperature:F0}°C";

            _secondaryHudSwatch.BackColor = Color.FromArgb(sMat.BaseR, sMat.BaseG, sMat.BaseB);
            _secondaryHudName.Text = sMat.Definition.Name;
            _secondaryHudInfo.Text = $"{sMat.Definition.State} · {sMat.Definition.DefaultTemperature:F0}°C";
        }

        private void SwapMaterials()
        {
            var temp = _primaryMaterialIndex;
            _primaryMaterialIndex = _secondaryMaterialIndex;
            _secondaryMaterialIndex = temp;

            UpdateSelectedMaterialHUD();
            UpdateMaterialCardsSelection();
            UpdateHotbarSelection();
        }

        private void SimTimer_Tick(object? sender, EventArgs e)
        {
            if (_engine.IsRunning)
            {
                for (int i = 0; i < _engine.StepsPerTick; i++)
                {
                    _engine.Step();
                }
            }

            // Render to buffer and invalidate canvas
            _grid.RenderToPixelBuffer(_pixelBuffer, _currentViewMode);
            _canvas.Invalidate();

            // Update Inspector Status Bar continuously during simulation
            UpdateInspectorStatusBar();

            // FPS calculation
            _frameCount++;
            var now = DateTime.Now;
            if ((now - _lastFpsTime).TotalSeconds >= 1.0)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _lastFpsTime = now;
                _fpsLabel.Text = $"FPS: {_fps}";
                _particleLabel.Text = $"Particles: {_grid.CountActiveParticles()}";
            }
        }

        private void UpdateInspectorStatusBar()
        {
            if (_isMouseInsideCanvas && _grid.InBounds(_currentHoverGridPos.X, _currentHoverGridPos.Y))
            {
                ref var cell = ref _grid.GetCell(_currentHoverGridPos.X, _currentHoverGridPos.Y);
                var mat = _registry.GetMaterial(cell.MaterialIndex);
                string burningStr = cell.IsBurning ? " [BURNING]" : "";
                _statusLabel.Text = $"({_currentHoverGridPos.X}, {_currentHoverGridPos.Y}) · {mat.Definition.Name} [{mat.Definition.Category}, {mat.Definition.State}] · Temp: {cell.Temperature:F1}°C · Density: {mat.Definition.Density:F2}{burningStr}";
            }
        }

        private Point ScreenToGridPos(Point screenPt)
        {
            return _canvas.ScreenToGrid(screenPt, GridWidth, GridHeight);
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            _isMouseDown = true;
            _mouseButton = e.Button;
            var pt = ScreenToGridPos(e.Location);
            _dragStartGridPos = pt;
            _lastMouseGridPos = pt;
            _currentHoverGridPos = pt;
            _isMouseInsideCanvas = true;

            if (_currentTool != ActiveTool.Line && _currentTool != ActiveTool.Rectangle && _currentTool != ActiveTool.CircleShape)
            {
                ApplyToolAt(pt, _mouseButton, isInitialClick: true);
            }
            _canvas.Invalidate();
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            _isMouseInsideCanvas = true;
            var pt = ScreenToGridPos(e.Location);
            _currentHoverGridPos = pt;

            // Update Inspector Status Bar
            UpdateInspectorStatusBar();

            if (_isMouseDown)
            {
                if (_currentTool == ActiveTool.Line || _currentTool == ActiveTool.Rectangle || _currentTool == ActiveTool.CircleShape)
                {
                    // Drag shapes apply on MouseUp, preview renders live
                }
                else
                {
                    ApplyToolContinuous(_lastMouseGridPos, pt, _mouseButton);
                }
                _lastMouseGridPos = pt;
            }
            _canvas.Invalidate();
        }

        private void Canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!_isMouseDown) return;
            _isMouseDown = false;

            var pt = ScreenToGridPos(e.Location);
            ushort mat = _mouseButton == MouseButtons.Left ? _primaryMaterialIndex : _secondaryMaterialIndex;

            if (_currentTool == ActiveTool.Line)
            {
                _grid.DrawLine(_dragStartGridPos.X, _dragStartGridPos.Y, pt.X, pt.Y, _brushSize, mat);
            }
            else if (_currentTool == ActiveTool.Rectangle)
            {
                _grid.DrawBox(_dragStartGridPos.X, _dragStartGridPos.Y, pt.X, pt.Y, mat, filled: Control.ModifierKeys.HasFlag(Keys.Shift), thickness: _brushSize);
            }
            else if (_currentTool == ActiveTool.CircleShape)
            {
                _grid.DrawEllipse(_dragStartGridPos.X, _dragStartGridPos.Y, pt.X, pt.Y, mat, filled: Control.ModifierKeys.HasFlag(Keys.Shift), thickness: _brushSize);
            }
            _canvas.Invalidate();
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            int delta = e.Delta > 0 ? 1 : -1;
            _brushSize = Math.Clamp(_brushSize + delta, 1, 30);
            _brushSizeTrackBar.Value = _brushSize;
            _brushSizeLabel.Text = $"BRUSH: {_brushSize}px";
        }

        private void ApplyToolAt(Point pt, MouseButtons btn, bool isInitialClick)
        {
            ushort mat = btn == MouseButtons.Left ? _primaryMaterialIndex : _secondaryMaterialIndex;

            switch (_currentTool)
            {
                case ActiveTool.CircleBrush:
                    _grid.DrawCircle(pt.X, pt.Y, _brushSize, mat);
                    break;

                case ActiveTool.SquareBrush:
                    _grid.DrawSquare(pt.X, pt.Y, _brushSize * 2, mat);
                    break;

                case ActiveTool.SprayBrush:
                    _grid.DrawCircle(pt.X, pt.Y, _brushSize * 2, mat, sprayDensity: 0.25f);
                    break;

                case ActiveTool.FillBucket:
                    if (isInitialClick)
                        _grid.FloodFill(pt.X, pt.Y, mat);
                    break;

                case ActiveTool.Pipette:
                    if (_grid.InBounds(pt.X, pt.Y))
                    {
                        ushort picked = _grid.GetCell(pt.X, pt.Y).MaterialIndex;
                        if (picked != MaterialRegistry.EmptyIndex)
                        {
                            if (btn == MouseButtons.Left)
                                _primaryMaterialIndex = picked;
                            else
                                _secondaryMaterialIndex = picked;
                            UpdateSelectedMaterialHUD();
                            UpdateMaterialCardsSelection();
                            UpdateHotbarSelection();
                        }
                    }
                    break;

                case ActiveTool.Eraser:
                    _grid.DrawCircle(pt.X, pt.Y, _brushSize, MaterialRegistry.EmptyIndex);
                    break;

                case ActiveTool.HeatGun:
                    _grid.ApplyThermalBrush(pt.X, pt.Y, _brushSize * 2, 100.0f);
                    break;

                case ActiveTool.ColdGun:
                    _grid.ApplyThermalBrush(pt.X, pt.Y, _brushSize * 2, -100.0f);
                    break;

                case ActiveTool.SparkIgniter:
                    ushort sparkIdx = _registry.GetIndex("spark");
                    _grid.DrawCircle(pt.X, pt.Y, Math.Max(2, _brushSize), sparkIdx > 0 ? sparkIdx : _registry.GetIndex("fire"), 1200.0f);
                    break;
            }
        }

        private void ApplyToolContinuous(Point p1, Point p2, MouseButtons btn)
        {
            ushort mat = btn == MouseButtons.Left ? _primaryMaterialIndex : _secondaryMaterialIndex;

            switch (_currentTool)
            {
                case ActiveTool.CircleBrush:
                    _grid.DrawLine(p1.X, p1.Y, p2.X, p2.Y, _brushSize, mat);
                    break;

                case ActiveTool.SquareBrush:
                    _grid.DrawSquare(p2.X, p2.Y, _brushSize * 2, mat);
                    break;

                case ActiveTool.SprayBrush:
                    _grid.DrawCircle(p2.X, p2.Y, _brushSize * 2, mat, sprayDensity: 0.25f);
                    break;

                case ActiveTool.Eraser:
                    _grid.DrawLine(p1.X, p1.Y, p2.X, p2.Y, _brushSize, MaterialRegistry.EmptyIndex);
                    break;

                case ActiveTool.HeatGun:
                    _grid.ApplyThermalBrush(p2.X, p2.Y, _brushSize * 2, 60.0f);
                    break;

                case ActiveTool.ColdGun:
                    _grid.ApplyThermalBrush(p2.X, p2.Y, _brushSize * 2, -60.0f);
                    break;

                case ActiveTool.SparkIgniter:
                    ushort sparkIdx = _registry.GetIndex("spark");
                    _grid.DrawCircle(p2.X, p2.Y, Math.Max(2, _brushSize), sparkIdx > 0 ? sparkIdx : _registry.GetIndex("fire"), 1200.0f);
                    break;
            }
        }

        private void TogglePlayPause()
        {
            _engine.IsRunning = !_engine.IsRunning;
            _playPauseButton.Text = _engine.IsRunning ? "⏸ PAUSE" : "▶ PLAY";
            _playPauseButton.AccentColor = _engine.IsRunning ? NeonTheme.NeonGreen : NeonTheme.NeonOrange;
        }

        private void StepSingleTick()
        {
            _engine.IsRunning = false;
            _playPauseButton.Text = "▶ PLAY";
            _playPauseButton.AccentColor = NeonTheme.NeonOrange;
            _engine.Step();
        }

        private void ClearGrid()
        {
            _grid.Clear();
        }

        private void SetViewMode(ViewMode mode)
        {
            _currentViewMode = mode;
        }

        private void SetFallThroughBottom(bool enabled)
        {
            _engine.FallThroughBottom = enabled;
            if (_fallThroughCheckBox != null && _fallThroughCheckBox.Checked != enabled)
                _fallThroughCheckBox.Checked = enabled;
            if (_fallThroughMenuItem != null && _fallThroughMenuItem.Checked != enabled)
                _fallThroughMenuItem.Checked = enabled;
        }

        private void SetAmbientTemperature(float temp, bool updateAir = false)
        {
            _grid.SetAmbientTemperature(temp, updateAir);
            if (_ambientTempNumeric != null && _ambientTempNumeric.Value != (decimal)temp)
            {
                _ambientTempNumeric.Value = Math.Clamp((decimal)temp, _ambientTempNumeric.Minimum, _ambientTempNumeric.Maximum);
            }
        }

        private void SetDecayRate(float rate)
        {
            _engine.TemperatureDecayRate = rate;
            int index = rate switch
            {
                <= 0.01f => 0,
                <= 0.15f => 1,
                <= 0.35f => 2,
                <= 0.75f => 3,
                <= 1.5f => 4,
                <= 3.0f => 5,
                _ => 6
            };
            if (_decayRateCombo != null && _decayRateCombo.SelectedIndex != index)
            {
                _decayRateCombo.SelectedIndex = index;
            }
        }

        private void Canvas_PaintOverlay(Graphics g, RectangleF vp)
        {
            if (!_isMouseInsideCanvas) return;

            float scaleX = vp.Width / GridWidth;
            float scaleY = vp.Height / GridHeight;

            ushort activeMatIdx = (_isMouseDown && _mouseButton == MouseButtons.Right) ? _secondaryMaterialIndex : _primaryMaterialIndex;
            var activeMat = _registry.GetMaterial(activeMatIdx);
            Color matColor = Color.FromArgb(activeMat.BaseR, activeMat.BaseG, activeMat.BaseB);

            if (_isMouseDown && (_currentTool == ActiveTool.Rectangle || _currentTool == ActiveTool.CircleShape || _currentTool == ActiveTool.Line))
            {
                // Live drag shape preview underlay
                int minX = Math.Min(_dragStartGridPos.X, _currentHoverGridPos.X);
                int maxX = Math.Max(_dragStartGridPos.X, _currentHoverGridPos.X);
                int minY = Math.Min(_dragStartGridPos.Y, _currentHoverGridPos.Y);
                int maxY = Math.Max(_dragStartGridPos.Y, _currentHoverGridPos.Y);
                int gw = maxX - minX + 1;
                int gh = maxY - minY + 1;

                float sx = vp.X + minX * scaleX;
                float sy = vp.Y + minY * scaleY;
                float sw = gw * scaleX;
                float sh = gh * scaleY;
                var sRect = new RectangleF(sx, sy, sw, sh);

                bool isShift = Control.ModifierKeys.HasFlag(Keys.Shift);

                if (_currentTool == ActiveTool.Rectangle)
                {
                    if (isShift)
                    {
                        using (var fillBrush = new SolidBrush(Color.FromArgb(140, matColor.R, matColor.G, matColor.B)))
                        {
                            g.FillRectangle(fillBrush, sRect);
                        }
                    }
                    else
                    {
                        float thickPx = Math.Max(1.0f, _brushSize * scaleX);
                        using (var penThick = new Pen(Color.FromArgb(120, matColor.R, matColor.G, matColor.B), thickPx))
                        {
                            penThick.Alignment = PenAlignment.Inset;
                            g.DrawRectangle(penThick, sRect.X, sRect.Y, sRect.Width, sRect.Height);
                        }
                    }
                    using (var penShadow = new Pen(Color.FromArgb(180, 0, 0, 0), 3.0f))
                    {
                        g.DrawRectangle(penShadow, sRect.X, sRect.Y, sRect.Width, sRect.Height);
                    }
                    using (var penBorder = new Pen(NeonTheme.NeonCyan, 1.5f))
                    {
                        penBorder.DashStyle = DashStyle.Dash;
                        g.DrawRectangle(penBorder, sRect.X, sRect.Y, sRect.Width, sRect.Height);
                    }
                    DrawDimensionTag(g, $"{gw} × {gh} [Thick: {_brushSize}px] {(isShift ? "(Filled)" : "(Outline)")} [Hold Shift to Fill]", sRect.Right + 6, sRect.Bottom + 6);
                }
                else if (_currentTool == ActiveTool.CircleShape)
                {
                    if (isShift)
                    {
                        using (var fillBrush = new SolidBrush(Color.FromArgb(140, matColor.R, matColor.G, matColor.B)))
                        {
                            g.FillEllipse(fillBrush, sRect);
                        }
                    }
                    else
                    {
                        float thickPx = Math.Max(1.0f, _brushSize * scaleX);
                        using (var penThick = new Pen(Color.FromArgb(120, matColor.R, matColor.G, matColor.B), thickPx))
                        {
                            penThick.Alignment = PenAlignment.Inset;
                            g.DrawEllipse(penThick, sRect);
                        }
                    }
                    using (var penShadow = new Pen(Color.FromArgb(180, 0, 0, 0), 3.0f))
                    {
                        g.DrawEllipse(penShadow, sRect);
                    }
                    using (var penBorder = new Pen(NeonTheme.NeonCyan, 1.5f))
                    {
                        penBorder.DashStyle = DashStyle.Dash;
                        g.DrawEllipse(penBorder, sRect);
                    }
                    DrawDimensionTag(g, $"Ø {Math.Max(gw, gh)} ({gw} × {gh}) [Thick: {_brushSize}px] {(isShift ? "(Filled)" : "(Outline)")} [Hold Shift to Fill]", sRect.Right + 6, sRect.Bottom + 6);
                }
                else if (_currentTool == ActiveTool.Line)
                {
                    float x0 = vp.X + (_dragStartGridPos.X + 0.5f) * scaleX;
                    float y0 = vp.Y + (_dragStartGridPos.Y + 0.5f) * scaleY;
                    float x1 = vp.X + (_currentHoverGridPos.X + 0.5f) * scaleX;
                    float y1 = vp.Y + (_currentHoverGridPos.Y + 0.5f) * scaleY;
                    float thickness = Math.Max(2.0f, _brushSize * 2.0f * scaleX);

                    using (var penUnderlay = new Pen(Color.FromArgb(100, matColor.R, matColor.G, matColor.B), thickness))
                    {
                        penUnderlay.StartCap = LineCap.Round;
                        penUnderlay.EndCap = LineCap.Round;
                        g.DrawLine(penUnderlay, x0, y0, x1, y1);
                    }
                    using (var penLine = new Pen(NeonTheme.NeonCyan, 1.5f))
                    {
                        penLine.DashStyle = DashStyle.Dash;
                        g.DrawLine(penLine, x0, y0, x1, y1);
                    }
                    int dist = (int)Math.Round(Math.Sqrt(Math.Pow(_currentHoverGridPos.X - _dragStartGridPos.X, 2) + Math.Pow(_currentHoverGridPos.Y - _dragStartGridPos.Y, 2)));
                    DrawDimensionTag(g, $"Length: {dist}px", x1 + 6, y1 + 6);
                }
            }
            else
            {
                // Hover brush shape cursor preview
                float cx = vp.X + (_currentHoverGridPos.X + 0.5f) * scaleX;
                float cy = vp.Y + (_currentHoverGridPos.Y + 0.5f) * scaleY;

                switch (_currentTool)
                {
                    case ActiveTool.CircleBrush:
                    {
                        float r = _brushSize * scaleX;
                        var sRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                        using (var fillBrush = new SolidBrush(Color.FromArgb(70, matColor.R, matColor.G, matColor.B)))
                        {
                            g.FillEllipse(fillBrush, sRect);
                        }
                        using (var penBorder = new Pen(NeonTheme.NeonCyan, 1.2f))
                        {
                            g.DrawEllipse(penBorder, sRect);
                        }
                        break;
                    }

                    case ActiveTool.SquareBrush:
                    {
                        float size = (_brushSize * 2) * scaleX;
                        var sRect = new RectangleF(cx - size / 2, cy - size / 2, size, size);
                        using (var fillBrush = new SolidBrush(Color.FromArgb(70, matColor.R, matColor.G, matColor.B)))
                        {
                            g.FillRectangle(fillBrush, sRect);
                        }
                        using (var penBorder = new Pen(NeonTheme.NeonCyan, 1.2f))
                        {
                            g.DrawRectangle(penBorder, sRect.X, sRect.Y, sRect.Width, sRect.Height);
                        }
                        break;
                    }

                    case ActiveTool.SprayBrush:
                    {
                        float r = (_brushSize * 2) * scaleX;
                        var sRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                        using (var penBorder = new Pen(Color.FromArgb(200, matColor.R, matColor.G, matColor.B), 1.2f))
                        {
                            penBorder.DashStyle = DashStyle.Dot;
                            g.DrawEllipse(penBorder, sRect);
                        }
                        using var penCross = new Pen(NeonTheme.NeonCyan, 1.0f);
                        g.DrawLine(penCross, cx - 4, cy, cx + 4, cy);
                        g.DrawLine(penCross, cx, cy - 4, cx, cy + 4);
                        break;
                    }

                    case ActiveTool.Eraser:
                    {
                        float r = _brushSize * scaleX;
                        var sRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                        using (var fillBrush = new SolidBrush(Color.FromArgb(60, 255, 50, 80)))
                        {
                            g.FillEllipse(fillBrush, sRect);
                        }
                        using (var penBorder = new Pen(NeonTheme.NeonRed, 1.2f))
                        {
                            penBorder.DashStyle = DashStyle.Dash;
                            g.DrawEllipse(penBorder, sRect);
                        }
                        break;
                    }

                    case ActiveTool.HeatGun:
                    {
                        float r = (_brushSize * 2) * scaleX;
                        var sRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                        using (var fillBrush = new SolidBrush(Color.FromArgb(50, 255, 140, 0)))
                        {
                            g.FillEllipse(fillBrush, sRect);
                        }
                        using (var penBorder = new Pen(NeonTheme.NeonOrange, 1.2f))
                        {
                            g.DrawEllipse(penBorder, sRect);
                        }
                        break;
                    }

                    case ActiveTool.ColdGun:
                    {
                        float r = (_brushSize * 2) * scaleX;
                        var sRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                        using (var fillBrush = new SolidBrush(Color.FromArgb(50, 40, 150, 255)))
                        {
                            g.FillEllipse(fillBrush, sRect);
                        }
                        using (var penBorder = new Pen(NeonTheme.NeonBlue, 1.2f))
                        {
                            g.DrawEllipse(penBorder, sRect);
                        }
                        break;
                    }

                    case ActiveTool.SparkIgniter:
                    {
                        float r = Math.Max(2, _brushSize) * scaleX;
                        var sRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                        using (var fillBrush = new SolidBrush(Color.FromArgb(60, 255, 230, 0)))
                        {
                            g.FillEllipse(fillBrush, sRect);
                        }
                        using (var penBorder = new Pen(NeonTheme.NeonYellow, 1.2f))
                        {
                            g.DrawEllipse(penBorder, sRect);
                        }
                        break;
                    }

                    case ActiveTool.Rectangle:
                    case ActiveTool.CircleShape:
                    case ActiveTool.Line:
                    case ActiveTool.FillBucket:
                    case ActiveTool.Pipette:
                    {
                        using var penCross = new Pen(NeonTheme.NeonCyan, 1.0f);
                        g.DrawLine(penCross, cx - 6, cy, cx + 6, cy);
                        g.DrawLine(penCross, cx, cy - 6, cx, cy + 6);
                        break;
                    }
                }
            }
        }

        private static void DrawDimensionTag(Graphics g, string text, float x, float y)
        {
            var font = NeonTheme.FontHeading;
            var size = g.MeasureString(text, font);
            var bgRect = new RectangleF(x, y, size.Width + 8, size.Height + 4);
            using (var bgBrush = new SolidBrush(Color.FromArgb(220, 10, 14, 22)))
            {
                g.FillRectangle(bgBrush, bgRect);
            }
            using (var borderPen = new Pen(NeonTheme.NeonCyan, 1.0f))
            {
                g.DrawRectangle(borderPen, bgRect.X, bgRect.Y, bgRect.Width, bgRect.Height);
            }
            using (var textBrush = new SolidBrush(NeonTheme.TextPrimary))
            {
                g.DrawString(text, font, textBrush, x + 4, y + 2);
            }
        }

        private void OpenYamlDialog()
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml|All Files (*.*)|*.*",
                Title = "Load Custom Materials YAML"
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    _registry.LoadFromFile(ofd.FileName);
                    PopulateCategories();
                    PopulateMaterialPalette();
                    InitHotbar();
                    _primaryMaterialIndex = _registry.Materials.Count > 1 ? _registry.Materials[1].Index : MaterialRegistry.EmptyIndex;
                    _secondaryMaterialIndex = _registry.Materials.Count > 2 ? _registry.Materials[2].Index : MaterialRegistry.EmptyIndex;
                    UpdateSelectedMaterialHUD();
                    MessageBox.Show(this, $"Successfully loaded {_registry.Count - 1} materials from YAML!", "YAML Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to load materials YAML:\n{ex.Message}", "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ReloadDefaultMaterials()
        {
            _registry = MaterialRegistry.CreateDefault();
            PopulateCategories();
            PopulateMaterialPalette();
            InitHotbar();
            _primaryMaterialIndex = _registry.GetIndex("sand");
            _secondaryMaterialIndex = _registry.GetIndex("water");
            UpdateSelectedMaterialHUD();
        }

        private void SaveSnapshotImage()
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|Bitmap (*.bmp)|*.bmp",
                Title = "Save Snapshot"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                _renderBitmap.Save(sfd.FileName);
            }
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            // If user is typing in search box, let normal typing happen unless Escape or Ctrl+F
            if (_searchBox.ContainsFocus && e.KeyCode != Keys.Escape)
            {
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                TogglePlayPause();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.OemPeriod)
            {
                StepSingleTick();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.C && !e.Control)
            {
                ClearGrid();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.X)
            {
                SwapMaterials();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                _searchBox.FocusInput();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _searchBox.SearchText = "";
                _canvas.Focus();
                e.Handled = true;
            }
            // Number keys 1-9 and 0 for Hotbar switching
            else if ((e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) || e.KeyCode == Keys.D0 ||
                     (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) || e.KeyCode == Keys.NumPad0)
            {
                int slotIndex = e.KeyCode switch
                {
                    Keys.D1 or Keys.NumPad1 => 1,
                    Keys.D2 or Keys.NumPad2 => 2,
                    Keys.D3 or Keys.NumPad3 => 3,
                    Keys.D4 or Keys.NumPad4 => 4,
                    Keys.D5 or Keys.NumPad5 => 5,
                    Keys.D6 or Keys.NumPad6 => 6,
                    Keys.D7 or Keys.NumPad7 => 7,
                    Keys.D8 or Keys.NumPad8 => 8,
                    Keys.D9 or Keys.NumPad9 => 9,
                    _ => 10
                };

                if (slotIndex <= _hotbarSlots.Count)
                {
                    var slot = _hotbarSlots[slotIndex - 1];
                    if (slot.MaterialIndex != MaterialRegistry.EmptyIndex)
                    {
                        if (e.Shift)
                        {
                            _secondaryMaterialIndex = slot.MaterialIndex;
                        }
                        else
                        {
                            _primaryMaterialIndex = slot.MaterialIndex;
                        }
                        UpdateSelectedMaterialHUD();
                        UpdateMaterialCardsSelection();
                        UpdateHotbarSelection();
                    }
                }
                e.Handled = true;
            }
            // Tool Shortcuts
            else if (e.KeyCode == Keys.B)
            {
                SelectTool(ActiveTool.CircleBrush);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Q)
            {
                SelectTool(ActiveTool.SquareBrush);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.S && !e.Control)
            {
                SelectTool(ActiveTool.SprayBrush);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.L && !e.Control)
            {
                SelectTool(ActiveTool.Line);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.R && !e.Control)
            {
                SelectTool(ActiveTool.Rectangle);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.O && !e.Control)
            {
                SelectTool(ActiveTool.CircleShape);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F && !e.Control)
            {
                SelectTool(ActiveTool.FillBucket);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.P && !e.Control)
            {
                SelectTool(ActiveTool.Pipette);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.E && !e.Control)
            {
                SelectTool(ActiveTool.Eraser);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.H)
            {
                SelectTool(ActiveTool.HeatGun);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.K)
            {
                SelectTool(ActiveTool.ColdGun);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.I)
            {
                SelectTool(ActiveTool.SparkIgniter);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.OemOpenBrackets) // '[' to decrease brush size
            {
                _brushSize = Math.Max(1, _brushSize - 1);
                _brushSizeTrackBar.Value = _brushSize;
                _brushSizeLabel.Text = $"BRUSH: {_brushSize}px";
                _canvas.Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.OemCloseBrackets) // ']' to increase brush size
            {
                _brushSize = Math.Min(30, _brushSize + 1);
                _brushSizeTrackBar.Value = _brushSize;
                _brushSizeLabel.Text = $"BRUSH: {_brushSize}px";
                _canvas.Invalidate();
                e.Handled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _simTimer?.Dispose();
                if (_pixelBufferHandle.IsAllocated)
                    _pixelBufferHandle.Free();
                _renderBitmap?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
