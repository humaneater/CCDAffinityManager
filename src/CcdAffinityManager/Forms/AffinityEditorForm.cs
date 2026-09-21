using System.ComponentModel;
using System.Numerics;
using CcdAffinityManager.Services;

namespace CcdAffinityManager.Forms;

internal sealed class AffinityEditorForm : Form
{
    private readonly CpuTopology _topology;
    private readonly Dictionary<int, CheckBox> _processorCheckBoxes = [];
    private readonly Label _selectionLabel = new();
    private readonly Button _okButton = new();

    public AffinityEditorForm(
        string applicationName,
        ulong initialMask,
        CpuTopology topology)
    {
        _topology = topology;

        Text = $"修改 CPU 亲和度 - {applicationName}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 460);
        MinimumSize = new Size(680, 400);
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildLayout(initialMask);
        UpdateSelectionState();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ulong SelectedMask { get; private set; }

    private void BuildLayout(ulong initialMask)
    {
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            Padding = new Padding(16, 12, 16, 8)
        };

        var titleLabel = new Label
        {
            Text = "选择允许该程序使用的逻辑处理器",
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 13)
        };

        var recommendationLabel = new Label
        {
            Text = _topology.RecommendedDescription,
            AutoSize = true,
            ForeColor = Color.FromArgb(32, 96, 64),
            Location = new Point(16, 45)
        };

        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(recommendationLabel);

        var processorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 8),
            AutoScroll = true
        };

        var table = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 8,
            RowCount = 1,
            Dock = DockStyle.Top
        };

        var processors = GetAvailableProcessors(_topology.SystemMask);
        var rowCount = Math.Max(1, (int)Math.Ceiling(processors.Count / 8d));
        table.RowCount = rowCount;

        for (var column = 0; column < 8; column++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        }

        for (var row = 0; row < rowCount; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        }

        for (var index = 0; index < processors.Count; index++)
        {
            var processor = processors[index];
            var checkBox = new CheckBox
            {
                Text = $"CPU {processor}",
                Checked = IsBitSet(initialMask, processor),
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                FlatStyle = FlatStyle.Standard,
                UseVisualStyleBackColor = true
            };

            checkBox.CheckedChanged += (_, _) => UpdateSelectionState();
            _processorCheckBoxes[processor] = checkBox;
            table.Controls.Add(checkBox, index % 8, index / 8);
        }

        processorPanel.Controls.Add(table);

        var actionPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(16, 7, 16, 7)
        };

        var recommendedButton = new Button
        {
            Text = "推荐：大缓存 CCD",
            Size = new Size(140, 32),
            Location = new Point(16, 7),
            Enabled = _topology.RecommendedMask != 0
        };
        recommendedButton.Click += (_, _) => SetMask(_topology.RecommendedMask);

        var allButton = new Button
        {
            Text = "全部处理器",
            Size = new Size(110, 32),
            Location = new Point(164, 7)
        };
        allButton.Click += (_, _) => SetMask(_topology.SystemMask);

        var clearButton = new Button
        {
            Text = "清空",
            Size = new Size(76, 32),
            Location = new Point(282, 7)
        };
        clearButton.Click += (_, _) => SetMask(0);

        actionPanel.Controls.Add(recommendedButton);
        actionPanel.Controls.Add(allButton);
        actionPanel.Controls.Add(clearButton);

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 78,
            Padding = new Padding(16, 8, 16, 12)
        };

        _selectionLabel.AutoSize = true;
        _selectionLabel.Location = new Point(16, 10);
        _selectionLabel.ForeColor = SystemColors.GrayText;

        _okButton.Text = "确定";
        _okButton.Size = new Size(90, 32);
        _okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _okButton.Location = new Point(Width - 214, 35);
        _okButton.Click += (_, _) =>
        {
            SelectedMask = GetSelectedMask();
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelButton = new Button
        {
            Text = "取消",
            Size = new Size(90, 32),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(Width - 118, 35),
            DialogResult = DialogResult.Cancel
        };

        footerPanel.Controls.Add(_selectionLabel);
        footerPanel.Controls.Add(_okButton);
        footerPanel.Controls.Add(cancelButton);

        Controls.Add(processorPanel);
        Controls.Add(actionPanel);
        Controls.Add(footerPanel);
        Controls.Add(headerPanel);

        AcceptButton = _okButton;
        CancelButton = cancelButton;
    }

    private void SetMask(ulong mask)
    {
        foreach (var pair in _processorCheckBoxes)
        {
            pair.Value.Checked = IsBitSet(mask, pair.Key);
        }

        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        SelectedMask = GetSelectedMask();
        _selectionLabel.Text =
            $"当前：{AffinityMaskFormatter.Format(SelectedMask)}  " +
            $"（{BitOperations.PopCount(SelectedMask)} 个逻辑处理器）";
        _okButton.Enabled = SelectedMask != 0;
    }

    private ulong GetSelectedMask()
    {
        var mask = 0UL;
        foreach (var pair in _processorCheckBoxes)
        {
            if (pair.Value.Checked)
            {
                mask |= 1UL << pair.Key;
            }
        }

        return mask & _topology.SystemMask;
    }

    private static IReadOnlyList<int> GetAvailableProcessors(ulong systemMask)
    {
        var processors = new List<int>();
        for (var bit = 0; bit < 64; bit++)
        {
            if (IsBitSet(systemMask, bit))
            {
                processors.Add(bit);
            }
        }

        return processors;
    }

    private static bool IsBitSet(ulong mask, int bit)
    {
        return bit is >= 0 and < 64 && (mask & (1UL << bit)) != 0;
    }
}
