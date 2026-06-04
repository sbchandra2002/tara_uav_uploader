using System;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;

namespace TARAFlasher
{
    public class SerialMonitorForm : Form
    {
        private SerialPort _port;
        private readonly StringBuilder _rxBuffer = new StringBuilder();

        // Controls
        private ComboBox cmbPort;
        private ComboBox cmbBaud;
        private ComboBox cmbData;
        private ComboBox cmbParity;
        private ComboBox cmbStop;
        private Button btnRefreshPorts;
        private Button btnConnect;
        private Button btnClear;
        private CheckBox chkAutoScroll;
        private CheckBox chkTimestamp;
        private CheckBox chkHex;
        private RichTextBox rtxLog;
        private TextBox txtSend;
        private Button btnSend;
        private ComboBox cmbNewline;
        private Label lblStatus;
        private Panel pnlStatus;

        // Theme colours (dark)
        private readonly Color _bg     = Color.FromArgb(18, 22, 28);
        private readonly Color _panel  = Color.FromArgb(28, 34, 44);
        private readonly Color _panel2 = Color.FromArgb(36, 42, 54);
        private readonly Color _accent = Color.FromArgb(32, 185, 255);
        private readonly Color _green  = Color.FromArgb(0, 220, 150);
        private readonly Color _red    = Color.FromArgb(255, 80, 80);
        private readonly Color _text   = Color.FromArgb(210, 225, 245);
        private readonly Color _subtle = Color.FromArgb(90, 120, 150);

        public SerialMonitorForm()
        {
            Text            = "Serial Monitor — TARA-Flasher";
            ClientSize      = new Size(860, 560);
            MinimumSize     = new Size(700, 400);
            BackColor       = _bg;
            Font            = new Font("Segoe UI", 9F);
            StartPosition   = FormStartPosition.CenterScreen;

            try { Icon = new Icon("logo.ico"); } catch { }

            BuildLayout();
            PopulatePortList();
            ApplyTheme();
        }

        // ── Layout ────────────────────────────────────────────────────────────
        private void BuildLayout()
        {
            // ── Top toolbar panel ────────────────────────────────────────────
            var toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = _panel,
                Padding   = new Padding(6, 6, 6, 0)
            };

            int tx = 8;
            toolbar.Controls.Add(MakeLabel("Port:", tx, 12));
            cmbPort = MakeCombo(tx + 36, 8, 90);
            toolbar.Controls.Add(cmbPort); tx += 134;

            btnRefreshPorts = MakeButton("↺", tx, 8, 28);
            btnRefreshPorts.Click += (s, e) => PopulatePortList();
            toolbar.Controls.Add(btnRefreshPorts); tx += 34;

            toolbar.Controls.Add(MakeLabel("Baud:", tx, 12));
            cmbBaud = MakeCombo(tx + 40, 8, 90, new[]
                { "300","1200","2400","4800","9600","19200","38400",
                  "57600","115200","230400","460800","921600" });
            cmbBaud.SelectedItem = "115200";
            toolbar.Controls.Add(cmbBaud); tx += 138;

            toolbar.Controls.Add(MakeLabel("Data:", tx, 12));
            cmbData = MakeCombo(tx + 38, 8, 50, new[] { "8", "7", "6", "5" });
            cmbData.SelectedItem = "8";
            toolbar.Controls.Add(cmbData); tx += 96;

            toolbar.Controls.Add(MakeLabel("Parity:", tx, 12));
            cmbParity = MakeCombo(tx + 48, 8, 66, new[] { "None", "Even", "Odd", "Mark", "Space" });
            cmbParity.SelectedItem = "None";
            toolbar.Controls.Add(cmbParity); tx += 122;

            toolbar.Controls.Add(MakeLabel("Stop:", tx, 12));
            cmbStop = MakeCombo(tx + 38, 8, 50, new[] { "1", "1.5", "2" });
            cmbStop.SelectedItem = "1";
            toolbar.Controls.Add(cmbStop); tx += 96;

            btnConnect = MakeButton("Connect", tx, 6, 80);
            btnConnect.BackColor = Color.FromArgb(20, 60, 40);
            btnConnect.ForeColor = _green;
            btnConnect.Click += BtnConnect_Click;
            toolbar.Controls.Add(btnConnect);

            Controls.Add(toolbar);

            // ── Status bar (bottom) ──────────────────────────────────────────
            pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = _panel };
            lblStatus = new Label
            {
                Text      = "Disconnected",
                ForeColor = _subtle,
                Font      = new Font("Consolas", 8F),
                Location  = new Point(8, 5),
                AutoSize  = true
            };
            pnlStatus.Controls.Add(lblStatus);
            Controls.Add(pnlStatus);

            // ── Send bar ─────────────────────────────────────────────────────
            var sendBar = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = _panel2 };

            txtSend = new TextBox
            {
                Location    = new Point(8, 8),
                Size        = new Size(560, 22),
                BackColor   = Color.FromArgb(12, 18, 26),
                ForeColor   = _text,
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Consolas", 9.5F)
            };
            txtSend.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendData(); } };
            sendBar.Controls.Add(txtSend);

            cmbNewline = MakeCombo(576, 8, 80, new[] { "None", "\\n", "\\r", "\\r\\n" });
            cmbNewline.SelectedItem = "\\r\\n";
            sendBar.Controls.Add(cmbNewline);

            btnSend = MakeButton("Send", 664, 6, 60);
            btnSend.Click += (s, e) => SendData();
            sendBar.Controls.Add(btnSend);

            chkHex = new CheckBox { Text = "HEX", Location = new Point(732, 10), AutoSize = true, ForeColor = _subtle };
            sendBar.Controls.Add(chkHex);

            Controls.Add(sendBar);

            // ── Options bar ──────────────────────────────────────────────────
            var optBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = _panel };

            btnClear = MakeButton("Clear", 8, 4, 60);
            btnClear.Click += (s, e) => { rtxLog.Clear(); _rxBuffer.Clear(); };
            optBar.Controls.Add(btnClear);

            chkAutoScroll = new CheckBox { Text = "Auto scroll", Checked = true, Location = new Point(76, 6), AutoSize = true, ForeColor = _subtle };
            optBar.Controls.Add(chkAutoScroll);

            chkTimestamp = new CheckBox { Text = "Timestamp", Location = new Point(166, 6), AutoSize = true, ForeColor = _subtle };
            optBar.Controls.Add(chkTimestamp);

            Controls.Add(optBar);

            // ── Log area ─────────────────────────────────────────────────────
            rtxLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(10, 14, 20),
                ForeColor   = _text,
                Font        = new Font("Consolas", 9.5F),
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                WordWrap    = false
            };
            Controls.Add(rtxLog);
        }

        // ── Helper factory methods ────────────────────────────────────────────
        private Label MakeLabel(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = _subtle };
        }

        private ComboBox MakeCombo(int x, int y, int width, string[] items = null)
        {
            var c = new ComboBox
            {
                Location      = new Point(x, y),
                Size          = new Size(width, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor     = Color.FromArgb(22, 28, 38),
                ForeColor     = _text,
                FlatStyle     = FlatStyle.Flat
            };
            if (items != null) c.Items.AddRange(items);
            return c;
        }

        private Button MakeButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(width, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 34, 44),
                ForeColor = _accent
            };
        }

        private void ApplyTheme()
        {
            foreach (Control c in Controls)
                if (c is CheckBox cb) cb.ForeColor = _subtle;
        }

        // ── Port list ─────────────────────────────────────────────────────────
        private void PopulatePortList()
        {
            var ports = SerialPort.GetPortNames();
            cmbPort.Items.Clear();
            cmbPort.Items.AddRange(ports);
            if (cmbPort.Items.Count > 0)
                cmbPort.SelectedIndex = cmbPort.Items.Count - 1;
        }

        // ── Connect / disconnect ───────────────────────────────────────────────
        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_port != null && _port.IsOpen)
                Disconnect();
            else
                Connect();
        }

        private void Connect()
        {
            if (cmbPort.SelectedItem == null) { SetStatus("No port selected.", false); return; }

            try
            {
                int baud = int.Parse(cmbPort.SelectedItem.ToString() == null ? "115200"
                                     : (cmbBaud.SelectedItem?.ToString() ?? "115200"));
                int data = int.Parse(cmbData.SelectedItem?.ToString() ?? "8");
                StopBits stop = ParseStopBits(cmbStop.SelectedItem?.ToString());
                Parity par = ParseParity(cmbParity.SelectedItem?.ToString());

                _port = new SerialPort(cmbPort.SelectedItem.ToString(), baud, par, data, stop)
                {
                    ReadTimeout  = 500,
                    WriteTimeout = 500,
                    Encoding     = Encoding.UTF8
                };
                _port.DataReceived += Port_DataReceived;
                _port.Open();

                btnConnect.Text      = "Disconnect";
                btnConnect.ForeColor = _red;
                btnConnect.BackColor = Color.FromArgb(50, 10, 10);
                SetStatus($"Connected: {cmbPort.SelectedItem} @ {baud} baud", true);
                AppendLine($"[PORT OPENED] {cmbPort.SelectedItem} {baud},{data},{cmbParity.SelectedItem},{cmbStop.SelectedItem}", _green);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, false);
                AppendLine("[ERROR] " + ex.Message, _red);
            }
        }

        private void Disconnect()
        {
            try
            {
                _port?.Close();
                _port?.Dispose();
                _port = null;
            }
            catch { }

            btnConnect.Text      = "Connect";
            btnConnect.ForeColor = _green;
            btnConnect.BackColor = Color.FromArgb(20, 60, 40);
            SetStatus("Disconnected", false);
            AppendLine("[PORT CLOSED]", _subtle);
        }

        // ── Receive ───────────────────────────────────────────────────────────
        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var data = _port?.ReadExisting();
                if (string.IsNullOrEmpty(data)) return;

                if (InvokeRequired)
                    BeginInvoke(new Action(() => ProcessRx(data)));
                else
                    ProcessRx(data);
            }
            catch { }
        }

        private void ProcessRx(string data)
        {
            _rxBuffer.Append(data);

            while (true)
            {
                var s = _rxBuffer.ToString();
                int nl = s.IndexOf('\n');
                if (nl < 0) break;

                string line = s.Substring(0, nl).TrimEnd('\r');
                _rxBuffer.Remove(0, nl + 1);

                string prefix = chkTimestamp.Checked
                    ? $"[{DateTime.Now:HH:mm:ss.fff}] " : string.Empty;

                AppendLine(prefix + line, _text);
            }
        }

        // ── Send ──────────────────────────────────────────────────────────────
        private void SendData()
        {
            if (_port == null || !_port.IsOpen)
            {
                AppendLine("[NOT CONNECTED]", _red);
                return;
            }

            string raw = txtSend.Text;
            if (string.IsNullOrEmpty(raw)) return;

            try
            {
                string nlSel = cmbNewline.SelectedItem?.ToString() ?? string.Empty;
                string suffix;
                if (nlSel == "\\n")         suffix = "\n";
                else if (nlSel == "\\r")    suffix = "\r";
                else if (nlSel == "\\r\\n") suffix = "\r\n";
                else                         suffix = string.Empty;

                if (chkHex.Checked)
                {
                    var bytes = ParseHexString(raw);
                    if (bytes != null)
                    {
                        _port.Write(bytes, 0, bytes.Length);
                        AppendLine($"[TX HEX] {raw}", Color.FromArgb(255, 220, 100));
                    }
                    else
                    {
                        AppendLine("[TX ERROR] Invalid hex string", _red);
                    }
                }
                else
                {
                    _port.Write(raw + suffix);
                    AppendLine($"[TX] {raw}", Color.FromArgb(255, 220, 100));
                }

                txtSend.Clear();
            }
            catch (Exception ex)
            {
                AppendLine("[TX ERROR] " + ex.Message, _red);
            }
        }

        // ── Log append ────────────────────────────────────────────────────────
        private void AppendLine(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => AppendLine(text, color))); return; }

            int start = rtxLog.TextLength;
            rtxLog.AppendText(text + Environment.NewLine);
            rtxLog.Select(start, text.Length);
            rtxLog.SelectionColor = color;
            rtxLog.SelectionLength = 0;

            if (chkAutoScroll.Checked)
                rtxLog.ScrollToCaret();
        }

        private void SetStatus(string msg, bool connected)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(msg, connected))); return; }
            lblStatus.Text      = msg;
            lblStatus.ForeColor = connected ? _green : _subtle;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static StopBits ParseStopBits(string s) =>
            s == "1.5" ? StopBits.OnePointFive :
            s == "2"   ? StopBits.Two : StopBits.One;

        private static Parity ParseParity(string s)
        {
            switch (s?.ToLower())
            {
                case "even":  return Parity.Even;
                case "odd":   return Parity.Odd;
                case "mark":  return Parity.Mark;
                case "space": return Parity.Space;
                default:      return Parity.None;
            }
        }

        private static byte[] ParseHexString(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.Length % 2 != 0) return null;
            var bytes = new byte[hex.Length / 2];
            try
            {
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                return bytes;
            }
            catch { return null; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                Disconnect();
                base.OnFormClosing(e);
            }
        }
    }
}
