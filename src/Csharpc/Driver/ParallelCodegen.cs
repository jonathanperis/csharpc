using System.Collections.Concurrent;
using System.Text;
using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Driver;

/// <summary>
/// Parallel machine-code generation (docs/adr/0014-compile-time.md). The whole program is optimized
/// as one module, as before; only the backend, which works function by function, is split. After
/// optimization the module's functions are cut into contiguous partitions of similar size, each
/// partition is code-generated in its own LLVM context on its own thread, and the objects are linked
/// together like the runtime's.
///
/// Each partition is a copy of the optimized module in which the functions of the other partitions
/// are declarations (same name, type, calling convention and attributes: LLVM's
/// elim-avail-extern turns them into declarations in place). Partition 0 also keeps every global
/// variable, module-level asm and the llvm.* globals (and the functions those list); the other
/// partitions declare them. Symbols that were internal become hidden externals with a reserved
/// prefix, so they bind across the objects of this program only (and are stripped from the
/// executable as before). Nothing is optimized after the split, so the code is the same as in one
/// object; the partitioning depends on the module alone, never on the thread count, so the output is
/// the same at every -j.
///
/// The partitions share one bitcode image of the optimized module, written once; each thread
/// parses it into a context of its own, prunes it and emits it with a target machine of its own.
/// </summary>
public static class ParallelCodegen
{
    /// <summary>
    /// Instructions per partition: below ~2 such units the extra serialization costs more than the
    /// parallel backend saves (hello world stays one object).
    /// </summary>
    private const int UnitInstructions = 10_000;
    private const int MaxPartitions = 8;
    private const string Prefix = "__csp_";
    private const LLVMLinkage AvailableExternally = (LLVMLinkage)1;

    /// <summary>
    /// Emits the module as several objects in parallel, or returns null when it is too small to be
    /// worth splitting (or uses something the split doesn't handle: aliases, ifuncs). When it splits,
    /// the module is disposed once serialized.
    /// </summary>
    public static List<string>? TryEmit(LlvmModule module, TargetMachine target, string objectPathBase, Action<string>? log)
    {
        var m = module.Ref;
        if (!LLVMGetFirstGlobalAlias(m).IsNull || !LLVMGetFirstGlobalIFunc(m).IsNull) return null;

        // 1. Sizes, in module order.
        List<(LLVMValueRef Fn, int Size)> functions = [];
        long total = 0;
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn))
        {
            if (LLVMIsDeclaration(fn) != 0) continue;
            var size = 0;
            for (var bb = LLVMGetFirstBasicBlock(fn); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb))
                for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
                    size++;
            functions.Add((fn, size));
            total += size;
        }
        var count = Environment.GetEnvironmentVariable("CSHARPC_CODEGEN_UNITS") is { Length: > 0 } forced && int.TryParse(forced, out var n)
            ? n
            : (int)Math.Min(MaxPartitions, total / UnitInstructions);
        count = Math.Min(count, functions.Count);
        if (count < 2) return null;

        // 2. Partitions: contiguous runs of about total/count instructions. What llvm.used,
        // llvm.compiler.used and the constructor lists name stays with the globals, in partition 0,
        // under its own name (the linker may look for it: lld's symbol ordering file places
        // csrt_code_start/csrt_data_start, Hardening.SegmentOrder).
        HashSet<LLVMValueRef> pinned = [];
        for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g))
            if (IsLlvmGlobal(g) && LLVMIsDeclaration(g) == 0)
                CollectGlobals(LLVMGetInitializer(g), pinned);
        var owner = new Dictionary<LLVMValueRef, int>();
        long done = 0;
        foreach (var (fn, size) in functions)
        {
            owner[fn] = pinned.Contains(fn) ? 0 : (int)Math.Min(count - 1, done * count / Math.Max(1, total));
            done += size;
        }

        // 3. Internal symbols become hidden externals, named (unnamed constants too) and prefixed.
        var anonymous = 0;
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn)) Externalize(fn, pinned, ref anonymous);
        for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g))
            if (!IsLlvmGlobal(g)) Externalize(g, pinned, ref anonymous);
        var owned = new HashSet<string>[count];
        for (var i = 0; i < count; i++) owned[i] = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (fn, _) in functions) owned[owner[fn]].Add(TakeName(fn));
        functions.Clear();
        owner.Clear();

        var threads = Math.Min(count, Parallelism.Threads);
        log?.Invoke($"split into {count} partitions ({total} instructions), {threads} thread(s)");
        // 4. One bitcode image of the whole program; each partition parses it into its own context
        // and prunes it. The program module is freed first: the partitions reuse its memory.
        var bitcode = Serialize(m);
        module.Dispose();
        var objects = Enumerable.Range(0, count).Select(i => $"{objectPathBase}.{i}.o").ToList();
        var next = -1;
        var failures = new ConcurrentQueue<Exception>();
        void Work()
        {
            for (int i; (i = Interlocked.Increment(ref next)) < count && failures.IsEmpty;)
                try { EmitPartition(bitcode, i, owned[i], target, objects[i]); }
                catch (Exception e) { failures.Enqueue(e); }
        }
        // The calling thread is one of the workers; the others get a stack as large as the main
        // thread's (the backend recurses over deep expression DAGs).
        var helpers = Enumerable.Range(1, threads - 1)
            .Select(_ => new Thread(Work, maxStackSize: 16 << 20) { IsBackground = true, Name = "csharpc-codegen" }).ToList();
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

    private static unsafe void EmitPartition(byte[] bitcode, int partition, HashSet<string> owned, TargetMachine target, string path)
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
                        throw new InvalidOperationException("internal compiler error: csharpc could not re-read its own bitcode for parallel code generation");
                }
                finally { LLVMDisposeMemoryBuffer(buffer); }
            }
            try
            {
                // A target machine must not emit two modules at once: one per partition.
                using var machine = target.CreateSibling();
                Prune(m, partition, owned, machine);
                if (LLVMTargetMachineEmitToFile(machine.Ref, m, path, LLVMCodeGenFileType.Object, out var error) != 0)
                    throw new InvalidOperationException($"LLVM code generation failed: {TakeMessage(error)}");
            }
            finally { LLVMDisposeModule(m); }
        }
        finally { LLVMContextDispose(context); }
    }

    /// <summary>Turns everything partition <paramref name="partition"/> doesn't own into declarations.</summary>
    private static void Prune(LLVMModuleRef m, int partition, HashSet<string> owned, TargetMachine target)
    {
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn))
            if (LLVMIsDeclaration(fn) == 0 && !owned.Contains(TakeName(fn))) MakeAvailableExternally(fn);
        if (partition != 0)
        {
            List<LLVMValueRef> llvmGlobals = [];
            for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g))
            {
                if (LLVMIsDeclaration(g) != 0) continue;
                if (IsLlvmGlobal(g)) llvmGlobals.Add(g);
                else MakeAvailableExternally(g);
            }
            foreach (var g in llvmGlobals) LLVMDeleteGlobal(g);
            unsafe { LLVMSetModuleInlineAsm2(m, null, 0); }
        }
        // Deletes the bodies and initializers in place (definitions become external declarations,
        // keeping their attributes); the code that refers to them is untouched.
        LlvmModule.RunPasses(m, "elim-avail-extern", target);
        if (partition != 0) DropUnusedDeclarations(m);
    }

    /// <summary>
    /// A [ThreadStatic] lives in partition 0. Other partitions declare it, and one that never touches
    /// it still emits the declaration: LLVM writes an unused thread_local declaration as an ordinary
    /// undefined symbol, which the linker rejects against the TLS definition ("TLS definition
    /// mismatches non-TLS reference"). A declaration nothing in this partition uses is not a reference.
    /// </summary>
    private static void DropUnusedDeclarations(LLVMModuleRef m)
    {
        List<LLVMValueRef> unused = [];
        for (var g = LLVMGetFirstGlobal(m); !g.IsNull; g = LLVMGetNextGlobal(g))
        {
            if (LLVMIsDeclaration(g) == 0 || IsLlvmGlobal(g)) continue;
            if (LLVMGetFirstUse(g) == 0) unused.Add(g);
        }
        foreach (var g in unused) LLVMDeleteGlobal(g);
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
        // The prefix keeps a formerly internal symbol from clashing with a global of the same name in
        // another object of the link (the runtime's, libc's in --static builds). Names starting with
        // \x01 are verbatim assembler names (C asm labels).
        if (name.Length == 0) SetName(value, $"{Prefix}anon.{anonymous++}");
        else if (!keepName.Contains(value) && name[0] != '\x01') SetName(value, Prefix + name);
        LLVMSetLinkage(value, LLVMLinkage.External);
        LLVMSetVisibility(value, HiddenVisibility);
    }

    private static bool IsLlvmGlobal(LLVMValueRef global) => TakeName(global).StartsWith("llvm.", StringComparison.Ordinal);

    private static void SetName(LLVMValueRef value, string name) =>
        LLVMSetValueName2(value, name, (nuint)Encoding.UTF8.GetByteCount(name));

    /// <summary>The functions and variables a constant (an llvm.used list, a constructor table) refers to.</summary>
    private static void CollectGlobals(LLVMValueRef constant, HashSet<LLVMValueRef> globals)
    {
        if (constant.IsNull) return;
        if (!LLVMIsAGlobalValue(constant).IsNull) { globals.Add(constant); return; }
        var operands = LLVMGetNumOperands(constant);
        for (var i = 0u; i < operands; i++) CollectGlobals(LLVMGetOperand(constant, i), globals);
    }
}
