using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
public readonly struct BufferedLogPiece : IBufferElementData
{
    public readonly byte Value;

    public BufferedLogPiece(byte value)
    {
        Value = value;
    }

    public static implicit operator BufferedLogPiece(byte v) => new(v);
    public static implicit operator BufferedLogPiece(LogPieceType v) => new((byte)v);
}

public enum LogPieceType : byte
{
    Unknown0,
    CombatTurret_Shoot,
    Command,
    Radar,
    Transmission_WiredOut,
    Transmission_WiredIn,
    Transmission_WirelessOut,
    Transmission_WirelessIn,
    Unknown1,
}

public readonly struct LogPieceHeader
{
    public readonly LogPieceType Type;
    public readonly long Timestamp;
    public readonly uint Index;

    public LogPieceHeader(LogPieceType type, long timestamp, uint index)
    {
        Type = type;
        Timestamp = timestamp;
        Index = index;
    }

    public static LogPieceHeader Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceType type = (LogPieceType)buffer[index++];
        long timestamp = buffer.ReadUnsafe<long>(ref index);
        uint _index = buffer.ReadUnsafe<uint>(ref index);

        //DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (type is <= LogPieceType.Unknown0 or >= LogPieceType.Unknown1) throw new Exception($"Invalid log piece header type {(int)type}");

        return new LogPieceHeader(type, timestamp, _index);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        buffer.Add((byte)Type);
        buffer.WriteUnsafe(Timestamp);
        buffer.WriteUnsafe(Index);
    }
}

public readonly struct UnitLog_CombatTurret_Shoot
{
    public readonly LogPieceHeader Header;

    public UnitLog_CombatTurret_Shoot(long timestamp, uint index)
    {
        Header = new(LogPieceType.CombatTurret_Shoot, timestamp, index);
    }

    public static UnitLog_CombatTurret_Shoot Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceHeader header = LogPieceHeader.Read(buffer, ref index);

        return new UnitLog_CombatTurret_Shoot(header.Timestamp, header.Index);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        Header.Write(buffer);
    }
}

public readonly struct UnitLog_Command
{
    public readonly LogPieceHeader Header;
    public readonly int CommandId;
    public readonly FixedList32Bytes<byte> Data;

    public UnitLog_Command(long timestamp, uint index, int commandId, FixedList32Bytes<byte> data)
    {
        Header = new(LogPieceType.Command, timestamp, index);
        CommandId = commandId;
        Data = data;
    }

    public static UnitLog_Command Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceHeader header = LogPieceHeader.Read(buffer, ref index);

        int commandId = buffer.ReadUnsafe<int>(ref index);
        FixedList32Bytes<byte> data = buffer.ReadFixedList32Unsafe<byte>(ref index);

        return new UnitLog_Command(header.Timestamp, header.Index, commandId, data);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        Header.Write(buffer);

        buffer.WriteUnsafe(CommandId);
        buffer.WriteListUnsafe(Data);
    }
}

public readonly struct UnitLog_Radar
{
    public readonly LogPieceHeader Header;
    public readonly bool Success;
    public readonly RadarResponse RadarResponse;
    public readonly SpawnedGhost ContextualEntity;

    public UnitLog_Radar(long timestamp, uint index, bool success, RadarResponse radarResponse, SpawnedGhost contextualEntity)
    {
        Header = new(LogPieceType.Radar, timestamp, index);
        Success = success;
        RadarResponse = radarResponse;
        ContextualEntity = contextualEntity;
    }

    public static UnitLog_Radar Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceHeader header = LogPieceHeader.Read(buffer, ref index);

        bool success = buffer[index++] != 0;
        RadarResponse radarResponse = success ? buffer.ReadUnsafe<RadarResponse>(ref index) : default;
        SpawnedGhost contextualEntity = success ? buffer.ReadSpawnedGhost(ref index) : default;

        return new UnitLog_Radar(header.Timestamp, header.Index, success, radarResponse, contextualEntity);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        Header.Write(buffer);

        buffer.Add((byte)(Success ? 1 : 0));
        if (Success)
        {
            buffer.WriteUnsafe(RadarResponse);
            buffer.Write(ContextualEntity);
        }
    }
}

public readonly struct UnitLog_Transmission_WiredOut
{
    public readonly LogPieceHeader Header;
    public readonly OutgoingWiredUnitTransmissionMetadata Metadata;
    public readonly FixedList32Bytes<byte> Data;

    public UnitLog_Transmission_WiredOut(long timestamp, uint index, OutgoingWiredUnitTransmissionMetadata metadata, FixedList32Bytes<byte> data)
    {
        Header = new(LogPieceType.Transmission_WiredOut, timestamp, index);
        Metadata = metadata;
        Data = data;
    }

    public static UnitLog_Transmission_WiredOut Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceHeader header = LogPieceHeader.Read(buffer, ref index);

        OutgoingWiredUnitTransmissionMetadata metadata = buffer.ReadUnsafe<OutgoingWiredUnitTransmissionMetadata>(ref index);
        FixedList32Bytes<byte> data = buffer.ReadFixedList32Unsafe<byte>(ref index);

        return new UnitLog_Transmission_WiredOut(header.Timestamp, header.Index, metadata, data);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        Header.Write(buffer);

        buffer.WriteUnsafe(Metadata);
        buffer.WriteListUnsafe(Data);
    }
}

public readonly struct UnitLog_Transmission_WiredIn
{
    public readonly LogPieceHeader Header;
    public readonly IncomingWiredUnitTransmissionMetadata Metadata;
    public readonly FixedList32Bytes<byte> Data;
    public readonly SpawnedGhost ContextualEntity;

    public UnitLog_Transmission_WiredIn(long timestamp, uint index, IncomingWiredUnitTransmissionMetadata metadata, FixedList32Bytes<byte> data, SpawnedGhost contextualEntity)
    {
        Header = new(LogPieceType.Transmission_WiredIn, timestamp, index);
        Metadata = metadata;
        Data = data;
        ContextualEntity = contextualEntity;
    }

    public static UnitLog_Transmission_WiredIn Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceHeader header = LogPieceHeader.Read(buffer, ref index);

        IncomingWiredUnitTransmissionMetadata metadata = buffer.ReadUnsafe<IncomingWiredUnitTransmissionMetadata>(ref index);
        FixedList32Bytes<byte> data = buffer.ReadFixedList32Unsafe<byte>(ref index);
        SpawnedGhost contextualEntity = buffer.ReadSpawnedGhost(ref index);

        return new UnitLog_Transmission_WiredIn(header.Timestamp, header.Index, metadata, data, contextualEntity);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        Header.Write(buffer);

        buffer.WriteUnsafe(Metadata);
        buffer.WriteListUnsafe(Data);
        buffer.Write(ContextualEntity);
    }
}

public readonly struct UnitLog_Transmission_WirelessOut
{
    public readonly LogPieceHeader Header;
    public readonly OutgoingWirelessUnitTransmissionMetadata Metadata;
    public readonly FixedList32Bytes<byte> Data;

    public UnitLog_Transmission_WirelessOut(long timestamp, uint index, OutgoingWirelessUnitTransmissionMetadata metadata, FixedList32Bytes<byte> data)
    {
        Header = new(LogPieceType.Transmission_WirelessOut, timestamp, index);
        Metadata = metadata;
        Data = data;
    }

    public static UnitLog_Transmission_WirelessOut Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceHeader header = LogPieceHeader.Read(buffer, ref index);

        OutgoingWirelessUnitTransmissionMetadata metadata = buffer.ReadUnsafe<OutgoingWirelessUnitTransmissionMetadata>(ref index);
        FixedList32Bytes<byte> data = buffer.ReadFixedList32Unsafe<byte>(ref index);

        return new UnitLog_Transmission_WirelessOut(header.Timestamp, header.Index, metadata, data);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        Header.Write(buffer);

        buffer.WriteUnsafe(Metadata);
        buffer.WriteListUnsafe(Data);
    }
}

public readonly struct UnitLog_Transmission_WirelessIn
{
    public readonly LogPieceHeader Header;
    public readonly IncomingWirelessUnitTransmissionMetadata Metadata;
    public readonly FixedList32Bytes<byte> Data;
    public readonly SpawnedGhost ContextualEntity;

    public UnitLog_Transmission_WirelessIn(long timestamp, uint index, IncomingWirelessUnitTransmissionMetadata metadata, FixedList32Bytes<byte> data, SpawnedGhost contextualEntity)
    {
        Header = new(LogPieceType.Transmission_WirelessIn, timestamp, index);
        Metadata = metadata;
        Data = data;
        ContextualEntity = contextualEntity;
    }

    public static UnitLog_Transmission_WirelessIn Read(ReadOnlySpan<byte> buffer, ref int index)
    {
        LogPieceHeader header = LogPieceHeader.Read(buffer, ref index);

        IncomingWirelessUnitTransmissionMetadata metadata = buffer.ReadUnsafe<IncomingWirelessUnitTransmissionMetadata>(ref index);
        FixedList32Bytes<byte> data = buffer.ReadFixedList32Unsafe<byte>(ref index);
        SpawnedGhost contextualEntity = buffer.ReadSpawnedGhost(ref index);

        return new UnitLog_Transmission_WirelessIn(header.Timestamp, header.Index, metadata, data, contextualEntity);
    }

    public void Write(DynamicBuffer<BufferedLogPiece> buffer)
    {
        LogPieceExtensions.LimitSize(buffer);
        Header.Write(buffer);

        buffer.WriteUnsafe(Metadata);
        buffer.WriteListUnsafe(Data);
        buffer.Write(ContextualEntity);
    }
}

static class LogPieceExtensions
{
    const int MaxBufferSize = 1024;

    public static LogPieceHeader Read(DynamicBuffer<BufferedLogPiece> buffer, ref int offset) => Read(buffer.Reinterpret<byte>().AsNativeArray().AsReadOnlySpan(), ref offset);

    public static LogPieceHeader Read(ReadOnlySpan<byte> buffer, ref int offset) => (LogPieceType)buffer[offset] switch
    {
        LogPieceType.CombatTurret_Shoot => UnitLog_CombatTurret_Shoot.Read(buffer, ref offset).Header,
        LogPieceType.Command => UnitLog_Command.Read(buffer, ref offset).Header,
        LogPieceType.Radar => UnitLog_Radar.Read(buffer, ref offset).Header,
        LogPieceType.Transmission_WiredOut => UnitLog_Transmission_WiredOut.Read(buffer, ref offset).Header,
        LogPieceType.Transmission_WiredIn => UnitLog_Transmission_WiredIn.Read(buffer, ref offset).Header,
        LogPieceType.Transmission_WirelessOut => UnitLog_Transmission_WirelessOut.Read(buffer, ref offset).Header,
        LogPieceType.Transmission_WirelessIn => UnitLog_Transmission_WirelessIn.Read(buffer, ref offset).Header,
        LogPieceType.Unknown0 => throw new UnreachableException(),
        LogPieceType.Unknown1 => throw new UnreachableException(),
        _ => default,
    };

    public static void LimitSize(DynamicBuffer<BufferedLogPiece> buffer)
    {
        int removeSize = 0;
        while (buffer.Length - removeSize > MaxBufferSize)
        {
            Read(buffer, ref removeSize);
        }
        if (removeSize > 0) buffer.RemoveRange(0, removeSize);
    }

    public static unsafe void WriteListUnsafe<T>(this DynamicBuffer<BufferedLogPiece> buffer, FixedList32Bytes<T> list) where T : unmanaged => buffer.WriteListUnsafe(list.GetUnsafePtr(), list.Length);
    public static unsafe void WriteListUnsafe<T>(this DynamicBuffer<BufferedLogPiece> buffer, FixedList64Bytes<T> list) where T : unmanaged => buffer.WriteListUnsafe(list.GetUnsafePtr(), list.Length);
    public static unsafe void WriteListUnsafe<T>(this DynamicBuffer<BufferedLogPiece> buffer, FixedList128Bytes<T> list) where T : unmanaged => buffer.WriteListUnsafe(list.GetUnsafePtr(), list.Length);

    public static unsafe void WriteListUnsafe<T>(this DynamicBuffer<BufferedLogPiece> buffer, T* value, int count) where T : unmanaged
    {
        buffer.WriteUnsafe((ushort)count);
        for (int i = 0; i < count; i++)
        {
            buffer.WriteUnsafe(value[i]);
        }
    }

    public static FixedList32Bytes<T> ReadFixedList32Unsafe<T>(this ReadOnlySpan<byte> buffer, ref int index) where T : unmanaged
    {
        FixedList32Bytes<T> result = new();
        int length = buffer.ReadUnsafe<ushort>(ref index);
        for (int i = 0; i < length; i++)
        {
            result.Add(buffer.ReadUnsafe<T>(ref index));
        }
        return result;
    }

    public static FixedList64Bytes<T> ReadFixedList64Unsafe<T>(this ReadOnlySpan<byte> buffer, ref int index) where T : unmanaged
    {
        FixedList64Bytes<T> result = new();
        int length = buffer.ReadUnsafe<ushort>(ref index);
        for (int i = 0; i < length; i++)
        {
            result.Add(buffer.ReadUnsafe<T>(ref index));
        }
        return result;
    }

    public static FixedList128Bytes<T> ReadFixedList128Unsafe<T>(this ReadOnlySpan<byte> buffer, ref int index) where T : unmanaged
    {
        FixedList128Bytes<T> result = new();
        int length = buffer.ReadUnsafe<ushort>(ref index);
        for (int i = 0; i < length; i++)
        {
            result.Add(buffer.ReadUnsafe<T>(ref index));
        }
        return result;
    }

    public static ReadOnlySpan<byte> ReadBytes(this ReadOnlySpan<byte> buffer, int count, ref int index)
    {
        ReadOnlySpan<byte> result = buffer.Slice(index, count);
        index += count;
        return result;
    }

    public static SpawnedGhost ReadSpawnedGhost(this ReadOnlySpan<byte> buffer, ref int index)
    {
        int id = buffer.ReadUnsafe<int>(ref index);
        uint spawnTick = buffer.ReadUnsafe<uint>(ref index);
        return new SpawnedGhost(id, new NetworkTick(spawnTick));
    }

    public static void Write(this DynamicBuffer<BufferedLogPiece> buffer, SpawnedGhost value)
    {
        buffer.WriteUnsafe(value.ghostId);
        buffer.WriteUnsafe(value.spawnTick.SerializedData);
    }

    public static unsafe T ReadUnsafe<T>(this ReadOnlySpan<byte> buffer, ref int index) where T : unmanaged
    {
        fixed (byte* ptr = buffer[index..])
        {
            T result = *(T*)ptr;
            index += UnsafeUtility.SizeOf<T>();
            return result;
        }
    }

    public static unsafe void WriteUnsafe<T>(this DynamicBuffer<BufferedLogPiece> buffer, T value) where T : unmanaged
    {
        int length = UnsafeUtility.SizeOf<T>();
        byte* bytes = (byte*)&value;
        for (int i = 0; i < length; i++)
        {
            buffer.Add(bytes[i]);
        }
    }
}
