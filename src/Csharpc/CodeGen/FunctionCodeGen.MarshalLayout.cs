using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Marshal.SizeOf, OffsetOf, PtrToStructure, StructureToPtr and DestroyStructure for a type known at the call
/// site (a type argument, `typeof(X)`, or the static type of the structure): the unmanaged layout is computed at
/// compile time, as the CLR's marshaller lays it out — sequential or explicit, [StructLayout] Pack and Size,
/// `bool` as a 4-byte BOOL (or 1 byte with [MarshalAs(U1/I1)]), `char` as one ANSI byte unless
/// the layout says Unicode — and the copies are field by field (or one block copy when the unmanaged layout is
/// the managed one). Types that need real marshalling (strings, arrays, delegates, object references) or whose
/// type is only known at run time are explicit diagnostics.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private enum NativeKind { Raw, Bool, Char, Struct, Opaque }

    private sealed record NativeField(IFieldSymbol Field, int Offset, int Size, NativeKind Kind, NativeLayout? Nested, int BoolSize = 4);

    private sealed class NativeLayout(ITypeSymbol type, NativeKind kind)
    {
        public ITypeSymbol Type { get; } = type;
        public NativeKind Kind { get; } = kind;
        public int Size { get; set; }
        public int Align { get; set; } = 1;
        public int BoolSize { get; set; } = 4;
        public List<NativeField> Fields { get; } = [];
        public bool IsClass { get; set; }
    }

    /// <summary>What the marshaller would refuse (an ArgumentException at run time) versus what csharpc can't do yet.</summary>
    private sealed class NativeLayoutException(bool notMarshalable, string message, string? paramName = null) : Exception(message)
    {
        public bool NotMarshalable { get; } = notMarshalable;
        /// <summary>Set when the marshaller throws ArgumentException(message, paramName) for the call's own argument.</summary>
        public string? ParamName { get; } = paramName;
    }

    private const string StructLayoutAttributeName = "System.Runtime.InteropServices.StructLayoutAttribute";

    private static AttributeData? AttributeNamed(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fullName);

    /// <summary>[StructLayout] of a type: LayoutKind (Sequential 0, Explicit 2, Auto 3), Pack, Size and CharSet (None 1, Ansi 2, Unicode 3, Auto 4).</summary>
    private (int Kind, int Pack, int Size, int CharSet) StructLayoutFacts(INamedTypeSymbol type)
    {
        if (type.OriginalDefinition.MetadataToken != 0 && _m.Il.OwnerOf(type.OriginalDefinition) is { } owner)
        {
            // From IL the attribute is the type's layout and string-format flags and its ClassLayout row.
            var definition = owner.TypeDefinitionOf(type);
            var layout = definition.GetLayout();
            var kind = (definition.Attributes & System.Reflection.TypeAttributes.LayoutMask) switch
            {
                System.Reflection.TypeAttributes.SequentialLayout => 0,
                System.Reflection.TypeAttributes.ExplicitLayout => 2,
                _ => 3,
            };
            var charSet = (definition.Attributes & System.Reflection.TypeAttributes.StringFormatMask) switch
            {
                System.Reflection.TypeAttributes.UnicodeClass => 3,
                System.Reflection.TypeAttributes.AutoClass => 4,
                _ => 2,
            };
            return (kind, layout.PackingSize, layout.Size, charSet);
        }
        var attribute = AttributeNamed(type, StructLayoutAttributeName);
        int Named(string name) => attribute?.NamedArguments.FirstOrDefault(n => n.Key == name).Value.Value is { } n ? Convert.ToInt32(n) : 0;
        return (attribute?.ConstructorArguments.FirstOrDefault().Value is { } k ? Convert.ToInt32(k) : type.TypeKind == TypeKind.Struct ? 0 : 3,
            Named("Pack"), Named("Size"), attribute is null ? 2 : Named("CharSet") is var c and not 0 ? c : 2);
    }

    /// <summary>A field's [FieldOffset] and the UnmanagedType of its [MarshalAs], from source or from IL metadata.</summary>
    private (int? Offset, int? Unmanaged) FieldMarshalFacts(IFieldSymbol field)
    {
        if (field.OriginalDefinition.MetadataToken != 0 && _m.Il.OwnerOf(field.ContainingType.OriginalDefinition) is { } owner)
        {
            var definition = owner.FieldDefinitionOf(field);
            int? offset = definition.GetOffset() is >= 0 and var o ? o : null;
            int? unmanaged = null;
            var descriptor = definition.GetMarshallingDescriptor();
            if (!descriptor.IsNil) unmanaged = owner.Reader.GetBlobReader(descriptor).ReadByte(); // the NativeType, as UnmanagedType
            return (offset, unmanaged);
        }
        int? declared = AttributeNamed(field, "System.Runtime.InteropServices.FieldOffsetAttribute")?.ConstructorArguments.FirstOrDefault().Value is { } d ? Convert.ToInt32(d) : null;
        int? marshalAs = AttributeNamed(field, "System.Runtime.InteropServices.MarshalAsAttribute")?.ConstructorArguments.FirstOrDefault().Value is { } m ? Convert.ToInt32(m) : null;
        return (declared, marshalAs);
    }

    private int PointerBytes => (int)T.Target.SizeOf(L.Ptr);

    /// <summary>The unmanaged layout of a type given to Marshal.SizeOf and friends.</summary>
    private NativeLayout NativeLayoutOfType(ITypeSymbol type, bool sizeOf)
    {
        type = S(type);
        var name = ModuleCodeGen.ClrName(type);
        var notMarshalable = new NativeLayoutException(true, $"Type '{name}' cannot be marshaled as an unmanaged structure; no meaningful size or offset can be computed.");
        // Marshal.SizeOf refuses an enum itself; reading or writing one through memory works (as its underlying type).
        if ((sizeOf && type.TypeKind == TypeKind.Enum) || type is IArrayTypeSymbol or IPointerTypeSymbol or ITypeParameterSymbol
            || type.SpecialType is SpecialType.System_String or SpecialType.System_Object or SpecialType.System_Void
            || type.TypeKind is TypeKind.Interface or TypeKind.Delegate)
            throw notMarshalable;
        var scalar = NativeScalarLayout(type, null, charSet: 2 /* Ansi: the default */, notMarshalable);
        if (scalar is not null) return scalar;
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct or TypeKind.Class } structure)
            throw notMarshalable;
        if (!_m.IsUserType(structure)) throw new NativeLayoutException(false, $"Marshal layout of library type '{name}'");
        return BuildNativeLayout(structure, notMarshalable);
    }

    /// <summary>A field or element of a primitive, native-int, enum, bool, char or opaque (decimal, Guid) type; null for anything else.</summary>
    private NativeLayout? NativeScalarLayout(ITypeSymbol type, IFieldSymbol? field, int charSet, NativeLayoutException notMarshalable)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } underlying }) type = underlying;
        NativeLayout Scalar(NativeKind kind, int size, int align = 0) => new(type, kind) { Size = size, Align = align == 0 ? size : align };
        int? unmanaged = field is null ? null : FieldMarshalFacts(field).Unmanaged;
        switch (type.SpecialType)
        {
            case SpecialType.System_Byte or SpecialType.System_SByte: return Scalar(NativeKind.Raw, 1);
            case SpecialType.System_Int16 or SpecialType.System_UInt16: return Scalar(NativeKind.Raw, 2);
            case SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single: return Scalar(NativeKind.Raw, 4);
            case SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double: return Scalar(NativeKind.Raw, 8);
            case SpecialType.System_IntPtr or SpecialType.System_UIntPtr: return Scalar(NativeKind.Raw, PointerBytes);
            case SpecialType.System_Boolean:
            {
                // UnmanagedType: Bool 2 and I4/U4 (7, 8) are BOOL; I1/U1 (3, 4) one byte. The marshaller refuses a
                // VariantBool field (0x25) in a structure, so a layout with one has no size.
                var size = unmanaged switch { null or 2 or 7 or 8 => 4, 3 or 4 => 1, 0x25 => throw notMarshalable, _ => throw new NativeLayoutException(false, $"[MarshalAs] on bool field '{field?.Name}'") };
                return new NativeLayout(type, NativeKind.Bool) { Size = size, Align = size, BoolSize = size };
            }
            case SpecialType.System_Char:
            {
                // CharSet: None 1 and Ansi 2 marshal one byte, Unicode 3 two; Auto (4) depends on the OS.
                var unicode = unmanaged switch { 5 or 6 => true, 3 or 4 => false, null => charSet == 3, _ => throw new NativeLayoutException(false, $"[MarshalAs] on char field '{field?.Name}'") };
                if (unmanaged is null && charSet == 4) throw new NativeLayoutException(false, "CharSet.Auto");
                return unicode ? Scalar(NativeKind.Raw, 2) : Scalar(NativeKind.Char, 1);
            }
        }
        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) return Scalar(NativeKind.Raw, PointerBytes);
        if (type is INamedTypeSymbol { Name: "Decimal" or "Guid", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } opaque)
        {
            var size = opaque.Name == "Guid" ? 16 : 16;
            if ((int)T.Target.SizeOf(T.Storage(type)) != size) throw new NativeLayoutException(false, $"Marshal layout of '{opaque.Name}'");
            return Scalar(NativeKind.Opaque, size, opaque.Name == "Guid" ? 4 : 8);
        }
        return null;
    }

    private NativeLayout BuildNativeLayout(INamedTypeSymbol type, NativeLayoutException notMarshalable)
    {
        // Structs default to sequential, classes to auto (not marshalable).
        var (kind, pack, size, charSet) = StructLayoutFacts(type);
        if (kind == 3) throw notMarshalable;
        if (type.TypeKind == TypeKind.Class && type.BaseType?.SpecialType != SpecialType.System_Object)
            throw new NativeLayoutException(false, $"Marshal layout of '{type.Name}', which derives from another class");
        var layout = new NativeLayout(type, NativeKind.Struct) { IsClass = type.TypeKind == TypeKind.Class };
        long end = 0;
        var align = 1;
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst))
        {
            if (field.IsFixedSizeBuffer) throw new NativeLayoutException(false, $"Marshal layout of fixed-size buffer '{field.Name}'");
            var fieldType = S(field.Type);
            var leaf = NativeScalarLayout(fieldType, field, charSet, notMarshalable);
            if (leaf is null)
            {
                if (fieldType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } nestedType || !_m.IsUserType(nestedType) )
                    throw new NativeLayoutException(false, $"Marshal layout of field '{field.Name}' of type '{fieldType}'");
                leaf = BuildNativeLayout(nestedType, notMarshalable);
            }
            var fieldAlign = pack == 0 ? leaf.Align : Math.Min(leaf.Align, pack);
            long offset;
            if (kind == 2)
            {
                if (FieldMarshalFacts(field).Offset is not { } declared)
                    throw new NativeLayoutException(false, $"explicit layout of '{type.Name}' without [FieldOffset] on '{field.Name}'");
                offset = declared;
            }
            else offset = (end + fieldAlign - 1) / fieldAlign * fieldAlign;
            layout.Fields.Add(new NativeField(field, (int)offset, leaf.Size, leaf.Kind, leaf.Kind == NativeKind.Struct ? leaf : null, leaf.BoolSize));
            end = Math.Max(end, offset + leaf.Size);
            align = Math.Max(align, fieldAlign);
        }
        layout.Align = align;
        layout.Size = (int)Math.Max(layout.Fields.Count == 0 ? 1 : (end + align - 1) / align * align, size);
        return layout;
    }

    // ======================================================================================
    // The call sites
    // ======================================================================================

    private LLVMValueRef? TryMarshalLayoutMethod(IMethodSymbol target, IInvocationOperation inv)
    {
        if (target.ContainingType is not { Name: "Marshal", ContainingNamespace: { Name: "InteropServices", ContainingNamespace.Name: "Runtime" } }) return null;
        if (target.Name is not ("SizeOf" or "OffsetOf" or "PtrToStructure" or "StructureToPtr" or "DestroyStructure")) return null;
        var arguments = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        var typeArgument = target.TypeArguments.Length == 1 ? S(target.TypeArguments[0]) : null;
        try
        {
            return target.Name switch
            {
                "SizeOf" => MarshalSizeOf(target, inv, arguments, typeArgument),
                "OffsetOf" => MarshalOffsetOf(inv, arguments, typeArgument),
                "PtrToStructure" => MarshalPtrToStructure(target, inv, arguments, typeArgument),
                "StructureToPtr" => MarshalStructureToPtr(inv, arguments, typeArgument),
                _ => MarshalDestroyStructure(inv, arguments, typeArgument),
            };
        }
        catch (NativeLayoutException e)
        {
            // The marshaller's own refusals of SizeOf/OffsetOf are ArgumentExceptions at run time; for the other
            // members its messages differ, so anything it would refuse there is not guessed at.
            if (e.ParamName is not null) ThrowMarshal("MarshalArgument", _m.StringLiteral(e.Message), _m.StringLiteral(e.ParamName));
            else if (e.NotMarshalable && target.Name is "SizeOf" or "OffsetOf") ThrowMarshal("MarshalNotMarshalable", _m.StringLiteral(e.Message));
            else throw CompileError.Unsupported($"'{target}': {(e.NotMarshalable ? "a type the marshaller refuses" : e.Message)}", inv);
            return target.ReturnsVoid ? default(LLVMValueRef) : LlvmContext.Null(T.Value(S(target.ReturnType)));
        }
    }

    /// <summary>The type an argument of type Type (typeof(X)) or object (the structure) stands for, when it is known statically.</summary>
    private ITypeSymbol? MarshalTypeOf(IOperation argument, bool structure)
    {
        if (!structure) return ExactTypeOf(argument) is { } typeOf ? S(typeOf) : null;
        var value = argument;
        while (value is IConversionOperation { Operand: var operand } conversion && !conversion.Conversion.IsUserDefined && !conversion.Conversion.IsNumeric && !conversion.Conversion.IsNullable)
            value = operand;
        var type = value is IlValueOperation { BoxedFrom: { } boxed } ? boxed.Type : value.Type;
        type = type is null ? null : S(type);
        return type is null || type.SpecialType == SpecialType.System_Object || type.TypeKind is TypeKind.Interface or TypeKind.TypeParameter
            || (!type.IsValueType && !type.IsSealed && type.TypeKind != TypeKind.Array) ? null : type;
    }

    private static bool IsNullConstant(IOperation operation) =>
        operation is IlValueOperation { IsNullLiteral: true } || operation.ConstantValue is { HasValue: true, Value: null };

    private LLVMValueRef MarshalSizeOf(IMethodSymbol target, IInvocationOperation inv, IArgumentOperation[] arguments, ITypeSymbol? typeArgument)
    {
        // SizeOf(Type), SizeOf<T>(), SizeOf(object), SizeOf<T>(T)
        var parameter = arguments.Length == 1 ? target.OriginalDefinition.Parameters[0].Type : null;
        var takesType = parameter is { Name: "Type", ContainingNamespace.Name: "System" };
        ITypeSymbol type;
        if (arguments.Length == 0)
        {
            type = typeArgument!;
            ThrowIfGeneric(type, "T");
        }
        else if (takesType)
        {
            if (IsNullConstant(arguments[0].Value))
            {
                ThrowMarshalNull("t");
                return L.ConstInt(L.I32, 0);
            }
            type = MarshalTypeOf(arguments[0].Value, structure: false) ?? throw CompileError.Unsupported("Marshal.SizeOf of a Type known only at run time", inv);
            ThrowIfGeneric(type, "t");
        }
        else
        {
            if (IsNullConstant(arguments[0].Value))
            {
                ThrowMarshalNull("structure");
                return L.ConstInt(L.I32, 0);
            }
            type = MarshalTypeOf(arguments[0].Value, structure: true) ?? (typeArgument is { } t && (t.IsValueType || t.IsSealed) ? t
                : throw CompileError.Unsupported("Marshal.SizeOf of a structure whose run-time type is not known statically", inv));
            var instance = EmitArgument(arguments[0], target);
            if (!type.IsValueType) ThrowMarshalIfNull(instance, "structure");
        }
        return L.ConstInt(L.I32, NativeLayoutOfType(type, sizeOf: true).Size);
    }

    private void ThrowMarshalIfNull(LLVMValueRef reference, string paramName)
    {
        var fail = NewBlock("null.arg");
        var ok = NewBlock("arg.ok");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, reference, LlvmContext.Null(L.Ptr)), fail, ok);
        _b.PositionAtEnd(fail);
        CallCoreLib("ThrowHelper", "MarshalArgumentNull", _m.StringLiteral(paramName));
        _b.Unreachable();
        _b.PositionAtEnd(ok);
    }

    private LLVMValueRef MarshalOffsetOf(IInvocationOperation inv, IArgumentOperation[] arguments, ITypeSymbol? typeArgument)
    {
        var nameArgument = arguments[^1];
        if (arguments.Length == 2 && IsNullConstant(arguments[0].Value))
        {
            ThrowMarshalNull("t");
            return LlvmContext.Null(T.NativeInt);
        }
        var type = arguments.Length == 2 ? MarshalTypeOf(arguments[0].Value, structure: false) : typeArgument;
        if (type is null) throw CompileError.Unsupported("Marshal.OffsetOf of a Type known only at run time", inv);
        var native = T.NativeInt;
        if (!IsNullConstant(nameArgument.Value) && nameArgument.Value.ConstantValue is not { HasValue: true, Value: string })
            throw CompileError.Unsupported("Marshal.OffsetOf of a field name known only at run time", inv);
        if (nameArgument.Value.ConstantValue.Value is not string fieldName)
        {
            ThrowMarshalNull("name");
            return LlvmContext.Null(native);
        }
        var layout = NativeLayoutOfType(type, sizeOf: true);
        if (layout.Fields.FirstOrDefault(f => f.Field.Name == fieldName) is not { } field)
        {
            ThrowMarshal("MarshalNotMember", _m.StringLiteral(Mangler.MetadataName((INamedTypeSymbol)layout.Type)));
            return LlvmContext.Null(native);
        }
        return L.ConstInt(native, field.Offset);
    }

    private void ThrowMarshalNull(string paramName) => ThrowMarshal("MarshalArgumentNull", _m.StringLiteral(paramName));

    /// <summary>Calls a corelib helper that always throws; the code that follows (the call's value) lands in an unreachable block.</summary>
    private void ThrowMarshal(string helper, params LLVMValueRef[] arguments)
    {
        CallCoreLib("ThrowHelper", helper, arguments);
        _b.Unreachable();
        _b.PositionAtEnd(NewBlock("after.throw"));
    }

    // PtrToStructure<T>(IntPtr) / PtrToStructure(IntPtr, Type) / PtrToStructure(IntPtr, object) / PtrToStructure<T>(IntPtr, T)
    private LLVMValueRef MarshalPtrToStructure(IMethodSymbol target, IInvocationOperation inv, IArgumentOperation[] arguments, ITypeSymbol? typeArgument)
    {
        var native = EmitArgument(arguments[0], target);
        var fills = arguments.Length == 2 && (typeArgument is not null ? arguments[1].Parameter!.Type.TypeKind == TypeKind.TypeParameter || SymbolEqualityComparer.Default.Equals(S(arguments[1].Parameter!.Type), typeArgument)
            : arguments[1].Parameter!.Type.SpecialType == SpecialType.System_Object);
        ITypeSymbol type;
        if (fills && IsNullConstant(arguments[1].Value)) { ThrowMarshalNull("structure"); return default; }
        if (arguments.Length == 1) type = typeArgument!;
        else if (fills) type = MarshalTypeOf(arguments[1].Value, structure: true) ?? typeArgument ?? throw CompileError.Unsupported("Marshal.PtrToStructure into an object whose run-time type is not known statically", inv);
        else
        {
            if (IsNullConstant(arguments[1].Value)) { ThrowMarshalNull("structureType"); return default; }
            type = MarshalTypeOf(arguments[1].Value, structure: false) ?? throw CompileError.Unsupported("Marshal.PtrToStructure of a Type known only at run time", inv);
        }
        if (arguments.Length == 1) ThrowIfGeneric(type, "T");
        else if (!fills) ThrowIfGeneric(type, "structureType");
        else if (type is INamedTypeSymbol { IsGenericType: true }) throw CompileError.Unsupported("Marshal.PtrToStructure into an instance of a generic class", inv);
        var layout = NativeLayoutOfType(type, sizeOf: false);
        RequireCopyable(layout, inv);
        var nativeAddress = _b.Cast(LLVMOpcode.IntToPtr, native, L.Ptr);
        var storage = T.Storage(type);
        var isNull = _b.ICmp(LLVMIntPredicate.EQ, nativeAddress, LlvmContext.Null(L.Ptr));

        if (fills)
        {
            // Fills an existing class instance: a null structure is an ArgumentNullException, a null pointer does nothing.
            if (type.IsValueType) throw CompileError.Unsupported("Marshal.PtrToStructure(IntPtr, object) into a value type", inv);
            var instance = EmitArgument(arguments[1], target);
            ThrowMarshalIfNull(instance, "structure");
            var body = NewBlock("fill");
            var done = NewBlock("fill.done");
            _b.CondBr(isNull, done, body);
            _b.PositionAtEnd(body);
            CopyNativeStructure(layout, nativeAddress, instance, toNative: false);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return default;
        }
        if (type.IsValueType)
        {
            // A null pointer yields null, which the generic form unboxes (NullReferenceException) and the Type form returns.
            var result = NewTemp(storage, "marshal.value");
            var valueTypeReturn = arguments.Length == 1;
            if (valueTypeReturn) NullCheckPointer(nativeAddress);
            var read = NewBlock("read");
            var after = NewBlock("read.done");
            var boxed = NewTemp(L.Ptr, "marshal.box");
            _b.Store(LlvmContext.Null(L.Ptr), boxed);
            if (valueTypeReturn) _b.Br(read); else _b.CondBr(isNull, after, read);
            _b.PositionAtEnd(read);
            _b.Store(LlvmContext.Null(storage), result);
            CopyNativeStructure(layout, nativeAddress, result, toNative: false);
            if (valueTypeReturn) { _b.Br(after); _b.PositionAtEnd(after); return LoadValue(result, type); }
            _b.Store(Box(LoadValue(result, type), type, inv), boxed);
            _b.Br(after);
            _b.PositionAtEnd(after);
            return _b.Load(L.Ptr, boxed);
        }
        else
        {
            // A class: a new instance (no constructor runs, as in the CLR) filled from the memory; null for a null pointer.
            var result = NewTemp(L.Ptr, "marshal.object");
            _b.Store(LlvmContext.Null(L.Ptr), result);
            var read = NewBlock("read");
            var after = NewBlock("read.done");
            _b.CondBr(isNull, after, read);
            _b.PositionAtEnd(read);
            var classLayout = _m.GetLayout((INamedTypeSymbol)type);
            var instance = Allocate(classLayout.MethodTable, L.ConstInt(L.I64, (long)classLayout.InstanceSize));
            CopyNativeStructure(layout, nativeAddress, instance, toNative: false);
            _b.Store(instance, result);
            _b.Br(after);
            _b.PositionAtEnd(after);
            return _b.Load(L.Ptr, result);
        }
    }

    private void NullCheckPointer(LLVMValueRef pointer) =>
        FailIf(_b.ICmp(LLVMIntPredicate.EQ, pointer, LlvmContext.Null(L.Ptr)), "csrt_throw_null_reference");

    private const string GenericTypeMessage = "The specified Type must not be a generic type.";

    /// <summary>The marshaller refuses a generic type given as a Type or type argument (not as an instance for SizeOf).</summary>
    private static void ThrowIfGeneric(ITypeSymbol type, string paramName, string message = GenericTypeMessage)
    {
        if (type is INamedTypeSymbol { IsGenericType: true }) throw new NativeLayoutException(false, message, paramName);
    }

    // StructureToPtr<T>(T, IntPtr, bool) / StructureToPtr(object, IntPtr, bool)
    private LLVMValueRef MarshalStructureToPtr(IInvocationOperation inv, IArgumentOperation[] arguments, ITypeSymbol? typeArgument)
    {
        var structureArgument = arguments[0];
        if (IsNullConstant(structureArgument.Value))
        {
            ThrowMarshalNull("structure");
            return default;
        }
        var type = MarshalTypeOf(structureArgument.Value, structure: true) ?? (typeArgument is { } t && (t.IsValueType || t.IsSealed) ? t
            : throw CompileError.Unsupported("Marshal.StructureToPtr of a structure whose run-time type is not known statically", inv));
        ThrowIfGeneric(type, "structure", "The specified object must not be an instance of a generic type.");
        var layout = NativeLayoutOfType(type, sizeOf: false);
        RequireCopyable(layout, inv);
        var target = S(inv.TargetMethod);
        var value = EmitArgument(structureArgument, target);
        var native = EmitArgument(arguments[1], target);
        EmitArgument(arguments[2], target);
        var address = _b.Cast(LLVMOpcode.IntToPtr, native, L.Ptr);
        LLVMValueRef source;
        if (type.IsValueType)
        {
            // The argument is the value itself (generic form) or a box of it (object form).
            var declared = TypeOf(structureArgument.Value);
            source = declared is { IsValueType: true } ? Spill(ToStorage(value, type)) : UnboxedAddress(value, type);
        }
        else
        {
            ThrowMarshalIfNull(value, "structure");
            source = value;
        }
        var failPtr = NewBlock("null.ptr");
        var ok = NewBlock("ptr.ok");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, address, LlvmContext.Null(L.Ptr)), failPtr, ok);
        _b.PositionAtEnd(failPtr);
        CallCoreLib("ThrowHelper", "MarshalArgumentNull", _m.StringLiteral("ptr"));
        _b.Unreachable();
        _b.PositionAtEnd(ok);
        CopyNativeStructure(layout, address, source, toNative: true);
        return default;
    }

    /// <summary>The address of the value inside a box (a null box is the NullReferenceException the unbox gives; the argument check reports it first for classes).</summary>
    private LLVMValueRef UnboxedAddress(LLVMValueRef box, ITypeSymbol type)
    {
        ThrowMarshalIfNull(box, "structure");
        return CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], box, MethodTableFor(type, null!));
    }

    // DestroyStructure<T>(IntPtr) / DestroyStructure(IntPtr, Type)
    private LLVMValueRef MarshalDestroyStructure(IInvocationOperation inv, IArgumentOperation[] arguments, ITypeSymbol? typeArgument)
    {
        if (arguments.Length == 2 && IsNullConstant(arguments[1].Value)) { ThrowMarshalNull("structuretype"); return default; }
        var type = arguments.Length == 2 ? MarshalTypeOf(arguments[1].Value, structure: false) : typeArgument;
        if (type is null) throw CompileError.Unsupported("Marshal.DestroyStructure of a Type known only at run time", inv);
        ThrowIfGeneric(type, "structuretype");
        var layout = NativeLayoutOfType(type, sizeOf: false);
        RequireCopyable(layout, inv);
        var target = S(inv.TargetMethod);
        var pointer = EmitArgument(arguments[0], target);
        for (var i = 1; i < arguments.Length; i++) EmitArgument(arguments[i], target);
        var fail = NewBlock("null.ptr");
        var ok = NewBlock("ptr.ok");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, pointer, LlvmContext.Null(pointer.Type)), fail, ok);
        _b.PositionAtEnd(fail);
        CallCoreLib("ThrowHelper", "MarshalArgumentNull", _m.StringLiteral("ptr"));
        _b.Unreachable();
        _b.PositionAtEnd(ok);
        // Nothing owned to release; the marshaller still clears the native bytes of a layout it converted.
        if (layout.Kind == NativeKind.Struct && !NativeLayoutIsBlittable(layout)) MemSet(_b.Cast(LLVMOpcode.IntToPtr, pointer, L.Ptr), L.ConstInt(L.I64, layout.Size));
        return default;
    }

    private void RequireCopyable(NativeLayout layout, IOperation at)
    {
        if (layout.Kind == NativeKind.Char) throw CompileError.Unsupported("marshalling a char as one ANSI byte (give the layout CharSet.Unicode)", at);
        foreach (var field in layout.Fields)
        {
            if (field.Kind == NativeKind.Char) throw CompileError.Unsupported($"marshalling char field '{field.Field.Name}' as one ANSI byte (give the layout CharSet.Unicode)", at);
            if (field.Nested is { } nested) RequireCopyable(nested, at);
        }
    }

    // ======================================================================================
    // Copying between the unmanaged layout and a managed struct or class instance
    // ======================================================================================

    /// <summary>The managed byte offset of a struct field, for the identical-layout fast path.</summary>
    private int? ManagedOffset(INamedTypeSymbol structType, IFieldSymbol field)
    {
        var layout = _m.GetStructLayout(structType);
        if (layout.ByteOffset is { } offsets && offsets.TryGetValue(field.OriginalDefinition, out var byteOffset)) return (int)byteOffset;
        if (!layout.FieldIndex.TryGetValue(field.OriginalDefinition, out var index)) return null;
        return (int)T.Target.OffsetOf(layout.LlvmType, index);
    }

    /// <summary>True when the unmanaged bytes of a struct are exactly its managed bytes.</summary>
    private bool NativeLayoutIsManaged(NativeLayout layout)
    {
        if (layout.IsClass || layout.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } structType) return layout.Kind is NativeKind.Raw or NativeKind.Opaque;
        if ((int)T.Target.SizeOf(T.Storage(structType)) != layout.Size) return false;
        foreach (var field in layout.Fields)
        {
            if (field.Kind is not (NativeKind.Raw or NativeKind.Opaque or NativeKind.Struct)) return false;
            if (ManagedOffset(structType, field.Field) != field.Offset) return false;
            if (field.Nested is { } nested && !NativeLayoutIsManaged(nested)) return false;
        }
        return true;
    }

    /// <summary>True when marshalling the type moves no converted fields (no bool or char): the CLR copies such a layout as it is.</summary>
    private static bool NativeLayoutIsBlittable(NativeLayout layout) =>
        layout.Kind is NativeKind.Raw or NativeKind.Opaque
        || (layout.Kind == NativeKind.Struct && layout.Fields.All(f => f.Kind is NativeKind.Raw or NativeKind.Opaque || (f.Nested is { } nested && NativeLayoutIsBlittable(nested))));

    /// <summary>True when the fields tile the struct exactly: no padding and no overlap, so its bytes are its fields' bytes.</summary>
    private static bool NativeLayoutIsContiguous(NativeLayout layout)
    {
        var end = 0;
        foreach (var field in layout.Fields.OrderBy(f => f.Offset))
        {
            if (field.Offset != end) return false;
            end += field.Size;
        }
        return end == layout.Size;
    }

    /// <summary>One block copy does it: the unmanaged bytes are the managed ones (writing also needs no padding, which a managed copy leaves undefined).</summary>
    private bool CopiesAsBlock(NativeLayout layout, bool toNative) =>
        layout.Kind == NativeKind.Struct && NativeLayoutIsManaged(layout) && (!toNative || NativeLayoutIsContiguous(layout));

    /// <summary>
    /// Copies a whole structure: <paramref name="managed"/> is the address of a struct or the class instance. Fields
    /// are written into zeroed bytes, so padding is zero as in a freshly made structure (the CLR clears a layout it
    /// converts first, and copies the managed padding of a blittable one, which is zero in a new struct).
    /// </summary>
    private void CopyNativeStructure(NativeLayout layout, LLVMValueRef native, LLVMValueRef managed, bool toNative)
    {
        if (toNative && layout.Kind == NativeKind.Struct && !CopiesAsBlock(layout, toNative)) MemSet(native, L.ConstInt(L.I64, layout.Size));
        CopyNativeFields(layout, native, managed, toNative);
    }

    private void CopyNativeFields(NativeLayout layout, LLVMValueRef native, LLVMValueRef managed, bool toNative)
    {
        if (layout.Kind != NativeKind.Struct)
        {
            CopyNativeScalar(layout.Kind, layout.Size, layout.BoolSize, layout.Type, native, managed, toNative);
            return;
        }
        if (CopiesAsBlock(layout, toNative))
        {
            var bytes = L.ConstInt(L.I64, layout.Size);
            if (toNative) MemMove(native, managed, bytes, noReferences: true); else MemMove(managed, native, bytes, noReferences: true);
            return;
        }
        foreach (var field in layout.Fields)
        {
            var managedField = FieldAddressOn(managed, field.Field);
            var nativeField = field.Offset == 0 ? native : _b.Gep(L.I8, native, [L.ConstInt(L.I64, field.Offset)]);
            if (field.Nested is { } nested) CopyNativeFields(nested, nativeField, managedField, toNative);
            else CopyNativeScalar(field.Kind, field.Size, field.BoolSize, S(field.Field.Type), nativeField, managedField, toNative);
        }
    }

    private void CopyNativeScalar(NativeKind kind, int size, int boolSize, ITypeSymbol type, LLVMValueRef native, LLVMValueRef managed, bool toNative)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } underlying }) type = underlying;
        if (kind == NativeKind.Bool)
        {
            var nativeType = L.IntTypeOf(size * 8);
            if (toNative)
            {
                var flag = _b.ICmp(LLVMIntPredicate.NE, _b.Load(L.I8, managed), L.ConstInt(L.I8, 0));
                Native.LLVMSetAlignment(_b.Store(_b.Cast(LLVMOpcode.ZExt, flag, nativeType), native), 1);
            }
            else
            {
                var raw = _b.Load(nativeType, native);
                Native.LLVMSetAlignment(raw, 1);
                _b.Store(_b.Cast(LLVMOpcode.ZExt, _b.ICmp(LLVMIntPredicate.NE, raw, L.ConstInt(nativeType, 0)), L.I8), managed);
            }
            return;
        }
        var storage = T.Storage(type);
        if (toNative) UnsafeStore(native, _b.Load(storage, managed), type, unaligned: true);
        else _b.Store(UnsafeLoadRaw(native, storage), managed);
    }

    private LLVMValueRef UnsafeLoadRaw(LLVMValueRef address, LLVMTypeRef storage)
    {
        var load = _b.Load(storage, address);
        Native.LLVMSetAlignment(load, 1);
        return load;
    }
}
