using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using BinaryReader = Unity.Entities.Serialization.BinaryReader;
using BinaryWriter = Unity.Entities.Serialization.BinaryWriter;

static unsafe class FixedListExtensions
{
    public static Span<T> AsSpan<T>(ref this FixedList32Bytes<T> list) where T : unmanaged => new(list.GetUnsafePtr(), list.Length);
    public static Span<T> AsSpan<T>(ref this FixedList64Bytes<T> list) where T : unmanaged => new(list.GetUnsafePtr(), list.Length);
    public static Span<T> AsSpan<T>(ref this FixedList128Bytes<T> list) where T : unmanaged => new(list.GetUnsafePtr(), list.Length);
    public static Span<T> AsSpan<T>(ref this FixedList512Bytes<T> list) where T : unmanaged => new(list.GetUnsafePtr(), list.Length);
    public static Span<T> AsSpan<T>(ref this FixedList4096Bytes<T> list) where T : unmanaged => new(list.GetUnsafePtr(), list.Length);

    public static T* GetUnsafePtr<T>(ref this FixedList32Bytes<T> list) where T : unmanaged => (T*)((byte*)Unsafe.AsPointer(ref list) + UnsafeUtility.SizeOf<ushort>());
    public static T* GetUnsafePtr<T>(ref this FixedList64Bytes<T> list) where T : unmanaged => (T*)((byte*)Unsafe.AsPointer(ref list) + UnsafeUtility.SizeOf<ushort>());
    public static T* GetUnsafePtr<T>(ref this FixedList128Bytes<T> list) where T : unmanaged => (T*)((byte*)Unsafe.AsPointer(ref list) + UnsafeUtility.SizeOf<ushort>());
    public static T* GetUnsafePtr<T>(ref this FixedList512Bytes<T> list) where T : unmanaged => (T*)((byte*)Unsafe.AsPointer(ref list) + UnsafeUtility.SizeOf<ushort>());
    public static T* GetUnsafePtr<T>(ref this FixedList4096Bytes<T> list) where T : unmanaged => (T*)((byte*)Unsafe.AsPointer(ref list) + UnsafeUtility.SizeOf<ushort>());
}

static unsafe partial class FixedStringExtensions
{
    public static Span<byte> AsSpan(ref this FixedString32Bytes list) => new(list.GetUnsafePtr(), list.Length);
    public static Span<byte> AsSpan(ref this FixedString64Bytes list) => new(list.GetUnsafePtr(), list.Length);
    public static Span<byte> AsSpan(ref this FixedString128Bytes list) => new(list.GetUnsafePtr(), list.Length);
    public static Span<byte> AsSpan(ref this FixedString512Bytes list) => new(list.GetUnsafePtr(), list.Length);
    public static Span<byte> AsSpan(ref this FixedString4096Bytes list) => new(list.GetUnsafePtr(), list.Length);
}

static unsafe class BinaryWriterExtensions
{
    public static void Write(this BinaryWriter writer, Guid value) => writer.Write(value.ToByteArray());
    public static void WriteUnsafe<T>(this BinaryWriter writer, T value) where T : unmanaged => writer.WriteBytes(Unsafe.AsPointer(ref value), UnsafeUtility.SizeOf<T>());
    public static void Write(this BinaryWriter writer, long value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, ulong value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, int value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, uint value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, short value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, ushort value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, char value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, sbyte value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, byte value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, bool value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, float value) => writer.WriteUnsafe(value);
    public static void Write(this BinaryWriter writer, float2 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
    }
    public static void Write(this BinaryWriter writer, float3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }
    public static void Write(this BinaryWriter writer, float4 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }
    public static void Write(this BinaryWriter writer, quaternion value) => writer.Write(value.value);
    public static void Write(this BinaryWriter writer, Entity entity)
    {
        writer.Write(entity.Index);
        writer.Write(entity.Version);
    }

    [SuppressMessage("Style", "IDE0010:Add missing cases")]
    public static void Write<T>(this BinaryWriter writer, T value) where T : unmanaged, Enum
    {
        switch (value.GetTypeCode())
        {
            case TypeCode.Boolean:
                writer.WriteBytes(Unsafe.AsPointer(ref value), UnsafeUtility.SizeOf<bool>());
                break;
            case TypeCode.Byte:
                writer.WriteBytes(Unsafe.AsPointer(ref value), UnsafeUtility.SizeOf<byte>());
                break;
            case TypeCode.Char:
                writer.WriteBytes(Unsafe.AsPointer(ref value), UnsafeUtility.SizeOf<char>());
                break;
            case TypeCode.Int32:
                writer.WriteBytes(Unsafe.AsPointer(ref value), UnsafeUtility.SizeOf<int>());
                break;
            default:
                throw new NotImplementedException(value.GetTypeCode().ToString());
        }
    }

    public static void WriteUnsafe<T>(this BinaryWriter writer, FixedList32Bytes<T> value) where T : unmanaged
    {
        writer.Write((byte)value.Length);
        writer.WriteBytes(value.GetUnsafePtr(), value.Length * UnsafeUtility.SizeOf<T>());
    }
    public static void WriteUnsafe<T>(this BinaryWriter writer, FixedList64Bytes<T> value) where T : unmanaged
    {
        writer.Write((byte)value.Length);
        writer.WriteBytes(value.GetUnsafePtr(), value.Length * UnsafeUtility.SizeOf<T>());
    }
    public static void WriteUnsafe<T>(this BinaryWriter writer, FixedList128Bytes<T> value) where T : unmanaged
    {
        writer.Write((byte)value.Length);
        writer.WriteBytes(value.GetUnsafePtr(), value.Length * UnsafeUtility.SizeOf<T>());
    }
    public static void WriteUnsafe<T>(this BinaryWriter writer, FixedList512Bytes<T> value) where T : unmanaged
    {
        writer.Write((ushort)value.Length);
        writer.WriteBytes(value.GetUnsafePtr(), value.Length * UnsafeUtility.SizeOf<T>());
    }
    public static void WriteUnsafe<T>(this BinaryWriter writer, FixedList4096Bytes<T> value) where T : unmanaged
    {
        writer.Write((ushort)value.Length);
        writer.WriteBytes(value.GetUnsafePtr(), value.Length * UnsafeUtility.SizeOf<T>());
    }

    public static void Write(this BinaryWriter writer, FixedString32Bytes value) => writer.WriteUnsafe(value.AsFixedList());
    public static void Write(this BinaryWriter writer, FixedString64Bytes value) => writer.WriteUnsafe(value.AsFixedList());
    public static void Write(this BinaryWriter writer, FixedString128Bytes value) => writer.WriteUnsafe(value.AsFixedList());
    public static void Write(this BinaryWriter writer, FixedString512Bytes value) => writer.WriteUnsafe(value.AsFixedList());
    public static void Write(this BinaryWriter writer, FixedString4096Bytes value) => writer.WriteUnsafe(value.AsFixedList());

    public delegate void ItemSerializer<T>(BinaryWriter writer, T item);

    public static void Write<T>(this BinaryWriter writer, DynamicBuffer<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write(values.Length);

        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values.ElementAt(i));
        }
    }
    public static void Write<T>(this BinaryWriter writer, IReadOnlyCollection<T> values, ItemSerializer<T> serializer)
    {
        writer.Write(values.Count);
        foreach (T item in values)
        {
            serializer(writer, item);
        }
    }
    public static void Write<T>(this BinaryWriter writer, UnsafeList<T>.ReadOnly values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values.Ptr[i]);
        }
    }
    public static void Write<T>(this BinaryWriter writer, UnsafeList<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values.Ptr[i]);
        }
    }
    public static void Write<T>(this BinaryWriter writer, ReadOnlySpan<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values[i]);
        }
    }
    public static void Write<T>(this BinaryWriter writer, FixedList32Bytes<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write((byte)values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values[i]);
        }
    }
    public static void Write<T>(this BinaryWriter writer, FixedList64Bytes<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write((byte)values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values[i]);
        }
    }
    public static void Write<T>(this BinaryWriter writer, FixedList128Bytes<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write((byte)values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values[i]);
        }
    }
    public static void Write<T>(this BinaryWriter writer, FixedList512Bytes<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write((ushort)values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values[i]);
        }
    }
    public static void Write<T>(this BinaryWriter writer, FixedList4096Bytes<T> values, ItemSerializer<T> serializer) where T : unmanaged
    {
        writer.Write((ushort)values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            serializer(writer, values[i]);
        }
    }
}

static unsafe class BinaryReaderExtensions
{
    public static byte[] ReadBytes(this BinaryReader reader, int length)
    {
        byte[] res = new byte[length];
        fixed (byte* ptr = res)
        {
            reader.ReadBytes(ptr, length);
        }
        return res;
    }

    public static void ReadBytes(this BinaryReader reader, Span<byte> result)
    {
        fixed (byte* ptr = result)
        {
            reader.ReadBytes(ptr, result.Length);
        }
    }

    public static Guid ReadGuid(this BinaryReader reader)
    {
        byte* buffer = stackalloc byte[16];
        reader.ReadBytes(buffer, 16);
        return new(new ReadOnlySpan<byte>(buffer, 16));
    }

    public static T ReadUnsafe<T>(this BinaryReader reader) where T : unmanaged
    {
        T res;
        reader.ReadBytes(&res, UnsafeUtility.SizeOf<T>());
        return res;
    }
    public static long ReadLong(this BinaryReader reader) => reader.ReadUnsafe<long>();
    public static ulong ReadUlong(this BinaryReader reader) => reader.ReadUnsafe<ulong>();
    public static int ReadInt(this BinaryReader reader) => reader.ReadUnsafe<int>();
    public static uint ReadUint(this BinaryReader reader) => reader.ReadUnsafe<uint>();
    public static short ReadShort(this BinaryReader reader) => reader.ReadUnsafe<short>();
    public static ushort ReadUshort(this BinaryReader reader) => reader.ReadUnsafe<ushort>();
    public static char ReadChar(this BinaryReader reader) => reader.ReadUnsafe<char>();
    public static sbyte ReadSbyte(this BinaryReader reader) => reader.ReadUnsafe<sbyte>();
    public static byte ReadByte(this BinaryReader reader) => reader.ReadUnsafe<byte>();
    public static bool ReadBool(this BinaryReader reader) => reader.ReadUnsafe<bool>();
    public static float ReadFloat(this BinaryReader reader) => reader.ReadUnsafe<float>();
    public static float2 ReadFloat2(this BinaryReader reader) => new(reader.ReadFloat(), reader.ReadFloat());
    public static float3 ReadFloat3(this BinaryReader reader) => new(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
    public static float4 ReadFloat4(this BinaryReader reader) => new(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
    public static quaternion ReadQuaternion(this BinaryReader reader) => new(reader.ReadFloat4());
    public static Entity ReadEntityUnsafe(this BinaryReader reader)
    {
        return new Entity()
        {
            Index = reader.ReadInt(),
            Version = reader.ReadInt(),
        };
    }
    public static Entity ReadEntity(this BinaryReader reader, IReadOnlyDictionary<Entity, Entity> serializedEntities)
    {
        Entity serializedEntity = reader.ReadEntityUnsafe();
        if (!serializedEntities.TryGetValue(serializedEntity, out Entity entity))
        {
            Debug.LogError($"Invalid serialized entity `{serializedEntity}`");
            return Entity.Null;
        }
        return entity;
    }

    public static FixedList32Bytes<T> ReadFixedList32Unsafe<T>(this BinaryReader reader) where T : unmanaged
    {
        FixedList32Bytes<T> res = new() { Length = reader.ReadByte() };
        reader.ReadBytes(res.GetUnsafePtr(), res.Length * UnsafeUtility.SizeOf<T>());
        return res;
    }
    public static FixedList64Bytes<T> ReadFixedList64Unsafe<T>(this BinaryReader reader) where T : unmanaged
    {
        FixedList64Bytes<T> res = new() { Length = reader.ReadByte() };
        reader.ReadBytes(res.GetUnsafePtr(), res.Length * UnsafeUtility.SizeOf<T>());
        return res;
    }
    public static FixedList128Bytes<T> ReadFixedList128Unsafe<T>(this BinaryReader reader) where T : unmanaged
    {
        FixedList128Bytes<T> res = new() { Length = reader.ReadByte() };
        reader.ReadBytes(res.GetUnsafePtr(), res.Length * UnsafeUtility.SizeOf<T>());
        return res;
    }
    public static FixedList512Bytes<T> ReadFixedList512Unsafe<T>(this BinaryReader reader) where T : unmanaged
    {
        FixedList512Bytes<T> res = new() { Length = reader.ReadUshort() };
        reader.ReadBytes(res.GetUnsafePtr(), res.Length * UnsafeUtility.SizeOf<T>());
        return res;
    }
    public static FixedList4096Bytes<T> ReadFixedList4096Unsafe<T>(this BinaryReader reader) where T : unmanaged
    {
        FixedList4096Bytes<T> res = new() { Length = reader.ReadUshort() };
        reader.ReadBytes(res.GetUnsafePtr(), res.Length * UnsafeUtility.SizeOf<T>());
        return res;
    }

    public static FixedString32Bytes ReadFixedString32(this BinaryReader reader)
    {
        FixedList32Bytes<byte> data = reader.ReadFixedList32Unsafe<byte>();
        FixedString32Bytes res = new() { Length = data.Length };
        data.AsSpan().CopyTo(res.AsSpan());
        return res;
    }
    public static FixedString64Bytes ReadFixedString64(this BinaryReader reader)
    {
        FixedList64Bytes<byte> data = reader.ReadFixedList64Unsafe<byte>();
        FixedString64Bytes res = new() { Length = data.Length };
        data.AsSpan().CopyTo(res.AsSpan());
        return res;
    }
    public static FixedString128Bytes ReadFixedString128(this BinaryReader reader)
    {
        FixedList128Bytes<byte> data = reader.ReadFixedList128Unsafe<byte>();
        FixedString128Bytes res = new() { Length = data.Length };
        data.AsSpan().CopyTo(res.AsSpan());
        return res;
    }
    public static FixedString512Bytes ReadFixedString512(this BinaryReader reader)
    {
        FixedList512Bytes<byte> data = reader.ReadFixedList512Unsafe<byte>();
        FixedString512Bytes res = new() { Length = data.Length };
        data.AsSpan().CopyTo(res.AsSpan());
        return res;
    }
    public static FixedString4096Bytes ReadFixedString4096(this BinaryReader reader)
    {
        FixedList4096Bytes<byte> data = reader.ReadFixedList4096Unsafe<byte>();
        FixedString4096Bytes res = new() { Length = data.Length };
        data.AsSpan().CopyTo(res.AsSpan());
        return res;
    }

    public delegate T ItemDeserializer<T>(BinaryReader reader);
    public delegate void ItemDeserializerRef<T>(BinaryReader reader, ref T item);

    public static void ReadDynamicBuffer<T>(this BinaryReader reader, DynamicBuffer<T> buffer, ItemDeserializer<T> deserializer) where T : unmanaged
    {
        int length = reader.ReadInt();
        buffer.Length = length;

        for (int i = 0; i < length; i++)
        {
            buffer.ElementAt(i) = deserializer(reader);
        }
    }

    public static void ReadDynamicBuffer<T>(this BinaryReader reader, DynamicBuffer<T> buffer, ItemDeserializerRef<T> deserializer) where T : unmanaged
    {
        int length = reader.ReadInt();
        if (buffer.Length != length) Debug.LogWarning($"Dynamic buffer size changed");

        for (int i = 0; i < length; i++)
        {
            deserializer(reader, ref buffer.ElementAt(i));
        }
    }

    public static T[] ReadArray<T>(this BinaryReader reader, ItemDeserializer<T> deserializer)
    {
        T[] result = new T[reader.ReadInt()];
        for (int i = 0; i < result.Length; i++) result[i] = deserializer(reader);
        return result;
    }

    public static FixedList32Bytes<T> ReadFixedList32<T>(this BinaryReader reader, ItemDeserializer<T> deserializer) where T : unmanaged
    {
        FixedList32Bytes<T> res = new() { Length = reader.ReadByte() };
        for (int i = 0; i < res.Length; i++)
        {
            res[i] = deserializer(reader);
        }
        return res;
    }
    public static FixedList64Bytes<T> ReadFixedList64<T>(this BinaryReader reader, ItemDeserializer<T> deserializer) where T : unmanaged
    {
        FixedList64Bytes<T> res = new() { Length = reader.ReadByte() };
        for (int i = 0; i < res.Length; i++)
        {
            res[i] = deserializer(reader);
        }
        return res;
    }
    public static FixedList128Bytes<T> ReadFixedList128<T>(this BinaryReader reader, ItemDeserializer<T> deserializer) where T : unmanaged
    {
        FixedList128Bytes<T> res = new() { Length = reader.ReadByte() };
        for (int i = 0; i < res.Length; i++)
        {
            res[i] = deserializer(reader);
        }
        return res;
    }
    public static FixedList512Bytes<T> ReadFixedList512<T>(this BinaryReader reader, ItemDeserializer<T> deserializer) where T : unmanaged
    {
        FixedList512Bytes<T> res = new() { Length = reader.ReadUshort() };
        for (int i = 0; i < res.Length; i++)
        {
            res[i] = deserializer(reader);
        }
        return res;
    }
    public static FixedList4096Bytes<T> ReadFixedList4096<T>(this BinaryReader reader, ItemDeserializer<T> deserializer) where T : unmanaged
    {
        FixedList4096Bytes<T> res = new() { Length = reader.ReadUshort() };
        for (int i = 0; i < res.Length; i++)
        {
            res[i] = deserializer(reader);
        }
        return res;
    }
}
