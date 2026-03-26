using Synkro.Core;
using Xunit;

namespace Synkro.Tests.Core;

public class DelayBufferTests
{
    [Fact]
    public void ZeroDelay_PassesThrough()
    {
        var buffer = new DelayBuffer(sampleRate: 48000, channels: 2, delayMs: 0);
        var input = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var output = new float[4];

        buffer.Process(input, output, input.Length);

        Assert.Equal(input, output);
    }

    [Fact]
    public void WithDelay_OutputIsSilentThenDelayed()
    {
        // 1ms delay at 48kHz stereo = 96 samples
        var buffer = new DelayBuffer(sampleRate: 48000, channels: 2, delayMs: 1);
        int delaySamples = 48000 * 2 * 1 / 1000; // 96

        // Send delaySamples worth of 1.0f
        var input1 = new float[delaySamples];
        Array.Fill(input1, 1.0f);
        var output1 = new float[delaySamples];
        buffer.Process(input1, output1, delaySamples);

        // First chunk should be silence (delay buffer initially zero)
        Assert.All(output1, sample => Assert.Equal(0.0f, sample));

        // Send another chunk — should now get the first chunk's data
        var input2 = new float[delaySamples];
        Array.Fill(input2, 2.0f);
        var output2 = new float[delaySamples];
        buffer.Process(input2, output2, delaySamples);

        Assert.All(output2, sample => Assert.Equal(1.0f, sample));
    }

    [Fact]
    public void UpdateDelay_ChangesBufferSize()
    {
        var buffer = new DelayBuffer(sampleRate: 48000, channels: 2, delayMs: 0);
        var input = new float[] { 1.0f, 2.0f };
        var output = new float[2];

        buffer.Process(input, output, 2);
        Assert.Equal(new float[] { 1.0f, 2.0f }, output);

        // Now add delay
        buffer.UpdateDelay(1);

        int delaySamples = 48000 * 2 * 1 / 1000;
        var input2 = new float[delaySamples];
        Array.Fill(input2, 5.0f);
        var output2 = new float[delaySamples];
        buffer.Process(input2, output2, delaySamples);

        // Output should be silence (buffer was reset on delay change)
        Assert.All(output2, sample => Assert.Equal(0.0f, sample));
    }
}
