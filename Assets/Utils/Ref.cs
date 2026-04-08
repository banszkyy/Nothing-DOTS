using System;

public class Ref<T> : IEquatable<Ref<T>> where T : notnull
{
    public T Value;

    public Ref(T value)
    {
        Value = value;
    }

    public override bool Equals(object? obj) => obj is Ref<T> other && Equals(other);
    public bool Equals(Ref<T> other) => Value.Equals(other.Value);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
}
