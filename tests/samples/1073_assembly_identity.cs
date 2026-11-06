// What Assembly.GetName() reports for the program's own assembly (an unsigned assembly has an empty public key and so
// the PublicKey flag), the assemblies it references as csc writes them (GetReferencedAssemblies, in metadata
// order), the program's [assembly: ...] attributes, and the entry point's signature for each kind of Main.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

[assembly: AssemblyMetadata("Channel", "stable")]
[assembly: AssemblyMetadata("Build", "42")]

class Marker { }

static class Program
{
    static void Main(string[] args)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetName();
        Console.WriteLine($"{name.Name} {name.Version} [{name.CultureName}] {name.CultureInfo?.Name}");
        Console.WriteLine($"key={name.GetPublicKey()?.Length} token={name.GetPublicKeyToken()?.Length} flags={name.Flags}");
        Console.WriteLine(name.FullName == assembly.FullName);
        Console.WriteLine(name.ToString() == assembly.ToString());
        Console.WriteLine(AssemblyName.ReferenceMatchesDefinition(name, assembly.GetName()));
        Console.WriteLine(new AssemblyName(assembly.FullName!).GetPublicKey() is null);
        Console.WriteLine(assembly.GetName() != assembly.GetName());

        Console.WriteLine("-- referenced assemblies");
        foreach (var reference in assembly.GetReferencedAssemblies())
            Console.WriteLine($"{reference.Name} {reference.Version} {reference.FullName.Substring(reference.FullName.IndexOf("PublicKeyToken", StringComparison.Ordinal))}");
        Console.WriteLine(assembly.GetReferencedAssemblies() != assembly.GetReferencedAssemblies());

        Console.WriteLine("-- attributes");
        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            Console.WriteLine($"{metadata.Key}={metadata.Value}");

        // csc's anonymous types are generic types csharpc can't list (GetTypes says so); the public ones are all there.
        var anonymous = new { Id = 1 };
        Console.WriteLine($"{anonymous.Id} {assembly.GetExportedTypes().Length} {assembly.ExportedTypes.Count()}");

        Console.WriteLine("-- entry point");
        var entry = assembly.EntryPoint!;
        Console.WriteLine($"{entry.DeclaringType!.Name}.{entry.Name}({string.Join(", ", entry.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}) -> {entry.ReturnType.Name} static={entry.IsStatic} public={entry.IsPublic}");
        Console.WriteLine(entry == typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static));
        Console.WriteLine(typeof(Marker).Assembly.EntryPoint == entry);
        Console.WriteLine(typeof(int).Assembly.EntryPoint is null);
        Console.WriteLine(Assembly.GetEntryAssembly() == assembly);
    }
}
