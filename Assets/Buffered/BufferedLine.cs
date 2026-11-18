using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public struct BufferedLine : IBufferElementData
{
    public float3x2 Position;
    public byte Color;
    public float Age;

    public BufferedLine(float3x2 value, byte color, float age)
    {
        Position = value;
        Color = color;
        Age = age;
    }
}
