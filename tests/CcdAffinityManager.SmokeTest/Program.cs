using System.Diagnostics;
using CcdAffinityManager.Models;
using CcdAffinityManager.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var topology = CpuTopologyService.Detect();
Console.WriteLine($"Detected mask: 0x{topology.RecommendedMask:X}");
Console.WriteLine($"System mask:   0x{topology.SystemMask:X}");
Console.WriteLine($"Large L3 cache: {topology.RecommendedCacheSizeBytes / (1024d * 1024d):0.#} MB");

if (topology.RecommendedMask == 0 ||
    (topology.RecommendedMask & ~topology.SystemMask) != 0)
{
    Console.Error.WriteLine("Topology validation failed.");
    return 1;
}

var pingPath = Path.Combine(Environment.SystemDirectory, "ping.exe");
await ValidateProcessStartWatcherAsync(pingPath);

using var target = Process.Start(new ProcessStartInfo
{
    FileName = pingPath,
    Arguments = "-n 30 127.0.0.1",
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true
});

if (target is null)
{
    Console.Error.WriteLine("Could not start the test process.");
    return 2;
}

var originalMask = unchecked((ulong)target.ProcessorAffinity.ToInt64());
var desiredMask = topology.RecommendedMask;
var service = new AffinityService(topology.SystemMask);
var rule = new AffinityRule
{
    DisplayName = "Affinity smoke test",
    ProcessName = "ping",
    ExecutablePath = pingPath,
    AffinityMask = desiredMask,
    Enabled = true
};

try
{
    var applyResult = service.Synchronize([rule]);
    target.Refresh();
    var appliedMask = unchecked((ulong)target.ProcessorAffinity.ToInt64());

    Console.WriteLine($"Original mask:  0x{originalMask:X}");
    Console.WriteLine($"Applied mask:   0x{appliedMask:X}");
    Console.WriteLine($"Matched:        {applyResult.MatchedProcesses}");
    Console.WriteLine($"Applied:        {applyResult.AppliedProcesses}");

    if (applyResult.MatchedProcesses != 1 ||
        applyResult.AppliedProcesses != 1 ||
        appliedMask != desiredMask)
    {
        Console.Error.WriteLine("Apply validation failed.");
        return 3;
    }

    var restoreResult = service.RestoreAll();
    target.Refresh();
    var restoredMask = unchecked((ulong)target.ProcessorAffinity.ToInt64());

    Console.WriteLine($"Restored mask:  0x{restoredMask:X}");
    Console.WriteLine($"Restore errors: {restoreResult.FailedProcesses}");

    if (restoreResult.FailedProcesses != 0 ||
        restoredMask != originalMask)
    {
        Console.Error.WriteLine("Restore validation failed.");
        return 4;
    }

    var nameOnlyRule = new AffinityRule
    {
        DisplayName = "Affinity smoke test by name",
        ProcessName = "ping",
        ExecutablePath = string.Empty,
        MatchByProcessName = true,
        AffinityMask = desiredMask,
        Enabled = true
    };

    var unrelatedEventResult = service.SynchronizeProcess(
        int.MaxValue,
        "unrelated-helper",
        [nameOnlyRule]);
    var exitedProcessResult = service.SynchronizeProcess(
        int.MaxValue,
        "ping",
        [nameOnlyRule]);

    if (unrelatedEventResult is not null || exitedProcessResult is not null)
    {
        Console.Error.WriteLine("Stale process event validation failed.");
        return 5;
    }

    var nameApplyResult = service.Synchronize([nameOnlyRule]);
    target.Refresh();
    var nameAppliedMask = unchecked((ulong)target.ProcessorAffinity.ToInt64());

    Console.WriteLine($"Name match mask: 0x{nameAppliedMask:X}");
    if (nameApplyResult.MatchedProcesses != 1 ||
        nameApplyResult.AppliedProcesses != 1 ||
        nameAppliedMask != desiredMask)
    {
        Console.Error.WriteLine("Name-only rule validation failed.");
        return 6;
    }

    var nameRestoreResult = service.RestoreAll();
    target.Refresh();
    var nameRestoredMask = unchecked((ulong)target.ProcessorAffinity.ToInt64());
    if (nameRestoreResult.FailedProcesses != 0 ||
        nameRestoredMask != originalMask)
    {
        Console.Error.WriteLine("Name-only rule restore validation failed.");
        return 7;
    }

    Console.WriteLine("Smoke test passed.");
    return 0;
}
finally
{
    service.RestoreAll();
    if (!target.HasExited)
    {
        target.Kill();
    }

    target.WaitForExit();
}

static async Task ValidateProcessStartWatcherAsync(string executablePath)
{
    using var watcher = new ProcessStartWatcher();
    watcher.Start();
    Console.WriteLine($"Process watcher: {watcher.ModeDescription}");

    var detected = new TaskCompletionSource<int>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    watcher.ProcessStarted += (_, eventArgs) =>
    {
        detected.TrySetResult(eventArgs.ProcessId);
    };

    using var observedProcess = Process.Start(new ProcessStartInfo
    {
        FileName = executablePath,
        Arguments = "-n 10 127.0.0.1",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true
    });

    if (observedProcess is null)
    {
        throw new InvalidOperationException("Could not start process watcher test target.");
    }

    int? detectedProcessId;
    try
    {
        detectedProcessId = await detected.Task.WaitAsync(TimeSpan.FromSeconds(8));
    }
    catch (TimeoutException)
    {
        detectedProcessId = null;
    }

    if (detectedProcessId != observedProcess.Id)
    {
        throw new InvalidOperationException(
            $"Process watcher did not detect PID {observedProcess.Id}.");
    }

    if (!observedProcess.HasExited)
    {
        observedProcess.Kill();
    }

    observedProcess.WaitForExit();
    Console.WriteLine($"Process watcher detected PID {observedProcess.Id}.");
}
