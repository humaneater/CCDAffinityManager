using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using CcdAffinityManager.Native;

namespace CcdAffinityManager.Services;

internal sealed class ProcessStartWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<int> _knownProcessIds = [];
    private ManagementEventWatcher? _eventWatcher;
    private System.Threading.Timer? _fallbackTimer;
    private bool _started;
    private bool _disposed;
    private int _polling;

    public event EventHandler<ProcessStartedEventArgs>? ProcessStarted;

    public string ModeDescription { get; private set; } = "未启动";

    public bool UsesEventNotifications { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
        }

        if (TryStartEventWatcher())
        {
            return;
        }

        StartFallbackTimer();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
        }

        if (_eventWatcher is not null)
        {
            _eventWatcher.EventArrived -= OnEventArrived;
            try
            {
                _eventWatcher.Stop();
            }
            catch (ManagementException)
            {
                // The WMI service may already be shutting down.
            }

            _eventWatcher.Dispose();
            _eventWatcher = null;
        }

        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
    }

    private bool TryStartEventWatcher()
    {
        ManagementEventWatcher? watcher = null;
        try
        {
            watcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            watcher.EventArrived += OnEventArrived;
            watcher.Start();

            _eventWatcher = watcher;
            UsesEventNotifications = true;
            ModeDescription = "事件驱动";
            return true;
        }
        catch (Exception exception) when (
            exception is ManagementException or UnauthorizedAccessException or COMException)
        {
            if (watcher is not null)
            {
                watcher.EventArrived -= OnEventArrived;
                watcher.Dispose();
            }

            UsesEventNotifications = false;
            return false;
        }
    }

    private void StartFallbackTimer()
    {
        lock (_gate)
        {
            _knownProcessIds.Clear();
            foreach (var processId in GetCurrentProcessIds())
            {
                _knownProcessIds.Add(processId);
            }
        }

        ModeDescription = "新 PID 差量检测（约 3 秒）";
        _fallbackTimer = new System.Threading.Timer(
            PollForNewProcesses,
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3));
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs eventArgs)
    {
        try
        {
            var processIdValue = eventArgs.NewEvent.Properties["ProcessID"].Value;
            var processNameValue = eventArgs.NewEvent.Properties["ProcessName"].Value;
            var processId = Convert.ToInt32(processIdValue, CultureInfo.InvariantCulture);
            var processName = Convert.ToString(processNameValue, CultureInfo.InvariantCulture) ?? string.Empty;
            OnProcessStarted(processId, processName);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or ManagementException)
        {
            // Ignore malformed WMI events. The next event is independent.
        }
    }

    private void PollForNewProcesses(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0)
        {
            return;
        }

        try
        {
            var currentProcessIds = GetCurrentProcessIds();
            var currentSet = new HashSet<int>(currentProcessIds);
            List<int> newProcessIds;

            lock (_gate)
            {
                newProcessIds = currentProcessIds
                    .Where(processId => !_knownProcessIds.Contains(processId))
                    .ToList();

                _knownProcessIds.RemoveWhere(processId => !currentSet.Contains(processId));
                foreach (var processId in currentProcessIds)
                {
                    _knownProcessIds.Add(processId);
                }
            }

            foreach (var processId in newProcessIds)
            {
                OnProcessStarted(processId, string.Empty);
            }
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void OnProcessStarted(int processId, string processName)
    {
        ProcessStarted?.Invoke(this, new ProcessStartedEventArgs(processId, processName));
    }

    private static IReadOnlyList<int> GetCurrentProcessIds()
    {
        var buffer = new uint[1024];

        while (true)
        {
            var arraySizeBytes = checked((uint)(buffer.Length * sizeof(uint)));
            if (!NativeMethods.EnumProcesses(buffer, arraySizeBytes, out var bytesReturned))
            {
                return [];
            }

            if (bytesReturned < arraySizeBytes)
            {
                var count = (int)(bytesReturned / sizeof(uint));
                return buffer
                    .Take(count)
                    .Where(processId => processId != 0 && processId <= int.MaxValue)
                    .Select(processId => (int)processId)
                    .Distinct()
                    .ToArray();
            }

            if (buffer.Length >= 65536)
            {
                return [];
            }

            buffer = new uint[buffer.Length * 2];
        }
    }
}

internal sealed record ProcessStartedEventArgs(int ProcessId, string ProcessName);
