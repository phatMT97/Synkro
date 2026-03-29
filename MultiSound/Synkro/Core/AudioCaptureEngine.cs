using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synkro.Core;

public class AudioCaptureEngine : IDisposable
{
    private readonly object _captureLock = new();
    private WasapiLoopbackCapture? _capture;
    private float[]? _sampleBuffer;

    public WaveFormat? WaveFormat
    {
        get { lock (_captureLock) return _capture?.WaveFormat; }
    }

    public string? CaptureDeviceId { get; private set; }

    public event EventHandler<float[]>? DataAvailable;
    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureStopped;

    public void Start(MMDevice? device = null)
    {
        lock (_captureLock)
        {
            _capture?.StopRecording();
            _capture?.Dispose();

            if (device != null)
            {
                CaptureDeviceId = device.ID;
                _capture = new WasapiLoopbackCapture(device);
            }
            else
            {
                using var enumerator = new MMDeviceEnumerator();
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                CaptureDeviceId = defaultDevice.ID;
                _capture = new WasapiLoopbackCapture();
            }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }

        CaptureStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        int sampleCount = e.BytesRecorded / 4;

        // Reuse buffer to reduce GC pressure; only reallocate when size changes
        if (_sampleBuffer == null || _sampleBuffer.Length != sampleCount)
            _sampleBuffer = new float[sampleCount];

        Buffer.BlockCopy(e.Buffer, 0, _sampleBuffer, 0, e.BytesRecorded);
        DataAvailable?.Invoke(this, _sampleBuffer);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        CaptureStopped?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        lock (_captureLock)
        {
            _capture?.StopRecording();
        }
    }

    public void Dispose()
    {
        lock (_captureLock)
        {
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;
        }
    }
}
