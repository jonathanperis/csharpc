using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Corelib methods the C runtime calls back into (ADR 0008): a static corelib method marked
/// [RuntimeExport("csrt_symbol", "trigger_global")] gets an external wrapper named csrt_symbol, but
/// only once the program references the trigger (the runtime global or function whose code makes the
/// call, e.g. csrt_Boxed_Double_MT for boxed doubles' ToString). Programs that never box a double don't compile
/// or link the double formatter; the runtime's weak fallback definition satisfies the linker instead.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private List<(IMethodSymbol Method, string Symbol, string Trigger)>? _runtimeExports;
    /// <summary>
    /// "library!entry" triggers of native contracts a P/Invoke bound to: corelib methods that implement a .NET shim's entry point in C#
    /// ([RuntimeExport("SystemNative_Open", "libSystem.Native!SystemNative_Open")], corelib/IO/SystemNative.cs).
    /// </summary>
    private readonly HashSet<string> _requestedNativeContracts = new(StringComparer.Ordinal);

    /// <summary>Does the corelib implement this shim entry point (a [RuntimeExport] whose trigger is "library!entry")? Notes the request if so.</summary>
    internal bool TryRequestNativeContract(string library, string entry)
    {
        _runtimeExports ??= CollectRuntimeExports();
        var trigger = library + "!" + entry;
        foreach (var (_, symbol, t) in _runtimeExports)
            if (t == trigger && symbol == entry)
            {
                _requestedNativeContracts.Add(trigger);
                return true;
            }
        return false;
    }
    private readonly HashSet<string> _emittedExports = [];

    private bool EmitRuntimeExports()
    {
        _runtimeExports ??= CollectRuntimeExports();
        var emitted = false;
        foreach (var (method, symbol, trigger) in _runtimeExports)
        {
            var referenced = _requestedNativeContracts.Contains(trigger)
                || (_globals.ContainsKey(trigger) && !_registrationOnlyGlobals.Contains(trigger)) || !Module.GetFunction(trigger).IsNull
                || (trigger.EndsWith("MTE", StringComparison.Ordinal) && !Native.LLVMGetNamedGlobal(Module.Ref, trigger).IsNull); // a class's method table
            if (!referenced || !_emittedExports.Add(symbol)) continue;
            var target = GetFunction(method);
            // A native contract's symbol is already declared by the P/Invoke that binds to it (ModuleCodeGen.PInvoke): define that declaration.
            var wrapper = Module.GetFunction(symbol);
            if (wrapper.IsNull) wrapper = Module.AddFunction(symbol, target.Type);
            else Native.LLVMSetLinkage(wrapper, LLVMLinkage.Internal);
            Llvm.AddFunctionAttribute(wrapper, "uwtable", 2);
            using var b = new IrBuilder(Llvm);
            b.PositionAtEnd(b.AppendBlock(wrapper, "entry"));
            var args = Enumerable.Range(0, method.Parameters.Length).Select(i => wrapper.Param((uint)i)).ToArray();
            var result = b.Call(target.Type, target.Fn, args);
            if (method.ReturnsVoid) b.RetVoid(); else b.Ret(result);
            emitted = true;
        }
        return emitted;
    }

    private List<(IMethodSymbol, string, string)> CollectRuntimeExports()
    {
        var list = new List<(IMethodSymbol, string, string)>();
        void Add(IMethodSymbol method)
        {
            foreach (var attribute in method.GetAttributes())
                if (attribute.AttributeClass?.Name == "RuntimeExportAttribute"
                    && attribute.ConstructorArguments is [{ Value: string symbol }, { Value: string trigger }])
                    list.Add((method, symbol, trigger));
        }
        // A precompiled corelib (ADR 0015) indexed its exports when it was built.
        if (CoreLib.PrecompiledExports is { } exports)
        {
            foreach (var (typeName, methodName) in exports)
                foreach (var method in CoreLib.TypeNamed(typeName)?.GetMembers(methodName).OfType<IMethodSymbol>() ?? [])
                    Add(method);
            return list;
        }
        var ns = Compilation.GlobalNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == CoreLib.Namespace);
        if (ns == null) return list;
        foreach (var type in ns.GetTypeMembers())
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                Add(method);
        return list;
    }
}
