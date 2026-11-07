// typeof(object).Assembly is System.Private.CoreLib: its assembly attributes are the framework's own (strings, numbers,
// enums, repeated and ambiguous ones), as Roslyn's PDB writer reads them (EmbedCompilationOptions).
using System;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

class P
{
    static void Show<T>(Func<T?> read) where T : class
    {
        try { Console.WriteLine(typeof(T).Name + ": " + (read() is { } value ? "present" : "absent")); }
        catch (Exception e) { Console.WriteLine(typeof(T).Name + ": " + e.GetType().Name); }
    }

    static void Main()
    {
        var assembly = typeof(object).Assembly;
        Console.WriteLine(assembly.GetName().Name);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration);
        var framework = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        Console.WriteLine(framework?.FrameworkName + " / " + framework?.FrameworkDisplayName);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.StartsWith("11.0.") ?? false);
        Console.WriteLine(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version?.StartsWith("11.0.") ?? false);
        Console.WriteLine(assembly.GetCustomAttribute<CompilationRelaxationsAttribute>()?.CompilationRelaxations);
        Console.WriteLine(assembly.GetCustomAttribute<RuntimeCompatibilityAttribute>()?.WrapNonExceptionThrows);
        Console.WriteLine(assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>()?.CultureName);
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Select(m => m.Key + "=" + m.Value).Order().ToArray();
        Console.WriteLine(string.Join(";", metadata.Where(m => !m.StartsWith("RepositoryUrl"))));
        Show(() => assembly.GetCustomAttribute<AssemblyMetadataAttribute>());
        Show(() => assembly.GetCustomAttribute<AssemblyCultureAttribute>());
        Console.WriteLine(assembly.GetCustomAttributes<AssemblyCultureAttribute>().Count());
        Console.WriteLine(typeof(string).Assembly == assembly);
    }
}
