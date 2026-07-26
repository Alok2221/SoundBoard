using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundboardApp.Services;

public sealed class AudioPlaybackService : IDisposable
{
    private readonly List<PlaybackSession> _sessions = [];
    private readonly object _lock = new();
    private bool _disposed;
    private float _masterVolume = 0.85f;

    public string? DiscordDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public bool EnableMonitor { get; set; } = true;
    public bool OverlapSounds { get; set; } = true;

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1f);
            RefreshAllVolumes();
        }
    }

    public event EventHandler? PlaybackStateChanged;

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
                return _sessions.Count > 0;
        }
    }

    public void Play(string filePath, float padVolume = 1f, Guid? padId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        if (!OverlapSounds)
            StopAll();

        var discordDevice = DiscordDeviceId;
        var monitorDevice = MonitorDeviceId;

        var session = new PlaybackSession
        {
            PadId = padId,
            PadVolume = Math.Clamp(padVolume, 0f, 1f)
        };

        try
        {
            session.DiscordReader = OpenReader(filePath);
            session.DiscordVolume = CreateVolumeProvider(session.DiscordReader, EffectiveVolume(session.PadVolume));
            session.DiscordOut = CreateOutput(discordDevice);
            session.DiscordOut.Init(session.DiscordVolume);
            session.DiscordOut.PlaybackStopped += OnPlaybackStopped;

            if (EnableMonitor)
            {
                // Playing twice to the same device can fail on some Win11 drivers -
                // when monitor == discord, skip the second output.
                if (!SameDevice(discordDevice, monitorDevice))
                {
                    session.MonitorReader = OpenReader(filePath);
                    session.MonitorVolume = CreateVolumeProvider(session.MonitorReader, EffectiveVolume(session.PadVolume));
                    session.MonitorOut = CreateOutput(monitorDevice);
                    session.MonitorOut.Init(session.MonitorVolume);
                    session.MonitorOut.PlaybackStopped += OnPlaybackStopped;
                }
            }

            lock (_lock)
                _sessions.Add(session);

            session.DiscordOut.Play();
            session.MonitorOut?.Play();
            RaisePlaybackStateChanged();
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void SetPadVolume(Guid padId, float padVolume)
    {
        var volume = Math.Clamp(padVolume, 0f, 1f);
        lock (_lock)
        {
            foreach (var session in _sessions.Where(s => s.PadId == padId))
            {
                session.PadVolume = volume;
                ApplyVolume(session);
            }
        }
    }

    public void StopAll()
    {
        List<PlaybackSession> copy;
        lock (_lock)
        {
            copy = _sessions.ToList();
            _sessions.Clear();
        }

        foreach (var session in copy)
            session.Dispose();

        RaisePlaybackStateChanged();
    }

    private static bool SameDevice(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);

    private void RefreshAllVolumes()
    {
        lock (_lock)
        {
            foreach (var session in _sessions)
                ApplyVolume(session);
        }
    }

    private void ApplyVolume(PlaybackSession session)
    {
        var volume = EffectiveVolume(session.PadVolume);
        if (session.DiscordVolume is not null)
            session.DiscordVolume.Volume = volume;
        if (session.MonitorVolume is not null)
            session.MonitorVolume.Volume = volume;
    }

    private float EffectiveVolume(float padVolume) =>
        Math.Clamp(_masterVolume * padVolume, 0f, 1f);

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            var finished = _sessions.Where(s => s.IsFinished).ToList();
            foreach (var session in finished)
            {
                _sessions.Remove(session);
                session.Dispose();
            }
        }

        RaisePlaybackStateChanged();
    }

    private void RaisePlaybackStateChanged()
    {
        try
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // ignore subscribers after UI shutdown
        }
    }

    private static IWavePlayer CreateOutput(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return new WasapiOut(AudioClientShareMode.Shared, true, 80);

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        return new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
    }

    private static VolumeSampleProvider CreateVolumeProvider(WaveStream reader, float volume)
    {
        ISampleProvider sampleProvider = reader.ToSampleProvider();

        if (sampleProvider.WaveFormat.Channels == 1)
            sampleProvider = new MonoToStereoSampleProvider(sampleProvider);

        if (sampleProvider.WaveFormat.SampleRate != 48000)
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 48000);

        return new VolumeSampleProvider(sampleProvider) { Volume = volume };
    }

    private static WaveStream OpenReader(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".ogg" => new VorbisWaveReader(filePath),
                ".wav" => new AudioFileReader(filePath),
                ".aiff" or ".aif" => new AiffFileReader(filePath),
                _ => new AudioFileReader(filePath)
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot open audio file ({ext}). Check codecs / whether the file is corrupted.", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAll();
    }

    private sealed class PlaybackSession : IDisposable
    {
        public Guid? PadId { get; set; }
        public float PadVolume { get; set; } = 1f;
        public WaveStream? DiscordReader { get; set; }
        public WaveStream? MonitorReader { get; set; }
        public VolumeSampleProvider? DiscordVolume { get; set; }
        public VolumeSampleProvider? MonitorVolume { get; set; }
        public IWavePlayer? DiscordOut { get; set; }
        public IWavePlayer? MonitorOut { get; set; }

        public bool IsFinished
        {
            get
            {
                var discordDone = DiscordOut is null || DiscordOut.PlaybackState != PlaybackState.Playing;
                var monitorDone = MonitorOut is null || MonitorOut.PlaybackState != PlaybackState.Playing;
                return discordDone && monitorDone;
            }
        }

        public void Dispose()
        {
            SafeDispose(DiscordOut);
            SafeDispose(MonitorOut);
            SafeDispose(DiscordReader);
            SafeDispose(MonitorReader);
            DiscordOut = null;
            MonitorOut = null;
            DiscordReader = null;
            MonitorReader = null;
            DiscordVolume = null;
            MonitorVolume = null;
        }

        private static void SafeDispose(IDisposable? disposable)
        {
            try
            {
                if (disposable is IWavePlayer player)
                    player.Stop();

                disposable?.Dispose();
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }
}
