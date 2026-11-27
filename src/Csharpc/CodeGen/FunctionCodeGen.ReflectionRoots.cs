using Csharpc.Frontend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// The trimmer's rule for reflection the framework cannot see through: a framework method marked
/// [RequiresUnreferencedCode] (System.Text.Json's serializer, ...) reads members and builds generic types from the Types it is
/// given, which the library leaves to the program to root. A call to one roots the types it names: the type arguments, the
/// typeof / GetType() / value arguments, and the member, base, generic-argument, constructor-parameter and attribute types of
/// those. The members of the program's own types stay (reflection reads them), and every type is recorded as a root for the
/// instantiations the framework builds with MakeGenericType (ModuleCodeGen.ReflectionRoots.cs).
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>The method is the program's own: its sources, or its assembly given as IL (not the corelib, not a framework or package library).</summary>
    private bool IsProgramMethod(IMethodSymbol? method) =>
        method is not null && (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _m.Compilation.Assembly) && !CoreLib.IsCoreLib(method)
            || _m.MainIl is not null && _m.Il.OwnerOf(method.OriginalDefinition) == _m.MainIl);

    /// <summary>The method (or its type) carries [RequiresUnreferencedCode].</summary>
    private static bool RequiresUnreferencedCode(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        static bool Has(ISymbol symbol) => symbol.GetAttributes().Any(a => a.AttributeClass is { Name: "RequiresUnreferencedCodeAttribute", ContainingNamespace: { Name: "CodeAnalysis", ContainingNamespace.Name: "Diagnostics" } });
        return Has(definition) || Has(definition.ContainingType) || definition.AssociatedSymbol is { } associated && Has(associated);
    }

    /// <summary>A call from the program (or any code but the callee's own assembly) to a [RequiresUnreferencedCode] method of the framework's IL.</summary>
    private void NoteReflectionRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        if (target.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)) return;
        var definition = target.OriginalDefinition;
        // Only the framework's IL: a shadowed library has its own metadata, and the program's own methods are not the trimmer's concern.
        var owner = _m.Il.OwnerOf(definition);
        if (!(owner is { IsFramework: true, IsCoreLib: false } || IlFirst.Assemblies.Contains(definition.ContainingAssembly?.Name ?? "", StringComparer.OrdinalIgnoreCase))) return;
        if (!RequiresUnreferencedCode(target)) return;
        // The framework's own methods calling each other are not a program rooting types.
        if (_method is not null && _m.Il.OwnerOf(_method.OriginalDefinition) is { } caller && caller.Name == definition.ContainingAssembly?.Name) return;
        KeepReflectionRoots(target, inv);
    }

    private void KeepReflectionRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        if (Environment.GetEnvironmentVariable("CSHARPC_TRACE_REFLECTION") == "1") Console.Error.WriteLine($"[csharpc] rooting call {target} from {_method}");
        // The framework's own IL calling a [RequiresUnreferencedCode] method (DiagnosticSource.Write<WebApplicationBuilder>, ConfigurationBinder.GetValue<bool>)
        // roots what mentions the program: the framework's use of its own types is its business, and a public entry point the program called
        // (HttpClient.GetFromJsonAsync<T> is [RequiresUnreferencedCode] itself) has rooted the types the program gave it already.
        // Rooting them all would keep every constructor of those types, and lower whatever the constructors reach (Server.IIS).
        void Root(ITypeSymbol type)
        {
            if (InFrameworkIl && !_m.MentionsProgramOrPackageType(type)) return;
            _m.KeepReflectionType(type);
        }
        foreach (var t in target.TypeArguments) Root(_subst.Apply(t));
        foreach (var arg in inv.Arguments)
        {
            var value = arg.Value;
            if (arg.Parameter is { Name: "value" } && value is IConversionOperation { Operand.Type: { } operandType }) Root(_subst.Apply(operandType));
            if (value is IlValueOperation { TypeOfOperand: { } typeOf }) Root(_subst.Apply(typeOf));
            // What source sees as the operand of the conversion to the parameter's type: the IL stack entry's own (more derived) type.
            if (arg.Parameter is { Name: "value" } && value is IlValueOperation { EntryType: { } entryType }) Root(_subst.Apply(entryType));
            while (value is IConversionOperation c) value = c.Operand;
            switch (value)
            {
                case ITypeOfOperation typeOfOperation: Root(_subst.Apply(typeOfOperation.TypeOperand)); break;
                case IInvocationOperation { TargetMethod.Name: "GetType", Instance.Type: { } instanceType }: Root(_subst.Apply(instanceType)); break;
            }
            if (arg.Parameter is { Name: "value" } && value.Type is { } valueType) Root(_subst.Apply(valueType));
        }
    }

}
