using System.Collections.Concurrent;
using System.Text;
using Csharpc.Llvm;
using Csharpc.Wasm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Driver;

/// <summary>
/// Parallel optimization and code generation of large programs (docs/adr/0014-compile-time.md). The
/// optimization pipeline, run over the whole program, was most of a large build and ran on one
/// thread (TechEmpower's Platform entry: 80 of 140 s). Here the module is cut into partitions of
/// similar size before the pipeline; each partition is optimized and code-generated in its own LLVM
/// context on its own thread, and the objects are linked together, as ParallelCodegen does after a
/// whole-program optimization.
///
/// What crosses partitions:
/// - Calls: a partition gets an available_externally copy of every small function its code calls
///   (and theirs, a few levels down), so the inliner sees the same small callees it would see in
///   the whole program; larger callees are declarations. The copies are optimized in each partition
///   that imports them and dropped once inlining is done (elim-avail-extern, in the pipeline).
/// - Constant globals (string literals, static data) are copied the same way, so loads from them
///   still fold; mutable globals are declared outside the partition that owns them.
/// - A symbol stays internal when only its own partition refers to it; one that other partitions
///   refer to becomes a hidden external with a reserved prefix, as in ParallelCodegen.
/// - The type data TypeDataDetach parked is reattached in partition 0, which owns the stashes.
///
/// The partitioning depends on the module alone, never on the thread count, so the output is the
/// same at every -j.
/// </summary>
public static class ParallelOptimize
{
    /// <summary>Instructions per partition before optimization: smaller programs stay one module.</summary>
    private const long UnitInstructions = 200_000;
    private const int MaxPartitions = 8;
    /// <summary>Callees up to this size are copied into the partitions that call them.</summary>
    private const int ImportInstructions = 80;
    private const int ImportDepth = 3;
    private const string Prefix = "__csp_";
    private const LLVMLinkage AvailableExternally = (LLVMLinkage)1;

    public static bool Disabled => Environment.GetEnvironmentVariable("CSHARPC_OPT_UNITS") == "1";

    /// <summary>
    /// Optimizes the module with <paramref name="pipeline"/> and emits it as several objects, or
    /// returns null (nothing changed) when it is too small or uses what the split doesn't handle.
    /// When it splits, the module is disposed.
    /// </summary>
    public static List<string>? TryRun(LlvmModule module, TypeDataDetach.Detached detached, string pipeline, TargetMachine target,
        string objectPathBase, Action<string>? log)
    {
        if (Disabled) return null;
        var m = module.Ref;
        if (!LLVMGetFirstGlobalAlias(m).IsNull || !LLVMGetFirstGlobalIFunc(m).IsNull) return null;

        // 0. Partitions refer to symbols by name: unnamed definitions get one (they stay internal).
        var unnamed = 0;
        for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g))
            if (LLVMIsDeclaration(g) == 0 && TakeName(g).Length == 0) SetName(g, $"{Prefix}opt.unnamed.{unnamed++}");
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn))
            if (LLVMIsDeclaration(fn) == 0 && TakeName(fn).Length == 0) SetName(fn, $"{Prefix}opt.unnamed.{unnamed++}");

        // 1. Defined functions in module order, their sizes, direct callees and references.
        var functions = Ir.DefinedFunctions(m);
        var index = new Dictionary<LLVMValueRef, int>();
        for (var i = 0; i < functions.Count; i++) index[functions[i]] = i;
        var size = new int[functions.Count];
        var callees = new List<int>[functions.Count];
        var refs = new List<LLVMValueRef>[functions.Count];
        var constantRefs = new Dictionary<LLVMValueRef, List<LLVMValueRef>>();
        long total = 0;
        for (var i = 0; i < functions.Count; i++)
        {
            var seen = new HashSet<LLVMValueRef>();
            var calls = new HashSet<int>();
            var n = 0;
            for (var bb = LLVMGetFirstBasicBlock(functions[i]); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb))
                for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
                {
                    n++;
                    if (Ir.IsCall(inst) && Ir.DirectCallee(inst) is { IsNull: false } callee && index.TryGetValue(callee, out var c)) calls.Add(c);
                    var operands = LLVMGetNumOperands(inst);
                    for (var k = 0u; k < operands; k++)
                    {
                        var op = LLVMGetOperand(inst, k);
                        if (op.IsNull || LLVMIsConstant(op) == 0) continue;
                        foreach (var g in GlobalsIn(op, constantRefs)) seen.Add(g);
                    }
                }
            // Not an operand: the personality function (csrt_personality) is referenced from the function itself.
            if (LLVMHasPersonalityFn(functions[i]) != 0 && LLVMGetPersonalityFn(functions[i]) is { IsNull: false } personality)
                foreach (var g in GlobalsIn(personality, constantRefs)) seen.Add(g);
            size[i] = n;
            total += n;
            callees[i] = [.. calls];
            refs[i] = [.. seen];
        }
        var count = Environment.GetEnvironmentVariable("CSHARPC_OPT_UNITS") is { Length: > 0 } forced && int.TryParse(forced, out var units)
            ? units
            : (int)Math.Min(MaxPartitions, total / UnitInstructions);
        count = Math.Min(count, functions.Count);
        if (count < 2) return null;

        // 2. Owners: contiguous runs of about total/count instructions (lowering emits a function near
        // the code that first needed it). What llvm.* lists name stays in partition 0.
        var globals = new List<LLVMValueRef>();
        for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g)) globals.Add(g);
        HashSet<LLVMValueRef> pinned = [];
        foreach (var g in globals)
            if (IsLlvmGlobal(g) && LLVMIsDeclaration(g) == 0)
                foreach (var v in GlobalsIn(LLVMGetInitializer(g), constantRefs)) pinned.Add(v);
        var owner = new Dictionary<LLVMValueRef, int>();
        long done = 0;
        for (var i = 0; i < functions.Count; i++)
        {
            owner[functions[i]] = pinned.Contains(functions[i]) ? 0 : (int)Math.Min(count - 1, done * count / total);
            done += size[i];
        }

        // 3. Imports: small callees, a few levels down, as available_externally copies.
        var imports = new HashSet<int>[count];
        for (var p = 0; p < count; p++)
        {
            imports[p] = [];
            var frontier = new List<int>();
            for (var i = 0; i < functions.Count; i++) if (owner[functions[i]] == p) frontier.Add(i);
            for (var depth = 0; depth < ImportDepth && frontier.Count > 0; depth++)
            {
                var next = new List<int>();
                foreach (var f in frontier)
                    foreach (var c in callees[f])
                        if (owner[functions[c]] != p && Importable(functions[c], size[c]) && imports[p].Add(c)) next.Add(c);
                frontier = next;
            }
        }

        // 4. Globals: owned by the lowest partition whose code refers to them (partition 0 for the
        // llvm.* lists and anything only global initializers refer to); constants are copied into
        // the other partitions that refer to them.
        var referencedByInitializers = new HashSet<LLVMValueRef>();
        foreach (var g in globals)
            if (LLVMIsDeclaration(g) == 0)
                foreach (var v in GlobalsIn(LLVMGetInitializer(g), constantRefs)) referencedByInitializers.Add(v);
        var globalOwner = new Dictionary<LLVMValueRef, int>();
        foreach (var g in globals) if (LLVMIsDeclaration(g) == 0) globalOwner[g] = referencedByInitializers.Contains(g) || IsLlvmGlobal(g) ? 0 : int.MaxValue;
        for (var i = 0; i < functions.Count; i++)
            foreach (var r in refs[i])
                if (globalOwner.TryGetValue(r, out var o) && owner[functions[i]] < o) globalOwner[r] = owner[functions[i]];
        foreach (var g in globals) if (globalOwner.TryGetValue(g, out var o) && o == int.MaxValue) globalOwner[g] = 0;

        // 5. Which partitions refer to each symbol, from the bodies each partition keeps: its own
        // functions and globals, its imports, and the constants copied in for them (transitively).
        var referencedIn = new Dictionary<LLVMValueRef, HashSet<int>>();
        void Note(LLVMValueRef symbol, int p)
        {
            if (!referencedIn.TryGetValue(symbol, out var set)) referencedIn[symbol] = set = [];
            set.Add(p);
        }
        var copies = new HashSet<LLVMValueRef>[count];
        for (var p = 0; p < count; p++)
        {
            copies[p] = [];
            var pending = new Stack<LLVMValueRef>();
            void Refer(LLVMValueRef symbol)
            {
                Note(symbol, p);
                if (globalOwner.TryGetValue(symbol, out var o) && o != p && IsCopyableConstant(symbol) && copies[p].Add(symbol)) pending.Push(symbol);
            }
            for (var i = 0; i < functions.Count; i++)
                if (owner[functions[i]] == p || imports[p].Contains(i))
                    foreach (var r in refs[i]) Refer(r);
            foreach (var (g, o) in globalOwner)
                if (o == p) foreach (var r in GlobalsIn(LLVMGetInitializer(g), constantRefs)) Refer(r);
            while (pending.Count > 0)
                foreach (var r in GlobalsIn(LLVMGetInitializer(pending.Pop()), constantRefs)) Refer(r);
            foreach (var i in imports[p]) Note(functions[i], p);
        }
        int OwnerOf(LLVMValueRef symbol) => owner.TryGetValue(symbol, out var o) ? o : globalOwner.TryGetValue(symbol, out var go) ? go : -1;
        bool Shared(LLVMValueRef symbol) => referencedIn.TryGetValue(symbol, out var set) && set.Any(p => p != OwnerOf(symbol));

        // 6. Shared internal symbols become hidden externals under one name in every partition.
        var anonymous = 0;
        foreach (var fn in functions) if (Shared(fn)) Externalize(fn, pinned, ref anonymous);
        foreach (var g in globals) if (LLVMIsDeclaration(g) == 0 && !IsLlvmGlobal(g) && Shared(g)) Externalize(g, pinned, ref anonymous);

        var plans = new PartitionPlan[count];
        for (var p = 0; p < count; p++)
        {
            var plan = plans[p] = new PartitionPlan();
            foreach (var fn in functions)
                if (owner[fn] == p) plan.Owned.Add(TakeName(fn));
            foreach (var i in imports[p]) plan.Imported.Add(TakeName(functions[i]));
            foreach (var (g, o) in globalOwner) if (o == p) plan.Owned.Add(TakeName(g));
            foreach (var g in copies[p]) plan.Imported.Add(TakeName(g));
        }
        var detachedNames = detached.Names();
        var threads = Math.Min(count, Parallelism.Threads);
        log?.Invoke($"optimize in {count} partitions ({total} instructions, {imports.Sum(s => s.Count)} imported functions), {threads} thread(s)");

        var bitcode = Serialize(m);
        module.Dispose();
        var objects = Enumerable.Range(0, count).Select(i => $"{objectPathBase}.{i}.o").ToList();
        var nextPartition = -1;
        var failures = new ConcurrentQueue<Exception>();
        void Work()
        {
            for (int i; (i = Interlocked.Increment(ref nextPartition)) < count && failures.IsEmpty;)
                try { RunPartition(bitcode, i, plans[i], detachedNames, pipeline, target, objects[i]); }
                catch (Exception e) { failures.Enqueue(e); }
        }
        // The optimizer and the backend recurse deeply: helper threads get the main thread's stack size.
        var helpers = Enumerable.Range(1, threads - 1)
            .Select(_ => new Thread(Work, maxStackSize: 16 << 20) { IsBackground = true, Name = "csharpc-optimize" }).ToList();
        foreach (var helper in helpers) helper.Start();
        Work();
        foreach (var helper in helpers) helper.Join();
        if (failures.TryDequeue(out var failure))
        {
            foreach (var o in objects) File.Delete(o);
            throw failure;
        }
        return objects;
    }

    private sealed class PartitionPlan
    {
        public HashSet<string> Owned { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Imported { get; } = new(StringComparer.Ordinal);
    }

    private static bool Importable(LLVMValueRef fn, int size) =>
        !Ir.HasEnumAttribute(fn, "presplitcoroutine") && !Ir.HasEnumAttribute(fn, "noinline") && !Ir.HasEnumAttribute(fn, "optnone")
        && (size <= ImportInstructions || Ir.HasEnumAttribute(fn, "alwaysinline"));

    private static bool IsCopyableConstant(LLVMValueRef g) =>
        !LLVMIsAFunction(g).IsNull ? false
        : LLVMIsDeclaration(g) == 0 && LLVMIsGlobalConstant(g) != 0 && LLVMIsThreadLocal(g) == 0 && !IsLlvmGlobal(g);

    private static unsafe void RunPartition(byte[] bitcode, int partition, PartitionPlan plan,
        (List<(string Name, LLVMLinkage Linkage)> Globals, List<string> PreviousUsed) detached, string pipeline, TargetMachine target, string path)
    {
        var context = LLVMContextCreate();
        try
        {
            LLVMModuleRef m;
            fixed (byte* data = bitcode)
            {
                var buffer = LLVMCreateMemoryBufferWithMemoryRange(data, (nuint)bitcode.Length, "partition", 0);
                try
                {
                    if (LLVMParseBitcodeInContext2(context, buffer, out m) != 0)
                        throw new InvalidOperationException("internal compiler error: csharpc could not re-read its own bitcode for parallel optimization");
                }
                finally { LLVMDisposeMemoryBuffer(buffer); }
            }
            try
            {
                using var machine = target.CreateSibling();
                Prune(m, partition, plan, machine);
                LlvmModule.RunPasses(m, pipeline, machine);
                if (partition == 0) Reattach(m, LLVMPointerTypeInContext(context, 0), detached);
                LlvmModule.RunPasses(m, "globaldce", machine);
                DropUnusedDeclarations(m);
                if (target.Triple.Contains("linux", StringComparison.Ordinal)) SectionPerFunction(m);
                if (LLVMTargetMachineEmitToFile(machine.Ref, m, path, LLVMCodeGenFileType.Object, out var error) != 0)
                    throw new InvalidOperationException($"LLVM code generation failed: {TakeMessage(error)}");
            }
            finally { LLVMDisposeModule(m); }
        }
        finally { LLVMContextDispose(context); }
    }

    /// <summary>
    /// Keeps the partition's own functions and globals; what it imports becomes available_externally
    /// copies; everything else becomes declarations. Only partition 0 keeps the llvm.* lists and the
    /// module-level asm.
    /// </summary>
    private static void Prune(LLVMModuleRef m, int partition, PartitionPlan plan, TargetMachine target)
    {
        List<LLVMValueRef> imported = [];
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn))
        {
            if (LLVMIsDeclaration(fn) != 0) continue;
            var name = TakeName(fn);
            if (plan.Owned.Contains(name)) continue;
            if (plan.Imported.Contains(name)) imported.Add(fn);
            else MakeAvailableExternally(fn);
        }
        List<LLVMValueRef> llvmGlobals = [];
        for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g))
        {
            if (LLVMIsDeclaration(g) != 0) continue;
            if (IsLlvmGlobal(g))
            {
                if (partition != 0) llvmGlobals.Add(g);
                continue;
            }
            var name = TakeName(g);
            if (plan.Owned.Contains(name)) continue;
            if (plan.Imported.Contains(name)) imported.Add(g);
            else MakeAvailableExternally(g);
        }
        foreach (var g in llvmGlobals) LLVMDeleteGlobal(g);
        if (partition != 0) unsafe { LLVMSetModuleInlineAsm2(m, null, 0); }
        // Definitions the partition doesn't keep become declarations in place; the imports are marked
        // only afterwards, so this pass leaves their bodies for the pipeline's inliner.
        LlvmModule.RunPasses(m, "elim-avail-extern", target);
        foreach (var value in imported) MakeAvailableExternally(value);
        DropUnusedDeclarations(m);
    }

    /// <summary>TypeDataDetach.Reattach by name, in partition 0 (the stashes' owner); the type data stays hidden-external.</summary>
    private static void Reattach(LLVMModuleRef m, LLVMTypeRef ptr, (List<(string Name, LLVMLinkage Linkage)> Globals, List<string> PreviousUsed) detached)
    {
        if (detached.Globals.Count == 0) return;
        var used = LLVMGetNamedGlobal(m, "llvm.compiler.used");
        if (!used.IsNull) LLVMDeleteGlobal(used);
        foreach (var (name, _) in detached.Globals)
        {
            var g = LLVMGetNamedGlobal(m, name);
            var stash = LLVMGetNamedGlobal(m, name + ".detached");
            if (g.IsNull || stash.IsNull) continue;
            LLVMSetInitializer(g, LLVMGetInitializer(stash));
            LLVMSetLinkage(g, LLVMLinkage.External);
            LLVMSetVisibility(g, HiddenVisibility);
            LLVMDeleteGlobal(stash);
        }
        List<LLVMValueRef> previous = [];
        foreach (var name in detached.PreviousUsed)
        {
            var v = LLVMGetNamedGlobal(m, name);
            if (v.IsNull) v = LLVMGetNamedFunction(m, name);
            if (!v.IsNull) previous.Add(v);
        }
        if (previous.Count > 0) TypeDataDetach.SetUsedList(m, ptr, previous);
    }

    /// <summary>
    /// ELF: a section per function (-ffunction-sections), so the linker's --gc-sections drops the shared
    /// functions no partition calls any more once their callers inlined them (in a single module,
    /// GlobalDCE dropped them as internal functions).
    /// </summary>
    private static void SectionPerFunction(LLVMModuleRef m)
    {
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn))
            if (LLVMIsDeclaration(fn) == 0 && LLVMGetSection(fn) is var section && (section == 0 || System.Runtime.InteropServices.Marshal.PtrToStringUTF8(section) is ""))
                LLVMSetSection(fn, ".text." + TakeName(fn));
    }

    /// <summary>
    /// Declarations nothing uses: a thread_local one would still be written as an ordinary undefined
    /// symbol (ParallelCodegen.DropUnusedDeclarations), and an internal one is invalid.
    /// </summary>
    private static void DropUnusedDeclarations(LLVMModuleRef m)
    {
        List<LLVMValueRef> unused = [];
        for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g))
            if (LLVMIsDeclaration(g) != 0 && !IsLlvmGlobal(g) && LLVMGetFirstUse(g) == 0) unused.Add(g);
        foreach (var g in unused) LLVMDeleteGlobal(g);
        List<LLVMValueRef> fns = [];
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn))
            if (LLVMIsDeclaration(fn) != 0 && LLVMGetIntrinsicID(fn) == 0 && LLVMGetFirstUse(fn) == 0) fns.Add(fn);
        foreach (var fn in fns) LLVMDeleteFunction(fn);
    }

    /// <summary>The functions and variables a constant refers to (through expressions and aggregates, not other globals' initializers).</summary>
    private static List<LLVMValueRef> GlobalsIn(LLVMValueRef constant, Dictionary<LLVMValueRef, List<LLVMValueRef>> cache)
    {
        if (constant.IsNull) return [];
        if (!LLVMIsAGlobalValue(constant).IsNull) return [constant];
        if (cache.TryGetValue(constant, out var known)) return known;
        List<LLVMValueRef> found = [];
        var operands = LLVMGetNumOperands(constant);
        for (var i = 0u; i < operands; i++)
        {
            var op = LLVMGetOperand(constant, i);
            if (!op.IsNull && LLVMIsConstant(op) != 0) found.AddRange(GlobalsIn(op, cache));
        }
        return cache[constant] = found;
    }

    private static void MakeAvailableExternally(LLVMValueRef value)
    {
        LLVMSetComdat(value, 0);
        LLVMSetLinkage(value, AvailableExternally);
    }

    private static unsafe byte[] Serialize(LLVMModuleRef m)
    {
        var buffer = LLVMWriteBitcodeToMemoryBuffer(m);
        try { return new ReadOnlySpan<byte>(LLVMGetBufferStart(buffer), checked((int)LLVMGetBufferSize(buffer))).ToArray(); }
        finally { LLVMDisposeMemoryBuffer(buffer); }
    }

    private static void Externalize(LLVMValueRef value, HashSet<LLVMValueRef> keepName, ref int anonymous)
    {
        if (LLVMIsDeclaration(value) != 0 || LLVMGetLinkage(value) is not (LLVMLinkage.Internal or LLVMLinkage.Private)) return;
        var name = TakeName(value);
        if (name.Length == 0) SetName(value, $"{Prefix}opt.anon.{anonymous++}");
        else if (!keepName.Contains(value) && name[0] != '\x01' && !name.StartsWith(Prefix, StringComparison.Ordinal)) SetName(value, Prefix + name);
        LLVMSetLinkage(value, LLVMLinkage.External);
        LLVMSetVisibility(value, HiddenVisibility);
    }

    private static bool IsLlvmGlobal(LLVMValueRef global) => TakeName(global).StartsWith("llvm.", StringComparison.Ordinal);

    private static void SetName(LLVMValueRef value, string name) =>
        LLVMSetValueName2(value, name, (nuint)Encoding.UTF8.GetByteCount(name));
}
