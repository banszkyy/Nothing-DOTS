using System;

public static class RandomManaged
{
    public static Random Shared = new();

    public static float Float(this Random random) => (float)random.NextDouble();
    public static float Float(this Random random, float max) => (float)random.NextDouble() * max;
    public static float Float(this Random random, float min, float max) => ((float)random.NextDouble() * (max - min)) + min;
    public static Unity.Mathematics.float2 Rotation(this Random random) => new(MathF.Cos(random.Float(0, MathF.PI * 2)), MathF.Sin(random.Float(0, MathF.PI * 2)));
}
