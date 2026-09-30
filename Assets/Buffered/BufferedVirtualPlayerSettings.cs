using Unity.Collections;
using Unity.Entities;

public struct BufferedVirtualPlayerSettings : IBufferElementData
{
    public required FixedString32Bytes Nickname;
}
