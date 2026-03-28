namespace Synkro.Core;

public static class VolumeProcessor
{
    public static void Apply(float[] input, float[] output, int count, float volume)
    {
        if (volume <= 1.0f)
        {
            // Linear gain for 0-100%
            for (int i = 0; i < count; i++)
                output[i] = input[i] * volume;
        }
        else
        {
            // Tanh soft compression for >100%:
            // Quiet parts get boosted significantly, peaks stay at 1.0 (no clipping)
            // At 500%: input 0.1 → 0.46 (4.6x boost), input 0.5 → 0.99 (2x boost)
            float invTanhVol = 1.0f / MathF.Tanh(volume);
            for (int i = 0; i < count; i++)
                output[i] = MathF.Tanh(input[i] * volume) * invTanhVol;
        }
    }
}
