using System.ComponentModel;
using System.Diagnostics;
using CcdAffinityManager.Forms;
using CcdAffinityManager.Models;
using CcdAffinityManager.Services;

namespace CcdAffinityManager;

public sealed class MainForm : Form
{
    private readonly CpuTopology _topology;
    private readonly RuleStore _ruleStore = new();
    private readonly AffinityService _affinityService;
    private readonly BindingList<AffinityRule> _rules;
    private readonly object _rulesGate = new();
    private readonly DataGridView _ruleGrid = new();
    private readonly ToolStripButton _monitorButton = new();
    private readonly ToolStripButton _applyNowButton = new();
    private readonly ToolStripButton _editAffinityButton = new();
    private readonly ToolStripButton _launchButton = new();
    private readonly ToolStripButton _deleteButton = new();
    private readonly ToolStripButton _autoStartButton = new();
    private readonly Label _modeLabel = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _configLabel = new();
    private ProcessStartWatcher? _processStartWatcher;
    private bool _monitoring;
    private bool _closing;
    private bool _suppressAutoStart;
    private int _scanInProgress;

    public MainForm()
    {
        _topology = CpuTopologyService.Detect();
        _affinityService = new AffinityService(_topology.SystemMask);

        var settings = _ruleStore.Load();
        NormalizeRules(settings.Rules);
        _rules = new BindingList<AffinityRule>(settings.Rules);

        Text = "X3D CCD 亲和度管理器";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 700);
        MinimumSize = new Size(920, 560);
        Font = new Font("Microsoft YaHei UI", 9F);
        KeyPreview = true;

        BuildLayout();
        InitializeAutoStartState();

        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            RefreshUiState();
            SetStatus("就绪。规则会自动缓存，点击“启用监控”后开始生效。");
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildToolStrip(), 0, 1);
        root.Controls.Add(BuildRuleGrid(), 0, 2);
        root.Controls.Add(BuildStatusStrip(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeaderPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 11, 18, 8),
            BackColor = Color.FromArgb(244, 247, 250)
        };

        var titleLabel = new Label
        {
            Text = "X3D CCD 亲和度管理器",
            AutoSize = true,
            Location = new Point(18, 11),
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold)
        };

        _modeLabel.AutoSize = true;
        _modeLabel.Location = new Point(19, 48);
        _modeLabel.ForeColor = SystemColors.GrayText;
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(_modeLabel);
        return panel;
    }

    private Control BuildToolStrip()
    {
        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(8, 3, 8, 3),
            AutoSize = true
        };

        toolStrip.Items.Add(CreateToolButton("添加进程", "选择当前正在运行的程序", AddRunningProcess));
        toolStrip.Items.Add(CreateToolButton("添加 EXE", "直接选择要管理的 EXE", AddExecutable));
        toolStrip.Items.Add(new ToolStripSeparator());
        _editAffinityButton.Text = "修改亲和度";
        _editAffinityButton.ToolTipText = "修改选中规则的逻辑处理器";
        _editAffinityButton.Click += (_, _) => EditSelectedAffinity();
        toolStrip.Items.Add(_editAffinityButton);

        _launchButton.Text = "启动程序";
        _launchButton.ToolTipText = "启动选中的 EXE，并在监控开启时自动绑定";
        _launchButton.Click += (_, _) => LaunchSelectedRule();
        toolStrip.Items.Add(_launchButton);

        _applyNowButton.Text = "立即应用";
        _applyNowButton.ToolTipText = "立即对所有启用规则执行一次绑定";
        _applyNowButton.Click += (_, _) => ApplyNow();
        toolStrip.Items.Add(_applyNowButton);

        _deleteButton.Text = "删除";
        _deleteButton.ToolTipText = "删除选中的规则";
        _deleteButton.Click += (_, _) => DeleteSelectedRule();
        toolStrip.Items.Add(_deleteButton);

        toolStrip.Items.Add(new ToolStripSeparator());
        _monitorButton.Text = "启用监控";
        _monitorButton.Font = new Font(toolStrip.Font, FontStyle.Bold);
        _monitorButton.ToolTipText = "启用后立即应用规则，并监听随后启动的匹配进程";
        _monitorButton.Click += (_, _) => ToggleMonitoring();
        toolStrip.Items.Add(_monitorButton);

        _autoStartButton.Text = "开机启动";
        _autoStartButton.CheckOnClick = true;
        _autoStartButton.Alignment = ToolStripItemAlignment.Right;
        _autoStartButton.ToolTipText = "登录 Windows 后自动启动本工具，默认不启动监控";
        _autoStartButton.CheckedChanged += (_, _) => AutoStartChanged();
        toolStrip.Items.Add(_autoStartButton);

        return toolStrip;
    }

    private Control BuildRuleGrid()
    {
        _ruleGrid.Dock = DockStyle.Fill;
        _ruleGrid.AutoGenerateColumns = false;
        _ruleGrid.AllowUserToAddRows = false;
        _ruleGrid.AllowUserToDeleteRows = false;
        _ruleGrid.AllowUserToResizeColumns = true;
        _ruleGrid.AllowUserToResizeRows = false;
        _ruleGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _ruleGrid.RowHeadersVisible = false;
        _ruleGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _ruleGrid.MultiSelect = false;
        _ruleGrid.BackgroundColor = SystemColors.Window;
        _ruleGrid.BorderStyle = BorderStyle.None;
        _ruleGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _ruleGrid.ColumnHeadersHeight = 36;
        _ruleGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _ruleGrid.RowTemplate.Height = 34;
        _ruleGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
        _ruleGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 230, 246);
        _ruleGrid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        _ruleGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _ruleGrid.DataSource = _rules;

        _ruleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(AffinityRule.Enabled),
            HeaderText = "启用",
            Width = 54,
            FlatStyle = FlatStyle.Flat,
            ReadOnly = false
        });
        _ruleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AffinityRule.DisplayName),
            HeaderText = "应用",
            Width = 170,
            ReadOnly = true
        });
        _ruleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AffinityRule.MatchTargetText),
            HeaderText = "EXE 路径",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 500,
            MinimumWidth = 240,
            ReadOnly = true
        });
        _ruleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AffinityRule.AffinityText),
            HeaderText = "CPU 亲和度",
            Width = 215,
            ReadOnly = true
        });
        _ruleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AffinityRule.RuntimeStatus),
            HeaderText = "状态",
            Width = 190,
            ReadOnly = true
        });

        _ruleGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_ruleGrid.IsCurrentCellDirty &&
                _ruleGrid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _ruleGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _ruleGrid.CellValueChanged += RuleGridCellValueChanged;
        _ruleGrid.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex >= 0 &&
                _ruleGrid.Columns[eventArgs.ColumnIndex].DataPropertyName ==
                nameof(AffinityRule.Enabled))
            {
                return;
            }

            EditSelectedAffinity();
        };
        _ruleGrid.SelectionChanged += (_, _) => RefreshUiState();
        _ruleGrid.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Delete)
            {
                DeleteSelectedRule();
                eventArgs.Handled = true;
            }
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("修改亲和度", null, (_, _) => EditSelectedAffinity());
        contextMenu.Items.Add("启动程序", null, (_, _) => LaunchSelectedRule());
        contextMenu.Items.Add("立即应用", null, (_, _) => ApplyNow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("删除规则", null, (_, _) => DeleteSelectedRule());
        _ruleGrid.ContextMenuStrip = contextMenu;

        return _ruleGrid;
    }

    private Control BuildStatusStrip()
    {
        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Fill,
            SizingGrip = false
        };

        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _configLabel.Text = $"缓存：{_ruleStore.SettingsPath}";
        _configLabel.ForeColor = SystemColors.GrayText;
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(_configLabel);
        return statusStrip;
    }

    private static ToolStripButton CreateToolButton(
        string text,
        string toolTip,
        Action action)
    {
        var button = new ToolStripButton
        {
            Text = text,
            ToolTipText = toolTip,
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void NormalizeRules(IEnumerable<AffinityRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.AffinityMask == 0 ||
                (rule.AffinityMask & _topology.SystemMask) == 0)
            {
                rule.AffinityMask = _topology.RecommendedMask;
            }

            if (string.IsNullOrWhiteSpace(rule.DisplayName))
            {
                rule.DisplayName = string.IsNullOrWhiteSpace(rule.ProcessName)
                    ? Path.GetFileNameWithoutExtension(rule.ExecutablePath)
                    : rule.ProcessName;
            }

            if (string.IsNullOrWhiteSpace(rule.ProcessName))
            {
                rule.ProcessName = Path.GetFileNameWithoutExtension(rule.ExecutablePath);
            }

            if (string.IsNullOrWhiteSpace(rule.ExecutablePath))
            {
                rule.MatchByProcessName = true;
            }

            rule.SetRuntimeStatus(rule.Enabled ? "等待监控" : "已暂停");
        }
    }

    private void InitializeAutoStartState()
    {
        try
        {
            _suppressAutoStart = true;
            _autoStartButton.Checked = StartupService.IsEnabled();
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _autoStartButton.Enabled = false;
        }
        finally
        {
            _suppressAutoStart = false;
        }
    }

    private void AutoStartChanged()
    {
        if (_suppressAutoStart)
        {
            return;
        }

        try
        {
            StartupService.SetEnabled(_autoStartButton.Checked);
            SetStatus(_autoStartButton.Checked
                ? "已设置 Windows 登录后自动启动。"
                : "已取消 Windows 登录后自动启动。");
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _suppressAutoStart = true;
            _autoStartButton.Checked = !_autoStartButton.Checked;
            _suppressAutoStart = false;
            MessageBox.Show(
                this,
                $"无法修改开机启动项：{exception.Message}",
                "开机启动",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void AddRunningProcess()
    {
        using var picker = new ProcessPickerForm();
        if (picker.ShowDialog(this) == DialogResult.OK &&
            picker.SelectedProcess is not null)
        {
            AddRule(picker.SelectedProcess);
        }
    }

    private void AddExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择要绑定 CCD 的程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddRule(dialog.FileName);
        }
    }

    private void AddRule(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        AddRule(new ProcessSelection(
            processName,
            processName,
            executablePath,
            false));
    }

    private void AddRule(ProcessSelection selection)
    {
        var normalizedPath = string.Empty;
        if (!selection.MatchByProcessName &&
            !string.IsNullOrWhiteSpace(selection.ExecutablePath))
        {
            try
            {
                normalizedPath = Path.GetFullPath(selection.ExecutablePath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                MessageBox.Show(
                    this,
                    $"EXE 路径无效：{exception.Message}",
                    "添加规则",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        var matchByProcessName =
            selection.MatchByProcessName || string.IsNullOrWhiteSpace(normalizedPath);

        AffinityRule? existing;
        lock (_rulesGate)
        {
            existing = _rules.FirstOrDefault(rule => matchByProcessName
                ? rule.MatchByProcessName &&
                  string.Equals(
                      rule.ProcessName,
                      selection.ProcessName,
                      StringComparison.OrdinalIgnoreCase)
                : !rule.MatchByProcessName &&
                  ProcessInspector.PathsEqual(rule.ExecutablePath, normalizedPath));
        }

        if (existing is not null)
        {
            SelectRule(existing);
            SetStatus($"该程序已经存在规则：{existing.DisplayName}");
            return;
        }

        if (matchByProcessName &&
            MessageBox.Show(
                this,
                $"无法读取 {selection.ProcessName} 的 EXE 路径。\r\n\r\n" +
                "该规则将按进程名匹配，会影响所有使用同名 EXE 的进程。是否继续？",
                "按进程名添加规则",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var rule = new AffinityRule
        {
            DisplayName = selection.DisplayName,
            ProcessName = selection.ProcessName,
            ExecutablePath = normalizedPath,
            AffinityMask = _topology.RecommendedMask,
            Enabled = true,
            MatchByProcessName = matchByProcessName
        };
        rule.SetRuntimeStatus(_monitoring ? "等待应用" : "等待监控");

        lock (_rulesGate)
        {
            _rules.Add(rule);
        }

        SaveRules();
        SelectRule(rule);
        ReconcileProcessStartWatcher();
        if (_monitoring)
        {
            TriggerScan();
        }

        SetStatus(
            matchByProcessName
                ? $"已按进程名添加 {rule.DisplayName}，默认绑定 {rule.AffinityText}。"
                : $"已添加 {rule.DisplayName}，默认绑定 {rule.AffinityText}。");
    }

    private void EditSelectedAffinity()
    {
        var rule = GetSelectedRule();
        if (rule is null)
        {
            return;
        }

        using var editor = new AffinityEditorForm(
            rule.DisplayName,
            rule.AffinityMask,
            _topology);

        if (editor.ShowDialog(this) != DialogResult.OK ||
            editor.SelectedMask == rule.AffinityMask)
        {
            return;
        }

        var restartWatcher = _monitoring && rule.Enabled;
        if (restartWatcher)
        {
            DisposeProcessStartWatcher();
            _affinityService.RestoreRule(rule.Id);
        }

        rule.AffinityMask = editor.SelectedMask;
        rule.SetRuntimeStatus(rule.Enabled ? "等待应用" : "已暂停");
        SaveRules();

        if (restartWatcher)
        {
            ReconcileProcessStartWatcher();
            TriggerScan();
        }

        SetStatus($"{rule.DisplayName} 的亲和度已改为 {rule.AffinityText}。");
    }

    private void LaunchSelectedRule()
    {
        var rule = GetSelectedRule();
        if (rule is null)
        {
            return;
        }

        if (rule.MatchByProcessName || string.IsNullOrWhiteSpace(rule.ExecutablePath))
        {
            MessageBox.Show(
                this,
                "该规则无法读取原始 EXE 路径，因此不能由本工具直接启动。\r\n" +
                "请通过 Steam、启动器或快捷方式正常启动程序，监控会自动应用亲和度。",
                "启动程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!File.Exists(rule.ExecutablePath))
        {
            MessageBox.Show(
                this,
                "规则中的 EXE 文件不存在，请删除后重新添加。",
                "启动程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var bindAtLaunch = _monitoring && rule.Enabled;
            if (bindAtLaunch)
            {
                var launchResult = SuspendedProcessLauncher.LaunchWithAffinity(
                    rule.ExecutablePath,
                    null,
                    Path.GetDirectoryName(rule.ExecutablePath) ?? string.Empty,
                    rule.AffinityMask & _topology.SystemMask);

                if (launchResult.AffinityError is null &&
                    _affinityService.TrackLaunchedProcess(
                        launchResult.ProcessId,
                        rule.Id,
                        launchResult.OriginalMask))
                {
                    rule.SetRuntimeStatus("已启动，启动时已绑定");
                    SetStatus($"已启动 {rule.DisplayName}，并在进程启动前绑定 {rule.AffinityText}。");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(launchResult.AffinityError))
                {
                    rule.SetRuntimeStatus($"启动成功，但绑定失败：{TrimStatus(launchResult.AffinityError)}");
                    SetStatus($"已启动 {rule.DisplayName}，但启动时绑定失败。");
                }
                else
                {
                    rule.SetRuntimeStatus("启动成功，正在确认亲和度");
                    SetStatus($"已启动 {rule.DisplayName}，正在确认亲和度。");
                }

                HandleProcessStarted(launchResult.ProcessId, rule.ExecutableName);
                return;
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = rule.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(rule.ExecutablePath) ?? string.Empty,
                UseShellExecute = true
            });

            if (_monitoring && process is not null)
            {
                HandleProcessStarted(process.Id, rule.ExecutableName);
            }

            SetStatus($"已启动 {rule.DisplayName}。");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or
            System.Security.SecurityException)
        {
            MessageBox.Show(
                this,
                $"启动程序失败：{exception.Message}",
                "启动程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ApplyNow()
    {
        if (!_rules.Any(rule => rule.Enabled))
        {
            SetStatus("没有启用中的规则。");
            return;
        }

        TriggerScan(force: true);
    }

    private void DeleteSelectedRule()
    {
        var rule = GetSelectedRule();
        if (rule is null)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"确定删除规则“{rule.DisplayName}”吗？\r\n\r\n删除时会恢复该程序当前的原始亲和度。",
                "删除规则",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var restartWatcher = _monitoring;
        if (restartWatcher)
        {
            DisposeProcessStartWatcher();
        }

        rule.Enabled = false;
        var restoreResult = _affinityService.RestoreRule(rule.Id);

        lock (_rulesGate)
        {
            _rules.Remove(rule);
        }

        SaveRules();
        ReconcileProcessStartWatcher();
        RefreshUiState();
        ReportRestoreErrors(restoreResult, "删除规则时");
        SetStatus($"已删除规则：{rule.DisplayName}。");
    }

    private void ToggleMonitoring()
    {
        if (_monitoring)
        {
            _monitoring = false;
            DisposeProcessStartWatcher();

            var restoreResult = _affinityService.RestoreAll();
            foreach (var rule in _rules.Where(rule => rule.Enabled))
            {
                rule.SetRuntimeStatus("等待监控");
            }

            RefreshUiState();
            ReportRestoreErrors(restoreResult, "暂停监控时");
            SetStatus(
                restoreResult.RestoredProcesses > 0
                    ? $"监控已暂停，已恢复 {restoreResult.RestoredProcesses} 个进程。"
                    : "监控已暂停。");
            return;
        }

        _monitoring = true;
        ReconcileProcessStartWatcher();
        RefreshUiState();
        TriggerScan();
        SetStatus("监控已启用，正在应用现有规则。");
    }

    private void RuleGridCellValueChanged(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 ||
            eventArgs.ColumnIndex < 0 ||
            _ruleGrid.Columns[eventArgs.ColumnIndex].DataPropertyName !=
            nameof(AffinityRule.Enabled))
        {
            return;
        }

        var rule = _ruleGrid.Rows[eventArgs.RowIndex].DataBoundItem as AffinityRule;
        if (rule is null)
        {
            return;
        }

        if (rule.Enabled)
        {
            rule.SetRuntimeStatus(_monitoring ? "等待应用" : "等待监控");
            SaveRules();
            ReconcileProcessStartWatcher();
            if (_monitoring)
            {
                TriggerScan();
            }

            SetStatus($"规则已启用：{rule.DisplayName}。");
            return;
        }

        DisposeProcessStartWatcher();
        var restoreResult = _affinityService.RestoreRule(rule.Id);
        rule.SetRuntimeStatus("已暂停");
        SaveRules();
        ReconcileProcessStartWatcher();
        ReportRestoreErrors(restoreResult, "停用规则时");
        SetStatus($"规则已暂停：{rule.DisplayName}。");
    }

    private void TriggerScan(bool force = false)
    {
        if ((!force && !_monitoring) || _closing)
        {
            return;
        }

        var rules = SnapshotRules();
        if (!rules.Any(rule => rule.Enabled))
        {
            ReconcileProcessStartWatcher();
            return;
        }

        if (Interlocked.Exchange(ref _scanInProgress, 1) != 0)
        {
            return;
        }

        Task.Run(() =>
        {
            ScanSummary? summary = null;
            string? error = null;

            try
            {
                summary = _affinityService.Synchronize(rules);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or
                Win32Exception or NotSupportedException)
            {
                error = exception.Message;
            }
            finally
            {
                Interlocked.Exchange(ref _scanInProgress, 0);
            }

            if (_closing)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                if (error is not null)
                {
                    SetStatus($"应用规则失败：{error}");
                    return;
                }

                if (summary is not null)
                {
                    ApplyScanSummary(summary);
                }
            });
        });
    }

    private void ApplyScanSummary(ScanSummary summary)
    {
        foreach (var result in summary.RuleResults)
        {
            ApplyRuleScanResult(result);
        }

        SetStatus(
            $"已同步 {summary.EnabledRules} 条规则，" +
            $"匹配 {summary.MatchedProcesses} 个进程，" +
            $"成功 {summary.AppliedProcesses} 个" +
            (summary.FailedProcesses > 0
                ? $"，{summary.FailedProcesses} 个错误。"
                : "。"));
    }

    private void ApplyRuleScanResult(RuleScanResult result)
    {
        AffinityRule? rule;
        lock (_rulesGate)
        {
            rule = _rules.FirstOrDefault(item => item.Id == result.RuleId);
        }

        if (rule is null)
        {
            return;
        }

        if (!rule.Enabled)
        {
            rule.SetRuntimeStatus("已暂停");
            return;
        }

        if (result.Errors.Count > 0)
        {
            rule.SetRuntimeStatus($"部分失败：{TrimStatus(result.Errors[0])}");
        }
        else if (result.MatchedProcesses == 0)
        {
            rule.SetRuntimeStatus(_monitoring ? "监控中，等待进程启动" : "等待监控");
        }
        else
        {
            rule.SetRuntimeStatus($"已应用，共 {result.AppliedProcesses} 个进程");
        }
    }

    private void HandleProcessStarted(int processId, string processName)
    {
        if (!_monitoring || _closing)
        {
            return;
        }

        var rules = SnapshotRules();
        if (!rules.Any(rule => rule.Enabled))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(processName) &&
            !rules.Any(rule =>
                string.Equals(
                    NormalizeProcessName(rule.ProcessName),
                    NormalizeProcessName(processName),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Task.Run(async () =>
        {
            await Task.Delay(150);
            if (!_monitoring || _closing)
            {
                return;
            }

            RuleScanResult? result = null;
            try
            {
                result = _affinityService.SynchronizeProcess(processId, processName, rules);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // A process can exit while its start notification is being handled.
            }

            if (result is null || _closing)
            {
                return;
            }

            RunOnUiThread(() => ApplyRuleScanResult(result));
        });
    }

    private void ReconcileProcessStartWatcher()
    {
        var shouldWatch = _monitoring &&
                          !_closing &&
                          _rules.Any(rule => rule.Enabled);

        if (shouldWatch)
        {
            if (_processStartWatcher is null)
            {
                _processStartWatcher = new ProcessStartWatcher();
                _processStartWatcher.ProcessStarted += ProcessStartWatcherProcessStarted;
                _processStartWatcher.Start();
            }
        }
        else
        {
            DisposeProcessStartWatcher();
        }

        RefreshUiState();
    }

    private void ProcessStartWatcherProcessStarted(
        object? sender,
        ProcessStartedEventArgs eventArgs)
    {
        HandleProcessStarted(eventArgs.ProcessId, eventArgs.ProcessName);
    }

    private void DisposeProcessStartWatcher()
    {
        if (_processStartWatcher is null)
        {
            return;
        }

        _processStartWatcher.ProcessStarted -= ProcessStartWatcherProcessStarted;
        _processStartWatcher.Dispose();
        _processStartWatcher = null;
    }

    private void SaveRules()
    {
        try
        {
            AffinityRule[] snapshot;
            lock (_rulesGate)
            {
                snapshot = _rules.Select(rule => rule.Clone()).ToArray();
            }

            _ruleStore.Save(new AppSettings { Rules = snapshot.ToList() });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"保存规则失败：{exception.Message}");
        }
    }

    private AffinityRule[] SnapshotRules()
    {
        lock (_rulesGate)
        {
            return _rules.Select(rule => rule.Clone()).ToArray();
        }
    }

    private AffinityRule? GetSelectedRule()
    {
        return _ruleGrid.CurrentRow?.DataBoundItem as AffinityRule;
    }

    private void SelectRule(AffinityRule rule)
    {
        foreach (DataGridViewRow row in _ruleGrid.Rows)
        {
            if (!ReferenceEquals(row.DataBoundItem, rule))
            {
                continue;
            }

            row.Selected = true;
            row.Cells[0].Selected = true;
            _ruleGrid.CurrentCell = row.Cells[0];
            return;
        }
    }

    private void RefreshUiState()
    {
        var hasSelection = GetSelectedRule() is not null;
        _editAffinityButton.Enabled = hasSelection;
        _launchButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
        _applyNowButton.Enabled = _rules.Any(rule => rule.Enabled);
        _monitorButton.Text = _monitoring ? "暂停监控" : "启用监控";

        var privilege = ElevationService.IsAdministrator() ? "管理员" : "标准权限";
        var watcherMode = _processStartWatcher?.ModeDescription ?? "监控未运行";
        _modeLabel.Text =
            $"{_topology.RecommendedDescription}  |  " +
            $"权限：{privilege}  |  检测：{watcherMode}";
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private void ReportRestoreErrors(RestoreResult result, string action)
    {
        if (result.Errors.Count == 0)
        {
            return;
        }

        var details = string.Join(
            Environment.NewLine,
            result.Errors.Take(5));
        MessageBox.Show(
            this,
            $"{action}有 {result.Errors.Count} 个进程未能恢复：{Environment.NewLine}{Environment.NewLine}{details}",
            "恢复亲和度",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _closing = true;
        DisposeProcessStartWatcher();
        var restoreResult = _affinityService.RestoreAll();

        if (restoreResult.Errors.Count > 0 &&
            eventArgs.CloseReason == CloseReason.UserClosing)
        {
            var details = string.Join(
                Environment.NewLine,
                restoreResult.Errors.Take(5));
            var answer = MessageBox.Show(
                this,
                $"有 {restoreResult.Errors.Count} 个进程无法恢复原始亲和度：{Environment.NewLine}" +
                $"{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}" +
                "仍要退出吗？",
                "退出并恢复",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer == DialogResult.No)
            {
                _closing = false;
                ReconcileProcessStartWatcher();
                eventArgs.Cancel = true;
            }
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
            // The window can close while a background scan is finishing.
        }
    }

    private static string TrimStatus(string value)
    {
        const int maximumLength = 48;
        return value.Length <= maximumLength
            ? value
            : value[..(maximumLength - 1)] + "…";
    }

    private static string NormalizeProcessName(string processName)
    {
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }
}
