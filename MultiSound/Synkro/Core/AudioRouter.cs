using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synkro.Core;

public class AudioRouter : IDisposable
{
    private readonly AudioCaptureEngine _captureEngine;
    private readonly List<AudioOutputChannel> _channels = new();
    private readonly object _lock = new();
    private int _globalFineTuneMs;

    public IReadOnlyList<AudioOutputChannel> Channels
    {
        get { lock (_lock) return _channels.ToList(); }
    }

    public int GlobalFineTuneMs
    {
        get => _globalFineTuneMs;
        set => _globalFineTuneMs = value;
    }

    /// <summary>
    /// Set this to the capture source device ID so auto-delay accounts for its latency.
    /// When capturing from a high-latency device (e.g. BT), output devices need extra
    /// delay to stay in sync with the native playback on the capture source.
    /// </summary>
    public string? CaptureSourceDeviceId { get; set; }

    public AudioRouter(AudioCaptureEngine captureEngine)
    {
        _captureEngine = captureEngine;
        _captureEngine.DataAvailable += OnCaptureDataAvailable;
    }

    public AudioOutputChannel AddDevice(MMDevice device)
    {
        var channel = new AudioOutputChannel(device);

        if (_captureEngine.WaveFormat != null)
        {
            channel.Initialize(_captureEngine.WaveFormat);
        }

        lock (_lock) _channels.Add(channel);
        return channel;
    }

    public void RemoveDevice(string deviceId)
    {
        lock (_lock)
        {
            var channel = _channels.Find(c => c.DeviceId == deviceId);
            if (channel != null)
            {
                channel.Stop();
                channel.Dispose();
                _channels.Remove(channel);
            }
        }
    }

    public void InitializeAll()
    {
        if (_captureEngine.WaveFormat == null) return;

        lock (_lock)
        {
            foreach (var channel in _channels)
            {
                channel.Initialize(_captureEngine.WaveFormat);
            }
        }

        ApplyAutoDelay();
    }

    public void ApplyAutoDelay()
    {
        lock (_lock)
        {
            var active = _channels.Where(c => c.IsEnabled).ToList();
            if (active.Count == 0) return;

            // Include capture source latency: the capture device plays audio natively,
            // so output devices must be delayed to match its hardware latency.
            long captureLatency = 0;
            if (CaptureSourceDeviceId != null)
            {
                var captureCh = _channels.FirstOrDefault(c => c.DeviceId == CaptureSourceDeviceId);
                if (captureCh != null)
                    captureLatency = captureCh.GetEstimatedLatencyMs();
            }

            long maxOutputLatency = active.Max(c => c.GetEstimatedLatencyMs());
            long maxLatency = Math.Max(captureLatency, maxOutputLatency);

            foreach (var channel in active)
            {
                long deviceLatency = channel.GetEstimatedLatencyMs();
                int compensationMs = (int)(maxLatency - deviceLatency) + _globalFineTuneMs;
                channel.DelayMs = Math.Max(0, compensationMs);
            }
        }
    }

    public void StartAll()
    {
        lock (_lock)
        {
            foreach (var channel in _channels)
            {
                if (channel.IsEnabled)
                    channel.Start();
            }
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var channel in _channels)
                channel.Stop();
        }
    }

    private void OnCaptureDataAvailable(object? sender, float[] samples)
    {
        List<AudioOutputChannel> snapshot;
        lock (_lock) { snapshot = _channels.ToList(); }

        foreach (var channel in snapshot)
        {
            if (channel.IsEnabled && channel.IsPlaying)
                channel.WriteSamples(samples, samples.Length);
        }
    }

    public void Dispose()
    {
        StopAll();
        lock (_lock)
        {
            foreach (var channel in _channels)
                channel.Dispose();
            _channels.Clear();
        }
    }
}
