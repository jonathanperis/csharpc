using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Arrays and strings as BCL interfaces (int[] as IEnumerable&lt;int&gt;/IList&lt;int&gt;, string as
/// IEnumerable&lt;char&gt;). They share one runtime method table each, so instead of interface maps the
/// *interface's* identity table carries the implementation: its `interfaces` field points at a slot
/// table of CsharpcCoreLib.SZArrayHelper&lt;T&gt; static methods (used when the receiver is an array), its
/// `base` field at CsharpcCoreLib.StringInterfaces methods (receiver is a string); csrt falls back
/// to them (runtime/csrt.c builtin_interface_slots). A static (T[] self, args...) method has the
/// same ABI as the interface method (ptr this, args...).
///
/// Tables are only built for interfaces the program actually converts an array/string to.
/// Limitation: all arrays share csrt_Array_MT, so `object is IEnumerable&lt;X&gt;` can't check the
/// element type of an array at runtime.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly HashSet<INamedTypeSymbol> _arrayInterfaces = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _stringInterfaces = new(SymbolEqualityComparer.Default);
    private readonly Queue<INamedTypeSymbol> _pendingBuiltinInterfaces = new();
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _arraySlotTables = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _stringSlotTables = new(SymbolEqualityComparer.Default);

    public void RequestArrayInterface(INamedTypeSymbol itf)
    {
        foreach (var i in (IEnumerable<INamedTypeSymbol>)[itf, .. itf.AllInterfaces])
            if (i.IsGenericType && !TypeSubstitution.IsOpen(i) && _arrayInterfaces.Add(i)) _pendingBuiltinInterfaces.Enqueue(i);
    }

    public void RequestStringInterface(INamedTypeSymbol itf)
    {
        foreach (var i in (IEnumerable<INamedTypeSymbol>)[itf, .. itf.AllInterfaces])
            if (i.IsGenericType && _stringInterfaces.Add(i)) _pendingBuiltinInterfaces.Enqueue(i);
    }

    /// <summary>Corelib helper implementing <paramref name="member"/> of <paramref name="itf"/> for arrays, if any.</summary>
    public IMethodSymbol? ArrayInterfaceHelper(INamedTypeSymbol itf, IMethodSymbol member) =>
        BuiltinHelper("SZArrayHelper", itf, member, itf.TypeArguments.FirstOrDefault());

    public IMethodSymbol? StringInterfaceHelper(INamedTypeSymbol itf, IMethodSymbol member) =>
        BuiltinHelper("StringInterfaces", itf, member, null);

    private IMethodSymbol? BuiltinHelper(string helperName, INamedTypeSymbol itf, IMethodSymbol member, ITypeSymbol? elementType)
    {
        var ns = Compilation.Assembly.GlobalNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == CoreLib.Namespace);
        var helper = ns?.GetTypeMembers(helperName).FirstOrDefault(t => t.Arity == (elementType is null ? 0 : 1));
        if (helper is null) return null;
        if (elementType is not null) helper = helper.Construct(elementType);
        var name = itf.MetadataName.Replace("`", "") + "_" + member.Name;
        return helper.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault();
    }

    /// <summary>Builds slot tables for newly requested interfaces; true if anything was emitted.</summary>
    private bool EmitPendingBuiltinInterfaceTables()
    {
        var any = false;
        while (_pendingBuiltinInterfaces.TryDequeue(out var itf))
        {
            any = true;
            if (_arrayInterfaces.Contains(itf) && !_arraySlotTables.ContainsKey(itf))
                _arraySlotTables[itf] = SlotTable(itf, "ATBL", m => ArrayInterfaceHelper(itf, m));
            if (_stringInterfaces.Contains(itf) && !_stringSlotTables.ContainsKey(itf))
                _stringSlotTables[itf] = SlotTable(itf, "STBL", m => StringInterfaceHelper(itf, m));
            RefreshInterfaceTable(itf);
        }
        return any;
    }

    private LLVMValueRef SlotTable(INamedTypeSymbol itf, string suffix, Func<IMethodSymbol, IMethodSymbol?> helper)
    {
        var slots = InterfaceSlots(itf).Select(m => helper(m) is { } h ? GetFunction(h).Fn : LlvmContext.Null(Llvm.Ptr)).ToArray();
        var init = LlvmContext.ConstArray(Llvm.Ptr, slots);
        return Module.AddGlobal(Mangler.TypeSymbol(itf, suffix), Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
    }

    private void RefreshInterfaceTable(INamedTypeSymbol itf)
    {
        var global = InterfaceMethodTable(itf);
        var init = Llvm.ConstStruct(
            _stringSlotTables.GetValueOrDefault(itf, LlvmContext.Null(Llvm.Ptr)), CString(Mangler.MetadataName(itf)),
            Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 2 /* CSRT_MT_INTERFACE */),
            _arraySlotTables.GetValueOrDefault(itf, LlvmContext.Null(Llvm.Ptr)), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        Native.LLVMSetInitializer(global, init);
    }
}
