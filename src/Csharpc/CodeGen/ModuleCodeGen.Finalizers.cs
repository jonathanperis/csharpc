using Microsoft.CodeAnalysis;
using Csharpc.Llvm;

namespace Csharpc.CodeGen;

// Finalizers (~T()): a class whose chain declares one allocates its instances with csrt_alloc_finalizable, which
// registers the object with the collector (src/runtime/gc.c, "Finalizers"). The runtime calls one function per
// class, a thunk that runs the class chain's finalizers, most derived first (what the base-finalizer call csc puts
// in a destructor's finally block does).
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<nint, ClassLayout> _layoutByMethodTable = [];
    private readonly Dictionary<nint, LLVMValueRef?> _finalizerThunks = [];

    /// <summary>The finalizers a class's instances run, most derived first (classes compiled by csharpc only).</summary>
    internal List<IMethodSymbol> FinalizerChain(ClassLayout layout)
    {
        var chain = new List<IMethodSymbol>();
        for (INamedTypeSymbol? type = layout.Type; type is { SpecialType: not SpecialType.System_Object } && IsUserType(type); type = type.BaseType)
        {
            var finalizer = type.GetMembers("Finalize").OfType<IMethodSymbol>().FirstOrDefault(m =>
                m is { IsStatic: false, ReturnsVoid: true, Parameters.IsEmpty: true, TypeParameters.IsEmpty: true }
                && (m.MethodKind == MethodKind.Destructor || m.IsOverride));
            if (finalizer is not null) chain.Add(finalizer);
        }
        return chain;
    }

    /// <summary>The function the runtime calls to finalize an instance of this class's method table, or null if it has no finalizer.</summary>
    internal LLVMValueRef? FinalizerThunk(LLVMValueRef methodTable)
    {
        if (!_layoutByMethodTable.TryGetValue(methodTable.Handle, out var layout)) return null;
        if (_finalizerThunks.TryGetValue(methodTable.Handle, out var existing)) return existing;
        var chain = FinalizerChain(layout);
        if (chain.Count == 0) return _finalizerThunks[methodTable.Handle] = null;
        var type = Llvm.FunctionType(Llvm.Void, Llvm.Ptr);
        var fn = Module.AddFunction("__csharpc_finalizers." + Mangler.TypeSymbol(layout.Type, "chain"), type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "uwtable", 2);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        var self = Native.LLVMGetParam(fn, 0);
        foreach (var finalizer in chain)
        {
            var target = GetFunction(finalizer);
            b.Call(target.Type, target.Fn, [self]);
        }
        b.RetVoid();
        return _finalizerThunks[methodTable.Handle] = fn;
    }

    private const int CsrtMtTrackedReference = 1 << 17; // CSRT_MT_TRACKED_REFERENCE (src/runtime/csrt.h)

    /// <summary>
    /// ObjectiveCMarshal's "tracked reference with finalizer": the class or a base carries [ObjectiveCTrackedType]
    /// (inherited) and the class has a finalizer (ObjectiveCMarshal.CreateReferenceTrackingHandle requires both).
    /// </summary>
    private bool IsTrackedReference(ClassLayout layout)
    {
        if (layout.IsLibrary || FinalizerChain(layout).Count == 0) return false;
        for (INamedTypeSymbol? type = layout.Type; type is { SpecialType: not SpecialType.System_Object }; type = type.BaseType)
            if (type.GetAttributes().Any(a => a.AttributeClass is { Name: "ObjectiveCTrackedTypeAttribute", ContainingNamespace: { Name: "ObjectiveC" } }))
                return true;
        return false;
    }
}
