using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Csharpc.Frontend;

/// <summary>An assembly attribute as the implementation assembly stores it: the attribute type's full name, the types and values of its constructor arguments, and its named arguments.</summary>
public sealed record FrameworkAttribute(string TypeName, IReadOnlyList<(string Type, object? Value)> Arguments, IReadOnlyList<(string Name, string Type, object? Value)> Named);

/// <summary>
/// The assembly attributes of System.Private.CoreLib, read at compile time from the shared framework's implementation
/// assembly: what <c>typeof(object).Assembly.GetCustomAttributes()</c> returns under .NET (the reference assemblies
/// carry fewer). Roslyn's PDB writer asks for one of them, so csharpc compiled by csharpc needs them to emit a program.
/// </summary>
public static class FrameworkAttributes
{
    private static readonly Lock s_lock = new();
    private static (string Identity, IReadOnlyList<FrameworkAttribute> Attributes)? s_corelib;
    private static bool s_read;

    /// <summary>The identity ("System.Private.CoreLib, Version=...") and attributes of System.Private.CoreLib; null without a shared framework.</summary>
    public static (string Identity, IReadOnlyList<FrameworkAttribute> Attributes)? CoreLib
    {
        get
        {
            lock (s_lock)
            {
                if (!s_read) { s_corelib = Read(); s_read = true; }
                return s_corelib;
            }
        }
    }

    private static (string, IReadOnlyList<FrameworkAttribute>)? Read()
    {
        if (SharedFramework.Directory is not { } dir) return null;
        try
        {
            using var pe = new PEReader(File.OpenRead(Path.Combine(dir, "System.Private.CoreLib.dll")));
            var md = pe.GetMetadataReader();
            var identity = md.GetAssemblyDefinition().GetAssemblyName().FullName;
            List<FrameworkAttribute> attributes = [];
            var provider = new TypeProvider();
            foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = md.GetCustomAttribute(handle);
                var typeName = TypeNameOf(md, attribute.Constructor);
                if (typeName is null) continue;
                var value = attribute.DecodeValue(provider);
                attributes.Add(new FrameworkAttribute(typeName,
                    [.. value.FixedArguments.Select(a => (a.Type, a.Value))],
                    [.. value.NamedArguments.Select(a => (a.Name ?? "", a.Type, a.Value))]));
            }
            return (identity, attributes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException) { return null; }
    }

    private static string? TypeNameOf(MetadataReader md, EntityHandle constructor) => constructor.Kind switch
    {
        HandleKind.MethodDefinition => FullName(md, md.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
        HandleKind.MemberReference when md.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent
            => FullName(md, (TypeReferenceHandle)parent),
        _ => null,
    };

    private static string FullName(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var name = md.GetString(type.Name);
        if (type.GetDeclaringType() is { IsNil: false } outer) return FullName(md, outer) + "+" + name;
        return type.Namespace.IsNil ? name : md.GetString(type.Namespace) + "." + name;
    }

    private static string FullName(MetadataReader md, TypeReferenceHandle handle)
    {
        var type = md.GetTypeReference(handle);
        var name = md.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference) return FullName(md, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        return type.Namespace.IsNil ? name : md.GetString(type.Namespace) + "." + name;
    }

    /// <summary>Type names as strings: enough to match a constructor by its parameter types.</summary>
    private sealed class TypeProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode switch
        {
            PrimitiveTypeCode.Boolean => "Boolean", PrimitiveTypeCode.Byte => "Byte", PrimitiveTypeCode.SByte => "SByte",
            PrimitiveTypeCode.Char => "Char", PrimitiveTypeCode.Int16 => "Int16", PrimitiveTypeCode.UInt16 => "UInt16",
            PrimitiveTypeCode.Int32 => "Int32", PrimitiveTypeCode.UInt32 => "UInt32", PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.UInt64 => "UInt64", PrimitiveTypeCode.Single => "Single", PrimitiveTypeCode.Double => "Double",
            PrimitiveTypeCode.String => "String", PrimitiveTypeCode.Object => "Object", _ => typeCode.ToString(),
        };

        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => FullName(reader, handle);
        public string GetTypeFromSerializedName(string name) => name;
        // Every enum of the corelib's assembly attributes has an int underlying type.
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
    }
}
