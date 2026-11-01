using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// [DynamicDependency] as ILLink and NativeAOT honour it: a member carrying one keeps the members it names
/// for reflection when the member itself is kept. RazorSlices' generated proxies rely on it
/// (`[DynamicDependency(All, "AspNetCoreGeneratedDocument.Templates_X, App", "App")] const string TypeName`,
/// then `Type.GetType(TypeName)` and reflection over the template's properties). csharpc applies the
/// attributes of a lowered method, and those of its type's fields once any method of the type is lowered
/// (a constant field is never "used": its value is inlined).
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly HashSet<INamedTypeSymbol> _dynamicDependencyTypes = new(SymbolEqualityComparer.Default);

    private void NoteDynamicDependencies(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        if (CoreLib.IsCoreLib(definition)) return; // csharpc's own library keeps what it reflects on explicitly
        // The shared frameworks' own dependencies (DI, type converters, ...) name code csharpc replaces or reaches
        // otherwise; the program's and its packages' are applied.
        if (Il.OwnerOf(definition) is { } owner && IsSharedFrameworkAssembly(owner.Path)) return;
        ApplyDynamicDependencies(definition, definition.ContainingType);
        if (definition.ContainingType is { } type && _dynamicDependencyTypes.Add(type.OriginalDefinition))
            foreach (var field in type.OriginalDefinition.GetMembers().OfType<IFieldSymbol>())
                ApplyDynamicDependencies(field, type);
    }

    private void ApplyDynamicDependencies(ISymbol member, INamedTypeSymbol? owner)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "DynamicDependencyAttribute" } c
                || c.ContainingNamespace?.ToDisplayString() != "System.Diagnostics.CodeAnalysis") continue;
            var args = attribute.ConstructorArguments;
            // (memberTypes, Type) / (memberTypes, typeName, assemblyName): the members of those kinds, methods
            // included, as NativeAOT keeps them. A method's invoker and function (which compile it) are made only
            // if the program invokes methods through reflection (ModuleCodeGen.LazyInvokers.cs): a Razor slice's
            // ~100 Write overloads cost their metadata, not their code, in a program that only reads properties.
            if (args.Length >= 2 && args[0].Value is int kinds)
            {
                if (TargetOf(args, 1) is { } target) MarkDynamicallyAccessed(target, kinds);
                continue;
            }
            // (memberSignature[, Type | typeName, assemblyName]): the members with that name (a signature's
            // parameter list narrows ILLink's choice to one overload; csharpc keeps every overload of the name).
            if (args.Length >= 1 && args[0].Value is string signature)
            {
                var target = args.Length == 1 ? owner : TargetOf(args, 1);
                if (target is null) continue;
                var name = signature;
                var paren = name.IndexOf('(');
                if (paren >= 0) name = name[..paren];
                name = name.Trim();
                if (name.Length == 0) continue;
                if (name == "#ctor" || name == ".ctor") MarkDynamicallyAccessed(target, DamConstructors);
                else MarkDynamicallyAccessed(target, DamMethods | DamProperties | DamFields, name);
            }
        }
    }

    private static string? s_sharedRoot;

    /// <summary>An assembly of an installed shared framework (dotnet/shared/&lt;framework&gt;/&lt;version&gt;/).</summary>
    private static bool IsSharedFrameworkAssembly(string path)
    {
        s_sharedRoot ??= Frontend.SharedFramework.Directory is { } dir
            ? Path.GetFullPath(Path.Combine(dir.TrimEnd('/', '\\'), "..", "..")) + Path.DirectorySeparatorChar : "";
        return s_sharedRoot.Length > 1 && Path.GetFullPath(path).StartsWith(s_sharedRoot, StringComparison.Ordinal);
    }

    /// <summary>The type an attribute names at <paramref name="index"/>: a Type argument, or a (typeName, assemblyName) pair.</summary>
    private ITypeSymbol? TargetOf(System.Collections.Immutable.ImmutableArray<TypedConstant> args, int index)
    {
        if (args[index].Value is ITypeSymbol type) return type;
        if (args[index].Value is not string typeName) return null;
        var comma = typeName.IndexOf(',');
        var metadataName = (comma >= 0 ? typeName[..comma] : typeName).Trim();
        var assemblyName = args.Length > index + 1 && args[index + 1].Value is string a ? a : comma >= 0 ? typeName[(comma + 1)..].Trim() : null;
        if (assemblyName is not null)
            foreach (var reference in Compilation.References)
                if (Compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly
                    && string.Equals(assembly.Identity.Name, assemblyName, StringComparison.OrdinalIgnoreCase)
                    && assembly.GetTypeByMetadataName(metadataName) is { } inAssembly)
                    return inAssembly;
        return Compilation.GetTypeByMetadataName(metadataName);
    }
}
