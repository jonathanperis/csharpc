// TypeDescriptor.GetConverter + TypeConverter string conversion and Activator.CreateInstance(Type)
// — the Binder surface for ConfigurationIgnore, without System.ComponentModel.TypeConverter.dll.
#nullable disable
#pragma warning disable IL2067, IL2072
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

sealed class Widget
{
    public string Name = "default";
    public Widget() { Name = "constructed"; }
}

class Program
{
    static void Main()
    {
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(string));
        Console.WriteLine(converter.CanConvertFrom(typeof(string)));
        Console.WriteLine(converter.CanConvertFrom(typeof(int)));
        Console.WriteLine((string)converter.ConvertFromInvariantString("hello"));

        Widget w = (Widget)Activator.CreateInstance(Keep<Widget>());
        Console.WriteLine(w.Name);

        // Binder's FindOpenGenericInterface walks GetInterfaces(); string has none described as open generics.
        Console.WriteLine(typeof(string).GetInterfaces().Length >= 0);

        try { Activator.CreateInstance((Type)null); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
    }

    static Type Keep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>() => typeof(T);
}
