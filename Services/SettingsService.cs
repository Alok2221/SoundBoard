using System.IO;
using System.Text.Json;
using SoundboardApp.Models;

namespace SoundboardApp.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly string _settingsDir;

    public SettingsService()
    {
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SoundboardApp");
        Directory.CreateDirectory(_settingsDir);
        _settingsPath = Path.Combine(_settingsDir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Sanitize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Sanitize(settings);
            Directory.CreateDirectory(_settingsDir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Atomic write - protects against corruption on crash / Defender scan
            File.Copy(tempPath, _settingsPath, overwrite: true);
            File.Delete(tempPath);
        }
        catch
        {
            // Don't crash the UI on disk / AppData permission issues
        }
    }

    private static void Sanitize(AppSettings settings)
    {
        settings.MasterVolume = Math.Clamp(settings.MasterVolume, 0.0, 1.0);
        settings.Pads ??= [];
        foreach (var pad in settings.Pads)
        {
            pad.Volume = Math.Clamp(pad.Volume, 0.0, 1.0);
            pad.Name ??= string.Empty;
            pad.FilePath ??= string.Empty;
        }
    }
}
