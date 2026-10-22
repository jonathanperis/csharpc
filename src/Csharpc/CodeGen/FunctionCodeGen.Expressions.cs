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
        if (op.ConstantValue.HasValue) return Constant(op.ConstantValue.Value, TypeOf(op), op);

        return op switch
        {
            IFlowCaptureReferenceOperation r => LoadCapture(r),
            ILocalReferenceOperation l => LoadValue(LocalSlot(l.Local), S(l.Local.Type)),
            IParameterReferenceOperation p => LoadValue(ParameterSlot(p.Parameter, p), S(p.Parameter.Type)),
            IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.PatternInput } => PatternInputValue(op),
            IInstanceReferenceOperation when InStructMember => LoadValue(_this, S(_method!.ContainingType)),
            IInstanceReferenceOperation => ThisValue(op),
            // IntPtr.Zero / UIntPtr.Zero: the only static fields interop code reads off these types.
            IFieldReferenceOperation { Field: { IsStatic: true, Name: "Zero", ContainingType.SpecialType: SpecialType.System_IntPtr or SpecialType.System_UIntPtr } } zf =>
                LlvmContext.Null(T.Value(zf.Field.Type)),
            IFieldReferenceOperation f when TryLowLevelLibraryMember(f, out var libraryField) => libraryField,
            IFieldReferenceOperation { Field.IsStatic: true } f when !_m.IsUserType(_m.CoreLib.Map(f.Field).ContainingType) =>
                BclValueTypes.TryGetField(f.Field, out var bclField) ? bclField(this, f)
                    : throw CompileError.Unsupported($"static field '{f.Field}' of a library type", f),
            IFieldReferenceOperation { Field.IsVolatile: true } vf => VolatileLoad(FieldAddress(vf), S(vf.Field.Type)),
            IFieldReferenceOperation f => LoadValue(FieldAddress(f), S(f.Field.Type)),
            ITypeOfOperation t => TypeObject(S(t.TypeOperand), t),
            IPropertyReferenceOperation { Property.RefKind: not RefKind.None } p => LoadValue(RefPropertyAddress(p), TypeOf(p)!),
            IPropertyReferenceOperation p => EmitPropertyGet(p),
            IArrayElementReferenceOperation { Indices: [{ Type: var rangeType }] } a when IsRangeType(rangeType) => EmitArrayRange(a),
            IArrayElementReferenceOperation a => LoadValue(ElementAddress(a), TypeOf(a)!),
            IImplicitIndexerReferenceOperation ii => EmitImplicitIndexer(ii),
            IRangeOperation r => EmitRange(r),
            ISimpleAssignmentOperation a => EmitSimpleAssignment(a),
            ICompoundAssignmentOperation c => EmitCompoundAssignment(c),
            IIncrementOrDecrementOperation i => EmitIncrementOrDecrement(i),
            IBinaryOperation b => EmitBinary(b),
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Hat } u => EmitHat(u),
            IUnaryOperation u => EmitUnary(u),
            IConversionOperation c => EmitConversion(c),
            IInvocationOperation { TargetMethod.RefKind: not RefKind.None } i => LoadValue(EmitInvocation(i), i.Type!),
            IInvocationOperation i => EmitInvocation(i),
            IObjectCreationOperation o => EmitObjectCreation(o),
            ITypeParameterObjectCreationOperation o => EmitNewT(o),
            IArrayCreationOperation a => EmitArrayCreation(a),
            ICollectionExpressionOperation c => EmitCollectionExpression(c),
            IInterpolatedStringOperation s => EmitInterpolatedString(s),
            IIsNullOperation n when TypeMapper.IsNullable(n.Operand.Type) => _b.Not(NullableHas(EmitValue(n.Operand))),
            IIsNullOperation n when TypeOf(n.Operand) is { IsValueType: true, TypeKind: not (TypeKind.Pointer or TypeKind.FunctionPointer) }
                => L.False, // a (non-nullable) struct is never null
            IIsNullOperation n when TypeOf(n.Operand) is { IsValueType: true } vt && !TypeMapper.IsNullable(vt) => EmitValueThenFalse(n.Operand),
            IIsNullOperation n => _b.ICmp(LLVMIntPredicate.EQ, EmitValue(n.Operand), LlvmContext.Null(L.Ptr)),
            IIsTypeOperation t => EmitIsType(t),
            IAnonymousObjectCreationOperation a => EmitAnonymousCreation(a),
            IIsPatternOperation p => PatternTest(EmitValue(p.Value), TypeOf(p.Value), p.Pattern),
            IDefaultValueOperation d => LlvmContext.Null(T.Value(TypeOf(d)!)),
            IExpressionStatementOperation e => EmitValue(e.Operation),
            ITranslatedQueryOperation q => EmitValue(q.Operation), // query syntax: the lowered method-call chain
            ICaughtExceptionOperation c => EmitCaughtException(c),
            IDeclarationExpressionOperation d => EmitValue(d.Expression),
            ILocalFunctionOperation => default, // body is lowered separately (ModuleCodeGen.EnqueueNested)
            IDelegateCreationOperation d => EmitDelegateCreation(d),
            IAwaitOperation a => EmitAwait(a),
            IEmptyOperation => default,
            IAnonymousFunctionOperation or IFlowAnonymousFunctionOperation =>
                throw CompileError.Unsupported("lambda not converted to a delegate (e.g. expression trees)", op),
            IInterpolatedStringHandlerCreationOperation =>
                throw CompileError.Unsupported("interpolated string handlers", op),
            IDeconstructionAssignmentOperation d => EmitDeconstruction(d),
            ITupleOperation t when TypeOf(t) is INamedTypeSymbol { IsTupleType: true } tupleType => BuildTuple(t, tupleType),
            ITupleOperation => throw CompileError.Unsupported("tuple without a natural type", op),
            ITupleBinaryOperation t => EmitTupleBinary(t),
            IAddressOfOperation a => EmitAddressOf(a),
            ISizeOfOperation s => EmitSizeOf(s),
            IFunctionPointerInvocationOperation f => EmitFunctionPointerInvocation(f),
            IUtf8StringOperation u => EmitUtf8String(u),
            IEventReferenceOperation e => EmitEventReference(e),
            IEventAssignmentOperation e => EmitEventAssignment(e),
            { Kind: OperationKind.None } => EmitNone(op),
            // With nullable analysis on (e.g. a `#nullable enable` region), Roslyn's CFG leaves
            // `lock (System.Threading.Lock)` in lambdas as Invalid(lockExpr) + body, losing the lock. The driver
            // lowers with the nullable context disabled; a source directive can still re-enable it.
            IInvalidOperation { Syntax.Parent: Microsoft.CodeAnalysis.CSharp.Syntax.LockStatementSyntax } =>
                throw CompileError.Unsupported("`lock` on a System.Threading.Lock inside a '#nullable enable' region (Roslyn CFG limitation; lock on an object, or disable nullable there)", op),
            IInvalidOperation => throw CompileError.Unsupported("invalid code", op),
            _ => throw CompileError.Unsupported($"operation {op.Kind}", op),
        };
    }

    private LLVMValueRef NullCheckValue(LLVMValueRef reference)
    {
        NullCheck(reference);
        return default;
    }

    private LLVMValueRef EmitValueThenFalse(IOperation operand)
    {
        EmitValue(operand);
        return L.False;
    }

    private LLVMValueRef LoadCapture(IFlowCaptureReferenceOperation r)
    {
        var rType = TypeOf(r);
        if (_cfg!.PropertyCaptures.TryGetValue(r.Id, out var propertyCapture)) return GetCapturedProperty(propertyCapture);
        if (_cfg!.LvalueCaptures.Contains(r.Id)) return LoadValue(CaptureAddress(r), rType!);
        var (slot, type) = CaptureSlot(r.Id, rType);
        if (type is null) return _b.Load(L.Ptr, slot);
        var value = LoadValue(slot, type);
        return rType is null ? value : Coerce(value, type, rType);
    }

    // ======================================================================================
    // Constants
    // ======================================================================================

    internal LLVMValueRef Constant(object? value, ITypeSymbol? type, IOperation? at = null)
    {
        if (value is null) return LlvmContext.Null(type is null ? L.Ptr : T.Value(type));
        if (value is string s) return _m.StringLiteral(s);
        if (value is decimal dec) return DecimalConstant(dec);

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
        IFieldReferenceOperation { Field: { IsStatic: true, Name: "Zero", ContainingType.SpecialType: SpecialType.System_IntPtr or SpecialType.System_UIntPtr } } z =>
            Spill(LlvmContext.Null(T.Value(z.Field.Type))),
        IFieldReferenceOperation f => FieldAddress(f),
        IArrayElementReferenceOperation a => ElementAddress(a),
        IDeclarationExpressionOperation d => AddressOf(d.Expression),
        IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.PatternInput } => Spill(PatternInputValue(target)),
        IInstanceReferenceOperation when InStructMember => _this, // `this = other;` in a struct
        IDiscardOperation d => NewTemp(T.Storage(TypeOf(d)!), "discard"),
        IFlowCaptureReferenceOperation c when _cfg!.LvalueCaptures.Contains(c.Id) => CaptureAddress(c),
        IPropertyReferenceOperation { Property.RefKind: not RefKind.None } p => RefPropertyAddress(p),
        IImplicitIndexerReferenceOperation ii => ImplicitIndexerAddress(ii),
        IInvocationOperation { TargetMethod.RefKind: not RefKind.None } i => EmitInvocation(i),
        _ when TryAddressOfNone(target, out var pointee) => pointee,
        _ => throw CompileError.Unsupported($"assignment to {target.Kind}", target),
    };

    private LLVMValueRef CaptureAddress(IFlowCaptureReferenceOperation r) =>
        _b.Load(L.Ptr, _cfg!.Captures.TryGetValue(r.Id, out var c) ? c.Slot : throw CompileError.Unsupported("capture used before definition", r));

    private LLVMValueRef FieldAddress(IFieldReferenceOperation f)
    {
        var field = S(f.Field);
        if (field.IsStatic) return _m.StaticField(field);
        if (field.ContainingType.IsValueType) return FieldAddressOn(AddressOrSpill(f.Instance!), field);
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
        if (a.IsRef) return EmitRefAssignment(a);
        if (a.Target is IPropertyReferenceOperation { Property.RefKind: RefKind.None } p) return EmitPropertySet(p, () => EmitValue(a.Value), TypeOf(a.Value));
        if (a.Target is IFlowCaptureReferenceOperation r && _cfg!.PropertyCaptures.TryGetValue(r.Id, out var pc))
            return SetCapturedProperty(pc, Coerce(EmitValue(a.Value), TypeOf(a.Value), pc.Property.Type));

        var targetType = TypeOf(a.Target)!;
        var address = AddressOf(a.Target);           // C#: target sub-expressions are evaluated first
        var value = Coerce(EmitValue(a.Value), TypeOf(a.Value), targetType);
        if (a.Target is IFieldReferenceOperation { Field.IsVolatile: true }) VolatileStore(value, address, targetType);
        else StoreValue(ToStorage(value, targetType), address);
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
        if (target is ITupleOperation tupleTargets && value is not ITupleOperation)
            return EmitTupleValueDeconstruction(tupleTargets, d.Value);
        if (target is not ITupleOperation targets || value is not ITupleOperation values || targets.Elements.Length != values.Elements.Length
            || targets.Elements.Any(e => e is ITupleOperation))
            throw CompileError.Unsupported("deconstruction other than (a, b, ...) = (x, y, ...)", d);

        var plan = targets.Elements.Select(PlanTarget).ToArray();
        var results = values.Elements.Select((v, i) => targets.Elements[i] is IDiscardOperation
            ? EmitValue(v)
            : Coerce(EmitValue(v), TypeOf(v), TypeOf(targets.Elements[i])!)).ToArray();
        for (var i = 0; i < plan.Length; i++)
            if (targets.Elements[i] is not IDiscardOperation) StoreTarget(plan[i], results[i], TypeOf(targets.Elements[i])!);
        return default;
    }

    /// <summary>`ref T x = ref expr;` / `x = ref y;` — the ref local's pointer slot gets the location's address.</summary>
    private LLVMValueRef EmitRefAssignment(ISimpleAssignmentOperation a)
    {
        if (a.Target is not ILocalReferenceOperation { Local.IsRef: true } refLocal)
            throw CompileError.Unsupported("ref assignment to anything but a ref local", a);
        var address = AddressOf(a.Value);
        _b.Store(address, RefLocalPointer(refLocal.Local));
        return IsAggregate(a.Type) || a.Type is null ? default : LoadValue(address, a.Type);
    }

    /// <summary>Read-modify-write on any assignable target (locals, fields, elements, properties).</summary>
    private LLVMValueRef ReadModifyWrite(IOperation target, Func<LLVMValueRef, (LLVMValueRef NewValue, LLVMValueRef Result)> modify)
    {
        var type = TypeOf(target)!;
        if (target is IFlowCaptureReferenceOperation r && _cfg!.PropertyCaptures.TryGetValue(r.Id, out var pc))
        {
            var (newValue, result) = modify(GetCapturedProperty(pc));
            SetCapturedProperty(pc, newValue);
            return result;
        }
        if (target is IPropertyReferenceOperation { Property.RefKind: RefKind.None } p)
        {
            // Evaluate the receiver once, then call get + set on it.
            var property = S(p.Property);
            var self = property.IsStatic ? default : Receiver(p.Instance!, property.GetMethod!);
            // Indexer arguments are evaluated once, then shared by the get and set calls (c[i++] += v).
            var savedArgs = _argumentCache;
            if (!p.Arguments.IsEmpty)
            {
                _argumentCache = new Dictionary<IArgumentOperation, LLVMValueRef>(ReferenceEqualityComparer.Instance);
                foreach (var argument in p.Arguments) _argumentCache[argument] = EmitArgument(argument, property.GetMethod!);
            }
            var old = CallAccessor(p, property.GetMethod!, self, []);
            var (newValue, result) = modify(old);
            CallAccessor(p, property.SetMethod ?? throw CompileError.Unsupported("compound assignment to a get-only property", p), self, [newValue]);
            _argumentCache = savedArgs;
            return result;
        }
        var address = AddressOf(target);
        var (updated, value) = modify(LoadValue(address, type));
        StoreValue(ToStorage(updated, type), address);
        return value;
    }

    private LLVMValueRef EmitCompoundAssignment(ICompoundAssignmentOperation c)
    {
        if (TryDelegateCompound(c) is { } combined) return combined;
        if (IsPointer(c.Target.Type))
            return ReadModifyWrite(c.Target, old =>
            {
                var moved = PointerArithmetic(c.OperatorKind, old, EmitValue(c.Value), c.Target.Type!, c.Value.Type!, c);
                return (moved, moved);
            });
        if (c.OperatorMethod is { } om && !IsBuiltInStringConcat(om) && !IsGenericMathOnPrimitive(om, c.Target))
        {
            // `x op= y` with a user-defined static operator: x = op(x, y).
            om = S(om);
            if (!om.IsStatic || om.Parameters.Length != 2)
                throw CompileError.Unsupported($"compound assignment with operator '{om}'", c);
            if (!_m.IsUserMethod(om))
                return ReadModifyWrite(c.Target, old =>
                {
                    if (!BclValueTypes.TryGet(om, out var handler)) throw CompileError.Unsupported($"compound assignment with operator '{om}'", c);
                    var r = handler(this, om, default, [Coerce(old, c.Target.Type, om.Parameters[0].Type),
                        Coerce(EmitValue(c.Value), c.Value.Type, om.Parameters[1].Type)], c);
                    r = Coerce(r, om.ReturnType, c.Target.Type!);
                    return (r, r);
                });
            return ReadModifyWrite(c.Target, old =>
            {
                var result = DirectCall(om, [Coerce(old, TypeOf(c.Target), om.Parameters[0].Type),
                    Coerce(EmitValue(c.Value), TypeOf(c.Value), om.Parameters[1].Type)]);
                result = Coerce(result, om.ReturnType, TypeOf(c.Target)!);
                return (result, result);
            });
        }
        var targetType = TypeOf(c.Target)!;
        var valueType = TypeOf(c.Value)!;
        if (c.IsLifted)
            return ReadModifyWrite(c.Target, old =>
            {
                var r = LiftedArithmetic(c.OperatorKind, old, targetType, EmitValue(c.Value), valueType, targetType, c);
                return (r, r);
            });
        return ReadModifyWrite(c.Target, old =>
        {
            if (c.OperatorKind == BinaryOperatorKind.Add && targetType.SpecialType == SpecialType.System_String)
            {
                var concat = ConcatStrings(old, ToStringValue(c.Value));
                return (concat, concat);
            }
            // e.g. `byte b += 1` computes in int, then narrows back (OutConversion).
            var opType = OperationTypeFor(targetType, valueType, c.OperatorKind);
            var left = Coerce(old, targetType, opType);
            var right = c.OperatorKind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift
                ? EmitValue(c.Value)
                : Coerce(EmitValue(c.Value), valueType, opType);
            var computed = Arithmetic(c.OperatorKind, left, right, opType, valueType, c, c.IsChecked);
            var result = c.IsChecked && !SymbolEqualityComparer.Default.Equals(opType, targetType)
                ? ConvertNumeric(computed, opType, targetType, c, isChecked: true)
                : Coerce(computed, opType, targetType);
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
        if (op.OperatorMethod is { } incrementMethod && !_m.IsUserMethod(incrementMethod)
            && BclValueTypes.TryGet(S(incrementMethod), out var libraryIncrement))
            return ReadModifyWrite(op.Target, old =>
            {
                var updated = libraryIncrement(this, S(incrementMethod), default, [old], op);
                return (updated, op.IsPostfix ? old : updated);
            });
        if (op.OperatorMethod is { } userIncrement && _m.IsUserMethod(S(userIncrement)))
            return ReadModifyWrite(op.Target, old =>
            {
                var updated = DirectCall(S(userIncrement), [old]); // static T operator ++(T)
                return (updated, op.IsPostfix ? old : updated);
            });
        if (op.OperatorMethod is not null) throw CompileError.Unsupported("user-defined ++/--", op);
        var type = TypeOf(op.Target)!;
        var increment = op.Kind == OperationKind.Increment;
        if (IsPointer(type))
            return ReadModifyWrite(op.Target, old =>
            {
                var moved = PointerStep(old, type, increment ? 1 : -1);
                return (moved, op.IsPostfix ? old : moved);
            });
        if (TypeMapper.IsNullable(type))
            return ReadModifyWrite(op.Target, old =>
            {
                var (has, payload, underlying) = NullableParts(old, type);
                var one = T.Value(underlying).IsFloatingPoint ? LlvmContext.ConstReal(payload.Type, 1) : L.ConstInt(payload.Type, 1);
                var opcode = (payload.Type.IsFloatingPoint, increment) switch
                {
                    (true, true) => LLVMOpcode.FAdd, (true, false) => LLVMOpcode.FSub, (false, true) => LLVMOpcode.Add, _ => LLVMOpcode.Sub,
                };
                var updated = MakeNullable(type, has, _b.BinOp(opcode, payload, one));
                return (updated, op.IsPostfix ? old : updated);
            });
        if (BclValueTypes.IsDecimal(type))
            return ReadModifyWrite(op.Target, old =>
            {
                var updated = DecimalArithmetic(increment ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract, old, DecimalOne, op);
                return (updated, op.IsPostfix ? old : updated);
            });
        return ReadModifyWrite(op.Target, old =>
        {
            var llvmType = T.Value(type);
            var updated = llvmType.IsFloatingPoint
                ? _b.BinOp(increment ? LLVMOpcode.FAdd : LLVMOpcode.FSub, old, LlvmContext.ConstReal(llvmType, 1))
                : op.IsChecked
                    ? CheckedArithmetic(increment ? "add" : "sub", old, L.ConstInt(llvmType, 1), TypeMapper.IsSigned(type))
                    : _b.BinOp(increment ? LLVMOpcode.Add : LLVMOpcode.Sub, old, L.ConstInt(llvmType, 1));
            return (updated, op.IsPostfix ? old : updated);
        });
    }

    // ======================================================================================
    // Operators
    // ======================================================================================

    /// <summary>
    /// Generic math: `a + b` on `T : INumber&lt;T&gt;` binds to a static abstract interface operator. Once
    /// T is substituted with a primitive, it's just the built-in operator.
    /// </summary>
    private bool IsGenericMathOnPrimitive(IMethodSymbol op, IOperation operand) =>
        op.IsStatic && op.ContainingType.TypeKind == TypeKind.Interface
        && TypeOf(operand) is { SpecialType: not SpecialType.None } t && t.SpecialType != SpecialType.System_Object;

    private static bool IsBuiltInStringConcat(IMethodSymbol method) =>
        method.ContainingType.SpecialType == SpecialType.System_String && method.Name is "op_Addition" or "Concat";

    private LLVMValueRef EmitBinary(IBinaryOperation op)
    {
        // User type implementing a static abstract operator (generic math on user types).
        if (op.OperatorMethod is { } staticOp && ResolveStaticInterfaceMember(S(staticOp), op.ConstrainedToType) is { } opImpl)
            return EmitDirectCall(opImpl, [op.LeftOperand, op.RightOperand]);
        if (TryDelegateBinary(op) is { } delegates) return delegates;
        if (op.OperatorMethod is { } om0 && !IsBuiltInStringConcat(om0) && om0.ContainingType.SpecialType != SpecialType.System_String
            && om0.ContainingType.SpecialType is not (SpecialType.System_Delegate or SpecialType.System_MulticastDelegate)
            && !IsGenericMathOnPrimitive(om0, op.LeftOperand))
        {
            var om = S(om0);
            if (!_m.IsUserMethod(om)) return LibraryOperator(om, [op.LeftOperand, op.RightOperand], op);
            return EmitDirectCall(om, [op.LeftOperand, op.RightOperand]);
        }
        if (FoldValueTypeNullComparison(op) is { } folded) return folded;
        if (op.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            && NullableComparedWithNull(op) is { } nullableSide)
        {
            var has = NullableHas(EmitValue(nullableSide)); // `x == null` on a T?
            return op.OperatorKind == BinaryOperatorKind.Equals ? _b.Not(has) : has;
        }
        if (op.IsLifted)
            return LiftedArithmetic(op.OperatorKind, EmitValue(op.LeftOperand), TypeOf(op.LeftOperand)!,
                EmitValue(op.RightOperand), TypeOf(op.RightOperand)!, TypeOf(op)!, op);

        var lt = TypeOf(op.LeftOperand);
        var rt = TypeOf(op.RightOperand);
        if (op.OperatorKind == BinaryOperatorKind.Add && (lt?.SpecialType == SpecialType.System_String || rt?.SpecialType == SpecialType.System_String))
            return ConcatStrings(ToStringValue(op.LeftOperand), ToStringValue(op.RightOperand));

        if (op.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
            return ShortCircuit(op);

        var left = EmitValue(op.LeftOperand);
        var right = EmitValue(op.RightOperand);
        return Arithmetic(op.OperatorKind, left, right, lt ?? rt!, rt ?? lt!, op, op.IsChecked);
    }

    private static IOperation? NullableComparedWithNull(IBinaryOperation op)
    {
        static bool IsNull(IOperation o) => o.ConstantValue is { HasValue: true, Value: null };
        if (TypeMapper.IsNullable(op.LeftOperand.Type) && IsNull(op.RightOperand)) return op.LeftOperand;
        if (TypeMapper.IsNullable(op.RightOperand.Type) && IsNull(op.LeftOperand)) return op.RightOperand;
        return null;
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

    /// <summary>checked(a op b): llvm.{s,u}{add,sub,mul}.with.overflow + OverflowException.</summary>
    internal LLVMValueRef CheckedArithmetic(string op, LLVMValueRef l, LLVMValueRef r, bool signed)
    {
        var type = l.Type;
        var pair = L.Struct(type, L.I1);
        var f = _m.Intrinsic($"llvm.{(signed ? "s" : "u")}{op}.with.overflow.i{type.IntWidth}", pair, type, type);
        var result = _b.Call(f.Type, f.Fn, [l, r]);
        FailIf(_b.ExtractValue(result, 1), "csrt_throw_overflow");
        return _b.ExtractValue(result, 0);
    }

    internal LLVMValueRef Arithmetic(BinaryOperatorKind kind, LLVMValueRef l, LLVMValueRef r, ITypeSymbol lt, ITypeSymbol rt, IOperation at,
        bool isChecked = false)
    {
        if (BclValueTypes.IsDecimal(lt) || BclValueTypes.IsDecimal(rt)) return DecimalArithmetic(kind, l, r, at);
        if (IsPointer(lt) || IsPointer(rt)) return PointerArithmetic(kind, l, r, lt, rt, at);
        // DateTime comparisons (tuple ==, lifted operators) look at the ticks, never the Kind bits.
        if (BclValueTypes.IsDateTime(lt) && BclValueTypes.IsDateTime(rt))
            (l, r) = (BclValueTypes.TicksOf(this, lt, l), BclValueTypes.TicksOf(this, rt, r));
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
        if (isChecked && l.Type.IntWidth > 1)
        {
            switch (kind)
            {
                case BinaryOperatorKind.Add: return CheckedArithmetic("add", l, r, signed);
                case BinaryOperatorKind.Subtract: return CheckedArithmetic("sub", l, r, signed);
                case BinaryOperatorKind.Multiply: return CheckedArithmetic("mul", l, r, signed);
            }
        }
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
        if (op.OperatorMethod is { } om0 && !IsGenericMathOnPrimitive(om0, op.Operand))
        {
            var om = S(om0);
            if (!_m.IsUserMethod(om)) return LibraryOperator(om, [op.Operand], op);
            return EmitDirectCall(om, [op.Operand]);
        }
        if (op.IsLifted) return EmitLiftedUnary(op);
        var v = EmitValue(op.Operand);
        if (BclValueTypes.IsDecimal(TypeOf(op.Operand)))
            return op.OperatorKind == UnaryOperatorKind.Minus ? DecimalNegate(v) : v;
        if (op.IsChecked && op.OperatorKind == UnaryOperatorKind.Minus && v.Type.IsInteger)
            return CheckedArithmetic("sub", L.ConstInt(v.Type, 0), v, TypeMapper.IsSigned(op.Operand.Type!));
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
        var from = TypeOf(c.Operand);
        var to = TypeOf(c)!;
        // `(T)(object)x` in generic code where, after substitution, x is already a T: a no-op (no box/unbox).
        if (c.OperatorMethod is null && c.Operand is IConversionOperation { OperatorMethod: null } inner
            && TypeOf(inner)?.SpecialType == SpecialType.System_Object && TypeOf(inner.Operand) is { } original
            && SymbolEqualityComparer.Default.Equals(TypeSubstitution.EraseTupleNames(original, _m.Compilation), TypeSubstitution.EraseTupleNames(to, _m.Compilation)))
            return EmitValue(inner.Operand);
        if (TrySpanConversion(c) is { } span) return span;
        if (TryIndexConversion(c) is { } index) return index;
        if (c.OperatorMethod is { } om0)
        {
            var om = S(om0);
            if (!_m.IsUserMethod(om)) return LibraryOperator(om, [c.Operand], c);
            return EmitDirectCall(om, [c.Operand]);
        }
        if (c.Conversion.IsIdentity || (from is not null && SymbolEqualityComparer.Default.Equals(from, to))) return EmitValue(c.Operand);
        // foreach over T[]: `(V)enumerator.Current` (object) becomes a typed element load.
        if (c.Operand is IPropertyReferenceOperation { Property.Name: "Current" } current
            && TryArrayEnumerator(current.Instance, out var elementType))
        {
            var element = ArrayEnumeratorCurrent(EmitValue(current.Instance!), elementType);
            return elementType.IsReferenceType ? element : Coerce(element, elementType, to);
        }
        // ...and the foreach's finally tests `enumerator as IDisposable`: array enumerators never are.
        if (TryArrayEnumerator(c.Operand, out _)) return LlvmContext.Null(L.Ptr);
        if (to is INamedTypeSymbol { IsTupleType: true } toTuple && c.Operand is ITupleOperation tupleLiteral)
            return BuildTuple(tupleLiteral, toTuple);
        if (from is null) return Constant(null, to, c); // null literal / default
        // T[] / string used as a BCL interface: make sure the corelib helper tables exist.
        if (to is INamedTypeSymbol { TypeKind: TypeKind.Interface } targetInterface)
        {
            if (from is IArrayTypeSymbol { Rank: 1 }) _m.RequestArrayInterface(targetInterface);
            else if (from.SpecialType == SpecialType.System_String) _m.RequestStringInterface(targetInterface);
        }
        if (TryNullableConversion(c, from, to) is { } nullable) return nullable;
        if (IsAggregate(from) && IsAggregate(to)) return Coerce(EmitValue(c.Operand), from, to);
        if (from.IsValueType && to.IsReferenceType) return Box(EmitValue(c.Operand), from, c);
        if (from.IsReferenceType && to.IsValueType)
        {
            var payload = CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], EmitValue(c.Operand), MethodTableFor(to, c));
            return LoadValue(payload, to);
        }

        var value = EmitValue(c.Operand);
        if (from.IsReferenceType)
        {
            // After substitution the definition's classification may not apply (T -> U), so re-classify.
            var conversion = _subst.IsIdentity ? c.Conversion : _m.Compilation.ClassifyCommonConversion(from, to);
            if (conversion.IsImplicit && (conversion.IsReference || conversion.IsIdentity)) return value; // upcast: same pointer
            if (c.IsTryCast) return CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], value, MethodTableFor(to, c));
            return CastClass(value, to, c);
        }
        return ConvertNumeric(value, from, to, c, c.IsChecked && !c.Conversion.IsImplicit);
    }

    /// <summary>Converts between types that may differ only in representation (numeric/enum), else passes through.</summary>
    internal LLVMValueRef Coerce(LLVMValueRef value, ITypeSymbol? from, ITypeSymbol to)
    {
        if (from is null || SymbolEqualityComparer.Default.Equals(from, to)) return value;
        if (from.IsReferenceType || to.IsReferenceType)
        {
            // Implicit T[]/string -> BCL interface (argument passing, spreads...): ensure the corelib slot tables exist.
            if (to is INamedTypeSymbol { TypeKind: TypeKind.Interface } itf)
            {
                if (from is IArrayTypeSymbol { Rank: 1 }) _m.RequestArrayInterface(itf);
                else if (from.SpecialType == SpecialType.System_String) _m.RequestStringInterface(itf);
            }
            return value;
        }
        if (IsAggregate(from) || IsAggregate(to)) return CoerceAggregate(value, from, to);
        return ConvertNumeric(value, from, to, null);
    }

    /// <summary>checked((T)x): OverflowException unless x is representable in T (NaN never is).</summary>
    private void CheckConversionRange(LLVMValueRef value, ITypeSymbol from, ITypeSymbol to)
    {
        var src = value.Type;
        var dst = T.Value(to);
        if (!dst.IsInteger) return; // to float/double never overflows in C#
        var (srcSigned, dstSigned) = (TypeMapper.IsSigned(from), TypeMapper.IsSigned(to));
        var width = (int)dst.IntWidth;
        if (src.IsFloatingPoint)
        {
            double lo = dstSigned ? -Math.Pow(2, width - 1) - 1 : -1;
            double hi = dstSigned ? Math.Pow(2, width - 1) : Math.Pow(2, width);
            if (width == 64 && dstSigned) lo = -9223372036854777856.0; // next double below -2^63
            var ok = _b.BinOp(LLVMOpcode.And,
                _b.FCmp(LLVMRealPredicate.OGT, value, LlvmContext.ConstReal(src, lo)),
                _b.FCmp(LLVMRealPredicate.OLT, value, LlvmContext.ConstReal(src, hi)));
            FailIf(_b.Not(ok), "csrt_throw_overflow");
            return;
        }
        var sw = (int)src.IntWidth;
        LLVMValueRef fail = L.False;
        if (width < sw || (width == sw && srcSigned != dstSigned))
        {
            var narrowed = width < sw ? _b.Cast(LLVMOpcode.Trunc, value, dst) : value;
            var back = width < sw ? _b.Cast(dstSigned ? LLVMOpcode.SExt : LLVMOpcode.ZExt, narrowed, src) : value;
            fail = _b.ICmp(LLVMIntPredicate.NE, back, value);
            if (srcSigned != dstSigned)
            {
                var negative = srcSigned
                    ? _b.ICmp(LLVMIntPredicate.SLT, value, L.ConstInt(src, 0))
                    : _b.ICmp(LLVMIntPredicate.SLT, narrowed, L.ConstInt(dst, 0));
                fail = _b.BinOp(LLVMOpcode.Or, fail, negative);
            }
        }
        else if (srcSigned && !dstSigned)
            fail = _b.ICmp(LLVMIntPredicate.SLT, value, L.ConstInt(src, 0)); // widening signed -> unsigned
        else return;
        FailIf(fail, "csrt_throw_overflow");
    }

    internal LLVMValueRef ConvertNumeric(LLVMValueRef value, ITypeSymbol from, ITypeSymbol to, IOperation? at, bool isChecked = false)
    {
        if (BclValueTypes.IsDecimal(from) || BclValueTypes.IsDecimal(to)) return DecimalConvert(value, from, to, at);
        var src = value.Type;
        var dst = T.Value(to);
        if (isChecked) CheckConversionRange(value, from, to);
        if (src.Handle == dst.Handle) return value;
        if (src.IsPointer && dst.IsInteger) return _b.Cast(LLVMOpcode.PtrToInt, value, dst);
        if (src.IsInteger && dst.IsPointer)
            return _b.Cast(LLVMOpcode.IntToPtr, src.IntWidth == 64 ? value : _b.Cast(TypeMapper.IsSigned(from) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, L.I64), dst);

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
