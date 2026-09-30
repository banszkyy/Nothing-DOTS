using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

public struct VirtualPlayer : IComponentData
{
    public int Index;
}
