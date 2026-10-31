// Null checks on an unconstrained type parameter instantiated with Nullable<T>: a null int? is null
// for `?.`, `??`, `== null`, `is null` and `is { }`, exactly as for a reference type.
Check<int?>(null);
Check<int?>(0);
Check<int?>(42);
Check<double?>(null);
Check<string?>(null);
Check<string?>("text");
Check<int>(0);

static void Check<T>(T value)
{
    string text = value?.ToString() ?? "null";
    object boxed = (object?)value ?? "none";
    bool equalsNull = value == null;
    bool isNull = value is null;
    bool isSomething = value is { };
    bool hashed = (value?.GetHashCode()).HasValue;
    Console.WriteLine($"{typeof(T).Name}: [{text}] {boxed} {equalsNull} {isNull} {isSomething} {hashed}");
}
