using Unity.Burst;
using Unity.Collections;
using Unity.NetCode;

[BurstCompile]
public struct ChatMessageNotificationRpc : IRpcCommand
{
    public required int Sender;
    public required FixedString64Bytes Message;
    public required long Time;
}
