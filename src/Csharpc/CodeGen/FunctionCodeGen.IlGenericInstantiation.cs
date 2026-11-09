using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// <c>RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter(typeof(G&lt;X&gt;), typeof(T))</c> in framework IL (issue #224): the
/// instantiation of G's definition over T, created and default-constructed. CoreLib creates its default comparers
/// (<c>ComparerHelpers</c>) and the default array sort helper (<c>ArraySortHelper&lt;T&gt;</c>) so, through run-time generic
/// instantiation; NativeAOT's ILC expands the call when both arguments are known types. csharpc monomorphizes a closed world, so T is
/// known at every use and the instantiation is emitted at compile time: a <c>newobj G&lt;T&gt;::.ctor()</c>. Constraints are not verified,
/// as in IL. Arguments that are not typeof constants are an explicit diagnostic.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private IlEntry? IlCreateInstanceForAnotherGenericParameter(IMethodSymbol method, IlEntry[] args)
    {
        if (method is not { Name: "CreateInstanceForAnotherGenericParameter", IsStatic: true } || method.ContainingType.ToDisplayString() != "CsharpcCoreLib.RuntimeTypeHandleMembers") return null; // (the corelib member that stands for RuntimeTypeHandle's)
        if (args.Length is not (2 or 3) || args.Any(a => a.Symbol is not ITypeSymbol))
            throw new IlException("RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter with a type that is not a typeof constant: csharpc instantiates generic types at compile time");
        // (The symbols are the IL's own, in the caller's generic context: the object creation substitutes them like any newobj.)
        if (args[0].Symbol is not INamedTypeSymbol { IsGenericType: true } generic || generic.OriginalDefinition.TypeParameters.Length != args.Length - 1)
            throw new IlException($"RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter: '{args[0].Symbol}' is not a generic type with {args.Length - 1} type parameter(s)");
        var instance = generic.OriginalDefinition.Construct([.. args.Skip(1).Select(a => (ITypeSymbol)a.Symbol!)]);
        if (instance.IsValueType || instance.IsAbstract)
            throw new IlException($"RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter: '{instance.OriginalDefinition}' is not a class that can be instantiated");
        var constructor = instance.InstanceConstructors.FirstOrDefault(c => c.Parameters.IsEmpty)
            ?? throw new IlException($"RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter: '{instance.OriginalDefinition}' has no parameterless constructor (a MissingMethodException in .NET)");
        return IlNewObject(constructor, []) with { Type = Special(SpecialType.System_Object) };
    }
}
