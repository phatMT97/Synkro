namespace Synkro.Core;

public static class VolumeProcessor
{
    public static void Apply(float[] input, float[] output, int count, float volume)
    {
        for (int i = 0; i < count; i++)
        {
            output[i] = input[i] * volume;
        }
    }
}
