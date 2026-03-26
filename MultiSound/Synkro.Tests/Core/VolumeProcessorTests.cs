using Xunit;
using Synkro.Core;

namespace Synkro.Tests.Core;

public class VolumeProcessorTests
{
    [Fact]
    public void FullVolume_NoChange()
    {
        var input = new float[] { 0.5f, -0.5f, 1.0f, -1.0f };
        var output = new float[4];

        VolumeProcessor.Apply(input, output, 4, volume: 1.0f);

        Assert.Equal(input, output);
    }

    [Fact]
    public void HalfVolume_HalvesSamples()
    {
        var input = new float[] { 1.0f, -1.0f, 0.6f, -0.4f };
        var output = new float[4];

        VolumeProcessor.Apply(input, output, 4, volume: 0.5f);

        Assert.Equal(new float[] { 0.5f, -0.5f, 0.3f, -0.2f }, output);
    }

    [Fact]
    public void ZeroVolume_AllSilence()
    {
        var input = new float[] { 1.0f, -1.0f, 0.5f };
        var output = new float[3];

        VolumeProcessor.Apply(input, output, 3, volume: 0.0f);

        Assert.All(output, s => Assert.Equal(0.0f, s));
    }
}
