namespace SoundboardApp.Models;

public sealed class AudioDeviceInfo : IEquatable<AudioDeviceInfo>
{
    /// <summary>WASAPI endpoint id. Empty string = Windows default device.</summary>
    public string DeviceId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public bool IsVirtualCable { get; init; }
    public bool IsStereoMix { get; init; }

    public override string ToString() => Name;

    public bool Equals(AudioDeviceInfo? other)
    {
        if (other is null)
            return false;

        return string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal)
               && string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as AudioDeviceInfo);

    public override int GetHashCode() => HashCode.Combine(DeviceId, Name);
}
