using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EgsLL.Core;

namespace EgsLL.Forms
{
    public class MainForm : Form
    {
        private DataGridView _grid;
        private Button _btnRefresh;
        private Button _btnRecover;
        private Button _btnScan;
        private Button _btnInfo;
        private StatusStrip _statusBar;
        private ToolStripStatusLabel _statusLabel;
        private Label _titleLabel;
        private Panel _topPanel;
        private CancellationTokenSource _scanCts;

        private List<GameManifest> _manifests = new List<GameManifest>();

        public MainForm()
        {
            InitializeComponent();
            LoadGames();
        }

        private void InitializeComponent()
        {
            Text = "EGS-LL  --  Experienced Game Store Launcher Launcher";
            Size = new Size(960, 620);
            MinimumSize = new Size(750, 450);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);

            // --- Top panel ---
            _topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(16, 8, 16, 8)
            };

            _titleLabel = new Label
            {
                Text = "EGS-LL  " + GetVersionLabel(),
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 215),
                AutoSize = true,
                Location = new Point(16, 12)
            };

            var subtitleLabel = new Label
            {
                Text = "Quality-of-life wrapper for the Epic Games Store",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(160, 160, 160),
                AutoSize = true,
                Location = new Point(18, 48)
            };

            _topPanel.Controls.Add(_titleLabel);
            _topPanel.Controls.Add(subtitleLabel);

            // --- Button panel ---
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(12, 6, 12, 6),
                BackColor = Color.FromArgb(40, 40, 40)
            };

            _btnRefresh = CreateButton("Refresh", "Reload the game list from EGS manifests");
            _btnRefresh.Click += (s, e) => LoadGames();

            _btnScan = CreateButton("Scan Drives", "Scan all drives for unregistered EGS game folders");
            _btnScan.Click += (s, e) => ScanDrives();

            _btnRecover = CreateButton("Recover Selected", "Run the recovery workflow for the selected game");
            _btnRecover.Click += (s, e) => StartRecovery();
            _btnRecover.Enabled = false;

            _btnInfo = CreateButton("EGS Info", "Show EGS installation details");
            _btnInfo.Click += (s, e) => ShowEgsInfo();

            buttonPanel.Controls.Add(_btnRefresh);
            buttonPanel.Controls.Add(_btnScan);
            buttonPanel.Controls.Add(_btnRecover);
            buttonPanel.Controls.Add(_btnInfo);

            // --- Grid ---
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Color.FromArgb(25, 25, 25),
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(35, 35, 35),
                    ForeColor = Color.FromArgb(220, 220, 220),
                    SelectionBackColor = Color.FromArgb(0, 120, 140),
                    SelectionForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = Color.FromArgb(0, 200, 215),
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                },
                EnableHeadersVisualStyles = false,
                RowHeadersVisible = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(55, 55, 55)
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DisplayName", HeaderText = "Game", FillWeight = 30
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "InstallLocation", HeaderText = "Install Path", FillWeight = 30
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Size", HeaderText = "Size", FillWeight = 9
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Status", HeaderText = "Status", FillWeight = 11
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Source", HeaderText = "Source", FillWeight = 8
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "EgStore", HeaderText = ".egstore", FillWeight = 7
            });

            _grid.SelectionChanged += (s, e) =>
            {
                _btnRecover.Enabled = _grid.SelectedRows.Count > 0;
            };

            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) StartRecovery();
            };

            // Context menu
            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Recover", null, (s, e) => StartRecovery());
            ctx.Items.Add("Open Folder", null, (s, e) => OpenGameFolder());
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("Refresh", null, (s, e) => LoadGames());
            ctx.Items.Add("Scan Drives", null, (s, e) => ScanDrives());
            _grid.ContextMenuStrip = ctx;

            // --- Status bar ---
            _statusBar = new StatusStrip
            {
                BackColor = Color.FromArgb(30, 30, 30)
            };
            _statusLabel = new ToolStripStatusLabel
            {
                ForeColor = Color.FromArgb(160, 160, 160),
                Text = "Ready"
            };
            _statusBar.Items.Add(_statusLabel);

            // --- Layout ---
            Controls.Add(_grid);
            Controls.Add(buttonPanel);
            Controls.Add(_topPanel);
            Controls.Add(_statusBar);
        }

        private static Button CreateButton(string text, string tooltip)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 220),
                BackColor = Color.FromArgb(55, 55, 55),
                Padding = new Padding(8, 2, 8, 2),
                Margin = new Padding(4),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 120, 140);

            var tt = new ToolTip();
            tt.SetToolTip(btn, tooltip);

            return btn;
        }

        private void LoadGames()
        {
            _grid.Rows.Clear();
            _statusLabel.Text = "Loading manifests...";
            Cursor = Cursors.WaitCursor;

            try
            {
                _manifests = ManifestReader.ReadAll();
                PopulateGrid();
                _statusLabel.Text = string.Format("{0} game(s) from EGS manifests", _manifests.Count);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private async void ScanDrives()
        {
            _btnScan.Enabled = false;
            _btnRefresh.Enabled = false;
            _statusLabel.Text = "Scanning drives for EGS game folders...";
            Cursor = Cursors.WaitCursor;

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<string>(msg => _statusLabel.Text = msg);
                var fromManifests = ManifestReader.ReadAll();
                var fromScan = await DriveScanner.ScanAsync(progress, _scanCts.Token);
                _manifests = DriveScanner.MergeResults(fromManifests, fromScan);

                PopulateGrid();

                int registered = 0, discovered = 0;
                foreach (var m in _manifests)
                {
                    if (m.IsDiscovered) discovered++;
                    else registered++;
                }

                _statusLabel.Text = string.Format(
                    "{0} game(s): {1} registered, {2} discovered on disk",
                    _manifests.Count, registered, discovered);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _statusLabel.Text = "Scan error: " + ex.Message;
            }
            finally
            {
                _btnScan.Enabled = true;
                _btnRefresh.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private void PopulateGrid()
        {
            _grid.Rows.Clear();

            foreach (var m in _manifests)
            {
                bool hasEgstore = !string.IsNullOrEmpty(m.InstallLocation)
                    && ManifestReader.HasEgstore(m.InstallLocation);
                _grid.Rows.Add(
                    m.DisplayName,
                    m.InstallLocation,
                    m.FormattedSize,
                    m.Status,
                    m.Source,
                    hasEgstore ? "Yes" : "No"
                );
            }
        }

        private GameManifest GetSelectedManifest()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            int idx = _grid.SelectedRows[0].Index;
            if (idx < 0 || idx >= _manifests.Count) return null;
            return _manifests[idx];
        }

        private void StartRecovery()
        {
            var manifest = GetSelectedManifest();
            if (manifest == null)
            {
                MessageBox.Show("Please select a game first.", "EGS-LL",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Determine game path — let user override via folder browser
            string gamePath = manifest.InstallLocation;
            bool dirExists = !string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath);

            if (!dirExists)
            {
                var result = MessageBox.Show(
                    string.Format(
                        "The install folder for \"{0}\" is missing or not accessible:\n{1}\n\n" +
                        "Would you like to browse for the game folder?",
                        manifest.DisplayName, gamePath ?? "(empty)"),
                    "EGS-LL -- Folder Not Found",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result != DialogResult.Yes) return;

                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select the game folder for: " + manifest.DisplayName;
                    if (fbd.ShowDialog() == DialogResult.OK)
                        gamePath = fbd.SelectedPath;
                    else
                        return;
                }
            }

            using (var form = new RecoveryForm(manifest, gamePath))
            {
                form.ShowDialog(this);
            }

            // Refresh after recovery
            LoadGames();
        }

        private void OpenGameFolder()
        {
            var manifest = GetSelectedManifest();
            if (manifest == null) return;

            if (Directory.Exists(manifest.InstallLocation))
            {
                System.Diagnostics.Process.Start("explorer.exe", manifest.InstallLocation);
            }
            else
            {
                MessageBox.Show("Folder not found: " + manifest.InstallLocation,
                    "EGS-LL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowEgsInfo()
        {
            var info = RegistryHelper.GetInstallInfo();
            string msg;

            if (!info.Found)
            {
                msg = "Epic Games Store installation not detected.\n\n" +
                      "Checked standard registry paths and default install locations.";
            }
            else
            {
                msg = string.Format(
                    "Epic Games Store Installation\n\n" +
                    "Install Dir:    {0}\n" +
                    "Launcher Exe:   {1}\n" +
                    "Version:        {2}\n" +
                    "Data Path:      {3}\n" +
                    "Manifests:      {4}\n" +
                    "App Data Path:  {5}\n" +
                    "Running:        {6}",
                    info.InstallDir ?? "N/A",
                    info.LauncherExe ?? "N/A",
                    info.Version ?? "N/A",
                    info.DataPath ?? "N/A",
                    info.ManifestsPath ?? "N/A",
                    info.AppDataPath ?? "N/A",
                    ProcessHelper.IsEgsRunning() ? "Yes" : "No");
            }

            MessageBox.Show(msg, "EGS-LL -- EGS Info", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static string GetVersionLabel()
        {
            string ver = typeof(MainForm).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (string.IsNullOrEmpty(ver) || ver == "0.0.0")
                return "experimental";

            int plus = ver.IndexOf('+');
            if (plus > 0)
                ver = ver.Substring(0, plus);

            return ver;
        }
    }
}
