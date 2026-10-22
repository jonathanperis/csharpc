using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Closures and delegates (docs/03-lowering.md#closures-and-delegates).
///
///   * Captured variables live in heap environments ({ header, parent, vars... }), one per CFG
///     region that declares captured variables, allocated each time control enters the region.
///   * Lambdas and local functions are ordinary functions taking their creation scope's
///     environment as a hidden first parameter.
///   * A delegate is { mt, target, fn }; invoking it calls fn(target, args...). For lambdas the
///     target is the environment, for instance method groups the receiver, and static methods
///     are adapted with a thunk that ignores it.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private readonly Dictionary<EnvScope, LLVMValueRef> _envSlots = new(ReferenceEqualityComparer.Instance);

    private ClosureFunction CurrentClosure => _cfg!.Closure;

    /// <summary>Allocates the function-level scope (captured parameters / this) before the body runs.</summary>
    private void EnterFunctionScope()
    {
        var closure = CurrentClosure;
        if (closure.Analysis.ScopeOf(closure, closure.Cfg.Root) is not { } scope) return;
        var env = AllocateEnv(scope);
        foreach (var p in scope.Vars.OfType<IParameterSymbol>())
            if (_slots.TryGetValue(p, out var spilled))
                _b.Store(_b.Load(T.Storage(S(p.Type)), spilled), _b.StructGep(scope.TypeFor(_subst), env, scope.FieldIndex[p]));
        if (scope.ThisField is { } thisField && _this.Handle != 0)
            _b.Store(_this, _b.StructGep(scope.TypeFor(_subst), env, thisField));
    }

    /// <summary>On a branch that enters regions with captured variables, allocate fresh environments.</summary>
    private LLVMBasicBlockRef EnterScopes(ControlFlowBranch branch, LLVMBasicBlockRef dest)
    {
        var closure = CurrentClosure;
        if (!closure.Analysis.HasCaptures || branch.EnteringRegions.IsEmpty) return dest;
        var scopes = branch.EnteringRegions
            .Select(r => closure.Analysis.ScopeOf(closure, r))
            .OfType<EnvScope>()
            .OrderBy(s => Depth(s.Region))
            .ToList();
        if (scopes.Count == 0) return dest;

        var current = _b.InsertBlock;
        var block = _b.AppendBlock(_fn.Fn, "enter.scope");
        _b.PositionAtEnd(block);
        foreach (var scope in scopes) AllocateEnv(scope);
        _b.Br(dest);
        _b.PositionAtEnd(current);
        return block;

        static int Depth(ControlFlowRegion r) { var d = 0; for (var x = r.EnclosingRegion; x is not null; x = x.EnclosingRegion) d++; return d; }
    }

    private LLVMValueRef AllocateEnv(EnvScope scope)
    {
        var size = T.Target.SizeOf(scope.TypeFor(_subst));
        var env = CallRuntime("csrt_alloc", L.Ptr, [L.Ptr, L.I64], _m.ExternalGlobal("csrt_Object_MT"), L.ConstInt(L.I64, (long)size));
        env.SetName("env");
        _b.Store(EnvValue(scope.Parent), _b.StructGep(scope.TypeFor(_subst), env, EnvScope.ParentField));
        if (!_envSlots.TryGetValue(scope, out var slot)) _envSlots[scope] = slot = NewTemp(L.Ptr, "env.slot");
        _b.Store(env, slot);
        return env;
    }

    /// <summary>The current environment object for <paramref name="target"/>, walking parent links if needed.</summary>
    private LLVMValueRef EnvValue(EnvScope? target)
    {
        if (target is null) return LlvmContext.Null(L.Ptr);
        var closure = CurrentClosure;
        if (target.Owner == closure)
        {
            if (!_envSlots.TryGetValue(target, out var slot)) _envSlots[target] = slot = NewTemp(L.Ptr, "env.slot");
            return _b.Load(L.Ptr, slot);
        }
        var env = _incomingEnv;
        for (var scope = closure.Incoming; scope != target; scope = scope.Parent)
        {
            if (scope is null) throw new CompileError("CSLLVM0004", "internal error: closure environment not reachable", null);
            env = _b.Load(L.Ptr, _b.StructGep(scope.TypeFor(_subst), env, EnvScope.ParentField), "env.parent");
        }
        return env;
    }

    private LLVMValueRef? CapturedAddress(ISymbol variable)
    {
        if (_cfg is null || CurrentClosure.Analysis.ScopeOfVar(variable) is not { } scope) return null;
        return _b.StructGep(scope.TypeFor(_subst), EnvValue(scope), scope.FieldIndex[variable], variable.Name);
    }

    private LLVMValueRef ThisValue(IOperation at)
    {
        if (_this.Handle != 0) return _this;
        if (_cfg is not null && CurrentClosure.Analysis.ThisScope is { ThisField: { } field } scope)
            return _b.Load(L.Ptr, _b.StructGep(scope.TypeFor(_subst), EnvValue(scope), field), "this");
        throw CompileError.Unsupported("'this' outside an instance member", at);
    }

    private ClosureFunction NestedFunction(IMethodSymbol symbol, IOperation at) =>
        CurrentClosure.Analysis.FunctionFor(symbol)
        ?? throw CompileError.Unsupported($"reference to local function '{symbol.Name}' outside its declaring body", at);

    // ======================================================================================
    // Local function calls
    // ======================================================================================

    private LLVMValueRef CallLocalFunction(IInvocationOperation inv)
    {
        var target = S(inv.TargetMethod);
        var callee = NestedFunction(target.OriginalDefinition, inv);
        var args = new LLVMValueRef[1 + target.Parameters.Length];
        args[0] = EnvValue(callee.Incoming);
        foreach (var argument in inv.Arguments) args[1 + argument.Parameter!.Ordinal] = EmitArgument(argument, target);
        var f = NestedFunctionInstance(callee, target, inv);
        return _b.Call(f.Type, f.Fn, args);
    }

    /// <summary>
    /// The lowered function for a lambda / local function referenced from this body. Nested functions
    /// share the enclosing substitution; a generic local function adds its own type arguments and is
    /// instantiated on first use (monomorphization, like generic methods).
    /// </summary>
    private LoweredFunction NestedFunctionInstance(ClosureFunction callee, IMethodSymbol symbol, IOperation at)
    {
        if (!symbol.IsGenericMethod) return _m.GetNestedFunction(symbol, _subst);
        if (TypeSubstitution.IsOpen(symbol)) throw CompileError.Unsupported($"open generic instantiation '{symbol}'", at);
        var subst = _subst.With(symbol.OriginalDefinition.TypeParameters, symbol.TypeArguments, _m.Compilation);
        _m.RequestNested(callee, symbol, subst);
        return _m.GetNestedFunction(symbol, subst);
    }

    // ======================================================================================
    // Delegates: { ptr mt, ptr target, ptr fn }
    // ======================================================================================

    private LLVMTypeRef DelegateType => L.Struct(L.Ptr, L.Ptr, L.Ptr);

    private LLVMValueRef EmitDelegateCreation(IDelegateCreationOperation creation)
    {
        LLVMValueRef target, fn;
        switch (creation.Target)
        {
            case IFlowAnonymousFunctionOperation lambda:
                target = EnvValue(NestedFunction(lambda.Symbol, creation).Incoming);
                fn = _m.GetNestedFunction(lambda.Symbol, _subst).Fn;
                break;
            case IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } local:
            {
                var callee = NestedFunction(local.Method.OriginalDefinition, creation);
                target = EnvValue(callee.Incoming);
                fn = NestedFunctionInstance(callee, S(local.Method), creation).Fn;
                break;
            }
            case IMethodReferenceOperation { Method.IsStatic: true } group when _m.IsUserMethod(group.Method):
                target = LlvmContext.Null(L.Ptr);
                fn = _m.StaticThunk(S(group.Method));
                break;
            case IMethodReferenceOperation group when _m.IsUserMethod(group.Method):
            {
                // obj.Method: the receiver is the target; virtual methods are resolved now, as in .NET.
                target = EmitValue(group.Instance!);
                if (group.Instance is not IInstanceReferenceOperation) NullCheck(target);
                var method = S(group.Method);
                var isVirtual = group.IsVirtual && (method.IsVirtual || method.IsAbstract || method.IsOverride)
                    && !method.IsSealed && !method.ContainingType.IsSealed;
                fn = isVirtual ? LoadVirtualFunction(method, target) : _m.GetFunction(method).Fn;
                break;
            }
            case IMethodReferenceOperation group:
                throw CompileError.Unsupported($"delegate to library method '{group.Method}'", creation);
            default:
                throw CompileError.Unsupported($"delegate creation from {creation.Target.Kind}", creation);
        }
        var mt = _m.DelegateMethodTable((INamedTypeSymbol)TypeOf(creation)!);
        var obj = CallRuntime("csrt_alloc", L.Ptr, [L.Ptr, L.I64], mt, L.ConstInt(L.I64, (long)T.Target.SizeOf(DelegateType)));
        _b.Store(target, _b.StructGep(DelegateType, obj, 1));
        _b.Store(fn, _b.StructGep(DelegateType, obj, 2));
        return obj;
    }

    /// <summary>Resolves a user virtual method on <paramref name="self"/> via its vtable (delegate binding).</summary>
    private LLVMValueRef LoadVirtualFunction(IMethodSymbol method, LLVMValueRef self)
    {
        var layout = _m.GetLayout(method.ContainingType);
        if (!layout.SlotOf.TryGetValue(ClassLayout.RootOf(method), out var slot))
            throw CompileError.Unsupported($"delegate to virtual method '{method}'", method);
        var mt = _b.Load(L.Ptr, self, "mt");
        var offset = T.Target.SizeOf(T.MethodTableHeader) + (ulong)slot * T.PointerSize;
        return _b.Load(L.Ptr, _b.InBoundsGep(L.I8, mt, [L.ConstUInt(L.I64, offset)], "slot"), "vfn");
    }

    private LLVMValueRef EmitDelegateInvoke(IInvocationOperation inv)
    {
        var invoke = S(inv.TargetMethod);
        var d = EmitValue(inv.Instance!);
        NullCheck(d);
        var args = new LLVMValueRef[1 + invoke.Parameters.Length];
        args[0] = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 1), "target");
        foreach (var argument in inv.Arguments) args[1 + argument.Parameter!.Ordinal] = EmitArgument(argument, invoke);
        var fn = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 2), "fn");

        List<LLVMTypeRef> parameters = [L.Ptr];
        foreach (var p in invoke.Parameters) parameters.Add(p.RefKind == RefKind.None ? T.Value(p.Type) : L.Ptr);
        var fnType = L.FunctionType(T.Value(invoke.ReturnType), [.. parameters]);
        return _b.Call(fnType, fn, args);
    }
}
