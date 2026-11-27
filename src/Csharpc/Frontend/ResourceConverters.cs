using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.Frontend;

/// <summary>
/// Run-time TypeConverter creation in a closed world: the objects `ResourceManager.GetObject` creates from a preserialized
/// .resources file's converter strings.
///
/// A WinForms-style .resx with `GenerateResourceUsePreserializedResources` holds
/// `&lt;data name="Location" type="System.Drawing.Point, System.Drawing"&gt;&lt;value&gt;10, 20&lt;/value&gt;`. .NET's
/// DeserializingResourceReader finds the type by name at run time and calls
/// `TypeDescriptor.GetConverter(type).ConvertFromInvariantString(text)`. csharpc has no run-time type lookup by name,
/// but the types a program can get this way are known when it is compiled: the ones its own .resources files name. For each,
/// the front end resolves the type and its converter (the [TypeConverter] attribute, as TypeDescriptor does, or the intrinsic
/// converter of a BCL type) and generates
///
///     CsharpcCoreLib.ProgramHooks.ResourceObject(string type, string text)       a switch over the type names
///
/// which the corelib calls (corelib/Resources.cs). The converters are the framework's own code (System.ComponentModel.TypeConverter's
/// IL, kept by being named here) or the program's. A type that can't be resolved, or whose converter has no accessible
/// constructor, has no case and is reported by name at run time. (`TypeDescriptor.GetConverter(typeof(X))` itself is the framework's
/// code: it finds the [TypeConverter] attribute and creates the converter through the kept constructor.)
/// </summary>
public static class ResourceConverters
{
    private const string TreePath = "<csharpc-resource-converters>.g.cs";

    /// <summary>The types ResourceManager.GetObject serves itself (corelib/Resources.cs), as the .resources file spells their full names.</summary>
    private static readonly HashSet<string> s_primitives = new(StringComparer.Ordinal)
    {
        "null", "System.String", "System.Boolean", "System.Char", "System.Byte", "System.SByte", "System.Int16", "System.UInt16",
        "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal", "System.Byte[]",
    };

    /// <summary>BCL types TypeDescriptor gives a converter without an attribute (ReflectTypeDescriptionProvider's intrinsic table): the converter's full name.</summary>
    private static readonly Dictionary<string, string> s_intrinsic = new(StringComparer.Ordinal)
    {
        ["System.DateTime"] = "System.ComponentModel.DateTimeConverter",
        ["System.DateTimeOffset"] = "System.ComponentModel.DateTimeOffsetConverter",
        ["System.DateOnly"] = "System.ComponentModel.DateOnlyConverter",
        ["System.TimeOnly"] = "System.ComponentModel.TimeOnlyConverter",
        ["System.Globalization.CultureInfo"] = "System.ComponentModel.CultureInfoConverter",
        ["System.Uri"] = "System.UriTypeConverter",
        ["System.Half"] = "System.ComponentModel.HalfConverter",
        ["System.Int128"] = "System.ComponentModel.Int128Converter",
        ["System.UInt128"] = "System.ComponentModel.UInt128Converter",
        ["System.Numerics.BigInteger"] = "System.ComponentModel.BigIntegerConverter",
    };

    /// <summary>BCL types whose converters ResourceManager.GetObject creates itself (corelib/Resources.cs).</summary>
    private static readonly HashSet<string> s_corelibConverters = new(StringComparer.Ordinal)
    {
        "System.Guid", "System.TimeSpan", "System.Version",
    };

    /// <summary>The full names of the types the preserialized .resources among <paramref name="resources"/> store as TypeConverter strings, bar the ones GetObject creates itself.</summary>
    public static IReadOnlyList<string> TypeNames(IEnumerable<ProgramResource> resources)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            if (!resource.Name.EndsWith(".resources", StringComparison.Ordinal) || !File.Exists(resource.Path)) continue;
            try
            {
                foreach (var entry in Il.ResourcesFile.Read(File.ReadAllBytes(resource.Path)))
                    if (entry.Text is null && entry.Format == 3 && !s_primitives.Contains(entry.TypeName)) names.Add(entry.TypeName);
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException or ArgumentException or FormatException) { }
        }
        return [.. names];
    }

    /// <summary>Generates the ResourceObject hook for the .resources types <paramref name="typeNames"/>.</summary>
    public static CSharpCompilation Add(CSharpCompilation compilation, IReadOnlyList<string> typeNames, CSharpParseOptions parseOptions) =>
        Add(compilation, compilation, typeNames, parseOptions);

    /// <summary>The same for an IL program, resolved in <paramref name="symbols"/> (the compilation that imports its internals).</summary>
    public static CSharpCompilation AddFromIl(CSharpCompilation compilation, CSharpCompilation symbols, IReadOnlyList<string> typeNames, CSharpParseOptions parseOptions) =>
        Add(compilation, symbols, typeNames, parseOptions);

    private static CSharpCompilation Add(CSharpCompilation target, CSharpCompilation compilation, IReadOnlyList<string> typeNames, CSharpParseOptions parseOptions)
    {
        var resourceCases = new StringBuilder();
        foreach (var name in typeNames)
            if (!name.Contains('`') && compilation.GetTypeByMetadataName(name) is { } type && Creation(compilation, type) is { } creation)
                resourceCases.AppendLine($"                case {Literal(name)}: return new object[] {{ {creation}.ConvertFromInvariantString(text) }};");

        if (resourceCases.Length == 0) return target;
        var source = new StringBuilder();
        source.AppendLine("#pragma warning disable CS0612, CS0618, CS0619");
        source.AppendLine("namespace CsharpcCoreLib");
        source.AppendLine("{");
        source.AppendLine("    internal static partial class ProgramHooks");
        source.AppendLine("    {");
        source.AppendLine("        internal static object[] ResourceObject(string type, string text)");
        source.AppendLine("        {");
        source.AppendLine("            switch (type)");
        source.AppendLine("            {");
        source.Append(resourceCases);
        source.AppendLine("            }");
        source.AppendLine("            return null;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");
        return target.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source.ToString(), parseOptions, path: TreePath));
    }

    public static bool IsSynthesizedTree(SyntaxTree tree) => tree.FilePath == TreePath;

    /// <summary>The type names of the TypeConverter strings in the `.resources` manifest resources of the assemblies at <paramref name="paths"/> (IL programs).</summary>
    public static IReadOnlyList<string> TypeNamesOfAssemblies(IEnumerable<string> paths)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            try
            {
                using var pe = new PEReader(File.OpenRead(path));
                if (!pe.HasMetadata || pe.PEHeaders.CorHeader is not { } cor || cor.ResourcesDirectory.Size == 0) continue;
                var reader = pe.GetMetadataReader();
                var directory = pe.GetSectionData(cor.ResourcesDirectory.RelativeVirtualAddress);
                foreach (var handle in reader.ManifestResources)
                {
                    var resource = reader.GetManifestResource(handle);
                    if (!resource.Implementation.IsNil || !reader.GetString(resource.Name).EndsWith(".resources", StringComparison.Ordinal)) continue;
                    var block = directory.GetReader((int)resource.Offset, directory.Length - (int)resource.Offset);
                    foreach (var entry in ResourcesFile.Read(block.ReadBytes(block.ReadInt32())))
                        if (entry.Text is null && entry.Format == 3 && !s_primitives.Contains(entry.TypeName)) names.Add(entry.TypeName);
                }
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException or ArgumentException or FormatException or BadImageFormatException) { }
        }
        return [.. names];
    }

    private static string TypeName(ITypeSymbol type) =>
        "global::" + type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.ExpandNullable));

    /// <summary>
    /// The C# expression that creates the converter TypeDescriptor.GetConverter gives <paramref name="type"/> (when the corelib's own
    /// table doesn't cover it): the nearest [TypeConverter] up the hierarchy, EnumConverter, NullableConverter, or the intrinsic converter
    /// of a BCL type. Null when there is none, or it can't be created from the program (inaccessible, no suitable constructor).
    /// </summary>
    private static string? Creation(CSharpCompilation compilation, ITypeSymbol type)
    {
        if (type.SpecialType is not (SpecialType.None or SpecialType.System_DateTime) || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)) return null;
        var fullName = type.ContainingType is null && type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.MetadataName : null;
        if (fullName is not null && s_corelibConverters.Contains(fullName)) return null;

        string? converterName = null;
        INamedTypeSymbol? converter = null;
        for (var t = type; t is not null && converter is null && converterName is null; t = t.BaseType)
            foreach (var attribute in t.GetAttributes())
            {
                if (attribute.AttributeClass is not { Name: "TypeConverterAttribute", ContainingNamespace: { Name: "ComponentModel", ContainingNamespace.Name: "System" } }
                    || attribute.ConstructorArguments.Length != 1) continue;
                var argument = attribute.ConstructorArguments[0];
                if (argument.Value is INamedTypeSymbol named) converter = named;
                else if (argument.Value is string qualified) converterName = AssemblyLess(qualified);
                break;
            }
        if (converter is null && converterName is null)
        {
            if (type.TypeKind == TypeKind.Enum) converterName = "System.ComponentModel.EnumConverter";
            else if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }) converterName = "System.ComponentModel.NullableConverter";
            else if (fullName is null || !s_intrinsic.TryGetValue(fullName, out converterName)) return null;
        }
        converter ??= converterName is null ? null : compilation.GetTypeByMetadataName(converterName);
        if (converter is null || converter.IsAbstract || converter.IsGenericType || !compilation.IsSymbolAccessibleWithin(converter, compilation.Assembly)) return null;

        // ReflectTypeDescriptionProvider.CreateInstance: a constructor taking the Type first, else the parameterless one.
        var constructors = converter.InstanceConstructors.Where(c => compilation.IsSymbolAccessibleWithin(c, compilation.Assembly)).ToList();
        if (constructors.Any(c => c.Parameters.Length == 1 && c.Parameters[0].Type is { Name: "Type", ContainingNamespace.Name: "System" }))
            return $"new {TypeName(converter)}(typeof({TypeName(type)}))";
        return constructors.Any(c => c.Parameters.Length == 0) ? $"new {TypeName(converter)}()" : null;
    }

    /// <summary>"Ns.Type, Assembly, Version=..." (an attribute's type name string) to its metadata name "Ns.Type".</summary>
    private static string AssemblyLess(string qualified)
    {
        var depth = 0;
        for (var i = 0; i < qualified.Length; i++)
        {
            if (qualified[i] == '[') depth++;
            else if (qualified[i] == ']') depth--;
            else if (qualified[i] == ',' && depth == 0) return qualified[..i].Trim();
        }
        return qualified.Trim();
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);
}
