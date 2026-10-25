using Csharpc.Frontend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Reflection-based System.Text.Json calls whose type gets no compile-time metadata (anonymous and
/// file-local types: Frontend/JsonReflectionContexts.cs) are reported here, where the call is lowered,
/// rather than by the front end: in a method the program never reaches the failure becomes a stub
/// (CSLLVM0007, ModuleCodeGen.Il.cs), as NativeAOT compiles such code and only fails when it runs.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private void CheckJsonSerializerRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        // The library method as bound (S() would already name the corelib's JsonSerializer).
        if (inv.TargetMethod.ContainingType is not { Name: "JsonSerializer", ContainingNamespace: { Name: "Json", ContainingNamespace: { Name: "Text", ContainingNamespace.Name: "System" } } }) return;
        if (target.Parameters.Any(JsonReflectionContexts.IsJsonMetadataParameter)) return;
        foreach (var t in target.TypeArguments)
            if (JsonReflectionContexts.UnsupportedRoot(S(t)) is { } why) throw CompileError.Unsupported(why, inv);
        foreach (var arg in inv.Arguments)
        {
            var value = arg.Value;
            // Serialize<object>(x) and Serialize(x, typeof(T)) / x.GetType(): the value's own type is serialized.
            if (arg.Parameter is { Name: "value" } && value is IConversionOperation { IsImplicit: true, Operand.Type: { } operandType }
                && JsonReflectionContexts.UnsupportedRoot(S(operandType)) is { } valueWhy)
                throw CompileError.Unsupported(valueWhy, inv);
            if (arg.Parameter?.Type is not { Name: "Type", ContainingNamespace.Name: "System" }) continue;
            while (value is IConversionOperation c) value = c.Operand;
            var named = value switch
            {
                ITypeOfOperation typeOf => typeOf.TypeOperand,
                IInvocationOperation { TargetMethod.Name: "GetType", Instance.Type: { } instanceType } => instanceType,
                _ => null,
            };
            if (named is not null && JsonReflectionContexts.UnsupportedRoot(S(named)) is { } typeWhy) throw CompileError.Unsupported(typeWhy, inv);
        }
    }
}
