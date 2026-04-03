using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synkro.Core;

public enum ChannelMode
{
    Stereo = 0,
    Left = 1,
    Right = 2
}

public class AudioOutputChannel : IDisposable
{
    private readonly MMDevice _device;
    private readonly object _writeLock = new();
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

    // Resampling support for format-incompatible devices (e.g. FxSound virtual device)
    private bool _needsResample;
    private float _resampleRatio;
    private int _inputChannels;   // capture source channel count (may be >2)
    private int _outputChannels;  // actual output channel count (typically 2)
    private float[]? _resampleBuffer;
    private float[]? _stereoBuffer; // for downmixing multichannel → stereo

    // Endpoint volume boost for >100% volume
    private AudioEndpointVolume? _endpointVolume;
    private float _savedEndpointVolume = -1f;
    private bool _endpointBoosted;

    public const float MaxVolume = 5.0f;

    private const int WiredLatencyEstimateMs = 10;
    private const int BluetoothLatencyEstimateMs = 180;

    // PKEY_Device_EnumeratorName = {A45C254E-DF1C-4EFD-8020-67D146A850E0}, 24
    private static readonly NAudio.CoreAudioApi.PropertyKey PKEY_EnumeratorName =
        new(new Guid("{A45C254E-DF1C-4EFD-8020-67D146A850E0}"), 24);

    public string DeviceId => _device.ID;
    public string FriendlyName => _device.FriendlyName;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsEnabled { get; set; } = true;
    public bool IsBluetooth { get; private set; }
    public ChannelMode ChannelMode { get; set; } = ChannelMode.Stereo;
    public string ErrorMessage { get; private set; } = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public int FineTuneMs { get; set; } = 0;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, MaxVolume);
            ApplyEndpointBoost();
        }
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
        try { _endpointVolume = _device.AudioEndpointVolume; } catch { }
    }

    private void DetectBluetooth()
    {
        try
        {
            var prop = _device.Properties[PKEY_EnumeratorName];
            var enumeratorName = prop?.Value as string ?? "";
            IsBluetooth = enumeratorName.Contains("BTH", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            IsBluetooth = false;
        }
    }

    public void Initialize(WaveFormat captureFormat)
    {
        lock (_writeLock)
        {
            try
            {
                ErrorMessage = string.Empty;
                _waveFormat = captureFormat;
                _needsResample = false;
                _inputChannels = captureFormat.Channels;
                _outputChannels = Math.Min(captureFormat.Channels, 2); // stereo output
                _outputLatencyMs = IsBluetooth ? 100 : 30;
                _output?.Stop();
                _output?.Dispose();
                _output = null;

                // Build candidate formats using stereo output (device-native channel count).
                // Multichannel capture (e.g. VB-Audio 16ch) is downmixed to stereo in WriteSamples.
                int outCh = _outputChannels;
                var candidates = new List<WaveFormat>
                {
                    WaveFormat.CreateIeeeFloatWaveFormat(captureFormat.SampleRate, outCh)
                };

                try
                {
                    var mix = _device.AudioClient.MixFormat;
                    if (mix.SampleRate != captureFormat.SampleRate)
                        candidates.Add(WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, outCh));
                }
                catch { }

                // Common sample rates as last resort
                foreach (int rate in new[] { 48000, 44100 })
                {
                    if (candidates.All(c => c.SampleRate != rate))
                        candidates.Add(WaveFormat.CreateIeeeFloatWaveFormat(rate, outCh));
                }

                string? lastError = null;
                WaveFormat? successFormat = null;

                foreach (var fmt in candidates)
                {
                    // Try event-driven mode first (lower latency)
                    lastError = TryInitOutput(fmt, useEventSync: true);
                    if (lastError == null) { successFormat = fmt; break; }

                    // Try timer-driven mode (more compatible)
                    lastError = TryInitOutput(fmt, useEventSync: false);
                    if (lastError == null) { successFormat = fmt; break; }
                }

                if (successFormat == null || _output == null)
                {
                    ErrorMessage = lastError ?? "Cannot initialize output";
                    _output = null;
                    return;
                }

                int outSampleRate = successFormat.SampleRate;
                if (outSampleRate != captureFormat.SampleRate)
                {
                    _needsResample = true;
                    _resampleRatio = (float)outSampleRate / captureFormat.SampleRate;
                }

                _delayBuffer = new DelayBuffer(outSampleRate, outCh, _delayMs);

                int maxSamples = (int)(outSampleRate * outCh * 0.1);
                _processBuffer = new float[maxSamples];
                _delayedBuffer = new float[maxSamples];
                _byteBuffer = new byte[maxSamples * 4];
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                _output = null;
            }
        }
    }

    private string? TryInitOutput(WaveFormat format, bool useEventSync)
    {
        try
        {
            _output?.Dispose();
            _bufferedProvider = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true
            };
            _output = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync, _outputLatencyMs);
            _output.Init(_bufferedProvider);
            return null;
        }
        catch (Exception ex)
        {
            _output?.Dispose();
            _output = null;
            return ex.Message;
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
        lock (_writeLock)
        {
            if (_bufferedProvider == null || _delayBuffer == null || _waveFormat == null)
                return;

            float[] workSamples = samples;
            int workCount = count;

            // Downmix multichannel capture (e.g. 16ch VB-Audio) to stereo.
            // Extract channels 0 (L) and 1 (R) from the interleaved source.
            if (_inputChannels > _outputChannels)
            {
                int frames = workCount / _inputChannels;
                int stereoCount = frames * _outputChannels;
                if (_stereoBuffer == null || _stereoBuffer.Length < stereoCount)
                    _stereoBuffer = new float[stereoCount];

                for (int f = 0; f < frames; f++)
                {
                    int srcOff = f * _inputChannels;
                    int dstOff = f * _outputChannels;
                    for (int ch = 0; ch < _outputChannels; ch++)
                        _stereoBuffer[dstOff + ch] = workSamples[srcOff + ch];
                }

                workSamples = _stereoBuffer;
                workCount = stereoCount;
            }

            if (_needsResample)
            {
                workCount = Resample(workSamples, workCount);
                workSamples = _resampleBuffer!;
            }

            if (_processBuffer == null || workCount > _processBuffer.Length)
            {
                _processBuffer = new float[workCount];
                _delayedBuffer = new float[workCount];
                _byteBuffer = new byte[workCount * 4];
            }

            VolumeProcessor.Apply(workSamples, _processBuffer, workCount, _volume);

            // L/R channel extraction: Left=index 0, Right=index 1 (standard PCM interleaving)
            if (ChannelMode != ChannelMode.Stereo && _outputChannels >= 2)
            {
                int srcCh = ChannelMode == ChannelMode.Left ? 0 : 1;
                for (int i = 0; i < workCount; i += _outputChannels)
                {
                    float sample = _processBuffer[i + srcCh];
                    for (int ch = 0; ch < _outputChannels; ch++)
                        _processBuffer[i + ch] = sample;
                }
            }

            _delayBuffer.Process(_processBuffer, _delayedBuffer!, workCount);

            // Safety clamp — tanh compression keeps signal in range, this is a fallback
            for (int i = 0; i < workCount; i++)
                _delayedBuffer![i] = Math.Clamp(_delayedBuffer[i], -1.0f, 1.0f);

            Buffer.BlockCopy(_delayedBuffer!, 0, _byteBuffer!, 0, workCount * 4);
            _bufferedProvider.AddSamples(_byteBuffer!, 0, workCount * 4);
        }
    }

    /// <summary>
    /// Linear interpolation resampler for converting between sample rates.
    /// Returns the number of output samples.
    /// </summary>
    private int Resample(float[] input, int inputCount)
    {
        int channels = _outputChannels;
        int inputFrames = inputCount / channels;
        int outputFrames = (int)(inputFrames * _resampleRatio);
        int outputCount = outputFrames * channels;

        if (_resampleBuffer == null || _resampleBuffer.Length < outputCount)
            _resampleBuffer = new float[outputCount];

        double invRatio = 1.0 / _resampleRatio;
        for (int i = 0; i < outputFrames; i++)
        {
            double srcPos = i * invRatio;
            int srcIdx = (int)srcPos;
            float frac = (float)(srcPos - srcIdx);
            int nextIdx = Math.Min(srcIdx + 1, inputFrames - 1);

            for (int ch = 0; ch < channels; ch++)
            {
                float s1 = input[srcIdx * channels + ch];
                float s2 = input[nextIdx * channels + ch];
                _resampleBuffer[i * channels + ch] = s1 + (s2 - s1) * frac;
            }
        }

        return outputCount;
    }

    /// Returns estimated total device latency in ms.
    /// Bluetooth: ~180ms (codec + transport), Wired: ~10ms.
    public long GetEstimatedLatencyMs()
    {
        return IsBluetooth ? BluetoothLatencyEstimateMs : WiredLatencyEstimateMs;
    }

    public void Stop()
    {
        lock (_writeLock)
        {
            _output?.Stop();
            RestoreEndpointVolume();
        }
    }

    /// <summary>
    /// Gradually scale device endpoint volume for boost above 100%.
    /// At 100%: endpoint stays at original. At 500%: endpoint at max (1.0).
    /// Linear interpolation between — no sudden jumps.
    /// </summary>
    private void ApplyEndpointBoost()
    {
        if (_endpointVolume == null) return;
        try
        {
            if (_volume > 1.0f)
            {
                if (!_endpointBoosted)
                {
                    _savedEndpointVolume = _endpointVolume.MasterVolumeLevelScalar;
                    _endpointBoosted = true;
                }
                float t = (_volume - 1.0f) / (MaxVolume - 1.0f);
                float target = _savedEndpointVolume + (1.0f - _savedEndpointVolume) * t;
                _endpointVolume.MasterVolumeLevelScalar = target;
            }
            else if (_endpointBoosted)
            {
                if (_savedEndpointVolume >= 0f)
                    _endpointVolume.MasterVolumeLevelScalar = _savedEndpointVolume;
                _endpointBoosted = false;
            }
        }
        catch { }
    }

    private void RestoreEndpointVolume()
    {
        if (!_endpointBoosted || _endpointVolume == null || _savedEndpointVolume < 0f) return;
        try { _endpointVolume.MasterVolumeLevelScalar = _savedEndpointVolume; } catch { }
        _endpointBoosted = false;
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            RestoreEndpointVolume();
            _output?.Stop();
            _output?.Dispose();
            _output = null;
            _bufferedProvider = null;
        }
    }
}
