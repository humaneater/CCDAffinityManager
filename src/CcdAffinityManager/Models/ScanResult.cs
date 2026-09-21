namespace CcdAffinityManager.Models;

public sealed record RuleScanResult(
    Guid RuleId,
    int MatchedProcesses,
    int AppliedProcesses,
    IReadOnlyList<string> Errors);

public sealed record ScanSummary(
    int EnabledRules,
    int MatchedProcesses,
    int AppliedProcesses,
    int FailedProcesses,
    IReadOnlyList<RuleScanResult> RuleResults);

public sealed record RestoreResult(
    int RestoredProcesses,
    int FailedProcesses,
    IReadOnlyList<string> Errors);
