namespace CcdAffinityManager.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public List<AffinityRule> Rules { get; set; } = [];
}
