using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

[BurstCompile]
public struct PlaceBuildingRequestRpc : IRpcCommand
{
    public required float3 Position;
    public required FixedString32Bytes BuildingName;
    public required FixedString64Bytes Source;
}
