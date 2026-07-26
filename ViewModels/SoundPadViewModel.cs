using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundboardApp.Models;

namespace SoundboardApp.ViewModels;

public partial class SoundPadViewModel : ObservableObject
{
    private readonly Action<SoundPadViewModel> _play;
    private readonly Action<SoundPadViewModel> _remove;
    private readonly Action<SoundPadViewModel> _beginHotkeyCapture;
    private readonly Action<SoundPadViewModel> _clearHotkey;
    private readonly Action<SoundPadViewModel> _volumeChanged;
    private readonly Action _persist;
    private bool _isInitializing = true;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string? _hotkey;
    [ObservableProperty] private double _volume = 1.0;
    [ObservableProperty] private int _colorIndex;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isCapturingHotkey;

    public Guid Id { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? Path.GetFileNameWithoutExtension(FilePath)
        : Name;

    public string HotkeyDisplay => IsCapturingHotkey
        ? "Press shortcut..."
        : string.IsNullOrWhiteSpace(Hotkey) ? "No hotkey" : Hotkey;

    public string VolumePercent => $"{(int)Math.Round(Volume * 100)}%";

    public SoundPadViewModel(
        SoundPad pad,
        Action<SoundPadViewModel> play,
        Action<SoundPadViewModel> remove,
        Action<SoundPadViewModel> beginHotkeyCapture,
        Action<SoundPadViewModel> clearHotkey,
        Action<SoundPadViewModel> volumeChanged,
        Action persist)
    {
        _play = play;
        _remove = remove;
        _beginHotkeyCapture = beginHotkeyCapture;
        _clearHotkey = clearHotkey;
        _volumeChanged = volumeChanged;
        _persist = persist;

        Id = pad.Id;
        Name = pad.Name;
        FilePath = pad.FilePath;
        Hotkey = pad.Hotkey;
        Volume = pad.Volume;
        ColorIndex = pad.ColorIndex;
        _isInitializing = false;
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        if (!_isInitializing)
            _persist();
    }

    partial void OnHotkeyChanged(string? value)
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
        if (!_isInitializing)
            _persist();
    }

    partial void OnIsCapturingHotkeyChanged(bool value) => OnPropertyChanged(nameof(HotkeyDisplay));

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumePercent));
        if (!_isInitializing)
            _volumeChanged(this);
    }

    [RelayCommand]
    private void Play() => _play(this);

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private void CaptureHotkey() => _beginHotkeyCapture(this);

    [RelayCommand]
    private void ClearHotkey() => _clearHotkey(this);

    public SoundPad ToModel() => new()
    {
        Id = Id,
        Name = Name,
        FilePath = FilePath,
        Hotkey = Hotkey,
        Volume = Volume,
        ColorIndex = ColorIndex
    };
}
