using Unity.Burst;
using Unity.Collections;
using Unity.NetCode;

[BurstCompile]
public struct ChatMessageRequestRpc : IRpcCommand
{
    public required FixedString64Bytes Message;
    public required long Time;
}
