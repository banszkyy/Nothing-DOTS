using System;
using System.Runtime.CompilerServices;
using Unity.Entities;

public static class DynamicBufferExtensions
{
    public static void Set<T>(this DynamicBuffer<T> buffer, int index, in T value) where T : unmanaged
    {
        buffer.AsNativeArray().AsSpan()[index] = value;
    }

    public static TSource FirstOrDefault<TSource, TClosure>(
        this DynamicBuffer<TSource> source,
        Func<TSource, TClosure, bool> predicate,
        TClosure closure)
        where TSource : unmanaged
    {
        for (int i = 0; i < source.Length; i++)
        {
            if (predicate.Invoke(source[i], closure))
            {
                return source[i];
            }
        }
        return default;
    }

    public static unsafe ref T RefOf<T>(this DynamicBuffer<T> buffer, int i) where T : unmanaged => ref Unsafe.AsRef<T>(&((T*)buffer.GetUnsafePtr())[i]);

    public static unsafe Span<T> AsSpan<T>(this DynamicBuffer<T> buffer) where T : unmanaged => new(buffer.GetUnsafePtr(), buffer.Length);
    public static unsafe ReadOnlySpan<T> AsReadOnlySpan<T>(this DynamicBuffer<T> buffer) where T : unmanaged => new(buffer.GetUnsafeReadOnlyPtr(), buffer.Length);

    public static unsafe Span<byte> AsSpan(this DynamicBuffer<ProcessorMemoryBuffer> buffer) => new(buffer.GetUnsafePtr(), buffer.Length);
    public static unsafe ReadOnlySpan<byte> AsReadOnlySpan(this DynamicBuffer<ProcessorMemoryBuffer> buffer) => new(buffer.GetUnsafeReadOnlyPtr(), buffer.Length);
}
