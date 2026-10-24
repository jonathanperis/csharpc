// System.Reflection.Metadata (and System.Collections.Immutable) compiled from the shared framework's IL:
// read the IL test libraries' assemblies (built by tests/il.sh before the apps) and list their contents.
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

string[] paths = args.Length > 0 ? args : ["../IlShapes/bin/Release/net11.0/IlShapes.dll", "../IlFeatures/bin/Release/net11.0/IlFeatures.dll"];
foreach (var path in paths) Dump(path, fromStream: path.Contains("Features"));
MapImage(paths[^1]);

// A third way in: a FileStream read lazily. Blocks above 16 KiB (the whole image here) are memory-mapped.
static void MapImage(string path)
{
    using var pe = new PEReader(File.OpenRead(path));
    var image = pe.GetEntireImage();
    var header = image.GetContent(0, 2);
    Console.WriteLine($"== mapped {Path.GetFileName(path)}: {image.Length > 16 * 1024} '{(char)header[0]}{(char)header[1]}' {pe.GetMetadataReader().TypeDefinitions.Count} types");
    Console.WriteLine($"   e_lfanew read through the image: {BitConverter.ToInt32(image.GetContent(0x3C, 4).ToArray(), 0) > 0}");
}

static void Dump(string path, bool fromStream)
{
    // Two ways in: an in-memory image, and a stream read up front.
    using var pe = fromStream
        ? new PEReader(new MemoryStream(File.ReadAllBytes(path)), PEStreamOptions.PrefetchEntireImage)
        : new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
    var headers = pe.PEHeaders;
    Console.WriteLine($"== {Path.GetFileName(path)}: PE32+={headers.PEHeader!.Magic == PEMagic.PE32Plus} dll={headers.IsDll} sections={string.Join(",", headers.SectionHeaders.Select(s => s.Name))}");
    Console.WriteLine($"CorFlags: {headers.CorHeader!.Flags}");
    var md = pe.GetMetadataReader();
    var asm = md.GetAssemblyDefinition();
    Console.WriteLine($"Assembly: {md.GetString(asm.Name)} {asm.Version} culture='{md.GetString(asm.Culture)}' flags={asm.Flags}");
    Console.WriteLine($"Metadata: {md.MetadataVersion} kind={md.MetadataKind} types={md.TypeDefinitions.Count} methods={md.MethodDefinitions.Count} fields={md.FieldDefinitions.Count}");
    foreach (var h in md.AssemblyReferences.OrderBy(h => md.GetString(md.GetAssemblyReference(h).Name), StringComparer.Ordinal))
    {
        var r = md.GetAssemblyReference(h);
        Console.WriteLine($"  ref {md.GetString(r.Name)} {r.Version}");
    }

    var provider = new NameProvider(md);
    foreach (var h in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(h);
        var generic = t.GetGenericParameters().Count > 0
            ? "<" + string.Join(",", t.GetGenericParameters().Select(p => md.GetString(md.GetGenericParameter(p).Name))) + ">"
            : "";
        var baseType = t.BaseType.IsNil ? "" : " : " + provider.Name(t.BaseType);
        Console.WriteLine($"type {provider.Name(h)}{generic}{baseType} ({t.Attributes})");
        foreach (var ih in t.GetInterfaceImplementations())
            Console.WriteLine($"  implements {provider.Name(md.GetInterfaceImplementation(ih).Interface)}");
        foreach (var fh in t.GetFields())
        {
            var f = md.GetFieldDefinition(fh);
            Console.WriteLine($"  field {f.DecodeSignature(provider, null)} {md.GetString(f.Name)} ({f.Attributes})");
        }
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            var sig = m.DecodeSignature(provider, null);
            var parameters = m.GetParameters().Select(p => md.GetParameter(p)).Where(p => p.SequenceNumber > 0)
                .Select(p => md.GetString(p.Name)).ToList();
            var named = sig.ParameterTypes.Select((type, i) => i < parameters.Count ? $"{type} {parameters[i]}" : type);
            var size = m.RelativeVirtualAddress == 0 ? "no body" : $"IL {pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes()!.Length} bytes";
            Console.WriteLine($"  method {sig.ReturnType} {md.GetString(m.Name)}({string.Join(", ", named)}) [{m.Attributes}] {size}");
        }
        foreach (var ph in t.GetProperties())
        {
            var p = md.GetPropertyDefinition(ph);
            Console.WriteLine($"  property {p.DecodeSignature(provider, null).ReturnType} {md.GetString(p.Name)}");
        }
        foreach (var eh in t.GetEvents())
        {
            var e = md.GetEventDefinition(eh);
            Console.WriteLine($"  event {provider.Name(e.Type)} {md.GetString(e.Name)}");
        }
    }

    // Custom attributes on the assembly, with their constructor arguments decoded.
    foreach (var ah in asm.GetCustomAttributes())
    {
        var a = md.GetCustomAttribute(ah);
        var ctor = a.Constructor.Kind == HandleKind.MemberReference ? md.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent : default;
        var value = a.DecodeValue(provider);
        var fixedArgs = string.Join(", ", value.FixedArguments.Select(x => x.Value is string s ? $"\"{s}\"" : $"{x.Value}"));
        Console.WriteLine($"  [{(ctor.IsNil ? "?" : provider.Name(ctor))}({fixedArgs})]");
    }

    // The user-string heap and member references, by token order.
    var strings = new List<string>();
    var handle = MetadataTokens.UserStringHandle(1);
    while (!handle.IsNil && strings.Count < 8)
    {
        strings.Add(md.GetUserString(handle));
        handle = md.GetNextHandle(handle);
    }
    Console.WriteLine($"  user strings: {string.Join(" | ", strings)}");
    var memberRefs = md.MemberReferences.Select(h => md.GetMemberReference(h)).Take(12)
        .Select(r => $"{provider.Name(r.Parent)}::{md.GetString(r.Name)}");
    Console.WriteLine($"  member refs: {string.Join(", ", memberRefs)}");

    // Raw blob reading: the first method body's IL opcodes.
    var first = md.MethodDefinitions.Select(h => md.GetMethodDefinition(h)).First(m => m.RelativeVirtualAddress != 0);
    var reader = pe.GetMethodBody(first.RelativeVirtualAddress).GetILReader();
    var ops = new StringBuilder();
    while (reader.RemainingBytes > 0 && ops.Length < 200)
    {
        int op = reader.ReadByte();
        if (op == 0xFE) op = 0xFE00 | reader.ReadByte();
        ops.Append($"{op:X2} ");
        // Skip the operand of the few opcodes this list knows; stop at anything else.
        int operand = op switch
        {
            0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x14 or 0x16
                or 0x17 or 0x18 or 0x19 or 0x1A or 0x1B or 0x1C or 0x1D or 0x1E or 0x2A or 0x26 or 0x25 or 0x58 or 0x59 => 0,
            0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x1F or 0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0xDE => 1,
            0x20 or 0x28 or 0x6F or 0x73 or 0x72 or 0x7B or 0x7D or 0x7E or 0x80 or 0x38 or 0x39 or 0x3A or 0x8D or 0x74 or 0x75 => 4,
            _ => -1,
        };
        if (operand < 0) break;
        reader.Offset += operand;
    }
    Console.WriteLine($"  {md.GetString(first.Name)} IL: {ops.ToString().TrimEnd()}");
}

/// <summary>Type names for signatures, attribute arguments and handles.</summary>
sealed class NameProvider(MetadataReader md) : ISignatureTypeProvider<string, object?>, ICustomAttributeTypeProvider<string>
{
    public string Name(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(md, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(md, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(md, null, (TypeSpecificationHandle)handle, 0),
        _ => handle.Kind.ToString(),
    };

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Int32 => "int", PrimitiveTypeCode.String => "string", PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool", PrimitiveTypeCode.Object => "object", PrimitiveTypeCode.Double => "double",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var t = reader.GetTypeDefinition(handle);
        var name = reader.GetString(t.Name);
        if (t.IsNested) return GetTypeFromDefinition(reader, t.GetDeclaringType(), 0) + "/" + name;
        var ns = reader.GetString(t.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var r = reader.GetTypeReference(handle);
        var name = reader.GetString(r.Name);
        if (r.ResolutionScope.Kind == HandleKind.TypeReference) return GetTypeFromReference(reader, (TypeReferenceHandle)r.ResolutionScope, 0) + "/" + name;
        var ns = reader.GetString(r.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";

    public string GetSystemType() => "System.Type";
    public bool IsSystemType(string type) => type == "System.Type";
    public string GetTypeFromSerializedName(string name) => name;
    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
}
