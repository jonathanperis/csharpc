using System.Runtime.InteropServices;
using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Driver;

/// <summary>
/// Keeps the type data out of LLVM's call graph while the optimization pipeline runs. The CGSCC
/// inliner walks a lazy call graph whose reference edges follow every constant a function uses,
/// global initializers included: a function allocating a class refers to its method table, whose
/// vtable and interface slot tables refer to the class's methods, which allocate other classes... So
/// nearly every function of a large program was one reference SCC (11,465 call SCCs in TechEmpower's),
/// and each edge the inliner removed (LazyCallGraph::RefSCC::removeInternalRefEdge) cost time in the
/// size of that SCC: about 40 % of the whole -O2 pipeline, by stack samples, most of the inliner's
/// half. Without the function pointers in global initializers the largest one has 16.
///
/// <see cref="Detach"/> moves the initializer of each csharpc-generated global that points at a
/// function (method tables, interface slot tables, delegate tables, reflection tables) to a private
/// stash global nothing else references, and leaves the original as a declaration; both are kept in
/// llvm.compiler.used, so the functions stay alive and address-taken, as they were. Generated code
/// never loads from these tables at constant addresses (dispatch loads the receiver's method table),
/// so the optimizer loses no folding it relied on. <see cref="Reattach"/> puts the initializers back
/// and runs GlobalDCE, which drops what the optimized program no longer reaches.
/// </summary>
public static class TypeDataDetach
{
    public sealed class Detached
    {
        internal readonly List<(LLVMValueRef Global, LLVMValueRef Stash, LLVMLinkage Linkage)> Globals = [];
        internal LLVMValueRef[] PreviousUsed = [];
        internal LLVMTypeRef Ptr;

        /// <summary>The detached globals and the previous llvm.compiler.used entries, by name (ParallelOptimize reattaches in another context).</summary>
        internal (List<(string Name, LLVMLinkage Linkage)> Globals, List<string> PreviousUsed) Names() =>
            ([.. Globals.Select(d => (NameOf(d.Global), d.Linkage))], [.. PreviousUsed.Select(NameOf)]);
    }

    private const string UsedName = "llvm.compiler.used";

    public static Detached Detach(LlvmModule module, LlvmContext llvm)
    {
        var detached = new Detached { Ptr = llvm.Ptr };
        var candidates = new List<LLVMValueRef>();
        for (var g = LLVMGetFirstGlobal(module.Ref); !g.IsNull; g = LLVMGetNextGlobal(g))
        {
            if (LLVMIsDeclaration(g) != 0 || LLVMIsThreadLocal(g) != 0) continue;
            var name = NameOf(g);
            // Only csharpc's own tables (mangled names, or dotted ones no C identifier has): the runtime's
            // C tables of function pointers may be indexed by constants the optimizer should fold.
            if (!(name.StartsWith("_CN", StringComparison.Ordinal) || name.StartsWith("csrt.", StringComparison.Ordinal)
                  || name.StartsWith("csharpc.", StringComparison.Ordinal))) continue;
            if (ReferencesFunction(LLVMGetInitializer(g))) candidates.Add(g);
        }
        if (candidates.Count == 0) return detached;

        var used = LLVMGetNamedGlobal(module.Ref, UsedName);
        if (!used.IsNull)
        {
            var list = LLVMGetInitializer(used);
            var count = list.IsNull ? 0 : LLVMGetNumOperands(list);
            detached.PreviousUsed = new LLVMValueRef[count];
            for (var i = 0; i < count; i++) detached.PreviousUsed[i] = LLVMGetOperand(list, (uint)i);
            LLVMDeleteGlobal(used);
        }
        var keep = new List<LLVMValueRef>(detached.PreviousUsed);
        foreach (var g in candidates)
        {
            var stash = LLVMAddGlobal(module.Ref, LLVMGlobalGetValueType(g), NameOf(g) + ".detached");
            LLVMSetInitializer(stash, LLVMGetInitializer(g));
            LLVMSetGlobalConstant(stash, LLVMIsGlobalConstant(g));
            LLVMSetLinkage(stash, LLVMLinkage.Private);
            var linkage = LLVMGetLinkage(g);
            LLVMSetInitializer(g, default);
            LLVMSetLinkage(g, LLVMLinkage.External);
            detached.Globals.Add((g, stash, linkage));
            keep.Add(g);
            keep.Add(stash);
        }
        SetUsed(module, detached.Ptr, keep);
        return detached;
    }

    public static void Reattach(LlvmModule module, Detached detached, TargetMachine target)
    {
        if (detached.Globals.Count == 0) return;
        var used = LLVMGetNamedGlobal(module.Ref, UsedName);
        if (!used.IsNull) LLVMDeleteGlobal(used);
        foreach (var (g, stash, linkage) in detached.Globals)
        {
            LLVMSetInitializer(g, LLVMGetInitializer(stash));
            LLVMSetLinkage(g, linkage);
            LLVMDeleteGlobal(stash);
        }
        if (detached.PreviousUsed.Length > 0) SetUsed(module, detached.Ptr, detached.PreviousUsed);
        module.RunPasses("globaldce", target);
    }

    private static void SetUsed(LlvmModule module, LLVMTypeRef ptr, IReadOnlyList<LLVMValueRef> values) => SetUsedList(module.Ref, ptr, values);

    internal static void SetUsedList(LLVMModuleRef module, LLVMTypeRef ptr, IReadOnlyList<LLVMValueRef> values)
    {
        var array = LLVMArrayType2(ptr, (ulong)values.Count);
        var used = LLVMAddGlobal(module, array, UsedName);
        LLVMSetInitializer(used, LLVMConstArray2(ptr, [.. values], (ulong)values.Count));
        LLVMSetLinkage(used, LLVMLinkage.Appending);
        LLVMSetSection(used, "llvm.metadata");
    }

    /// <summary>Whether a constant refers to a function directly (through aggregates and expressions, not other globals).</summary>
    private static bool ReferencesFunction(LLVMValueRef constant)
    {
        if (constant.IsNull) return false;
        if (!LLVMIsAFunction(constant).IsNull) return true;
        if (!LLVMIsAGlobalValue(constant).IsNull) return false;
        var operands = LLVMGetNumOperands(constant);
        for (var i = 0u; i < operands; i++)
            if (ReferencesFunction(LLVMGetOperand(constant, i))) return true;
        return false;
    }

    private static string NameOf(LLVMValueRef value) =>
        Marshal.PtrToStringUTF8(LLVMGetValueName2(value, out var length), (int)length) ?? "";
}
