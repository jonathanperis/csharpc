using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

public sealed partial class FunctionCodeGen
{
    // ======================================================================================
    // Calls
    // ======================================================================================

    private LLVMValueRef EmitInvocation(IInvocationOperation inv)
    {
        var target = S(inv.TargetMethod);
        // `T.Create()` on a static abstract/virtual interface member: after substitution T is concrete.
        if (ResolveStaticInterfaceMember(target, inv.ConstrainedToType) is { } staticImpl)
            return EmitUserCall(staticImpl, default, true, inv.Arguments, isVirtual: false);
        if (target.MethodKind == MethodKind.DelegateInvoke) return EmitDelegateInvoke(inv);
        if (target.MethodKind == MethodKind.LocalFunction) return CallLocalFunction(inv);
        if (TypeMapper.IsNullable(target.ContainingType)) return EmitNullableMethod(inv);
        // Span enumerators are ref structs with no state to dispose (the foreach finally calls IDisposable.Dispose).
        if (target.Name == "Dispose" && TypeMapper.IsSpanEnumerator(TypeOf(inv.Instance))) return default;
        if (!target.IsStatic && inv.Instance is { } receiver)
        {
            var receiverType = TypeOf(receiver);
            // Interface members (user or library, e.g. IComparable<T>.CompareTo): devirtualize or dispatch.
            if (target.ContainingType.TypeKind == TypeKind.Interface && !IsArrayEnumeration(receiver))
                return EmitInterfaceCall(target, receiver, inv.Arguments, [], inv);
            // object.ToString/Equals/GetHashCode on a value-type receiver (a substituted T): box, then dispatch.
            if (receiverType is { IsValueType: true } && target.ContainingType.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
            {
                if (target.Name == "ToString" && FormatPrimitive(EmitValue(receiver), receiverType) is { } formatted) return formatted;
                var boxed = Box(EmitValue(receiver), receiverType, inv);
                var extra = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(EmitArgument);
                return VirtualCall(target, boxed, [boxed, .. extra]);
            }
        }
        if (_m.IsUserMethod(target))
        {
            var self = ModuleCodeGen.HasThis(target) || ModuleCodeGen.HasExtensionReceiver(target) ? Receiver(inv.Instance!, target) : default;
            var isBaseCall = inv.Instance?.Syntax is BaseExpressionSyntax;
            return EmitUserCall(target, self, ReceiverIsNonNull(inv.Instance, target), inv.Arguments, inv.IsVirtual && !isBaseCall);
        }
        if (TrySpanMethod(inv, out var spanResult)) return spanResult;
        if (Intrinsics.TryGet(target, out var handler)) return handler(this, inv);
        if (TryEmitLibraryExceptionCall(inv, out var result)) return result;
        throw CompileError.MissingIntrinsic(target, inv);
    }

    /// <summary>Static abstract/virtual interface member + constrained type → the concrete implementation.</summary>
    internal IMethodSymbol? ResolveStaticInterfaceMember(IMethodSymbol target, ITypeSymbol? constrainedTo)
    {
        if (!target.IsStatic || target.ContainingType.TypeKind != TypeKind.Interface || constrainedTo is null) return null;
        var concrete = S(constrainedTo);
        if (concrete is ITypeParameterSymbol || !_m.IsUserType(concrete)) return null; // primitives: generic-math paths
        return concrete.FindImplementationForInterfaceMember(target) as IMethodSymbol
            ?? (target.IsAbstract ? throw CompileError.Unsupported($"no implementation of '{target}' on '{concrete}'", target) : target);
    }

    /// <summary>
    /// Calls a method compiled by csharpc. Arguments are evaluated in source (evaluation) order and
    /// then placed by parameter ordinal, which handles named/out-of-order arguments.
    /// </summary>
    internal LLVMValueRef EmitUserCall(IMethodSymbol target, LLVMValueRef self, bool selfIsNonNull,
        ImmutableArray<IArgumentOperation> arguments, bool isVirtual, LLVMValueRef[]? extraArgs = null)
    {
        var hasThis = ModuleCodeGen.HasThis(target);
        var offset = hasThis || ModuleCodeGen.HasExtensionReceiver(target) ? 1 : 0;
        var args = new LLVMValueRef[offset + target.Parameters.Length];
        if (offset == 1)
        {
            if (hasThis && !selfIsNonNull) NullCheck(self); // extension receivers may legitimately be null
            args[0] = self;
        }
        foreach (var argument in arguments) args[offset + argument.Parameter!.Ordinal] = EmitArgument(argument, target);
        if (extraArgs is not null)
            for (var i = 0; i < extraArgs.Length; i++) args[args.Length - extraArgs.Length + i] = extraArgs[i];

        var dispatch = isVirtual && hasThis && (target.IsVirtual || target.IsAbstract || target.IsOverride)
            && !target.IsSealed && !target.ContainingType.IsSealed;
        if (dispatch && _m.Devirtualize(target) is { } only) return DirectCall(only, args); // closed-world CHA
        return dispatch ? VirtualCall(target, self, args) : DirectCall(target, args);
    }

    private LLVMValueRef DirectCall(IMethodSymbol target, LLVMValueRef[] args)
    {
        var f = _m.GetFunction(target);
        return _b.Call(f.Type, f.Fn, args);
    }

    /// <summary>Static call to a user-defined operator/conversion with operand expressions as arguments.</summary>
    private LLVMValueRef EmitDirectCall(IMethodSymbol target, IOperation[] operands)
    {
        var args = operands.Select((o, i) => Coerce(EmitValue(o), TypeOf(o), target.Parameters[i].Type)).ToArray();
        return DirectCall(target, args);
    }

    private LLVMValueRef EmitArgument(IArgumentOperation argument) => EmitArgument(argument, null);

    /// <summary>Evaluates an argument and converts it to the (substituted) parameter type of <paramref name="target"/>.</summary>
    private LLVMValueRef EmitArgument(IArgumentOperation argument, IMethodSymbol? target)
    {
        var parameter = argument.Parameter!;
        if (parameter.RefKind is RefKind.In or RefKind.RefReadOnlyParameter) return AddressOrSpill(argument.Value);
        if (parameter.RefKind != RefKind.None) return AddressOf(argument.Value);
        var parameterType = target is not null && parameter.Ordinal < target.Parameters.Length
            ? target.Parameters[parameter.Ordinal].Type
            : S(parameter.Type);
        return Coerce(EmitValue(argument.Value), TypeOf(argument.Value), parameterType);
    }

    /// <summary>
    /// mt = obj->mt; fn = mt->vtable[slot]; call fn(obj, ...). The vtable sits right after the
    /// 24-byte method-table header (docs/03-lowering.md#virtual-dispatch).
    /// </summary>
    internal LLVMValueRef VirtualCall(IMethodSymbol target, LLVMValueRef self, LLVMValueRef[] args)
    {
        var root = ClassLayout.RootOf(target);
        int slot;
        LLVMTypeRef fnType;
        if (_m.IsUserType(target.ContainingType) || _m.IsLibraryException(target.ContainingType))
        {
            var layout = _m.GetLayout(target.ContainingType);
            slot = layout.SlotOf.TryGetValue(root, out var s) ? s : throw CompileError.Unsupported($"virtual call to '{target}'", target);
            fnType = _m.FunctionTypeOf(target);
        }
        else
        {
            slot = root.Name switch
            {
                "ToString" => ClassLayout.ToStringSlot,
                "Equals" => ClassLayout.EqualsSlot,
                "GetHashCode" => ClassLayout.GetHashCodeSlot,
                _ => throw CompileError.Unsupported($"virtual call to library method '{target}'", target),
            };
            fnType = slot switch
            {
                ClassLayout.EqualsSlot => L.FunctionType(L.I1, L.Ptr, L.Ptr),
                ClassLayout.GetHashCodeSlot => L.FunctionType(L.I32, L.Ptr),
                _ => L.FunctionType(L.Ptr, L.Ptr),
            };
        }
        var mt = _b.Load(L.Ptr, self, "mt");
        var offset = T.Target.SizeOf(T.MethodTableHeader) + (ulong)slot * T.PointerSize;
        var slotAddress = _b.InBoundsGep(L.I8, mt, [L.ConstUInt(L.I64, offset)], "slot");
        var fn = _b.Load(L.Ptr, slotAddress, "vfn");
        return _b.Call(fnType, fn, args);
    }

    // ======================================================================================
    // Objects
    // ======================================================================================

    private LLVMValueRef EmitObjectCreation(IObjectCreationOperation creation)
    {
        if (creation.Initializer is not null) throw CompileError.Unsupported("object initializer", creation);
        var type = (INamedTypeSymbol)TypeOf(creation)!;
        if (TrySpanConstruction(creation, out var span) || TryStatelessLibraryCreation(creation, out span)) return span;
        if (type.IsValueType) return EmitValueTypeCreation(creation, type);
        if (type.SpecialType == SpecialType.System_Object)
            return Allocate(_m.ExternalGlobal("csrt_Object_MT"), L.ConstInt(L.I64, (long)T.PointerSize));
        if (_m.IsLibraryException(type)) return EmitLibraryExceptionCreation(creation);
        if (!_m.IsUserType(type) && creation.Constructor is { } libraryCtor && Intrinsics.TryGetConstructor(libraryCtor, out var ctorHandler))
            return ctorHandler(this, creation);
        if (!_m.IsUserType(type) || type.TypeKind != TypeKind.Class)
            throw CompileError.Unsupported($"creating instances of '{type}'", creation);
        if (type.IsAbstract) throw CompileError.Unsupported("instantiating an abstract class", creation);

        var layout = _m.GetLayout(type);
        var obj = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
        EmitUserCall(S(creation.Constructor!), obj, selfIsNonNull: true, creation.Arguments, isVirtual: false);
        return obj;
    }

    /// <summary>`new T()` — after substitution T is a concrete class with a parameterless constructor.</summary>
    private LLVMValueRef EmitNewT(ITypeParameterObjectCreationOperation creation)
    {
        var type = TypeOf(creation)!;
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named || !_m.IsUserType(named))
            throw CompileError.Unsupported($"new T() with T = '{type}'", creation);
        var layout = _m.GetLayout(named);
        var obj = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
        var ctor = named.InstanceConstructors.First(c => c.Parameters.IsEmpty);
        DirectCall(ctor, [obj]);
        return obj;
    }

    private LLVMValueRef Allocate(LLVMValueRef methodTable, LLVMValueRef size) =>
        CallRuntime("csrt_alloc", L.Ptr, [L.Ptr, L.I64], methodTable, size);

    internal LLVMValueRef ObjectToString(LLVMValueRef obj) =>
        CallRuntime("csrt_object_to_string", L.Ptr, [L.Ptr], obj);

    /// <summary>`obj is T` for classes: walk the method-table base chain in the runtime.</summary>
    private LLVMValueRef IsInstance(LLVMValueRef value, ITypeSymbol? from, ITypeSymbol target, IOperation at)
    {
        if (from is not null)
        {
            var conversion = _m.Compilation.ClassifyCommonConversion(from, target);
            if (conversion.IsIdentity && from.IsValueType) return L.True;
            if (from.IsReferenceType && conversion is { IsImplicit: true, IsReference: true } or { IsIdentity: true })
                return _b.ICmp(LLVMIntPredicate.NE, value, LlvmContext.Null(L.Ptr));
        }
        var mt = MethodTableFor(target, at);
        var result = CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], value, mt);
        return _b.ICmp(LLVMIntPredicate.NE, result, LlvmContext.Null(L.Ptr));
    }

    private LLVMValueRef CastClass(LLVMValueRef value, ITypeSymbol target, IOperation at) =>
        CallRuntime("csrt_castclass", L.Ptr, [L.Ptr, L.Ptr], value, MethodTableFor(target, at));

    /// <summary>Boxes a primitive: { mt = csrt_Boxed_&lt;T&gt;_MT, payload @ offset 8 }.</summary>
    internal LLVMValueRef Box(LLVMValueRef value, ITypeSymbol type, IOperation at)
    {
        if (TypeMapper.IsNullable(type))
        {
            // Boxing T?: null becomes a null reference, otherwise a boxed T.
            var (has, payload, underlying) = NullableParts(value, type);
            var result = NewTemp(L.Ptr, "box.nullable");
            _b.Store(LlvmContext.Null(L.Ptr), result);
            var boxBlock = NewBlock("box");
            var done = NewBlock("box.done");
            _b.CondBr(has, boxBlock, done);
            _b.PositionAtEnd(boxBlock);
            _b.Store(Box(payload, underlying, at), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(L.Ptr, result);
        }
        var mt = MethodTableFor(type, at);
        var storage = T.Storage(type);
        var box = Allocate(mt, L.ConstInt(L.I64, 8 + (long)T.Target.SizeOf(storage)));
        StoreValue(ToStorage(value, type), _b.InBoundsGep(L.I8, box, [L.ConstInt(L.I64, 8)]));
        return box;
    }

    private static bool IsBoxablePrimitive(ITypeSymbol type) => type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64
        or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Double or SpecialType.System_Single
        or SpecialType.System_Boolean or SpecialType.System_Char;

    private LLVMValueRef MethodTableFor(ITypeSymbol type, IOperation at) => type switch
    {
        { SpecialType: SpecialType.System_String } => _m.ExternalGlobal("csrt_String_MT"),
        { SpecialType: SpecialType.System_Object } => _m.ExternalGlobal("csrt_Object_MT"),
        _ when IsBoxablePrimitive(type) => _m.ExternalGlobal($"csrt_Boxed_{type.MetadataName}_MT"),
        { TypeKind: TypeKind.Enum } => throw CompileError.Unsupported($"boxing enum '{type}'", at),
        INamedTypeSymbol { TypeKind: TypeKind.Class } named when _m.IsUserType(named) || _m.IsLibraryException(named) =>
            _m.GetLayout(named).MethodTable,
        INamedTypeSymbol { TypeKind: TypeKind.Struct } named when _m.IsUserType(named) => _m.StructMethodTable(named),
        INamedTypeSymbol { TypeKind: TypeKind.Interface } itf => _m.InterfaceMethodTable(itf),
        _ => throw CompileError.Unsupported($"type test against '{type}'", at),
    };

    private LLVMValueRef EmitIsType(IIsTypeOperation op)
    {
        var test = IsInstance(EmitValue(op.ValueOperand), TypeOf(op.ValueOperand), S(op.TypeOperand), op);
        return op.IsNegated ? _b.Not(test) : test;
    }

    // ======================================================================================
    // Properties
    // ======================================================================================

    private LLVMValueRef EmitPropertyGet(IPropertyReferenceOperation p)
    {
        var property = S(p.Property);
        if (TypeMapper.IsNullable(property.ContainingType)) return EmitNullableMember(p);
        if (property.ContainingType.IsAnonymousType) return AnonymousPropertyGet(p);
        // Generic math constants (T.Zero, T.One...) on a substituted primitive T.
        if (property is { IsStatic: true, ContainingType.TypeKind: TypeKind.Interface } && TypeOf(p) is { SpecialType: not SpecialType.None } numeric
            && property.Name is "Zero" or "One" or "AdditiveIdentity" or "MultiplicativeIdentity")
            return Constant(property.Name is "Zero" or "AdditiveIdentity" ? 0 : 1, _m.Compilation.GetSpecialType(SpecialType.System_Int32)) is var c
                ? ConvertNumeric(c, _m.Compilation.GetSpecialType(SpecialType.System_Int32), numeric, p) : default;
        if (property.ContainingType.TypeKind == TypeKind.Interface && !property.IsStatic && !IsArrayEnumeration(p.Instance!))
            return EmitInterfaceCall(property.GetMethod!, p.Instance!, p.Arguments, [], null);
        if (_m.IsUserType(property.ContainingType))
        {
            var getter = property.GetMethod ?? throw CompileError.Unsupported($"reading set-only property '{property.Name}'", p);
            var self = property.IsStatic ? default : Receiver(p.Instance!, getter);
            return CallAccessor(p, getter, self, []);
        }
        if (TrySpanProperty(p, out var spanResult)) return spanResult;
        if (Intrinsics.TryGetProperty(property, out var handler)) return handler(this, p);
        if (TryEmitLibraryExceptionProperty(p, out var exceptionMember)) return exceptionMember;
        throw CompileError.Unsupported($"'{property.ContainingType}.{property.Name}' has no implementation in the csharpc runtime yet", p);
    }

    private LLVMValueRef EmitPropertySet(IPropertyReferenceOperation p, Func<LLVMValueRef> value, ITypeSymbol? valueType)
    {
        var property = S(p.Property);
        if (property.ContainingType.TypeKind == TypeKind.Interface && !property.IsStatic)
        {
            var assigned = Coerce(value(), valueType, property.Type);
            EmitInterfaceCall(property.SetMethod!, p.Instance!, p.Arguments, [assigned], null);
            return assigned;
        }
        if (!_m.IsUserType(property.ContainingType))
            throw CompileError.Unsupported($"setting '{property.ContainingType}.{property.Name}'", p);
        var self = property.IsStatic ? default
            : property.ContainingType.IsValueType ? AddressOrSpill(p.Instance!) : EmitValue(p.Instance!);
        var v = Coerce(value(), valueType, property.Type);

        if (property.SetMethod is null)
        {
            // Get-only auto-property assigned in a constructor: write the backing field directly.
            var field = BackingField(property);
            var address = field.IsStatic ? _m.StaticField(field) : FieldAddressOn(self, field);
            StoreValue(ToStorage(v, property.Type), address);
            return v;
        }
        CallAccessor(p, property.SetMethod, self, [v]);
        return v;
    }

    private LLVMValueRef CallAccessor(IPropertyReferenceOperation p, IMethodSymbol accessor, LLVMValueRef self, LLVMValueRef[] extra)
    {
        var isBase = p.Instance?.Syntax is BaseExpressionSyntax;
        return EmitUserCall(accessor, self, ReceiverIsNonNull(p.Instance, accessor), p.Arguments, isVirtual: !isBase, extra);
    }

    // ======================================================================================
    // Arrays:  { mt, i64 length, [0 x T] }
    // ======================================================================================

    private LLVMValueRef EmitArrayCreation(IArrayCreationOperation creation)
    {
        var arrayType = (IArrayTypeSymbol)TypeOf(creation)!;
        if (arrayType.Rank != 1) return EmitMdArrayCreation(creation, arrayType);
        var length = ToIndex(EmitValue(creation.DimensionSizes[0]), TypeOf(creation.DimensionSizes[0])!);
        var array = NewArray(arrayType.ElementType, length);
        if (creation.Initializer is { } init)
            StoreElements(array, arrayType.ElementType, init.ElementValues);
        return array;
    }

    private LLVMValueRef EmitCollectionExpression(ICollectionExpressionOperation collection)
    {
        if (TypeMapper.IsSpan(TypeOf(collection))) return SpanFromCollection(collection, SpanElement(TypeOf(collection)!));
        if (TypeOf(collection) is not IArrayTypeSymbol { Rank: 1 } arrayType)
            throw CompileError.Unsupported($"collection expression of type '{collection.Type}'", collection);
        if (collection.Elements.Any(e => e is ISpreadOperation)) throw CompileError.Unsupported("spread elements", collection);
        var array = NewArray(arrayType.ElementType, L.ConstInt(L.I64, collection.Elements.Length));
        StoreElements(array, arrayType.ElementType, collection.Elements);
        return array;
    }

    private LLVMValueRef NewArray(ITypeSymbol elementType, LLVMValueRef length)
    {
        var elementSize = T.Target.SizeOf(T.Storage(elementType));
        // Arrays of primitives/enums hold no references: a distinct method table lets the GC skip them.
        var helper = Optimizations.HoldsNoReferences(elementType) ? "csrt_new_array_primitive" : "csrt_new_array";
        return CallRuntime(helper, L.Ptr, [L.I64, L.I64], L.ConstInt(L.I64, (long)elementSize), length);
    }

    private void StoreElements(LLVMValueRef array, ITypeSymbol elementType, ImmutableArray<IOperation> values)
    {
        var storage = T.Storage(elementType);
        for (var i = 0; i < values.Length; i++)
        {
            var address = _b.InBoundsGep(T.ArrayHeader(storage), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, i)]);
            StoreValue(ToStorage(Coerce(EmitValue(values[i]), TypeOf(values[i]), elementType), elementType), address);
        }
    }

    private LLVMValueRef ToIndex(LLVMValueRef value, ITypeSymbol type) =>
        value.Type.IntWidth == 64 ? value : _b.Cast(TypeMapper.IsSigned(type) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, L.I64);

    private LLVMValueRef ElementAddress(IArrayElementReferenceOperation element)
    {
        if (element.Indices.Length != 1) return MdElementAddress(element, (IArrayTypeSymbol)TypeOf(element.ArrayReference)!);
        var index = element.Indices[0];
        var indexType = TypeOf(index);
        var isIndex = IsIndexType(indexType); // array[^n]
        if (!isIndex && indexType?.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_UInt32 or SpecialType.System_UInt64))
            throw CompileError.Unsupported($"array index of type '{indexType}'", element);
        var array = EmitValue(element.ArrayReference);
        var i = isIndex
            ? _b.Cast(LLVMOpcode.SExt, ArrayIndexFromIndex(array, EmitValue(index)), L.I64)
            : ToIndex(EmitValue(index), indexType!);
        NullCheck(array);
        var storage = T.Storage(TypeOf(element)!);
        var header = T.ArrayHeader(storage);
        var length = _b.Load(L.I64, _b.StructGep(header, array, 1), "len");
        // One unsigned compare also catches negative indices.
        FailIf(_b.ICmp(LLVMIntPredicate.UGE, i, length), "csrt_throw_index_out_of_range");
        return _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), i]);
    }

    // foreach over T[]: Roslyn's CFG lowers it to the IEnumerable<T> pattern. We substitute a
    // stack-allocated { T[] array, i64 index } "enumerator"; after SROA this is a plain index loop.
    private LLVMTypeRef ArrayEnumeratorType => L.Struct(L.Ptr, L.I64, L.I64); // { data, index, length }

    internal LLVMValueRef ArrayGetEnumerator(IOperation instance, IOperation at)
    {
        var source = instance;
        while (source is IConversionOperation { Conversion.IsImplicit: true } c) source = c.Operand;
        if (TypeOf(source) is not IArrayTypeSymbol arrayType)
            throw CompileError.Unsupported($"foreach over '{TypeOf(source)}' (only arrays so far)", at);
        var array = EmitValue(source);
        NullCheck(array);
        var enumerator = NewTemp(ArrayEnumeratorType, "array.enumerator");
        _b.Store(ArrayDataStart(array, arrayType), _b.StructGep(ArrayEnumeratorType, enumerator, 0));
        _b.Store(L.ConstInt(L.I64, -1), _b.StructGep(ArrayEnumeratorType, enumerator, 1));
        _b.Store(_b.Load(L.I64, _b.StructGep(T.ArrayHeader(L.I8), array, 1), "len"), _b.StructGep(ArrayEnumeratorType, enumerator, 2));
        return enumerator;
    }

    internal LLVMValueRef ArrayEnumeratorMoveNext(LLVMValueRef enumerator)
    {
        var indexAddress = _b.StructGep(ArrayEnumeratorType, enumerator, 1);
        var index = _b.BinOp(LLVMOpcode.Add, _b.Load(L.I64, indexAddress), L.ConstInt(L.I64, 1));
        _b.Store(index, indexAddress);
        var length = _b.Load(L.I64, _b.StructGep(ArrayEnumeratorType, enumerator, 2), "len");
        return _b.ICmp(LLVMIntPredicate.SLT, index, length);
    }

    internal LLVMValueRef ArrayEnumeratorCurrent(LLVMValueRef enumerator, ITypeSymbol elementType)
    {
        var data = _b.Load(L.Ptr, _b.StructGep(ArrayEnumeratorType, enumerator, 0));
        var index = _b.Load(L.I64, _b.StructGep(ArrayEnumeratorType, enumerator, 1));
        var address = _b.InBoundsGep(T.Storage(elementType), data, [index]);
        return LoadValue(address, elementType);
    }

    // foreach over string: string.GetEnumerator() -> CharEnumerator (pattern-based). Same trick:
    // a stack { string, i32 index } that SROA turns into an index loop.
    private LLVMTypeRef StringEnumeratorType => L.Struct(L.Ptr, L.I32);

    internal LLVMValueRef StringGetEnumerator(LLVMValueRef str)
    {
        NullCheck(str);
        var e = NewTemp(StringEnumeratorType, "string.enumerator");
        _b.Store(str, _b.StructGep(StringEnumeratorType, e, 0));
        _b.Store(L.ConstInt(L.I32, -1), _b.StructGep(StringEnumeratorType, e, 1));
        return e;
    }

    internal LLVMValueRef StringEnumeratorMoveNext(LLVMValueRef e)
    {
        var indexAddress = _b.StructGep(StringEnumeratorType, e, 1);
        var index = _b.BinOp(LLVMOpcode.Add, _b.Load(L.I32, indexAddress), L.ConstInt(L.I32, 1));
        _b.Store(index, indexAddress);
        var str = _b.Load(L.Ptr, _b.StructGep(StringEnumeratorType, e, 0));
        var length = _b.Load(L.I32, _b.StructGep(T.StringHeader, str, 1), "strlen");
        return _b.ICmp(LLVMIntPredicate.SLT, index, length);
    }

    internal LLVMValueRef StringEnumeratorCurrent(LLVMValueRef e)
    {
        var str = _b.Load(L.Ptr, _b.StructGep(StringEnumeratorType, e, 0));
        var index = _b.Load(L.I32, _b.StructGep(StringEnumeratorType, e, 1));
        var address = _b.InBoundsGep(T.StringHeader, str, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), index]);
        return _b.Load(L.I16, address);
    }

    internal LLVMValueRef ArrayLength(LLVMValueRef array)
    {
        NullCheck(array);
        return _b.Load(L.I64, _b.StructGep(T.ArrayHeader(L.I8), array, 1), "len");
    }

    // ======================================================================================
    // Strings
    // ======================================================================================

    internal LLVMValueRef ConcatStrings(LLVMValueRef a, LLVMValueRef b) =>
        CallRuntime("csrt_string_concat", L.Ptr, [L.Ptr, L.Ptr], a, b);

    /// <summary>Strips conversions to object (boxing) so the value can be formatted by its real type.</summary>
    internal static IOperation Unbox(IOperation op)
    {
        while (op is IConversionOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_Object or SpecialType.System_String } c
               && c.Operand.Type is { } from && (from.IsValueType || c.Conversion.IsReference || c.Conversion.IsIdentity))
            op = c.Operand;
        return op;
    }

    /// <summary>The string a value formats to in string concatenation / interpolation / Console.Write.</summary>
    internal LLVMValueRef ToStringValue(IOperation op)
    {
        op = Unbox(op);
        if (op.ConstantValue is { HasValue: true, Value: var constant } && TypeOf(op)?.TypeKind != TypeKind.Enum)
        {
            // Fold constants at compile time, using .NET's own formatting.
            return _m.StringLiteral(constant switch
            {
                null => "",
                bool b => b ? "True" : "False",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => constant.ToString() ?? "",
            });
        }
        var type = TypeOf(op);
        if (type is null) return _m.StringLiteral("");
        if (type.SpecialType == SpecialType.System_String) return EmitValue(op);
        var value = EmitValue(op);
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var formatter = _m.EnumFormatter(enumType);
            return _b.Call(formatter.Type, formatter.Fn, [value]);
        }
        return FormatValue(value, type, op);
    }

    internal LLVMValueRef? FormatPrimitive(LLVMValueRef value, ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 => CallRuntime("csrt_string_from_i32", L.Ptr, [L.I32], value),
        SpecialType.System_Int64 => CallRuntime("csrt_string_from_i64", L.Ptr, [L.I64], value),
        SpecialType.System_UInt32 => CallRuntime("csrt_string_from_u32", L.Ptr, [L.I32], value),
        SpecialType.System_UInt64 => CallRuntime("csrt_string_from_u64", L.Ptr, [L.I64], value),
        SpecialType.System_SByte or SpecialType.System_Int16 =>
            CallRuntime("csrt_string_from_i32", L.Ptr, [L.I32], _b.Cast(LLVMOpcode.SExt, value, L.I32)),
        SpecialType.System_Byte or SpecialType.System_UInt16 =>
            CallRuntime("csrt_string_from_i32", L.Ptr, [L.I32], _b.Cast(LLVMOpcode.ZExt, value, L.I32)),
        SpecialType.System_Double => CallRuntime("csrt_string_from_f64", L.Ptr, [L.F64], value),
        SpecialType.System_Single => CallRuntime("csrt_string_from_f32", L.Ptr, [L.F32], value),
        SpecialType.System_Boolean => CallRuntime("csrt_string_from_bool", L.Ptr, [L.I1], value),
        SpecialType.System_Char => CallRuntime("csrt_string_from_char", L.Ptr, [L.I16], value),
        _ => null,
    };

    /// <summary>`{value,alignment:format}` in an interpolated string.</summary>
    private LLVMValueRef AlignAndFormat(IInterpolationOperation hole)
    {
        var piece = hole.FormatString is { } format ? FormatWith(hole.Expression, EmitValue(format)) : ToStringValue(hole.Expression);
        if (hole.Alignment is { } alignment)
            piece = CallRuntime("csrt_string_align", L.Ptr, [L.Ptr, L.I32], piece, Coerce(EmitValue(alignment), TypeOf(alignment), _m.Compilation.GetSpecialType(SpecialType.System_Int32)));
        return piece;
    }

    /// <summary>value.ToString(format) with .NET standard/custom numeric format strings (runtime/format.c).</summary>
    internal LLVMValueRef FormatWith(IOperation valueOp, LLVMValueRef format)
    {
        valueOp = Unbox(valueOp);
        var type = TypeOf(valueOp)!;
        var value = EmitValue(valueOp);
        if (type.TypeKind == TypeKind.Enum) return ToStringValue(valueOp);
        switch (type.SpecialType)
        {
            case SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64:
                return CallRuntime("csrt_format_i64", L.Ptr, [L.I64, L.Ptr, L.I32],
                    value.Type.IntWidth == 64 ? value : _b.Cast(LLVMOpcode.SExt, value, L.I64), format, L.ConstInt(L.I32, value.Type.IntWidth));
            case SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64:
                return CallRuntime("csrt_format_u64", L.Ptr, [L.I64, L.Ptr, L.I32],
                    value.Type.IntWidth == 64 ? value : _b.Cast(LLVMOpcode.ZExt, value, L.I64), format, L.ConstInt(L.I32, value.Type.IntWidth));
            case SpecialType.System_Double:
                return CallRuntime("csrt_format_f64", L.Ptr, [L.F64, L.Ptr, L.I1], value, format, L.False);
            case SpecialType.System_Single:
                return CallRuntime("csrt_format_f64", L.Ptr, [L.F64, L.Ptr, L.I1], _b.Cast(LLVMOpcode.FPExt, value, L.F64), format, L.True);
        }
        var obj = type.IsValueType ? Box(value, type, valueOp) : value;
        return CallRuntime("csrt_format_object", L.Ptr, [L.Ptr, L.Ptr], obj, format);
    }

    /// <summary>Builds an object[] from argument expressions (each already converted/boxed to object).</summary>
    internal LLVMValueRef ObjectArray(IReadOnlyList<IOperation> values)
    {
        var objectType = _m.Compilation.GetSpecialType(SpecialType.System_Object);
        var array = NewArray(objectType, L.ConstInt(L.I64, values.Count));
        StoreElements(array, objectType, [.. values]);
        return array;
    }

    private LLVMValueRef EmitInterpolatedString(IInterpolatedStringOperation s)
    {
        LLVMValueRef? result = null;
        foreach (var part in s.Parts)
        {
            var piece = part switch
            {
                IInterpolatedStringTextOperation text => ToStringValue(text.Text),
                IInterpolationOperation hole => AlignAndFormat(hole),
                _ => throw CompileError.Unsupported($"interpolated string part {part.Kind}", part),
            };
            result = result is { } r ? ConcatStrings(r, piece) : piece;
        }
        return result ?? _m.StringLiteral("");
    }

    // ======================================================================================
    // Patterns (`is`, switch arms after CFG lowering)
    // ======================================================================================

    private LLVMValueRef PatternTest(LLVMValueRef value, ITypeSymbol? inputType, IPatternOperation pattern)
    {
        if (TypeMapper.IsNullable(inputType)) return NullablePatternTest(value, inputType!, pattern);
        switch (pattern)
        {
            case IConstantPatternOperation constant:
            {
                var c = EmitValue(constant.Value);
                var constantType = TypeOf(constant.Value);
                if (inputType is not null && !value.Type.IsPointer) c = Coerce(c, constantType, inputType);
                return Arithmetic(BinaryOperatorKind.Equals, value, c, inputType ?? constantType!, constantType ?? inputType!, pattern);
            }
            case IRelationalPatternOperation relational:
            {
                var c = Coerce(EmitValue(relational.Value), TypeOf(relational.Value), inputType!);
                return Arithmetic(relational.OperatorKind, value, c, inputType!, inputType!, pattern);
            }
            case IDiscardPatternOperation:
                return L.True;
            case INegatedPatternOperation negated:
                return _b.Not(PatternTest(value, inputType, negated.Pattern));
            case IBinaryPatternOperation binary:
                return BinaryPatternTest(value, inputType, binary);
            case IRecursivePatternOperation recursive:
                return RecursivePatternTest(value, inputType, recursive);
            case IListPatternOperation list:
                return ListPatternTest(value, inputType, list);
            case ITypePatternOperation typePattern:
                return IsInstance(value, inputType, S(typePattern.MatchedType), pattern);
            case IDeclarationPatternOperation declaration:
            {
                var test = declaration.MatchesNull ? L.True : IsInstance(value, inputType, declaration.MatchedType is { } mt ? S(mt) : inputType!, pattern);
                if (declaration.DeclaredSymbol is ILocalSymbol local)
                {
                    var localType = S(local.Type);
                    if (localType.IsValueType && value.Type.IsPointer)
                    {
                        // `obj is int n`: unbox only when the type test succeeded.
                        var unbox = NewBlock("pattern.unbox");
                        var done = NewBlock("pattern.done");
                        _b.CondBr(test, unbox, done);
                        _b.PositionAtEnd(unbox);
                        StoreValue(ToStorage(LoadValue(_b.InBoundsGep(L.I8, value, [L.ConstInt(L.I64, 8)]), localType), localType), LocalSlot(local));
                        _b.Br(done);
                        _b.PositionAtEnd(done);
                    }
                    else StoreValue(ToStorage(Coerce(value, inputType, localType), localType), LocalSlot(local));
                }
                return test;
            }
            default:
                throw CompileError.Unsupported($"pattern {pattern.Kind}", pattern);
        }
    }
}
