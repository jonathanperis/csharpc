using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

public sealed partial class FunctionCodeGen
{
    /// <summary>Evaluates an operation to an SSA value (bool as i1; default for void).</summary>
    internal LLVMValueRef EmitValue(IOperation op)
    {
        if (op.ConstantValue.HasValue) return Constant(op.ConstantValue.Value, op.Type, op);

        return op switch
        {
            IFlowCaptureReferenceOperation r => LoadCapture(r),
            ILocalReferenceOperation l => LoadValue(LocalSlot(l.Local), l.Local.Type),
            IParameterReferenceOperation p => LoadValue(ParameterSlot(p.Parameter, p), p.Parameter.Type),
            IInstanceReferenceOperation => ThisValue(op),
            IFieldReferenceOperation f => LoadValue(FieldAddress(f), f.Field.Type),
            IPropertyReferenceOperation p => EmitPropertyGet(p),
            IArrayElementReferenceOperation a => LoadValue(ElementAddress(a), a.Type!),
            ISimpleAssignmentOperation a => EmitSimpleAssignment(a),
            ICompoundAssignmentOperation c => EmitCompoundAssignment(c),
            IIncrementOrDecrementOperation i => EmitIncrementOrDecrement(i),
            IBinaryOperation b => EmitBinary(b),
            IUnaryOperation u => EmitUnary(u),
            IConversionOperation c => EmitConversion(c),
            IInvocationOperation i => EmitInvocation(i),
            IObjectCreationOperation o => EmitObjectCreation(o),
            IArrayCreationOperation a => EmitArrayCreation(a),
            ICollectionExpressionOperation c => EmitCollectionExpression(c),
            IInterpolatedStringOperation s => EmitInterpolatedString(s),
            IIsNullOperation n => _b.ICmp(LLVMIntPredicate.EQ, EmitValue(n.Operand), LlvmContext.Null(L.Ptr)),
            IIsTypeOperation t => EmitIsType(t),
            IIsPatternOperation p => PatternTest(EmitValue(p.Value), p.Value.Type, p.Pattern),
            IDefaultValueOperation d => LlvmContext.Null(T.Value(d.Type!)),
            IExpressionStatementOperation e => EmitValue(e.Operation),
            IDeclarationExpressionOperation d => EmitValue(d.Expression),
            ILocalFunctionOperation => default, // body is lowered separately (ModuleCodeGen.EnqueueNested)
            IDelegateCreationOperation d => EmitDelegateCreation(d),
            IEmptyOperation => default,
            IAnonymousFunctionOperation or IFlowAnonymousFunctionOperation =>
                throw CompileError.Unsupported("lambda not converted to a delegate (e.g. expression trees)", op),
            IInterpolatedStringHandlerCreationOperation =>
                throw CompileError.Unsupported("interpolated string handlers", op),
            IDeconstructionAssignmentOperation d => EmitDeconstruction(d),
            ITupleOperation => throw CompileError.Unsupported("tuple values (tuples need struct support)", op),
            IInvalidOperation => throw CompileError.Unsupported("invalid code", op),
            _ => throw CompileError.Unsupported($"operation {op.Kind}", op),
        };
    }

    private LLVMValueRef LoadCapture(IFlowCaptureReferenceOperation r)
    {
        if (_cfg!.LvalueCaptures.Contains(r.Id)) return LoadValue(CaptureAddress(r), r.Type!);
        var (slot, type) = CaptureSlot(r.Id, r.Type);
        if (type is null) return _b.Load(L.Ptr, slot);
        var value = LoadValue(slot, type);
        return r.Type is null ? value : Coerce(value, type, r.Type);
    }

    // ======================================================================================
    // Constants
    // ======================================================================================

    internal LLVMValueRef Constant(object? value, ITypeSymbol? type, IOperation? at = null)
    {
        if (value is null) return LlvmContext.Null(type is null ? L.Ptr : T.Value(type));
        if (value is string s) return _m.StringLiteral(s);

        var llvmType = type is not null && type.SpecialType != SpecialType.System_Object ? T.Value(type) : value switch
        {
            bool => L.I1, char or short or ushort => L.I16, sbyte or byte => L.I8, int or uint => L.I32,
            long or ulong => L.I64, float => L.F32, double => L.F64,
            _ => throw CompileError.Unsupported($"constant of type {value.GetType().Name}", at),
        };
        return value switch
        {
            bool b => L.ConstInt(llvmType, b ? 1 : 0),
            char c => L.ConstInt(llvmType, c),
            float f when llvmType.IsFloatingPoint => LlvmContext.ConstReal(llvmType, f),
            double d when llvmType.IsFloatingPoint => LlvmContext.ConstReal(llvmType, d),
            ulong u => L.ConstUInt(llvmType, u),
            sbyte or byte or short or ushort or int or uint or long => L.ConstInt(llvmType, Convert.ToInt64(value)),
            decimal => throw CompileError.Unsupported("System.Decimal", at),
            _ => throw CompileError.Unsupported($"constant {value} of type {value.GetType().Name}", at),
        };
    }

    // ======================================================================================
    // L-values
    // ======================================================================================

    internal LLVMValueRef AddressOf(IOperation target) => target switch
    {
        ILocalReferenceOperation l => LocalSlot(l.Local),
        IParameterReferenceOperation p => ParameterSlot(p.Parameter, p),
        IFieldReferenceOperation f => FieldAddress(f),
        IArrayElementReferenceOperation a => ElementAddress(a),
        IDeclarationExpressionOperation d => AddressOf(d.Expression),
        IDiscardOperation d => NewTemp(T.Storage(d.Type!), "discard"),
        IFlowCaptureReferenceOperation c when _cfg!.LvalueCaptures.Contains(c.Id) => CaptureAddress(c),
        _ => throw CompileError.Unsupported($"assignment to {target.Kind}", target),
    };

    private LLVMValueRef CaptureAddress(IFlowCaptureReferenceOperation r) =>
        _b.Load(L.Ptr, _cfg!.Captures.TryGetValue(r.Id, out var c) ? c.Slot : throw CompileError.Unsupported("capture used before definition", r));

    private LLVMValueRef FieldAddress(IFieldReferenceOperation f)
    {
        var field = f.Field;
        if (field.IsStatic) return _m.StaticField(field);
        var instance = EmitValue(f.Instance!);
        if (f.Instance is not IInstanceReferenceOperation) NullCheck(instance);
        if (!_m.IsUserType(field.ContainingType)) throw CompileError.Unsupported($"field '{field}' of a library type", f);
        return FieldAddressOn(instance, field);
    }

    // ======================================================================================
    // Assignment
    // ======================================================================================

    private LLVMValueRef EmitSimpleAssignment(ISimpleAssignmentOperation a)
    {
        if (a.IsRef) throw CompileError.Unsupported("ref assignment", a);
        if (a.Target is IPropertyReferenceOperation p) return EmitPropertySet(p, () => EmitValue(a.Value), a.Value.Type);

        var address = AddressOf(a.Target);           // C#: target sub-expressions are evaluated first
        var value = Coerce(EmitValue(a.Value), a.Value.Type, a.Target.Type!);
        StoreValue(ToStorage(value, a.Target.Type!), address);
        return value;
    }

    /// <summary>
    /// `(a, b) = (x, y)` / `var (a, b) = (x, y)`: evaluate target locations, then all values, then store
    /// — so swaps like `(a[i], a[j]) = (a[j], a[i])` work. Tuple *values* need structs (not yet).
    /// </summary>
    private LLVMValueRef EmitDeconstruction(IDeconstructionAssignmentOperation d)
    {
        var target = d.Target is IDeclarationExpressionOperation decl ? decl.Expression : d.Target;
        var value = d.Value;
        while (value is IConversionOperation { Conversion.IsImplicit: true } c) value = c.Operand;
        if (target is not ITupleOperation targets || value is not ITupleOperation values || targets.Elements.Length != values.Elements.Length
            || targets.Elements.Any(e => e is ITupleOperation or IPropertyReferenceOperation))
            throw CompileError.Unsupported("deconstruction other than (a, b, ...) = (x, y, ...)", d);

        var addresses = targets.Elements.Select(e => e is IDiscardOperation ? default : AddressOf(e)).ToArray();
        var results = values.Elements.Select((v, i) => targets.Elements[i] is IDiscardOperation
            ? EmitValue(v)
            : Coerce(EmitValue(v), v.Type, targets.Elements[i].Type!)).ToArray();
        for (var i = 0; i < addresses.Length; i++)
            if (addresses[i].Handle != 0)
                StoreValue(ToStorage(results[i], targets.Elements[i].Type!), addresses[i]);
        return default;
    }

    /// <summary>Read-modify-write on any assignable target (locals, fields, elements, properties).</summary>
    private LLVMValueRef ReadModifyWrite(IOperation target, Func<LLVMValueRef, (LLVMValueRef NewValue, LLVMValueRef Result)> modify)
    {
        var type = target.Type!;
        if (target is IPropertyReferenceOperation p)
        {
            // Evaluate the receiver once, then call get + set on it.
            var self = p.Property.IsStatic ? default : EmitValue(p.Instance!);
            var old = CallAccessor(p, p.Property.GetMethod!, self, []);
            var (newValue, result) = modify(old);
            CallAccessor(p, p.Property.SetMethod ?? throw CompileError.Unsupported("compound assignment to a get-only property", p), self, [newValue]);
            return result;
        }
        var address = AddressOf(target);
        var (updated, value) = modify(LoadValue(address, type));
        StoreValue(ToStorage(updated, type), address);
        return value;
    }

    private LLVMValueRef EmitCompoundAssignment(ICompoundAssignmentOperation c)
    {
        if (c.OperatorMethod is { } om && !IsBuiltInStringConcat(om))
            throw CompileError.Unsupported("compound assignment with a user-defined operator", c);
        if (c.IsChecked) throw CompileError.Unsupported("checked arithmetic", c);
        var targetType = c.Target.Type!;
        return ReadModifyWrite(c.Target, old =>
        {
            if (c.OperatorKind == BinaryOperatorKind.Add && targetType.SpecialType == SpecialType.System_String)
            {
                var concat = ConcatStrings(old, ToStringValue(c.Value));
                return (concat, concat);
            }
            // e.g. `byte b += 1` computes in int, then narrows back (OutConversion).
            var opType = OperationTypeFor(targetType, c.Value.Type!, c.OperatorKind);
            var left = Coerce(old, targetType, opType);
            var right = c.OperatorKind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift
                ? EmitValue(c.Value)
                : Coerce(EmitValue(c.Value), c.Value.Type, opType);
            var result = Coerce(Arithmetic(c.OperatorKind, left, right, opType, c.Value.Type!, c), opType, targetType);
            return (result, result);
        });
    }

    private ITypeSymbol OperationTypeFor(ITypeSymbol target, ITypeSymbol value, BinaryOperatorKind kind)
    {
        var intType = _m.Compilation.GetSpecialType(SpecialType.System_Int32);
        ITypeSymbol Promote(ITypeSymbol t) =>
            t.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
                or SpecialType.System_UInt16 or SpecialType.System_Char ? intType : t;
        if (target.TypeKind == TypeKind.Enum || target.SpecialType == SpecialType.System_Boolean) return target;
        if (kind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift)
            return Promote(target);
        var (t, v) = (Promote(target), Promote(value));
        if (t.SpecialType == SpecialType.System_Double) return t;
        if (v.SpecialType == SpecialType.System_Double) return v;
        if (TypeMapper.IsFloatingPoint(t)) return t;
        if (TypeMapper.IsFloatingPoint(v)) return v;
        return T.Target.SizeOf(T.Value(v)) > T.Target.SizeOf(T.Value(t)) ? v : t;
    }

    private LLVMValueRef EmitIncrementOrDecrement(IIncrementOrDecrementOperation op)
    {
        if (op.OperatorMethod is not null) throw CompileError.Unsupported("user-defined ++/--", op);
        if (op.IsChecked) throw CompileError.Unsupported("checked arithmetic", op);
        var type = op.Target.Type!;
        var increment = op.Kind == OperationKind.Increment;
        return ReadModifyWrite(op.Target, old =>
        {
            var llvmType = T.Value(type);
            var updated = llvmType.IsFloatingPoint
                ? _b.BinOp(increment ? LLVMOpcode.FAdd : LLVMOpcode.FSub, old, LlvmContext.ConstReal(llvmType, 1))
                : _b.BinOp(increment ? LLVMOpcode.Add : LLVMOpcode.Sub, old, L.ConstInt(llvmType, 1));
            return (updated, op.IsPostfix ? old : updated);
        });
    }

    // ======================================================================================
    // Operators
    // ======================================================================================

    private static bool IsBuiltInStringConcat(IMethodSymbol method) =>
        method.ContainingType.SpecialType == SpecialType.System_String && method.Name is "op_Addition" or "Concat";

    private LLVMValueRef EmitBinary(IBinaryOperation op)
    {
        if (op.OperatorMethod is { } om && !IsBuiltInStringConcat(om) && om.ContainingType.SpecialType != SpecialType.System_String
            && om.ContainingType.SpecialType is not (SpecialType.System_Delegate or SpecialType.System_MulticastDelegate))
        {
            if (!_m.IsUserMethod(om)) throw CompileError.Unsupported($"operator '{om}'", op);
            return EmitDirectCall(om, [op.LeftOperand, op.RightOperand]);
        }
        if (op.IsChecked && op.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply)
            throw CompileError.Unsupported("checked arithmetic", op);

        var lt = op.LeftOperand.Type;
        var rt = op.RightOperand.Type;
        if (op.OperatorKind == BinaryOperatorKind.Add && (lt?.SpecialType == SpecialType.System_String || rt?.SpecialType == SpecialType.System_String))
            return ConcatStrings(ToStringValue(op.LeftOperand), ToStringValue(op.RightOperand));

        if (op.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
            return ShortCircuit(op);

        var left = EmitValue(op.LeftOperand);
        var right = EmitValue(op.RightOperand);
        return Arithmetic(op.OperatorKind, left, right, lt ?? rt!, rt ?? lt!, op);
    }

    /// <summary>&amp;&amp; / || that survived CFG lowering (rare: e.g. inside a pattern) — lower it ourselves.</summary>
    private LLVMValueRef ShortCircuit(IBinaryOperation op)
    {
        var isAnd = op.OperatorKind == BinaryOperatorKind.ConditionalAnd;
        var result = NewTemp(L.I8, isAnd ? "and" : "or");
        var left = EmitValue(op.LeftOperand);
        _b.Store(_b.Cast(LLVMOpcode.ZExt, left, L.I8), result);
        var rhs = NewBlock("sc.rhs");
        var done = NewBlock("sc.done");
        if (isAnd) _b.CondBr(left, rhs, done); else _b.CondBr(left, done, rhs);
        _b.PositionAtEnd(rhs);
        _b.Store(_b.Cast(LLVMOpcode.ZExt, EmitValue(op.RightOperand), L.I8), result);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Cast(LLVMOpcode.Trunc, _b.Load(L.I8, result), L.I1);
    }

    internal LLVMValueRef Arithmetic(BinaryOperatorKind kind, LLVMValueRef l, LLVMValueRef r, ITypeSymbol lt, ITypeSymbol rt, IOperation at)
    {
        // Reference types: string value equality, otherwise reference equality.
        if (l.Type.IsPointer || r.Type.IsPointer)
        {
            var isString = lt.SpecialType == SpecialType.System_String && rt.SpecialType == SpecialType.System_String;
            LLVMValueRef equal = isString
                ? CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], l, r)
                : _b.ICmp(LLVMIntPredicate.EQ, l, r);
            return kind switch
            {
                BinaryOperatorKind.Equals => equal,
                BinaryOperatorKind.NotEquals => _b.Not(equal),
                _ => throw CompileError.Unsupported($"operator {kind} on reference types", at),
            };
        }

        if (l.Type.IsFloatingPoint)
        {
            return kind switch
            {
                BinaryOperatorKind.Add => _b.BinOp(LLVMOpcode.FAdd, l, r),
                BinaryOperatorKind.Subtract => _b.BinOp(LLVMOpcode.FSub, l, r),
                BinaryOperatorKind.Multiply => _b.BinOp(LLVMOpcode.FMul, l, r),
                BinaryOperatorKind.Divide => _b.BinOp(LLVMOpcode.FDiv, l, r),
                BinaryOperatorKind.Remainder => _b.BinOp(LLVMOpcode.FRem, l, r),
                // IEEE semantics, as in C#: every comparison with NaN is false, except != which is true.
                BinaryOperatorKind.Equals => _b.FCmp(LLVMRealPredicate.OEQ, l, r),
                BinaryOperatorKind.NotEquals => _b.FCmp(LLVMRealPredicate.UNE, l, r),
                BinaryOperatorKind.LessThan => _b.FCmp(LLVMRealPredicate.OLT, l, r),
                BinaryOperatorKind.LessThanOrEqual => _b.FCmp(LLVMRealPredicate.OLE, l, r),
                BinaryOperatorKind.GreaterThan => _b.FCmp(LLVMRealPredicate.OGT, l, r),
                BinaryOperatorKind.GreaterThanOrEqual => _b.FCmp(LLVMRealPredicate.OGE, l, r),
                _ => throw CompileError.Unsupported($"operator {kind} on {lt}", at),
            };
        }

        var signed = TypeMapper.IsSigned(lt);
        switch (kind)
        {
            case BinaryOperatorKind.Add: return _b.BinOp(LLVMOpcode.Add, l, r);
            case BinaryOperatorKind.Subtract: return _b.BinOp(LLVMOpcode.Sub, l, r);
            case BinaryOperatorKind.Multiply: return _b.BinOp(LLVMOpcode.Mul, l, r);
            case BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder:
                return CheckedDivide(kind == BinaryOperatorKind.Divide, l, r, signed);
            case BinaryOperatorKind.And: return _b.BinOp(LLVMOpcode.And, l, r);
            case BinaryOperatorKind.Or: return _b.BinOp(LLVMOpcode.Or, l, r);
            case BinaryOperatorKind.ExclusiveOr: return _b.BinOp(LLVMOpcode.Xor, l, r);
            case BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift:
            {
                // C# masks the shift count to the operand width (x << 33 == x << 1 for int); LLVM would yield poison.
                var width = l.Type.IntWidth;
                var count = Coerce(r, rt, width == 64 ? _m.Compilation.GetSpecialType(SpecialType.System_Int64) : _m.Compilation.GetSpecialType(SpecialType.System_Int32));
                count = _b.BinOp(LLVMOpcode.And, count, L.ConstInt(l.Type, width - 1));
                var op = kind switch
                {
                    BinaryOperatorKind.LeftShift => LLVMOpcode.Shl,
                    BinaryOperatorKind.RightShift when signed => LLVMOpcode.AShr,
                    _ => LLVMOpcode.LShr,
                };
                return _b.BinOp(op, l, count);
            }
            case BinaryOperatorKind.Equals: return _b.ICmp(LLVMIntPredicate.EQ, l, r);
            case BinaryOperatorKind.NotEquals: return _b.ICmp(LLVMIntPredicate.NE, l, r);
            case BinaryOperatorKind.LessThan: return _b.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, l, r);
            case BinaryOperatorKind.LessThanOrEqual: return _b.ICmp(signed ? LLVMIntPredicate.SLE : LLVMIntPredicate.ULE, l, r);
            case BinaryOperatorKind.GreaterThan: return _b.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, l, r);
            case BinaryOperatorKind.GreaterThanOrEqual: return _b.ICmp(signed ? LLVMIntPredicate.SGE : LLVMIntPredicate.UGE, l, r);
            default: throw CompileError.Unsupported($"operator {kind}", at);
        }
    }

    /// <summary>
    /// Integer division with .NET semantics: DivideByZeroException on /0 and OverflowException on
    /// MinValue / -1 — both are undefined behaviour for LLVM's sdiv/srem, so we must check first.
    /// </summary>
    private LLVMValueRef CheckedDivide(bool divide, LLVMValueRef l, LLVMValueRef r, bool signed)
    {
        var type = l.Type;
        FailIf(_b.ICmp(LLVMIntPredicate.EQ, r, L.ConstInt(type, 0)), "csrt_throw_divide_by_zero");
        if (signed)
        {
            var min = L.ConstUInt(type, 1UL << (int)(type.IntWidth - 1));
            var overflow = _b.BinOp(LLVMOpcode.And,
                _b.ICmp(LLVMIntPredicate.EQ, l, min),
                _b.ICmp(LLVMIntPredicate.EQ, r, L.ConstInt(type, -1)));
            FailIf(overflow, "csrt_throw_overflow");
        }
        var op = (divide, signed) switch
        {
            (true, true) => LLVMOpcode.SDiv,
            (true, false) => LLVMOpcode.UDiv,
            (false, true) => LLVMOpcode.SRem,
            (false, false) => LLVMOpcode.URem,
        };
        return _b.BinOp(op, l, r);
    }

    private LLVMValueRef EmitUnary(IUnaryOperation op)
    {
        if (op.OperatorMethod is { } om)
        {
            if (!_m.IsUserMethod(om)) throw CompileError.Unsupported($"operator '{om}'", op);
            return EmitDirectCall(om, [op.Operand]);
        }
        if (op.IsChecked && op.OperatorKind == UnaryOperatorKind.Minus) throw CompileError.Unsupported("checked arithmetic", op);
        var v = EmitValue(op.Operand);
        return op.OperatorKind switch
        {
            UnaryOperatorKind.Plus => v,
            UnaryOperatorKind.Minus => v.Type.IsFloatingPoint ? _b.FNeg(v) : _b.Neg(v),
            UnaryOperatorKind.BitwiseNegation or UnaryOperatorKind.Not => _b.Not(v),
            _ => throw CompileError.Unsupported($"unary operator {op.OperatorKind}", op),
        };
    }

    // ======================================================================================
    // Conversions
    // ======================================================================================

    private LLVMValueRef EmitConversion(IConversionOperation c)
    {
        var from = c.Operand.Type;
        var to = c.Type!;
        if (c.OperatorMethod is { } om)
        {
            if (!_m.IsUserMethod(om)) throw CompileError.Unsupported($"conversion operator '{om}'", c);
            return EmitDirectCall(om, [c.Operand]);
        }
        if (c.Conversion.IsIdentity) return EmitValue(c.Operand);
        // foreach over T[]: `(V)enumerator.Current` (object) becomes a typed element load.
        if (c.Operand is IPropertyReferenceOperation { Property.Name: "Current" } current
            && TryArrayEnumerator(current.Instance, out var elementType))
        {
            var element = ArrayEnumeratorCurrent(EmitValue(current.Instance!), elementType);
            return elementType.IsReferenceType ? element : Coerce(element, elementType, to);
        }
        // ...and the foreach's finally tests `enumerator as IDisposable`: array enumerators never are.
        if (TryArrayEnumerator(c.Operand, out _)) return LlvmContext.Null(L.Ptr);
        if (from is null) return Constant(null, to, c); // null literal / default
        if (from.IsValueType && to.IsReferenceType) return Box(EmitValue(c.Operand), from, c);
        if (from.IsReferenceType && to.IsValueType)
        {
            var payload = CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], EmitValue(c.Operand), MethodTableFor(to, c));
            return LoadValue(payload, to);
        }
        if (c.IsChecked && !c.Conversion.IsImplicit && from.IsValueType)
            throw CompileError.Unsupported("checked conversion", c);

        var value = EmitValue(c.Operand);
        if (from.IsReferenceType)
        {
            if (c.Conversion.IsImplicit) return value; // upcast: same pointer
            return CastClass(value, to, c);
        }
        return ConvertNumeric(value, from, to, c);
    }

    /// <summary>Converts between types that may differ only in representation (numeric/enum), else passes through.</summary>
    internal LLVMValueRef Coerce(LLVMValueRef value, ITypeSymbol? from, ITypeSymbol to)
    {
        if (from is null || SymbolEqualityComparer.Default.Equals(from, to)) return value;
        if (from.IsReferenceType || to.IsReferenceType) return value;
        return ConvertNumeric(value, from, to, null);
    }

    internal LLVMValueRef ConvertNumeric(LLVMValueRef value, ITypeSymbol from, ITypeSymbol to, IOperation? at)
    {
        var src = value.Type;
        var dst = T.Value(to);
        if (src.Handle == dst.Handle) return value;

        if (src.IsInteger && dst.IsInteger)
        {
            var (sw, dw) = (src.IntWidth, dst.IntWidth);
            if (sw == dw) return value;
            if (sw > dw) return _b.Cast(LLVMOpcode.Trunc, value, dst);
            return _b.Cast(TypeMapper.IsSigned(from) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, dst);
        }
        if (src.IsInteger && dst.IsFloatingPoint)
            return _b.Cast(TypeMapper.IsSigned(from) ? LLVMOpcode.SIToFP : LLVMOpcode.UIToFP, value, dst);
        if (src.IsFloatingPoint && dst.IsFloatingPoint)
            return _b.Cast(src.Kind == LLVMTypeKind.Double ? LLVMOpcode.FPTrunc : LLVMOpcode.FPExt, value, dst);
        if (src.IsFloatingPoint && dst.IsInteger)
        {
            // .NET 9+ defines float->int conversions as saturating (NaN -> 0); LLVM's fptosi would be poison.
            var signed = TypeMapper.IsSigned(to);
            var name = $"llvm.fpto{(signed ? "s" : "u")}i.sat.i{dst.IntWidth}.{(src.Kind == LLVMTypeKind.Double ? "f64" : "f32")}";
            var f = _m.Intrinsic(name, dst, src);
            return _b.Call(f.Type, f.Fn, [value]);
        }
        throw CompileError.Unsupported($"conversion from '{from}' to '{to}'", at);
    }
}
