using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundboardApp.Services;

/// <summary>
/// Captures a physical microphone and plays it into the Discord virtual-cable output
/// so voice chat still works when Discord's input is set to CABLE Output.
/// </summary>
public sealed class MicPassthroughService : IDisposable
{
    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private IWavePlayer? _output;
    private BufferedWaveProvider? _buffer;
    private VolumeSampleProvider? _volume;
    private bool _disposed;
    private string? _inputDeviceId;
    private string? _outputDeviceId;
    private bool _enabled;
    private float _micVolume = 1f;

    public float MicVolume
    {
        get => _micVolume;
        set
        {
            _micVolume = Math.Clamp(value, 0f, 1f);
            lock (_lock)
            {
                if (_volume is not null)
                    _volume.Volume = _micVolume;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _capture is not null;
        }
    }

    public string? LastError { get; private set; }

    public void Configure(bool enabled, string? inputDeviceId, string? outputDeviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            var same =
                _enabled == enabled
                && string.Equals(_inputDeviceId, inputDeviceId, StringComparison.Ordinal)
                && string.Equals(_outputDeviceId, outputDeviceId, StringComparison.Ordinal);

            if (same && (_capture is not null || !enabled))
                return;

            _enabled = enabled;
            _inputDeviceId = inputDeviceId;
            _outputDeviceId = outputDeviceId;
            RestartUnlocked();
        }
    }

    public void Stop()
    {
        lock (_lock)
            StopUnlocked();
    }

    private void RestartUnlocked()
    {
        StopUnlocked();
        LastError = null;

        if (!_enabled)
            return;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var inputDevice = ResolveCaptureDevice(enumerator, _inputDeviceId);
            var outputDevice = ResolveRenderDevice(enumerator, _outputDeviceId);

            if (string.Equals(inputDevice.ID, outputDevice.ID, StringComparison.Ordinal))
            {
                LastError = "Microphone and Discord output cannot be the same device.";
                return;
            }

            _capture = new WasapiCapture(inputDevice);
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(200)
            };

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            ISampleProvider samples = _buffer.ToSampleProvider();
            if (samples.WaveFormat.Channels == 1)
                samples = new MonoToStereoSampleProvider(samples);

            if (samples.WaveFormat.SampleRate != 48000)
                samples = new WdlResamplingSampleProvider(samples, 48000);

            _volume = new VolumeSampleProvider(samples) { Volume = _micVolume };

            _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 60);
            _output.Init(_volume);
            _output.Play();
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            LastError = ex.GetBaseException().Message;
            StopUnlocked();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var buffer = _buffer;
        if (buffer is null || e.BytesRecorded <= 0)
            return;

        try
        {
            buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
        catch
        {
            // overflow discarded by provider settings
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            LastError = e.Exception.GetBaseException().Message;
    }

    private void StopUnlocked()
    {
        if (_capture is not null)
        {
            try
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                if (_capture.CaptureState != CaptureState.Stopped)
                    _capture.StopRecording();
            }
            catch
            {
            }

            try
            {
                _capture.Dispose();
            }
            catch
            {
            }

            _capture = null;
        }

        if (_output is not null)
        {
            try
            {
                _output.Stop();
                _output.Dispose();
            }
            catch
            {
            }

            _output = null;
        }

        _buffer = null;
        _volume = null;
    }

    private static MMDevice ResolveCaptureDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        return enumerator.GetDevice(deviceId);
    }

    private static MMDevice ResolveRenderDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        return enumerator.GetDevice(deviceId);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_lock)
            StopUnlocked();
    }
}
