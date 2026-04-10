using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

struct Connector : IComponentData
{
    public FixedList64Bytes<float3> PortPositions;
}
