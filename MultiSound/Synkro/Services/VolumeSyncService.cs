using NAudio.CoreAudioApi;

namespace Synkro.Services;

/// <summary>
/// Monitors the default audio endpoint volume and fires scale events.
/// When system volume changes, all output slots scale proportionally.
/// Uses polling for compatibility with virtual audio devices (e.g. VB-Audio)
/// that may not fire COM volume notifications.
/// </summary>
public class VolumeSyncService : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _defaultDevice;
    private AudioEndpointVolume? _endpointVolume;
    private float _referenceVolume;
    private float _lastVolume;
    private Timer? _pollTimer;

    private const int PollIntervalMs = 100;
    private const float ChangeThreshold = 0.001f;

    /// <summary>
    /// Fires when system volume changes. Parameter is the scale factor
    /// relative to the reference volume (e.g., 0.5 means system went to 50% of reference).
    /// </summary>
    public event EventHandler<float>? VolumeScaleChanged;

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpointVolume = _defaultDevice.AudioEndpointVolume;
            _referenceVolume = _endpointVolume.MasterVolumeLevelScalar;
            _lastVolume = _referenceVolume;

            _pollTimer = new Timer(PollVolume, null, PollIntervalMs, PollIntervalMs);
        }
        catch { }
    }

    /// <summary>
    /// Record current system volume as the new baseline.
    /// Call this when the user manually adjusts a slot volume in the app.
    /// </summary>
    public void MarkReferenceVolume()
    {
        try
        {
            var vol = _endpointVolume?.MasterVolumeLevelScalar ?? 1.0f;
            _referenceVolume = vol;
            _lastVolume = vol;
        }
        catch { }
    }

    private void PollVolume(object? state)
    {
        try
        {
            if (_endpointVolume == null || _referenceVolume <= 0.001f) return;

            float current = _endpointVolume.MasterVolumeLevelScalar;
            if (MathF.Abs(current - _lastVolume) < ChangeThreshold) return;

            _lastVolume = current;
            float scale = current / _referenceVolume;
            VolumeScaleChanged?.Invoke(this, scale);
        }
        catch { }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _endpointVolume = null;
        _defaultDevice = null;
        _enumerator?.Dispose();
        _enumerator = null;
    }
}
