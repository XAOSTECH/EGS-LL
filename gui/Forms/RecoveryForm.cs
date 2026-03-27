using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using EgsLL.Core;

namespace EgsLL.Forms
{
    /// <summary>
    /// Modal dialog that displays real-time recovery progress.
    /// Runs the full automated recovery flow and reports each stage.
    /// </summary>
    public class RecoveryForm : Form
    {
        private readonly GameManifest _manifest;
        private readonly string _gamePath;

        private Label _gameLabel;
        private Label _pathLabel;
        private ListBox _logBox;
        private ProgressBar _progressBar;
        private Button _btnCancel;
        private Button _btnClose;
        private Button _btnPaused;
        private Button _btnExport;
        private Panel _headerPanel;

        private RecoveryEngine _engine;
        private bool _running;

        public RecoveryForm(GameManifest manifest, string gamePath)
        {
            _manifest = manifest;
            _gamePath = gamePath;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "EGS-LL -- Recovery: " + (_manifest.DisplayName ?? "Game");
            Size = new Size(680, 520);
            MinimumSize = new Size(500, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(30, 30, 30);

            // --- Header ---
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(35, 35, 35),
                Padding = new Padding(16, 10, 16, 10)
            };

            _gameLabel = new Label
            {
                Text = _manifest.DisplayName ?? "Unknown Game",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 215),
                AutoSize = true,
                Location = new Point(16, 8)
            };

            _pathLabel = new Label
            {
                Text = _gamePath,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(140, 140, 140),
                AutoSize = true,
                Location = new Point(18, 40)
            };

            _headerPanel.Controls.Add(_gameLabel);
            _headerPanel.Controls.Add(_pathLabel);

            // --- Progress bar ---
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 6,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false
            };

            // --- Log box ---
            _logBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Consolas", 9.5F),
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.None,
                IntegralHeight = false
            };

            // --- Button panel ---
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.FromArgb(35, 35, 35)
            };

            _btnClose = new Button
            {
                Text = "Close",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 220),
                BackColor = Color.FromArgb(55, 55, 55),
                Padding = new Padding(12, 2, 12, 2),
                Margin = new Padding(4),
                Enabled = false,
                DialogResult = DialogResult.OK
            };
            _btnClose.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);

            _btnCancel = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 220),
                BackColor = Color.FromArgb(140, 50, 50),
                Padding = new Padding(12, 2, 12, 2),
                Margin = new Padding(4)
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(180, 60, 60);
            _btnCancel.Click += BtnCancel_Click;

            _btnPaused = new Button
            {
                Text = "I've Paused",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 220),
                BackColor = Color.FromArgb(50, 120, 50),
                Padding = new Padding(12, 2, 12, 2),
                Margin = new Padding(4),
                Visible = false
            };
            _btnPaused.FlatAppearance.BorderColor = Color.FromArgb(60, 160, 60);
            _btnPaused.Click += BtnPaused_Click;

            _btnExport = new Button
            {
                Text = "Export Logs",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 220),
                BackColor = Color.FromArgb(55, 55, 55),
                Padding = new Padding(12, 2, 12, 2),
                Margin = new Padding(4),
                Visible = false
            };
            _btnExport.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            _btnExport.Click += BtnExport_Click;

            buttonPanel.Controls.Add(_btnClose);
            buttonPanel.Controls.Add(_btnExport);
            buttonPanel.Controls.Add(_btnPaused);
            buttonPanel.Controls.Add(_btnCancel);

            // --- Layout ---
            Controls.Add(_logBox);
            Controls.Add(_progressBar);
            Controls.Add(_headerPanel);
            Controls.Add(buttonPanel);

            // Start recovery on load
            Load += async (s, e) => await BeginRecovery();

            FormClosing += (s, e) =>
            {
                if (_running)
                {
                    var result = MessageBox.Show(
                        "Recovery is in progress. Cancel and restore backup?",
                        "EGS-LL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (result == DialogResult.Yes)
                        _engine?.Cancel();
                    else
                        e.Cancel = true;
                }
            };
        }

        private async Task BeginRecovery()
        {
            _engine = new RecoveryEngine(_gamePath);

            // Validate first
            string error = _engine.Validate();
            if (error != null)
            {
                Log("[X] " + error, Color.FromArgb(255, 80, 80));
                _btnClose.Enabled = true;
                _btnCancel.Enabled = false;
                return;
            }

            // Wire up events
            _engine.StageChanged += (stage, msg) =>
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(() => OnStageChanged(stage, msg)));
                else
                    OnStageChanged(stage, msg);
            };

            _engine.Completed += (success, msg) =>
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(() => OnCompleted(success, msg)));
                else
                    OnCompleted(success, msg);
            };

            _running = true;
            _progressBar.Visible = true;

            Log("[*] Starting recovery for: " + (_manifest.DisplayName ?? "game"),
                Color.FromArgb(0, 200, 215));
            Log("[*] Path: " + _gamePath, Color.FromArgb(0, 200, 215));
            Log("", Color.White);

            await Task.Run(() => _engine.RunAsync(_manifest));
        }

        private void OnStageChanged(RecoveryStage stage, string message)
        {
            Color color;
            string prefix;

            switch (stage)
            {
                case RecoveryStage.Error:
                    color = Color.FromArgb(255, 80, 80);
                    prefix = "[X] ";
                    break;
                case RecoveryStage.Renamed:
                case RecoveryStage.FolderDetected:
                case RecoveryStage.EgsSuspended:
                case RecoveryStage.SwapComplete:
                case RecoveryStage.Complete:
                    color = Color.FromArgb(80, 220, 80);
                    prefix = "[+] ";
                    _btnPaused.Visible = false;
                    break;
                case RecoveryStage.UserActionRequired:
                    color = Color.FromArgb(255, 160, 40);
                    prefix = "[!] ";
                    _btnPaused.Visible = true;
                    _btnPaused.Focus();
                    break;
                case RecoveryStage.WaitingForFolder:
                case RecoveryStage.SuspendingEgs:
                    color = Color.FromArgb(255, 200, 60);
                    prefix = "[!] ";
                    break;
                default:
                    color = Color.FromArgb(200, 200, 200);
                    prefix = "[*] ";
                    break;
            }

            Log(prefix + message, color);

            // Update progress bar stages (rough mapping)
            int progress = 0;
            switch (stage)
            {
                case RecoveryStage.Renaming:       progress = 10; break;
                case RecoveryStage.Renamed:        progress = 20; break;
                case RecoveryStage.LaunchingEgs:    progress = 30; break;
                case RecoveryStage.WaitingForFolder:progress = 40; break;
                case RecoveryStage.FolderDetected:  progress = 55; break;
                case RecoveryStage.SuspendingEgs:   progress = 65; break;
                case RecoveryStage.EgsSuspended:    progress = 70; break;
                case RecoveryStage.UserActionRequired: progress = 67; break;
                case RecoveryStage.Swapping:        progress = 80; break;
                case RecoveryStage.SwapComplete:    progress = 90; break;
                case RecoveryStage.ResumingEgs:     progress = 95; break;
                case RecoveryStage.Complete:        progress = 100; break;
            }

            if (progress > 0)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Maximum = 100;
                _progressBar.Value = Math.Min(progress, 100);
            }
        }

        private void OnCompleted(bool success, string message)
        {
            _running = false;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = success ? 100 : 0;

            Log("", Color.White);
            if (success)
            {
                Log("=== " + message + " ===", Color.FromArgb(80, 220, 80));
            }
            else
            {
                Log("=== " + message + " ===", Color.FromArgb(255, 80, 80));
            }

            _btnCancel.Enabled = false;
            _btnClose.Enabled = true;
            _btnExport.Visible = true;
            _btnClose.Focus();

            _engine?.Dispose();
            _engine = null;
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (!_running) return;

            var result = MessageBox.Show(
                "Cancel recovery and restore backup?",
                "EGS-LL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                _engine?.Cancel();
                Log("[!] Cancelling...", Color.FromArgb(255, 200, 60));
            }
        }

        private void BtnPaused_Click(object sender, EventArgs e)
        {
            _btnPaused.Visible = false;
            _engine?.ConfirmUserAction();
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = "egsll_log_" + stamp + ".log";
                string path = Path.Combine(dir, filename);

                using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
                {
                    foreach (var item in _logBox.Items)
                        writer.WriteLine(item.ToString());
                }

                Log("[+] Log exported to: " + path, Color.FromArgb(80, 220, 80));
            }
            catch (Exception ex)
            {
                Log("[X] Failed to export log: " + ex.Message, Color.FromArgb(255, 80, 80));
            }
        }

        private void Log(string message, Color color)
        {
            string entry = string.IsNullOrEmpty(message)
                ? ""
                : string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), message);

            _logBox.Items.Add(entry);

            // Scroll to bottom
            _logBox.TopIndex = Math.Max(0, _logBox.Items.Count - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _engine?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
