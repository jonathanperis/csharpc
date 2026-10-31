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
    /// <summary>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault as the program's runtimeconfig.json sets it (default: true).</summary>
    private bool JsonReflectionByDefault() =>
        _m.RuntimeConfig.LastOrDefault(kv => kv.Key == "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault").Value != "false";

    private void CheckJsonSerializerRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        // The library method as bound (S() would already name the corelib's JsonSerializer).
        if (inv.TargetMethod.ContainingType is not { Name: "JsonSerializer", ContainingNamespace: { Name: "Json", ContainingNamespace: { Name: "Text", ContainingNamespace.Name: "System" } } }) return;
        if (target.Parameters.Any(JsonReflectionContexts.IsJsonMetadataParameter)) return;
        // Compiled code: the metadata of reflection-based serialization is generated from the C# sources of the
        // call sites (JsonReflectionContexts), which IL doesn't have; without it the call would throw at run time.
        // (Options may carry a source-generated TypeInfoResolver: calls given options are left to run time. With
        // reflection disabled in runtimeconfig.json, .NET throws InvalidOperationException there, as the corelib does.)
        if (inv is IlOperation && !_m.Il.IsCoreLib(_method) && JsonReflectionByDefault() && !inv.TargetMethod.Parameters.Any(JsonReflectionContexts.IsJsonMetadataParameter)
            && !inv.Arguments.Any(a => a.Parameter?.Type is { Name: "JsonSerializerOptions", ContainingNamespace.Name: "Json" } && a.Value is not IlValueOperation { IsNullLiteral: true }))
            throw CompileError.Unsupported($"reflection-based System.Text.Json serialization in compiled code ('{inv.TargetMethod}' without a JsonTypeInfo or JsonSerializerContext): csharpc generates that metadata from C# source only; use the JSON source generator ([JsonSerializable])", inv);
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
