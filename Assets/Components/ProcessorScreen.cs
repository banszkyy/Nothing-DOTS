using Unity.Entities;
using Unity.Mathematics;

struct ProcessorScreen : IComponentData
{
    public float3 Position;
    public quaternion Rotation;
    public float2 Size;
    public float FontSize;
}
