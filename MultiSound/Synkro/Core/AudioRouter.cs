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

            if (active.Count == 1)
            {
                // Single channel: only apply fine-tune as delay
                active[0].DelayMs = Math.Max(0, _globalFineTuneMs);
                return;
            }

            // Multiple channels: auto-compensate + fine-tune
            long maxLatency = active.Max(c => c.GetEstimatedLatencyMs());

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
