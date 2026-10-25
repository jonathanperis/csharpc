using System.Collections.Immutable;
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
        if (ModuleCodeGen.IsPInvoke(target))
        {
            // Local `static extern` P/Invoke (what the [LibraryImport] generator emits): no environment.
            var stub = _m.PInvokeStub(target);
            var pinvokeArgs = new LLVMValueRef[target.Parameters.Length];
            foreach (var argument in inv.Arguments) pinvokeArgs[argument.Parameter!.Ordinal] = EmitArgument(argument, target);
            return _b.Call(stub.Type, stub.Fn, pinvokeArgs);
        }
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
    // Delegates: { ptr mt, ptr target, ptr fn, ptr invocationList } (list: see FunctionCodeGen.Events.cs)
    // ======================================================================================

    private LLVMTypeRef DelegateType => L.Struct(L.Ptr, L.Ptr, L.Ptr, L.Ptr);

    private LLVMValueRef EmitDelegateCreation(IDelegateCreationOperation creation)
    {
        // A lambda converted to Expression<TDelegate> is data (an expression tree), not a delegate.
        if (creation.Type?.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
            throw CompileError.Unsupported("expression trees (a lambda converted to System.Linq.Expressions.Expression<T>)", creation);
        LLVMValueRef target, fn;
        var capturesNothing = false;
        IMethodSymbol? described = null; // what Delegate.Method reports (ModuleCodeGen.NoteDelegateMethod)
        var sourceLambda = false;
        // `Func<string> f = s.Ext`: Roslyn's operation tree has no receiver for an extension method group
        // closed over its first argument (only IL's ldftn form is lowered, FunctionCodeGen.IlCalls.cs).
        if (creation.Target is IMethodReferenceOperation { Method.IsExtensionMethod: true, Instance: null } extension
            && extension.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax access
            && access.Expression.ToString() != extension.Method.ContainingType.Name && access.Expression.ToString() != extension.Method.ContainingType.ToDisplayString())
            throw CompileError.Unsupported($"a delegate to extension method '{extension.Method}' closed over its receiver", creation);
        switch (creation.Target)
        {
            case IFlowAnonymousFunctionOperation lambda:
                target = EnvValue(NestedFunction(lambda.Symbol, creation).Incoming);
                fn = _m.GetNestedFunction(lambda.Symbol, _subst).Fn;
                (described, sourceLambda) = (lambda.Symbol, true);
                break;
            case IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } local:
            {
                var callee = NestedFunction(local.Method.OriginalDefinition, creation);
                target = EnvValue(callee.Incoming);
                fn = NestedFunctionInstance(callee, S(local.Method), creation).Fn;
                capturesNothing = callee.Incoming is null; // compiled to a static method by C#, so cached too
                (described, sourceLambda) = (S(local.Method), true);
                break;
            }
            // A static method closed over its first argument (IL's `ldarg x; ldftn Ext; newobj D` for
            // `Func<T> f = x.Ext`): the delegate calls fn(target, rest...), which is the method itself when
            // its first parameter is a reference (the target's representation).
            case IMethodReferenceOperation { Method.IsStatic: true, Instance: { } closedOver } group
                when _m.IsUserMethod(S(group.Method)) && S(group.Method).Parameters is [{ RefKind: RefKind.None, Type.IsReferenceType: true }, ..]:
                target = EmitValue(closedOver);
                fn = _m.GetFunction(S(group.Method)).Fn;
                described = S(group.Method);
                break;
            case IMethodReferenceOperation { Method.IsStatic: true } group when _m.IsUserMethod(S(group.Method)):
                target = LlvmContext.Null(L.Ptr);
                fn = _m.StaticThunk(S(group.Method));
                described = S(group.Method);
                break;
            case IMethodReferenceOperation group when _m.IsUserMethod(group.Method):
            {
                // obj.Method: the receiver is the target; virtual methods are resolved now, as in .NET.
                target = EmitValue(group.Instance!);
                var method = S(group.Method);
                var dispatched = group.IsVirtual && (method.IsVirtual || method.IsAbstract || method.IsOverride);
                // A virtual method group compiles to `ldvirtftn`, which throws NullReferenceException on a
                // null receiver; `ldftn` + the delegate constructor reject it with an ArgumentException.
                if (dispatched) NullCheck(target);
                else if (group.Instance is not IInstanceReferenceOperation) DelegateTargetCheck(target);
                var isVirtual = dispatched && !method.IsSealed && !method.ContainingType.IsSealed;
                fn = isVirtual ? LoadVirtualFunction(method, target) : _m.GetFunction(method).Fn;
                if (!isVirtual) described = method;
                break;
            }
            // A library method (Path.GetFullPath, set.Contains, char.IsAscii...): a thunk with the delegate's
            // calling convention calls it through the ordinary call lowering (intrinsics, corelib, dispatch).
            case IMethodReferenceOperation group when LibraryDelegateTarget(Substitution.Apply(group.Method)):
            {
                var method = Substitution.Apply(group.Method); // the library member itself: an instance shadow is a static corelib method
                if (method.IsStatic) target = LlvmContext.Null(L.Ptr);
                else
                {
                    target = EmitValue(group.Instance!);
                    if (group.Instance is not IInstanceReferenceOperation) DelegateTargetCheck(target);
                }
                fn = _m.LibraryDelegateThunk(method, group.IsVirtual, creation.Syntax);
                if (!group.IsVirtual || method.IsStatic || !(method.IsVirtual || method.IsAbstract || method.IsOverride)) described = method;
                break;
            }
            case IMethodReferenceOperation group:
                throw CompileError.Unsupported($"delegate to library method '{group.Method}'", creation);
            default:
                throw CompileError.Unsupported($"delegate creation from {creation.Target.Kind}", creation);
        }
        if (described is not null) _m.NoteDelegateMethod(fn, described, sourceLambda);
        var delegateType = (INamedTypeSymbol)TypeOf(creation)!;
        var mt = _m.DelegateMethodTable(delegateType);
        // C# 11+ caches a static method group's delegate in a static field of the converting type (Roslyn's
        // <>O container), so two conversions of M there are the same object: ReferenceEquals sees it.
        if (creation.Target is IMethodReferenceOperation { Method: var staticMethod, Instance: null }
            && (staticMethod.MethodKind == MethodKind.LocalFunction ? capturesNothing : staticMethod.IsStatic)
            && creation.Syntax is not Microsoft.CodeAnalysis.CSharp.Syntax.BaseObjectCreationExpressionSyntax && _method is not null)
        {
            var key = $"{Mangler.TypeSymbol((INamedTypeSymbol)S(_method.ContainingType), "dcache")}.{Mangler.MetadataName(delegateType)}.{Mangler.Method(S(staticMethod))}";
            var cache = _m.DelegateCache(key);
            var existing = _b.Load(L.Ptr, cache);
            var done = NewBlock("dcache.done");
            var create = NewBlock("dcache.create");
            var result = NewTemp(L.Ptr, "dcache.value");
            _b.Store(existing, result);
            _b.CondBr(_b.ICmp(LLVMIntPredicate.NE, existing, LlvmContext.Null(L.Ptr)), done, create);
            _b.PositionAtEnd(create);
            var fresh = NewDelegate(mt, target, fn);
            _b.Store(fresh, cache);
            _b.Store(fresh, result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(L.Ptr, result);
        }
        return NewDelegate(mt, target, fn);
    }

    private LLVMValueRef NewDelegate(LLVMValueRef mt, LLVMValueRef target, LLVMValueRef fn)
    {
        var obj = CallRuntime("csrt_alloc", L.Ptr, [L.Ptr, L.I64], mt, L.ConstInt(L.I64, (long)T.Target.SizeOf(DelegateType)));
        _b.Store(target, _b.StructGep(DelegateType, obj, 1));
        _b.Store(fn, _b.StructGep(DelegateType, obj, 2));
        return obj;
    }

    /// <summary>Binding an instance method to null is an ArgumentException when the delegate is created, as in .NET.</summary>
    private void DelegateTargetCheck(LLVMValueRef target) =>
        ThrowLibraryExceptionIf(_b.ICmp(LLVMIntPredicate.EQ, target, LlvmContext.Null(L.Ptr)),
            "System.ArgumentException", "Delegate to an instance method cannot have null 'this'.");

    /// <summary>Library methods a delegate thunk can call: by-value parameters, and a reference-type receiver if any.</summary>
    private static bool LibraryDelegateTarget(IMethodSymbol method) =>
        method.Parameters.All(p => p.RefKind == RefKind.None) && method.RefKind == RefKind.None
        && (method.IsStatic || method.ContainingType.IsReferenceType) && !TypeSubstitution.IsOpen(method);

    /// <summary>The body of ModuleCodeGen.LibraryDelegateThunk: (target, args...) => method(args...).</summary>
    internal void EmitLibraryDelegateThunk(IMethodSymbol method, bool isVirtual, SyntaxNode syntax)
    {
        var instance = method.IsStatic ? null
            : new IlValueOperation(syntax, method.ContainingType, () => _fn.Fn.Param(0), nonNull: true);
        var arguments = method.Parameters.Select((p, i) => (IArgumentOperation)new IlArgumentOperation(syntax, p,
            new IlValueOperation(syntax, p.Type, () => _fn.Fn.Param((uint)(i + 1))))).ToImmutableArray();
        var invocation = new IlInvocationOperation(syntax, method, instance, isVirtual && !method.IsStatic, arguments, null);
        var result = EmitValue(invocation);
        if (method.ReturnsVoid) _b.RetVoid();
        else _b.Ret(ToStorageIfNeeded(result, method.ReturnType));
        Finish();
    }

    private LLVMValueRef ToStorageIfNeeded(LLVMValueRef value, ITypeSymbol type) =>
        value.Type.Handle == T.Value(type).Handle ? value : Coerce(value, type, type);

    /// <summary>Resolves a user virtual method on <paramref name="self"/> via its vtable (delegate binding).</summary>
    private LLVMValueRef LoadVirtualFunction(IMethodSymbol method, LLVMValueRef self)
    {
        _m.NoteVirtualCall(method);
        if (method.ContainingType.TypeKind == TypeKind.Interface)
        {
            // `ldvirtftn IFoo::M` / `obj.M` with obj typed as an interface: the implementation comes from
            // the object's interface map, like an interface call (InterfaceDispatch).
            if (method.IsGenericMethod) throw CompileError.Unsupported($"delegate to generic interface method '{method}'", method);
            var itf = method.ContainingType;
            var index = ModuleCodeGen.InterfaceSlotIndex(itf, method);
            _m.NoteInterfaceDispatch(itf, index);
            var slots = InterfaceSlotTable(self, itf);
            return _b.Load(L.Ptr, _b.InBoundsGep(L.I8, slots, [L.ConstUInt(L.I64, (ulong)index * T.PointerSize)], "islot"), "ifn");
        }
        var layout = _m.GetLayout(method.ContainingType);
        if (!layout.SlotOf.TryGetValue(_m.RootOf(method), out var slot))
            throw CompileError.Unsupported($"delegate to virtual method '{method}'", method);
        _m.NoteVirtualDispatch(_m.RootOf(method));
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
        foreach (var argument in inv.Arguments) args[1 + argument.Parameter!.Ordinal] = EmitArgument(argument, invoke);

        List<LLVMTypeRef> parameters = [L.Ptr];
        foreach (var p in invoke.Parameters) parameters.Add(p.RefKind == RefKind.None ? T.Value(p.Type) : L.Ptr);
        var returnType = T.Value(invoke.ReturnType);
        var fnType = L.FunctionType(returnType, [.. parameters]);
        return InvokeDelegate(d, returnType, (target, fn) =>
        {
            args[0] = target;
            return _b.Call(fnType, fn, args);
        });
    }
}
