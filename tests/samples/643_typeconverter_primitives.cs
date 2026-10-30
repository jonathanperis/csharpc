// TypeDescriptor.GetConverter for the primitive converters Configuration.Binder relies on: values,
// trimming, hex prefixes, and the exception types/messages for bad input (a converter that silently
// can't convert made Binder leave int/bool/double settings at their defaults).
#nullable disable
#pragma warning disable IL2026, IL2067, IL2072
using System;
using System.ComponentModel;

class Program
{
    static void Show(Type type, string text)
    {
        TypeConverter c = TypeDescriptor.GetConverter(type);
        try
        {
            object v = c.ConvertFromInvariantString(text);
            Console.WriteLine($"{type.Name} [{text}] -> {(v is char ch ? ((int)ch).ToString() : v)} ({v.GetType().Name})");
        }
        catch (Exception e)
        {
            Console.WriteLine($"{type.Name} [{text}] !! {e.GetType().Name}: {e.Message} | {e.InnerException?.GetType().Name}: {e.InnerException?.Message}");
        }
    }

    static void Main()
    {
        foreach (Type t in new[] { typeof(int), typeof(bool), typeof(double), typeof(long), typeof(byte), typeof(char), typeof(TimeSpan), typeof(Guid), typeof(Version), typeof(decimal), typeof(float), typeof(sbyte), typeof(short), typeof(ushort), typeof(uint), typeof(ulong) })
            Console.WriteLine($"{t.Name} {TypeDescriptor.GetConverter(t).CanConvertFrom(typeof(string))} {TypeDescriptor.GetConverter(t).CanConvertFrom(typeof(int))}");

        Show(typeof(int), "8080");
        Show(typeof(int), "  -42 ");
        Show(typeof(int), "0x1F");
        Show(typeof(int), "#ff");
        Show(typeof(int), "&H10");
        Show(typeof(int), "abc");
        Show(typeof(int), "");
        Show(typeof(int), "99999999999");
        Show(typeof(long), "9000000000");
        Show(typeof(uint), "0xFFFFFFFF");
        Show(typeof(ulong), "18446744073709551615");
        Show(typeof(short), "-7");
        Show(typeof(ushort), "65535");
        Show(typeof(byte), "255");
        Show(typeof(byte), "256");
        Show(typeof(sbyte), "-128");
        Show(typeof(bool), "true");
        Show(typeof(bool), " False ");
        Show(typeof(bool), "yes");
        Show(typeof(double), "0.5");
        Show(typeof(double), "1e3");
        Show(typeof(double), "0x10");
        Show(typeof(float), "2.25");
        Show(typeof(decimal), "12.50");
        Show(typeof(char), "x");
        Show(typeof(char), " y ");
        Show(typeof(char), "");
        Show(typeof(char), "xy");
        Show(typeof(Guid), " 6f9619ff-8b86-d011-b42d-00c04fc964ff ");
        Show(typeof(TimeSpan), "01:02:03");
        Show(typeof(TimeSpan), " 1.02:00 ");
        Show(typeof(TimeSpan), "nope");
        Show(typeof(Version), "1.2.3");
        Show(typeof(Version), "x");
        Show(typeof(string), "kept");

        try { TypeDescriptor.GetConverter(typeof(int)).ConvertFrom(5); }
        catch (NotSupportedException e) { Console.WriteLine(e.Message); }
    }
}
