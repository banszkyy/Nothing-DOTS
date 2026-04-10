using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public struct BufferedWorldLabel : IBufferElementData
{
    public float3 Position;
    public byte Color;
    public FixedString32Bytes Text;
    public float DieAt;

    public BufferedWorldLabel(float3 value, byte color, FixedString32Bytes text, float dieAt)
    {
        Position = value;
        Color = color;
        Text = text;
        DieAt = dieAt;
    }
}
