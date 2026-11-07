// RuntimeTypeHandle (Type.TypeHandle, GetTypeFromHandle, as a dictionary key, RunClassConstructor), ParameterModifier in the
// binder overloads of GetMethod/GetConstructor/GetProperty, Activator.CreateInstance<object>() and what builds on it.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

class WithCctor
{
    public static int Value = Init();
    static int Init() { Console.WriteLine("static constructor of WithCctor ran"); return 5; }
}

class Target
{
    public Target() { }
    public Target(int x) { }
    public void M(ref int a, int b) { }
    public void M(int a, int b) { }
    public int P { get; set; }
}

class Program
{
    static object s_lazy;
    static bool s_initialized;
    static object s_lock;

    static void Main()
    {
        RuntimeTypeHandle h = typeof(int).TypeHandle;
        Console.WriteLine(Type.GetTypeFromHandle(h));
        Console.WriteLine(h.Equals(typeof(int).TypeHandle));
        Console.WriteLine(h.Equals(typeof(string).TypeHandle));
        Console.WriteLine(h.Equals((object)typeof(int).TypeHandle));
        Console.WriteLine(h.Equals(null));
        Console.WriteLine(h.Equals((object)5));
        Console.WriteLine(h.GetHashCode() == typeof(int).TypeHandle.GetHashCode());
        Console.WriteLine(h.Value != IntPtr.Zero);
        Console.WriteLine(default(RuntimeTypeHandle).Value == IntPtr.Zero);
        Console.WriteLine(Type.GetTypeFromHandle(default) == null);
        Console.WriteLine(Type.GetTypeFromHandle(typeof(List<int>).TypeHandle));
        Console.WriteLine(Type.GetTypeHandle("x").Equals(typeof(string).TypeHandle));
        var names = new Dictionary<RuntimeTypeHandle, string>();
        names[typeof(int).TypeHandle] = "int";
        names[typeof(string).TypeHandle] = "string";
        names[typeof(int).TypeHandle] = "int again";
        Console.WriteLine(names.Count + " " + names[typeof(int).TypeHandle] + " " + names[typeof(string).TypeHandle] + " " + names.ContainsKey(typeof(long).TypeHandle));

        Console.WriteLine("before RunClassConstructor");
        RuntimeHelpers.RunClassConstructor(typeof(WithCctor).TypeHandle);
        Console.WriteLine("after RunClassConstructor");
        RuntimeHelpers.RunClassConstructor(typeof(WithCctor).TypeHandle);
        Console.WriteLine(WithCctor.Value);

        var modifier = new ParameterModifier(2);
        modifier[0] = true;
        Console.WriteLine(modifier[0] + " " + modifier[1]);
        try { new ParameterModifier(0); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { _ = modifier[5]; } catch (Exception e) { Console.WriteLine(e.GetType().Name); }
        Console.WriteLine(Array.Empty<ParameterModifier>().Length + " " + new ParameterModifier[3].Length);
        var flags = BindingFlags.Public | BindingFlags.Instance;
        Console.WriteLine(typeof(Target).GetMethod("M", flags, null, new[] { typeof(int), typeof(int) }, new[] { modifier }));
        Console.WriteLine(typeof(Target).GetMethod("M", flags, null, new[] { typeof(int), typeof(int) }, null));
        Console.WriteLine(typeof(Target).GetConstructor(flags, null, new[] { typeof(int) }, null));
        Console.WriteLine(typeof(Target).GetConstructor(flags, null, Type.EmptyTypes, new ParameterModifier[0]));
        Console.WriteLine(typeof(Target).GetProperty("P", flags, null, typeof(int), Type.EmptyTypes, null));

        object o = Activator.CreateInstance<object>();
        Console.WriteLine(o.GetType() + " " + (o != null));
        Console.WriteLine(!ReferenceEquals(o, Activator.CreateInstance<object>()));
        Console.WriteLine(Activator.CreateInstance(typeof(object)).GetType());
        var first = LazyInitializer.EnsureInitialized(ref s_lazy);
        var second = LazyInitializer.EnsureInitialized(ref s_lazy);
        Console.WriteLine(ReferenceEquals(first, second) + " " + first.GetType());
        var third = LazyInitializer.EnsureInitialized(ref s_lazy, ref s_initialized, ref s_lock);
        Console.WriteLine(ReferenceEquals(first, third) + " " + s_initialized);
        Console.WriteLine(Activator.CreateInstance<List<int>>().Count + " " + Activator.CreateInstance<int>() + " " + Activator.CreateInstance<DateTime>().Year);
    }
}
