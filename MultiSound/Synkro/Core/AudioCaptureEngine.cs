using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synkro.Core;

public class AudioCaptureEngine : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    public WaveFormat? WaveFormat => _capture?.WaveFormat;
    public string? CaptureDeviceId { get; private set; }

    public event EventHandler<float[]>? DataAvailable;
    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureStopped;

    public void Start()
    {
        _capture?.StopRecording();
        _capture?.Dispose();

        // Detect default render device (capture source)
        using var enumerator = new MMDeviceEnumerator();
        var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        CaptureDeviceId = defaultDevice.ID;

        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        CaptureStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // Convert byte[] to float[] (WASAPI Loopback is 32-bit IEEE float)
        int sampleCount = e.BytesRecorded / 4;
        var samples = new float[sampleCount];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        DataAvailable?.Invoke(this, samples);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        CaptureStopped?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _capture?.StopRecording();
    }

    public void Dispose()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
    }
}
