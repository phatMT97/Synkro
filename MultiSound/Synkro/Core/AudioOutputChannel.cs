using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synkro.Core;

public class AudioOutputChannel : IDisposable
{
    private readonly MMDevice _device;
    private WasapiOut? _output;
    private BufferedWaveProvider? _bufferedProvider;
    private DelayBuffer? _delayBuffer;
    private volatile float _volume = 1.0f;
    private volatile int _delayMs;
    private WaveFormat? _waveFormat;
    private int _outputLatencyMs;

    private float[]? _processBuffer;
    private float[]? _delayedBuffer;
    private byte[]? _byteBuffer;

    // Estimated latency: wired ~10ms, Bluetooth ~180ms
    private const int WiredLatencyEstimateMs = 10;
    private const int BluetoothLatencyEstimateMs = 180;

    public string DeviceId => _device.ID;
    public string FriendlyName => _device.FriendlyName;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsEnabled { get; set; } = true;
    public bool IsBluetooth { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0f, 3.0f);
    }

    public int DelayMs
    {
        get => _delayMs;
        set
        {
            _delayMs = Math.Max(0, value);
            _delayBuffer?.UpdateDelay(_delayMs);
        }
    }

    public AudioOutputChannel(MMDevice device)
    {
        _device = device;
        DetectBluetooth();
    }

    private void DetectBluetooth()
    {
        try
        {
            // PnP instance ID contains "BTHENUM" for Bluetooth devices
            IsBluetooth = _device.InstanceId
                .Contains("BTHENUM", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            IsBluetooth = false;
        }
    }

    public void Initialize(WaveFormat captureFormat)
    {
        try
        {
            ErrorMessage = string.Empty;
            _waveFormat = captureFormat;
            _bufferedProvider = new BufferedWaveProvider(captureFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true
            };

            _delayBuffer = new DelayBuffer(
                captureFormat.SampleRate,
                captureFormat.Channels,
                _delayMs);

            int maxSamples = (int)(captureFormat.SampleRate * captureFormat.Channels * 0.1);
            _processBuffer = new float[maxSamples];
            _delayedBuffer = new float[maxSamples];
            _byteBuffer = new byte[maxSamples * 4];

            // Lower latency for wired, standard for Bluetooth
            _outputLatencyMs = IsBluetooth ? 100 : 30;
            _output?.Stop();
            _output?.Dispose();
            _output = new WasapiOut(_device, AudioClientShareMode.Shared, true, _outputLatencyMs);
            _output.Init(_bufferedProvider);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _output = null;
        }
    }

    public void Start()
    {
        if (!IsEnabled || _output == null) return;
        try
        {
            _output.Play();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public void WriteSamples(float[] samples, int count)
    {
        if (_bufferedProvider == null || _delayBuffer == null || _waveFormat == null)
            return;
        if (_processBuffer == null || count > _processBuffer.Length)
        {
            _processBuffer = new float[count];
            _delayedBuffer = new float[count];
            _byteBuffer = new byte[count * 4];
        }

        VolumeProcessor.Apply(samples, _processBuffer, count, _volume);
        _delayBuffer.Process(_processBuffer, _delayedBuffer!, count);

        // Clamp samples to [-1, 1] to prevent clipping when volume > 100%
        for (int i = 0; i < count; i++)
            _delayedBuffer![i] = Math.Clamp(_delayedBuffer[i], -1.0f, 1.0f);

        Buffer.BlockCopy(_delayedBuffer!, 0, _byteBuffer!, 0, count * 4);
        _bufferedProvider.AddSamples(_byteBuffer!, 0, count * 4);
    }

    /// Returns estimated total device latency in ms.
    /// Bluetooth: ~180ms (codec + transport), Wired: ~10ms.
    public long GetEstimatedLatencyMs()
    {
        return IsBluetooth ? BluetoothLatencyEstimateMs : WiredLatencyEstimateMs;
    }

    public void Stop()
    {
        _output?.Stop();
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _bufferedProvider = null;
    }
}
