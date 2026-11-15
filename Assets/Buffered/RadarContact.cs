using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[InternalBufferCapacity(8)]
public struct RadarContact : IBufferElementData
{
    public float3 Pos;
    public float RadialVel;
    public double Time;
    public byte Strength;
    public byte Emission;
    public Entity Source;
}
