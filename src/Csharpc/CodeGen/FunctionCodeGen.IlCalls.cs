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
    private void IlCall(IlInstruction instruction, Stack<IlEntry> stack, ITypeSymbol? constrained)
    {
        var op = instruction.OpCode;
        var method = IlMethod(instruction.Token);
        if (method.IsVararg) throw new IlException($"vararg call to '{method}'");
        var args = new IlEntry[method.Parameters.Length];
        for (var i = args.Length - 1; i >= 0; i--) args[i] = stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");
        IlEntry? receiver = null;
        if (op != ILOpCode.Newobj && !method.IsStatic)
            receiver = stack.Count > 0 ? stack.Pop() : throw new IlException("evaluation stack underflow");

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
                ? IlOperand(arg with { Kind = IlKind.ByRef, Value = IlAddress(arg) }, parameter.Type)
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
            return IlOperand(receiver, constrained, asReceiver: true);
        }
        if (method.ContainingType.IsValueType) return IlOperand(receiver, method.ContainingType, asReceiver: true);
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

        // Library operators and conversions (decimal + decimal, (long)BigInteger ...): the source path's operator lowering.
        if (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion && method.IsStatic && !_m.IsUserMethod(mapped)
            && BclValueTypes.TryGet(mapped, out _))
        {
            var operands = method.Parameters.Select((p, i) => (IOperation)IlOperand(args[i], p.Type)).ToArray();
            return IlPush(LibraryOperator(mapped, operands, IlAt()), method.ReturnType);
        }

        // Library property accessors (string.Length, Console.Out, List<T>.Count on an interface...): the
        // source path implements them as properties, not as accessor methods.
        if (method.AssociatedSymbol is IPropertySymbol property && !_m.IsUserMethod(mapped) && method.ContainingType.TypeKind != TypeKind.Interface)
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
        return method.ReturnsVoid ? null : IlPush(result, method.ReturnType);
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
            // A static method closed over its first argument (`Func<int> f = s.ExtensionMethod`) needs a thunk csharpc doesn't build yet.
            if (!(target.Kind == IlKind.Ref && target.Type is null))
                throw new IlException($"delegate to static method '{method}' closed over its first argument");
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
        switch (ownerName, method.Name)
        {
            case ("System.Type", "GetTypeFromHandle") when args is [{ Kind: IlKind.Token, Symbol: ITypeSymbol type }]:
                return new IlEntry(IlKind.Ref, TypeObject(S(type), IlAt()), method.ReturnType) { NonNull = true };
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray") when args is [var array, { Kind: IlKind.Token, Symbol: IFieldSymbol field }]:
            {
                var data = IlRvaData(field) ?? throw new IlException($"InitializeArray from field '{field}' without RVA data");
                var target = IlRef(array);
                NullCheck(target);
                var elements = _b.InBoundsGep(L.I8, target, [L.ConstInt(L.I64, 16)]); // { mt, i64 length, data }
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
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "EnsureSufficientExecutionStack") when args.Length == 0:
                return handled;
            case ("System.Runtime.CompilerServices.RuntimeHelpers", "IsReferenceOrContainsReferences") when method.TypeArguments.Length == 1:
                return I4(Optimizations.HoldsNoReferences(S(method.TypeArguments[0])) ? 0 : 1);
        }
        foreach (var arg in args)
            if (arg.Kind == IlKind.Token) throw new IlException($"ldtoken result passed to '{method}'");
        return null;
    }
}
