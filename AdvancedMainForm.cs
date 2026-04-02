using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CUAVFlasher
{
    public class AdvancedMainForm : Form
    {
        private string CLI_PATH;
        private AppConfig _config;
        private readonly string configFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CUAVFlasher",
            "config.json");
        private const string DefaultAddress = "0x08000000";
        private const uint FlashBaseAddress = 0x08000000;
        private const long DefaultDeviceFlashSizeBytes = 2L * 1024 * 1024;
        private const uint ChunkSize = 0x00020000;
        private const string PasswordFileName = "password.dat";
        private const string DefaultPassword = "12345";
        private const string DefaultAdminPassword = "Tara@123";
        private DataGridView dgvFiles;
        private Button btnUploadAll;
        private Button btnMenu;
        private ContextMenuStrip menuStrip;
        private Button btnConnect;
        private Panel pnlConnectionIndicator;
        private Label lblConnectionText;
        private GroupBox grpFirmware;
        private TextBox txtBootloaderPath;
        private TextBox txtMetadataPath;
        private TextBox txtFirmwarePath;
        private Button btnBrowseBootloader;
        private Button btnBrowseMetadata;
        private Button btnBrowseFirmware;
        private bool isDarkTheme = true;
        private readonly Color darkBack = Color.FromArgb(28, 32, 38);
        private readonly Color darkPanel = Color.FromArgb(36, 41, 51);
        private readonly Color darkAccent = Color.FromArgb(32, 185, 255);
        private readonly Color darkGrid = Color.FromArgb(44, 48, 58);
        private readonly Color darkText = Color.FromArgb(220, 230, 245);
        private readonly Color darkSubtle = Color.FromArgb(120, 140, 160);
        private readonly Color lightBack = Color.FromArgb(245, 247, 250);
        private readonly Color lightPanel = Color.FromArgb(230, 235, 245);
        private readonly Color lightAccent = Color.FromArgb(0, 120, 215);
        private readonly Color lightGrid = Color.FromArgb(210, 220, 230);
        private readonly Color lightText = Color.FromArgb(30, 35, 45);
        private readonly Color lightSubtle = Color.FromArgb(100, 110, 130);
        private TextBox txtLog;
        private Panel storagePanel;
        private ProgressBar progressBar;
        private Label lblProgressInfo;
        private StorageVisualizerControl storageVisualizer;
        private Label storageLabel;
        private Label storageInfo;
        private long deviceFlashSize = DefaultDeviceFlashSizeBytes;
        private static readonly string[] BootloaderNameTokens = { "bootloader", "boot" };
        private static readonly string[] MetadataNameTokens = { "metadata", "meta" };
        private static readonly string[] FirmwareNameTokens = { "firmware", "firm", "app", "main", "fw" };
        private volatile bool passwordVerifiedThisSession;

        class AppConfig
        {
            public string STM32CLIPath { get; set; }
            public string FirmwareFolderPath { get; set; }
            public bool DarkTheme { get; set; } = true;
            public string AdminPasswordHash { get; set; }
            public string AdminPasswordSalt { get; set; }
        }

        // LoadConfig is called before UI is initialized, so no AppendLog here.
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
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFile, json);
            }
            catch (Exception ex) { AppendLog($"Config save error: {ex.Message}"); }
        }

        private string FindSTM32CLI()
        {
            string exeName = "STM32_Programmer_CLI.exe";
            string defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin",
                exeName);

            if (File.Exists(defaultPath))
                return defaultPath;

            try
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var dirs = Directory.GetDirectories(programFiles, "STM32CubeProgrammer*", SearchOption.AllDirectories);
                foreach (var dir in dirs)
                {
                    var cli = Path.Combine(dir, "bin", exeName);
                    if (File.Exists(cli))
                        return cli;
                }
            }
            catch (Exception ex) { AppendLog($"CLI search error: {ex.Message}"); }

            return null;
        }

        // On first run, generate and store a salted hash of the default admin password.
        // The plaintext never lives in the config or at runtime beyond this call.
        private void EnsureAdminPasswordInitialized(AppConfig config)
        {
            if (!string.IsNullOrEmpty(config.AdminPasswordHash) && !string.IsNullOrEmpty(config.AdminPasswordSalt))
                return;

            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);

            var hash = ComputePasswordHash(salt, DefaultAdminPassword);
            config.AdminPasswordHash = Convert.ToBase64String(hash);
            config.AdminPasswordSalt = Convert.ToBase64String(salt);
            SaveConfig(config);
        }

        private bool VerifyAdminPassword(string input)
        {
            if (string.IsNullOrEmpty(_config.AdminPasswordHash) || string.IsNullOrEmpty(_config.AdminPasswordSalt))
                return string.Equals(input, DefaultAdminPassword, StringComparison.Ordinal);

            var salt = Convert.FromBase64String(_config.AdminPasswordSalt);
            var expected = Convert.FromBase64String(_config.AdminPasswordHash);
            var actual = ComputePasswordHash(salt, input);
            return FixedTimeEquals(expected, actual);
        }

        private void SetAdminPassword(string newPassword)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);

            var hash = ComputePasswordHash(salt, newPassword);
            _config.AdminPasswordHash = Convert.ToBase64String(hash);
            _config.AdminPasswordSalt = Convert.ToBase64String(salt);
            SaveConfig(_config);
        }

        public AdvancedMainForm()
        {
            // Load config before UI setup so theme is correct on first paint.
            _config = LoadConfig();
            isDarkTheme = _config.DarkTheme;

            InitializeComponents();
            EnsurePasswordFileExists();
            EnsureAdminPasswordInitialized(_config);

            if (!string.IsNullOrEmpty(_config.STM32CLIPath) && File.Exists(_config.STM32CLIPath))
            {
                CLI_PATH = _config.STM32CLIPath;
                AppendLog("Using saved STM32 CLI path.");
            }
            else
            {
                CLI_PATH = FindSTM32CLI();
                if (!string.IsNullOrEmpty(CLI_PATH))
                {
                    _config.STM32CLIPath = CLI_PATH;
                    SaveConfig(_config);
                    AppendLog("STM32 CLI auto-detected and saved.");
                }
                else
                {
                    using (var ofd = new OpenFileDialog())
                    {
                        ofd.Title = "Select STM32_Programmer_CLI.exe";
                        ofd.Filter = "Executable (*.exe)|*.exe";
                        if (ofd.ShowDialog() == DialogResult.OK)
                        {
                            CLI_PATH = ofd.FileName;
                            _config.STM32CLIPath = CLI_PATH;
                            SaveConfig(_config);
                            AppendLog("STM32 CLI path set manually.");
                        }
                        else
                        {
                            MessageBox.Show("STM32CubeProgrammer CLI not found. Use Menu > Set CLI Path to configure it.");
                        }
                    }
                }
            }

            InitializeFirmwareRows();
            UpdateStorageIndicator();
        }

        private void InitializeComponents()
        {
            Text = "TARA-UAV Firmware Uploader";
            ClientSize = new Size(1200, 750);
            Font = new Font("Segoe UI", 9F);
            this.BackColor = darkBack;
            this.Padding = new Padding(10);

            try { this.Icon = new Icon("logo.ico"); }
            catch { }

            var lblTitle = new Label
            {
                Text = "TARA-UAV Firmware Uploader",
                Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
                ForeColor = darkAccent,
                Location = new Point(20, 12),
                AutoSize = true,
                Name = "lblTitle"
            };
            Controls.Add(lblTitle);

            var lblSub = new Label
            {
                Text = " ",
                Font = new Font("Segoe UI", 9F),
                ForeColor = darkSubtle,
                Location = new Point(24, 46),
                AutoSize = true,
                Name = "lblSub"
            };
            Controls.Add(lblSub);

            btnConnect = new Button { Text = "Connect", Location = new Point(920, 12), Size = new Size(100, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat, BackColor = darkPanel, ForeColor = darkAccent };
            btnConnect.Click += BtnConnect_Click;
            Controls.Add(btnConnect);

            btnMenu = new Button
            {
                Text = "☰",
                Location = new Point(872, 12),
                Size = new Size(40, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = darkPanel,
                ForeColor = darkAccent
            };
            btnMenu.Click += BtnMenu_Click;
            Controls.Add(btnMenu);
            InitializeMenu();

            pnlConnectionIndicator = new Panel { Size = new Size(16, 16), Location = new Point(1030, 18), BackColor = Color.DarkRed, Anchor = AnchorStyles.Top | AnchorStyles.Right, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(pnlConnectionIndicator);

            lblConnectionText = new Label { Text = "Disconnected", ForeColor = darkText, Location = new Point(1055, 16), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(lblConnectionText);

            dgvFiles = new DataGridView
            {
                Location = new Point(20, 220),
                Size = new Size(640, 222),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowDrop = false
            };

            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "FileName", HeaderText = "File Name", ReadOnly = true });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "Address", Width = 120 });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "Size (bytes)", ReadOnly = true, Width = 120 });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", ReadOnly = true, Width = 120 });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColorTag", HeaderText = "ColorTag", ReadOnly = true, Visible = false });
            dgvFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "FullPath", HeaderText = "FullPath", ReadOnly = true, Visible = false });
            Controls.Add(dgvFiles);

            grpFirmware = new GroupBox
            {
                Text = "Firmware Selection",
                Location = new Point(20, 92),
                Size = new Size(640, 115),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = isDarkTheme ? darkPanel : lightPanel,
                ForeColor = isDarkTheme ? darkText : lightText
            };

            txtBootloaderPath = new TextBox { Location = new Point(110, 24), Size = new Size(420, 23), ReadOnly = true, BackColor = isDarkTheme ? darkBack : Color.White, ForeColor = isDarkTheme ? darkText : lightText, BorderStyle = BorderStyle.FixedSingle };
            txtMetadataPath = new TextBox { Location = new Point(110, 52), Size = new Size(420, 23), ReadOnly = true, BackColor = isDarkTheme ? darkBack : Color.White, ForeColor = isDarkTheme ? darkText : lightText, BorderStyle = BorderStyle.FixedSingle };
            txtFirmwarePath = new TextBox { Location = new Point(110, 80), Size = new Size(420, 23), ReadOnly = true, BackColor = isDarkTheme ? darkBack : Color.White, ForeColor = isDarkTheme ? darkText : lightText, BorderStyle = BorderStyle.FixedSingle };

            var lblBoot = new Label { Text = "Bootloader:", Location = new Point(12, 27), AutoSize = true, ForeColor = isDarkTheme ? darkText : lightText };
            var lblMeta = new Label { Text = "Metadata:", Location = new Point(12, 55), AutoSize = true, ForeColor = isDarkTheme ? darkText : lightText };
            var lblFw = new Label { Text = "Firmware:", Location = new Point(12, 83), AutoSize = true, ForeColor = isDarkTheme ? darkText : lightText };

            btnBrowseBootloader = new Button { Text = "Browse", Location = new Point(540, 22), Size = new Size(80, 25), FlatStyle = FlatStyle.Flat, BackColor = darkBack, ForeColor = darkAccent };
            btnBrowseMetadata = new Button { Text = "Browse", Location = new Point(540, 50), Size = new Size(80, 25), FlatStyle = FlatStyle.Flat, BackColor = darkBack, ForeColor = darkAccent };
            btnBrowseFirmware = new Button { Text = "Browse", Location = new Point(540, 78), Size = new Size(80, 25), FlatStyle = FlatStyle.Flat, BackColor = darkBack, ForeColor = darkAccent };

            btnBrowseBootloader.Click += (s, e) => SelectSingleFirmwareFile(0);
            btnBrowseMetadata.Click += (s, e) => SelectSingleFirmwareFile(1);
            btnBrowseFirmware.Click += (s, e) => SelectSingleFirmwareFile(2);

            grpFirmware.Controls.AddRange(new Control[]
            {
                lblBoot, txtBootloaderPath, btnBrowseBootloader,
                lblMeta, txtMetadataPath, btnBrowseMetadata,
                lblFw, txtFirmwarePath, btnBrowseFirmware
            });
            Controls.Add(grpFirmware);

            btnUploadAll = new Button { Text = "Upload", Location = new Point(20, 460), Size = new Size(120, 32), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, FlatStyle = FlatStyle.Flat, BackColor = darkPanel, ForeColor = darkAccent, Enabled = false };
            btnUploadAll.Click += BtnUploadAll_Click;
            Controls.Add(btnUploadAll);

            progressBar = new ProgressBar { Location = new Point(680, 92), Size = new Size(500, 20), Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = darkPanel, ForeColor = darkAccent };
            Controls.Add(progressBar);

            lblProgressInfo = new Label { Text = "Idle", Location = new Point(680, 120), ForeColor = darkAccent, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(lblProgressInfo);

            txtLog = new TextBox { Location = new Point(680, 145), Size = new Size(500, 315), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom, BackColor = darkPanel, ForeColor = darkText, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(txtLog);

            ApplyThemeDefaults();
            InitializeStorageIndicator();
        }

        private void ApplyThemeDefaults()
        {
            var buttonList = new[] { btnUploadAll, btnConnect, btnMenu };
            foreach (var btn in buttonList)
            {
                if (btn == null) continue;
                btn.BackColor = isDarkTheme ? darkPanel : lightPanel;
                btn.ForeColor = isDarkTheme ? darkAccent : lightAccent;
                btn.FlatStyle = FlatStyle.Flat;
            }

            dgvFiles.EnableHeadersVisualStyles = false;

            if (isDarkTheme)
            {
                this.BackColor = darkBack;
                dgvFiles.BackgroundColor = darkBack;
                dgvFiles.ColumnHeadersDefaultCellStyle.BackColor = darkPanel;
                dgvFiles.ColumnHeadersDefaultCellStyle.ForeColor = darkAccent;
                dgvFiles.RowsDefaultCellStyle.BackColor = darkBack;
                dgvFiles.RowsDefaultCellStyle.ForeColor = darkText;
                dgvFiles.RowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 95, 120);
                dgvFiles.RowsDefaultCellStyle.SelectionForeColor = darkAccent;
                dgvFiles.GridColor = darkGrid;
                txtLog.BackColor = darkPanel;
                txtLog.ForeColor = darkText;
                lblConnectionText.ForeColor = darkText;
                lblProgressInfo.ForeColor = darkAccent;
                if (grpFirmware != null)
                {
                    grpFirmware.BackColor = darkPanel;
                    grpFirmware.ForeColor = darkText;
                    txtBootloaderPath.BackColor = darkBack; txtBootloaderPath.ForeColor = darkText;
                    txtMetadataPath.BackColor = darkBack; txtMetadataPath.ForeColor = darkText;
                    txtFirmwarePath.BackColor = darkBack; txtFirmwarePath.ForeColor = darkText;
                    btnBrowseBootloader.BackColor = darkPanel; btnBrowseBootloader.ForeColor = darkAccent;
                    btnBrowseMetadata.BackColor = darkPanel; btnBrowseMetadata.ForeColor = darkAccent;
                    btnBrowseFirmware.BackColor = darkPanel; btnBrowseFirmware.ForeColor = darkAccent;
                }
                var lblTitle = Controls.Find("lblTitle", true).FirstOrDefault() as Label;
                if (lblTitle != null) lblTitle.ForeColor = darkAccent;
                var lblSub = Controls.Find("lblSub", true).FirstOrDefault() as Label;
                if (lblSub != null) lblSub.ForeColor = darkSubtle;
            }
            else
            {
                Color softBack = Color.FromArgb(238, 243, 250);
                Color softPanel = Color.FromArgb(220, 230, 245);
                Color softAccent = Color.FromArgb(60, 140, 220);
                Color softGrid = Color.FromArgb(200, 210, 230);
                Color softText = Color.FromArgb(40, 50, 70);
                Color softSubtle = Color.FromArgb(120, 130, 160);
                this.BackColor = softBack;
                dgvFiles.BackgroundColor = softBack;
                dgvFiles.ColumnHeadersDefaultCellStyle.BackColor = softPanel;
                dgvFiles.ColumnHeadersDefaultCellStyle.ForeColor = softAccent;
                dgvFiles.RowsDefaultCellStyle.BackColor = softBack;
                dgvFiles.RowsDefaultCellStyle.ForeColor = softText;
                dgvFiles.RowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 230, 255);
                dgvFiles.RowsDefaultCellStyle.SelectionForeColor = softAccent;
                dgvFiles.GridColor = softGrid;
                txtLog.BackColor = softPanel;
                txtLog.ForeColor = softText;
                lblConnectionText.ForeColor = softText;
                lblProgressInfo.ForeColor = softAccent;
                if (grpFirmware != null)
                {
                    grpFirmware.BackColor = softPanel;
                    grpFirmware.ForeColor = softText;
                    txtBootloaderPath.BackColor = Color.White; txtBootloaderPath.ForeColor = softText;
                    txtMetadataPath.BackColor = Color.White; txtMetadataPath.ForeColor = softText;
                    txtFirmwarePath.BackColor = Color.White; txtFirmwarePath.ForeColor = softText;
                    btnBrowseBootloader.BackColor = softPanel; btnBrowseBootloader.ForeColor = softAccent;
                    btnBrowseMetadata.BackColor = softPanel; btnBrowseMetadata.ForeColor = softAccent;
                    btnBrowseFirmware.BackColor = softPanel; btnBrowseFirmware.ForeColor = softAccent;
                }
                var lblTitle = Controls.Find("lblTitle", true).FirstOrDefault() as Label;
                if (lblTitle != null) lblTitle.ForeColor = softAccent;
                var lblSub = Controls.Find("lblSub", true).FirstOrDefault() as Label;
                if (lblSub != null) lblSub.ForeColor = softSubtle;
            }

            dgvFiles.BorderStyle = BorderStyle.None;
            dgvFiles.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvFiles.ColumnHeadersHeight = 30;
        }

        private void InitializeMenu()
        {
            menuStrip = new ContextMenuStrip();

            var toggleThemeItem = new ToolStripMenuItem("Toggle Theme");
            toggleThemeItem.Click += BtnToggleTheme_Click;

            var changePasswordItem = new ToolStripMenuItem("Change Upload Password");
            changePasswordItem.Click += BtnChangeUploadPassword_Click;

            var changeAdminPasswordItem = new ToolStripMenuItem("Change Admin Password");
            changeAdminPasswordItem.Click += BtnChangeAdminPassword_Click;

            var setCliPathItem = new ToolStripMenuItem("Set CLI Path");
            setCliPathItem.Click += BtnSetCliPath_Click;

            menuStrip.Items.Add(toggleThemeItem);
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(changePasswordItem);
            menuStrip.Items.Add(changeAdminPasswordItem);
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(setCliPathItem);
        }

        private void BtnMenu_Click(object sender, EventArgs e)
        {
            if (menuStrip == null)
                InitializeMenu();
            menuStrip.Show(btnMenu, new Point(0, btnMenu.Height));
        }

        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            await DetectStLinkAsync();
        }

        private async Task DetectStLinkAsync()
        {
            SetConnectionState(false, "Scanning...");
            AppendLog("Running STM32_Programmer_CLI -l to detect ST-LINK...");
            if (string.IsNullOrEmpty(CLI_PATH) || !File.Exists(CLI_PATH)) { AppendLog($"CLI not found at {CLI_PATH}"); SetConnectionState(false, "CLI Not Found"); return; }

            var psi = new ProcessStartInfo { FileName = CLI_PATH, Arguments = "-l", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            try
            {
                using (var proc = Process.Start(psi))
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    var err = await proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit();
                    AppendCliOutput(output);
                    if (!string.IsNullOrEmpty(err)) AppendCliOutput("[ERR] " + err);

                    bool connected = false;
                    if (proc.ExitCode == 0)
                    {
                        connected = Regex.IsMatch(output, @"ST-LINK\s+SN\s*:", RegexOptions.IgnoreCase);
                    }
                    else if (output.IndexOf("No ST-LINK detected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             output.IndexOf("No STLink detected", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AppendLog("No ST-LINK detected. Please check connection and drivers.");
                    }
                    else if (!string.IsNullOrEmpty(err))
                    {
                        AppendLog("Error from CLI: " + err);
                    }
                    SetConnectionState(connected, connected ? "Connected" : "Disconnected");
                }
            }
            catch (Exception ex) { AppendLog("Error during detection: " + ex.Message); SetConnectionState(false, "Error"); }
        }

        private void SetConnectionState(bool connected, string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetConnectionState(connected, text))); return; }
            pnlConnectionIndicator.BackColor = connected ? Color.LimeGreen : Color.DarkRed;
            lblConnectionText.Text = text;
            btnUploadAll.Enabled = connected;
            btnConnect.Text = connected ? "Connected" : "Connect";
        }

        private void LoadFixedFirmwareSet(bool showUiErrors)
        {
            // Use configured firmware folder, or ask if not set.
            string folder = _config.FirmwareFolderPath;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                AppendLog("Firmware folder not configured or missing. Prompting for folder selection.");
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select the firmware folder containing bootloader, metadata, and firmware files";
                    if (fbd.ShowDialog(this) != DialogResult.OK)
                        return;

                    folder = fbd.SelectedPath;
                    _config.FirmwareFolderPath = folder;
                    SaveConfig(_config);
                    AppendLog($"Firmware folder set to: {folder}");
                }
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bin", ".hex" };
            var files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => allowed.Contains(Path.GetExtension(p)))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count < 3)
            {
                var msg = $"Expected at least 3 firmware files in:{Environment.NewLine}{folder}{Environment.NewLine}Found: {files.Count}";
                AppendLog(msg);
                if (showUiErrors) MessageBox.Show(this, msg, "Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dgvFiles.Rows.Clear();
                UpdateStorageIndicator();
                return;
            }

            var selected = SelectOrderedFixedFirmwareFiles(files);
            if (selected.Count != 3)
            {
                var msg = $"Could not identify a bootloader/metadata/firmware set in:{Environment.NewLine}{folder}";
                AppendLog(msg);
                if (showUiErrors) MessageBox.Show(this, msg, "Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dgvFiles.Rows.Clear();
                UpdateStorageIndicator();
                return;
            }

            LoadFirmwareFiles(selected, "Firmware folder", copyIntoAppFolder: true);
        }

        private static uint ParseHexAddressOrDefault(string address, uint fallback)
        {
            if (string.IsNullOrWhiteSpace(address))
                return fallback;
            var s = address.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static List<string> SelectOrderedFixedFirmwareFiles(List<string> allFilesSortedByName)
        {
            if (allFilesSortedByName == null)
                return new List<string>();

            var remaining = allFilesSortedByName
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var boot = TakeFirstMatch(remaining, BootloaderNameTokens);
            var meta = TakeFirstMatch(remaining, MetadataNameTokens);
            var fw = TakeFirstMatch(remaining, FirmwareNameTokens);

            if (boot == null && remaining.Count > 0) { boot = remaining[0]; remaining.RemoveAt(0); }
            if (meta == null && remaining.Count > 0) { meta = remaining[0]; remaining.RemoveAt(0); }
            if (fw == null && remaining.Count > 0) { fw = remaining[0]; remaining.RemoveAt(0); }

            var selected = new List<string>();
            if (!string.IsNullOrWhiteSpace(boot)) selected.Add(boot);
            if (!string.IsNullOrWhiteSpace(meta)) selected.Add(meta);
            if (!string.IsNullOrWhiteSpace(fw)) selected.Add(fw);
            return selected;
        }

        private static string TakeFirstMatch(List<string> remaining, string[] tokens)
        {
            if (remaining == null || remaining.Count == 0 || tokens == null || tokens.Length == 0)
                return null;

            for (int i = 0; i < remaining.Count; i++)
            {
                var name = (Path.GetFileNameWithoutExtension(remaining[i]) ?? string.Empty).ToLowerInvariant();
                if (tokens.Any(t => name.Contains(t)))
                {
                    var match = remaining[i];
                    remaining.RemoveAt(i);
                    return match;
                }
            }
            return null;
        }

        private void InitializeFirmwareRows()
        {
            dgvFiles.Rows.Clear();
            uint baseAddr = ParseHexAddressOrDefault(DefaultAddress, FlashBaseAddress);

            for (int i = 0; i < 3; i++)
            {
                var rowIndex = dgvFiles.Rows.Add();
                var row = dgvFiles.Rows[rowIndex];
                row.Tag = i;
                row.Cells["FileName"].Value = string.Empty;
                row.Cells["FullPath"].Value = string.Empty;
                row.Cells["Address"].Value = $"0x{unchecked(baseAddr + (uint)i * ChunkSize):X8}";
                row.Cells["Size"].Value = string.Empty;
                row.Cells["Status"].Value = "Not Selected";
                var color = ColorFromHsl(200 + (rowIndex * 10) % 60, 60, 55);
                row.Cells["ColorTag"].Value = color.ToArgb().ToString();
                row.DefaultCellStyle.BackColor = Color.FromArgb(28, 28, 36);
                row.DefaultCellStyle.ForeColor = Color.White;
            }

            txtBootloaderPath.Text = string.Empty;
            txtMetadataPath.Text = string.Empty;
            txtFirmwarePath.Text = string.Empty;
            dgvFiles.Invalidate();
        }

        private void SetFirmwareSlot(int slotIndex, string originalPath)
        {
            if (slotIndex < 0 || slotIndex > 2)
                return;
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
                return;

            var roleNames = new[] { "bootloader", "metadata", "firmware" };
            var cachedPath = CopyFirmwareFileToAppFolder(originalPath, roleNames[slotIndex]);
            var fi = new FileInfo(cachedPath);

            if (slotIndex == 0) txtBootloaderPath.Text = originalPath;
            else if (slotIndex == 1) txtMetadataPath.Text = originalPath;
            else txtFirmwarePath.Text = originalPath;

            while (dgvFiles.Rows.Count < 3)
                dgvFiles.Rows.Add();

            var row = dgvFiles.Rows[slotIndex];
            row.Tag = slotIndex;
            row.Cells["FileName"].Value = Path.GetFileName(originalPath);
            row.Cells["FullPath"].Value = cachedPath;
            row.Cells["Size"].Value = fi.Length.ToString();
            row.Cells["Status"].Value = "Pending";

            dgvFiles.Invalidate();
            UpdateStorageIndicator();
        }

        private void LoadFirmwareFiles(List<string> orderedBootMetaFirmwareFiles, string sourceLabel, bool copyIntoAppFolder)
        {
            if (orderedBootMetaFirmwareFiles == null || orderedBootMetaFirmwareFiles.Count != 3)
                return;

            dgvFiles.Rows.Clear();
            uint baseAddr = ParseHexAddressOrDefault(DefaultAddress, FlashBaseAddress);
            var roleNames = new[] { "bootloader", "metadata", "firmware" };
            AppendLog($"{sourceLabel} flash order:");

            for (int i = 0; i < orderedBootMetaFirmwareFiles.Count; i++)
            {
                var originalPath = orderedBootMetaFirmwareFiles[i];
                var effectivePath = originalPath;

                if (copyIntoAppFolder)
                {
                    try
                    {
                        effectivePath = CopyFirmwareFileToAppFolder(originalPath, roleNames[i]);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Copy failed for {Path.GetFileName(originalPath)}: {ex.Message}");
                        MessageBox.Show(this, $"Failed to copy {Path.GetFileName(originalPath)} into app folder:{Environment.NewLine}{ex.Message}", "Copy Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                var fi = new FileInfo(effectivePath);
                var rowIndex = dgvFiles.Rows.Add();
                var row = dgvFiles.Rows[rowIndex];

                row.Cells["FileName"].Value = Path.GetFileName(originalPath);
                row.Cells["FullPath"].Value = effectivePath;
                row.Cells["Address"].Value = $"0x{unchecked(baseAddr + (uint)i * ChunkSize):X8}";
                row.Cells["Size"].Value = fi.Length.ToString();
                row.Cells["Status"].Value = "Pending";

                var color = ColorFromHsl(200 + (rowIndex * 10) % 60, 60, 55);
                row.Cells["ColorTag"].Value = color.ToArgb().ToString();
                row.DefaultCellStyle.BackColor = Color.FromArgb(28, 28, 36);
                row.DefaultCellStyle.ForeColor = Color.White;

                AppendLog($"  {roleNames[i],-9}: {Path.GetFileName(originalPath)} -> {Path.GetFileName(effectivePath)} @ {row.Cells["Address"].Value}");
            }

            dgvFiles.Invalidate();
            UpdateStorageIndicator();
        }

        private string GetFirmwareCacheDirectory()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CUAVFlasher", "firmware");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string CopyFirmwareFileToAppFolder(string sourcePath, string roleName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found.", sourcePath);

            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".bin";

            var safeRole = string.IsNullOrWhiteSpace(roleName) ? "image" : roleName.Trim().ToLowerInvariant();
            var destFileName = safeRole + ext.ToLowerInvariant();
            var destPath = Path.Combine(GetFirmwareCacheDirectory(), destFileName);

            var srcFull = Path.GetFullPath(sourcePath);
            var destFull = Path.GetFullPath(destPath);
            if (string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase))
                return destPath;

            File.Copy(sourcePath, destPath, overwrite: true);
            return destPath;
        }

        private void BtnToggleTheme_Click(object sender, EventArgs e)
        {
            isDarkTheme = !isDarkTheme;
            _config.DarkTheme = isDarkTheme;
            SaveConfig(_config);
            ApplyThemeDefaults();
            UpdateStorageTheme();
        }

        private void UpdateStorageTheme()
        {
            if (storagePanel != null)
            {
                storagePanel.BackColor = isDarkTheme ? darkPanel : lightPanel;
                storageLabel.ForeColor = isDarkTheme ? darkText : lightText;
                storageInfo.ForeColor = isDarkTheme ? darkSubtle : lightSubtle;
            }
            if (storageVisualizer != null)
            {
                storageVisualizer.BackColor = isDarkTheme ? darkBack : Color.FromArgb(250, 252, 255);
                storageVisualizer.ThemeIsDark = isDarkTheme;
                storageVisualizer.Invalidate();
            }
        }

        private async void BtnUploadAll_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(CLI_PATH) || !File.Exists(CLI_PATH))
            {
                MessageBox.Show(this, "STM32 CLI not found. Use Menu > Set CLI Path to configure it.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var rows = dgvFiles.Rows.Cast<DataGridViewRow>().ToList();
            var pending = new List<DataGridViewRow>();
            foreach (var r in rows)
            {
                var fileName = r.Cells["FileName"].Value?.ToString() ?? string.Empty;
                var address = r.Cells["Address"].Value?.ToString() ?? DefaultAddress;
                var fullPath = r.Cells["FullPath"].Value?.ToString();

                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                {
                    fullPath = FindFullPathForFile(fileName);
                    if (!string.IsNullOrEmpty(fullPath)) r.Cells["FullPath"].Value = fullPath;
                }

                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) { r.Cells["Status"].Value = "Missing"; AppendLog($"File missing: {fileName}"); continue; }
                if (!Regex.IsMatch(address, "^0x[0-9A-Fa-f]+$")) { r.Cells["Status"].Value = "Invalid Address"; AppendLog($"Invalid address for {fileName}: {address}"); continue; }
                pending.Add(r);
            }

            if (pending.Count == 0) { MessageBox.Show(this, "No valid pending files.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            pending = pending
                .OrderBy(r => ParseHexAddressOrDefault(r.Cells["Address"].Value?.ToString(), 0))
                .ToList();

            var roleNames = new[] { "bootloader", "metadata", "firmware" };
            uint baseAddr = ParseHexAddressOrDefault(DefaultAddress, FlashBaseAddress);
            for (int i = 0; i < pending.Count; i++)
            {
                var fullPath = pending[i].Cells["FullPath"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                    continue;

                try
                {
                    var addr = ParseHexAddressOrDefault(pending[i].Cells["Address"].Value?.ToString(), baseAddr);
                    int slot = -1;
                    if (addr >= baseAddr)
                    {
                        var offset = addr - baseAddr;
                        if (ChunkSize != 0 && (offset % ChunkSize) == 0)
                            slot = (int)(offset / ChunkSize);
                    }
                    var role = (slot >= 0 && slot < roleNames.Length) ? roleNames[slot] : "image";
                    var cached = CopyFirmwareFileToAppFolder(fullPath, role);
                    pending[i].Cells["FullPath"].Value = cached;
                }
                catch (Exception ex) { AppendLog($"Copy-to-cache failed for {Path.GetFileName(fullPath)}: {ex.Message}"); }
            }

            if (!VerifyUploadPassword())
            {
                AppendLog("Upload cancelled (password not verified).");
                return;
            }

            DisableControlsDuringFlash(true);
            progressBar.Minimum = 0; progressBar.Maximum = pending.Count; progressBar.Value = 0; lblProgressInfo.Text = "Starting...";
            int completed = 0; int successCount = 0;
            foreach (var r in pending)
            {
                var fileName = r.Cells["FileName"].Value.ToString();
                var addr = r.Cells["Address"].Value.ToString();
                var fullPath = r.Cells["FullPath"].Value?.ToString();
                r.Cells["Status"].Value = "Flashing";
                lblProgressInfo.Text = $"Flashing {fileName} ({completed + 1}/{pending.Count})";
                try
                {
                    var ok = await FlashFileAsync(fullPath, addr);
                    r.Cells["Status"].Value = ok ? "Success" : "Failed";
                    if (ok) successCount++;
                }
                catch (Exception ex) { r.Cells["Status"].Value = "Failed"; AppendLog($"Error: {ex.Message}"); }
                completed++; progressBar.Value = completed;
            }
            DisableControlsDuringFlash(false);
            lblProgressInfo.Text = successCount == pending.Count ? "SUCCESS" : (successCount == 0 ? "FAILED" : "PARTIAL FAILURE");
            AppendLog($"Flashing complete. Success: {successCount}, Total: {pending.Count}");
            ShowNeonResultDialog("Flash Result", lblProgressInfo.Text, successCount == pending.Count ? NeonDialogKind.Success : (successCount == 0 ? NeonDialogKind.Error : NeonDialogKind.Warning));
        }

        private void DisableControlsDuringFlash(bool disable)
        {
            btnUploadAll.Enabled = !disable;
            if (btnMenu != null) btnMenu.Enabled = !disable;
            if (btnBrowseBootloader != null) btnBrowseBootloader.Enabled = !disable;
            if (btnBrowseMetadata != null) btnBrowseMetadata.Enabled = !disable;
            if (btnBrowseFirmware != null) btnBrowseFirmware.Enabled = !disable;
        }

        private void SelectSingleFirmwareFile(int slotIndex)
        {
            using (var ofd = new OpenFileDialog())
            {
                string slotName = slotIndex == 0 ? "bootloader" : (slotIndex == 1 ? "metadata" : "firmware");
                ofd.Title = $"Select {slotName} file";
                ofd.Filter = "Firmware files (*.bin;*.hex)|*.bin;*.hex|All files (*.*)|*.*";
                ofd.Multiselect = false;
                if (ofd.ShowDialog(this) != DialogResult.OK)
                    return;
                if (string.IsNullOrWhiteSpace(ofd.FileName) || !File.Exists(ofd.FileName))
                    return;
                SetFirmwareSlot(slotIndex, ofd.FileName);
            }
        }

        private void BtnSetFirmwareFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select the folder containing bootloader, metadata, and firmware files";
                fbd.SelectedPath = _config.FirmwareFolderPath ?? string.Empty;
                if (fbd.ShowDialog(this) != DialogResult.OK)
                    return;
                _config.FirmwareFolderPath = fbd.SelectedPath;
                SaveConfig(_config);
                AppendLog($"Firmware folder set to: {fbd.SelectedPath}");
                ShowNeonResultDialog("Folder Set", $"Firmware folder configured:\n{fbd.SelectedPath}", NeonDialogKind.Success);
            }
        }

        private void BtnSetCliPath_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select STM32_Programmer_CLI.exe";
                ofd.Filter = "Executable (*.exe)|*.exe";
                if (!string.IsNullOrEmpty(CLI_PATH)) ofd.InitialDirectory = Path.GetDirectoryName(CLI_PATH);
                if (ofd.ShowDialog(this) != DialogResult.OK)
                    return;
                CLI_PATH = ofd.FileName;
                _config.STM32CLIPath = CLI_PATH;
                SaveConfig(_config);
                AppendLog($"CLI path updated to: {CLI_PATH}");
            }
        }

        private void BtnChangeUploadPassword_Click(object sender, EventArgs e)
        {
            try
            {
                EnsurePasswordFileExists();

                bool adminOk = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var admin = PromptForPassword(attempt == 0 ? "Enter admin password" : "Incorrect admin password. Try again");
                    if (admin == null)
                        return;
                    if (VerifyAdminPassword(admin))
                    {
                        adminOk = true;
                        break;
                    }
                }

                if (!adminOk)
                {
                    ShowNeonResultDialog("Access Denied", "Admin password verification failed.", NeonDialogKind.Error);
                    return;
                }

                var newPassword = PromptForPassword("Enter new upload password");
                if (newPassword == null) return;

                var confirm = PromptForPassword("Confirm new upload password");
                if (confirm == null) return;

                if (!string.Equals(newPassword, confirm, StringComparison.Ordinal))
                {
                    ShowNeonResultDialog("Error", "Passwords do not match.", NeonDialogKind.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(newPassword))
                {
                    ShowNeonResultDialog("Error", "Password cannot be empty.", NeonDialogKind.Warning);
                    return;
                }

                SetPassword(newPassword);
                passwordVerifiedThisSession = false;
                AppendLog("Upload password changed successfully.");
                ShowNeonResultDialog("Success", "Upload password changed.", NeonDialogKind.Success);
            }
            catch (Exception ex)
            {
                AppendLog("Change password error: " + ex.Message);
                ShowNeonResultDialog("Error", "Failed to change password:\r\n" + ex.Message, NeonDialogKind.Error);
            }
        }

        private void BtnChangeAdminPassword_Click(object sender, EventArgs e)
        {
            try
            {
                bool adminOk = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var current = PromptForPassword(attempt == 0 ? "Enter current admin password" : "Incorrect. Try again");
                    if (current == null) return;
                    if (VerifyAdminPassword(current)) { adminOk = true; break; }
                }

                if (!adminOk)
                {
                    ShowNeonResultDialog("Access Denied", "Current admin password incorrect.", NeonDialogKind.Error);
                    return;
                }

                var newPw = PromptForPassword("Enter new admin password");
                if (newPw == null) return;

                var confirm = PromptForPassword("Confirm new admin password");
                if (confirm == null) return;

                if (!string.Equals(newPw, confirm, StringComparison.Ordinal))
                {
                    ShowNeonResultDialog("Error", "Passwords do not match.", NeonDialogKind.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(newPw))
                {
                    ShowNeonResultDialog("Error", "Password cannot be empty.", NeonDialogKind.Warning);
                    return;
                }

                SetAdminPassword(newPw);
                AppendLog("Admin password changed successfully.");
                ShowNeonResultDialog("Success", "Admin password changed.", NeonDialogKind.Success);
            }
            catch (Exception ex)
            {
                AppendLog("Change admin password error: " + ex.Message);
                ShowNeonResultDialog("Error", "Failed to change admin password:\r\n" + ex.Message, NeonDialogKind.Error);
            }
        }

        private string FindFullPathForFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            try
            {
                var cacheDir = GetFirmwareCacheDirectory();
                var cached = Directory.GetFiles(cacheDir, fileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrEmpty(cached))
                    return cached;
            }
            catch (Exception ex) { AppendLog($"Cache search error: {ex.Message}"); }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Directory.GetFiles(baseDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (Exception ex)
            {
                AppendLog($"BaseDir search error: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> FlashFileAsync(string filePath, string address)
        {
            string args = $"-c port=SWD -d \"{filePath}\" {address} -v";
            AppendLog($"{DateTime.Now:HH:mm:ss} Executing: {CLI_PATH} {args}");
            var psi = new ProcessStartInfo { FileName = CLI_PATH, Arguments = args, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<int>();
                proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendCliOutput(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendCliOutput("[ERR] " + e.Data); };
                proc.Exited += (s, e) => { try { tcs.TrySetResult(proc.ExitCode); } catch (Exception ex) { tcs.TrySetException(ex); } };
                try
                {
                    if (!proc.Start()) { AppendLog("Failed to start CLI."); return false; }
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    var exit = await tcs.Task.ConfigureAwait(false);
                    AppendLog($"Process exited with code {exit}");
                    return exit == 0;
                }
                catch (Exception ex) { AppendLog("Process error: " + ex.Message); return false; }
            }
        }

        private void AppendCliOutput(string output) { AppendLog("[CLI] " + output); }
        private void AppendLog(string text) { if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(text))); return; } txtLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}"); }

        private static Color ColorFromHsl(int h, int s, int l)
        {
            double H = h / 360.0, S = s / 100.0, L = l / 100.0;
            double r = 0, g = 0, b = 0;
            if (S == 0) { r = g = b = L; }
            else
            {
                Func<double, double, double, double> HueToRgb = (pp, qq, tt) =>
                {
                    if (tt < 0) tt += 1;
                    if (tt > 1) tt -= 1;
                    if (tt < 1.0 / 6) return pp + (qq - pp) * 6 * tt;
                    if (tt < 1.0 / 2) return qq;
                    if (tt < 2.0 / 3) return pp + (qq - pp) * (2.0 / 3 - tt) * 6;
                    return pp;
                };
                double q = L < 0.5 ? L * (1 + S) : L + S - L * S;
                double p = 2 * L - q;
                r = HueToRgb(p, q, H + 1.0 / 3);
                g = HueToRgb(p, q, H);
                b = HueToRgb(p, q, H - 1.0 / 3);
            }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        private void InitializeStorageIndicator()
        {
            storagePanel = new Panel
            {
                Location = new Point(20, 500),
                Size = new Size(640, 190),
                BackColor = isDarkTheme ? darkPanel : lightPanel,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            storageLabel = new Label
            {
                Text = "Storage Usage & Address Mapping:",
                Location = new Point(10, 10),
                AutoSize = true,
                ForeColor = isDarkTheme ? darkText : lightText,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            storagePanel.Controls.Add(storageLabel);

            storageVisualizer = new StorageVisualizerControl
            {
                Location = new Point(10, 35),
                Size = new Size(620, 80),
                BackColor = isDarkTheme ? darkBack : Color.FromArgb(250, 252, 255),
                ThemeIsDark = isDarkTheme,
                DarkThemeColors = new[] { darkBack, darkText },
                LightThemeColors = new[] { Color.FromArgb(250, 252, 255), lightText }
            };
            storagePanel.Controls.Add(storageVisualizer);

            storageInfo = new Label
            {
                Location = new Point(10, 128),
                AutoSize = false,
                Size = new Size(620, 52),
                ForeColor = isDarkTheme ? darkSubtle : lightSubtle,
                Font = new Font("Segoe UI", 8F),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            storagePanel.Controls.Add(storageInfo);
            Controls.Add(storagePanel);
        }

        private void UpdateStorageIndicator()
        {
            var fileSegments = new List<StorageSegment>();
            long totalSize = 0;

            foreach (DataGridViewRow row in dgvFiles.Rows)
            {
                if (row.Cells["Size"].Value != null &&
                    long.TryParse(row.Cells["Size"].Value.ToString(), out var sz) &&
                    row.Cells["Address"].Value != null &&
                    row.Cells["FileName"].Value != null)
                {
                    var addressStr = row.Cells["Address"].Value.ToString();
                    var fileName = row.Cells["FileName"].Value.ToString();
                    var colorArgb = row.Cells["ColorTag"].Value?.ToString();

                    Color segmentColor = Color.CornflowerBlue;
                    if (!string.IsNullOrEmpty(colorArgb) && int.TryParse(colorArgb, out var argb))
                        segmentColor = Color.FromArgb(argb);

                    var addressStart = ParseHexAddressOrDefault(addressStr, FlashBaseAddress);
                    uint addressEnd = addressStart + (uint)sz;

                    fileSegments.Add(new StorageSegment
                    {
                        FileName = fileName,
                        Size = sz,
                        AddressStart = addressStart,
                        AddressEnd = addressEnd,
                        Color = segmentColor
                    });
                    totalSize += sz;
                }
            }

            long maxSize = deviceFlashSize > 0 ? deviceFlashSize : DefaultDeviceFlashSizeBytes;
            var usedBytes = CalculateUsedBytesInFlash(fileSegments, FlashBaseAddress, maxSize);
            var freeBytes = Math.Max(0, maxSize - usedBytes);
            var usedPct = maxSize > 0 ? (usedBytes * 100.0 / maxSize) : 0;

            if (storageVisualizer != null)
            {
                storageVisualizer.SetSegments(fileSegments, FlashBaseAddress, maxSize);
                storageVisualizer.Invalidate();
            }

            storageInfo.Text =
                $"Used: {usedBytes:N0} / {maxSize:N0} bytes ({usedPct:0.0}%)   Free: {freeBytes:N0} bytes" +
                Environment.NewLine +
                $"Files total: {totalSize:N0} bytes   Flash: 0x{FlashBaseAddress:X8} - 0x{unchecked(FlashBaseAddress + (uint)maxSize):X8}";
        }

        private static long CalculateUsedBytesInFlash(List<StorageSegment> segments, uint flashBaseAddress, long flashSizeBytes)
        {
            if (segments == null || segments.Count == 0 || flashSizeBytes <= 0)
                return 0;

            ulong flashStart = flashBaseAddress;
            ulong flashEnd = (ulong)flashBaseAddress + (ulong)flashSizeBytes;

            var ranges = new List<(ulong Start, ulong End)>();
            foreach (var seg in segments)
            {
                ulong start = seg.AddressStart;
                ulong end = seg.AddressEnd;
                if (end <= flashStart || start >= flashEnd) continue;
                if (start < flashStart) start = flashStart;
                if (end > flashEnd) end = flashEnd;
                if (end > start) ranges.Add((start, end));
            }

            if (ranges.Count == 0) return 0;
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

            ulong mergedStart = ranges[0].Start;
            ulong mergedEnd = ranges[0].End;
            ulong total = 0;

            for (int i = 1; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (r.Start <= mergedEnd)
                {
                    if (r.End > mergedEnd) mergedEnd = r.End;
                }
                else
                {
                    total += mergedEnd - mergedStart;
                    mergedStart = r.Start;
                    mergedEnd = r.End;
                }
            }
            total += mergedEnd - mergedStart;
            return total > long.MaxValue ? long.MaxValue : (long)total;
        }

        private void EnsurePasswordFileExists()
        {
            try
            {
                var path = GetPasswordFilePath();
                if (!File.Exists(path))
                    SetPassword(DefaultPassword);
            }
            catch (Exception ex) { AppendLog("Password init error: " + ex.Message); }
        }

        private string GetPasswordFilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CUAVFlasher");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PasswordFileName);
        }

        private bool VerifyUploadPassword()
        {
            if (passwordVerifiedThisSession)
                return true;

            EnsurePasswordFileExists();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                var input = PromptForPassword(attempt == 0 ? "Enter password to upload" : "Incorrect password. Try again");
                if (input == null) return false;
                if (VerifyPassword(input))
                {
                    passwordVerifiedThisSession = true;
                    return true;
                }
            }

            ShowNeonResultDialog("Access Denied", "Password verification failed.", NeonDialogKind.Error);
            return false;
        }

        private static byte[] ComputePasswordHash(byte[] salt, string password)
        {
            using (var sha = SHA256.Create())
            {
                var pwBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
                var data = new byte[salt.Length + pwBytes.Length];
                Buffer.BlockCopy(salt, 0, data, 0, salt.Length);
                Buffer.BlockCopy(pwBytes, 0, data, salt.Length, pwBytes.Length);
                return sha.ComputeHash(data);
            }
        }

        private void SetPassword(string newPassword)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);
            var hash = ComputePasswordHash(salt, newPassword);
            var content = Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
            File.WriteAllText(GetPasswordFilePath(), content);
        }

        private bool VerifyPassword(string input)
        {
            try
            {
                var content = File.ReadAllText(GetPasswordFilePath()).Trim();
                var parts = content.Split(new[] { ':' }, 2);
                if (parts.Length != 2) return false;
                var salt = Convert.FromBase64String(parts[0]);
                var expected = Convert.FromBase64String(parts[1]);
                var actual = ComputePasswordHash(salt, input);
                return FixedTimeEquals(expected, actual);
            }
            catch (Exception ex)
            {
                AppendLog($"Password verify error: {ex.Message}");
                return false;
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private string PromptForPassword(string titleText)
        {
            using (var dlg = new Form())
            using (var lbl = new Label())
            using (var txt = new TextBox())
            using (var hint = new Label())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                dlg.Text = "AUTH";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(420, 170);
                dlg.ShowInTaskbar = false;
                dlg.BackColor = Color.FromArgb(8, 12, 18);
                dlg.Font = new Font("Consolas", 9.5F);

                lbl.Text = titleText;
                lbl.Location = new Point(16, 14);
                lbl.Size = new Size(388, 36);
                lbl.ForeColor = Color.FromArgb(0, 255, 170);
                lbl.Font = new Font("Consolas", 11F, FontStyle.Bold);

                hint.Text = "Input is masked. Press Enter to confirm.";
                hint.Location = new Point(16, 52);
                hint.Size = new Size(388, 16);
                hint.ForeColor = Color.FromArgb(120, 170, 160);

                txt.Location = new Point(16, 76);
                txt.Size = new Size(388, 24);
                txt.UseSystemPasswordChar = true;
                txt.BackColor = Color.FromArgb(4, 10, 14);
                txt.ForeColor = Color.FromArgb(220, 255, 245);
                txt.BorderStyle = BorderStyle.FixedSingle;

                ok.Text = "OK";
                ok.Location = new Point(248, 118);
                ok.Size = new Size(75, 30);
                ok.DialogResult = DialogResult.OK;
                ok.FlatStyle = FlatStyle.Flat;
                ok.BackColor = Color.FromArgb(12, 20, 28);
                ok.ForeColor = Color.FromArgb(0, 255, 170);

                cancel.Text = "Cancel";
                cancel.Location = new Point(329, 118);
                cancel.Size = new Size(75, 30);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.BackColor = Color.FromArgb(12, 20, 28);
                cancel.ForeColor = Color.FromArgb(180, 210, 210);

                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                dlg.Controls.AddRange(new Control[] { lbl, hint, txt, ok, cancel });

                return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }

        private enum NeonDialogKind { Success, Warning, Error, Info }

        private void ShowNeonResultDialog(string title, string message, NeonDialogKind kind)
        {
            using (var dlg = new Form())
            using (var header = new Panel())
            using (var lblTitle = new Label())
            using (var lblBody = new Label())
            using (var btn = new Button())
            {
                dlg.Text = title ?? "Status";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.ClientSize = new Size(520, 220);
                dlg.BackColor = Color.FromArgb(8, 12, 18);
                dlg.Font = new Font("Segoe UI", 9.5F);

                Color accent;
                string badge;
                switch (kind)
                {
                    case NeonDialogKind.Success: accent = Color.FromArgb(0, 255, 170); badge = "SUCCESS"; break;
                    case NeonDialogKind.Warning: accent = Color.FromArgb(255, 190, 60); badge = "WARNING"; break;
                    case NeonDialogKind.Error: accent = Color.FromArgb(255, 80, 110); badge = "ERROR"; break;
                    default: accent = Color.FromArgb(80, 180, 255); badge = "INFO"; break;
                }

                header.Location = new Point(0, 0);
                header.Size = new Size(dlg.ClientSize.Width, 58);
                header.BackColor = Color.FromArgb(12, 18, 26);

                lblTitle.Text = $"{badge} :: {title}";
                lblTitle.Location = new Point(16, 16);
                lblTitle.Size = new Size(dlg.ClientSize.Width - 32, 28);
                lblTitle.ForeColor = accent;
                lblTitle.Font = new Font("Consolas", 12F, FontStyle.Bold);
                header.Controls.Add(lblTitle);

                lblBody.Text = message ?? string.Empty;
                lblBody.Location = new Point(16, 74);
                lblBody.Size = new Size(dlg.ClientSize.Width - 32, 96);
                lblBody.ForeColor = Color.FromArgb(220, 235, 245);
                lblBody.Font = new Font("Segoe UI", 10F);

                btn.Text = "OK";
                btn.Size = new Size(90, 32);
                btn.Location = new Point(dlg.ClientSize.Width - 106, dlg.ClientSize.Height - 48);
                btn.DialogResult = DialogResult.OK;
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = Color.FromArgb(12, 20, 28);
                btn.ForeColor = accent;

                dlg.AcceptButton = btn;
                dlg.Controls.Add(header);
                dlg.Controls.Add(lblBody);
                dlg.Controls.Add(btn);
                dlg.ShowDialog(this);
            }
        }
    }

    public class StorageSegment
    {
        public string FileName { get; set; }
        public long Size { get; set; }
        public uint AddressStart { get; set; }
        public uint AddressEnd { get; set; }
        public Color Color { get; set; }
    }

    public class StorageVisualizerControl : Control
    {
        private const uint DefaultFlashBaseAddress = 0x08000000;
        private const long DefaultFlashSizeBytes = 2L * 1024 * 1024;
        private List<StorageSegment> segments = new List<StorageSegment>();
        private uint flashBaseAddress = DefaultFlashBaseAddress;
        private long flashSizeBytes = DefaultFlashSizeBytes;
        public bool ThemeIsDark { get; set; } = true;
        public Color[] DarkThemeColors { get; set; }
        public Color[] LightThemeColors { get; set; }

        public StorageVisualizerControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        }

        public void SetSegments(List<StorageSegment> newSegments, uint newFlashBaseAddress, long newFlashSizeBytes)
        {
            segments = newSegments ?? new List<StorageSegment>();
            flashBaseAddress = newFlashBaseAddress;
            flashSizeBytes = newFlashSizeBytes > 0 ? newFlashSizeBytes : DefaultFlashSizeBytes;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (segments.Count == 0) { DrawEmptyState(e); return; }
            DrawStorageBar(e);
            DrawLegend(e);
        }

        private void DrawStorageBar(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int barX = 5, barY = 5, barWidth = Width - 10, barHeight = 20;

            using (var bgBrush = new SolidBrush(Color.FromArgb(50, 50, 50)))
                g.FillRectangle(bgBrush, barX, barY, barWidth, barHeight);

            using (var borderPen = new Pen(ThemeIsDark ? Color.FromArgb(100, 100, 100) : Color.FromArgb(200, 200, 200)))
                g.DrawRectangle(borderPen, barX, barY, barWidth, barHeight);

            uint minAddress = flashBaseAddress;
            uint maxAddress = unchecked(flashBaseAddress + (uint)Math.Max(1, flashSizeBytes));
            uint addressRange = maxAddress - minAddress;
            if (addressRange == 0) addressRange = 1;

            var labelColor = ThemeIsDark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(80, 80, 80);
            using (var font = new Font("Segoe UI", 7F))
            using (var labelBrush = new SolidBrush(labelColor))
            {
                g.DrawString($"0x{minAddress:X8}", font, labelBrush, barX, barY - 15);
                g.DrawString($"0x{maxAddress:X8}", font, labelBrush, barX + barWidth - 60, barY - 15);
            }

            const int minVisibleWidth = 2;
            foreach (var segment in segments)
            {
                ulong segStart = segment.AddressStart;
                ulong segEnd = segment.AddressEnd;
                ulong flashStart = minAddress;
                ulong flashEnd = maxAddress;

                if (segEnd <= flashStart || segStart >= flashEnd) continue;

                ulong visibleStart = segStart < flashStart ? flashStart : segStart;
                ulong visibleEnd = segEnd > flashEnd ? flashEnd : segEnd;
                ulong visibleSize = visibleEnd > visibleStart ? (visibleEnd - visibleStart) : 0;
                if (visibleSize == 0) continue;

                ulong offset = visibleStart - flashStart;
                float segmentStartX = barX + (float)(offset * (ulong)barWidth) / (float)addressRange;
                float segmentWidth = (float)(visibleSize * (ulong)barWidth) / (float)addressRange;
                int segmentX = (int)segmentStartX;
                int segmentW = Math.Max(minVisibleWidth, (int)Math.Ceiling(segmentWidth));
                if (segmentX + segmentW > barX + barWidth)
                    segmentW = barX + barWidth - segmentX;

                if (segmentW <= 0) continue;

                using (var brush = new SolidBrush(segment.Color))
                    g.FillRectangle(brush, segmentX, barY, segmentW, barHeight);

                using (var borderPen = new Pen(Color.FromArgb(20, 20, 20)))
                    g.DrawRectangle(borderPen, segmentX, barY, segmentW, barHeight);

                if (segmentW > 80)
                {
                    string addressText = $"0x{segment.AddressStart:X8}-0x{segment.AddressEnd:X8}";
                    using (var addressFont = new Font("Segoe UI", 6F))
                    using (var textBrush = new SolidBrush(Color.White))
                    using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(addressText, addressFont, textBrush, segmentX + segmentW / 2, barY + barHeight / 2, fmt);
                }
            }
        }

        private void DrawLegend(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int legendX = 5, legendY = 30, lineHeight = 22;
            int maxColumnsPerRow = 2;
            int columnWidth = (Width - 10) / maxColumnsPerRow;
            int currentColumn = 0, currentRow = 0;

            var textColor = ThemeIsDark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(50, 50, 50);
            var addressColor = ThemeIsDark ? Color.FromArgb(150, 200, 255) : Color.FromArgb(0, 100, 180);

            using (var font = new Font("Segoe UI", 6.5F))
            using (var textBrush = new SolidBrush(textColor))
            using (var addressBrush = new SolidBrush(addressColor))
            using (var segBorderPen = new Pen(Color.FromArgb(50, 50, 50)))
            {
                foreach (var segment in segments)
                {
                    int x = legendX + (currentColumn * columnWidth);
                    int y = legendY + (currentRow * lineHeight);

                    var colorBox = new Rectangle(x, y, 10, 10);
                    using (var segBrush = new SolidBrush(segment.Color))
                        g.FillRectangle(segBrush, colorBox);
                    g.DrawRectangle(segBorderPen, colorBox);

                    g.DrawString($"{segment.FileName} ({FormatBytes(segment.Size)})", font, textBrush, x + 15, y);
                    g.DrawString($"  @ 0x{segment.AddressStart:X8} → 0x{segment.AddressEnd:X8}", font, addressBrush, x + 15, y + 9);

                    currentColumn++;
                    if (currentColumn >= maxColumnsPerRow) { currentColumn = 0; currentRow++; }
                }
            }
        }

        private void DrawEmptyState(PaintEventArgs e)
        {
            var textColor = ThemeIsDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(120, 120, 120);
            using (var font = new Font("Segoe UI", 9F))
            using (var brush = new SolidBrush(textColor))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString("No files loaded", font, brush, ClientRectangle, fmt);
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:F1}{sizes[order]}";
        }
    }
}
