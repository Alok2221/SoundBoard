namespace SoundboardApp.Models;

public sealed class AppSettings
{
    public string? DiscordOutputDeviceId { get; set; }
    public string? DiscordOutputDeviceName { get; set; }
    public string? MonitorOutputDeviceId { get; set; }
    public string? MonitorOutputDeviceName { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public string? MicrophoneDeviceName { get; set; }

    /// <summary>Legacy WaveOut index (ignored when DeviceId is present).</summary>
    public int DiscordOutputDeviceNumber { get; set; } = -1;

    /// <summary>Legacy WaveOut index (ignored when DeviceId is present).</summary>
    public int MonitorOutputDeviceNumber { get; set; } = -1;

    public bool EnableMonitor { get; set; } = true;
    public bool EnableMicrophone { get; set; } = true;
    public double MasterVolume { get; set; } = 0.85;
    public double MicVolume { get; set; } = 1.0;
    public bool OverlapSounds { get; set; } = true;
    public List<SoundPad> Pads { get; set; } = [];
}
