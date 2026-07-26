namespace SoundboardApp.Models;

public sealed class AppSettings
{
    public int DiscordOutputDeviceNumber { get; set; } = -1;
    public string? DiscordOutputDeviceName { get; set; }
    public int MonitorOutputDeviceNumber { get; set; } = -1;
    public string? MonitorOutputDeviceName { get; set; }
    public bool EnableMonitor { get; set; } = true;
    public double MasterVolume { get; set; } = 0.85;
    public bool OverlapSounds { get; set; } = true;
    public List<SoundPad> Pads { get; set; } = [];
}
