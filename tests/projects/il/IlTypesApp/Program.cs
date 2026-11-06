using System.Reflection;
using IlTypes;

// Assembly-level reflection over an assembly csharpc compiles from IL: its type list (GetTypes in metadata order,
// closures and state machines included, ExportedTypes), module, references, entry point, and the program's own.
var library = typeof(IPlugin).Assembly;
Console.WriteLine(library.FullName);
Console.WriteLine(library.GetName().GetPublicKey()!.Length + " " + library.GetName().GetPublicKeyToken()!.Length);
Console.WriteLine("-- GetTypes");
foreach (var type in library.GetTypes())
    Console.WriteLine($"{type.FullName} | {(type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class")} | " +
        $"{(type.IsPublic ? "public" : type.IsNotPublic ? "internal" : type.IsNestedPublic ? "nested-public" : type.IsNestedPrivate ? "nested-private" : type.IsNestedAssembly ? "nested-internal" : "nested-other")}" +
        $"{(type.IsAbstract ? " abstract" : "")}{(type.IsSealed ? " sealed" : "")}{(type.IsGenericTypeDefinition ? " generic" : "")} | " +
        $"{type.DeclaringType?.Name ?? "-"} | {type.BaseType?.Name ?? "-"}");
Console.WriteLine("-- ExportedTypes");
foreach (var type in library.ExportedTypes) Console.WriteLine(type.FullName);
Console.WriteLine(library.DefinedTypes.Count() == library.GetTypes().Length);
Console.WriteLine(library.GetType("IlTypes.Alpha") == typeof(Alpha));
Console.WriteLine(library.GetType("IlTypes.PluginBase+Settings")?.FullName);
Console.WriteLine(library.GetType("IlTypes.Hidden`1")?.FullName);
Console.WriteLine(library.GetType("IlTypes.Nope") is null);

Console.WriteLine("-- module, references, entry point");
var module = library.ManifestModule;
Console.WriteLine($"{module.Name} {module.ScopeName} {module.MetadataToken} {module.ModuleVersionId != Guid.Empty} {module.Assembly == library}");
Console.WriteLine(typeof(Alpha).Module == module);
Console.WriteLine(library.GetModules().Length);
Console.WriteLine(library.EntryPoint is null);
Console.WriteLine(string.Join(", ", library.GetReferencedAssemblies().Select(r => r.Name)));

var program = typeof(Program).Assembly;
Console.WriteLine(program.EntryPoint!.Name + " " + (program.EntryPoint.DeclaringType == typeof(Program)));
Console.WriteLine(string.Join(", ", program.GetReferencedAssemblies().Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal)));
Console.WriteLine(string.Join(", ", program.GetTypes().Select(t => t.FullName)));
Console.WriteLine(program.GetName().GetPublicKey()!.Length + " " + program.GetName().Flags);
Console.WriteLine(program.ManifestModule.MetadataToken + " " + (program.ManifestModule.Assembly == program));
