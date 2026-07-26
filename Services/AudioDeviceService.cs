using NAudio.Wave;
using SoundboardApp.Models;

namespace SoundboardApp.Services;

public sealed class AudioDeviceService
{
    private static readonly string[] VirtualCableHints =
    [
        "cable",
        "vb-audio",
        "voicemeeter",
        "virtual"
    ];

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new()
            {
                DeviceNumber = -1,
                Name = "Windows default device",
                IsVirtualCable = false
            }
        };

        try
        {
            var count = WaveOut.DeviceCount;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var caps = WaveOut.GetCapabilities(i);
                    var name = string.IsNullOrWhiteSpace(caps.ProductName)
                        ? $"Device {i}"
                        : caps.ProductName;

                    devices.Add(new AudioDeviceInfo
                    {
                        DeviceNumber = i,
                        Name = name,
                        IsVirtualCable = IsVirtualCable(name)
                    });
                }
                catch
                {
                    // skip broken / unavailable device
                }
            }
        }
        catch
        {
            // WaveOut unavailable - keep default device only
        }

        return devices;
    }

    public AudioDeviceInfo? FindPreferredDiscordDevice(IEnumerable<AudioDeviceInfo> devices) =>
        devices.FirstOrDefault(d => d.IsVirtualCable);

    private static bool IsVirtualCable(string name)
    {
        var lower = name.ToLowerInvariant();
        return VirtualCableHints.Any(hint => lower.Contains(hint));
    }
}
