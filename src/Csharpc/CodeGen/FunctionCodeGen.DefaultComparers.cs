using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// <c>EqualityComparer&lt;T&gt;.Default.Equals / GetHashCode</c> and <c>Comparer&lt;T&gt;.Default.Compare</c> on a closed T (issue #223).
///
/// The framework's collections call the default comparer per probe and rely on the JIT and on NativeAOT's ILC to devirtualize and
/// inline it. The corelib's own collections call <c>CoreIntrinsics.AreEqual/HashOf/Compare</c>, which codegen synthesizes inline per
/// instantiation. A call on the default comparer is the same operation, so it lowers to the same code: no lazily initialized static,
/// no virtual call. The comparer is not evaluated at all when only such a call uses it (the IL front-end drops the getter call that
/// produced it, see <see cref="_eraseIfUnused"/>). Any other use of the comparer keeps the object and the virtual call.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>EqualityComparer&lt;T&gt; (equality: true) or Comparer&lt;T&gt; of the BCL, by its real name (a corelib shadow reports it too).</summary>
    private static bool IsDefaultComparerType(INamedTypeSymbol? type, out bool equality)
    {
        equality = false;
        if (type is not { Arity: 1 }) return false;
        switch (Mangler.MetadataName(type.OriginalDefinition))
        {
            case "System.Collections.Generic.EqualityComparer`1": equality = true; return true;
            case "System.Collections.Generic.Comparer`1": return true;
            default: return false;
        }
    }

    internal static bool IsDefaultComparerGetter(IMethodSymbol method) =>
        method is { IsStatic: true, Name: "get_Default", Parameters.IsEmpty: true } && IsDefaultComparerType(method.ContainingType, out _);

    /// <summary>
    /// Which operation, if any, the call is on a default comparer: "Equals" / "GetHashCode" of EqualityComparer&lt;T&gt;, "Compare" of
    /// Comparer&lt;T&gt;, with the closed T. Not for the explicit non-generic interface members (those take object).
    /// </summary>
    private bool IsDefaultComparerOperation(IMethodSymbol method, out ITypeSymbol element)
    {
        element = null!;
        if (method.IsStatic || !IsDefaultComparerType(method.ContainingType, out var equality)) return false;
        var shape = (method.Name, method.Parameters.Length, equality);
        if (shape is not (("Equals", 2, true) or ("GetHashCode", 1, true) or ("Compare", 2, false))) return false;
        // (The object overloads of the non-generic interfaces are explicit implementations: not members a call on the class names.)
        element = S(method.ContainingType.TypeArguments[0]);
        return element is not ITypeParameterSymbol;
    }

    private LLVMValueRef DefaultComparerOperation(IMethodSymbol method, ITypeSymbol element, LLVMValueRef[] arguments, IOperation at) => method.Name switch
    {
        "Equals" => EqualsOf(element, arguments[0], arguments[1], at),
        "GetHashCode" => HashOf(element, arguments[0], at),
        _ => CompareOf(element, arguments[0], arguments[1], at),
    };

    /// <summary>Source front-end: <c>EqualityComparer&lt;T&gt;.Default.Equals(a, b)</c> as the comparer's inline equivalent.</summary>
    private LLVMValueRef? TryDefaultComparerCall(IInvocationOperation inv, IMethodSymbol target)
    {
        if (inv.Instance is not IPropertyReferenceOperation { Property: { IsStatic: true, Name: "Default", GetMethod: { } getter } } || !IsDefaultComparerGetter(getter)) return null;
        if (!IsDefaultComparerOperation(target, out var element)) return null;
        var arguments = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(a => EmitArgument(a, target)).ToArray();
        return DefaultComparerOperation(target, element, arguments, inv);
    }

    /// <summary>
    /// Calls whose only effect is a cache they fill, made eagerly because IL pushes the value before it is known what consumes it:
    /// the default comparer getter (a comparer the peephole above consumed is not needed) and the Type object of a typeof (the
    /// <c>typeof(T).IsValueType</c> and <c>typeof(T) == typeof(X)</c> guards of generic code fold on the type). Erased from the function
    /// at its end if nothing uses the result.
    /// </summary>
    private readonly List<LLVMValueRef> _eraseIfUnused = [];

    /// <summary>IL front-end: a call on the entry <see cref="IlEntry.IsDefaultComparer"/> marks (the result of the getter).</summary>
    private IlEntry? IlDefaultComparerCall(IMethodSymbol method, IlEntry? receiver, IlEntry[] args)
    {
        if (receiver is not { IsDefaultComparer: true } || !IsDefaultComparerOperation(method, out var element)) return null;
        var arguments = args.Select(a => IlTyped(a, element)).ToArray();
        return IlPush(DefaultComparerOperation(method, element, arguments, IlAt()), method.ReturnType);
    }

    private void EraseUnusedCalls()
    {
        foreach (var call in _eraseIfUnused)
            if (Native.LLVMGetFirstUse(call) == 0 && Native.LLVMIsAInstruction(call).Handle != 0 && Native.LLVMGetInstructionOpcode(call) == LLVMOpcode.Call)
                Native.LLVMInstructionEraseFromParent(call);
        _eraseIfUnused.Clear();
    }
}
