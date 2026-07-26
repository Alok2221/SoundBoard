namespace SoundboardApp.Models;

public sealed class AudioDeviceInfo
{
    public int DeviceNumber { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsVirtualCable { get; init; }

    public override string ToString() => Name;
}
