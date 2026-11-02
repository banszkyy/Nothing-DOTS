using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public struct Vehicle : IComponentData
{
    public float MaxSpeed;
    public float Acceleration;
    public float Steering;

    [GhostField(Quantization = 100)] public float Speed;
    [GhostField(Quantization = 100)] public float2 Input;
}
