// AssemblyName parsed from display names (.NET's grammar: quoting, escapes, attribute checks),
// FullName formatting, Assembly.Load of the program's own assembly, and IncrementalHash into a span.
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

class Program
{
    static void Show(string text)
    {
        try
        {
            var n = new AssemblyName(text);
            var token = n.GetPublicKeyToken();
            Console.WriteLine($"[{n.Name}] v={n.Version?.ToString() ?? "null"} culture={n.CultureName ?? "null"} token={(token == null ? "null" : Convert.ToHexString(token))} flags={n.Flags} | {n.FullName}");
        }
        catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        Show("Microsoft.AspNetCore");
        Show("System.Runtime, Version=11.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
        Show("  Spaced Name  , Version=1.2");
        Show("Lib, Version=1.2.3, Culture=Neutral, PublicKeyToken=null, Retargetable=Yes");
        Show("\"Quoted, Name\", Version=4.0.0.1");
        Show("Esc\\=aped\\,x");
        Show("Lib, Custom=ignored, ProcessorArchitecture=amd64");
        Show("Lib, Version=1");
        Show("Lib, Version=1.2.3.4.5");
        Show("Lib, Version=1.2, Version=1.3");
        Show("Lib, PublicKeyToken=abc");
        Show("Lib, Retargetable=Maybe");
        Show("Lib,");
        Show("'unterminated");
        Show(", Version=1.0");
        try { new AssemblyName(""); } catch (ArgumentException e) { Console.WriteLine(e.Message); }

        var built = new AssemblyName { Name = "Built", Version = new Version(2, 3, 4) };
        Console.WriteLine(built.FullName);
        built.SetPublicKeyToken(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Console.WriteLine(built + " " + AssemblyName.ReferenceMatchesDefinition(new AssemblyName("built"), built));

        var self = typeof(Program).Assembly;
        Console.WriteLine(Assembly.Load(new AssemblyName(self.GetName().Name!)) == self);
        Console.WriteLine(Assembly.Load("603_ASSEMBLY_NAME_LOAD") == self);
        Console.WriteLine(self.GetName().FullName == self.FullName);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("abc"u8);
        Span<byte> digest = stackalloc byte[40];
        Console.WriteLine(hash.GetHashAndReset(digest) + " " + Convert.ToHexString(digest.Slice(0, 32)));
        hash.AppendData("abc"u8);
        Console.WriteLine(hash.TryGetHashAndReset(digest.Slice(0, 10), out var written) + " " + written);
        try { hash.GetHashAndReset(new byte[3]); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
    }
}
