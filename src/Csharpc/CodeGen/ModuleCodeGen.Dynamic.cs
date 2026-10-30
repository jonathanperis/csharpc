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
        return result;
    }

    private static bool IsDynamicRuntimeType(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => !TypeSubstitution.IsOpen(array),
        INamedTypeSymbol { TypeKind: TypeKind.Interface or TypeKind.Error } => false,
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
            DynamicPlan plan;
            try { plan = PlanDynamicSite(site); }
            catch (CompileError error) { if (site.Fingerprint != "error") Errors.Add(error); site.Fingerprint = "error"; continue; }
            if (plan.Fingerprint == site.Fingerprint) continue;
            site.Fingerprint = plan.Fingerprint;
            EmitDynamicSiteBody(site, plan);
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
        catch (CompileError error)
        {
            Native.LLVMDeleteFunction(fn);
            Errors.Add(error.Location is { IsInSource: true } ? error : new CompileError(error.Id, error.Message, site.Syntax.GetLocation()));
            site.Fingerprint = "error";
            return;
        }
        Native.LLVMReplaceAllUsesWith(old.Fn, fn);
        Native.LLVMDeleteFunction(old.Fn);
        site.Function = lowered;
    }

    /// <summary>The type-name function's body, once every type is known (after the fixed point).</summary>
    private void FinishDynamicTypeNames()
    {
        if (_dynamicTypeName is not { } f) return;
        new FunctionCodeGen(this, null, f).EmitDynamicTypeNames(DynamicRuntimeTypes(), this);
    }
}
