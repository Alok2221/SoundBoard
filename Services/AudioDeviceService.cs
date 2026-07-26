using NAudio.CoreAudioApi;
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

    private static readonly string[] StereoMixHints =
    [
        "stereo mix",
        "miks stereo",
        "what u hear",
        "wave out mix"
    ];

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        Enumerate(DataFlow.Render, includeDefault: true);

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() =>
        Enumerate(DataFlow.Capture, includeDefault: true);

    public AudioDeviceInfo? FindPreferredDiscordDevice(IEnumerable<AudioDeviceInfo> devices) =>
        devices.FirstOrDefault(d => d.IsVirtualCable && !string.IsNullOrEmpty(d.DeviceId))
        ?? devices.FirstOrDefault(d => d.IsVirtualCable);

    public AudioDeviceInfo? FindPreferredMicrophone(IEnumerable<AudioDeviceInfo> devices) =>
        devices.FirstOrDefault(d =>
            !string.IsNullOrEmpty(d.DeviceId)
            && !d.IsVirtualCable
            && !d.IsStereoMix)
        ?? devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceId));

    private static IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow, bool includeDefault)
    {
        var devices = new List<AudioDeviceInfo>();

        if (includeDefault)
        {
            devices.Add(new AudioDeviceInfo
            {
                DeviceId = string.Empty,
                Name = flow == DataFlow.Capture
                    ? "Windows default microphone"
                    : "Windows default device",
                IsVirtualCable = false,
                IsStereoMix = false
            });
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                try
                {
                    var name = string.IsNullOrWhiteSpace(endpoint.FriendlyName)
                        ? endpoint.ID
                        : endpoint.FriendlyName;

                    devices.Add(new AudioDeviceInfo
                    {
                        DeviceId = endpoint.ID,
                        Name = name,
                        IsVirtualCable = IsVirtualCable(name),
                        IsStereoMix = IsStereoMix(name)
                    });
                }
                catch
                {
                    // skip broken / unavailable endpoint
                }
                finally
                {
                    endpoint.Dispose();
                }
            }
        }
        catch
        {
            // WASAPI unavailable - keep default entry only
        }

        return devices;
    }

    private static bool IsVirtualCable(string name)
    {
        var lower = name.ToLowerInvariant();
        return VirtualCableHints.Any(hint => lower.Contains(hint));
    }

    private static bool IsStereoMix(string name)
    {
        var lower = name.ToLowerInvariant();
        return StereoMixHints.Any(hint => lower.Contains(hint));
    }
}
