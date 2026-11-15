using Unity.Collections;

public readonly struct UnitCommandRequest
{
    public readonly int Id;
    public readonly FixedList32Bytes<byte> Data;

    public UnitCommandRequest(int id, FixedList32Bytes<byte> data)
    {
        Id = id;
        Data = data;
    }
}
