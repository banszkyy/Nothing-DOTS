using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;

public static class HeightMapGenerator
{
    static readonly ProfilerMarker _marker = new("Terrain.HeightMapGenerator");

    public static void GenerateHeightMap(ref NativeArray<float> values, int width, int height, float heightMultiplier, in NoiseGeneratorUnmanaged noiseGenerator)
    {
        using var _ = _marker.Auto();

        float v;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                v = noiseGenerator.Sample(x, y);
                v *= v * heightMultiplier;
                values[x + y * width] = v;
            }
        }
    }

    public static void GenerateHeightMap(ref NativeArray<float> values, int width, int height, float heightMultiplier, in float2 offset, in NoiseSettings noiseSettings, Allocator allocator)
    {
        if (heightMultiplier == 0f)
        {
            values.AsSpan().Clear();
            return;
        }
        NoiseGeneratorUnmanaged noiseGenerator = new(noiseSettings, offset, allocator: allocator);
        GenerateHeightMap(ref values, width, height, heightMultiplier, in noiseGenerator);
        noiseGenerator.Dispose();
    }
}
