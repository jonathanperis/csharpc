using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// The [NullableContext] and [Nullable] attributes of the BCL's own types and members, read from the shared framework's implementation
/// assembly (Roslyn hides them in the reference assemblies the program binds to). NullabilityInfoContext over a framework member
/// (System.Text.Json asks it of a KeyValuePair's Key) needs exactly these; the rest of such a member's attribute list stays unknown, so
/// the lists carry only the nullable attributes and are flagged for the nullability reader alone.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private sealed class FrameworkAssembly(PEReader pe)
    {
        public readonly PEReader Pe = pe;
        public readonly MetadataReader Reader = pe.GetMetadataReader();
        private Dictionary<string, TypeDefinitionHandle>? _types;

        public TypeDefinitionHandle? Find(string? ns, string name, TypeDefinitionHandle? declaring)
        {
            if (declaring is { } outer)
            {
                foreach (var nested in Reader.GetTypeDefinition(outer).GetNestedTypes())
                    if (Reader.GetString(Reader.GetTypeDefinition(nested).Name) == name) return nested;
                return null;
            }
            _types ??= Reader.TypeDefinitions.Where(h => Reader.GetTypeDefinition(h).GetDeclaringType().IsNil)
                .GroupBy(h => (Reader.GetString(Reader.GetTypeDefinition(h).Namespace) + "." + Reader.GetString(Reader.GetTypeDefinition(h).Name)))
                .ToDictionary(g => g.Key, g => g.First());
            return _types.TryGetValue((ns ?? "") + "." + name, out var found) ? found : null;
        }
    }

    private readonly Dictionary<string, FrameworkAssembly?> _frameworkAssemblies = new(StringComparer.Ordinal);

    private FrameworkAssembly? FrameworkAssemblyOf(INamedTypeSymbol type)
    {
        var full = AssemblyOf(type);
        if (full is null) return null;
        var name = full.Split(',')[0];
        if (_frameworkAssemblies.TryGetValue(name, out var cached)) return cached;
        FrameworkAssembly? result = null;
        var directory = Frontend.SharedFramework.Directory;
        if (directory is not null && name.IndexOfAny(['/', (char)92]) < 0 && File.Exists(Path.Combine(directory, name + ".dll")))
        {
            try { result = new FrameworkAssembly(new PEReader(File.OpenRead(Path.Combine(directory, name + ".dll")))); }
            catch (Exception e) when (e is IOException or BadImageFormatException or InvalidOperationException) { }
        }
        return _frameworkAssemblies[name] = result;
    }

    /// <summary>A type of the BCL's reference assemblies (no source, no IL assembly of the program's): its implementation holds the attributes.</summary>
    private bool IsFrameworkReferenceType(INamedTypeSymbol type) =>
        type.OriginalDefinition.MetadataToken != 0 && !type.OriginalDefinition.Locations.Any(l => l.IsInSource) && Il.OwnerOf(type.OriginalDefinition) is null
        && !CoreLib.IsCoreLib(type) && !type.IsAnonymousType;

    private (FrameworkAssembly Assembly, TypeDefinitionHandle Type)? FrameworkTypeDefinition(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null; t = t.ContainingType) chain.Insert(0, t);
        if (FrameworkAssemblyOf(type) is not { } assembly) return null;
        TypeDefinitionHandle? current = null;
        foreach (var part in chain)
        {
            current = assembly.Find(part.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null, part.MetadataName, current);
            if (current is null) return null;
        }
        return (assembly, current!.Value);
    }

    /// <summary>The nullable attributes among <paramref name="handles"/>: [NullableContext] first, then [Nullable], as csc writes them.</summary>
    private List<AttributeInstance> NullableAttributesOf(MetadataReader reader, CustomAttributeHandleCollection handles)
    {
        byte? context = null;
        List<byte>? flags = null;
        foreach (var handle in handles)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            if (parent.Kind != HandleKind.TypeReference) continue;
            var name = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name);
            if (name is not ("NullableAttribute" or "NullableContextAttribute")) continue;
            var blob = reader.GetBlobBytes(attribute.Value);
            if (blob.Length == 5) { if (name == "NullableContextAttribute") context = blob[2]; else flags = [blob[2]]; }
            else if (blob.Length >= 9 && name == "NullableAttribute")
            {
                var count = BitConverter.ToInt32(blob, 2);
                if (count >= 0 && blob.Length >= 6 + count) flags = [.. blob.Skip(6).Take(count)];
            }
        }
        var list = new List<AttributeInstance>();
        if (context is { } c && NullableContextInstance(c) is { } contextInstance) list.Add(contextInstance);
        if (flags is not null && NullableInstance(flags) is { } nullableInstance) list.Add(nullableInstance);
        return list;
    }

    /// <summary>
    /// The members of an anonymous type: csc gives them [CompilerGenerated] and [DebuggerBrowsable] and no nullable annotations (the properties are
    /// of type parameters). The list is empty and flagged, so only the nullability reader and the framework's own queries may use it.
    /// </summary>
    private static AttributeSet AnonymousTypeSet() => FrameworkNullableSet([]);

    private static AttributeSet FrameworkNullableSet(List<AttributeInstance> instances) => new(instances, 0, Unknown: true, NullableExplicit: true);

    /// <summary>The nullable attributes of a BCL type, or null where its implementation cannot be found.</summary>
    internal List<AttributeInstance>? FrameworkTypeNullable(INamedTypeSymbol type)
    {
        if (!IsFrameworkReferenceType(type) || FrameworkTypeDefinition(type) is not var (assembly, handle)) return null;
        return NullableAttributesOf(assembly.Reader, assembly.Reader.GetTypeDefinition(handle).GetCustomAttributes());
    }

    private MethodDefinitionHandle? FrameworkMethod((FrameworkAssembly Assembly, TypeDefinitionHandle Type) owner, IMethodSymbol method)
    {
        var reader = owner.Assembly.Reader;
        var name = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.MetadataName;
        var matches = reader.GetTypeDefinition(owner.Type).GetMethods().Where(h => reader.GetString(reader.GetMethodDefinition(h).Name) == name
            && reader.GetMethodDefinition(h).GetParameters().Count(p => reader.GetParameter(p).SequenceNumber > 0) == method.Parameters.Length).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The nullable attributes of the <paramref name="ordinal"/>th type parameter of a BCL type; null where unknown.</summary>
    internal List<AttributeInstance>? FrameworkGenericParameterNullable(INamedTypeSymbol owner, int ordinal)
    {
        if (!IsFrameworkReferenceType(owner) || FrameworkTypeDefinition(owner) is not var (assembly, handle)) return null;
        var reader = assembly.Reader;
        var parameters = reader.GetTypeDefinition(handle).GetGenericParameters();
        // The CLR lists a nested type's parameters after its container's: ordinals count only this type's own.
        var own = ordinal - (owner.ContainingType is { } outer ? outer.TypeParameters.Length : 0);
        return own >= 0 && own < parameters.Count ? NullableAttributesOf(reader, reader.GetGenericParameter(parameters[own]).GetCustomAttributes()) : null;
    }

    private (FrameworkAssembly Assembly, TypeDefinitionHandle Type)? FrameworkOwner(ISymbol member) =>
        member.ContainingType is { } type && IsFrameworkReferenceType(type) ? FrameworkTypeDefinition(type) : null;

    /// <summary>A BCL method's, constructor's or accessor's [NullableContext]; null where unknown.</summary>
    private AttributeSet? FrameworkMethodSet(IMethodSymbol method)
    {
        if (method.ContainingType.IsAnonymousType) return AnonymousTypeSet();
        if (FrameworkOwner(method) is not { } owner || FrameworkMethod(owner, method.OriginalDefinition) is not { } handle) return null;
        var reader = owner.Assembly.Reader;
        return FrameworkNullableSet(NullableAttributesOf(reader, reader.GetMethodDefinition(handle).GetCustomAttributes()));
    }

    /// <summary>The attributes of a BCL method's return value (sequence 0) or parameter.</summary>
    private AttributeSet? FrameworkParameterSet(IMethodSymbol method, int sequence)
    {
        if (method.ContainingType.IsAnonymousType) return AnonymousTypeSet();
        if (FrameworkOwner(method) is not { } owner || FrameworkMethod(owner, method.OriginalDefinition) is not { } handle) return null;
        var reader = owner.Assembly.Reader;
        foreach (var parameter in reader.GetMethodDefinition(handle).GetParameters())
            if (reader.GetParameter(parameter).SequenceNumber == sequence)
                return FrameworkNullableSet(NullableAttributesOf(reader, reader.GetParameter(parameter).GetCustomAttributes()));
        return FrameworkNullableSet([]);
    }

    private AttributeSet? FrameworkPropertySet(IPropertySymbol property)
    {
        if (property.ContainingType.IsAnonymousType) return AnonymousTypeSet();
        if (FrameworkOwner(property) is not { } owner) return null;
        var reader = owner.Assembly.Reader;
        var matches = reader.GetTypeDefinition(owner.Type).GetProperties().Where(h => reader.GetString(reader.GetPropertyDefinition(h).Name) == property.MetadataName).ToList();
        return matches.Count == 1 ? FrameworkNullableSet(NullableAttributesOf(reader, reader.GetPropertyDefinition(matches[0]).GetCustomAttributes())) : null;
    }

    private AttributeSet? FrameworkFieldSet(IFieldSymbol field)
    {
        if (field.ContainingType.IsAnonymousType) return AnonymousTypeSet();
        if (FrameworkOwner(field) is not { } owner) return null;
        var reader = owner.Assembly.Reader;
        var matches = reader.GetTypeDefinition(owner.Type).GetFields().Where(h => reader.GetString(reader.GetFieldDefinition(h).Name) == field.MetadataName).ToList();
        return matches.Count == 1 ? FrameworkNullableSet(NullableAttributesOf(reader, reader.GetFieldDefinition(matches[0]).GetCustomAttributes())) : null;
    }
}
