using System.Text;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using static Csharpc.Llvm.Native;

namespace Csharpc.CodeGen;

/// <summary>
/// DWARF debug info (`csharpc -g`), built with LLVM's DIBuilder (docs/11-debugging.md).
///
/// - One compile unit per program; DW_LANG_C_plus_plus (there is no DWARF code for C#), producer "csharpc".
/// - Every function lowered from C# gets a DISubprogram named like the C# member ("Program.Fib", "Circle.Area")
///   (no linkage name: debuggers would mis-demangle _CN… symbols); synthesized functions get none.
/// - Line/column locations come from each IOperation's syntax as it is lowered.
/// - Locals/parameters are described with dbg.declare on their allocas; strings, arrays and classes are
///   pointers to structs mirroring the runtime layout (tools/gdb/csharpc.py pretty-prints them).
/// </summary>
public sealed class DebugInfo : IDisposable
{
    private const int DwAteBoolean = 0x02, DwAteFloat = 0x04, DwAteSigned = 0x05, DwAteSignedChar = 0x06,
        DwAteUnsigned = 0x07, DwAteUnsignedChar = 0x08, DwAteUtf = 0x10;
    private const int FlagArtificial = 1 << 6, FlagPrototyped = 1 << 8, FlagObjectPointer = 1 << 10;
    private const uint DwTagStructureType = 0x13;
    private const int LanguageCPlusPlus = 3; // index into LLVMDWARFSourceLanguage, not the DWARF code

    private readonly ModuleCodeGen _m;
    private readonly LLVMDIBuilderRef _di;
    private readonly LLVMMetadataRef _unit;
    private readonly LLVMMetadataRef _unitFile;
    private readonly Dictionary<string, LLVMMetadataRef> _files = [];
    private readonly Dictionary<ITypeSymbol, LLVMMetadataRef> _types = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, LLVMMetadataRef> _named = [];
    private LLVMMetadataRef _emptySubroutine;

    public DebugInfo(ModuleCodeGen module, string mainFile, bool optimized)
    {
        _m = module;
        _di = LLVMCreateDIBuilder(module.Module.Ref);
        _unitFile = File(Path.GetFullPath(mainFile));
        _unit = LLVMDIBuilderCreateCompileUnit(_di, LanguageCPlusPlus, _unitFile, "csharpc", (nuint)"csharpc".Length,
            optimized ? 1 : 0, "", 0, 0, "", 0, 1 /* FullDebug */, 0, 0, 0, "", 0, "", 0);
        var i32 = module.Llvm.I32;
        LLVMAddModuleFlag(module.Module.Ref, 1 /* Warning */, "Debug Info Version", 18, LLVMValueAsMetadata(module.Llvm.ConstInt(i32, 3)));
        LLVMAddModuleFlag(module.Module.Ref, 1, "Dwarf Version", 13, LLVMValueAsMetadata(module.Llvm.ConstInt(i32, 4))); // gdb ignores LLVM's DWARF 5 .debug_names
    }

    private static nuint Len(string s) => (nuint)Encoding.UTF8.GetByteCount(s);

    public LLVMMetadataRef File(string path)
    {
        if (_files.TryGetValue(path, out var f)) return f;
        var name = Path.GetFileName(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        return _files[path] = LLVMDIBuilderCreateFile(_di, name, Len(name), dir, Len(dir));
    }

    public LLVMMetadataRef Location(int line, int column, LLVMMetadataRef scope) =>
        LLVMDIBuilderCreateDebugLocation(_m.Llvm.Ref, (uint)Math.Max(line, 0), (uint)Math.Max(column, 0), scope, default);

    /// <summary>Attaches a DISubprogram to a function lowered from C# (null for synthesized code).</summary>
    public FunctionDebug? BeginFunction(LLVMValueRef fn, IMethodSymbol? method)
    {
        if (method is null) return null;
        var location = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;
        var span = location.GetLineSpan();
        var file = File(Path.GetFullPath(span.Path));
        var line = (uint)(span.StartLinePosition.Line + 1);
        if (LLVMGetSubprogram(fn) is { IsNull: false } existing) return new FunctionDebug(this, existing, file, (int)line);
        var name = ReadableName(method);
        // No DW_AT_linkage_name: gdb would try to demangle our _CN… symbols (not Itanium) into garbage.
        var linkage = "";
        _emptySubroutine = _emptySubroutine.IsNull
            ? LLVMDIBuilderCreateSubroutineType(_di, _unitFile, [], 0, 0)
            : _emptySubroutine;
        var sp = LLVMDIBuilderCreateFunction(_di, file, name, Len(name), linkage, Len(linkage), file, line, _emptySubroutine,
            1, 1, line, FlagPrototyped, 0);
        LLVMSetSubprogram(fn, sp);
        return new FunctionDebug(this, sp, file, (int)line);
    }

    /// <summary>Artificial subprogram for compiler-generated functions (e.g. the native main shim).</summary>
    public FunctionDebug? BeginSynthetic(LLVMValueRef fn, string name, ISymbol near)
    {
        var location = near.Locations.FirstOrDefault(l => l.IsInSource);
        var file = location is null ? _unitFile : File(Path.GetFullPath(location.GetLineSpan().Path));
        _emptySubroutine = _emptySubroutine.IsNull ? LLVMDIBuilderCreateSubroutineType(_di, _unitFile, [], 0, 0) : _emptySubroutine;
        var sp = LLVMDIBuilderCreateFunction(_di, file, name, Len(name), "", 0, file, 0, _emptySubroutine, 0, 1, 0,
            FlagPrototyped | FlagArtificial, 0);
        LLVMSetSubprogram(fn, sp);
        return new FunctionDebug(this, sp, file, 0);
    }

    /// <summary>"Program.Fib", "Shapes.Circle.Area", "Program.&lt;Main&gt;$.Square" (local function).</summary>
    public static string ReadableName(IMethodSymbol method)
    {
        var name = method.MethodKind switch
        {
            MethodKind.Constructor => method.ContainingType.Name,
            MethodKind.StaticConstructor => "cctor",
            MethodKind.AnonymousFunction => "<lambda>",
            _ => method.Name,
        };
        return method.ContainingSymbol switch
        {
            IMethodSymbol outer => ReadableName(outer) + "." + name,
            INamedTypeSymbol type => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat.WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes)) + "." + name,
            _ => name,
        };
    }

    // ==================================================================================
    // Variables
    // ==================================================================================

    public void DeclareVariable(FunctionDebug f, LLVMValueRef storage, string name, ITypeSymbol type, Location? location,
        uint argNo, LLVMBasicBlockRef block)
    {
        if (string.IsNullOrEmpty(name) || name.StartsWith('<')) return;
        var diType = TypeOf(type);
        if (diType.IsNull) return;
        var line = location is { IsInSource: true } ? location.GetLineSpan().StartLinePosition.Line + 1 : f.Line;
        var variable = argNo > 0
            ? LLVMDIBuilderCreateParameterVariable(_di, f.Scope, name, Len(name), argNo, f.File, (uint)line, diType, 1, 0)
            : LLVMDIBuilderCreateAutoVariable(_di, f.Scope, name, Len(name), f.File, (uint)line, diType, 1, 0, 0);
        var expr = LLVMDIBuilderCreateExpression(_di, null, 0);
        LLVMDIBuilderInsertDeclareAtEnd(_di, storage, variable, expr, Location(line, 0, f.Scope), block);
    }

    /// <summary>`this`: an SSA parameter (not an alloca), described with dbg.value.</summary>
    public void DeclareThis(FunctionDebug f, LLVMValueRef self, ITypeSymbol type, LLVMBasicBlockRef block)
    {
        var diType = type.IsValueType && TypeOf(type) is { IsNull: false } valueType ? Pointer(valueType) : TypeOf(type);
        if (diType.IsNull) return;
        var variable = LLVMDIBuilderCreateParameterVariable(_di, f.Scope, "this", 4, 1, f.File, (uint)f.Line, diType, 1,
            FlagArtificial | FlagObjectPointer);
        LLVMDIBuilderInsertDbgValueAtEnd(_di, self, variable, LLVMDIBuilderCreateExpression(_di, null, 0), Location(f.Line, 0, f.Scope), block);
    }

    // ==================================================================================
    // Types
    // ==================================================================================

    private ulong PointerBits => _m.Types.PointerSize * 8;

    private LLVMMetadataRef Basic(string name, ulong bits, int encoding)
    {
        var key = "basic:" + name;
        if (_named.TryGetValue(key, out var t)) return t;
        return _named[key] = LLVMDIBuilderCreateBasicType(_di, name, Len(name), bits, (uint)encoding, 0);
    }

    private LLVMMetadataRef Pointer(LLVMMetadataRef pointee, string name = "") =>
        LLVMDIBuilderCreatePointerType(_di, pointee, PointerBits, 0, 0, name, Len(name));

    private LLVMMetadataRef Member(LLVMMetadataRef scope, string name, ulong bits, ulong offsetBits, LLVMMetadataRef type, int flags = 0) =>
        LLVMDIBuilderCreateMemberType(_di, scope, name, Len(name), _unitFile, 0, bits, 0, offsetBits, flags, type);

    private LLVMMetadataRef Struct(string name, ulong bits, LLVMMetadataRef[] members) =>
        LLVMDIBuilderCreateStructType(_di, _unit, name, Len(name), _unitFile, 0, bits, 0, 0, default, members, (uint)members.Length,
            0, default, name, Len(name));

    private LLVMMetadataRef OpaqueMethodTablePointer => _named.TryGetValue("mtptr", out var t)
        ? t
        : _named["mtptr"] = Pointer(Struct("csrt_MethodTable", 0, []));

    /// <summary>A pointer to System.Object: { __mt }.</summary>
    private LLVMMetadataRef ObjectPointer => _named.TryGetValue("object*", out var t)
        ? t
        : _named["object*"] = Pointer(Struct("System.Object", PointerBits, [Member(_unit, "__mt", PointerBits, 0, OpaqueMethodTablePointer, FlagArtificial)]));

    public LLVMMetadataRef TypeOf(ITypeSymbol type)
    {
        if (_types.TryGetValue(type, out var cached)) return cached;
        var result = Build(type);
        _types[type] = result;
        return result;
    }

    private LLVMMetadataRef Build(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return Basic("bool", 8, DwAteBoolean);
            case SpecialType.System_Char: return Basic("char", 16, DwAteUtf);
            case SpecialType.System_SByte: return Basic("sbyte", 8, DwAteSignedChar);
            case SpecialType.System_Byte: return Basic("byte", 8, DwAteUnsignedChar);
            case SpecialType.System_Int16: return Basic("short", 16, DwAteSigned);
            case SpecialType.System_UInt16: return Basic("ushort", 16, DwAteUnsigned);
            case SpecialType.System_Int32: return Basic("int", 32, DwAteSigned);
            case SpecialType.System_UInt32: return Basic("uint", 32, DwAteUnsigned);
            case SpecialType.System_Int64: return Basic("long", 64, DwAteSigned);
            case SpecialType.System_UInt64: return Basic("ulong", 64, DwAteUnsigned);
            case SpecialType.System_IntPtr: return Basic("nint", PointerBits, DwAteSigned);
            case SpecialType.System_UIntPtr: return Basic("nuint", PointerBits, DwAteUnsigned);
            case SpecialType.System_Single: return Basic("float", 32, DwAteFloat);
            case SpecialType.System_Double: return Basic("double", 64, DwAteFloat);
            case SpecialType.System_String: return StringPointer();
            case SpecialType.System_Object: return ObjectPointer;
        }
        switch (type)
        {
            case INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }:
            {
                var u = TypeOf(underlying);
                return u; // values print as numbers; names would need DW_TAG_enumeration_type
            }
            case IArrayTypeSymbol { Rank: 1 } array:
                return ArrayPointer(array);
            case IPointerTypeSymbol pointer:
                return TypeOf(pointer.PointedAtType) is { IsNull: false } pointee ? Pointer(pointee) : Pointer(Basic("void", 8, DwAteUnsigned));
            case INamedTypeSymbol { TypeKind: TypeKind.Class } cls when _m.IsUserType(cls) && !cls.IsAnonymousType && !TypeSubstitution.IsOpen(cls):
                return ClassPointer(cls);
            case INamedTypeSymbol { TypeKind: TypeKind.Struct } st when _m.IsUserType(st) && !TypeSubstitution.IsOpen(st):
                return StructValue(st);
            case { IsReferenceType: true }:
                return ObjectPointer;
            default:
                return default; // nullable, tuples, spans, decimal...: not described (yet)
        }
    }

    // System.String: { __mt, int Length, char chars[Length] }
    private LLVMMetadataRef StringPointer()
    {
        if (_named.TryGetValue("string*", out var t)) return t;
        var charType = Basic("char", 16, DwAteUtf);
        var chars = LLVMDIBuilderCreateArrayType(_di, 0, 16, charType, [LLVMDIBuilderGetOrCreateSubrange(_di, 0, 0)], 1);
        var header = _m.Types.StringHeader;
        var td = _m.Types.Target.DataLayout;
        var s = Struct("System.String", _m.Types.Target.SizeOf(header) * 8,
        [
            Member(_unit, "__mt", PointerBits, 0, OpaqueMethodTablePointer, FlagArtificial),
            Member(_unit, "Length", 32, LLVMOffsetOfElement(td, header, 1) * 8, Basic("int", 32, DwAteSigned)),
            Member(_unit, "chars", 0, LLVMOffsetOfElement(td, header, 2) * 8, chars),
        ]);
        return _named["string*"] = Pointer(s, "string");
    }

    // T[]: { __mt, long Length, T data[Length] }
    private LLVMMetadataRef ArrayPointer(IArrayTypeSymbol array)
    {
        var element = TypeOf(array.ElementType);
        if (element.IsNull) element = Basic("byte", 8, DwAteUnsignedChar);
        var storage = _m.Types.Storage(array.ElementType);
        var header = _m.Types.ArrayHeader(storage);
        var td = _m.Types.Target.DataLayout;
        var data = LLVMDIBuilderCreateArrayType(_di, 0, 0, element, [LLVMDIBuilderGetOrCreateSubrange(_di, 0, 0)], 1);
        var name = array.ToDisplayString();
        var s = Struct(name, _m.Types.Target.SizeOf(header) * 8,
        [
            Member(_unit, "__mt", PointerBits, 0, OpaqueMethodTablePointer, FlagArtificial),
            Member(_unit, "Length", 64, LLVMOffsetOfElement(td, header, 1) * 8, Basic("long", 64, DwAteSigned)),
            Member(_unit, "data", 0, LLVMOffsetOfElement(td, header, 2) * 8, data),
        ]);
        return Pointer(s);
    }

    // class C: pointer to { __mt, fields... } (inherited fields first, as laid out by ClassLayout).
    private LLVMMetadataRef ClassPointer(INamedTypeSymbol cls)
    {
        ClassLayout layout;
        try { layout = _m.GetLayout(cls); }
        catch (CompileError) { return ObjectPointer; }
        var name = cls.ToDisplayString();
        // Classes can refer to themselves (Node.Next): describe through a replaceable placeholder.
        var placeholder = LLVMDIBuilderCreateReplaceableCompositeType(_di, DwTagStructureType, name, Len(name), _unit, _unitFile, 0, 0,
            _m.Types.Target.SizeOf(layout.InstanceType) * 8, 0, 0, name, Len(name));
        var pointer = Pointer(placeholder);
        _types[cls] = pointer;
        var td = _m.Types.Target.DataLayout;
        List<LLVMMetadataRef> members = [Member(_unit, "__mt", PointerBits, 0, OpaqueMethodTablePointer, FlagArtificial)];
        foreach (var field in layout.Fields)
        {
            if (!layout.FieldIndex.TryGetValue(field, out var index)) continue;
            var fieldType = TypeOf(field.Type);
            if (fieldType.IsNull) continue;
            var storage = _m.Types.Storage(field.Type);
            members.Add(Member(_unit, FieldName(field), _m.Types.Target.SizeOf(storage) * 8, LLVMOffsetOfElement(td, layout.InstanceType, index) * 8, fieldType));
        }
        var real = Struct(name, _m.Types.Target.SizeOf(layout.InstanceType) * 8, [.. members]);
        LLVMMetadataReplaceAllUsesWith(placeholder, real);
        return pointer;
    }

    // struct S: by value { fields... }
    private LLVMMetadataRef StructValue(INamedTypeSymbol st)
    {
        StructLayout layout;
        try { layout = _m.GetStructLayout(st); }
        catch (CompileError) { return default; }
        var name = st.ToDisplayString();
        var td = _m.Types.Target.DataLayout;
        List<LLVMMetadataRef> members = [];
        foreach (var field in st.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst))
        {
            if (!layout.FieldIndex.TryGetValue(field, out var index)) continue;
            var fieldType = TypeOf(field.Type);
            if (fieldType.IsNull) continue;
            members.Add(Member(_unit, FieldName(field), _m.Types.Target.SizeOf(_m.Types.Storage(field.Type)) * 8,
                LLVMOffsetOfElement(td, layout.LlvmType, index) * 8, fieldType));
        }
        return Struct(name, _m.Types.Target.SizeOf(layout.LlvmType) * 8, [.. members]);
    }

    /// <summary>Auto-property backing fields are shown under the property's name.</summary>
    private static string FieldName(IFieldSymbol field) =>
        field.AssociatedSymbol is IPropertySymbol p ? p.Name
        : field.Name.StartsWith('<') && field.Name.EndsWith(">P") ? field.Name[1..^2] // captured primary-ctor parameter
        : field.Name;

    public void Finish() => LLVMDIBuilderFinalize(_di);

    public void Dispose() => LLVMDisposeDIBuilder(_di);
}

/// <summary>Debug state of one function being lowered.</summary>
public sealed class FunctionDebug(DebugInfo owner, LLVMMetadataRef scope, LLVMMetadataRef file, int line)
{
    public DebugInfo Owner { get; } = owner;
    public LLVMMetadataRef Scope { get; } = scope;
    public LLVMMetadataRef File { get; } = file;
    public int Line { get; } = line;

    public void SetLocation(IrBuilder builder, int line, int column) =>
        LLVMSetCurrentDebugLocation2(builder.Ref, Owner.Location(line, column, Scope));
}
