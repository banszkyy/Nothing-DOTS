using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

[BurstCompile]
public struct PlaceUnitRequestRpc : IRpcCommand
{
    public required float3 Position;
    public required FixedString32Bytes UnitName;
}
