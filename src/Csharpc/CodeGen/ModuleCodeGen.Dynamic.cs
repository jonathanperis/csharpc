using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.CodeGen;

/// <summary>
/// `dynamic` (docs/adr/0019-dynamic.md). The C# runtime binder (Microsoft.CSharp) binds a dynamic operation
/// when it runs, "as if the user had written" it against the best accessible type of each dynamic operand's
/// run-time value, and caches the result per type. A closed world knows every type a value can have at run
/// time: the types the program has method tables for. So csharpc binds each dynamic operation at compile
/// time, with Roslyn, at the operation's own source position (the binder's accessibility context), once for
/// every run-time type that can reach it and changes the outcome, and emits a dispatch function per site
/// that switches on the operands' method tables and runs the code bound for them. A binding that fails
/// becomes the RuntimeBinderException the binder throws, with its message.
///
/// The set of run-time types grows while the program is lowered, so site bodies are (re)emitted in the
/// codegen fixed point until no site gains a type it dispatches on.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<string, DynamicSite> _dynamicSites = new(StringComparer.Ordinal);
    private readonly List<DynamicSite> _dynamicSiteList = [];
    private LoweredFunction? _dynamicTypeName;
    private bool _dynamicBoxesRegistered;
    private static readonly bool s_dynamicTrace = Environment.GetEnvironmentVariable("CSHARPC_DYNAMIC_TRACE") is "1" or "2";
    private static readonly bool s_dynamicTraceCases = Environment.GetEnvironmentVariable("CSHARPC_DYNAMIC_TRACE") == "2";

    private readonly Dictionary<SyntaxTree, SemanticModel> _dynamicModels = [];

    /// <summary>The semantic model dynamic sites bind in (speculatively, at their position).</summary>
    internal SemanticModel DynamicSemanticModel(SyntaxTree tree) =>
        _dynamicModels.TryGetValue(tree, out var model) ? model : _dynamicModels[tree] = Compilation.GetSemanticModel(tree);

    /// <summary>The program uses `dynamic`.</summary>
    internal bool UsesDynamic => _dynamicSiteList.Count > 0;

    /// <summary>The dispatch function of a dynamic operation (created on first use, emitted in the fixed point).</summary>
    internal DynamicSite DynamicSiteFor(string key, Func<DynamicSite> create)
    {
        if (_dynamicSites.TryGetValue(key, out var site)) return site;
        site = create();
        var fnType = Llvm.FunctionType(site.ReturnLlvmType(this), [.. site.ParameterLlvmTypes(this)]);
        var fn = Module.AddFunction("csharpc.dynamic." + _dynamicSiteList.Count, fnType);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "uwtable", 2);
        site.Function = new LoweredFunction(fn, fnType);
        _dynamicSites[key] = site;
        _dynamicSiteList.Add(site);
        return site;
    }

    /// <summary>`string csharpc.dynamic.typename(ptr mt, i32 lookup)`: the binder's name for a run-time type (null: unknown).</summary>
    internal LoweredFunction DynamicTypeNameFunction()
    {
        if (_dynamicTypeName is { } f) return f;
        var type = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.I32);
        var fn = Module.AddFunction("csharpc.dynamic.typename", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        _dynamicTypeName = f = new LoweredFunction(fn, type);
        return f;
    }

    // ======================================================================================
    // The run-time types
    // ======================================================================================

    /// <summary>Every type a value can have at run time, with its method table(s): the concrete types the program has tables for.</summary>
    internal List<(ITypeSymbol Type, LLVMValueRef Mt)> DynamicRuntimeTypes()
    {
        RegisterKnownMethodTables();
        var result = new List<(ITypeSymbol, LLVMValueRef)>();
        var seen = new HashSet<nint>();
        foreach (var (mt, described) in _typeMts)
        {
            if (described is not ITypeSymbol symbol || !seen.Add(mt.Handle)) continue;
            var type = CoreLib.Unmap(symbol);
            if (IsDynamicRuntimeType(type)) result.Add((type, mt));
        }
        foreach (var layout in _anonymous.Values)
            if (seen.Add(layout.MethodTable.Handle)) result.Add((layout.Type, layout.MethodTable));
        foreach (var (tuple, mt) in _tupleMethodTables)
            if (seen.Add(mt.Handle) && tuple is ITypeSymbol tupleType) result.Add((CoreLib.Unmap(tupleType), mt));
        return result;
    }

    private static bool IsDynamicRuntimeType(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => !TypeSubstitution.IsOpen(array),
        INamedTypeSymbol { TypeKind: TypeKind.Interface or TypeKind.Error } => false,
        // System.Type stands for the runtime's Type objects (RuntimeType on .NET).
        INamedTypeSymbol { Name: "Type", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } => true,
        INamedTypeSymbol { IsStatic: true } or INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true } => false,
        INamedTypeSymbol named => !named.IsUnboundGenericType && !TypeSubstitution.IsOpen(named) && !TypeMapper.IsNullable(named)
            && named.SpecialType is not (SpecialType.System_Void or SpecialType.System_ValueType or SpecialType.System_Enum
                or SpecialType.System_Array or SpecialType.System_Delegate or SpecialType.System_MulticastDelegate),
        _ => false,
    };

    /// <summary>Boxed primitives can reach a dynamic operation from anywhere (the runtime boxes them too): always known.</summary>
    private void RegisterDynamicPrimitiveBoxes()
    {
        if (_dynamicBoxesRegistered) return;
        _dynamicBoxesRegistered = true;
        foreach (var special in s_boxedPrimitives)
        {
            var type = Compilation.GetSpecialType(special);
            RegisterTypeMethodTable(ExternalGlobal($"csrt_Boxed_{type.MetadataName}_MT"), type);
        }
        RegisterTypeMethodTable(ExternalGlobal("csrt_String_MT"), Compilation.GetSpecialType(SpecialType.System_String));
        RegisterTypeMethodTable(ExternalGlobal("csrt_Object_MT"), Compilation.GetSpecialType(SpecialType.System_Object));
        // Type objects are runtime objects with one method table; .NET's RuntimeType is internal, so the binder
        // sees System.Type. (Tasks share one table across Task<T> instantiations: they stay unknown, and a
        // dynamic member access on one fails explicitly.)
        if (Compilation.GetTypeByMetadataName("System.Type") is { } systemType) RegisterTypeMethodTable(ExternalGlobal("csrt_Type_MT"), systemType);
    }

    // ======================================================================================
    // The fixed point
    // ======================================================================================

    /// <summary>(Re)emits the sites whose dispatch changed; true when anything was emitted.</summary>
    private bool EmitDynamicSites()
    {
        if (_dynamicSiteList.Count == 0) return false;
        RegisterDynamicPrimitiveBoxes();
        var any = false;
        // Emitting a site can create more (an invoked property's value is invoked dynamically).
        for (var i = 0; i < _dynamicSiteList.Count; i++)
        {
            var site = _dynamicSiteList[i];
            if (site.Failed) continue;
            DynamicPlan plan;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { plan = PlanDynamicSite(site); }
            catch (CompileError error) { Errors.Add(error); site.Failed = true; continue; }
            if (plan.Fingerprint == site.Fingerprint) continue;
            if (s_dynamicTrace && site.Fingerprint is { } before)
            {
                var a = before.Split(']'); var b = plan.Fingerprint.Split(']');
                for (var k = 0; k < Math.Min(a.Length, b.Length); k++)
                    if (a[k] != b[k]) { Console.Error.WriteLine($"[dynamic]   changed: {a[k]}  ->  {b[k]}"); break; }
            }
            site.Fingerprint = plan.Fingerprint;
            var planned = watch.ElapsedMilliseconds;
            EmitDynamicSiteBody(site, plan);
            if (s_dynamicTrace)
                Console.Error.WriteLine($"[dynamic] site {i} {site.Kind} {site.Name ?? site.Operator} '{site.Syntax}': plan {planned} ms, emit {watch.ElapsedMilliseconds - planned} ms, cases [{string.Join(",", plan.Classes.Select(c => c?.Count ?? 0))}]");
            any = true;
        }
        return any;
    }

    private void EmitDynamicSiteBody(DynamicSite site, DynamicPlan plan)
    {
        // A fresh function each time: the earlier body (fewer types) is replaced at every call site.
        var old = site.Function;
        var fn = Module.AddFunction("csharpc.dynamic." + _dynamicSiteList.IndexOf(site), old.Type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "uwtable", 2);
        var lowered = new LoweredFunction(fn, old.Type);
        try
        {
            new FunctionCodeGen(this, null, lowered, substitution: site.Subst).EmitDynamicSite(site, plan);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Native.LLVMDeleteFunction(fn);
            var error = e as CompileError ?? new CompileError("CSLLVM9999",
                $"internal compiler error while binding the dynamic operation '{site.Syntax}': {e.GetType().Name}: {e.Message} (set CSHARPC_TRACE=1 for details)", null);
            if (s_trace || s_dynamicTrace) Console.Error.WriteLine($"[dynamic] site '{site.Syntax}' failed: {e}");
            Errors.Add(error.Location is { IsInSource: true } ? error : new CompileError(error.Id, error.Message, site.Syntax.GetLocation()));
            site.Failed = true;
            return;
        }
        Native.LLVMReplaceAllUsesWith(old.Fn, fn);
        Native.LLVMDeleteFunction(old.Fn);
        site.Function = lowered;
    }

    /// <summary>
    /// The function of one case of a site (same signature as the site): the code bound for that combination of
    /// operand types. Cases are kept across re-emissions of the site. A case csharpc can't lower (a library
    /// member it doesn't implement, for a type the value may never have) throws NotSupportedException when
    /// reached instead of failing the compile.
    /// </summary>
    internal LoweredFunction DynamicCaseFunction(DynamicSite site, DynamicClass?[] chosen)
    {
        var key = string.Join("|", chosen.Select(c => c is null ? "-" : c.Key.Kind + "/" + c.Key.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        if (site.Cases.TryGetValue(key, out var existing)) return existing;
        var type = site.Function.Type;
        var name = $"csharpc.dynamic.{_dynamicSiteList.IndexOf(site)}.case{site.Cases.Count}";
        LoweredFunction Create()
        {
            var fn = Module.AddFunction(name, type);
            Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
            Llvm.AddFunctionAttribute(fn, "uwtable", 2);
            return new LoweredFunction(fn, type);
        }
        var lowered = Create();
        if (s_dynamicTraceCases) Console.Error.WriteLine($"[dynamic] {name}: {key}");
        try
        {
            new FunctionCodeGen(this, null, lowered, substitution: site.Subst).EmitDynamicCase(site, [.. chosen]);
        }
        catch (CompileError error)
        {
            if (s_dynamicTrace) Console.Error.WriteLine($"[dynamic] case {key} of '{site.Syntax}': {error.Message}");
            Native.LLVMDeleteFunction(lowered.Fn);
            lowered = Create();
            var message = error.Message.StartsWith("not supported by csharpc yet: ", StringComparison.Ordinal)
                ? error.Message["not supported by csharpc yet: ".Length..] : error.Message;
            new FunctionCodeGen(this, null, lowered).EmitThrowingFactory(
                $"Not supported by csharpc yet: the dynamic operation '{site.Syntax}' on these operand types ({message}).");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A bug in csharpc: no half-built function stays behind; the site reports it.
            Native.LLVMDeleteFunction(lowered.Fn);
            throw new CompileError("CSLLVM9999",
                $"internal compiler error while binding the dynamic operation '{site.Syntax}' ({key}): {e.GetType().Name}: {e.Message}", site.Syntax.GetLocation());
        }
        return site.Cases[key] = lowered;
    }

    /// <summary>The type-name function's body, once every type is known (after the fixed point).</summary>
    private void FinishDynamicTypeNames()
    {
        if (_dynamicTypeName is not { } f) return;
        new FunctionCodeGen(this, null, f).EmitDynamicTypeNames(DynamicRuntimeTypes(), this);
    }
}
