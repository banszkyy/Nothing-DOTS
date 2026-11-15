using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public struct RadarTrack : IBufferElementData     // present only if MaxTracks > 0
{
    public ushort Handle;
    public byte MissCount;  // coast while predicting; LOST after threshold
    public byte Quality;
    public float3 Pos;        // filtered position = gate center
    public float3 Vel;        // filtered velocity vector (α-β filter)
    public uint LastUpdateTick;
}
