using NAudio.CoreAudioApi;

namespace Synkro.Services;

/// <summary>
/// Monitors the default audio endpoint volume and fires events when it changes.
/// Used to sync all output channel volumes proportionally with system volume.
/// </summary>
public class VolumeSyncService : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _defaultDevice;
    private AudioEndpointVolume? _endpointVolume;
    private float _referenceVolume;

    /// <summary>
    /// Fires when system volume changes. Parameter is the scale factor
    /// relative to the reference volume (e.g., 0.75 means system went to 75% of reference).
    /// </summary>
    public event EventHandler<float>? VolumeScaleChanged;

    public float CurrentSystemVolume => _endpointVolume?.MasterVolumeLevelScalar ?? 1.0f;

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpointVolume = _defaultDevice.AudioEndpointVolume;
            _referenceVolume = _endpointVolume.MasterVolumeLevelScalar;
            _endpointVolume.OnVolumeNotification += OnVolumeNotification;
        }
        catch
        {
            // No default device or access denied — volume sync disabled
        }
    }

    /// <summary>
    /// Call this when the user manually adjusts a channel volume in the app.
    /// Records the current system volume as the new reference point.
    /// </summary>
    public void MarkReferenceVolume()
    {
        _referenceVolume = _endpointVolume?.MasterVolumeLevelScalar ?? 1.0f;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        if (_referenceVolume <= 0.001f) return; // avoid divide by zero

        float scale = data.MasterVolume / _referenceVolume;
        VolumeScaleChanged?.Invoke(this, scale);
    }

    public void Stop()
    {
        if (_endpointVolume != null)
            _endpointVolume.OnVolumeNotification -= OnVolumeNotification;
    }

    public void Dispose()
    {
        Stop();
        _endpointVolume = null;
        _defaultDevice = null;
        _enumerator?.Dispose();
        _enumerator = null;
    }
}
