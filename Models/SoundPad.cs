namespace SoundboardApp.Models;

public sealed class SoundPad
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? Hotkey { get; set; }
    public double Volume { get; set; } = 1.0;
    public int ColorIndex { get; set; }
}
