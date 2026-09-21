using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CcdAffinityManager.Services;

namespace CcdAffinityManager.Models;

public sealed class AffinityRule : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _displayName = string.Empty;
    private string _processName = string.Empty;
    private string _executablePath = string.Empty;
    private ulong _affinityMask;
    private bool _matchByProcessName;
    private string _runtimeStatus = "等待监控";

    public Guid Id { get; set; } = Guid.NewGuid();

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value))
            {
                OnPropertyChanged(nameof(ExecutableName));
            }
        }
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (SetField(ref _executablePath, value))
            {
                OnPropertyChanged(nameof(ExecutableName));
            }
        }
    }

    public string ProcessName
    {
        get => _processName;
        set
        {
            if (SetField(ref _processName, value))
            {
                OnPropertyChanged(nameof(ExecutableName));
                OnPropertyChanged(nameof(MatchTargetText));
            }
        }
    }

    public bool MatchByProcessName
    {
        get => _matchByProcessName;
        set
        {
            if (SetField(ref _matchByProcessName, value))
            {
                OnPropertyChanged(nameof(MatchTargetText));
            }
        }
    }

    public ulong AffinityMask
    {
        get => _affinityMask;
        set
        {
            if (SetField(ref _affinityMask, value))
            {
                OnPropertyChanged(nameof(AffinityText));
            }
        }
    }

    [JsonIgnore]
    public string ExecutableName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ProcessName))
            {
                return ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? ProcessName
                    : ProcessName + ".exe";
            }

            return Path.GetFileName(ExecutablePath);
        }
    }

    [JsonIgnore]
    public string MatchTargetText => MatchByProcessName
        ? $"{ExecutableName}（按进程名匹配，无法读取路径）"
        : ExecutablePath;

    [JsonIgnore]
    public string AffinityText => AffinityMaskFormatter.Format(AffinityMask);

    [JsonIgnore]
    public string RuntimeStatus
    {
        get => _runtimeStatus;
        private set => SetField(ref _runtimeStatus, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AffinityRule Clone()
    {
        return new AffinityRule
        {
            Id = Id,
            Enabled = Enabled,
            DisplayName = DisplayName,
            ProcessName = ProcessName,
            ExecutablePath = ExecutablePath,
            AffinityMask = AffinityMask,
            MatchByProcessName = MatchByProcessName,
            RuntimeStatus = RuntimeStatus
        };
    }

    public void SetRuntimeStatus(string value)
    {
        RuntimeStatus = value;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
