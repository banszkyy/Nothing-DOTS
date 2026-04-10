using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public struct BufferedLine : IBufferElementData
{
    public float3x2 Position;
    public byte Color;
    public float DieAt;

    public BufferedLine(float3x2 value, byte color, float dieAt)
    {
        Position = value;
        Color = color;
        DieAt = dieAt;
    }
}
