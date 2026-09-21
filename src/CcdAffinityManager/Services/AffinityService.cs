using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CcdAffinityManager.Models;
using CcdAffinityManager.Native;

namespace CcdAffinityManager.Services;

internal sealed class AffinityService
{
    private readonly ulong _systemMask;
    private readonly object _operationGate = new();
    private readonly object _stateGate = new();
    private readonly Dictionary<int, AppliedState> _appliedStates = [];

    public AffinityService(ulong systemMask)
    {
        _systemMask = systemMask;
    }

    public ScanSummary Synchronize(IReadOnlyCollection<AffinityRule> rules)
    {
        lock (_operationGate)
        {
            CleanupExitedProcesses();

            var results = new List<RuleScanResult>();
            var matchedTotal = 0;
            var appliedTotal = 0;
            var failedTotal = 0;

            foreach (var rule in rules.Where(rule => rule.Enabled))
            {
                var result = SynchronizeRule(rule);
                results.Add(result);
                matchedTotal += result.MatchedProcesses;
                appliedTotal += result.AppliedProcesses;
                failedTotal += result.Errors.Count;
            }

            return new ScanSummary(
                results.Count,
                matchedTotal,
                appliedTotal,
                failedTotal,
                results);
        }
    }

    public RuleScanResult? SynchronizeProcess(
        int processId,
        string? observedProcessName,
        IReadOnlyCollection<AffinityRule> rules)
    {
        lock (_operationGate)
        {
            CleanupExitedProcesses();

            var normalizedObservedName = NormalizeProcessName(observedProcessName);
            var candidateRules = rules
                .Where(item => item.Enabled)
                .Where(item =>
                {
                    var ruleName = GetRuleProcessName(item);
                    return ruleName is not null &&
                           (normalizedObservedName is null ||
                            string.Equals(
                                ruleName,
                                normalizedObservedName,
                                StringComparison.OrdinalIgnoreCase));
                })
                .ToArray();

            if (candidateRules.Length == 0)
            {
                return null;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return null;
                }

                var actualPath = ProcessInspector.TryGetExecutablePath(process);
                var rule = candidateRules.FirstOrDefault(item =>
                    RuleMatchesProcess(
                        item,
                        process,
                        actualPath,
                        GetRuleProcessName(item)!));

                if (rule is null)
                {
                    return null;
                }

                var error = ApplyToProcess(process, rule);
                return new RuleScanResult(
                    rule.Id,
                    1,
                    error is null ? 1 : 0,
                    error is null ? [] : [error]);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                // The process can exit before its start notification is processed.
                return null;
            }
            catch (Win32Exception exception)
            {
                return new RuleScanResult(candidateRules[0].Id, 1, 0, [exception.Message]);
            }
        }
    }

    private static string? GetRuleProcessName(AffinityRule rule)
    {
        var processName = rule.ProcessName;
        try
        {
            if (string.IsNullOrWhiteSpace(processName) &&
                !string.IsNullOrWhiteSpace(rule.ExecutablePath))
            {
                processName = ProcessInspector.GetProcessName(rule.ExecutablePath);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return NormalizeProcessName(processName);
    }

    private static string? NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }

    private static bool RuleMatchesProcess(
        AffinityRule rule,
        Process process,
        string? actualPath,
        string expectedProcessName)
    {
        if (rule.MatchByProcessName)
        {
            return string.Equals(
                process.ProcessName,
                expectedProcessName,
                StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(actualPath))
        {
            return false;
        }

        return ProcessInspector.PathsEqual(actualPath, rule.ExecutablePath);
    }

    public RestoreResult RestoreRule(Guid ruleId)
    {
        return RestoreStates(state => state.RuleId == ruleId);
    }

    public RestoreResult RestoreAll()
    {
        return RestoreStates(_ => true);
    }

    private RuleScanResult SynchronizeRule(AffinityRule rule)
    {
        var errors = new List<string>();
        var processName = GetRuleProcessName(rule);
        if (processName is null)
        {
            return new RuleScanResult(rule.Id, 0, 0, ["进程名称或可执行文件路径无效"]);
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            return new RuleScanResult(rule.Id, 0, 0, [exception.Message]);
        }

        var matched = 0;
        var applied = 0;

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                var actualPath = rule.MatchByProcessName
                    ? null
                    : ProcessInspector.TryGetExecutablePath(process);

                if (!RuleMatchesProcess(rule, process, actualPath, processName))
                {
                    continue;
                }

                matched++;
                var error = ApplyToProcess(process, rule);
                if (error is null)
                {
                    applied++;
                }
                else if (!errors.Contains(error, StringComparer.Ordinal))
                {
                    errors.Add(error);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                // A process from the snapshot can exit before it is inspected.
                continue;
            }
            catch (Exception exception) when (
                exception is Win32Exception or NotSupportedException)
            {
                var error = exception.Message;
                if (!errors.Contains(error, StringComparer.Ordinal))
                {
                    errors.Add(error);
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return new RuleScanResult(rule.Id, matched, applied, errors);
    }

    private string? ApplyToProcess(Process process, AffinityRule rule)
    {
        var desiredMask = rule.AffinityMask & _systemMask;
        if (desiredMask == 0)
        {
            return "亲和度掩码不包含当前系统可用的逻辑处理器";
        }

        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessRights.QueryLimitedInformation |
            NativeMethods.ProcessAccessRights.SetInformation,
            false,
            process.Id);

        if (handle == IntPtr.Zero)
        {
            return FormatLastError("无法打开目标进程");
        }

        try
        {
            if (!NativeMethods.GetProcessAffinityMask(
                    handle,
                    out var currentMaskValue,
                    out _))
            {
                return FormatLastError("无法读取目标进程亲和度");
            }

            var currentMask = currentMaskValue.ToUInt64();
            if (currentMask == desiredMask)
            {
                return null;
            }

            var previousState = GetState(process.Id);
            if (previousState is not null &&
                previousState.RuleId != rule.Id)
            {
                return "该进程已被另一条规则管理";
            }

            var startTicks = ProcessInspector.TryGetStartTimeUtc(process, out var ticks)
                ? ticks
                : (long?)null;
            var originalMask = previousState?.OriginalMask ?? currentMask;

            if (!NativeMethods.SetProcessAffinityMask(handle, new UIntPtr(desiredMask)))
            {
                return FormatLastError("设置亲和度失败");
            }

            SetState(
                process.Id,
                new AppliedState(rule.Id, originalMask, startTicks));

            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private RestoreResult RestoreStates(Func<AppliedState, bool> predicate)
    {
        lock (_operationGate)
        {
            AppliedState[] states;
            lock (_stateGate)
            {
                states = _appliedStates
                    .Where(pair => predicate(pair.Value))
                    .Select(pair => pair.Value with { ProcessId = pair.Key })
                    .ToArray();
            }

            var restored = 0;
            var errors = new List<string>();

            foreach (var state in states)
            {
                var result = RestoreState(state);
                if (result.Error is null)
                {
                    if (result.Changed)
                    {
                        restored++;
                    }

                    RemoveState(state.ProcessId);
                }
                else if (!errors.Contains(result.Error, StringComparer.Ordinal))
                {
                    errors.Add(result.Error);
                }
            }

            return new RestoreResult(restored, errors.Count, errors);
        }
    }

    private RestoreStateResult RestoreState(AppliedState state)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(state.ProcessId);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return new RestoreStateResult(false, null);
        }

        using (process)
        {
            if (state.StartTicks is { } expectedTicks &&
                ProcessInspector.TryGetStartTimeUtc(process, out var actualTicks) &&
                expectedTicks != actualTicks)
            {
                return new RestoreStateResult(false, null);
            }

            var handle = NativeMethods.OpenProcess(
                NativeMethods.ProcessAccessRights.QueryLimitedInformation |
                NativeMethods.ProcessAccessRights.SetInformation,
                false,
                state.ProcessId);

            if (handle == IntPtr.Zero)
            {
                return new RestoreStateResult(
                    false,
                    FormatLastError($"恢复 PID {state.ProcessId} 的亲和度失败"));
            }

            try
            {
                if (!NativeMethods.GetProcessAffinityMask(
                        handle,
                        out var currentMaskValue,
                        out _))
                {
                    return new RestoreStateResult(
                        false,
                        FormatLastError($"读取 PID {state.ProcessId} 的亲和度失败"));
                }

                var currentMask = currentMaskValue.ToUInt64();
                if (currentMask == state.OriginalMask)
                {
                    return new RestoreStateResult(false, null);
                }

                if (!NativeMethods.SetProcessAffinityMask(
                        handle,
                        new UIntPtr(state.OriginalMask)))
                {
                    return new RestoreStateResult(
                        false,
                        FormatLastError($"恢复 PID {state.ProcessId} 的亲和度失败"));
                }

                return new RestoreStateResult(true, null);
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    private void CleanupExitedProcesses()
    {
        int[] processIds;
        lock (_stateGate)
        {
            processIds = _appliedStates.Keys.ToArray();
        }

        foreach (var processId in processIds)
        {
            if (!IsTrackedProcessAlive(processId))
            {
                RemoveState(processId);
            }
        }
    }

    private bool IsTrackedProcessAlive(int processId)
    {
        AppliedState? state;
        lock (_stateGate)
        {
            if (!_appliedStates.TryGetValue(processId, out state))
            {
                return false;
            }
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (state.StartTicks is { } expectedTicks &&
                ProcessInspector.TryGetStartTimeUtc(process, out var actualTicks) &&
                expectedTicks != actualTicks)
            {
                return false;
            }

            return !process.HasExited;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private AppliedState? GetState(int processId)
    {
        lock (_stateGate)
        {
            return _appliedStates.GetValueOrDefault(processId);
        }
    }

    private void SetState(int processId, AppliedState state)
    {
        lock (_stateGate)
        {
            _appliedStates[processId] = state;
        }
    }

    private void RemoveState(int processId)
    {
        lock (_stateGate)
        {
            _appliedStates.Remove(processId);
        }
    }

    private static string FormatLastError(string action)
    {
        var errorCode = Marshal.GetLastWin32Error();
        var message = new Win32Exception(errorCode).Message;
        var hint = errorCode == 5 ? "，请尝试以管理员身份运行" : string.Empty;
        return $"{action}：{message}{hint}（Win32 {errorCode}）";
    }

    private sealed record AppliedState(
        Guid RuleId,
        ulong OriginalMask,
        long? StartTicks)
    {
        public int ProcessId { get; init; }
    }

    private sealed record RestoreStateResult(bool Changed, string? Error);
}
