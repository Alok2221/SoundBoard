using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SoundboardApp.Models;
using SoundboardApp.Services;

namespace SoundboardApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] AudioExtensions =
    [
        ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif"
    ];

    private readonly AudioPlaybackService _playback;
    private readonly MicPassthroughService _micPassthrough;
    private readonly AudioDeviceService _devices;
    private readonly SettingsService _settingsService;
    private readonly HotkeyService _hotkeys;
    private SoundPadViewModel? _capturingPad;
    private bool _suppressSave;
    private DispatcherTimer? _saveDebounceTimer;

    public ObservableCollection<SoundPadViewModel> Pads { get; } = [];
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = [];

    [ObservableProperty] private AudioDeviceInfo? _selectedDiscordDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedMonitorDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedMicrophoneDevice;
    [ObservableProperty] private bool _enableMonitor = true;
    [ObservableProperty] private bool _enableMicrophone = true;
    [ObservableProperty] private double _masterVolume = 0.85;
    [ObservableProperty] private double _micVolume = 1.0;
    [ObservableProperty] private bool _overlapSounds = true;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _hasVirtualCable;

    public MainViewModel(
        AudioPlaybackService playback,
        MicPassthroughService micPassthrough,
        AudioDeviceService devices,
        SettingsService settingsService,
        HotkeyService hotkeys)
    {
        _playback = playback;
        _micPassthrough = micPassthrough;
        _devices = devices;
        _settingsService = settingsService;
        _hotkeys = hotkeys;
        _playback.PlaybackStateChanged += (_, _) =>
        {
            var app = Application.Current;
            if (app?.Dispatcher is null || app.Dispatcher.HasShutdownStarted)
                return;

            app.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    IsPlaying = _playback.IsPlaying;
                    if (!_playback.IsPlaying)
                    {
                        foreach (var pad in Pads)
                            pad.IsPlaying = false;
                    }
                }
                catch
                {
                    // UI may already be shutting down
                }
            });
        };
    }

    public void Initialize(Window window)
    {
        _hotkeys.Attach(window);
        RefreshDevices();
        LoadSettings();
        ApplyMicPassthrough();
        UpdateDiscordHint();
    }

    public void RefreshDevices()
    {
        var previousDiscord = SelectedDiscordDevice?.DeviceId;
        var previousDiscordName = SelectedDiscordDevice?.Name;
        var previousMonitor = SelectedMonitorDevice?.DeviceId;
        var previousMonitorName = SelectedMonitorDevice?.Name;
        var previousMic = SelectedMicrophoneDevice?.DeviceId;
        var previousMicName = SelectedMicrophoneDevice?.Name;

        _suppressSave = true;
        try
        {
            OutputDevices.Clear();
            foreach (var device in _devices.GetOutputDevices())
                OutputDevices.Add(device);

            InputDevices.Clear();
            foreach (var device in _devices.GetInputDevices())
                InputDevices.Add(device);

            HasVirtualCable = OutputDevices.Any(d => d.IsVirtualCable);

            SelectedDiscordDevice = FindDevice(OutputDevices, previousDiscord, previousDiscordName)
                ?? _devices.FindPreferredDiscordDevice(OutputDevices)
                ?? OutputDevices.FirstOrDefault();

            SelectedMonitorDevice = FindDevice(OutputDevices, previousMonitor, previousMonitorName)
                ?? OutputDevices.FirstOrDefault(d =>
                    !d.IsVirtualCable && !string.IsNullOrEmpty(d.DeviceId))
                ?? OutputDevices.FirstOrDefault();

            SelectedMicrophoneDevice = FindDevice(InputDevices, previousMic, previousMicName)
                ?? _devices.FindPreferredMicrophone(InputDevices)
                ?? InputDevices.FirstOrDefault();
        }
        finally
        {
            _suppressSave = false;
        }

        ApplyPlaybackSettings();
        ApplyMicPassthrough();
    }

    private static AudioDeviceInfo? FindDevice(
        IEnumerable<AudioDeviceInfo> devices,
        string? deviceId,
        string? name)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var byId = devices.FirstOrDefault(d =>
                string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(name))
            return devices.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        return null;
    }

    private void LoadSettings()
    {
        _suppressSave = true;
        try
        {
            var settings = _settingsService.Load();
            MasterVolume = settings.MasterVolume;
            MicVolume = settings.MicVolume;
            EnableMonitor = settings.EnableMonitor;
            EnableMicrophone = settings.EnableMicrophone;
            OverlapSounds = settings.OverlapSounds;

            SelectedDiscordDevice = FindDevice(
                    OutputDevices,
                    settings.DiscordOutputDeviceId,
                    settings.DiscordOutputDeviceName)
                ?? SelectedDiscordDevice;

            SelectedMonitorDevice = FindDevice(
                    OutputDevices,
                    settings.MonitorOutputDeviceId,
                    settings.MonitorOutputDeviceName)
                ?? SelectedMonitorDevice;

            SelectedMicrophoneDevice = FindDevice(
                    InputDevices,
                    settings.MicrophoneDeviceId,
                    settings.MicrophoneDeviceName)
                ?? SelectedMicrophoneDevice;

            Pads.Clear();
            foreach (var pad in settings.Pads.Where(p => File.Exists(p.FilePath)))
                Pads.Add(CreatePadVm(pad));

            ApplyPlaybackSettings();
            ReregisterHotkeys();
            OnPropertyChanged(nameof(HasPads));
            StatusMessage = Pads.Count == 0
                ? "Add sounds to get started"
                : $"Loaded {Pads.Count} sounds";
        }
        finally
        {
            _suppressSave = false;
        }
    }

    public void SaveSettings()
    {
        if (_suppressSave)
            return;

        var settings = new AppSettings
        {
            DiscordOutputDeviceId = SelectedDiscordDevice?.DeviceId,
            DiscordOutputDeviceName = SelectedDiscordDevice?.Name,
            MonitorOutputDeviceId = SelectedMonitorDevice?.DeviceId,
            MonitorOutputDeviceName = SelectedMonitorDevice?.Name,
            MicrophoneDeviceId = SelectedMicrophoneDevice?.DeviceId,
            MicrophoneDeviceName = SelectedMicrophoneDevice?.Name,
            EnableMonitor = EnableMonitor,
            EnableMicrophone = EnableMicrophone,
            MasterVolume = MasterVolume,
            MicVolume = MicVolume,
            OverlapSounds = OverlapSounds,
            Pads = Pads.Select(p => p.ToModel()).ToList()
        };

        _settingsService.Save(settings);
    }

    public void ScheduleSave()
    {
        if (_suppressSave)
            return;

        _saveDebounceTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Tick -= OnSaveDebounceTick;
        _saveDebounceTimer.Tick += OnSaveDebounceTick;
        _saveDebounceTimer.Start();
    }

    private void OnSaveDebounceTick(object? sender, EventArgs e)
    {
        if (_saveDebounceTimer is not null)
        {
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Tick -= OnSaveDebounceTick;
        }

        SaveSettings();
    }

    partial void OnSelectedDiscordDeviceChanged(AudioDeviceInfo? value)
    {
        ApplyPlaybackSettings();
        ApplyMicPassthrough();
        UpdateDiscordHint();
        SaveSettings();
    }

    partial void OnSelectedMonitorDeviceChanged(AudioDeviceInfo? value)
    {
        ApplyPlaybackSettings();
        SaveSettings();
    }

    partial void OnSelectedMicrophoneDeviceChanged(AudioDeviceInfo? value)
    {
        ApplyMicPassthrough();
        UpdateDiscordHint();
        SaveSettings();
    }

    partial void OnEnableMonitorChanged(bool value)
    {
        ApplyPlaybackSettings();
        SaveSettings();
    }

    partial void OnEnableMicrophoneChanged(bool value)
    {
        ApplyMicPassthrough();
        UpdateDiscordHint();
        SaveSettings();
    }

    partial void OnMasterVolumeChanged(double value)
    {
        _playback.MasterVolume = (float)value;
        OnPropertyChanged(nameof(MasterVolumePercent));
        ScheduleSave();
    }

    partial void OnMicVolumeChanged(double value)
    {
        _micPassthrough.MicVolume = (float)value;
        OnPropertyChanged(nameof(MicVolumePercent));
        ScheduleSave();
    }

    public string MasterVolumePercent => $"{(int)Math.Round(MasterVolume * 100)}%";
    public string MicVolumePercent => $"Mic {(int)Math.Round(MicVolume * 100)}%";

    partial void OnOverlapSoundsChanged(bool value)
    {
        ApplyPlaybackSettings();
        SaveSettings();
    }

    private void ApplyPlaybackSettings()
    {
        _playback.DiscordDeviceId = SelectedDiscordDevice?.DeviceId;
        _playback.MonitorDeviceId = SelectedMonitorDevice?.DeviceId;
        _playback.EnableMonitor = EnableMonitor;
        _playback.MasterVolume = (float)MasterVolume;
        _playback.OverlapSounds = OverlapSounds;
    }

    private void ApplyMicPassthrough()
    {
        _micPassthrough.MicVolume = (float)MicVolume;
        _micPassthrough.Configure(
            EnableMicrophone,
            SelectedMicrophoneDevice?.DeviceId,
            SelectedDiscordDevice?.DeviceId);

        if (EnableMicrophone && !string.IsNullOrWhiteSpace(_micPassthrough.LastError))
            StatusMessage = $"Mic error: {_micPassthrough.LastError}";
    }

    private void UpdateDiscordHint()
    {
        if (EnableMicrophone && _micPassthrough.IsRunning)
        {
            var micName = SelectedMicrophoneDevice?.Name ?? "microphone";
            StatusMessage = $"Mic live: {micName} → Discord cable";
        }
        else if (SelectedDiscordDevice?.IsVirtualCable == true)
        {
            StatusMessage = "Discord: set mic to CABLE Output / VB-Cable";
        }
        else if (!HasVirtualCable)
        {
            StatusMessage = "Install VB-Audio Cable to send sound to Discord";
        }
    }

    [RelayCommand]
    private void AddSounds()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select audio files",
            Filter = "Audio|*.mp3;*.wav;*.ogg;*.flac;*.m4a;*.aac;*.wma;*.aiff;*.aif|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
            return;

        AddFiles(dialog.FileNames);
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder with sounds"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var files = EnumerateAudioFilesSafe(dialog.FolderName).ToArray();
            AddFiles(files);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Folder access denied: {ex.Message}";
        }
    }

    public void AddDroppedFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                    files.AddRange(EnumerateAudioFilesSafe(path));
                else if (File.Exists(path) && IsAudioFile(path))
                    files.Add(path);
            }
            catch
            {
                // skip paths without permission (Controlled Folder Access / ACL)
            }
        }

        AddFiles(files);
    }

    private static IEnumerable<string> EnumerateAudioFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> subDirs;

            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsAudioFile(file))
                    yield return file;
            }

            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
                pending.Push(sub);
        }
    }

    private static bool IsAudioFile(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private void AddFiles(IEnumerable<string> files)
    {
        var added = 0;
        _suppressSave = true;
        try
        {
            foreach (var file in files)
            {
                if (Pads.Any(p => string.Equals(p.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var pad = new SoundPad
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    ColorIndex = Pads.Count % 8,
                    Volume = 1.0
                };
                Pads.Add(CreatePadVm(pad));
                added++;
            }
        }
        finally
        {
            _suppressSave = false;
        }

        SaveSettings();
        StatusMessage = added == 0 ? "These files are already on the soundboard" : $"Added {added} sounds";
        OnPropertyChanged(nameof(HasPads));
    }

    public bool HasPads => Pads.Count > 0;

    [RelayCommand]
    private void StopAll()
    {
        _playback.StopAll();
        foreach (var pad in Pads)
            pad.IsPlaying = false;
        StatusMessage = "Playback stopped";
    }

    [RelayCommand]
    private void RefreshDeviceList()
    {
        RefreshDevices();
        StatusMessage = "Device list refreshed";
        SaveSettings();
    }

    public bool TryCaptureHotkey(KeyEventArgs e)
    {
        if (_capturingPad is null)
            return false;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _capturingPad.IsCapturingHotkey = false;
            _capturingPad = null;
            StatusMessage = "Hotkey capture cancelled";
            return true;
        }

        var gesture = HotkeyService.FormatGesture(Keyboard.Modifiers, key);

        if (!HotkeyService.IsAllowedGlobalHotkey(Keyboard.Modifiers, key))
        {
            StatusMessage = "Use F1–F24 or a Ctrl/Alt/Shift combo (e.g. Ctrl+1)";
            return true;
        }

        var previous = _capturingPad.Hotkey;

        // Unregister previous gesture first
        if (!string.IsNullOrWhiteSpace(previous))
            _hotkeys.Unregister(previous);

        if (!_hotkeys.Register(gesture, CreateHotkeyCallback(_capturingPad)))
        {
            if (!string.IsNullOrWhiteSpace(previous))
                _hotkeys.Register(previous, CreateHotkeyCallback(_capturingPad));

            StatusMessage = $"Could not register hotkey {gesture} (taken by system/another app?)";
            _capturingPad.IsCapturingHotkey = false;
            _capturingPad = null;
            return true;
        }

        _capturingPad.Hotkey = gesture;
        _capturingPad.IsCapturingHotkey = false;
        StatusMessage = $"Hotkey {_capturingPad.DisplayName}: {gesture}";
        _capturingPad = null;
        SaveSettings();
        return true;
    }

    private SoundPadViewModel CreatePadVm(SoundPad pad) =>
        new(pad, PlayPad, RemovePad, BeginHotkeyCapture, ClearPadHotkey, OnPadVolumeChanged, ScheduleSave);

    private void OnPadVolumeChanged(SoundPadViewModel pad)
    {
        _playback.SetPadVolume(pad.Id, (float)pad.Volume);
        ScheduleSave();
    }

    private void ClearPadHotkey(SoundPadViewModel pad)
    {
        if (!string.IsNullOrWhiteSpace(pad.Hotkey))
            _hotkeys.Unregister(pad.Hotkey);

        pad.Hotkey = null;
        pad.IsCapturingHotkey = false;
        SaveSettings();
        StatusMessage = $"Cleared hotkey: {pad.DisplayName}";
    }

    private void BeginHotkeyCapture(SoundPadViewModel pad)
    {
        if (_capturingPad is not null)
            _capturingPad.IsCapturingHotkey = false;

        _capturingPad = pad;
        pad.IsCapturingHotkey = true;
        StatusMessage = "Press a key combination (Esc = cancel)";
    }

    private void PlayPad(SoundPadViewModel pad)
    {
        try
        {
            _playback.Play(pad.FilePath, (float)pad.Volume, pad.Id);
            pad.IsPlaying = true;
            StatusMessage = $"▶ {pad.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Playback error: {ex.Message}";
        }
    }

    private void RemovePad(SoundPadViewModel pad)
    {
        if (!string.IsNullOrWhiteSpace(pad.Hotkey))
            _hotkeys.Unregister(pad.Hotkey);

        if (_capturingPad == pad)
            _capturingPad = null;

        Pads.Remove(pad);
        SaveSettings();
        OnPropertyChanged(nameof(HasPads));
        StatusMessage = $"Removed {pad.DisplayName}";
    }

    private Action CreateHotkeyCallback(SoundPadViewModel pad) =>
        () =>
        {
            var app = Application.Current;
            if (app?.Dispatcher is null || app.Dispatcher.HasShutdownStarted)
                return;

            app.Dispatcher.BeginInvoke(() => PlayPad(pad));
        };

    private void ReregisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        foreach (var pad in Pads.Where(p => !string.IsNullOrWhiteSpace(p.Hotkey)))
        {
            if (!HotkeyService.TryParseGesture(pad.Hotkey!, out var mods, out var key) ||
                !HotkeyService.IsAllowedGlobalHotkey(mods, key) ||
                !_hotkeys.Register(pad.Hotkey!, CreateHotkeyCallback(pad)))
            {
                StatusMessage = $"Skipped unsafe/taken hotkey: {pad.Hotkey}";
            }
        }
    }

    public void Dispose()
    {
        if (_saveDebounceTimer is not null)
        {
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Tick -= OnSaveDebounceTick;
        }

        SaveSettings();
        _micPassthrough.Dispose();
        _playback.Dispose();
        _hotkeys.Dispose();
    }
}
