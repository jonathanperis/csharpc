using System.Collections.Immutable;
using System.Reflection.Metadata;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// IL call, callvirt and newobj. Calls are handed to the source path's lowering as synthetic
/// invocations / object creations / property references (IlOperations.cs), so an IL call site reaches
/// exactly the same intrinsics, corelib shadows, interface dispatch and devirtualization as C# source.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private void IlCall(IlInstruction instruction, Stack<IlEntry> stack, ITypeSymbol? constrained, bool isReadonly = false)
    {
        var op = instruction.OpCode;
        if (IlAssembly.TryArrayAccessor(instruction.Token, IlBody.Context, out var mdArray, out var accessor, out var parameterCount))
        {
            IlMdArrayAccessor(op, mdArray, accessor, parameterCount, stack, isReadonly);
            return;
        }
        var method = IlMethod(instruction.Token);
        if (method.IsVararg) throw new IlException($"vararg call to '{method}'");
        // csc's `dynamic` call sites (Binder.InvokeMember & co. + CallSite<T>): csharpc binds dynamic operations at
        // compile time from C# source (docs/adr/0019-dynamic.md); compiling Microsoft.CSharp's run-time binder from
        // IL instead would take minutes and still need Reflection.Emit. Fail here, before any of it is lowered.
        if (method.ContainingType is { Name: "Binder", ContainingNamespace: { Name: "RuntimeBinder", ContainingNamespace: { Name: "CSharp", ContainingNamespace.Name: "Microsoft" } } })
            throw new IlException($"'dynamic' in compiled code (the call site of Microsoft.CSharp.RuntimeBinder.Binder.{method.Name}): csharpc binds the call sites csc creates, and this one isn't one of them");
        var args = new IlEntry[method.Parameters.Length];
        for (var i = args.Length - 1; i >= 0; i--) args[i] = stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");
        IlEntry? receiver = null;
        if (op != ILOpCode.Newobj && !method.IsStatic)
            receiver = stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");
        if (receiver?.Dynamic is { } dynamicSite)
        {
            IlDynamicInvoke(method, dynamicSite, args, stack);
            return;
        }

        if (args.Any(a => a is { Kind: IlKind.Ref, Symbol: ITypeSymbol }))
            _m.NoteDynamicallyAccessedArguments(S(method), i => args[i] is { Kind: IlKind.Ref, Symbol: ITypeSymbol type } ? type : null);
        IlCheckExceptionText(method, receiver, args);
        if (IlSpecialCall(method, receiver, args) is { } special)
        {
            if (special.Kind != IlKind.Token) stack.Push(special); // Token = handled, nothing to push
            return;
        }
        if (op == ILOpCode.Newobj)
        {
            stack.Push(IlNewObject(method, args));
            return;
        }
        if (method.MethodKind == MethodKind.Constructor)
        {
            IlConstructorCall(method, receiver!, args);
            return;
        }
        if (IlInvoke(method, receiver, args, isVirtual: op == ILOpCode.Callvirt, constrained) is { } result) stack.Push(result);
    }

    /// <summary>
    /// The runtime-provided methods of a rectangular array type (ECMA-335 II.14.2): `newobj T[,]::.ctor(int32, int32)`,
    /// `call T[,]::Get/Set/Address`. Lower bounds (the 2 x rank constructor) are not supported.
    /// </summary>
    private void IlMdArrayAccessor(ILOpCode op, IArrayTypeSymbol array, string name, int parameterCount, Stack<IlEntry> stack, bool isReadonly)
    {
        var arrayType = (IArrayTypeSymbol)S(array);
        if (arrayType.IsSZArray) throw new IlException($"array accessor '{array}::{name}' on a vector");
        var rank = arrayType.Rank;
        var element = arrayType.ElementType;
        IlEntry Pop() => stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");
        LLVMValueRef[] PopIndices(int count)
        {
            var values = new LLVMValueRef[count];
            for (var i = count - 1; i >= 0; i--) values[i] = IlToI64(Pop(), signed: true);
            return values;
        }
        switch (name)
        {
            case ".ctor" when op == ILOpCode.Newobj && parameterCount == rank:
                stack.Push(new IlEntry(IlKind.Ref, NewMdArray(arrayType, PopIndices(rank)), array) { NonNull = true });
                return;
            case "Get" when parameterCount == rank:
            {
                var indices = PopIndices(rank);
                var target = IlRef(Pop());
                stack.Push(IlPush(LoadValue(MdElementAddress(target, arrayType, indices), element), array.ElementType));
                return;
            }
            case "Set" when parameterCount == rank + 1:
            {
                var value = Pop();
                var indices = PopIndices(rank);
                var target = IlRef(Pop());
                var address = MdElementAddress(target, arrayType, indices);
                var stored = IlTyped(value, array.ElementType);
                ArrayStoreCheck(target, arrayType, stored); // as stelem.ref: a string[,] may be an object[,]
                StoreValue(ToStorage(stored, element), address);
                return;
            }
            case "Address" when parameterCount == rank:
            {
                var indices = PopIndices(rank);
                var target = IlRef(Pop());
                var address = MdElementAddress(target, arrayType, indices);
                if (!isReadonly) ArrayExactTypeCheck(target, arrayType); // as ldelema
                stack.Push(new IlEntry(IlKind.ByRef, address, array.ElementType));
                return;
            }
        }
        throw new IlException($"multi-dimensional array accessor '{array}::{name}' with {parameterCount} arguments");
    }

    private ImmutableArray<IArgumentOperation> IlArguments(ImmutableArray<IParameterSymbol> parameters, IlEntry[] args, int offset = 0)
    {
        var builder = ImmutableArray.CreateBuilder<IArgumentOperation>(parameters.Length);
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var arg = args[offset + i];
            if (parameter.RefKind != RefKind.None && arg.Kind is not (IlKind.ByRef or IlKind.Pointer or IlKind.NativeInt))
                throw new IlException($"by-ref argument '{parameter.Name}' passed a {arg.Kind} value");
            var value = parameter.RefKind != RefKind.None
                ? IlOperand(arg, parameter.Type, lvalue: true)
                : IlOperand(arg, parameter.Type);
            builder.Add(new IlArgumentOperation(_il!.Syntax, parameter, value));
        }
        return builder.MoveToImmutable();
    }

    /// <summary>The receiver operand of an instance call, shaped like the source path expects.</summary>
    private IlValueOperation IlReceiver(IMethodSymbol method, IlEntry receiver, ITypeSymbol? constrained, bool baseCall)
    {
        var il = _il!;
        if (constrained is not null)
        {
            // `constrained. T callvirt M`: the receiver is a managed pointer to a T. After substitution a
            // reference-type T loads the object; a value type is called in place (or boxed) by the source path.
            if (receiver.Kind is not (IlKind.ByRef or IlKind.Pointer)) throw new IlException("constrained call without a managed pointer receiver");
            return IlOperand(receiver, constrained, lvalue: true);
        }
        if (method.ContainingType.IsValueType) return IlOperand(receiver, method.ContainingType, lvalue: true);
        var type = receiver.Type is { } t && S(t) is { IsReferenceType: true } && receiver.Kind == IlKind.Ref ? t : method.ContainingType;
        if (baseCall)
        {
            var value = receiver.Value;
            return new IlValueOperation(il.BaseSyntax, type, () => value, nonNull: true);
        }
        return IlOperand(receiver, type);
    }

    private IlEntry? IlInvoke(IMethodSymbol method, IlEntry? receiver, IlEntry[] args, bool isVirtual, ITypeSymbol? constrained)
    {
        var il = _il!;
        var mapped = S(method);
        // A non-virtual call to a virtual method on `this` is C#'s `base.M()`.
        var baseCall = !isVirtual && receiver is { IsThis: true } && (method.IsVirtual || method.IsOverride || method.IsAbstract);
        var instance = receiver is null ? null : IlReceiver(method, receiver, constrained, baseCall);
        // An interface call on an array (IL converts T[] / Array to IList implicitly, without an instruction):
        // the arrays' interface maps must include it, as for a source conversion.
        if (receiver is { Type: { } receiverType } && !method.IsStatic && method.ContainingType.TypeKind == TypeKind.Interface
            && S(receiverType) is IArrayTypeSymbol or { SpecialType: SpecialType.System_Array } && S(method.ContainingType) is INamedTypeSymbol arrayInterface)
            _m.RequestArrayInterface(arrayInterface);
        if (args is [{ Constant: string typeName }, ..] && IsGetTypeByName(method)) _m.NoteTypeByName(typeName);
        if (receiver is { Symbol: ITypeSymbol reflected }) NoteReflectedType(method, S(reflected), args is [{ Constant: string name }, ..] ? name : null);

        // Library operators and conversions (decimal + decimal, (long)BigInteger ...): the source path's operator lowering.
        if (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion && method.IsStatic && !_m.IsUserMethod(mapped)
            && BclValueTypes.TryGet(mapped, out _))
        {
            var operands = method.Parameters.Select((p, i) => (IOperation)IlOperand(args[i], p.Type)).ToArray();
            return IlPush(LibraryOperator(mapped, operands, IlAt()), method.ReturnType);
        }

        // Span conversions (string -> ReadOnlySpan<char>, T[] -> Span<T>, Span<T> -> ReadOnlySpan<T>) and
        // int -> Index: the source path implements them as conversions, not as methods.
        if (method.MethodKind == MethodKind.Conversion && method.IsStatic && method.Parameters.Length == 1
            && IlSpecialConversion(method, mapped, args[0]) is { } converted)
            return converted;

        // Operators of built-in types called as methods (decimal.op_Addition, and generic math through a
        // constrained call: INumber<T>.op_Multiply with T = int): the source path's operator lowering.
        if (method.IsStatic && method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion
            && IlBuiltinOperator(method, mapped, args, constrained) is { } builtin)
            return builtin;

        // Library property accessors (string.Length, Console.Out, List<T>.Count on an interface...): the
        // source path implements them as properties, not as accessor methods.
        // (and generic math identities, T.Zero with T = decimal: the source path's numeric constants)
        if (method.AssociatedSymbol is IPropertySymbol property && !_m.IsUserMethod(mapped) && (method.ContainingType.TypeKind != TypeKind.Interface || (method.IsStatic && constrained is not null && property.Name is "Zero" or "One" or "AdditiveIdentity" or "MultiplicativeIdentity")))
        {
            if (method.MethodKind == MethodKind.PropertyGet)
            {
                var reference = new IlPropertyReferenceOperation(il.Syntax, property, instance, IlArguments(property.Parameters, args), constrained);
                if (property.RefKind != RefKind.None) return new IlEntry(IlKind.ByRef, RefPropertyAddress(reference), property.Type);
                return IlPush(EmitValue(reference), property.Type);
            }
            if (method.MethodKind == MethodKind.PropertySet)
            {
                var reference = new IlPropertyReferenceOperation(il.Syntax, property, instance, IlArguments(property.Parameters, args), constrained);
                var value = IlOperand(args[^1], property.Type);
                EmitPropertySet(reference, () => EmitValue(value), TypeOf(value));
                return null;
            }
        }

        var invocation = new IlInvocationOperation(il.Syntax, method, instance, isVirtual && !baseCall, IlArguments(method.Parameters, args), constrained);
        if (method.RefKind != RefKind.None) return new IlEntry(IlKind.ByRef, EmitInvocation(invocation), method.ReturnType);
        var result = EmitValue(invocation);
        if (method.ReturnsVoid) return null;
        if (result.IsNull)
        {
            // A library member lowered for its effect only (an intrinsic that produces no value):
            // fine when the IL discards the result.
            if (IlNextOpCode() == ILOpCode.Pop) return new IlEntry(IlKind.Value, LlvmContext.Null(L.I8), method.ReturnType);
            throw new IlException($"using the result of '{method}' is not supported by csharpc yet");
        }
        var pushed = IlPush(result, method.ReturnType);
        // Remember Delegate.Combine/Remove results: the cast that follows them cannot fail (see castclass).
        return method is { Name: "Combine" or "Remove", ContainingType.Name: "Delegate", IsStatic: true } ? pushed with { Symbol = method } : pushed;
    }

    private IlEntry? IlSpecialConversion(IMethodSymbol method, IMethodSymbol mapped, IlEntry arg)
    {
        var to = S(mapped.ReturnType);
        var from = S(mapped.Parameters[0].Type);
        if (TypeMapper.IsSpan(to))
        {
            if (TypeMapper.IsSpan(from)) return IlPush(IlTyped(arg, method.Parameters[0].Type), method.ReturnType);
            if (from is IArrayTypeSymbol { Rank: 1 } array)
                return IlPush(ArraySpan(IlTyped(arg, method.Parameters[0].Type), array.ElementType, writable: IsWritableSpan(to)), method.ReturnType);
            if (from.SpecialType == SpecialType.System_String) return IlPush(StringSpan(IlTyped(arg, method.Parameters[0].Type)), method.ReturnType);
            // Span<T>/ReadOnlySpan<T>.op_Implicit(ArraySegment<T>): MemoryExtensions.AsSpan(segment) (the corelib's).
            if (method.Parameters[0].Type is INamedTypeSymbol { Name: "ArraySegment", Arity: 1 } segment
                && _m.Compilation.GetTypeByMetadataName("System.MemoryExtensions") is { } extensions
                && extensions.GetMembers("AsSpan").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsGenericMethod && m.Parameters.Length == 1
                    && m.Parameters[0].Type.OriginalDefinition is INamedTypeSymbol { Name: "ArraySegment" }) is { } asSpan
                && IlInvoke(asSpan.Construct(segment.TypeArguments[0]), null, [arg], isVirtual: false, constrained: null) is { } span)
                return IlPush(span.Value, method.ReturnType); // ReadOnlySpan<T> has Span<T>'s layout
        }
        if (IsIndexType(to) && from.SpecialType == SpecialType.System_Int32)
        {
            var value = IlTyped(arg, method.Parameters[0].Type);
            CheckNonNegative(value);
            return IlPush(value, method.ReturnType);
        }
        return null;
    }

    private static readonly Dictionary<string, (BinaryOperatorKind Kind, bool Checked)> s_ilBinaryOperators = new()
    {
        ["op_Addition"] = (BinaryOperatorKind.Add, false), ["op_CheckedAddition"] = (BinaryOperatorKind.Add, true),
        ["op_Subtraction"] = (BinaryOperatorKind.Subtract, false), ["op_CheckedSubtraction"] = (BinaryOperatorKind.Subtract, true),
        ["op_Multiply"] = (BinaryOperatorKind.Multiply, false), ["op_CheckedMultiply"] = (BinaryOperatorKind.Multiply, true),
        ["op_Division"] = (BinaryOperatorKind.Divide, false), ["op_CheckedDivision"] = (BinaryOperatorKind.Divide, true),
        ["op_Modulus"] = (BinaryOperatorKind.Remainder, false),
        ["op_BitwiseAnd"] = (BinaryOperatorKind.And, false), ["op_BitwiseOr"] = (BinaryOperatorKind.Or, false),
        ["op_ExclusiveOr"] = (BinaryOperatorKind.ExclusiveOr, false), ["op_LeftShift"] = (BinaryOperatorKind.LeftShift, false),
        ["op_RightShift"] = (BinaryOperatorKind.RightShift, false), ["op_UnsignedRightShift"] = (BinaryOperatorKind.UnsignedRightShift, false),
        ["op_Equality"] = (BinaryOperatorKind.Equals, false), ["op_Inequality"] = (BinaryOperatorKind.NotEquals, false),
        ["op_LessThan"] = (BinaryOperatorKind.LessThan, false), ["op_LessThanOrEqual"] = (BinaryOperatorKind.LessThanOrEqual, false),
        ["op_GreaterThan"] = (BinaryOperatorKind.GreaterThan, false), ["op_GreaterThanOrEqual"] = (BinaryOperatorKind.GreaterThanOrEqual, false),
    };

    private static bool IsBuiltinOperand(ITypeSymbol type) =>
        type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Double or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    /// <summary>An operator method on built-in operand types (decimal, or a primitive T after substitution); null otherwise.</summary>
    private IlEntry? IlBuiltinOperator(IMethodSymbol method, IMethodSymbol mapped, IlEntry[] args, ITypeSymbol? constrained)
    {
        if (_m.IsUserMethod(mapped) || (constrained is not null && _m.IsUserType(S(constrained)))) return null;
        var types = mapped.Parameters.Select(p => S(p.Type)).ToArray();
        // IntPtr/UIntPtr's conversion operators (IL compiled before nint, or against netstandard): their
        // pointer overloads, and their integer ones are checked — (int)IntPtr throws OverflowException on
        // 64-bit, (IntPtr)long on 32-bit; the widening ones can't fail.
        var nativeIntOperator = mapped.ContainingType.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr
            && mapped.Name is "op_Explicit" or "op_Implicit" && types.Length == 1;
        if (nativeIntOperator && (IsBuiltinOperand(types[0]) || types[0] is IPointerTypeSymbol)
            && S(mapped.ReturnType) is var target && (IsBuiltinOperand(target) || target is IPointerTypeSymbol))
        {
            var operand = IlTyped(args[0], method.Parameters[0].Type);
            var bothIntegers = operand.Type.IsInteger && T.Value(target).IsInteger;
            return IlPush(ConvertNumeric(operand, types[0], target, IlAt(), isChecked: bothIntegers), method.ReturnType);
        }
        if (!types.All(IsBuiltinOperand)) return null;
        var values = method.Parameters.Select((p, i) => IlTyped(args[i], p.Type)).ToArray();
        var name = method.Name;
        if (s_ilBinaryOperators.TryGetValue(name, out var binary) && values.Length == 2)
            return IlPush(Arithmetic(binary.Kind, values[0], values[1], types[0], types[1], IlAt(), binary.Checked), method.ReturnType);
        if (values.Length != 1) return null;
        var (v, t) = (values[0], types[0]);
        LLVMValueRef One() => t.SpecialType == SpecialType.System_Decimal ? Constant(1m, t) : Constant(1, t);
        LLVMValueRef? result = name switch
        {
            "op_UnaryPlus" => v,
            "op_UnaryNegation" when t.SpecialType == SpecialType.System_Decimal => DecimalNegate(v),
            "op_UnaryNegation" => v.Type.IsFloatingPoint ? _b.FNeg(v) : _b.Neg(v),
            "op_CheckedUnaryNegation" when v.Type.IsInteger => CheckedArithmetic("sub", L.ConstInt(v.Type, 0), v, TypeMapper.IsSigned(t)),
            "op_OnesComplement" or "op_LogicalNot" when v.Type.IsInteger => _b.Not(v),
            "op_Increment" or "op_CheckedIncrement" => Arithmetic(BinaryOperatorKind.Add, v, One(), t, t, IlAt(), name.Contains("Checked")),
            "op_Decrement" or "op_CheckedDecrement" => Arithmetic(BinaryOperatorKind.Subtract, v, One(), t, t, IlAt(), name.Contains("Checked")),
            "op_Implicit" or "op_Explicit" or "op_CheckedExplicit" when IsBuiltinOperand(S(mapped.ReturnType)) =>
                ConvertNumeric(v, t, S(mapped.ReturnType), IlAt(), isChecked: name == "op_CheckedExplicit"),
            _ => null,
        };
        return result is { } r ? IlPush(r, method.ReturnType) : null;
    }

    private IlEntry IlNewObject(IMethodSymbol constructor, IlEntry[] args)
    {
        var il = _il!;
        var type = constructor.ContainingType;
        if (type.TypeKind == TypeKind.Delegate) return IlNewDelegate(type, args);
        var creation = new IlObjectCreationOperation(il.Syntax, constructor, IlArguments(constructor.Parameters, args));
        var value = EmitValue(creation);
        var entry = IlPush(value, type);
        return entry.Kind == IlKind.Ref ? entry with { NonNull = true } : entry;
    }

    /// <summary>`ldftn M` / `ldvirtftn M` + `newobj D::.ctor(object, native int)`: a delegate bound to M.</summary>
    private IlEntry IlNewDelegate(INamedTypeSymbol delegateType, IlEntry[] args)
    {
        var il = _il!;
        if (args.Length != 2 || args[1] is not { Kind: IlKind.MethodPtr, Symbol: IMethodSymbol method } pointer)
            throw new IlException($"delegate '{delegateType}' constructed from a function pointer that is not an ldftn/ldvirtftn result");
        var target = args[0];
        IOperation? instance = null;
        if (method.IsStatic)
        {
            // A static method closed over its first argument (`Func<int> f = s.ExtensionMethod`): a reference
            // first parameter takes the target as it is (FunctionCodeGen.Closures.cs); others need a thunk.
            if (!(target.Kind == IlKind.Ref && target.Type is null))
            {
                if (method.Parameters is not [{ RefKind: RefKind.None } first, ..] || !S(first.Type).IsReferenceType)
                    throw new IlException($"delegate to static method '{method}' closed over its first argument");
                instance = IlOperand(target, first.Type);
            }
        }
        else instance = IlOperand(target, target.Type is { } t && S(t).IsReferenceType ? t : method.ContainingType);
        var reference = new IlMethodReferenceOperation(il.Syntax, method, instance, isVirtual: pointer.Target is not null);
        return new IlEntry(IlKind.Ref, EmitValue(new IlDelegateCreationOperation(il.Syntax, delegateType, reference)), delegateType) { NonNull = true };
    }

    /// <summary>`call .ctor` on an existing object/struct: base/this constructor chaining, or in-place struct construction.</summary>
    private void IlConstructorCall(IMethodSymbol constructor, IlEntry receiver, IlEntry[] args)
    {
        var il = _il!;
        var type = constructor.ContainingType;
        if (type.SpecialType == SpecialType.System_Object) return; // object's constructor does nothing
        var st = S(type);
        if (type.IsValueType && !_m.IsUserType(st))
        {
            // A library struct initialized in place (`ldloca s; ...; call instance void Span<T>::.ctor(...)`).
            var value = EmitValue(new IlObjectCreationOperation(il.Syntax, constructor, IlArguments(constructor.Parameters, args)));
            StoreValue(ToStorage(value, st), IlAddress(receiver));
            return;
        }
        var instance = IlReceiver(constructor, receiver, null, baseCall: false);
        EmitValue(new IlInvocationOperation(il.Syntax, constructor, instance, isVirtual: false, IlArguments(constructor.Parameters, args), null));
    }

    /// <summary>Calls whose meaning is an IL idiom rather than a library member (ldtoken consumers...); null if not one.</summary>
    private IlEntry? IlSpecialCall(IMethodSymbol method, IlEntry? receiver, IlEntry[] args)
    {
        var owner = method.ContainingType;
        var ownerName = owner.ToDisplayString();
        var handled = new IlEntry(IlKind.Token, default, null);
        if (ownerName == "System.Runtime.CompilerServices.Unsafe" && IlUnsafeCall(method, args) is { } unsafeResult) return unsafeResult;
        if (ownerName == "System.Runtime.CompilerServices.AsyncHelpers" && IlRuntimeAsyncCall(method, args) is { } awaited) return awaited;
        if (args.Length == 0 && receiver is null && FeatureSwitchValue(method) is { } feature)
            return I4(feature ? 1 : 0) with { Constant = feature ? 1 : 0 };
        // Vector<T>.Count: framework IL reads it next to its Vector.IsHardwareAccelerated test (false: the
        // scalar path runs, with the same results). 16 bytes, NativeAOT's default for x64 and arm64.
        if (method is { Name: "get_Count", Parameters.IsEmpty: true } && receiver is null
            && owner is { Name: "Vector", Arity: 1, ContainingNamespace: { Name: "Numerics", ContainingNamespace.Name: "System" } })
        {
            int count = 16 / Math.Max(1, (int)_m.Types.Target.SizeOf(T.Storage(S(owner.TypeArguments[0]))));
            return I4(count) with { Constant = count };
        }
        // `fixed (char* p = s)`: a reference to the first char (the terminator of an empty string), as in .NET;
        // the compiler tests for null before the call.
        // Span<T>/ReadOnlySpan<T>.GetPinnableReference (`fixed (T* p = span)`): the data pointer, or a null
        // reference for an empty span, as in .NET.
        if (method.Name == "GetPinnableReference" && args.Length == 0 && receiver is { } spanReceiver
            && S(owner) is INamedTypeSymbol pinnedSpan && TypeMapper.IsSpan(pinnedSpan) && !TypeMapper.IsMemory(pinnedSpan))
        {
            var span = spanReceiver.Kind is IlKind.ByRef or IlKind.Pointer ? LoadValue(IlAddress(spanReceiver), pinnedSpan) : IlTyped(spanReceiver, owner);
            var empty = _b.ICmp(LLVMIntPredicate.EQ, SpanLength(span), L.ConstInt(L.I32, 0));
            return new IlEntry(IlKind.ByRef, _b.Select(empty, LlvmContext.Null(L.Ptr), SpanPointer(span)), pinnedSpan.TypeArguments[0]);
        }
        if (owner.SpecialType == SpecialType.System_String && method.Name == "GetPinnableReference" && receiver is { } text && args.Length == 0)
        {
            var s = IlRef(text);
            NullCheck(s);
            return new IlEntry(IlKind.ByRef, _b.InBoundsGep(L.I8, s, [L.ConstInt(L.I64, (long)T.PointerSize + 4)]), _m.Compilation.GetSpecialType(SpecialType.System_Char));
        }
        switch (ownerName, method.Name)
        {
            // `x.HasFlag(y)` compiles to two boxes: test the enum values (the JIT doesn't allocate either).
            case ("System.Enum", "HasFlag") when receiver is { Unboxed: { Type: INamedTypeSymbol { TypeKind: TypeKind.Enum } e } value }
                && args is [{ Unboxed: { Type: { } flagType } flag }] && SymbolEqualityComparer.Default.Equals(e, flagType):
            {
                var (v, f) = (IlTyped(value, e), IlTyped(flag, e));
                return IlPush(_b.ICmp(LLVMIntPredicate.EQ, _b.BinOp(LLVMOpcode.And, v, f), f), Special(SpecialType.System_Boolean));
            }
            case ("System.Type", "GetTypeFromHandle") when args is [{ Kind: IlKind.Token, Symbol: ITypeSymbol type }]:
                return new IlEntry(IlKind.Ref, TypeObject(S(type), IlAt()), method.ReturnType) { NonNull = true, Symbol = S(type) };
            // typeof(X).GetTypeInfo() is the Type object itself: X stays known for the reflection that follows.
            case ("System.Reflection.IntrospectionExtensions", "GetTypeInfo") when args is [{ Kind: IlKind.Ref, NonNull: true, Symbol: ITypeSymbol known } typeObject]:
                return new IlEntry(IlKind.Ref, IlRef(typeObject), method.ReturnType) { NonNull = true, Symbol = known };
            // `d.Target as SomeDelegate` (System.Linq.Expressions' CallInstruction.TryGetLightLambdaTarget): csharpc's
            // closure objects aren't .NET's display classes (Delegate.Target refuses them), but neither is a
            // delegate, so a test for a delegate type answers the same with the raw target.
            case ("System.Delegate", "get_Target") when receiver is not null && _ilIndex + 1 < IlBody.Instructions.Length
                && IlBody.Instructions[_ilIndex + 1] is { OpCode: ILOpCode.Isinst } next
                && S(ResolveIlType(next.Token)) is INamedTypeSymbol { TypeKind: TypeKind.Delegate }:
                return new IlEntry(IlKind.Ref, LastDelegateField(IlRef(receiver), 1), method.ReturnType);
            // methodof / fieldof (expression trees compiled by csc): the member's standalone reflection record
            // (ModuleCodeGen.MemberRecords.cs). The RuntimeTypeHandle overloads name the declaring generic
            // instantiation, which substitution has already applied.
            case ("System.Reflection.MethodBase", "GetMethodFromHandle") when args is [{ Kind: IlKind.Token, Symbol: IMethodSymbol tokenMethod }, ..]:
            {
                var target = Substitution.Apply(tokenMethod);
                var record = _b.Cast(LLVMOpcode.PtrToInt, _m.MethodRecordGlobal(target), T.NativeInt);
                var isConstructor = target.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor;
                return new IlEntry(IlKind.Ref, CallCoreLib("MemberRecords", "MethodBase", record, isConstructor ? L.True : L.False), method.ReturnType) { NonNull = true };
            }
            case ("System.Reflection.FieldInfo", "GetFieldFromHandle") when args is [{ Kind: IlKind.Token, Symbol: IFieldSymbol tokenField }, ..]:
            {
                var field = Substitution.Apply(tokenField);
                var record = _b.Cast(LLVMOpcode.PtrToInt, _m.FieldRecordGlobal(field), T.NativeInt);
                return new IlEntry(IlKind.Ref, CallCoreLib("MemberRecords", "Field", record), method.ReturnType) { NonNull = true };
            }
            // typeof(T).IsValueType after monomorphization (ImmutableExtensions.IsValueType<T>(), generic fast paths).
            case ("System.Type", "get_IsValueType") when receiver is { Symbol: ITypeSymbol known } && known is not ITypeParameterSymbol:
                return I4(known.IsValueType ? 1 : 0);
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray") when args is [var array, { Kind: IlKind.Token, Symbol: IFieldSymbol field }]:
            {
                var data = IlRvaData(field) ?? throw new IlException($"InitializeArray from field '{field}' without RVA data");
                var target = IlRef(array);
                NullCheck(target);
                // Rectangular arrays (`double[,] m = { { ... } }`) keep their bounds between the length and the data.
                var elements = array.Type is { } arrayType && S(arrayType) is IArrayTypeSymbol known
                    ? ArrayDataStart(target, known)
                    : throw new IlException("InitializeArray on a value not known to be an array");
                var copy = _m.Intrinsic("llvm.memcpy.p0.p0.i64", L.Void, L.Ptr, L.Ptr, L.I64, L.I1);
                _b.Call(copy.Type, copy.Fn, [elements, data.Global, L.ConstInt(L.I64, data.Size), L.False]);
                return handled;
            }
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "CreateSpan") when args is [{ Kind: IlKind.Token, Symbol: IFieldSymbol field }]:
            {
                var data = IlRvaData(field) ?? throw new IlException($"CreateSpan from field '{field}' without RVA data");
                var element = S(method.TypeArguments[0]);
                var length = data.Size / (int)T.Target.SizeOf(T.Storage(element));
                var spanType = method.ReturnType;
                var span = LlvmContext.Null(T.Value(S(spanType)));
                span = _b.InsertValue(span, data.Global, 0);
                span = _b.InsertValue(span, L.ConstInt(L.I32, length), 1);
                return IlPush(span, spanType);
            }
            // The compiler's inline-array helpers (Unsafe.As/Add over the buffer) for `params` spans.
            case ("<PrivateImplementationDetails>", var name) when name.StartsWith("InlineArray", StringComparison.Ordinal)
                                                                     && method.TypeArguments.Length == 2 && args.Length is 1 or 2:
            {
                var element = S(method.TypeArguments[1]);
                var buffer = IlAddress(args[0]);
                switch (name)
                {
                    case "InlineArrayElementRef" or "InlineArrayElementRefReadOnly" when args.Length == 2:
                        return new IlEntry(IlKind.ByRef, _b.InBoundsGep(T.Storage(element), buffer, [IlToI64(args[1], signed: true)]), method.TypeArguments[1]);
                    case "InlineArrayFirstElementRef" or "InlineArrayFirstElementRefReadOnly" when args.Length == 1:
                        return new IlEntry(IlKind.ByRef, buffer, method.TypeArguments[1]);
                    case "InlineArrayAsSpan" or "InlineArrayAsReadOnlySpan" when args.Length == 2:
                    {
                        var span = LlvmContext.Null(T.Value(S(method.ReturnType)));
                        span = _b.InsertValue(span, buffer, 0);
                        span = _b.InsertValue(span, _b.Cast(LLVMOpcode.Trunc, IlToI64(args[1], signed: true), L.I32), 1);
                        return IlPush(span, method.ReturnType);
                    }
                }
                break;
            }
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "EnsureSufficientExecutionStack") when args.Length == 0:
                return handled;
            // `fixed (char* p = s)` as older compilers emit it: the string's address plus this offset
            // (csrt_String: { mt, int32 length, chars }, the same layout as .NET's string).
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "get_OffsetToStringData") when args.Length == 0:
                return I4((int)T.PointerSize + 4);
            // A reference to a span's first element (or where it would be: the span's pointer, as in .NET).
            case ("System.Runtime.InteropServices.MemoryMarshal", "GetReference") when args.Length == 1 && method.TypeArguments.Length == 1:
                return new IlEntry(IlKind.ByRef, SpanPointer(IlTyped(args[0], method.Parameters[0].Type)), method.TypeArguments[0]);
            case ("System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference")
                when args.Length == 1 && method.TypeArguments.Length == 1 && method.Parameters[0].Type is IArrayTypeSymbol:
            {
                var array = IlRef(args[0]);
                NullCheck(array);
                return new IlEntry(IlKind.ByRef, _b.InBoundsGep(L.I8, array, [L.ConstInt(L.I64, 16)]), method.TypeArguments[0]); // { mt, i64 length, data }
            }
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "IsReferenceOrContainsReferences") when method.TypeArguments.Length == 1:
                return I4(Optimizations.IsReferenceOrContainsReferences(S(method.TypeArguments[0])) ? 1 : 0);
        }
        foreach (var arg in args)
            if (arg.Kind == IlKind.Token) throw new IlException($"ldtoken result passed to '{method}'");
        return null;
    }
}
