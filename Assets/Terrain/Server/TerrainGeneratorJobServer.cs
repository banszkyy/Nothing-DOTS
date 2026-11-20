using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

[BurstCompile]
partial struct TerrainGeneratorJobServer : IJobFor
{
    [ReadOnly] public NativeArray<int2>.ReadOnly Queue;
    [NativeDisableContainerSafetyRestriction] public NativeArray<NativeArray<float>>.ReadOnly Result;
    public NativeHeightMapSettings HeightMapSettings;

    [BurstCompile]
    public void Execute(int index)
    {
        int2 coord = Queue[index];
        float2 noiseOffset = new float2(coord.x, coord.y) * TerrainSystemServer.MeshWorldSize / TerrainSystemServer.meshScale;
        //Debug.Log($"Generating chunk at {coord.x} {coord.y}");
        NativeArray<float> chunk = Result[index];
        HeightMapGenerator.GenerateHeightMap(ref chunk, TerrainSystemServer.NumVertsPerLine, TerrainSystemServer.NumVertsPerLine, HeightMapSettings.heightMultiplier, noiseOffset, in HeightMapSettings.noiseSettings, Allocator.Temp);
    }
}
