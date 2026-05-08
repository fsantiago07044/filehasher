using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace FileHasher;

/// <summary>Main application window.</summary>
public sealed class MainForm : Form
{
    // ── Controls ─────────────────────────────────────────────────────────────

    // Target group
    private readonly TextBox  _pathBox;
    private readonly Button   _browseFileBtn;
    private readonly Button   _browseFolderBtn;
    private readonly CheckBox _allTypesChk;

    // Algorithm group
    private readonly RadioButton _rdMd5, _rdSha1, _rdSha256, _rdSha512;

    // Options group
    private readonly CheckBox    _metadataChk;
    private readonly CheckBox    _sidecarChk;
    private readonly TextBox     _sidecarExtBox;
    private readonly RadioButton _rdSha256Sum, _rdHashOnly;
    private readonly Panel       _sidecarOptsPanel;
    private readonly CheckBox    _csvChk;
    private readonly TextBox     _csvPathBox;
    private readonly Button      _csvBrowseBtn;
    private readonly Panel       _csvOptsPanel;

    // Actions
    private readonly Button _runAsAdminBtn;
    private readonly Button _clearBtn;
    private readonly Button _runBtn;
    private readonly Button _stopBtn;

    // Progress / status
    private readonly ColorProgressBar _progressBar;
    private readonly Label            _statusLabel;

    // Results
    private readonly ListView    _resultsView;
    private readonly ColumnHeader _colPath, _colHash, _colSize, _colModified;

    // Status strip
    private readonly ToolStripStatusLabel _logStripLabel;
    private readonly ToolStripStatusLabel _adminStripLabel;

    // ── State ────────────────────────────────────────────────────────────────

    private CancellationTokenSource? _cts;
    private Logger?                  _logger;
    private readonly List<HashResult> _allResults = new();

    // ── Constructor ──────────────────────────────────────────────────────────

    public MainForm()
    {
        SuspendLayout();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "app-icon", "hash-icon.ico");
        if (File.Exists(iconPath))
            Icon = new Icon(iconPath);

        Text            = IsAdmin() ? "FileHasher  [Administrator]" : "FileHasher";
        Width           = 860;
        Height          = 760;
        MinimumSize     = new Size(720, 660);
        Font            = new Font("Segoe UI", 9F);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        // ── Status strip (Bottom) ─────────────────────────────────────────────

        var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };

        _logStripLabel = new ToolStripStatusLabel
        {
            Text        = "Log: (not yet started)  — click to open log folder",
            Spring      = true,
            TextAlign   = ContentAlignment.MiddleLeft,
            IsLink      = true,
            LinkBehavior = LinkBehavior.HoverUnderline
        };
        _logStripLabel.Click += (_, _) => OpenLogFolder();

        _adminStripLabel = new ToolStripStatusLabel
        {
            Text      = IsAdmin() ? "● Administrator" : "○ Standard user",
            ForeColor = IsAdmin() ? Color.DarkGreen    : SystemColors.GrayText
        };

        statusStrip.Items.Add(_logStripLabel);
        statusStrip.Items.Add(_adminStripLabel);

        // ── Top panel (all options) ───────────────────────────────────────────

        const int M  = 8;   // outer margin
        const int G  = 6;   // gap between groups

        var topPanel = new Panel { Dock = DockStyle.Top };

        // --- GroupBox: Target ---
        var gbTarget = new GroupBox
        {
            Text   = "Target",
            Left   = M,
            Top    = G,
            Height = 88,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        _pathBox = new TextBox
        {
            Name   = "PathBox",
            Left   = M,
            Top    = 26,
            Height = 23,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _browseFolderBtn = new Button
        {
            Name   = "BrowseFolderBtn",
            Text   = "Browse Folder…",
            Top    = 25,
            Width  = 114,
            Height = 25,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        _browseFileBtn = new Button
        {
            Name   = "BrowseFileBtn",
            Text   = "Browse File…",
            Top    = 25,
            Width  = 96,
            Height = 25,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        _allTypesChk = new CheckBox
        {
            Name    = "AllTypesChk",
            Text    = "Scan all file types  (default: .exe and .msi only)",
            Left    = M,
            Top     = 58,
            Width   = 340,
            Checked = false
        };

        _pathBox.AllowDrop = true;
        _pathBox.DragEnter += PathBox_DragEnter;
        _pathBox.DragDrop  += PathBox_DragDrop;

        _browseFileBtn.Click   += BrowseFile_Click;
        _browseFolderBtn.Click += BrowseFolder_Click;

        gbTarget.Controls.AddRange(new Control[]
            { _pathBox, _browseFileBtn, _browseFolderBtn, _allTypesChk });

        // --- GroupBox: Hash Algorithm ---
        var gbAlgo = new GroupBox
        {
            Text   = "Hash Algorithm",
            Left   = M,
            Top    = gbTarget.Bottom + G,
            Height = 54,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        _rdMd5    = new RadioButton { Name = "AlgoMd5",    Text = "MD5",    Left = 10,  Top = 24, AutoSize = true };
        _rdSha1   = new RadioButton { Name = "AlgoSha1",   Text = "SHA1",   Left = 80,  Top = 24, AutoSize = true };
        _rdSha256 = new RadioButton { Name = "AlgoSha256", Text = "SHA256", Left = 158, Top = 24, AutoSize = true, Checked = true };
        _rdSha512 = new RadioButton { Name = "AlgoSha512", Text = "SHA512", Left = 248, Top = 24, AutoSize = true };

        gbAlgo.Controls.AddRange(new Control[] { _rdMd5, _rdSha1, _rdSha256, _rdSha512 });

        // --- GroupBox: Options ---
        var gbOptions = new GroupBox
        {
            Text   = "Options",
            Left   = M,
            Top    = gbAlgo.Bottom + G,
            Height = 200,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        _metadataChk = new CheckBox
        {
            Name  = "MetadataChk",
            Text  = "Include file metadata  (size and last modified date)",
            Left  = M,
            Top   = 24,
            Width = 380
        };

        _sidecarChk = new CheckBox
        {
            Name  = "SidecarChk",
            Text  = "Write sidecar hash files next to each file",
            Left  = M,
            Top   = 50,
            Width = 340
        };
        _sidecarChk.CheckedChanged += (_, _) => _sidecarOptsPanel.Enabled = _sidecarChk.Checked;

        _sidecarOptsPanel = new Panel
        {
            Left    = 26,
            Top     = 74,
            Height  = 56,
            Enabled = false,
            Anchor  = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        var lblExt = new Label
        {
            Text      = "Extension:",
            Left      = 0,
            Top       = 5,
            Width     = 68,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _sidecarExtBox  = new TextBox    { Name = "SidecarExtBox",        Text = ".sha256",                               Left = 72,  Top = 3,  Width = 80 };
        _rdSha256Sum    = new RadioButton { Name = "SidecarFmtSha256Sum", Text = "sha256sum format  (HASH *filename)", Left = 0,   Top = 30, AutoSize = true, Checked = true };
        _rdHashOnly     = new RadioButton { Name = "SidecarFmtHashOnly",  Text = "Hash only",                          Left = 248, Top = 30, AutoSize = true };
        _sidecarOptsPanel.Controls.AddRange(new Control[] { lblExt, _sidecarExtBox, _rdSha256Sum, _rdHashOnly });

        _csvChk = new CheckBox
        {
            Name  = "CsvChk",
            Text  = "Export results to CSV",
            Left  = M,
            Top   = 136,
            Width = 200
        };
        _csvChk.CheckedChanged += (_, _) => _csvOptsPanel.Enabled = _csvChk.Checked;

        _csvOptsPanel = new Panel
        {
            Left    = 26,
            Top     = 158,
            Height  = 28,
            Enabled = false,
            Anchor  = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _csvPathBox = new TextBox
        {
            Name   = "CsvPathBox",
            Left   = 0,
            Top    = 3,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _csvBrowseBtn = new Button
        {
            Name   = "CsvBrowseBtn",
            Text   = "Browse…",
            Top    = 2,
            Width  = 72,
            Height = 24,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        _csvBrowseBtn.Click += BrowseCsv_Click;
        _csvOptsPanel.Controls.AddRange(new Control[] { _csvPathBox, _csvBrowseBtn });

        gbOptions.Controls.AddRange(new Control[]
            { _metadataChk, _sidecarChk, _sidecarOptsPanel, _csvChk, _csvOptsPanel });

        // --- Actions panel ---
        var actionsPanel = new Panel
        {
            Left   = M,
            Top    = gbOptions.Bottom + G,
            Height = 34,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        _runAsAdminBtn = new Button
        {
            Name    = "RunAsAdminBtn",
            Text    = IsAdmin() ? "Running as Administrator" : "Run as Administrator…",
            Left    = 0,
            Top     = 0,
            Width   = 180,
            Height  = 30,
            Enabled = !IsAdmin()
        };
        _runAsAdminBtn.Click += RunAsAdmin_Click;

        _clearBtn = new Button
        {
            Name   = "ClearBtn",
            Text   = "Clear Results",
            Top    = 0,
            Width  = 110,
            Height = 30
        };
        _stopBtn = new Button
        {
            Name    = "StopBtn",
            Text    = "Stop",
            Top     = 0,
            Width   = 72,
            Height  = 30,
            Enabled = false,
            Anchor  = AnchorStyles.Right | AnchorStyles.Top
        };
        _runBtn = new Button
        {
            Name   = "RunBtn",
            Text   = "▶  Run",
            Top    = 0,
            Width  = 90,
            Height = 30,
            Font   = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        _clearBtn.Click += ClearResults_Click;
        _stopBtn.Click  += Stop_Click;
        _runBtn.Click   += Run_Click;

        actionsPanel.Controls.AddRange(new Control[] { _runAsAdminBtn, _clearBtn, _stopBtn, _runBtn });

        // --- Progress bar ---
        _progressBar = new ColorProgressBar
        {
            Left   = M,
            Top    = actionsPanel.Bottom + 6,
            Height = 20,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        // --- Status label ---
        _statusLabel = new Label
        {
            Name      = "StatusLabel",
            Left      = M,
            Top       = _progressBar.Bottom + 4,
            Height    = 20,
            Text      = "Ready.",
            ForeColor = SystemColors.GrayText,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        topPanel.Height = _statusLabel.Bottom + 8;
        topPanel.Controls.AddRange(new Control[]
            { gbTarget, gbAlgo, gbOptions, actionsPanel, _progressBar, _statusLabel });

        // ── Results ListView (Fill) ───────────────────────────────────────────

        _colPath     = new ColumnHeader { Text = "File Path",      Width = 440 };
        _colHash     = new ColumnHeader { Text = "SHA256",         Width = 220 };
        _colSize     = new ColumnHeader { Text = "Size (bytes)",   Width = 95  };
        _colModified = new ColumnHeader { Text = "Modified (UTC)", Width = 145 };

        _resultsView = new ListView
        {
            Name          = "ResultsView",
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = true,
            Font          = new Font("Consolas", 8.5F)
        };
        _resultsView.Columns.AddRange(new[] { _colPath, _colHash, _colSize, _colModified });

        // ── Menu bar ─────────────────────────────────────────────────────────

        var menuStrip = new MenuStrip();
        var helpMenu  = new ToolStripMenuItem("&Help");
        var aboutItem = new ToolStripMenuItem("About FileHasher…");
        aboutItem.Click += (_, _) => ShowAboutDialog();
        helpMenu.DropDownItems.Add(aboutItem);
        menuStrip.Items.Add(helpMenu);
        MainMenuStrip = menuStrip;

        // ── Wire up form ──────────────────────────────────────────────────────

        // Order matters: Fill first, then Top, then Bottom.
        // MenuStrip is added last so DockStyle.Top places it above topPanel.
        Controls.Add(_resultsView);
        Controls.Add(topPanel);
        Controls.Add(statusStrip);
        Controls.Add(menuStrip);

        ResumeLayout(false);
        PerformLayout();

        // Fix up right-anchored widths that depend on form client size
        LayoutRightAlignedControls(gbTarget, gbAlgo, gbOptions, actionsPanel);
        SizeChanged += (_, _) => LayoutRightAlignedControls(gbTarget, gbAlgo, gbOptions, actionsPanel);
    }

    // ── Layout helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Sets widths of the GroupBoxes and repositions right-anchored controls within them.
    /// Called on construction and on SizeChanged so the layout is always correct.
    /// </summary>
    private void LayoutRightAlignedControls(
        GroupBox gbTarget, GroupBox gbAlgo, GroupBox gbOptions, Panel actionsPanel)
    {
        const int M        = 8;
        int       gbWidth  = ClientSize.Width - M * 2;
        int       innerW   = gbWidth - M * 2;   // usable inside a GroupBox (8px each side)

        gbTarget.Width    = gbWidth;
        gbAlgo.Width      = gbWidth;
        gbOptions.Width   = gbWidth;
        actionsPanel.Width = gbWidth;

        // Target: TextBox fills, buttons right-anchored
        int btnFolderLeft = gbWidth - M - _browseFolderBtn.Width;
        int btnFileLeft   = btnFolderLeft - 4 - _browseFileBtn.Width;

        _browseFolderBtn.Left = btnFolderLeft;
        _browseFileBtn.Left   = btnFileLeft;
        _pathBox.Width        = btnFileLeft - M - 4;

        // Options: sidecar and csv panels fill width
        _sidecarOptsPanel.Width = innerW - 18;   // 18 = indent offset
        _csvOptsPanel.Width     = innerW - 18;

        int csvBrowseLeft = _csvOptsPanel.Width - _csvBrowseBtn.Width;
        _csvBrowseBtn.Left  = csvBrowseLeft;
        _csvPathBox.Width   = csvBrowseLeft - 4;

        // Actions: Stop and Run right-aligned; Clear centered in the gap
        _stopBtn.Left  = gbWidth - _runBtn.Width - 4 - _stopBtn.Width;
        _runBtn.Left   = gbWidth - _runBtn.Width;
        int clearMid   = (_runAsAdminBtn.Right + _stopBtn.Left) / 2;
        _clearBtn.Left = clearMid - _clearBtn.Width / 2;
    }

    // ── Drag-and-drop onto the path field ────────────────────────────────────

    private static void PathBox_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void PathBox_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return;

        var dropped = paths[0];
        _pathBox.Text        = dropped;
        _allTypesChk.Enabled = Directory.Exists(dropped);
    }

    // ── Browse / file-picker handlers ─────────────────────────────────────────

    private void BrowseFile_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title           = "Select a file to hash",
            Filter          = "Executables|*.exe;*.msi|All files|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text         = dlg.FileName;
            _allTypesChk.Enabled  = false;  // irrelevant for a single file
        }
    }

    private void BrowseFolder_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description         = "Select a folder to scan recursively",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text        = dlg.SelectedPath;
            _allTypesChk.Enabled = true;
        }
    }

    private void BrowseCsv_Click(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Title       = "Save results as CSV",
            Filter      = "CSV files|*.csv|All files|*.*",
            DefaultExt  = "csv",
            FileName    = $"FileHasher_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _csvPathBox.Text = dlg.FileName;
    }

    // ── UAC elevation ────────────────────────────────────────────────────────

    private void RunAsAdmin_Click(object? sender, EventArgs e) => RelaunchAsAdmin();

    private void RelaunchAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName       = Application.ExecutablePath,
                UseShellExecute = true,
                Verb           = "runas"
            });
            Application.Exit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User clicked "No" on the UAC prompt — silently ignore.
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not re-launch as Administrator:\n{ex.Message}",
                            "Elevation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Run / Stop ────────────────────────────────────────────────────────────

    private async void Run_Click(object? sender, EventArgs e)
    {
        // ── Validate inputs ──────────────────────────────────────────────────

        var path = _pathBox.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("Please select a file or folder first.",
                            "No target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool isFile = File.Exists(path);
        bool isDir  = Directory.Exists(path);

        if (!isFile && !isDir)
        {
            MessageBox.Show($"Path does not exist:\n{path}",
                            "Invalid path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_csvChk.Checked && string.IsNullOrWhiteSpace(_csvPathBox.Text))
        {
            MessageBox.Show("Please specify a CSV output path.",
                            "Missing CSV path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ── Build options snapshot ────────────────────────────────────────────

        var opts = new HashOptions(
            TargetPath:       path,
            IsFile:           isFile,
            Algorithm:        GetSelectedAlgorithm(),
            IncludeMetadata:  _metadataChk.Checked,
            WriteSidecarHashes: _sidecarChk.Checked,
            SidecarExtension: _sidecarExtBox.Text.Trim(),
            SidecarFormat:    _rdSha256Sum.Checked ? "sha256sum" : "hashonly",
            ExportCsv:        _csvChk.Checked,
            CsvPath:          _csvPathBox.Text.Trim(),
            AllFileTypes:     _allTypesChk.Checked
        );

        // ── Reset UI ─────────────────────────────────────────────────────────

        _allResults.Clear();
        _resultsView.Items.Clear();
        _colHash.Text      = opts.Algorithm;
        _progressBar.Value = 0;
        _progressBar.State = ColorProgressBar.BarState.Marquee;
        SetRunning(true);
        SetStatus("Enumerating files…");

        // ── Start logger ──────────────────────────────────────────────────────

        _logger?.Dispose();
        _logger = new Logger();
        _logger.LogInfo($"Target: {path}  |  Algorithm: {opts.Algorithm}  |  AllTypes: {opts.AllFileTypes}  |  Metadata: {opts.IncludeMetadata}  |  Sidecar: {opts.WriteSidecarHashes}");
        _logStripLabel.Text = $"Log: {_logger.LogPath}  — click to open folder";

        // ── Run ───────────────────────────────────────────────────────────────

        _cts = new CancellationTokenSource();
        var worker = new HashWorker(opts, _logger);

        worker.WarningRaised += w  => SafeInvoke(() => AppendWarning(w));
        worker.FileHashed    += r  => SafeInvoke(() => AddResult(r));

        try
        {
            // Phase 1 – enumerate
            var files = await worker.EnumerateAsync(_cts.Token);

            if (files.Count == 0)
            {
                SetStatus("No matching files found.");
                return;
            }

            // Phase 1b – per-file sidecar conflict resolution, before any hashing
            int sidecarSkipped = 0, sidecarOverwritten = 0;
            if (opts.WriteSidecarHashes)
            {
                var ext         = opts.SidecarExtension;
                var conflicting = await Task.Run(
                    () => files.Where(f => File.Exists(f + ext)).ToList(), _cts.Token);

                if (conflicting.Count > 0)
                {
                    bool skipAll = false, overwriteAll = false;
                    var  toSkip  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var conflictFile in conflicting)
                    {
                        SidecarConflictAction decision;

                        if (skipAll)
                            decision = SidecarConflictAction.Skip;
                        else if (overwriteAll)
                            decision = SidecarConflictAction.Overwrite;
                        else
                        {
                            decision = ShowSidecarConflictDialog(conflictFile, conflictFile + ext);
                            if      (decision == SidecarConflictAction.SkipAll)      skipAll      = true;
                            else if (decision == SidecarConflictAction.OverwriteAll) overwriteAll = true;
                        }

                        if (decision == SidecarConflictAction.Skip ||
                            decision == SidecarConflictAction.SkipAll)
                        {
                            toSkip.Add(conflictFile);
                            sidecarSkipped++;
                        }
                        else
                        {
                            sidecarOverwritten++;
                        }
                    }

                    if (toSkip.Count > 0)
                        files.RemoveAll(f => toSkip.Contains(f));
                }
            }

            // Phase 2 – hash
            SetStatus($"Hashing {files.Count:N0} file(s)…");
            _progressBar.State   = ColorProgressBar.BarState.Normal;
            _progressBar.Maximum = files.Count;
            _progressBar.Value   = 0;

            var progress = new Progress<int>(n =>
            {
                _progressBar.Value = Math.Min(n, _progressBar.Maximum);
                SetStatus($"Hashing {n:N0} / {files.Count:N0}…");
            });

            await worker.HashAllAsync(files, progress, _cts.Token);

            // Done
            int successes   = _allResults.Count(r => r.Success);
            int errors      = _allResults.Count(r => !r.Success);
            int skipped     = sidecarSkipped;
            int overwritten = sidecarOverwritten;

            _logger.LogSessionEnd(successes, errors);

            // Drain any queued Progress<int> callbacks before setting the final
            // status — otherwise MessageBox.Show()'s modal pump can run a stale
            // "Hashing N/N…" callback after we've already written "Done…".
            Application.DoEvents();
            SetStatus($"Done — {successes:N0} hashed, {errors:N0} error(s).  Log: {_logger.LogPath}");

            _progressBar.State = ColorProgressBar.BarState.Complete;

            // CSV export
            if (opts.ExportCsv && opts.CsvPath.Length > 0)
                ExportCsv(opts);

            var msg = new System.Text.StringBuilder();
            msg.AppendLine("Hashing complete!\n");
            msg.AppendLine($"Files hashed:  {successes:N0}");
            msg.AppendLine($"Errors:             {errors:N0}");
            if (skipped > 0 || overwritten > 0)
            {
                msg.AppendLine();
                if (skipped > 0)
                    msg.AppendLine($"Sidecars skipped:      {skipped:N0}");
                if (overwritten > 0)
                    msg.AppendLine($"Sidecars overwritten:  {overwritten:N0}");
            }
            msg.Append($"\nLog: {_logger.LogPath}");

            MessageBox.Show(msg.ToString(), "Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
            _logger?.LogWarning("Operation cancelled by user.");
        }
        catch (UnauthorizedAccessException ex)
        {
            var ask = MessageBox.Show(
                $"Access denied:\n{ex.Message}\n\nWould you like to restart as Administrator?",
                "Elevation required",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (ask == DialogResult.Yes)
                RelaunchAsAdmin();
            else
                SetStatus($"Access denied — try running as Administrator.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unexpected error:\n{ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            _progressBar.State = ColorProgressBar.BarState.Normal;
            SetRunning(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Stop_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        _stopBtn.Enabled = false;
        SetStatus("Stopping…");
    }

    private void ClearResults_Click(object? sender, EventArgs e)
    {
        _allResults.Clear();
        _resultsView.Items.Clear();
        _progressBar.Value = 0;
        _progressBar.State = ColorProgressBar.BarState.Normal;
        SetStatus("Ready.");
    }

    // ── Results ───────────────────────────────────────────────────────────────

    private void AddResult(HashResult r)
    {
        _allResults.Add(r);

        var item = new ListViewItem(r.FilePath);
        item.SubItems.Add(r.Success ? r.Hash : $"ERROR: {r.ErrorMessage}");
        item.SubItems.Add(r.Length.HasValue        ? r.Length.Value.ToString("N0")              : "");
        item.SubItems.Add(r.LastWriteUtc.HasValue  ? r.LastWriteUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "");

        if (!r.Success)
            item.ForeColor = Color.Firebrick;

        _resultsView.Items.Add(item);
    }

    private void AppendWarning(string message)
    {
        var item = new ListViewItem("[WARN]");
        item.SubItems.Add(message);
        item.ForeColor = Color.DarkOrange;
        _resultsView.Items.Add(item);
    }

    // ── CSV export ────────────────────────────────────────────────────────────

    private void ExportCsv(HashOptions opts)
    {
        try
        {
            var sb = new StringBuilder(4096);

            // Header
            sb.Append("Path");
            sb.Append(',');
            sb.Append(opts.Algorithm);
            if (opts.IncludeMetadata)
                sb.Append(",LengthBytes,LastWriteUtc");
            sb.AppendLine();

            foreach (var r in _allResults.Where(r => r.Success).OrderBy(r => r.FilePath))
            {
                sb.Append(CsvEscape(r.FilePath));
                sb.Append(',');
                sb.Append(r.Hash);
                if (opts.IncludeMetadata)
                {
                    sb.Append(',');
                    sb.Append(r.Length);
                    sb.Append(',');
                    sb.Append(r.LastWriteUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                }
                sb.AppendLine();
            }

            // UTF-8 with BOM so Excel opens it correctly without an import wizard
            File.WriteAllText(opts.CsvPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            _logger?.LogInfo($"CSV exported to: {opts.CsvPath}");
            SetStatus($"CSV saved: {opts.CsvPath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CSV export failed:\n{ex.Message}",
                            "Export error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    // ── Sidecar conflict dialog ───────────────────────────────────────────────

    private SidecarConflictAction ShowSidecarConflictDialog(string filePath, string sidecarPath)
    {
        var btnOverwrite    = new TaskDialogButton("&Overwrite");
        var btnOverwriteAll = new TaskDialogButton("Overwrite &All");
        var btnSkip         = new TaskDialogButton("&Skip");
        var btnSkipAll      = new TaskDialogButton("Skip A&ll");

        string details;
        try
        {
            var fi = new FileInfo(filePath);
            var si = new FileInfo(sidecarPath);
            details = $"File:             {fi.Name}\n"                        +
                      $"Size:             {fi.Length:N0} bytes\n"             +
                      $"Modified:         {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n\n" +
                      $"Existing sidecar: {si.Name}\n"                        +
                      $"Sidecar written:  {si.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
        }
        catch
        {
            details = $"File: {Path.GetFileName(filePath)}\n" +
                      $"Sidecar: {Path.GetFileName(sidecarPath)}";
        }

        var page = new TaskDialogPage
        {
            Caption       = "Sidecar Already Exists",
            Heading       = "This file already has a sidecar hash file",
            Text          = details + "\n\nRe-hash and overwrite the sidecar, or skip this file?",
            Icon          = TaskDialogIcon.Warning,
            DefaultButton = btnSkip,
            Buttons       = { btnOverwrite, btnOverwriteAll, btnSkip, btnSkipAll }
        };

        var clicked = TaskDialog.ShowDialog(this, page);

        if (clicked == btnOverwriteAll) return SidecarConflictAction.OverwriteAll;
        if (clicked == btnOverwrite)    return SidecarConflictAction.Overwrite;
        if (clicked == btnSkipAll)      return SidecarConflictAction.SkipAll;
        return SidecarConflictAction.Skip;
    }

    // ── About dialog ─────────────────────────────────────────────────────────

    private void ShowAboutDialog()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string version = ver is null ? "0.1" : $"{ver.Major}.{ver.Minor}.{ver.Build}";

        var page = new TaskDialogPage
        {
            Caption = "About FileHasher",
            Heading = "FileHasher",
            Text    = $"Version {version}\n\n"                        +
                      "A file and folder hashing utility for Windows.\n\n" +
                      "Author:    Fabian Santiago\n"                   +
                      "Copyright © 2026 FSP Productions, LLC",
            Icon    = TaskDialogIcon.Information,
            Buttons = { TaskDialogButton.OK }
        };

        TaskDialog.ShowDialog(this, page);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetRunning(bool running)
    {
        _runBtn.Enabled       = !running;
        _stopBtn.Enabled      = running;
        _browseFileBtn.Enabled   = !running;
        _browseFolderBtn.Enabled = !running;
        _runAsAdminBtn.Enabled   = !running && !IsAdmin();
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>Marshals <paramref name="action"/> to the UI thread if needed.</summary>
    private void SafeInvoke(Action action)
    {
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }

    private void OpenLogFolder()
    {
        if (_logger is null) return;
        var dir = Path.GetDirectoryName(_logger.LogPath);
        if (dir is not null && Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
    }

    private string GetSelectedAlgorithm()
    {
        if (_rdMd5.Checked)    return "MD5";
        if (_rdSha1.Checked)   return "SHA1";
        if (_rdSha512.Checked) return "SHA512";
        return "SHA256";
    }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        _logger?.Dispose();
        base.OnFormClosing(e);
    }

    // ── Custom progress bar (owner-drawn, supports color change on completion) ──

    private sealed class ColorProgressBar : Panel
    {
        public enum BarState { Normal, Marquee, Complete }

        private int      _maximum = 100;
        private int      _value   = 0;
        private BarState _state   = BarState.Normal;
        private int      _marqueeOffset = 0;

        private readonly System.Windows.Forms.Timer _marqueeTimer;

        private static readonly Color NormalColor   = Color.FromArgb(6,   176,  37);   // system green
        private static readonly Color CompleteColor = Color.FromArgb(0,   120, 215);   // Windows blue
        private static readonly Color TrackColor    = Color.FromArgb(225, 225, 225);   // light grey track

        public int Maximum
        {
            get => _maximum;
            set { _maximum = Math.Max(1, value); Invalidate(); }
        }

        public int Value
        {
            get => _value;
            set { _value = Math.Max(0, Math.Min(value, _maximum)); Invalidate(); }
        }

        public BarState State
        {
            get => _state;
            set
            {
                _state = value;
                _marqueeTimer.Enabled = (value == BarState.Marquee);
                if (value != BarState.Marquee)
                    _marqueeOffset = 0;
                Invalidate();
            }
        }

        public ColorProgressBar()
        {
            DoubleBuffered = true;
            BackColor      = TrackColor;

            _marqueeTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _marqueeTimer.Tick += (_, _) =>
            {
                _marqueeOffset = (_marqueeOffset + 5) % (Width + 80);
                Invalidate();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g     = e.Graphics;
            var inner = new Rectangle(1, 1, ClientSize.Width - 2, ClientSize.Height - 2);

            // Track
            using (var trackBrush = new SolidBrush(TrackColor))
                g.FillRectangle(trackBrush, inner);

            if (_state == BarState.Marquee)
            {
                int blockW = Math.Max(inner.Width / 4, 40);
                int x      = inner.Left + _marqueeOffset - blockW - 20;
                int clampL = Math.Max(inner.Left, x);
                int clampR = Math.Min(inner.Right, x + blockW);
                if (clampR > clampL)
                    using (var b = new SolidBrush(NormalColor))
                        g.FillRectangle(b, clampL, inner.Top, clampR - clampL, inner.Height);
            }
            else if (_maximum > 0 && _value > 0)
            {
                int   fillW = (int)((double)_value / _maximum * inner.Width);
                Color fill  = _state == BarState.Complete ? CompleteColor : NormalColor;
                using var b = new SolidBrush(fill);
                g.FillRectangle(b, inner.Left, inner.Top, fillW, inner.Height);
            }

            // Border
            ControlPaint.DrawBorder(g, ClientRectangle,
                Color.FromArgb(180, 180, 180), ButtonBorderStyle.Solid);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _marqueeTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
