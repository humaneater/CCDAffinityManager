namespace CcdAffinityManager.Models;

public sealed record ProcessSelection(
    string DisplayName,
    string ProcessName,
    string? ExecutablePath,
    bool MatchByProcessName);
