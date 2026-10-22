using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Value types: user structs, Nullable&lt;T&gt; and ValueTuple (docs/03-lowering.md#structs).
///
/// Representation: an SSA value of struct type is an LLVM first-class aggregate (so loads, stores,
/// arguments and returns copy, giving C#'s value semantics for free; SROA scalarizes them again).
/// Member access needs an address: lvalues (locals, parameters, fields, array elements, `this`)
/// use their storage directly so `s.X = 1` mutates in place, and rvalues are spilled to a temporary.
/// Struct instance methods and constructors receive `this` as a pointer to the storage.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>Struct-like value types lowered to LLVM aggregates.</summary>
    internal bool IsAggregate(ITypeSymbol? type) => type is INamedTypeSymbol named
        && (TypeMapper.IsNullable(named) || named.IsTupleType || (named.TypeKind == TypeKind.Struct && _m.IsUserType(named))
            || TypeMapper.IsSpan(named) || TypeMapper.IsSpanEnumerator(named));

    private bool InStructMember => _method is { ContainingType.IsValueType: true } m && ModuleCodeGen.HasThis(m);

    /// <summary>Address of a value-type operand: its storage if it is an lvalue, else a spilled copy.</summary>
    internal LLVMValueRef AddressOrSpill(IOperation op)
    {
        if (TryAddressOfNone(op, out var pointee)) return pointee; // `p->f`, `(*p).f`, `p[i].f`
        if (op is IPropertyReferenceOperation { Property.RefKind: not RefKind.None } refProperty) return RefPropertyAddress(refProperty);
        if (op is IInvocationOperation { TargetMethod.RefKind: not RefKind.None } refCall) return EmitInvocation(refCall);
        switch (op)
        {
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.PatternInput }:
                return Spill(PatternInputValue(op));
            case IInstanceReferenceOperation when InStructMember:
                return _this;
            case ILocalReferenceOperation or IParameterReferenceOperation or IArrayElementReferenceOperation
                or IDeclarationExpressionOperation:
                return AddressOf(op);
            case IFieldReferenceOperation f when f.Field.IsStatic || f.Instance is not null:
                return AddressOf(op);
            case IFlowCaptureReferenceOperation c when _cfg!.LvalueCaptures.Contains(c.Id):
                return CaptureAddress(c);
            case IFlowCaptureReferenceOperation c when _cfg!.Captures.TryGetValue(c.Id, out var capture) && capture.Type is not null:
                return capture.Slot;
            default:
                return Spill(EmitValue(op));
        }
    }

    internal LLVMValueRef Spill(LLVMValueRef value)
    {
        var temp = NewTemp(value.Type, "tmp");
        _b.Store(value, temp);
        return temp;
    }

    /// <summary>The `this` argument for an instance member: a pointer for structs, the reference for classes.</summary>
    internal LLVMValueRef Receiver(IOperation instance, IMethodSymbol member)
    {
        if (!member.ContainingType.IsValueType) return EmitValue(instance);
        // C# makes a defensive copy when a non-readonly member is invoked on a readonly field.
        if (instance is IFieldReferenceOperation { Field.IsReadOnly: true } f && !member.IsReadOnly
            && !(_method is { MethodKind: MethodKind.Constructor } && SymbolEqualityComparer.Default.Equals(_method.ContainingType, f.Field.ContainingType)))
            return Spill(EmitValue(instance));
        return AddressOrSpill(instance);
    }

    internal static bool ReceiverIsNonNull(IOperation? instance, IMethodSymbol member) =>
        instance is IInstanceReferenceOperation || member.ContainingType.IsValueType;

    internal LLVMValueRef FromStorage(LLVMValueRef value, ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Boolean ? _b.Cast(LLVMOpcode.Trunc, value, L.I1) : value;

    // ======================================================================================
    // Creation
    // ======================================================================================

    private LLVMValueRef EmitValueTypeCreation(IObjectCreationOperation creation, INamedTypeSymbol type)
    {
        var llvmType = T.Value(type);
        var args = creation.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        if (TypeMapper.IsNullable(type))
            return args.Length == 0 ? LlvmContext.Null(llvmType) : MakeNullable(type, L.True, EmitArgumentValue(args[0]));
        if (type.IsTupleType)
        {
            var tuple = LlvmContext.Null(llvmType);
            for (var i = 0; i < args.Length; i++)
                tuple = _b.InsertValue(tuple, ToStorage(EmitArgumentValue(args[i]), args[i].Parameter!.Type), (uint)i);
            return tuple;
        }
        if (!_m.IsUserType(type)) throw CompileError.Unsupported($"creating instances of library struct '{type}'", creation);

        // `new S()` without an explicit parameterless constructor is just default(S).
        if (creation.Constructor is null or { IsImplicitlyDeclared: true, Parameters.IsEmpty: true })
            return LlvmContext.Null(llvmType);
        var temp = NewTemp(llvmType, "new." + type.Name);
        _b.Store(LlvmContext.Null(llvmType), temp); // fields the constructor doesn't assign are zero
        EmitUserCall(creation.Constructor, temp, selfIsNonNull: true, creation.Arguments, isVirtual: false);
        return _b.Load(llvmType, temp);
    }

    private LLVMValueRef EmitArgumentValue(IArgumentOperation a) =>
        Coerce(EmitValue(a.Value), a.Value.Type, a.Parameter!.Type);

    // ======================================================================================
    // Nullable<T>:  { i8 hasValue, T value } — value is zero whenever hasValue is false
    // ======================================================================================

    internal LLVMValueRef NullableHas(LLVMValueRef nullable) =>
        _b.ICmp(LLVMIntPredicate.NE, _b.ExtractValue(nullable, 0, "has"), L.ConstInt(L.I8, 0));

    internal LLVMValueRef NullablePayload(LLVMValueRef nullable, ITypeSymbol underlying) =>
        FromStorage(_b.ExtractValue(nullable, 1, "value"), underlying);

    internal LLVMValueRef MakeNullable(ITypeSymbol nullableType, LLVMValueRef has, LLVMValueRef value)
    {
        var underlying = TypeMapper.NullableUnderlying(nullableType);
        var llvmType = T.Value(nullableType);
        var payload = ToStorage(value, underlying);
        payload = _b.Select(has, payload, LlvmContext.Null(payload.Type)); // keep the "zero when empty" invariant
        var result = _b.InsertValue(LlvmContext.Null(llvmType), _b.Cast(LLVMOpcode.ZExt, has, L.I8), 0);
        return _b.InsertValue(result, payload, 1);
    }

    /// <summary>Splits a possibly-nullable operand into (hasValue, payload, payload type).</summary>
    private (LLVMValueRef Has, LLVMValueRef Value, ITypeSymbol Type) NullableParts(LLVMValueRef value, ITypeSymbol type) =>
        TypeMapper.IsNullable(type)
            ? (NullableHas(value), NullablePayload(value, TypeMapper.NullableUnderlying(type)), TypeMapper.NullableUnderlying(type))
            : (L.True, value, type);

    private void ThrowLibraryExceptionIf(LLVMValueRef failed, string typeName, string message)
    {
        var fail = NewBlock("throw");
        var ok = NewBlock("ok");
        _b.CondBr(failed, fail, ok);
        _b.PositionAtEnd(fail);
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName(typeName)!, message);
        _b.PositionAtEnd(ok);
    }

    private LLVMValueRef NullableValueOrThrow(LLVMValueRef nullable, ITypeSymbol nullableType)
    {
        ThrowLibraryExceptionIf(_b.Not(NullableHas(nullable)), "System.InvalidOperationException", "Cannot read the Value property of a Nullable object that has no value. Check HasValue before reading Value.");
        return NullablePayload(nullable, TypeMapper.NullableUnderlying(nullableType));
    }

    /// <summary>Conversions with a nullable side: wrapping, lifting, unwrapping, boxing, unboxing.</summary>
    private LLVMValueRef? TryNullableConversion(IConversionOperation c, ITypeSymbol from, ITypeSymbol to)
    {
        bool fromNullable = TypeMapper.IsNullable(from), toNullable = TypeMapper.IsNullable(to);
        if (!fromNullable && !toNullable) return null;
        if (fromNullable && to.IsReferenceType) return Box(EmitValue(c.Operand), from, c);
        if (fromNullable && !toNullable)
            return Coerce(NullableValueOrThrow(EmitValue(c.Operand), from), TypeMapper.NullableUnderlying(from), to); // (T)x
        if (from.IsReferenceType)
        {
            // Unboxing to T?: null -> empty, otherwise unbox the T payload.
            var underlying = TypeMapper.NullableUnderlying(to);
            var obj = EmitValue(c.Operand);
            var result = NewTemp(T.Value(to), "unbox.nullable");
            _b.Store(LlvmContext.Null(T.Value(to)), result);
            var unbox = NewBlock("unbox");
            var done = NewBlock("unbox.done");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, obj, LlvmContext.Null(L.Ptr)), done, unbox);
            _b.PositionAtEnd(unbox);
            var payload = CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], obj, MethodTableFor(underlying, c));
            _b.Store(MakeNullable(to, L.True, LoadValue(payload, underlying)), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(T.Value(to), result);
        }
        return Coerce(EmitValue(c.Operand), from, to);
    }

    private LLVMValueRef CoerceAggregate(LLVMValueRef value, ITypeSymbol from, ITypeSymbol to)
    {
        if (TypeMapper.IsNullable(to))
        {
            var target = TypeMapper.NullableUnderlying(to);
            if (!TypeMapper.IsNullable(from)) return MakeNullable(to, L.True, Coerce(value, from, target));
            var (has, payload, source) = NullableParts(value, from);
            return T.Value(from).Handle == T.Value(to).Handle ? value : MakeNullable(to, has, Coerce(payload, source, target));
        }
        if (to is INamedTypeSymbol { IsTupleType: true } toTuple && from is INamedTypeSymbol { IsTupleType: true } fromTuple)
            return ConvertTuple(value, fromTuple, toTuple);
        return value;
    }

    private LLVMValueRef EmitNullableMember(IPropertyReferenceOperation p)
    {
        var value = EmitValue(p.Instance!);
        return p.Property.Name switch
        {
            "HasValue" => NullableHas(value),
            "Value" => NullableValueOrThrow(value, p.Property.ContainingType),
            _ => throw CompileError.Unsupported($"Nullable<T>.{p.Property.Name}", p),
        };
    }

    private LLVMValueRef EmitNullableMethod(IInvocationOperation inv)
    {
        var type = inv.TargetMethod.ContainingType;
        var value = EmitValue(inv.Instance!);
        var underlying = TypeMapper.NullableUnderlying(type);
        return (inv.TargetMethod.Name, inv.Arguments.Length) switch
        {
            ("GetValueOrDefault", 0) => NullablePayload(value, underlying),
            ("GetValueOrDefault", 1) => _b.Select(NullableHas(value), NullablePayload(value, underlying), EmitArgumentValue(inv.Arguments[0])),
            ("ToString", 0) => FormatValue(value, type, inv),
            _ => throw CompileError.Unsupported($"Nullable<T>.{inv.TargetMethod.Name}", inv),
        };
    }

    /// <summary>Lifted operators: null if either operand is null; lifted comparisons are false, except == on two nulls.</summary>
    internal LLVMValueRef LiftedArithmetic(BinaryOperatorKind kind, LLVMValueRef l, ITypeSymbol lt, LLVMValueRef r, ITypeSymbol rt,
        ITypeSymbol resultType, IOperation at)
    {
        var (lh, lv, lu) = NullableParts(l, lt);
        var (rh, rv, ru) = NullableParts(r, rt);
        var both = _b.BinOp(LLVMOpcode.And, lh, rh);
        switch (kind)
        {
            case BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals:
            {
                var equal = _b.BinOp(LLVMOpcode.Or,
                    _b.BinOp(LLVMOpcode.And, both, Arithmetic(BinaryOperatorKind.Equals, lv, rv, lu, ru, at)),
                    _b.BinOp(LLVMOpcode.And, _b.Not(lh), _b.Not(rh)));
                return kind == BinaryOperatorKind.Equals ? equal : _b.Not(equal);
            }
            case BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual
                or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual:
                return _b.BinOp(LLVMOpcode.And, both, Arithmetic(kind, lv, rv, lu, ru, at));
        }
        if (!TypeMapper.IsNullable(resultType)) throw CompileError.Unsupported($"lifted operator {kind}", at);
        if (lu.SpecialType == SpecialType.System_Boolean && kind is BinaryOperatorKind.And or BinaryOperatorKind.Or)
        {
            // C# three-valued logic: false & null == false, true | null == true, otherwise null wins.
            var isAnd = kind == BinaryOperatorKind.And;
            var lDecides = _b.BinOp(LLVMOpcode.And, lh, isAnd ? _b.Not(lv) : lv);
            var rDecides = _b.BinOp(LLVMOpcode.And, rh, isAnd ? _b.Not(rv) : rv);
            var decided = _b.BinOp(LLVMOpcode.Or, lDecides, rDecides);
            var has = _b.BinOp(LLVMOpcode.Or, decided, both);
            var value = isAnd ? _b.BinOp(LLVMOpcode.And, has, _b.Not(decided)) : decided;
            return MakeNullable(resultType, has, value);
        }

        if (kind is BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder && lv.Type.IsInteger)
        {
            // Only divide when both are present: a null divisor must yield null, not DivideByZeroException.
            var result = NewTemp(T.Value(resultType), "lifted");
            _b.Store(LlvmContext.Null(T.Value(resultType)), result);
            var compute = NewBlock("lifted.compute");
            var done = NewBlock("lifted.done");
            _b.CondBr(both, compute, done);
            _b.PositionAtEnd(compute);
            _b.Store(MakeNullable(resultType, L.True, Arithmetic(kind, lv, rv, lu, ru, at)), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(T.Value(resultType), result);
        }
        return MakeNullable(resultType, both, Arithmetic(kind, lv, rv, lu, ru, at));
    }

    private LLVMValueRef EmitLiftedUnary(IUnaryOperation op)
    {
        var (has, payload, _) = NullableParts(EmitValue(op.Operand), op.Operand.Type!);
        var result = op.OperatorKind switch
        {
            UnaryOperatorKind.Plus => payload,
            UnaryOperatorKind.Minus => payload.Type.IsFloatingPoint ? _b.FNeg(payload) : _b.Neg(payload),
            UnaryOperatorKind.BitwiseNegation or UnaryOperatorKind.Not => _b.Not(payload),
            _ => throw CompileError.Unsupported($"lifted unary operator {op.OperatorKind}", op),
        };
        return MakeNullable(op.Type!, has, result);
    }

    private LLVMValueRef NullablePatternTest(LLVMValueRef value, ITypeSymbol inputType, IPatternOperation pattern)
    {
        var underlying = TypeMapper.NullableUnderlying(inputType);
        var has = NullableHas(value);
        switch (pattern)
        {
            case IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } }:
                return _b.Not(has);
            case IDiscardPatternOperation:
                return L.True;
            case IDeclarationPatternOperation { MatchesNull: true, DeclaredSymbol: ILocalSymbol whole }:
                StoreValue(ToStorage(Coerce(value, inputType, whole.Type), whole.Type), LocalSlot(whole));
                return L.True;
            case IDeclarationPatternOperation declaration:
                if (declaration.DeclaredSymbol is ILocalSymbol local)
                    StoreValue(ToStorage(Coerce(NullablePayload(value, underlying), underlying, local.Type), local.Type), LocalSlot(local));
                return has;
            case ITypePatternOperation:
                return has;
            case INegatedPatternOperation negated:
                return _b.Not(NullablePatternTest(value, inputType, negated.Pattern));
            case IBinaryPatternOperation binary:
            {
                var left = NullablePatternTest(value, inputType, binary.LeftPattern);
                var right = NullablePatternTest(value, inputType, binary.RightPattern);
                return _b.BinOp(binary.OperatorKind == BinaryOperatorKind.Or ? LLVMOpcode.Or : LLVMOpcode.And, left, right);
            }
            default:
                return _b.BinOp(LLVMOpcode.And, has, PatternTest(NullablePayload(value, underlying), underlying, pattern));
        }
    }

    // ======================================================================================
    // ValueTuple: flattened { T1, T2, ... }
    // ======================================================================================

    private LLVMValueRef BuildTuple(ITupleOperation tuple, INamedTypeSymbol type)
    {
        var elements = type.TupleElements;
        if (elements.Length != tuple.Elements.Length) throw CompileError.Unsupported("tuple with more than 7 elements", tuple);
        var result = LlvmContext.Null(T.Value(type));
        for (var i = 0; i < elements.Length; i++)
        {
            var element = tuple.Elements[i];
            var elementType = elements[i].Type;
            var value = element switch
            {
                ITupleOperation nested when elementType is INamedTypeSymbol { IsTupleType: true } nestedType => BuildTuple(nested, nestedType),
                { Type: null } => Constant(null, elementType, element),
                _ => Coerce(EmitValue(element), element.Type, elementType),
            };
            result = _b.InsertValue(result, ToStorage(value, elementType), (uint)i);
        }
        return result;
    }

    private LLVMValueRef ConvertTuple(LLVMValueRef value, INamedTypeSymbol from, INamedTypeSymbol to)
    {
        if (T.Value(from).Handle == T.Value(to).Handle) return value; // only names differ
        var result = LlvmContext.Null(T.Value(to));
        for (var i = 0; i < to.TupleElements.Length; i++)
        {
            var (source, target) = (from.TupleElements[i].Type, to.TupleElements[i].Type);
            var element = Coerce(FromStorage(_b.ExtractValue(value, (uint)i), source), source, target);
            result = _b.InsertValue(result, ToStorage(element, target), (uint)i);
        }
        return result;
    }

    private LLVMValueRef TupleElement(LLVMValueRef tuple, INamedTypeSymbol type, int index) =>
        FromStorage(_b.ExtractValue(tuple, (uint)index), type.TupleElements[index].Type);

    private LLVMValueRef EmitTupleBinary(ITupleBinaryOperation op)
    {
        if (op.LeftOperand.Type is not INamedTypeSymbol { IsTupleType: true } lt || op.RightOperand.Type is not INamedTypeSymbol { IsTupleType: true } rt)
            throw CompileError.Unsupported("tuple comparison with untyped elements", op);
        var l = EmitValue(op.LeftOperand);
        var r = EmitValue(op.RightOperand);
        var equal = L.True;
        for (var i = 0; i < lt.TupleElements.Length; i++)
        {
            var (ltype, rtype) = (lt.TupleElements[i].Type, rt.TupleElements[i].Type);
            if (IsAggregate(ltype) || IsAggregate(rtype)) throw CompileError.Unsupported("nested tuple comparison", op);
            var a = TupleElement(l, lt, i);
            var b = Coerce(TupleElement(r, rt, i), rtype, ltype);
            equal = _b.BinOp(LLVMOpcode.And, equal, Arithmetic(BinaryOperatorKind.Equals, a, b, ltype, ltype, op));
        }
        return op.OperatorKind == BinaryOperatorKind.Equals ? equal : _b.Not(equal);
    }

    /// <summary>`var (a, b) = tupleValue;` — targets first, then the value, then element-wise stores.</summary>
    private LLVMValueRef EmitTupleValueDeconstruction(ITupleOperation targets, IOperation value)
    {
        if (TypeOf(value) is not INamedTypeSymbol { IsTupleType: true } && UserDeconstruct(targets, value) is { } viaMethod) return viaMethod;
        if (value.Type is not INamedTypeSymbol { IsTupleType: true } tupleType || tupleType.TupleElements.Length != targets.Elements.Length
            || targets.Elements.Any(e => e is ITupleOperation or IPropertyReferenceOperation))
            throw CompileError.Unsupported("deconstruction of this value", value);
        var addresses = targets.Elements.Select(e => e is IDiscardOperation ? default : AddressOf(e)).ToArray();
        var tuple = EmitValue(value);
        for (var i = 0; i < addresses.Length; i++)
        {
            if (addresses[i].Handle == 0) continue;
            var targetType = targets.Elements[i].Type!;
            var element = Coerce(TupleElement(tuple, tupleType, i), tupleType.TupleElements[i].Type, targetType);
            StoreValue(ToStorage(element, targetType), addresses[i]);
        }
        return default;
    }

    // ======================================================================================
    // Formatting (interpolation, concatenation, Console.Write) of any value
    // ======================================================================================

    internal LLVMValueRef FormatValue(LLVMValueRef value, ITypeSymbol type, IOperation at)
    {
        if (type.SpecialType == SpecialType.System_String) return value;
        if (FormatPrimitive(value, type) is { } primitive) return primitive;
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var formatter = _m.EnumFormatter(enumType);
            return _b.Call(formatter.Type, formatter.Fn, [value]);
        }
        if (type.IsReferenceType) return ObjectToString(value);

        if (TypeMapper.IsNullable(type))
        {
            // null formats as the empty string.
            var (has, payload, underlying) = NullableParts(value, type);
            var result = NewTemp(L.Ptr, "fmt");
            _b.Store(_m.StringLiteral(""), result);
            var format = NewBlock("fmt.value");
            var done = NewBlock("fmt.done");
            _b.CondBr(has, format, done);
            _b.PositionAtEnd(format);
            _b.Store(FormatValue(payload, underlying, at), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(L.Ptr, result);
        }
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            // ValueTuple.ToString(): "(a, b, c)"
            var text = _m.StringLiteral("(");
            for (var i = 0; i < tuple.TupleElements.Length; i++)
            {
                if (i > 0) text = ConcatStrings(text, _m.StringLiteral(", "));
                text = ConcatStrings(text, FormatValue(TupleElement(value, tuple, i), tuple.TupleElements[i].Type, at));
            }
            return ConcatStrings(text, _m.StringLiteral(")"));
        }
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } user && _m.IsUserType(user))
        {
            var toString = user.GetMembers("ToString").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsOverride && m.Parameters.IsEmpty);
            if (toString is not null)
            {
                var f = _m.GetFunction(toString);
                return _b.Call(f.Type, f.Fn, [Spill(value)]);
            }
            return ObjectToString(Box(value, type, at)); // ValueType.ToString(): the type name
        }
        throw CompileError.Unsupported($"formatting '{type}'", at);
    }
}
