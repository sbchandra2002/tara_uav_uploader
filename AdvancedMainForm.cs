using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TARAFlasher
{
    // ── Double-buffered panel base used throughout ────────────────────────────
    class HudPanel : Panel
    {
        public HudPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    // ── Dark metal context-menu renderer ─────────────────────────────────────
    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        static readonly Color BgColor     = Color.FromArgb(20, 26, 36);
        static readonly Color HoverBg     = Color.FromArgb(32, 50, 72);
        static readonly Color TextColor   = Color.FromArgb(210, 228, 248);
        static readonly Color AccentColor = Color.FromArgb(32, 185, 255);
        static readonly Color SepColor    = Color.FromArgb(38, 58, 82);
        static readonly Color BorderColor = Color.FromArgb(48, 32, 185, 255);

        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground         => Color.FromArgb(20, 26, 36);
            public override Color MenuItemSelected                    => Color.FromArgb(32, 50, 72);
            public override Color MenuItemBorder                      => Color.FromArgb(48, 32, 185, 255);
            public override Color MenuBorder                          => Color.FromArgb(48, 32, 185, 255);
            public override Color MenuItemSelectedGradientBegin       => Color.FromArgb(32, 50, 72);
            public override Color MenuItemSelectedGradientEnd         => Color.FromArgb(32, 50, 72);
            public override Color MenuItemPressedGradientBegin        => Color.FromArgb(24, 38, 56);
            public override Color MenuItemPressedGradientEnd          => Color.FromArgb(24, 38, 56);
            public override Color SeparatorLight                      => Color.FromArgb(38, 58, 82);
            public override Color SeparatorDark                       => Color.FromArgb(38, 58, 82);
            public override Color ImageMarginGradientBegin            => Color.FromArgb(20, 26, 36);
            public override Color ImageMarginGradientMiddle           => Color.FromArgb(20, 26, 36);
            public override Color ImageMarginGradientEnd              => Color.FromArgb(20, 26, 36);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(BgColor))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var p = new Pen(BorderColor))
                e.Graphics.DrawRectangle(p, r);
            // top accent line
            using (var p = new Pen(AccentColor, 1.5f))
                e.Graphics.DrawLine(p, r.Left + 1, r.Top, r.Right - 1, r.Top);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (item.Selected && item.Enabled)
            {
                var rc = new Rectangle(2, 1, item.Width - 4, item.Height - 2);
                using (var b = new SolidBrush(HoverBg))
                    e.Graphics.FillRectangle(b, rc);
                using (var p = new Pen(Color.FromArgb(60, AccentColor)))
                    e.Graphics.DrawRectangle(p, rc);
            }
            else
            {
                using (var b = new SolidBrush(BgColor))
                    e.Graphics.FillRectangle(b, e.Item.ContentRectangle);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(SepColor))
                e.Graphics.DrawLine(p, 6, y, e.Item.Width - 6, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled
                ? Color.FromArgb(80, TextColor)
                : (e.Item.ForeColor.ToArgb() != SystemColors.ControlText.ToArgb()
                    ? e.Item.ForeColor
                    : TextColor);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(160, AccentColor);
            base.OnRenderArrow(e);
        }
    }

    public class AdvancedMainForm : Form
    {
        // ── Config / CLI ─────────────────────────────────────────────────────
        private string CLI_PATH;
        private AppConfig _config;
        private readonly string configFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TARA-Flasher", "config.json");
        private const string DefaultAddress           = "0x08000000";
        private const uint   FlashBaseAddress         = 0x08000000;
        private const long   DefaultDeviceFlashSizeBytes = 2L * 1024 * 1024;
        private const uint   ChunkSize                = 0x00020000;
        private const string PasswordFileName         = "password.dat";
        private const string DefaultPassword          = "12345";
        private const string DefaultAdminPassword     = "Tara@123";
        private static readonly string[] BootloaderNameTokens = { "bootloader", "boot" };
        private static readonly string[] MetadataNameTokens   = { "metadata", "meta" };
        private static readonly string[] FirmwareNameTokens   = { "firmware", "firm", "app", "main", "fw" };

        // ── Controls ─────────────────────────────────────────────────────────
        private DataGridView dgvFiles;
        private Button   btnUploadAll;
        private Button   btnMenu;
        private Button   btnConnect;
        private ContextMenuStrip menuStrip;
        private HudPanel pnlConnectionIndicator;   // animated LED + scan rings
        private Label    lblConnectionText;
        private HudPanel pnlFirmware;              // replaces GroupBox
        private TextBox  txtBootloaderPath;
        private TextBox  txtMetadataPath;
        private TextBox  txtFirmwarePath;
        private Button   btnBrowseBootloader;
        private Button   btnBrowseMetadata;
        private Button   btnBrowseFirmware;
        private TextBox  txtLog;
        private HudPanel pnlProgress;              // custom HUD progress bar
        private Label    lblProgressInfo;
        private HudPanel storagePanel;
        private StorageVisualizerControl storageVisualizer;
        private Label    storageLabel;
        private Label    storageInfo;
        private HudPanel pnlUploadAnim;            // upload-phase animation overlay (form-level)
        private HudPanel _pnlStats;
        // Layout containers (made fields so LayoutAllControls can reposition them)
        private HudPanel _pnlHeader;
        private HudPanel _pnlGridContainer;
        private HudPanel _pnlProgressContainer;
        private HudPanel _pnlLogContainer;

        // ── Theme colours ─────────────────────────────────────────────────────
        private bool isDarkTheme = true;
        private readonly Color darkBack   = Color.FromArgb(16, 20, 28);
        private readonly Color darkPanel  = Color.FromArgb(24, 30, 42);
        private readonly Color darkAccent = Color.FromArgb(32, 185, 255);
        private readonly Color darkGrid   = Color.FromArgb(32, 40, 54);
        private readonly Color darkText   = Color.FromArgb(210, 228, 248);
        private readonly Color darkSubtle = Color.FromArgb(80, 110, 145);
        private readonly Color lightBack   = Color.FromArgb(240, 244, 252);
        private readonly Color lightPanel  = Color.FromArgb(220, 230, 248);
        private readonly Color lightAccent = Color.FromArgb(0, 120, 215);
        private readonly Color lightGrid   = Color.FromArgb(200, 215, 235);
        private readonly Color lightText   = Color.FromArgb(25, 32, 48);
        private readonly Color lightSubtle = Color.FromArgb(90, 110, 140);

        // ── State ─────────────────────────────────────────────────────────────
        private long deviceFlashSize = DefaultDeviceFlashSizeBytes;
        private volatile bool passwordVerifiedThisSession;
        private SerialMonitorForm _serialMonitor;
        private bool     _isConnected    = false;   // user explicitly clicked Connect
        private bool     _devicePresent  = false;   // auto-detected by poll
        private volatile bool _flashInProgress = false;
        private volatile bool _pollRunning     = false;
        private System.Windows.Forms.Timer _pollTimer;

        // ── Animation state ────────────────────────────────────────────────
        private System.Windows.Forms.Timer _uiAnim;
        private float _scanAngle;          // scan rings rotation
        private bool  _isScanning;         // true while CLI -l is running
        private float _uploadR1, _uploadR2, _uploadR3;
        private float _pulsePhase;
        private bool  _isChipErase;
        private int   _progressMax = 1, _progressVal;
        private string _flashCurrentFile = "";
        // Byte-level progress tracking for real overall percentage
        private long _totalFlashBytes;
        private long _completedFlashBytes;
        private long _currentFileSize;
        private int  _completedFiles;
        private int  _totalFiles;
        private Image _headerLogo;

        // ── AppConfig ─────────────────────────────────────────────────────────
        class AppConfig
        {
            public string STM32CLIPath       { get; set; }
            public string FirmwareFolderPath { get; set; }
            public bool   DarkTheme          { get; set; } = true;
            public string AdminPasswordHash  { get; set; }
            public string AdminPasswordSalt  { get; set; }
        }

        // ── Config I/O ────────────────────────────────────────────────────────
        private AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(configFile))
                {
                    var json = File.ReadAllText(configFile);
                    return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch { }
            return new AppConfig();
        }

        private void SaveConfig(AppConfig config)
        {
            try
            {
                var dir = Path.GetDirectoryName(configFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFile, json);
            }
            catch (Exception ex) { AppendLog($"Config save error: {ex.Message}"); }
        }

        // ── CLI discovery ─────────────────────────────────────────────────────
        private string FindSTM32CLI()
        {
            const string exeName = "STM32_Programmer_CLI.exe";
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            }.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                var def = Path.Combine(root, @"STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin", exeName);
                if (File.Exists(def)) return def;
                try
                {
                    foreach (var d in Directory.GetDirectories(root, "STM32*", SearchOption.AllDirectories))
                    {
                        var c = Path.Combine(d, "bin", exeName);
                        if (File.Exists(c)) return c;
                    }
                }
                catch { }
            }
            return null;
        }

        // Looks for an STM32CubeProgrammer installer EXE in the same folder as this app
        private string FindSTM32SetupInAppDir()
        {
            try
            {
                string appDir = Application.StartupPath;
                foreach (var pat in new[] { "SetupSTM32CubeProgrammer*.exe", "STM32CubeProgrammer_*.exe", "STM32Cube*.exe" })
                {
                    var found = Directory.GetFiles(appDir, pat, SearchOption.TopDirectoryOnly)
                        .Where(f => !Path.GetFileName(f).Equals("TARA-Flasher.exe", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f)
                        .FirstOrDefault();
                    if (found != null) return found;
                }
            }
            catch { }
            return null;
        }

        // Full CLI setup flow: auto-scan → run bundled installer → re-scan
        private async Task EnsureCLIAsync()
        {
            AppendLog("STM32 CLI not configured. Scanning system...");
            CLI_PATH = FindSTM32CLI();
            if (!string.IsNullOrEmpty(CLI_PATH))
            {
                _config.STM32CLIPath = CLI_PATH;
                SaveConfig(_config);
                AppendLog($"STM32 CLI auto-detected: {CLI_PATH}");
                return;
            }

            AppendLog("CLI not found in standard locations. Checking app folder for installer...");
            string setupExe = FindSTM32SetupInAppDir();
            if (!string.IsNullOrEmpty(setupExe))
            {
                AppendLog($"Installer found: {Path.GetFileName(setupExe)}");
                string msg = "STM32CubeProgrammer is required but not installed.\n\n" +
                             $"Installer: {Path.GetFileName(setupExe)}\n\n" +
                             "Click Install to run it now. After installation completes,\n" +
                             "the app will automatically detect the CLI.";
                if (ShowNeonConfirmDialog("STM32CubeProgrammer Required", msg))
                {
                    AppendLog("Launching STM32CubeProgrammer installer — please complete the wizard...");
                    lblProgressInfo.Text = "Installing STM32CubeProgrammer...";
                    try
                    {
                        using (var proc = new Process())
                        {
                            proc.StartInfo = new ProcessStartInfo { FileName = setupExe, UseShellExecute = true };
                            proc.EnableRaisingEvents = true;
                            var tcs = new TaskCompletionSource<int>();
                            proc.Exited += (s2, e2) => { try { tcs.TrySetResult(proc.ExitCode); } catch { tcs.TrySetResult(-1); } };
                            proc.Start();
                            await tcs.Task;
                        }

                        AppendLog("Installer closed. Rescanning for CLI...");
                        lblProgressInfo.Text = "Detecting CLI...";
                        CLI_PATH = FindSTM32CLI();
                        if (!string.IsNullOrEmpty(CLI_PATH))
                        {
                            _config.STM32CLIPath = CLI_PATH;
                            SaveConfig(_config);
                            AppendLog($"STM32 CLI ready: {CLI_PATH}");
                            lblProgressInfo.Text = "Idle";
                            return;
                        }
                        AppendLog("CLI not found after install. Use Menu ▸ Set CLI Path.");
                    }
                    catch (Exception ex) { AppendLog($"Installer error: {ex.Message}"); }
                }
                else
                {
                    AppendLog("Installation skipped. Use Menu ▸ Set CLI Path when ready.");
                }
            }
            else
            {
                AppendLog("No STM32CubeProgrammer installer found in app folder.");
                AppendLog("Please install STM32CubeProgrammer manually, or use Menu ▸ Set CLI Path.");
            }
            lblProgressInfo.Text = "CLI Not Found";
        }

        // ── Admin password ────────────────────────────────────────────────────
        private void EnsureAdminPasswordInitialized(AppConfig config)
        {
            if (!string.IsNullOrEmpty(config.AdminPasswordHash) && !string.IsNullOrEmpty(config.AdminPasswordSalt))
                return;
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            var hash = ComputePasswordHash(salt, DefaultAdminPassword);
            config.AdminPasswordHash = Convert.ToBase64String(hash);
            config.AdminPasswordSalt = Convert.ToBase64String(salt);
            SaveConfig(config);
        }

        private bool VerifyAdminPassword(string input)
        {
            if (string.IsNullOrEmpty(_config.AdminPasswordHash) || string.IsNullOrEmpty(_config.AdminPasswordSalt))
                return string.Equals(input, DefaultAdminPassword, StringComparison.Ordinal);
            var salt     = Convert.FromBase64String(_config.AdminPasswordSalt);
            var expected = Convert.FromBase64String(_config.AdminPasswordHash);
            var actual   = ComputePasswordHash(salt, input);
            return FixedTimeEquals(expected, actual);
        }

        private void SetAdminPassword(string newPassword)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            var hash = ComputePasswordHash(salt, newPassword);
            _config.AdminPasswordHash = Convert.ToBase64String(hash);
            _config.AdminPasswordSalt = Convert.ToBase64String(salt);
            SaveConfig(_config);
        }

        // ── Constructor ───────────────────────────────────────────────────────
        public AdvancedMainForm()
        {
            _config = LoadConfig();
            isDarkTheme = _config.DarkTheme;

            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            SizeChanged += (s, e) => { LayoutAllControls(); Refresh(); };
            InitializeComponents();
            EnsurePasswordFileExists();
            EnsureAdminPasswordInitialized(_config);

            try { _headerLogo = Image.FromFile("logo.png"); } catch { }

            // Use saved path if still valid; full detection happens on Load
            if (!string.IsNullOrEmpty(_config.STM32CLIPath) && File.Exists(_config.STM32CLIPath))
                CLI_PATH = _config.STM32CLIPath;

            Load += async (s, e) =>
            {
                if (!string.IsNullOrEmpty(CLI_PATH))
                    AppendLog($"STM32 CLI ready: {Path.GetFileName(CLI_PATH)}");
                else
                    await EnsureCLIAsync();
            };

            InitializeFirmwareRows();
            UpdateStorageIndicator();

            // Connection poll timer
            _pollTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _pollTimer.Tick += OnConnectionPollTick;
            _pollTimer.Start();

            // Initial layout pass (also called on every SizeChanged)
            LayoutAllControls();

            // UI animation timer (30 fps)
            _uiAnim = new System.Windows.Forms.Timer { Interval = 33 };
            _uiAnim.Tick += OnUiAnimTick;
            _uiAnim.Start();
        }

        // ── InitializeComponents ─────────────────────────────────────────────
        private void InitializeComponents()
        {
            Text             = "TARA-Flasher";
            ClientSize       = new Size(1260, 760);
            Font             = new Font("Segoe UI", 9F);
            BackColor        = darkBack;
            Padding          = new Padding(0);
            FormBorderStyle  = FormBorderStyle.FixedSingle;
            MaximizeBox      = false;
            MinimizeBox      = true;
            try { Icon = new Icon("logo.ico"); } catch { }

            // ── Header bar ───────────────────────────────────────────────────
            _pnlHeader = new HudPanel
            {
                Location  = new Point(0, 0),
                Size      = new Size(1260, 68),
                BackColor = Color.FromArgb(20, 26, 36),
                Anchor    = AnchorStyles.None
            };
            _pnlHeader.Paint += PaintHeader;
            Controls.Add(_pnlHeader);

            // Logo circle in header
            var pnlLogo = new HudPanel
            {
                Location  = new Point(12, 10),
                Size      = new Size(48, 48),
                BackColor = Color.Transparent
            };
            pnlLogo.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_headerLogo != null)
                {
                    // Draw image without any background fill — fully transparent behind logo
                    g.DrawImage(_headerLogo, 0, 0, 48, 48);
                    // Thin accent ring overlay (no background fill underneath)
                    using (var p = new Pen(Color.FromArgb(80, 32, 185, 255), 1.5f))
                        g.DrawEllipse(p, 1, 1, 45, 45);
                }
            };
            _pnlHeader.Controls.Add(pnlLogo);

            var lblTitle = new Label
            {
                Name      = "lblTitle",
                Text      = "TARA-Flasher",
                Font      = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
                ForeColor = darkAccent,
                Location  = new Point(68, 8),
                AutoSize  = true
            };
            _pnlHeader.Controls.Add(lblTitle);

            var lblSub = new Label
            {
                Name      = "lblSub",
                Text      = "Firmware Flasher",
                Font      = new Font("Segoe UI", 8.5F),
                ForeColor = darkSubtle,
                Location  = new Point(72, 44),
                AutoSize  = true
            };
            _pnlHeader.Controls.Add(lblSub);

            // Menu button (right-anchored)
            btnMenu = new Button
            {
                Text      = "☰",
                Location  = new Point(1060, 16),
                Size      = new Size(36, 36),
                Anchor    = AnchorStyles.None,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 36, 48),
                ForeColor = darkAccent,
                Font      = new Font("Segoe UI", 11F)
            };
            btnMenu.FlatAppearance.BorderColor = Color.FromArgb(40, 32, 185, 255);
            btnMenu.Click += BtnMenu_Click;
            _pnlHeader.Controls.Add(btnMenu);

            // Unified connection pill: LED dot + "Connect" / "Connected"
            pnlConnectionIndicator = new HudPanel
            {
                Location  = new Point(1104, 14),
                Size      = new Size(148, 40),
                BackColor = Color.FromArgb(20, 26, 36),
                Anchor    = AnchorStyles.None,
                Cursor    = Cursors.Hand
            };
            pnlConnectionIndicator.Paint   += PaintConnectionPill;
            pnlConnectionIndicator.Click   += BtnConnect_Click;
            _pnlHeader.Controls.Add(pnlConnectionIndicator);

            // These are kept as fields for code references but NOT added to any panel
            btnConnect        = new Button { Visible = false };
            lblConnectionText = new Label  { Visible = false };

            InitializeMenu();

            // ── Firmware HUD panel (left, below header) ──────────────────────
            pnlFirmware = new HudPanel
            {
                Location  = new Point(20, 80),
                Size      = new Size(640, 120),
                BackColor = darkPanel,
                Anchor    = AnchorStyles.None
            };
            pnlFirmware.Paint += (s, e) =>
            {
                using (var bg = new SolidBrush(darkPanel))
                    e.Graphics.FillRectangle(bg, pnlFirmware.ClientRectangle);
                DrawHudFrame(e.Graphics, pnlFirmware.ClientRectangle, "FIRMWARE FILES", darkAccent);
            };
            Controls.Add(pnlFirmware);

            // Textboxes + browse buttons inside firmware panel
            txtBootloaderPath = MakeTextBox(100, 28, 420); pnlFirmware.Controls.Add(txtBootloaderPath);
            txtMetadataPath   = MakeTextBox(100, 56, 420); pnlFirmware.Controls.Add(txtMetadataPath);
            txtFirmwarePath   = MakeTextBox(100, 84, 420); pnlFirmware.Controls.Add(txtFirmwarePath);

            pnlFirmware.Controls.Add(MakeFieldLabel("Bootloader:", 10, 31));
            pnlFirmware.Controls.Add(MakeFieldLabel("Metadata:",   10, 59));
            pnlFirmware.Controls.Add(MakeFieldLabel("Firmware:",   10, 87));

            btnBrowseBootloader = MakeBrowseButton(530, 26); btnBrowseBootloader.Click += (s, e) => SelectSingleFirmwareFile(0); pnlFirmware.Controls.Add(btnBrowseBootloader);
            btnBrowseMetadata   = MakeBrowseButton(530, 54); btnBrowseMetadata.Click   += (s, e) => SelectSingleFirmwareFile(1); pnlFirmware.Controls.Add(btnBrowseMetadata);
            btnBrowseFirmware   = MakeBrowseButton(530, 82); btnBrowseFirmware.Click   += (s, e) => SelectSingleFirmwareFile(2); pnlFirmware.Controls.Add(btnBrowseFirmware);

            // ── File grid container (managed by LayoutAllControls) ────────────
            _pnlGridContainer = new HudPanel
            {
                Location  = new Point(18, 208),
                Size      = new Size(646, 238),
                BackColor = darkBack,
                Padding   = new Padding(2, 18, 2, 2),
                Anchor    = AnchorStyles.None
            };
            _pnlGridContainer.Paint += (s, e) =>
            {
                using (var bg = new SolidBrush(darkBack))
                    e.Graphics.FillRectangle(bg, _pnlGridContainer.ClientRectangle);
                DrawHudFrame(e.Graphics, _pnlGridContainer.ClientRectangle, "FILE MAP", darkAccent);
            };
            Controls.Add(_pnlGridContainer);

            dgvFiles = new DataGridView
            {
                Dock                   = DockStyle.Fill,
                AllowUserToAddRows     = false,
                ReadOnly               = true,
                RowHeadersVisible      = false,
                SelectionMode          = DataGridViewSelectionMode.FullRowSelect,
                BorderStyle            = BorderStyle.None,
                BackgroundColor        = darkBack,
                GridColor              = darkGrid,
                EnableHeadersVisualStyles = false,
                CellBorderStyle        = DataGridViewCellBorderStyle.SingleHorizontal
            };
            dgvFiles.ColumnHeadersDefaultCellStyle.BackColor  = darkPanel;
            dgvFiles.ColumnHeadersDefaultCellStyle.ForeColor  = darkAccent;
            dgvFiles.ColumnHeadersDefaultCellStyle.Font       = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            dgvFiles.ColumnHeadersHeight = 30;
            dgvFiles.RowsDefaultCellStyle.BackColor           = darkBack;
            dgvFiles.RowsDefaultCellStyle.ForeColor           = darkText;
            dgvFiles.RowsDefaultCellStyle.SelectionBackColor  = Color.FromArgb(30, 80, 120);
            dgvFiles.RowsDefaultCellStyle.SelectionForeColor  = darkAccent;
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "FileName",  HeaderText = "File Name",    ReadOnly = true });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address",   HeaderText = "Address",      Width = 120 });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size",      HeaderText = "Size (bytes)", ReadOnly = true, Width = 110 });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status",    HeaderText = "Status",       ReadOnly = true, Width = 110 });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColorTag",  HeaderText = "ColorTag",     ReadOnly = true, Visible = false });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "FullPath",  HeaderText = "FullPath",     ReadOnly = true, Visible = false });
            _pnlGridContainer.Controls.Add(dgvFiles);

            // Upload animation — form-level child overlaying the grid (avoids Dock=Fill conflict)
            pnlUploadAnim = new HudPanel { Location = new Point(18, 208), Size = new Size(646, 238), BackColor = darkBack, Visible = false, Anchor = AnchorStyles.None };
            pnlUploadAnim.Paint += PaintUploadAnimation;
            Controls.Add(pnlUploadAnim);
            pnlUploadAnim.BringToFront();

            // ── Upload button ─────────────────────────────────────────────────
            btnUploadAll = new Button
            {
                Text      = "UPLOAD",
                Location  = new Point(20, 452),
                Size      = new Size(130, 40),
                Anchor    = AnchorStyles.None,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(20, 50, 75),
                ForeColor = darkAccent,
                Font      = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Enabled   = false
            };
            btnUploadAll.FlatAppearance.BorderColor = darkAccent;
            btnUploadAll.FlatAppearance.BorderSize  = 1;
            btnUploadAll.Click += BtnUploadAll_Click;
            Controls.Add(btnUploadAll);

            // ── Progress area container (managed by LayoutAllControls) ───────
            _pnlProgressContainer = new HudPanel
            {
                Location  = new Point(678, 80),
                Size      = new Size(564, 120),
                BackColor = darkPanel,
                Anchor    = AnchorStyles.None
            };
            _pnlProgressContainer.Paint += (s, e) =>
            {
                using (var bg = new SolidBrush(darkPanel))
                    e.Graphics.FillRectangle(bg, _pnlProgressContainer.ClientRectangle);
                DrawHudFrame(e.Graphics, _pnlProgressContainer.ClientRectangle, "PROGRESS", darkAccent);
            };
            Controls.Add(_pnlProgressContainer);

            pnlProgress = new HudPanel
            {
                Location  = new Point(8, 34),
                Size      = new Size(548, 18),
                BackColor = darkPanel,
                Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            pnlProgress.Paint += PaintProgressBar;
            _pnlProgressContainer.Controls.Add(pnlProgress);

            lblProgressInfo = new Label
            {
                Text      = "Idle",
                Location  = new Point(8, 60),
                ForeColor = darkAccent,
                AutoSize  = true,
                Font      = new Font("Consolas", 8.5F)
            };
            _pnlProgressContainer.Controls.Add(lblProgressInfo);

            // ── Log container (managed by LayoutAllControls) ──────────────────
            _pnlLogContainer = new HudPanel
            {
                Location  = new Point(678, 208),
                Size      = new Size(564, 288),
                BackColor = Color.FromArgb(10, 14, 20),
                Padding   = new Padding(2, 18, 2, 2),
                Anchor    = AnchorStyles.None
            };
            _pnlLogContainer.Paint += (s, e) =>
            {
                using (var bg = new SolidBrush(Color.FromArgb(10, 14, 20)))
                    e.Graphics.FillRectangle(bg, _pnlLogContainer.ClientRectangle);
                DrawHudFrame(e.Graphics, _pnlLogContainer.ClientRectangle, "SYSTEM LOG", darkAccent);
            };
            Controls.Add(_pnlLogContainer);

            txtLog = new TextBox
            {
                Dock        = DockStyle.Fill,
                Multiline   = true,
                ReadOnly    = true,
                ScrollBars  = ScrollBars.Vertical,
                BackColor   = Color.FromArgb(10, 14, 20),
                ForeColor   = Color.FromArgb(180, 215, 240),
                BorderStyle = BorderStyle.None,
                Font        = new Font("Consolas", 8.5F)
            };
            _pnlLogContainer.Controls.Add(txtLog);

            // ── Storage HUD panel ─────────────────────────────────────────────
            storagePanel = new HudPanel
            {
                Location  = new Point(20, 504),
                Size      = new Size(640, 228),
                BackColor = darkPanel,
                Anchor    = AnchorStyles.None
            };
            storagePanel.Paint += (s, e) =>
            {
                using (var bg = new SolidBrush(darkPanel))
                    e.Graphics.FillRectangle(bg, storagePanel.ClientRectangle);
                DrawHudFrame(e.Graphics, storagePanel.ClientRectangle, "STORAGE MAP", darkAccent);
            };
            Controls.Add(storagePanel);

            storageLabel = new Label
            {
                Text      = "",
                Location  = new Point(10, 22),
                AutoSize  = true,
                ForeColor = darkText,
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            storagePanel.Controls.Add(storageLabel);

            storageVisualizer = new StorageVisualizerControl
            {
                Location        = new Point(12, 38),
                Size            = new Size(616, 100),
                BackColor       = Color.FromArgb(12, 16, 24),
                ThemeIsDark     = true,
                DarkThemeColors = new[] { Color.FromArgb(12, 16, 24), darkText },
                LightThemeColors = new[] { Color.FromArgb(245, 248, 255), lightText }
            };
            storagePanel.Controls.Add(storageVisualizer);

            storageInfo = new Label
            {
                Location  = new Point(12, 148),
                AutoSize  = false,
                Size      = new Size(616, 64),
                ForeColor = darkSubtle,
                Font      = new Font("Consolas", 8F),
                Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            storagePanel.Controls.Add(storageInfo);

            // ── Stats panel (bottom-right) ────────────────────────────────────
            _pnlStats = new HudPanel
            {
                Location  = new Point(680, 504),
                Size      = new Size(560, 228),
                BackColor = darkPanel,
                Anchor    = AnchorStyles.None
            };
            _pnlStats.Paint += PaintStatsPanel;
            Controls.Add(_pnlStats);
        }

        // ── LayoutAllControls — called on startup and every SizeChanged ──────
        private void LayoutAllControls()
        {
            if (_pnlHeader == null || _pnlGridContainer == null) return;
            if (WindowState == FormWindowState.Minimized) return;

            SuspendLayout();
            int fw = ClientSize.Width;
            int fh = ClientSize.Height;

            const int headerH  = 68;
            const int topY     = 80;
            const int firmH    = 120;
            const int uploadH  = 40;
            const int storH    = 228;
            const int statsH   = 228;
            const int gap      = 8;
            const int lm       = 18;   // left/right margins
            const int rm       = 18;

            // Header: full width, fixed height
            _pnlHeader.Bounds = new Rectangle(0, 0, fw, headerH);
            if (btnMenu != null)
                btnMenu.Location = new Point(fw - rm - 148 - gap - 36, 16);
            if (pnlConnectionIndicator != null)
                pnlConnectionIndicator.Location = new Point(fw - rm - 148, 14);

            // Column split — left column ≈ 52 % of usable width (preserves original ratio)
            int usable = fw - lm - rm - gap;
            int leftW  = (int)(usable * 0.526);
            int rightX = lm + leftW + gap;
            int rightW = fw - rightX - rm;

            // Vertical positions
            int gridY   = topY + firmH + gap;
            int botY    = fh - rm - storH;          // storage / stats top
            int btnY    = botY - gap - uploadH;
            int gridH   = Math.Max(40, btnY - gap - gridY);
            int logH    = Math.Max(40, botY - gap - gridY);

            // ── Left column ────────────────────────────────────────────────────
            pnlFirmware.Bounds       = new Rectangle(lm, topY, leftW, firmH);
            _pnlGridContainer.Bounds = new Rectangle(lm, gridY, leftW, gridH);
            btnUploadAll.Bounds      = new Rectangle(lm, btnY, 130, uploadH);
            storagePanel.Bounds      = new Rectangle(lm, botY, leftW, storH);

            // Stretch firmware textboxes and browse buttons within pnlFirmware
            int tbW  = leftW - 100 - 90 - 10;  // label(90) + gap + browse(80) + margins
            int brX  = 100 + tbW + 8;
            if (txtBootloaderPath != null) txtBootloaderPath.SetBounds(100, 28, tbW, 22);
            if (txtMetadataPath   != null) txtMetadataPath  .SetBounds(100, 56, tbW, 22);
            if (txtFirmwarePath   != null) txtFirmwarePath  .SetBounds(100, 84, tbW, 22);
            if (btnBrowseBootloader != null) btnBrowseBootloader.Location = new Point(brX, 26);
            if (btnBrowseMetadata   != null) btnBrowseMetadata  .Location = new Point(brX, 54);
            if (btnBrowseFirmware   != null) btnBrowseFirmware  .Location = new Point(brX, 82);

            // Stretch storage visualizer within storagePanel
            if (storageVisualizer != null)
                storageVisualizer.Bounds = new Rectangle(12, 38, leftW - 24, 100);
            if (storageInfo != null)
                storageInfo.Bounds = new Rectangle(12, 148, leftW - 24, 64);

            // ── Right column ───────────────────────────────────────────────────
            _pnlProgressContainer.Bounds = new Rectangle(rightX, topY, rightW, firmH);
            _pnlLogContainer.Bounds      = new Rectangle(rightX, gridY, rightW, logH);
            _pnlStats.Bounds             = new Rectangle(rightX, botY, rightW, statsH);

            // Progress bar inside progress container
            if (pnlProgress != null)
                pnlProgress.Bounds = new Rectangle(8, 34, rightW - 16, 18);

            // Upload animation overlay: same position/size as grid container
            if (pnlUploadAnim != null)
                pnlUploadAnim.Bounds = _pnlGridContainer.Bounds;

            ResumeLayout(true);
        }

        // ── Control factories ─────────────────────────────────────────────────
        private TextBox MakeTextBox(int x, int y, int width) => new TextBox
        {
            Location    = new Point(x, y),
            Size        = new Size(width, 22),
            ReadOnly    = true,
            BackColor   = Color.FromArgb(14, 18, 26),
            ForeColor   = darkText,
            BorderStyle = BorderStyle.FixedSingle,
            Font        = new Font("Segoe UI", 8.5F)
        };

        private Label MakeFieldLabel(string text, int x, int y) => new Label
        {
            Text      = text,
            Location  = new Point(x, y),
            AutoSize  = true,
            ForeColor = darkSubtle,
            Font      = new Font("Segoe UI", 8.5F)
        };

        private Button MakeBrowseButton(int x, int y) => new Button
        {
            Text      = "Browse",
            Location  = new Point(x, y),
            Size      = new Size(80, 22),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 30, 44),
            ForeColor = darkAccent,
            Font      = new Font("Segoe UI", 8F)
        };

        // ── HUD frame painter (corner brackets + glow border + title) ─────────
        private void DrawHudFrame(Graphics g, Rectangle r, string title, Color accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int bx = r.X + 1, by = r.Y + 1, bw = r.Width - 2, bh = r.Height - 2;
            int c = 14; // corner length

            // Main border (very faint)
            using (var borderPen = new Pen(Color.FromArgb(35, accent), 1f))
                g.DrawRectangle(borderPen, bx, by, bw, bh);

            // Corner brackets
            using (var cp = new Pen(Color.FromArgb(160, accent), 1.8f))
            {
                // TL
                g.DrawLine(cp, bx, by, bx + c, by);
                g.DrawLine(cp, bx, by, bx, by + c);
                // TR
                g.DrawLine(cp, bx + bw, by, bx + bw - c, by);
                g.DrawLine(cp, bx + bw, by, bx + bw, by + c);
                // BL
                g.DrawLine(cp, bx, by + bh, bx + c, by + bh);
                g.DrawLine(cp, bx, by + bh, bx, by + bh - c);
                // BR
                g.DrawLine(cp, bx + bw, by + bh, bx + bw - c, by + bh);
                g.DrawLine(cp, bx + bw, by + bh, bx + bw, by + bh - c);
            }

            // Title badge — drawn fully INSIDE the top border so it is never clipped
            if (!string.IsNullOrEmpty(title))
            {
                using (var tf = new Font("Consolas", 7.5F, FontStyle.Bold))
                {
                    var sz = g.MeasureString(title, tf);
                    float tx = bx + 18;
                    float ty = by + 3;          // 3 px below the top border — always within clip rect

                    // Erase the border line behind the badge
                    // (we can only erase what is already painted — use a transparent-ish rect
                    //  over the faint border to create the "gap" effect)
                    using (var bb = new Pen(Color.FromArgb(35, accent), 3f))
                        g.DrawLine(bb, tx - 2, by, tx + sz.Width + 2, by);

                    using (var tb = new SolidBrush(Color.FromArgb(200, accent)))
                        g.DrawString(title, tf, tb, tx, ty);
                }
            }
        }

        // ── Header paint ──────────────────────────────────────────────────────
        private void PaintHeader(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = ((Control)sender).Width, h = ((Control)sender).Height;

            // Background gradient
            using (var bg = new LinearGradientBrush(new Point(0, 0), new Point(0, h),
                Color.FromArgb(24, 30, 42), Color.FromArgb(18, 24, 34)))
                g.FillRectangle(bg, 0, 0, w, h);

            // Bottom separator glow
            using (var lb = new LinearGradientBrush(new Point(0, h - 2), new Point(w, h - 2),
                Color.Transparent, Color.Transparent))
            {
                var cb = new ColorBlend(3)
                {
                    Colors    = new[] { Color.Transparent, Color.FromArgb(100, 32, 185, 255), Color.Transparent },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                lb.InterpolationColors = cb;
                g.FillRectangle(lb, 0, h - 2, w, 2);
            }
        }

        // ── Connection pill paint (LED + label in one element) ───────────────
        private void PaintConnectionPill(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = pnlConnectionIndicator.Width, h = pnlConnectionIndicator.Height;

            // Pill background
            Color borderCol, bgTint;
            string label;
            if (_isScanning)
            {
                borderCol = Color.FromArgb(140, 255, 185, 0);
                bgTint    = Color.FromArgb(18, 255, 185, 0);
                label     = "Scanning...";
            }
            else if (_isConnected)
            {
                borderCol = Color.FromArgb(160, 0, 210, 100);
                bgTint    = Color.FromArgb(18, 0, 210, 100);
                label     = "Connected";
            }
            else if (_devicePresent)
            {
                // Device detected by poll — ready to connect (green, not yet "Connected")
                borderCol = Color.FromArgb(130, 0, 200, 90);
                bgTint    = Color.FromArgb(14, 0, 200, 90);
                label     = "Connect";
            }
            else
            {
                borderCol = Color.FromArgb(140, 210, 50, 50);
                bgTint    = Color.FromArgb(18, 210, 50, 50);
                label     = "Connect";
            }

            using (var bg = new SolidBrush(Color.FromArgb(20, 26, 36)))
                g.FillRectangle(bg, 0, 0, w, h);
            using (var tint = new SolidBrush(bgTint))
                g.FillRectangle(tint, 0, 0, w, h);
            using (var bp = new Pen(borderCol, 1.5f))
                g.DrawRectangle(bp, 1, 1, w - 3, h - 3);

            // LED dot (left side)
            int lx = 14, ly = h / 2;
            if (_isScanning)
            {
                // Rotating amber arc
                int r = 9;
                using (var p = new Pen(Color.FromArgb(200, 255, 185, 0), 2f))
                    g.DrawArc(p, lx - r, ly - r, r * 2, r * 2, _scanAngle, 240f);
                using (var p = new Pen(Color.FromArgb(80, 255, 185, 0), 1.2f))
                    g.DrawArc(p, lx - r - 4, ly - r - 4, (r + 4) * 2, (r + 4) * 2, -_scanAngle * 0.7f, 180f);
                using (var b = new SolidBrush(Color.FromArgb(255, 185, 0)))
                    g.FillEllipse(b, lx - 3, ly - 3, 6, 6);
            }
            else
            {
                bool greenLed = _isConnected || _devicePresent;
                Color led   = greenLed ? Color.FromArgb(0, 230, 110) : Color.FromArgb(220, 55, 55);
                float pulse = greenLed ? (float)(8 + 3 * Math.Sin(_pulsePhase)) : 7f;
                using (var gp = new GraphicsPath())
                {
                    int gr = (int)pulse;
                    gp.AddEllipse(lx - gr, ly - gr, gr * 2, gr * 2);
                    using (var pgb = new PathGradientBrush(gp))
                    {
                        pgb.CenterColor    = Color.FromArgb(greenLed ? 65 : 38, led);
                        pgb.SurroundColors = new[] { Color.Transparent };
                        g.FillPath(pgb, gp);
                    }
                }
                using (var fb = new SolidBrush(led))
                    g.FillEllipse(fb, lx - 5, ly - 5, 10, 10);
                using (var hl = new SolidBrush(Color.FromArgb(100, 255, 255, 255)))
                    g.FillEllipse(hl, lx - 3, ly - 6, 4, 3);
            }

            // Label text
            Color textCol = _isScanning    ? Color.FromArgb(255, 200, 60)
                          : _isConnected   ? Color.FromArgb(0, 220, 110)
                          : _devicePresent ? Color.FromArgb(0, 200, 90)
                          :                  Color.FromArgb(220, 80, 80);
            using (var tf = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold))
            using (var tb = new SolidBrush(textCol))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                g.DrawString(label, tf, tb, new RectangleF(30, 0, w - 34, h), sf);
        }

        // ── Progress bar paint ────────────────────────────────────────────────
        private void PaintProgressBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = pnlProgress.Width, h = pnlProgress.Height;

            // Full-height track — no inset, no border box
            using (var track = new SolidBrush(Color.FromArgb(18, 40, 60)))
                g.FillRectangle(track, 0, 0, w, h);

            float ratio = _progressMax > 0 ? (float)_progressVal / _progressMax : 0f;
            int fw = (int)(w * ratio);
            if (fw > 1)
            {
                using (var fill = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, fw), h),
                    Color.FromArgb(0, 200, 255), darkAccent, LinearGradientMode.Horizontal))
                    g.FillRectangle(fill, 0, 0, fw, h);

                int tw = Math.Min(30, fw);
                using (var tip = new LinearGradientBrush(new Rectangle(fw - tw, 0, tw + 2, h),
                    Color.FromArgb(200, 255, 255, 255), Color.Transparent, LinearGradientMode.Horizontal))
                    g.FillRectangle(tip, fw - tw, 0, tw + 2, h);
            }
        }

        // ── Upload animation — tech blue / reddish-orange ────────────────────
        private void PaintUploadAnimation(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = pnlUploadAnim.Width, h = pnlUploadAnim.Height;
            int cx = w / 2, cy = h / 2;

            // ── Pick palette based on operation ──────────────────────────────
            bool   erase      = _isChipErase;
            Color  c1         = erase ? Color.FromArgb(220, 35, 20)   : Color.FromArgb(32, 185, 255);   // ring 1
            Color  c2         = erase ? Color.FromArgb(255, 110, 30)  : Color.FromArgb(255, 75, 28);    // ring 2
            Color  c3         = erase ? Color.FromArgb(200, 40, 25)   : Color.FromArgb(32, 185, 255);   // ring 3 body
            Color  c3tip      = erase ? Color.FromArgb(255, 140, 20)  : Color.FromArgb(255, 85, 35);    // ring 3 tip
            Color  coreRim    = erase ? Color.FromArgb(180, 30, 15)   : Color.FromArgb(32, 100, 160);
            Color  accentText = erase ? Color.FromArgb(255, 90, 60)   : darkAccent;
            string frameTitle = erase ? "ERASING" : "FLASHING";

            // Dark background
            using (var bg = new SolidBrush(darkBack))
                g.FillRectangle(bg, 0, 0, w, h);
            DrawHudFrame(g, new Rectangle(0, 0, w, h), frameTitle, accentText);

            // ── Ring 1 — 4 × 65° segments (CW, fast) ────────────────────────
            {
                int r = 52;
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                using (var bp = new Pen(Color.FromArgb(22, c1.R, c1.G, c1.B), 0.8f))
                    g.DrawEllipse(bp, rect);
                for (int i = 0; i < 4; i++)
                {
                    float startA = _uploadR1 + i * 90f;
                    using (var pg = new Pen(Color.FromArgb(42, c1.R, c1.G, c1.B), 7f))
                        g.DrawArc(pg, rect, startA, 65f);
                    using (var pm = new Pen(Color.FromArgb(225, c1.R, c1.G, c1.B), 2.5f))
                        g.DrawArc(pm, rect, startA, 65f);
                    var ri = new RectangleF(cx - r + 2, cy - r + 2, (r - 2) * 2, (r - 2) * 2);
                    using (var ph = new Pen(Color.FromArgb(70, c1.R, c1.G, c1.B), 1f))
                        g.DrawArc(ph, ri, startA + 5f, 55f);
                }
            }

            // ── Ring 2 — 3 × 80° segments (CCW, medium) ─────────────────────
            {
                int r = 74;
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                using (var bp = new Pen(Color.FromArgb(22, c2.R, c2.G, c2.B), 0.8f))
                    g.DrawEllipse(bp, rect);
                for (int i = 0; i < 3; i++)
                {
                    float startA = _uploadR2 + i * 120f;
                    using (var pg = new Pen(Color.FromArgb(42, c2.R, c2.G, c2.B), 7f))
                        g.DrawArc(pg, rect, startA, 80f);
                    using (var pm = new Pen(Color.FromArgb(220, c2.R, c2.G, c2.B), 2.5f))
                        g.DrawArc(pm, rect, startA, 80f);
                    var ri = new RectangleF(cx - r + 2, cy - r + 2, (r - 2) * 2, (r - 2) * 2);
                    using (var ph = new Pen(Color.FromArgb(65, c2.R, c2.G, c2.B), 1f))
                        g.DrawArc(ph, ri, startA + 5f, 70f);
                }
            }

            // ── Ring 3 — sweep + hot leading tip (CW, slow) ──────────────────
            {
                int r = 96;
                var rect    = new RectangleF(cx - r,     cy - r,     r * 2,       r * 2);
                var rectOut = new RectangleF(cx - r - 3, cy - r - 3, (r + 3) * 2, (r + 3) * 2);
                using (var bp = new Pen(Color.FromArgb(18, coreRim.R, coreRim.G, coreRim.B), 1.2f))
                    g.DrawEllipse(bp, rect);
                using (var pt = new Pen(Color.FromArgb(38, c3.R, c3.G, c3.B), 1.8f))
                    g.DrawArc(pt, rect, _uploadR3 + 95f, 60f);
                using (var ps = new Pen(Color.FromArgb(220, c3.R, c3.G, c3.B), 3f))
                    g.DrawArc(ps, rect, _uploadR3, 95f);
                using (var ptip = new Pen(Color.FromArgb(230, c3tip.R, c3tip.G, c3tip.B), 3.5f))
                    g.DrawArc(ptip, rect, _uploadR3, 10f);
                using (var pg = new Pen(Color.FromArgb(38, c3.R, c3.G, c3.B), 6f))
                    g.DrawArc(pg, rectOut, _uploadR3, 95f);
                for (int i = 0; i < 4; i++)
                {
                    double rad = (i * 90.0) * Math.PI / 180.0;
                    using (var tp = new Pen(Color.FromArgb(55, coreRim.R, coreRim.G, coreRim.B), 1.2f))
                        g.DrawLine(tp,
                            cx + (float)((r - 6) * Math.Cos(rad)),
                            cy + (float)((r - 6) * Math.Sin(rad)),
                            cx + (float)((r + 6) * Math.Cos(rad)),
                            cy + (float)((r + 6) * Math.Sin(rad)));
                }
            }

            // ── Centre core disc ──────────────────────────────────────────────
            {
                int cr = 42;
                using (var gp2 = new GraphicsPath())
                {
                    gp2.AddEllipse(cx - cr, cy - cr, cr * 2, cr * 2);
                    using (var pgb = new PathGradientBrush(gp2))
                    {
                        pgb.CenterColor    = Color.FromArgb(50, coreRim.R, coreRim.G, coreRim.B);
                        pgb.SurroundColors = new[] { Color.Transparent };
                        g.FillPath(pgb, gp2);
                    }
                }
                using (var bp = new Pen(Color.FromArgb(45, c1.R, c1.G, c1.B), 1f))
                    g.DrawEllipse(bp, cx - cr, cy - cr, cr * 2, cr * 2);
            }

            // ── Centre text ───────────────────────────────────────────────────
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                if (erase)
                {
                    // Chip erase: no percentage — just a pulsing "ERASING" label
                    int dots = ((int)(_uploadR1 / 45f) % 4);   // 0-3 cycling dots
                    string eraseLabel = "ERASING" + new string('.', dots);
                    using (var pf = new Font("Consolas", 13F, FontStyle.Bold))
                    using (var pb = new SolidBrush(accentText))
                        g.DrawString(eraseLabel, pf, pb, cx, cy, sf);
                }
                else
                {
                    float ratio = _progressMax > 0 ? (float)_progressVal / _progressMax : 0f;
                    string fileText = _flashCurrentFile.Length > 22
                        ? _flashCurrentFile.Substring(0, 20) + "…"
                        : _flashCurrentFile;
                    using (var pf = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                    using (var pb = new SolidBrush(Color.FromArgb(190, 215, 240)))
                        g.DrawString(fileText, pf, pb, cx, cy - 14, sf);

                    using (var pf = new Font("Consolas", 16F, FontStyle.Bold))
                    using (var pb = new SolidBrush(accentText))
                        g.DrawString($"{(int)(ratio * 100)}%", pf, pb, cx, cy + 8, sf);

                    using (var pf = new Font("Segoe UI", 7.5F))
                    using (var pb = new SolidBrush(darkSubtle))
                    using (var sf2 = new StringFormat { Alignment = StringAlignment.Center })
                        g.DrawString($"file {_completedFiles + 1} of {_totalFiles}", pf, pb, cx, cy + 30, sf2);
                }
            }
        }

        // ── Stats panel paint (bottom-right) ──────────────────────────────────
        private void PaintStatsPanel(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = ((Control)sender).ClientRectangle;

            using (var bg = new SolidBrush(darkPanel))
                g.FillRectangle(bg, r);
            DrawHudFrame(g, r, "DEVICE INFO", darkAccent);

            var lines = new[]
            {
                ("Flash Base",  $"0x{FlashBaseAddress:X8}"),
                ("Flash Size",  $"{DefaultDeviceFlashSizeBytes / 1024 / 1024} MB"),
                ("Chunk Size",  $"0x{ChunkSize:X5}"),
                ("Connection",  _isConnected ? "ST-LINK / SWD" : (_devicePresent ? "Device Found" : "—")),
                ("Status",      _flashInProgress ? "FLASHING" : (_isConnected ? "IDLE" : (_devicePresent ? "READY" : "OFFLINE"))),
            };

            using (var kf = new Font("Consolas", 8F))
            using (var vf = new Font("Consolas", 8F, FontStyle.Bold))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    int y = 26 + i * 28;
                    using (var kb = new SolidBrush(darkSubtle))
                        g.DrawString(lines[i].Item1, kf, kb, 16, y);
                    Color vc = lines[i].Item1 == "Status" && _flashInProgress
                        ? Color.FromArgb(255, 185, 0)
                        : (lines[i].Item1 == "Connection" && (_isConnected || _devicePresent))
                            ? (_isConnected ? Color.FromArgb(0, 220, 110) : Color.FromArgb(0, 180, 90))
                            : darkText;
                    using (var vb = new SolidBrush(vc))
                        g.DrawString(lines[i].Item2, vf, vb, 130, y);
                    // Separator line
                    using (var sp = new Pen(Color.FromArgb(20, 32, 185, 255), 1))
                        g.DrawLine(sp, 14, y + 18, r.Width - 14, y + 18);
                }
            }
        }

        // ── UI Animation tick ─────────────────────────────────────────────────
        private void OnUiAnimTick(object sender, EventArgs e)
        {
            _scanAngle  = (_scanAngle  + 3.5f) % 360f;
            _pulsePhase = (_pulsePhase + 0.12f) % ((float)Math.PI * 2);

            if (_flashInProgress)
            {
                _uploadR1 = (_uploadR1 + 1.5f) % 360f;
                _uploadR2 = (_uploadR2 - 0.75f + 360f) % 360f;
                _uploadR3 = (_uploadR3 + 0.45f) % 360f;
                pnlUploadAnim.Invalidate();
                pnlProgress.Invalidate();
            }

            // Repaint indicator + the parent header region around it to prevent ghost artifacts
            pnlConnectionIndicator.Invalidate();
            pnlConnectionIndicator.Parent?.Invalidate(
                new Rectangle(pnlConnectionIndicator.Left - 2, pnlConnectionIndicator.Top - 2,
                              pnlConnectionIndicator.Width + 4, pnlConnectionIndicator.Height + 4), false);

            if (_flashInProgress || _isConnected || _devicePresent)
                _pnlStats?.Invalidate();
        }

        // ── ApplyThemeDefaults ────────────────────────────────────────────────
        private void ApplyThemeDefaults()
        {
            var back   = isDarkTheme ? darkBack   : lightBack;
            var panel  = isDarkTheme ? darkPanel  : lightPanel;
            var accent = isDarkTheme ? darkAccent : lightAccent;
            var text   = isDarkTheme ? darkText   : lightText;
            var subtle = isDarkTheme ? darkSubtle : lightSubtle;

            BackColor = back;
            dgvFiles.BackgroundColor            = back;
            dgvFiles.ColumnHeadersDefaultCellStyle.BackColor = panel;
            dgvFiles.ColumnHeadersDefaultCellStyle.ForeColor = accent;
            dgvFiles.RowsDefaultCellStyle.BackColor          = back;
            dgvFiles.RowsDefaultCellStyle.ForeColor          = text;
            dgvFiles.GridColor = isDarkTheme ? darkGrid : lightGrid;
            txtLog.BackColor   = isDarkTheme ? Color.FromArgb(10, 14, 20) : Color.White;
            txtLog.ForeColor   = isDarkTheme ? Color.FromArgb(180, 215, 240) : lightText;
            lblProgressInfo.ForeColor = accent;
            pnlConnectionIndicator.Invalidate();

            foreach (Control c in pnlFirmware.Controls)
            {
                if (c is TextBox tb) { tb.BackColor = isDarkTheme ? Color.FromArgb(14, 18, 26) : Color.White; tb.ForeColor = text; }
                if (c is Label  lb) lb.ForeColor = subtle;
                if (c is Button btn) { btn.BackColor = panel; btn.ForeColor = accent; }
            }

            storagePanel.BackColor = panel;
            storageInfo.ForeColor  = subtle;
            storageVisualizer.ThemeIsDark = isDarkTheme;
            storageVisualizer.BackColor   = isDarkTheme ? Color.FromArgb(12, 16, 24) : Color.FromArgb(240, 244, 255);

            Invalidate(true);
        }

        // ── Menu ──────────────────────────────────────────────────────────────
        private void InitializeMenu()
        {
            menuStrip = new ContextMenuStrip
            {
                Renderer    = new DarkMenuRenderer(),
                BackColor   = Color.FromArgb(20, 26, 36),
                ForeColor   = Color.FromArgb(210, 228, 248),
                Font        = new Font("Segoe UI", 9F),
                ShowImageMargin = false,
                Padding     = new Padding(0, 3, 0, 3)
            };

            ToolStripMenuItem MakeItem(string text, EventHandler handler, Color? fore = null)
            {
                var item = new ToolStripMenuItem(text)
                {
                    BackColor = Color.FromArgb(20, 26, 36),
                    ForeColor = fore ?? Color.FromArgb(210, 228, 248),
                    Font      = new Font("Segoe UI", 9F),
                    Padding   = new Padding(10, 4, 10, 4)
                };
                item.Click += handler;
                return item;
            }

            var toggleThemeItem        = MakeItem("Toggle Theme",            BtnToggleTheme_Click);
            var changePasswordItem     = MakeItem("Change Upload Password",  BtnChangeUploadPassword_Click);
            var changeAdminPasswordItem= MakeItem("Change Admin Password",   BtnChangeAdminPassword_Click);
            var setCliPathItem         = MakeItem("Set CLI Path",            BtnSetCliPath_Click);
            var serialMonitorItem      = MakeItem("Serial Monitor",          (s, ev) => OpenSerialMonitor());
            var fullChipEraseItem      = MakeItem("Full Chip Erase...",      BtnFullChipErase_Click, Color.FromArgb(255, 90, 70));

            ToolStripSeparator MakeSep()
            {
                var sep = new ToolStripSeparator();
                sep.Paint += (s, ev) =>
                {
                    int y = sep.Height / 2;
                    using (var p = new Pen(Color.FromArgb(38, 58, 82)))
                        ev.Graphics.DrawLine(p, 6, y, sep.Width - 6, y);
                };
                return sep;
            }

            menuStrip.Items.Add(toggleThemeItem);
            menuStrip.Items.Add(MakeSep());
            menuStrip.Items.Add(serialMonitorItem);
            menuStrip.Items.Add(MakeSep());
            menuStrip.Items.Add(changePasswordItem);
            menuStrip.Items.Add(changeAdminPasswordItem);
            menuStrip.Items.Add(MakeSep());
            menuStrip.Items.Add(setCliPathItem);
            menuStrip.Items.Add(MakeSep());
            menuStrip.Items.Add(fullChipEraseItem);
        }

        private void BtnMenu_Click(object sender, EventArgs e)
        {
            if (menuStrip == null) InitializeMenu();
            menuStrip.Show(btnMenu, new Point(0, btnMenu.Height));
        }

        // ── Connect / poll ────────────────────────────────────────────────────
        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            await DetectStLinkAsync();
        }

        private async Task DetectStLinkAsync()
        {
            _isScanning = true;
            SetConnectionState(false, "Scanning...");
            AppendLog("Scanning for ST-LINK...");

            if (string.IsNullOrEmpty(CLI_PATH) || !File.Exists(CLI_PATH))
            {
                _isScanning = false;
                SetConnectionState(false, "CLI Not Found");
                AppendLog($"CLI not found at {CLI_PATH}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName               = CLI_PATH,
                Arguments              = "-l",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            try
            {
                using (var proc = Process.Start(psi))
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    var err    = await proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit();
                    AppendCliOutput(output);
                    if (!string.IsNullOrEmpty(err)) AppendCliOutput("[ERR] " + err);

                    bool connected = proc.ExitCode == 0 &&
                        Regex.IsMatch(output, @"ST-LINK\s+SN\s*:", RegexOptions.IgnoreCase);

                    if (!connected && output.IndexOf("No ST-LINK detected", StringComparison.OrdinalIgnoreCase) >= 0)
                        AppendLog("No ST-LINK detected. Check connection and drivers.");

                    _isScanning = false;
                    SetConnectionState(connected, connected ? "Connected" : "Disconnected");
                }
            }
            catch (Exception ex)
            {
                _isScanning = false;
                AppendLog("Error during detection: " + ex.Message);
                SetConnectionState(false, "Error");
            }
        }

        private async void OnConnectionPollTick(object sender, EventArgs e)
        {
            if (_pollRunning || _flashInProgress || _isScanning) return;
            if (string.IsNullOrEmpty(CLI_PATH) || !File.Exists(CLI_PATH)) return;

            _pollRunning = true;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = CLI_PATH,
                    Arguments              = "-l",
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using (var proc = Process.Start(psi))
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit();
                    bool detected = proc.ExitCode == 0 &&
                        Regex.IsMatch(output, @"ST-LINK\s+SN\s*:", RegexOptions.IgnoreCase);
                    if (detected != _devicePresent)
                        SetDevicePresent(detected);
                }
            }
            catch { }
            finally { _pollRunning = false; }
        }

        // Called by poll — only updates _devicePresent, never sets _isConnected
        private void SetDevicePresent(bool present)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetDevicePresent(present))); return; }
            _devicePresent = present;
            if (!present) _isConnected = false;   // device gone — lose user-connected state too
            _isScanning = false;
            pnlConnectionIndicator.Invalidate();
            btnUploadAll.Enabled = present && !_flashInProgress;
        }

        // Called when user explicitly clicks Connect (or from scan result)
        private void SetConnectionState(bool connected, string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetConnectionState(connected, text))); return; }
            _devicePresent = connected;
            _isConnected   = connected;
            if (!connected && text == "Scanning...") _isScanning = true;
            _isScanning = connected ? false : _isScanning;
            pnlConnectionIndicator.Invalidate();
            btnUploadAll.Enabled = connected && !_flashInProgress;
        }

        // ── Firmware folder + slot loading ────────────────────────────────────
        private void LoadFixedFirmwareSet(bool showUiErrors)
        {
            string folder = _config.FirmwareFolderPath;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                AppendLog("Firmware folder not configured. Prompting.");
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select folder containing bootloader, metadata, firmware";
                    if (fbd.ShowDialog(this) != DialogResult.OK) return;
                    folder = fbd.SelectedPath;
                    _config.FirmwareFolderPath = folder;
                    SaveConfig(_config);
                    AppendLog($"Firmware folder set to: {folder}");
                }
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bin", ".hex" };
            var files   = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => allowed.Contains(Path.GetExtension(p)))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();

            if (files.Count < 3)
            {
                var msg = $"Expected at least 3 firmware files in:{Environment.NewLine}{folder}{Environment.NewLine}Found: {files.Count}";
                AppendLog(msg);
                if (showUiErrors) MessageBox.Show(this, msg, "Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dgvFiles.Rows.Clear(); UpdateStorageIndicator(); return;
            }

            var selected = SelectOrderedFixedFirmwareFiles(files);
            if (selected.Count != 3)
            {
                var msg = $"Could not identify bootloader/metadata/firmware set in:{Environment.NewLine}{folder}";
                AppendLog(msg);
                if (showUiErrors) MessageBox.Show(this, msg, "Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dgvFiles.Rows.Clear(); UpdateStorageIndicator(); return;
            }
            LoadFirmwareFiles(selected, "Firmware folder", copyIntoAppFolder: true);
        }

        private static uint ParseHexAddressOrDefault(string address, uint fallback)
        {
            if (string.IsNullOrWhiteSpace(address)) return fallback;
            var s = address.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static List<string> SelectOrderedFixedFirmwareFiles(List<string> all)
        {
            if (all == null) return new List<string>();
            var rem  = all.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
            var boot = TakeFirstMatch(rem, BootloaderNameTokens);
            var meta = TakeFirstMatch(rem, MetadataNameTokens);
            var fw   = TakeFirstMatch(rem, FirmwareNameTokens);
            if (boot == null && rem.Count > 0) { boot = rem[0]; rem.RemoveAt(0); }
            if (meta == null && rem.Count > 0) { meta = rem[0]; rem.RemoveAt(0); }
            if (fw   == null && rem.Count > 0) { fw   = rem[0]; rem.RemoveAt(0); }
            var sel = new List<string>();
            if (!string.IsNullOrWhiteSpace(boot)) sel.Add(boot);
            if (!string.IsNullOrWhiteSpace(meta)) sel.Add(meta);
            if (!string.IsNullOrWhiteSpace(fw))   sel.Add(fw);
            return sel;
        }

        private static string TakeFirstMatch(List<string> rem, string[] tokens)
        {
            if (rem == null || rem.Count == 0 || tokens == null || tokens.Length == 0) return null;
            for (int i = 0; i < rem.Count; i++)
            {
                var name = (Path.GetFileNameWithoutExtension(rem[i]) ?? "").ToLowerInvariant();
                if (tokens.Any(t => name.Contains(t))) { var m = rem[i]; rem.RemoveAt(i); return m; }
            }
            return null;
        }

        private void InitializeFirmwareRows()
        {
            dgvFiles.Rows.Clear();
            uint baseAddr = ParseHexAddressOrDefault(DefaultAddress, FlashBaseAddress);
            for (int i = 0; i < 3; i++)
            {
                var ri = dgvFiles.Rows.Add();
                var row = dgvFiles.Rows[ri];
                row.Tag = i;
                row.Cells["FileName"].Value = string.Empty;
                row.Cells["FullPath"].Value = string.Empty;
                row.Cells["Address"].Value  = $"0x{unchecked(baseAddr + (uint)i * ChunkSize):X8}";
                row.Cells["Size"].Value     = string.Empty;
                row.Cells["Status"].Value   = "Not Selected";
                row.Cells["ColorTag"].Value = ColorFromHsl(200 + (ri * 10) % 60, 60, 55).ToArgb().ToString();
                row.DefaultCellStyle.BackColor = darkBack;
                row.DefaultCellStyle.ForeColor = darkText;
            }
            foreach (TextBox tb in new[] { txtBootloaderPath, txtMetadataPath, txtFirmwarePath })
                tb.Text = string.Empty;
            dgvFiles.Invalidate();
        }

        private void SetFirmwareSlot(int slotIndex, string originalPath)
        {
            if (slotIndex < 0 || slotIndex > 2) return;
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath)) return;

            var roleNames  = new[] { "bootloader", "metadata", "firmware" };
            var cachedPath = CopyFirmwareFileToAppFolder(originalPath, roleNames[slotIndex]);
            var fi         = new FileInfo(cachedPath);

            if (slotIndex == 0) txtBootloaderPath.Text = originalPath;
            else if (slotIndex == 1) txtMetadataPath.Text = originalPath;
            else txtFirmwarePath.Text = originalPath;

            while (dgvFiles.Rows.Count < 3) dgvFiles.Rows.Add();

            var row = dgvFiles.Rows[slotIndex];
            row.Cells["FileName"].Value = Path.GetFileName(originalPath);
            row.Cells["FullPath"].Value = cachedPath;
            row.Cells["Size"].Value     = fi.Length.ToString();
            row.Cells["Status"].Value   = "Pending";
            dgvFiles.Invalidate();
            UpdateStorageIndicator();
        }

        private void LoadFirmwareFiles(List<string> ordered, string sourceLabel, bool copyIntoAppFolder)
        {
            if (ordered == null || ordered.Count != 3) return;
            dgvFiles.Rows.Clear();
            uint baseAddr  = ParseHexAddressOrDefault(DefaultAddress, FlashBaseAddress);
            var  roleNames = new[] { "bootloader", "metadata", "firmware" };
            AppendLog($"{sourceLabel} flash order:");

            for (int i = 0; i < ordered.Count; i++)
            {
                var originalPath  = ordered[i];
                var effectivePath = originalPath;
                if (copyIntoAppFolder)
                {
                    try { effectivePath = CopyFirmwareFileToAppFolder(originalPath, roleNames[i]); }
                    catch (Exception ex)
                    {
                        AppendLog($"Copy failed for {Path.GetFileName(originalPath)}: {ex.Message}");
                        MessageBox.Show(this, $"Failed to copy {Path.GetFileName(originalPath)}:{Environment.NewLine}{ex.Message}", "Copy Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                var fi  = new FileInfo(effectivePath);
                var ri  = dgvFiles.Rows.Add();
                var row = dgvFiles.Rows[ri];
                row.Cells["FileName"].Value = Path.GetFileName(originalPath);
                row.Cells["FullPath"].Value = effectivePath;
                row.Cells["Address"].Value  = $"0x{unchecked(baseAddr + (uint)i * ChunkSize):X8}";
                row.Cells["Size"].Value     = fi.Length.ToString();
                row.Cells["Status"].Value   = "Pending";
                row.Cells["ColorTag"].Value = ColorFromHsl(200 + (ri * 10) % 60, 60, 55).ToArgb().ToString();
                row.DefaultCellStyle.BackColor = darkBack;
                row.DefaultCellStyle.ForeColor = darkText;
                AppendLog($"  {roleNames[i],-9}: {Path.GetFileName(originalPath)} -> {Path.GetFileName(effectivePath)} @ {row.Cells["Address"].Value}");
            }
            dgvFiles.Invalidate();
            UpdateStorageIndicator();
        }

        private string GetFirmwareCacheDirectory()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TARA-Flasher", "firmware");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string CopyFirmwareFileToAppFolder(string sourcePath, string roleName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found.", sourcePath);
            var ext        = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
            var safeRole   = string.IsNullOrWhiteSpace(roleName) ? "image" : roleName.Trim().ToLowerInvariant();
            var destPath   = Path.Combine(GetFirmwareCacheDirectory(), safeRole + ext.ToLowerInvariant());
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                return destPath;
            File.Copy(sourcePath, destPath, overwrite: true);
            return destPath;
        }

        // ── Theme toggle ──────────────────────────────────────────────────────
        private void BtnToggleTheme_Click(object sender, EventArgs e)
        {
            isDarkTheme      = !isDarkTheme;
            _config.DarkTheme = isDarkTheme;
            SaveConfig(_config);
            ApplyThemeDefaults();
        }

        // ── Upload ────────────────────────────────────────────────────────────
        private async void BtnUploadAll_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(CLI_PATH) || !File.Exists(CLI_PATH))
            {
                MessageBox.Show(this, "STM32 CLI not found. Use Menu > Set CLI Path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var rows    = dgvFiles.Rows.Cast<DataGridViewRow>().ToList();
            var pending = new List<DataGridViewRow>();
            foreach (var r in rows)
            {
                var fileName = r.Cells["FileName"].Value?.ToString() ?? string.Empty;
                var address  = r.Cells["Address"].Value?.ToString() ?? DefaultAddress;
                var fullPath = r.Cells["FullPath"].Value?.ToString();

                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                {
                    fullPath = FindFullPathForFile(fileName);
                    if (!string.IsNullOrEmpty(fullPath)) r.Cells["FullPath"].Value = fullPath;
                }

                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) { r.Cells["Status"].Value = "Missing"; AppendLog($"File missing: {fileName}"); continue; }
                if (!Regex.IsMatch(address, "^0x[0-9A-Fa-f]+$"))              { r.Cells["Status"].Value = "Invalid Address"; continue; }
                pending.Add(r);
            }

            if (pending.Count == 0) { MessageBox.Show(this, "No valid pending files.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            pending = pending.OrderBy(r => ParseHexAddressOrDefault(r.Cells["Address"].Value?.ToString(), 0)).ToList();

            var roleNames = new[] { "bootloader", "metadata", "firmware" };
            uint baseAddr = ParseHexAddressOrDefault(DefaultAddress, FlashBaseAddress);
            for (int i = 0; i < pending.Count; i++)
            {
                var fp = pending[i].Cells["FullPath"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(fp) || !File.Exists(fp)) continue;
                try
                {
                    var addr = ParseHexAddressOrDefault(pending[i].Cells["Address"].Value?.ToString(), baseAddr);
                    int slot = -1;
                    if (addr >= baseAddr) { var offset = addr - baseAddr; if (ChunkSize != 0 && (offset % ChunkSize) == 0) slot = (int)(offset / ChunkSize); }
                    var role   = (slot >= 0 && slot < roleNames.Length) ? roleNames[slot] : "image";
                    var cached = CopyFirmwareFileToAppFolder(fp, role);
                    pending[i].Cells["FullPath"].Value = cached;
                }
                catch (Exception ex) { AppendLog($"Copy-to-cache failed: {ex.Message}"); }
            }

            if (!VerifyUploadPassword()) { AppendLog("Upload cancelled (password)."); return; }

            // Calculate total bytes so progress shows real overall percentage
            _totalFlashBytes = 0;
            foreach (var r in pending)
            {
                var fp2 = r.Cells["FullPath"].Value?.ToString();
                if (!string.IsNullOrEmpty(fp2) && File.Exists(fp2))
                    _totalFlashBytes += new FileInfo(fp2).Length;
            }
            if (_totalFlashBytes == 0) _totalFlashBytes = 1;

            _completedFlashBytes = 0;
            _completedFiles      = 0;
            _totalFiles          = pending.Count;
            _progressMax         = 10000;   // 0–10000 → 0.0–100.0 %
            _progressVal         = 0;
            pnlProgress.Invalidate();
            lblProgressInfo.Text = "Starting...";

            DisableControlsDuringFlash(true);
            int completed = 0, successCount = 0;
            foreach (var r in pending)
            {
                var fileName = r.Cells["FileName"].Value.ToString();
                var addr     = r.Cells["Address"].Value.ToString();
                var fullPath = r.Cells["FullPath"].Value?.ToString();
                _flashCurrentFile = fileName;
                _currentFileSize  = (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                                        ? new FileInfo(fullPath).Length : 0;

                r.Cells["Status"].Value = "Flashing";
                lblProgressInfo.Text    = $"Flashing {fileName} ({completed + 1}/{pending.Count})";

                // Capture loop-local for lambda
                long baseBytes = _completedFlashBytes;
                long fileSz    = _currentFileSize;

                try
                {
                    var ok = await FlashFileAsync(fullPath, addr, (cliPct) =>
                    {
                        long bytesNow = baseBytes + (long)(fileSz * cliPct / 100.0);
                        _progressVal  = (int)((bytesNow * 10000L) / _totalFlashBytes);
                    });
                    r.Cells["Status"].Value = ok ? "Done" : "Failed";
                    if (ok) successCount++;
                }
                catch (Exception ex) { r.Cells["Status"].Value = "Failed"; AppendLog($"Error: {ex.Message}"); }

                _completedFlashBytes += _currentFileSize;
                _completedFiles       = ++completed;
                _progressVal          = (int)((_completedFlashBytes * 10000L) / _totalFlashBytes);
                pnlProgress.Invalidate();
            }

            DisableControlsDuringFlash(false);
            lblProgressInfo.Text = successCount == pending.Count ? "SUCCESS" : (successCount == 0 ? "FAILED" : "PARTIAL");
            AppendLog($"Flashing complete. Success: {successCount}/{pending.Count}");
            ShowNeonResultDialog("Flash Result", lblProgressInfo.Text,
                successCount == pending.Count ? NeonDialogKind.Success : (successCount == 0 ? NeonDialogKind.Error : NeonDialogKind.Warning));
            // Reset progress bar after result is acknowledged so it does not stay filled
            _progressVal = 0;
            _progressMax = 1;
            pnlProgress.Invalidate();
            lblProgressInfo.Text = "Idle";
        }

        private void DisableControlsDuringFlash(bool disable, bool chipErase = false)
        {
            _isChipErase     = chipErase && disable;
            _flashInProgress = disable;
            if (disable && _pnlGridContainer != null)
                pnlUploadAnim.Bounds = _pnlGridContainer.Bounds;
            // Hide the grid while the animation runs so no content bleeds through
            if (_pnlGridContainer != null) _pnlGridContainer.Visible = !disable;
            pnlUploadAnim.Visible = disable;
            if (disable) { pnlUploadAnim.BringToFront(); pnlUploadAnim.Refresh(); }
            if (!disable) _pnlGridContainer?.Refresh();
            btnUploadAll.Enabled   = !disable && _devicePresent;
            if (btnMenu             != null) btnMenu.Enabled            = !disable;
            if (btnBrowseBootloader != null) btnBrowseBootloader.Enabled = !disable;
            if (btnBrowseMetadata   != null) btnBrowseMetadata.Enabled   = !disable;
            if (btnBrowseFirmware   != null) btnBrowseFirmware.Enabled   = !disable;
        }

        private void SelectSingleFirmwareFile(int slotIndex)
        {
            using (var ofd = new OpenFileDialog())
            {
                string slotName = slotIndex == 0 ? "bootloader" : (slotIndex == 1 ? "metadata" : "firmware");
                ofd.Title     = $"Select {slotName} file";
                ofd.Filter    = "Firmware files (*.bin;*.hex)|*.bin;*.hex|All files (*.*)|*.*";
                ofd.Multiselect = false;
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                if (string.IsNullOrWhiteSpace(ofd.FileName) || !File.Exists(ofd.FileName)) return;
                SetFirmwareSlot(slotIndex, ofd.FileName);
            }
        }

        private void BtnSetFirmwareFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description  = "Select firmware folder";
                fbd.SelectedPath = _config.FirmwareFolderPath ?? string.Empty;
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                _config.FirmwareFolderPath = fbd.SelectedPath;
                SaveConfig(_config);
                AppendLog($"Firmware folder: {fbd.SelectedPath}");
                ShowNeonResultDialog("Folder Set", $"Firmware folder:\n{fbd.SelectedPath}", NeonDialogKind.Success);
            }
        }

        private void BtnSetCliPath_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title  = "Select STM32_Programmer_CLI.exe";
                ofd.Filter = "Executable (*.exe)|*.exe";
                if (!string.IsNullOrEmpty(CLI_PATH)) ofd.InitialDirectory = Path.GetDirectoryName(CLI_PATH);
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                CLI_PATH = ofd.FileName;
                _config.STM32CLIPath = CLI_PATH;
                SaveConfig(_config);
                AppendLog($"CLI path: {CLI_PATH}");
            }
        }

        // ── Change passwords ──────────────────────────────────────────────────
        private void BtnChangeUploadPassword_Click(object sender, EventArgs e)
        {
            try
            {
                EnsurePasswordFileExists();
                bool adminOk = false;
                for (int a = 0; a < 3; a++)
                {
                    var pw = PromptForPassword(a == 0 ? "Enter admin password" : "Incorrect. Try again");
                    if (pw == null) return;
                    if (VerifyAdminPassword(pw)) { adminOk = true; break; }
                }
                if (!adminOk) { ShowNeonResultDialog("Access Denied", "Admin password wrong.", NeonDialogKind.Error); return; }
                var np = PromptForPassword("New upload password"); if (np == null) return;
                var cf = PromptForPassword("Confirm new password"); if (cf == null) return;
                if (!string.Equals(np, cf)) { ShowNeonResultDialog("Error", "Passwords do not match.", NeonDialogKind.Warning); return; }
                if (string.IsNullOrWhiteSpace(np)) { ShowNeonResultDialog("Error", "Password cannot be empty.", NeonDialogKind.Warning); return; }
                SetPassword(np);
                passwordVerifiedThisSession = false;
                AppendLog("Upload password changed.");
                ShowNeonResultDialog("Success", "Upload password changed.", NeonDialogKind.Success);
            }
            catch (Exception ex) { ShowNeonResultDialog("Error", ex.Message, NeonDialogKind.Error); }
        }

        private void BtnChangeAdminPassword_Click(object sender, EventArgs e)
        {
            try
            {
                bool ok = false;
                for (int a = 0; a < 3; a++)
                {
                    var pw = PromptForPassword(a == 0 ? "Enter current admin password" : "Incorrect. Try again");
                    if (pw == null) return;
                    if (VerifyAdminPassword(pw)) { ok = true; break; }
                }
                if (!ok) { ShowNeonResultDialog("Access Denied", "Wrong password.", NeonDialogKind.Error); return; }
                var np = PromptForPassword("New admin password"); if (np == null) return;
                var cf = PromptForPassword("Confirm");            if (cf == null) return;
                if (!string.Equals(np, cf)) { ShowNeonResultDialog("Error", "Mismatch.", NeonDialogKind.Warning); return; }
                if (string.IsNullOrWhiteSpace(np)) { ShowNeonResultDialog("Error", "Empty.", NeonDialogKind.Warning); return; }
                SetAdminPassword(np);
                AppendLog("Admin password changed.");
                ShowNeonResultDialog("Success", "Admin password changed.", NeonDialogKind.Success);
            }
            catch (Exception ex) { ShowNeonResultDialog("Error", ex.Message, NeonDialogKind.Error); }
        }

        // ── Serial monitor ────────────────────────────────────────────────────
        private void OpenSerialMonitor()
        {
            if (_serialMonitor == null || _serialMonitor.IsDisposed)
                _serialMonitor = new SerialMonitorForm();
            _serialMonitor.Show(this);
            _serialMonitor.BringToFront();
        }

        // ── Full chip erase ───────────────────────────────────────────────────
        private async void BtnFullChipErase_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(CLI_PATH) || !File.Exists(CLI_PATH))
            { ShowNeonResultDialog("Error", "STM32 CLI not found.", NeonDialogKind.Error); return; }

            if (!_devicePresent)
            { ShowNeonResultDialog("Not Connected", "Connect device first.", NeonDialogKind.Warning); return; }

            bool adminOk = false;
            for (int a = 0; a < 3; a++)
            {
                var pw = PromptForPassword(a == 0 ? "Admin password required for chip erase" : "Incorrect. Try again");
                if (pw == null) return;
                if (VerifyAdminPassword(pw)) { adminOk = true; break; }
            }
            if (!adminOk) { ShowNeonResultDialog("Access Denied", "Admin password wrong.", NeonDialogKind.Error); return; }

            using (var confirm = new Form())
            using (var header  = new Panel())
            using (var lblW    = new Label())
            using (var lblD    = new Label())
            using (var btnY    = new Button())
            using (var btnN    = new Button())
            {
                confirm.Text = "CONFIRM FULL CHIP ERASE";
                confirm.FormBorderStyle = FormBorderStyle.FixedDialog;
                confirm.StartPosition   = FormStartPosition.CenterParent;
                confirm.MinimizeBox = false; confirm.MaximizeBox = false;
                confirm.ShowInTaskbar = false;
                confirm.ClientSize = new Size(520, 230);
                confirm.BackColor  = Color.FromArgb(8, 12, 18);

                header.Location = new Point(0, 0); header.Size = new Size(520, 58);
                header.BackColor = Color.FromArgb(18, 8, 8);
                lblW.Text = "WARNING :: FULL CHIP ERASE";
                lblW.Location = new Point(16, 16); lblW.Size = new Size(488, 28);
                lblW.ForeColor = Color.FromArgb(255, 80, 80);
                lblW.Font = new Font("Consolas", 12F, FontStyle.Bold);
                header.Controls.Add(lblW);

                lblD.Text = "This will permanently erase ALL data on the flash memory,\r\nincluding bootloader, metadata, and firmware.\r\n\r\nThis action CANNOT be undone. Proceed?";
                lblD.Location = new Point(16, 70); lblD.Size = new Size(488, 100);
                lblD.ForeColor = Color.FromArgb(220, 200, 200); lblD.Font = new Font("Segoe UI", 10F);

                btnN.Text = "Cancel"; btnN.Size = new Size(100, 34);
                btnN.Location = new Point(300, 180); btnN.DialogResult = DialogResult.Cancel;
                btnN.FlatStyle = FlatStyle.Flat; btnN.BackColor = Color.FromArgb(12, 20, 28);
                btnN.ForeColor = Color.FromArgb(180, 210, 210);
                btnY.Text = "ERASE"; btnY.Size = new Size(100, 34);
                btnY.Location = new Point(408, 180); btnY.DialogResult = DialogResult.Yes;
                btnY.FlatStyle = FlatStyle.Flat; btnY.BackColor = Color.FromArgb(80, 10, 10);
                btnY.ForeColor = Color.FromArgb(255, 80, 80);

                confirm.CancelButton = btnN;
                confirm.Controls.AddRange(new Control[] { header, lblD, btnN, btnY });
                if (confirm.ShowDialog(this) != DialogResult.Yes) return;
            }

            DisableControlsDuringFlash(true, chipErase: true);
            lblProgressInfo.Text = "Erasing chip...";
            AppendLog("Starting full chip erase...");

            bool ok = await FullChipEraseAsync();

            DisableControlsDuringFlash(false);
            if (ok)
            {
                lblProgressInfo.Text = "Erase complete";
                AppendLog("Full chip erase completed.");
                InitializeFirmwareRows(); UpdateStorageIndicator();
                ShowNeonResultDialog("Chip Erased", "Full chip erase completed successfully.", NeonDialogKind.Success);
            }
            else
            {
                lblProgressInfo.Text = "Erase failed";
                AppendLog("Erase failed. Check log.");
                ShowNeonResultDialog("Erase Failed", "Full chip erase failed.", NeonDialogKind.Error);
            }
        }

        private async Task<bool> FullChipEraseAsync()
        {
            string args = "-c port=SWD -e all";
            AppendLog($"{DateTime.Now:HH:mm:ss} Executing: {CLI_PATH} {args}");
            var psi = new ProcessStartInfo { FileName = CLI_PATH, Arguments = args, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<int>();
                proc.OutputDataReceived += (s, ev) => { if (!string.IsNullOrEmpty(ev.Data)) AppendCliOutput(ev.Data); };
                proc.ErrorDataReceived  += (s, ev) => { if (!string.IsNullOrEmpty(ev.Data)) AppendCliOutput("[ERR] " + ev.Data); };
                proc.Exited += (s, ev) => { try { tcs.TrySetResult(proc.ExitCode); } catch (Exception ex) { tcs.TrySetException(ex); } };
                try
                {
                    if (!proc.Start()) return false;
                    proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
                    var exit = await tcs.Task.ConfigureAwait(false);
                    AppendLog($"Erase exit code {exit}");
                    return exit == 0;
                }
                catch (Exception ex) { AppendLog("Erase error: " + ex.Message); return false; }
            }
        }

        // ── Misc helpers ──────────────────────────────────────────────────────
        private string FindFullPathForFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            try
            {
                var cached = Directory.GetFiles(GetFirmwareCacheDirectory(), fileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrEmpty(cached)) return cached;
            }
            catch (Exception ex) { AppendLog($"Cache search error: {ex.Message}"); }
            try { return Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, fileName, SearchOption.AllDirectories).FirstOrDefault(); }
            catch (Exception ex) { AppendLog($"BaseDir search: {ex.Message}"); return null; }
        }

        private async Task<bool> FlashFileAsync(string filePath, string address, Action<int> onCliProgress = null)
        {
            string args = $"-c port=SWD -d \"{filePath}\" {address} -v";
            AppendLog($"{DateTime.Now:HH:mm:ss} Flashing: {Path.GetFileName(filePath)} @ {address}");
            var psi = new ProcessStartInfo { FileName = CLI_PATH, Arguments = args, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<int>();
                proc.OutputDataReceived += (s, ev) =>
                {
                    if (string.IsNullOrEmpty(ev.Data)) return;
                    AppendCliOutput(ev.Data);
                    // Parse "XX%" from progress lines like "########## 57%"
                    var m = Regex.Match(ev.Data, @"(\d{1,3})\s*%");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int pct))
                        onCliProgress?.Invoke(Math.Min(100, pct));
                };
                proc.ErrorDataReceived  += (s, ev) => { if (!string.IsNullOrEmpty(ev.Data)) AppendCliOutput("[ERR] " + ev.Data); };
                proc.Exited += (s, ev) => { try { tcs.TrySetResult(proc.ExitCode); } catch (Exception ex) { tcs.TrySetException(ex); } };
                try
                {
                    if (!proc.Start()) { AppendLog("Failed to start CLI."); return false; }
                    proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
                    var exit = await tcs.Task.ConfigureAwait(false);
                    AppendLog($"Exit code {exit}");
                    return exit == 0;
                }
                catch (Exception ex) { AppendLog("Process error: " + ex.Message); return false; }
            }
        }

        private void AppendCliOutput(string output) { AppendLog("[CLI] " + output); }
        private void AppendLog(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(text))); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }

        private static Color ColorFromHsl(int h, int s, int l)
        {
            double H = h / 360.0, S = s / 100.0, L = l / 100.0;
            double r = 0, g = 0, b = 0;
            if (S == 0) { r = g = b = L; }
            else
            {
                Func<double, double, double, double> htr = (pp, qq, tt) =>
                {
                    if (tt < 0) tt += 1; if (tt > 1) tt -= 1;
                    if (tt < 1.0 / 6) return pp + (qq - pp) * 6 * tt;
                    if (tt < 1.0 / 2) return qq;
                    if (tt < 2.0 / 3) return pp + (qq - pp) * (2.0 / 3 - tt) * 6;
                    return pp;
                };
                double q = L < 0.5 ? L * (1 + S) : L + S - L * S, p = 2 * L - q;
                r = htr(p, q, H + 1.0 / 3); g = htr(p, q, H); b = htr(p, q, H - 1.0 / 3);
            }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        // ── Storage indicator ─────────────────────────────────────────────────
        private void UpdateStorageIndicator()
        {
            var segs  = new List<StorageSegment>();
            long total = 0;
            foreach (DataGridViewRow row in dgvFiles.Rows)
            {
                if (row.Cells["Size"].Value != null &&
                    long.TryParse(row.Cells["Size"].Value.ToString(), out var sz) &&
                    row.Cells["Address"].Value != null &&
                    row.Cells["FileName"].Value != null)
                {
                    var addrStr  = row.Cells["Address"].Value.ToString();
                    var fileName = row.Cells["FileName"].Value.ToString();
                    var colorArgb = row.Cells["ColorTag"].Value?.ToString();
                    Color sc = Color.CornflowerBlue;
                    if (!string.IsNullOrEmpty(colorArgb) && int.TryParse(colorArgb, out var argb))
                        sc = Color.FromArgb(argb);
                    var addrStart = ParseHexAddressOrDefault(addrStr, FlashBaseAddress);
                    segs.Add(new StorageSegment { FileName = fileName, Size = sz, AddressStart = addrStart, AddressEnd = addrStart + (uint)sz, Color = sc });
                    total += sz;
                }
            }

            long maxSz    = deviceFlashSize > 0 ? deviceFlashSize : DefaultDeviceFlashSizeBytes;
            var  usedBytes = CalculateUsedBytesInFlash(segs, FlashBaseAddress, maxSz);
            var  freeBytes = Math.Max(0, maxSz - usedBytes);
            var  usedPct  = maxSz > 0 ? usedBytes * 100.0 / maxSz : 0;

            storageVisualizer?.SetSegments(segs, FlashBaseAddress, maxSz);
            storageVisualizer?.Invalidate();

            storageInfo.Text =
                $"Used: {usedBytes:N0} / {maxSz:N0} bytes  ({usedPct:0.0}%)   Free: {freeBytes:N0} bytes{Environment.NewLine}" +
                $"Files total: {total:N0} bytes   Flash: 0x{FlashBaseAddress:X8} – 0x{unchecked(FlashBaseAddress + (uint)maxSz):X8}";
        }

        private static long CalculateUsedBytesInFlash(List<StorageSegment> segs, uint flashBase, long flashSz)
        {
            if (segs == null || segs.Count == 0 || flashSz <= 0) return 0;
            ulong fs = flashBase, fe = (ulong)flashBase + (ulong)flashSz;
            var ranges = new List<(ulong S, ulong E)>();
            foreach (var seg in segs)
            {
                ulong s = seg.AddressStart, en = seg.AddressEnd;
                if (en <= fs || s >= fe) continue;
                if (s < fs) s = fs; if (en > fe) en = fe;
                if (en > s) ranges.Add((s, en));
            }
            if (ranges.Count == 0) return 0;
            ranges.Sort((a, b) => a.S.CompareTo(b.S));
            ulong ms = ranges[0].S, me = ranges[0].E, tot = 0;
            for (int i = 1; i < ranges.Count; i++)
            {
                if (ranges[i].S <= me) { if (ranges[i].E > me) me = ranges[i].E; }
                else { tot += me - ms; ms = ranges[i].S; me = ranges[i].E; }
            }
            tot += me - ms;
            return tot > long.MaxValue ? long.MaxValue : (long)tot;
        }

        // ── Password file ─────────────────────────────────────────────────────
        private void EnsurePasswordFileExists()
        {
            try { var p = GetPasswordFilePath(); if (!File.Exists(p)) SetPassword(DefaultPassword); }
            catch (Exception ex) { AppendLog("Password init error: " + ex.Message); }
        }

        private string GetPasswordFilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TARA-Flasher");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PasswordFileName);
        }

        private bool VerifyUploadPassword()
        {
            if (passwordVerifiedThisSession) return true;
            EnsurePasswordFileExists();
            for (int a = 0; a < 3; a++)
            {
                var inp = PromptForPassword(a == 0 ? "Enter password to upload" : "Incorrect. Try again");
                if (inp == null) return false;
                if (VerifyPassword(inp)) { passwordVerifiedThisSession = true; return true; }
            }
            ShowNeonResultDialog("Access Denied", "Password verification failed.", NeonDialogKind.Error);
            return false;
        }

        private static byte[] ComputePasswordHash(byte[] salt, string password)
        {
            using (var sha = SHA256.Create())
            {
                var pw  = Encoding.UTF8.GetBytes(password ?? string.Empty);
                var buf = new byte[salt.Length + pw.Length];
                Buffer.BlockCopy(salt, 0, buf, 0, salt.Length);
                Buffer.BlockCopy(pw,   0, buf, salt.Length, pw.Length);
                return sha.ComputeHash(buf);
            }
        }

        private void SetPassword(string newPassword)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            var hash    = ComputePasswordHash(salt, newPassword);
            var content = Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
            File.WriteAllText(GetPasswordFilePath(), content);
        }

        private bool VerifyPassword(string input)
        {
            try
            {
                var parts    = File.ReadAllText(GetPasswordFilePath()).Trim().Split(new[] { ':' }, 2);
                if (parts.Length != 2) return false;
                var salt     = Convert.FromBase64String(parts[0]);
                var expected = Convert.FromBase64String(parts[1]);
                var actual   = ComputePasswordHash(salt, input);
                return FixedTimeEquals(expected, actual);
            }
            catch (Exception ex) { AppendLog($"Password verify error: {ex.Message}"); return false; }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length, len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ── Password prompt ───────────────────────────────────────────────────
        private string PromptForPassword(string titleText)
        {
            using (var dlg    = new Form())
            using (var lbl    = new Label())
            using (var txt    = new TextBox())
            using (var hint   = new Label())
            using (var ok     = new Button())
            using (var cancel = new Button())
            {
                dlg.Text = "AUTH"; dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false; dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(420, 170); dlg.ShowInTaskbar = false;
                dlg.BackColor  = Color.FromArgb(8, 12, 18);
                dlg.Font       = new Font("Consolas", 9.5F);

                lbl.Text = titleText; lbl.Location = new Point(16, 14); lbl.Size = new Size(388, 36);
                lbl.ForeColor = Color.FromArgb(0, 255, 170); lbl.Font = new Font("Consolas", 11F, FontStyle.Bold);

                hint.Text = "Input is masked. Press Enter to confirm.";
                hint.Location = new Point(16, 52); hint.Size = new Size(388, 16);
                hint.ForeColor = Color.FromArgb(120, 170, 160);

                txt.Location = new Point(16, 76); txt.Size = new Size(388, 24);
                txt.UseSystemPasswordChar = true;
                txt.BackColor = Color.FromArgb(4, 10, 14); txt.ForeColor = Color.FromArgb(220, 255, 245);
                txt.BorderStyle = BorderStyle.FixedSingle;

                ok.Text = "OK"; ok.Location = new Point(248, 118); ok.Size = new Size(75, 30);
                ok.DialogResult = DialogResult.OK; ok.FlatStyle = FlatStyle.Flat;
                ok.BackColor = Color.FromArgb(12, 20, 28); ok.ForeColor = Color.FromArgb(0, 255, 170);

                cancel.Text = "Cancel"; cancel.Location = new Point(329, 118); cancel.Size = new Size(75, 30);
                cancel.DialogResult = DialogResult.Cancel; cancel.FlatStyle = FlatStyle.Flat;
                cancel.BackColor = Color.FromArgb(12, 20, 28); cancel.ForeColor = Color.FromArgb(180, 210, 210);

                dlg.AcceptButton = ok; dlg.CancelButton = cancel;
                dlg.Controls.AddRange(new Control[] { lbl, hint, txt, ok, cancel });
                return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }

        // ── Neon confirm dialog (Yes / No) ────────────────────────────────────
        private bool ShowNeonConfirmDialog(string title, string message)
        {
            using (var dlg    = new Form())
            using (var header = new Panel())
            using (var lblT   = new Label())
            using (var lblB   = new Label())
            using (var btnYes = new Button())
            using (var btnNo  = new Button())
            {
                Color accent = Color.FromArgb(255, 190, 60);
                dlg.Text = title; dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false; dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false; dlg.ClientSize = new Size(520, 230);
                dlg.BackColor = Color.FromArgb(8, 12, 18); dlg.Font = new Font("Segoe UI", 9.5F);

                header.Location = new Point(0, 0); header.Size = new Size(520, 58);
                header.BackColor = Color.FromArgb(12, 18, 26);
                lblT.Text = $"SETUP :: {title}";
                lblT.Location = new Point(16, 16); lblT.Size = new Size(488, 28);
                lblT.ForeColor = accent; lblT.Font = new Font("Consolas", 12F, FontStyle.Bold);
                header.Controls.Add(lblT);

                lblB.Text = message;
                lblB.Location = new Point(16, 74); lblB.Size = new Size(488, 106);
                lblB.ForeColor = Color.FromArgb(220, 235, 245); lblB.Font = new Font("Segoe UI", 9.5F);

                btnYes.Text = "Install"; btnYes.Size = new Size(100, 32);
                btnYes.Location = new Point(304, 188); btnYes.DialogResult = DialogResult.Yes;
                btnYes.FlatStyle = FlatStyle.Flat;
                btnYes.BackColor = Color.FromArgb(10, 30, 15); btnYes.ForeColor = Color.FromArgb(0, 220, 110);
                btnYes.FlatAppearance.BorderColor = Color.FromArgb(0, 160, 80);

                btnNo.Text = "Skip"; btnNo.Size = new Size(90, 32);
                btnNo.Location = new Point(414, 188); btnNo.DialogResult = DialogResult.No;
                btnNo.FlatStyle = FlatStyle.Flat;
                btnNo.BackColor = Color.FromArgb(22, 10, 10); btnNo.ForeColor = Color.FromArgb(200, 80, 80);
                btnNo.FlatAppearance.BorderColor = Color.FromArgb(140, 40, 40);

                dlg.AcceptButton = btnYes; dlg.CancelButton = btnNo;
                dlg.Controls.AddRange(new Control[] { header, lblB, btnYes, btnNo });
                return dlg.ShowDialog(this) == DialogResult.Yes;
            }
        }

        // ── Neon result dialog ─────────────────────────────────────────────────
        private enum NeonDialogKind { Success, Warning, Error, Info }

        private void ShowNeonResultDialog(string title, string message, NeonDialogKind kind)
        {
            using (var dlg    = new Form())
            using (var header = new Panel())
            using (var lblT   = new Label())
            using (var lblB   = new Label())
            using (var btn    = new Button())
            {
                dlg.Text = title ?? "Status"; dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false; dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false; dlg.ClientSize = new Size(520, 220);
                dlg.BackColor = Color.FromArgb(8, 12, 18); dlg.Font = new Font("Segoe UI", 9.5F);

                Color accent; string badge;
                switch (kind)
                {
                    case NeonDialogKind.Success: accent = Color.FromArgb(0, 255, 170); badge = "SUCCESS"; break;
                    case NeonDialogKind.Warning: accent = Color.FromArgb(255, 190, 60); badge = "WARNING"; break;
                    case NeonDialogKind.Error:   accent = Color.FromArgb(255, 80, 110); badge = "ERROR";   break;
                    default:                     accent = Color.FromArgb(80, 180, 255); badge = "INFO";    break;
                }

                header.Location = new Point(0, 0); header.Size = new Size(520, 58);
                header.BackColor = Color.FromArgb(12, 18, 26);
                lblT.Text = $"{badge} :: {title}";
                lblT.Location = new Point(16, 16); lblT.Size = new Size(488, 28);
                lblT.ForeColor = accent; lblT.Font = new Font("Consolas", 12F, FontStyle.Bold);
                header.Controls.Add(lblT);

                lblB.Text = message ?? string.Empty;
                lblB.Location = new Point(16, 74); lblB.Size = new Size(488, 96);
                lblB.ForeColor = Color.FromArgb(220, 235, 245); lblB.Font = new Font("Segoe UI", 10F);

                btn.Text = "OK"; btn.Size = new Size(90, 32);
                btn.Location = new Point(414, 172); btn.DialogResult = DialogResult.OK;
                btn.FlatStyle = FlatStyle.Flat; btn.BackColor = Color.FromArgb(12, 20, 28); btn.ForeColor = accent;

                dlg.AcceptButton = btn;
                dlg.Controls.AddRange(new Control[] { header, lblB, btn });
                dlg.ShowDialog(this);
            }
        }

        // ── Form closing ──────────────────────────────────────────────────────
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var bg = new SolidBrush(isDarkTheme ? darkBack : lightBack))
                e.Graphics.FillRectangle(bg, ClientRectangle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _pollTimer?.Stop();
            _uiAnim?.Stop();
            try { _serialMonitor?.Close(); } catch { }
            _headerLogo?.Dispose();
            base.OnFormClosing(e);
        }
    }

    // ── Storage data class ────────────────────────────────────────────────────
    public class StorageSegment
    {
        public string FileName    { get; set; }
        public long   Size        { get; set; }
        public uint   AddressStart { get; set; }
        public uint   AddressEnd  { get; set; }
        public Color  Color       { get; set; }
    }

    // ── Storage visualizer (HUD-enhanced) ────────────────────────────────────
    public class StorageVisualizerControl : Control
    {
        private const uint DefaultFlashBase = 0x08000000;
        private const long DefaultFlashSize = 2L * 1024 * 1024;

        private List<StorageSegment> _segs = new List<StorageSegment>();
        private uint _flashBase = DefaultFlashBase;
        private long _flashSize = DefaultFlashSize;

        public bool    ThemeIsDark       { get; set; } = true;
        public Color[] DarkThemeColors   { get; set; }
        public Color[] LightThemeColors  { get; set; }

        public StorageVisualizerControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetSegments(List<StorageSegment> segs, uint flashBase, long flashSize)
        {
            _segs      = segs ?? new List<StorageSegment>();
            _flashBase = flashBase;
            _flashSize = flashSize > 0 ? flashSize : DefaultFlashSize;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            if (_segs.Count == 0) { DrawEmpty(g); return; }
            DrawBar(g);
            DrawLegend(g);
        }

        private void DrawBar(Graphics g)
        {
            int bx = 8, by = 16, bw = Width - 16, bh = 22;

            // Address labels
            var labelColor = ThemeIsDark ? Color.FromArgb(70, 120, 160) : Color.FromArgb(80, 100, 140);
            using (var f = new Font("Consolas", 6.5F))
            using (var b = new SolidBrush(labelColor))
            {
                g.DrawString($"0x{_flashBase:X8}", f, b, bx, 2);
                g.DrawString($"0x{unchecked(_flashBase + (uint)_flashSize):X8}", f, b, bx + bw - 72, 2);
            }

            // Background track with subtle grid lines
            using (var bg = new SolidBrush(Color.FromArgb(ThemeIsDark ? 15 : 220, 30, 40, 60)))
                g.FillRectangle(bg, bx, by, bw, bh);

            // Segment fills
            uint minA = _flashBase, maxA = unchecked(_flashBase + (uint)_flashSize);
            uint range = maxA - minA; if (range == 0) range = 1;

            foreach (var seg in _segs)
            {
                ulong ss = seg.AddressStart, se = seg.AddressEnd;
                ulong fs = minA, fe = maxA;
                if (se <= fs || ss >= fe) continue;
                ulong vs = ss < fs ? fs : ss, ve = se > fe ? fe : se;
                if (ve <= vs) continue;

                float x1 = bx + (float)((vs - fs) * (ulong)bw) / range;
                float x2 = bx + (float)((ve - fs) * (ulong)bw) / range;
                int sw = Math.Max(2, (int)(x2 - x1));

                using (var fill = new SolidBrush(seg.Color))
                    g.FillRectangle(fill, (int)x1, by, sw, bh);

                // Glow on top edge
                using (var glow = new LinearGradientBrush(
                    new RectangleF((int)x1, by, sw, 4),
                    Color.FromArgb(120, 255, 255, 255), Color.Transparent,
                    LinearGradientMode.Vertical))
                    g.FillRectangle(glow, (int)x1, by, sw, 4);

                // Address text inside segment if wide enough
                if (sw > 60)
                {
                    using (var sf = new Font("Consolas", 5.5F))
                    using (var sb = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                    using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString($"0x{seg.AddressStart:X8}", sf, sb, (int)x1 + sw / 2, by + bh / 2, fmt);
                }

                // Separator between segments
                using (var sp = new Pen(Color.FromArgb(40, 0, 0, 0), 1))
                    g.DrawLine(sp, (int)x1, by, (int)x1, by + bh);
            }

            // Border with HUD corner brackets
            using (var bp = new Pen(Color.FromArgb(60, 32, 185, 255), 1))
                g.DrawRectangle(bp, bx, by, bw, bh);

            int cl = 6;
            using (var cp = new Pen(Color.FromArgb(180, 32, 185, 255), 1.5f))
            {
                g.DrawLine(cp, bx, by, bx + cl, by); g.DrawLine(cp, bx, by, bx, by + cl);
                g.DrawLine(cp, bx + bw, by, bx + bw - cl, by); g.DrawLine(cp, bx + bw, by, bx + bw, by + cl);
                g.DrawLine(cp, bx, by + bh, bx + cl, by + bh); g.DrawLine(cp, bx, by + bh, bx, by + bh - cl);
                g.DrawLine(cp, bx + bw, by + bh, bx + bw - cl, by + bh); g.DrawLine(cp, bx + bw, by + bh, bx + bw, by + bh - cl);
            }
        }

        private void DrawLegend(Graphics g)
        {
            int lx = 8, ly = 46, lineH = 22, colW = (Width - 16) / 2;
            int col = 0, row = 0;
            var textColor = ThemeIsDark ? Color.FromArgb(180, 210, 240) : Color.FromArgb(40, 60, 90);
            var addrColor = ThemeIsDark ? Color.FromArgb(80, 160, 220) : Color.FromArgb(0, 80, 160);

            using (var tf = new Font("Segoe UI", 7F))
            using (var af = new Font("Consolas", 6.5F))
            using (var tb = new SolidBrush(textColor))
            using (var ab = new SolidBrush(addrColor))
            using (var bp = new Pen(Color.FromArgb(40, 50, 50, 50), 1))
            {
                foreach (var seg in _segs)
                {
                    int x = lx + col * colW, y = ly + row * lineH;
                    var box = new Rectangle(x, y + 2, 10, 10);
                    using (var sb = new SolidBrush(seg.Color)) g.FillRectangle(sb, box);
                    g.DrawRectangle(bp, box);
                    g.DrawString($"{seg.FileName}  ({FormatBytes(seg.Size)})", tf, tb, x + 14, y);
                    g.DrawString($"@ 0x{seg.AddressStart:X8} → 0x{seg.AddressEnd:X8}", af, ab, x + 14, y + 10);
                    col++;
                    if (col >= 2) { col = 0; row++; }
                }
            }
        }

        private void DrawEmpty(Graphics g)
        {
            var c = ThemeIsDark ? Color.FromArgb(50, 90, 120) : Color.FromArgb(120, 140, 180);
            using (var f = new Font("Consolas", 8.5F))
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("No firmware loaded", f, b, ClientRectangle, sf);
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes; int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:F1} {sizes[order]}";
        }
    }
}
