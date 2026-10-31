using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// C# interceptors ([InterceptsLocation], what the configuration-binder, request-delegate, options-validation and
/// logging generators emit for NativeAOT): a call whose location an interceptor names calls the interceptor
/// instead, as Roslyn's lowering rewrites it for emit. Roslyn's operation tree, which csharpc lowers, still shows
/// the original target; SemanticModel.GetInterceptorMethod is what csc's emit consults. The interceptor takes the
/// call's arguments (an instance method's receiver first, as its `this` parameter); a generic interceptor is
/// constructed with the intercepted method's containing types' and its own type arguments, in that order.
/// IL front-end programs come compiled by csc, interceptors already applied.
/// </summary>
public sealed partial class FunctionCodeGen
{
#pragma warning disable RSEXPERIMENTAL002 // GetInterceptorMethod: Roslyn's (experimental) public interceptor API
    private IMethodSymbol? InterceptorOf(IInvocationOperation inv)
    {
        if (!_m.HasInterceptors || inv.Syntax is not InvocationExpressionSyntax syntax) return null;
        if (!_m.Compilation.ContainsSyntaxTree(syntax.SyntaxTree)) return null;
        var interceptor = _m.SemanticModelOf(syntax.SyntaxTree).GetInterceptorMethod(syntax);
        if (interceptor is null) return null;
        var target = S(inv.TargetMethod);
        if (!interceptor.IsGenericMethod || !interceptor.IsDefinition) return interceptor;
        var typeArguments = new List<ITypeSymbol>();
        var outer = new Stack<INamedTypeSymbol>();
        for (var t = target.ContainingType; t is not null; t = t.ContainingType) outer.Push(t);
        foreach (var t in outer) typeArguments.AddRange(t.TypeArguments);
        typeArguments.AddRange(target.TypeArguments);
        if (typeArguments.Count != interceptor.Arity)
            throw CompileError.Unsupported($"interceptor '{interceptor}' of arity {interceptor.Arity} for '{target}'", inv);
        return interceptor.Construct([.. typeArguments]);
    }
#pragma warning restore RSEXPERIMENTAL002

    private LLVMValueRef EmitInterceptedCall(IInvocationOperation inv, IMethodSymbol interceptor)
    {
        var target = S(inv.TargetMethod);
        var receiver = inv.Instance is not null && !target.IsStatic;
        var offset = receiver ? 1 : 0;
        if (!interceptor.IsStatic || interceptor.Parameters.Length != offset + target.Parameters.Length)
            throw CompileError.Unsupported($"interceptor '{interceptor}' for '{target}'", inv);
        var args = new LLVMValueRef[interceptor.Parameters.Length];
        if (receiver)
        {
            var self = interceptor.Parameters[0];
            if (self.RefKind != RefKind.None)
                throw CompileError.Unsupported($"interceptor '{interceptor}' with a by-reference receiver", inv);
            var value = EmitValue(inv.Instance!);
            if (!TypeOf(inv.Instance!)!.IsValueType) NullCheck(value); // the intercepted instance call would throw NullReferenceException
            args[0] = Coerce(value, TypeOf(inv.Instance!), S(self.Type));
        }
        foreach (var argument in inv.Arguments) args[offset + argument.Parameter!.Ordinal] = EmitArgument(argument, target);
        return DirectCall(interceptor, args);
    }
}

public sealed partial class ModuleCodeGen
{
    private bool? _hasInterceptors;
    private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModels = [];

    /// <summary>Whether any source file declares an interceptor (most programs: none, and no call is looked up).</summary>
    internal bool HasInterceptors => _hasInterceptors ??= Compilation.SyntaxTrees.Any(t => t.GetText().ToString().Contains("InterceptsLocation", StringComparison.Ordinal));

    internal SemanticModel SemanticModelOf(SyntaxTree tree)
    {
        lock (_semanticModels)
        {
            if (!_semanticModels.TryGetValue(tree, out var model)) _semanticModels[tree] = model = Compilation.GetSemanticModel(tree);
            return model;
        }
    }
}
