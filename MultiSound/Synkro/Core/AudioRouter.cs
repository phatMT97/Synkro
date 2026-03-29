using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synkro.Core;

public class AudioRouter : IDisposable
{
    private readonly AudioCaptureEngine _captureEngine;
    private readonly List<AudioOutputChannel> _channels = new();
    private readonly object _lock = new();
    private volatile AudioOutputChannel[] _channelSnapshot = Array.Empty<AudioOutputChannel>();

    public IReadOnlyList<AudioOutputChannel> Channels
    {
        get { lock (_lock) return _channels.ToList(); }
    }

    /// <summary>
    /// Set this to the capture source device ID so auto-delay accounts for its latency.
    /// When capturing from a high-latency device (e.g. BT), output devices need extra
    /// delay to stay in sync with the native playback on the capture source.
    /// </summary>
    public string? CaptureSourceDeviceId { get; set; }

    /// <summary>L/R channel mode applied to the capture source before routing.</summary>
    public ChannelMode CaptureChannelMode { get; set; } = ChannelMode.Stereo;
    public int CaptureChannels { get; set; } = 2;

    public AudioRouter(AudioCaptureEngine captureEngine)
    {
        _captureEngine = captureEngine;
        _captureEngine.DataAvailable += OnCaptureDataAvailable;
    }

    /// <summary>
    /// Rebuilds the volatile snapshot used by the capture callback (lock-free read path).
    /// Must be called under _lock.
    /// </summary>
    private void UpdateSnapshot()
    {
        _channelSnapshot = _channels.ToArray();
    }

    public AudioOutputChannel AddDevice(MMDevice device)
    {
        var channel = new AudioOutputChannel(device);

        if (_captureEngine.WaveFormat != null)
        {
            channel.Initialize(_captureEngine.WaveFormat);
        }

        lock (_lock)
        {
            _channels.Add(channel);
            UpdateSnapshot();
        }
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
                UpdateSnapshot();
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

            long captureLatency = 0;
            if (CaptureSourceDeviceId != null)
            {
                var captureCh = _channels.FirstOrDefault(c => c.DeviceId == CaptureSourceDeviceId);
                if (captureCh != null)
                    captureLatency = captureCh.GetEstimatedLatencyMs();
            }

            long maxLatency = Math.Max(captureLatency,
                active.Max(c => c.GetEstimatedLatencyMs()));

            foreach (var channel in active)
            {
                long deviceLatency = channel.GetEstimatedLatencyMs();
                int autoMs = (int)(maxLatency - deviceLatency);
                channel.DelayMs = Math.Max(0, autoMs + channel.FineTuneMs);
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
        float[] workSamples = samples;

        // Apply source-level L/R filter before routing to outputs.
        // Channel indices are intentionally swapped: Left=1, Right=0.
        // WASAPI loopback on this hardware returns channels in reversed order.
        if (CaptureChannelMode != ChannelMode.Stereo && CaptureChannels >= 2)
        {
            // Copy to avoid mutating the shared capture buffer
            workSamples = new float[samples.Length];
            Array.Copy(samples, workSamples, samples.Length);

            int srcCh = CaptureChannelMode == ChannelMode.Left ? 1 : 0;
            for (int i = 0; i < workSamples.Length; i += CaptureChannels)
            {
                float sample = workSamples[i + srcCh];
                for (int ch = 0; ch < CaptureChannels; ch++)
                    workSamples[i + ch] = sample;
            }
        }

        // Read volatile snapshot — no lock needed on the hot path
        var snapshot = _channelSnapshot;

        foreach (var channel in snapshot)
        {
            if (channel.IsEnabled && channel.IsPlaying)
                channel.WriteSamples(workSamples, workSamples.Length);
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
            UpdateSnapshot();
        }
    }
}
