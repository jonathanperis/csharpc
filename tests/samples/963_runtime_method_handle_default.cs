// default(RuntimeMethodHandle) / default(RuntimeFieldHandle) as values: passed to another method (the shape
// of Microsoft.macOS's Runtime.GetNSObject<T>(nint), which forwards one for error messages), compared,
// stored in fields and boxed.
using System;

Console.WriteLine(Lookup.Get<string>(1));
Console.WriteLine(Lookup.Get<string>(0) ?? "null");

var method = default(RuntimeMethodHandle);
Console.WriteLine(method.Value);
Console.WriteLine(method.Equals(default(RuntimeMethodHandle)));
Console.WriteLine(method.Equals((object)default(RuntimeMethodHandle)));
Console.WriteLine(method.Equals("text"));
Console.WriteLine(method.Equals(null));
Console.WriteLine(method == default(RuntimeMethodHandle));
Console.WriteLine(method != default(RuntimeMethodHandle));
Console.WriteLine(((object)method).Equals(method));
Console.WriteLine(method.GetHashCode() == default(RuntimeMethodHandle).GetHashCode());
try { method.GetFunctionPointer(); } catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

var field = default(RuntimeFieldHandle);
Console.WriteLine(field.Value);
Console.WriteLine(field.Equals(default(RuntimeFieldHandle)));
Console.WriteLine(field == default(RuntimeFieldHandle));
Console.WriteLine(field != default(RuntimeFieldHandle));

var holder = new Holder();
Console.WriteLine(holder.Method == default(RuntimeMethodHandle));
Console.WriteLine(holder.Describe(default));

class Holder
{
    public RuntimeMethodHandle Method;
    public string Describe(RuntimeMethodHandle handle) => handle.Equals(Method) ? "same" : "different";
}

static class Lookup
{
    public static T? Get<T>(nint ptr) where T : class => Get<T>(ptr, default(RuntimeMethodHandle));

    static T? Get<T>(nint ptr, RuntimeMethodHandle method) where T : class
    {
        Console.WriteLine(method.Equals(default(RuntimeMethodHandle)) ? "no method" : "method");
        return ptr == 0 ? null : (T?)(object?)"found";
    }
}
