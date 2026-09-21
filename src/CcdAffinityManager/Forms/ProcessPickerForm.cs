using System.ComponentModel;
using System.Diagnostics;
using CcdAffinityManager.Models;
using CcdAffinityManager.Services;

namespace CcdAffinityManager.Forms;

internal sealed class ProcessPickerForm : Form
{
    private readonly TextBox _filterTextBox = new();
    private readonly ListView _processList = new();
    private readonly Label _loadingLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _selectButton = new();
    private readonly Button _cancelButton = new();
    private IReadOnlyList<ProcessCandidate> _allCandidates = [];
    private bool _loading;

    public ProcessPickerForm()
    {
        Text = "选择正在运行的进程";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(880, 540);
        MinimumSize = new Size(700, 420);
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildLayout();
        Shown += async (_, _) => await LoadProcessesAsync();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ProcessSelection? SelectedProcess { get; private set; }

    private void BuildLayout()
    {
        var filterPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 9, 12, 9)
        };

        var filterLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        filterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));

        var filterLabel = new Label
        {
            Text = "进程名或路径：",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _filterTextBox.Dock = DockStyle.Fill;
        _filterTextBox.Margin = new Padding(0, 2, 12, 2);
        _filterTextBox.PlaceholderText = "输入进程名或路径";
        _filterTextBox.TextChanged += (_, _) => PopulateList();

        _refreshButton.Text = "刷新";
        _refreshButton.Dock = DockStyle.Fill;
        _refreshButton.Margin = new Padding(0);
        _refreshButton.Click += async (_, _) => await LoadProcessesAsync();

        filterLayout.Controls.Add(filterLabel, 0, 0);
        filterLayout.Controls.Add(_filterTextBox, 1, 0);
        filterLayout.Controls.Add(_refreshButton, 2, 0);
        filterPanel.Controls.Add(filterLayout);

        _processList.Dock = DockStyle.Fill;
        _processList.View = View.Details;
        _processList.FullRowSelect = true;
        _processList.MultiSelect = false;
        _processList.GridLines = true;
        _processList.HideSelection = false;
        _processList.ShowItemToolTips = true;
        _processList.Columns.Add("进程", 220);
        _processList.Columns.Add("实例", 60, HorizontalAlignment.Right);
        _processList.Columns.Add("匹配方式 / EXE 路径", 520);
        _processList.SelectedIndexChanged += (_, _) =>
        {
            _selectButton.Enabled = !_loading && _processList.SelectedItems.Count > 0;
        };
        _processList.DoubleClick += (_, _) => AcceptSelection();

        _loadingLabel.Dock = DockStyle.Fill;
        _loadingLabel.Text = "正在读取进程信息…";
        _loadingLabel.TextAlign = ContentAlignment.MiddleCenter;
        _loadingLabel.ForeColor = SystemColors.GrayText;
        _loadingLabel.Visible = false;

        var listHost = new Panel { Dock = DockStyle.Fill };
        listHost.Controls.Add(_loadingLabel);
        listHost.Controls.Add(_processList);

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            Padding = new Padding(12, 9, 12, 9)
        };

        _selectButton.Text = "选择";
        _selectButton.Size = new Size(90, 32);
        _selectButton.Enabled = false;
        _selectButton.Click += (_, _) => AcceptSelection();

        _cancelButton.Text = "取消";
        _cancelButton.Size = new Size(90, 32);
        _cancelButton.DialogResult = DialogResult.Cancel;

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 200,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        flow.Controls.Add(_cancelButton);
        flow.Controls.Add(_selectButton);
        buttonPanel.Controls.Add(flow);

        Controls.Add(listHost);
        Controls.Add(buttonPanel);
        Controls.Add(filterPanel);

        AcceptButton = _selectButton;
        CancelButton = _cancelButton;
    }

    private async Task LoadProcessesAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        _loadingLabel.Visible = true;
        _processList.Visible = false;
        _refreshButton.Enabled = false;
        _selectButton.Enabled = false;

        var loaded = false;
        try
        {
            _allCandidates = await Task.Run(ReadProcessCandidates);
            _loading = false;
            loaded = true;
            PopulateList();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _loadingLabel.Text = $"读取进程失败：{exception.Message}";
        }
        finally
        {
            _loading = false;
            _loadingLabel.Visible = !loaded;
            _processList.Visible = loaded;
            _refreshButton.Enabled = true;
        }
    }

    private void PopulateList()
    {
        if (_loading)
        {
            return;
        }

        var filter = _filterTextBox.Text.Trim();
        var selectedKey = (_processList.SelectedItems.Count > 0
            ? _processList.SelectedItems[0].Tag as ProcessCandidate
            : null)?.Key;

        _processList.BeginUpdate();
        try
        {
            _processList.Items.Clear();
            foreach (var candidate in _allCandidates
                         .Where(candidate =>
                             filter.Length == 0 ||
                             candidate.DisplayName.Contains(
                                 filter,
                                 StringComparison.OrdinalIgnoreCase) ||
                             candidate.ProcessName.Contains(
                                 filter,
                                 StringComparison.OrdinalIgnoreCase) ||
                             candidate.ExecutablePath?.Contains(
                                 filter,
                                 StringComparison.OrdinalIgnoreCase) == true)
                         .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(candidate.DisplayName);
                item.SubItems.Add(candidate.ProcessCount.ToString());
                item.SubItems.Add(candidate.MatchTargetText);
                item.Tag = candidate;
                item.ToolTipText = candidate.MatchTargetText;
                _processList.Items.Add(item);

                if (selectedKey is not null &&
                    string.Equals(selectedKey, candidate.Key, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.EnsureVisible();
                }
            }
        }
        finally
        {
            _processList.EndUpdate();
        }

        _selectButton.Enabled = _processList.SelectedItems.Count > 0;
    }

    private void AcceptSelection()
    {
        if (_processList.SelectedItems.Count == 0 ||
            _processList.SelectedItems[0].Tag is not ProcessCandidate candidate)
        {
            return;
        }

        SelectedProcess = new ProcessSelection(
            candidate.DisplayName,
            candidate.ProcessName,
            candidate.ExecutablePath,
            candidate.MatchByProcessName);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static IReadOnlyList<ProcessCandidate> ReadProcessCandidates()
    {
        var candidates = new Dictionary<string, ProcessCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                var executablePath = ProcessInspector.TryGetExecutablePath(process);
                var matchByProcessName = string.IsNullOrWhiteSpace(executablePath);
                var key = matchByProcessName
                    ? $"name:{processName}"
                    : $"path:{executablePath}";

                if (candidates.TryGetValue(key, out var existing))
                {
                    candidates[key] = existing with
                    {
                        ProcessCount = existing.ProcessCount + 1
                    };
                }
                else
                {
                    candidates[key] = new ProcessCandidate(
                        key,
                        Path.GetFileNameWithoutExtension(executablePath!) ??
                        processName,
                        processName,
                        executablePath,
                        matchByProcessName,
                        1);
                }
            }
        }

        return candidates.Values.ToArray();
    }

    private sealed record ProcessCandidate(
        string Key,
        string DisplayName,
        string ProcessName,
        string? ExecutablePath,
        bool MatchByProcessName,
        int ProcessCount)
    {
        public string MatchTargetText => MatchByProcessName
            ? "按进程名匹配（无法读取 EXE 路径）"
            : ExecutablePath ?? string.Empty;
    }
}
