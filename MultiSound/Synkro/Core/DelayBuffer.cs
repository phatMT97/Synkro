namespace Synkro.Core;

public class DelayBuffer
{
    private class BufferState
    {
        public float[] Buffer;
        public int WritePos;
        public int ReadPos;
        public int DelaySamples;

        public BufferState(int delaySamples)
        {
            DelaySamples = delaySamples;
            Buffer = new float[Math.Max(delaySamples * 2, 1)];
            WritePos = delaySamples;
            ReadPos = 0;
        }
    }

    private volatile BufferState _state;
    private readonly int _sampleRate;
    private readonly int _channels;

    public DelayBuffer(int sampleRate, int channels, int delayMs)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _state = new BufferState(MsToSamples(delayMs));
    }

    public void Process(float[] input, float[] output, int count)
    {
        var state = _state; // snapshot

        if (state.DelaySamples == 0)
        {
            Array.Copy(input, output, count);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            state.Buffer[state.WritePos] = input[i];
            output[i] = state.Buffer[state.ReadPos];

            state.WritePos = (state.WritePos + 1) % state.Buffer.Length;
            state.ReadPos = (state.ReadPos + 1) % state.Buffer.Length;
        }
    }

    public void UpdateDelay(int delayMs)
    {
        var newState = new BufferState(MsToSamples(delayMs));
        Interlocked.Exchange(ref _state, newState);
    }

    private int MsToSamples(int ms)
    {
        return _sampleRate * _channels * ms / 1000;
    }
}
