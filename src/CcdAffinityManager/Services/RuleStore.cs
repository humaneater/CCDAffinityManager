using System.Text.Json;
using CcdAffinityManager.Models;

namespace CcdAffinityManager.Services;

internal sealed class RuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public RuleStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CcdAffinityManager");

        SettingsPath = Path.Combine(directory, "settings.json");
    }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                return new AppSettings();
            }

            settings.Rules ??= [];
            foreach (var rule in settings.Rules)
            {
                if (rule.Id == Guid.Empty)
                {
                    rule.Id = Guid.NewGuid();
                }

                if (string.IsNullOrWhiteSpace(rule.ProcessName))
                {
                    rule.ProcessName = Path.GetFileNameWithoutExtension(rule.ExecutablePath);
                }

                rule.DisplayName = string.IsNullOrWhiteSpace(rule.DisplayName)
                    ? rule.ProcessName
                    : rule.DisplayName;
            }

            return settings;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            TryBackupBrokenSettings();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, true);
    }

    private void TryBackupBrokenSettings()
    {
        try
        {
            var backupPath = SettingsPath + ".broken";
            File.Move(SettingsPath, backupPath, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A failed backup should not prevent the application from starting.
        }
    }
}
