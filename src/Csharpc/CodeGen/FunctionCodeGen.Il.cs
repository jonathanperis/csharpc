using System.Reflection.Metadata;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>Kinds of values on the IL evaluation stack (ECMA-335 III.1.1), as csharpc represents them.</summary>
internal enum IlKind
{
    Int32,      // i32 (bool, char, small integers and int32 widened, as the CLI does)
    Int64,      // i64
    NativeInt,  // pointer-sized integer
    Float,      // float or double (the CLI's F; the LLVM type keeps the precision)
    Ref,        // object reference (ptr)
    ByRef,      // managed pointer (ptr); Type is the pointee
    Pointer,    // unmanaged pointer (ptr); Type is the pointee (or null)
    Value,      // a value type instance (T.Value of its type)
    MethodPtr,  // ldftn/ldvirtftn result, consumed by a delegate constructor
    Token,      // ldtoken result, consumed by GetTypeFromHandle / InitializeArray / CreateSpan
}

/// <summary>One evaluation-stack entry. Types are the generic definition's (substituted with S()).</summary>
internal sealed record IlEntry(IlKind Kind, LLVMValueRef Value, ITypeSymbol? Type)
{
    public ISymbol? Symbol { get; init; }         // MethodPtr: the method; Token: the type/field
    public IlEntry? Target { get; init; }         // ldvirtftn: the receiver
    public bool IsThis { get; init; }
    public bool NonNull { get; init; }
    /// <summary>
    /// The value of an `ldc.*`/`ldstr` entry (int, long, float, double or string): operands built from it
    /// carry it as their ConstantValue, as Roslyn's operations do for C# constants, so lowering that
    /// needs a constant argument (CoreIntrinsics.LibraryFrames, ordinal comparisons) sees one.
    /// </summary>
    public object? Constant { get; init; }
    /// <summary>The length of an array this entry holds since `newarr` with a constant length (and `dup`s of it).</summary>
    public int? FreshLength { get; init; }
    /// <summary>
    /// A 64-bit `mul` by a constant (Index × Factor), kept through width-preserving conversions: C#
    /// compilers scale pointer offsets this way, and `p + i * sizeof(T)` becomes a typed GEP.
    /// </summary>
    public (IlEntry Index, long Factor)? Scaled { get; init; }
}

/// <summary>
/// The IL front-end's method lowering (docs/adr/0010-il-frontend.md): basic blocks from branch targets
/// and exception-region boundaries, an evaluation stack simulated at compile time, and stack values
/// that cross block boundaries spilled into per-block allocas (mem2reg turns them into phis).
/// Exception regions reuse the source path's model (ADR 0005): a landing pad per protected range,
/// a dispatch chain over its handlers, finally blocks entered through continuation selectors.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private IlState? _il;

    private sealed class IlBlock(int start, int end, int first, int last)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
        public int First { get; } = first;   // instruction indices
        public int Last { get; } = last;
        public LLVMBasicBlockRef Llvm { get; set; }
        public List<(IlKind Kind, ITypeSymbol? Type, LLVMTypeRef LlvmType, LLVMValueRef Slot)>? Entry { get; set; }
        public bool Emitted { get; set; }
        public IlTry? Try { get; set; }
        public IlClause? Handler { get; set; }
    }

    private sealed class IlTry(int start, int end)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
        public List<IlClause> Clauses { get; } = [];
        public IlTry? Outer { get; set; }
        public LLVMBasicBlockRef Pad { get; set; }
        public LLVMBasicBlockRef Dispatch { get; set; }
        public LLVMValueRef Slot { get; set; }
        public bool Contains(int offset) => offset >= Start && offset < End;
    }

    private sealed class IlClause(ExceptionRegion region, IlTry owner)
    {
        public ExceptionRegion Region { get; } = region;
        public IlTry Try { get; } = owner;
        public ExceptionRegionKind Kind => Region.Kind;
        public ITypeSymbol? CatchType { get; set; }
        public int HandlerStart => Region.HandlerOffset;
        public int HandlerEnd => Region.HandlerOffset + Region.HandlerLength;
        public LLVMValueRef Slot { get; set; }
        public LLVMValueRef Selector { get; set; }
        public List<LLVMBasicBlockRef> Continuations { get; } = [];
        public LLVMBasicBlockRef Reject { get; set; }
        public LLVMBasicBlockRef FilterPad { get; set; }
        public bool InFilter(int offset) => Kind == ExceptionRegionKind.Filter && offset >= Region.FilterOffset && offset < HandlerStart;
        public bool InHandler(int offset) => offset >= HandlerStart && offset < HandlerEnd
            || (Kind == ExceptionRegionKind.Filter && offset >= Region.FilterOffset && offset < HandlerStart);
    }

    private sealed class IlState(IlMethodBody body, SyntaxNode syntax, SyntaxNode baseSyntax)
    {
        public IlMethodBody Body { get; } = body;
        public SyntaxNode Syntax { get; } = syntax;
        public SyntaxNode BaseSyntax { get; } = baseSyntax;
        public List<IlBlock> Blocks { get; } = [];
        public Dictionary<int, IlBlock> BlockAt { get; } = [];
        public List<IlTry> Trys { get; } = [];
        public List<IlClause> Clauses { get; } = [];
        public LLVMValueRef[] Locals { get; set; } = [];
        public List<(LLVMBasicBlockRef Block, IlClause Clause)> FinallyEnds { get; } = [];
        public IlInstruction Current { get; set; }
        // Prefixes apply to the next instruction.
        public ITypeSymbol? Constrained { get; set; }
        public bool Volatile { get; set; }
        public bool Tail { get; set; }
        public bool WarnedTail { get; set; }
    }

    private IlMethodBody IlBody => _il!.Body;
    private IlAssembly IlAssembly => _il!.Body.Assembly;

    /// <summary>Lowers a method from its IL body (the IL front-end's counterpart of <see cref="Emit"/>).</summary>
    public void EmitIl(IlMethodBody body)
    {
        var (syntax, baseSyntax) = _m.IlSyntax(body.Assembly);
        var il = _il = new IlState(body, syntax, baseSyntax);
        try
        {
            BuildIlBlocks(il);
            BuildIlRegions(il);
        }
        catch (IlException e) { throw IlError(e.Message); }

        // Locals: zero-initialized (also without `localsinit`: the conservative GC scans them).
        il.Locals = new LLVMValueRef[body.Locals.Length];
        for (var i = 0; i < body.Locals.Length; i++)
        {
            var local = body.Locals[i];
            var storage = local.IsByRef ? L.Ptr : T.Storage(S(local.Type));
            il.Locals[i] = _allocas.Alloca(storage, $"loc{i}");
            _allocas.Store(LlvmContext.Null(storage), il.Locals[i]);
        }

        foreach (var block in il.Blocks) block.Llvm = NewBlock($"IL_{block.Start:X4}");
        // Landing pads and dispatch chains up front: a filter's "rejected" edge and the handlers' entry
        // values must exist whatever the order of the regions in the body.
        foreach (var t in il.Trys) IlLandingPad(t);
        il.Blocks[0].Entry = [];
        _b.Br(il.Blocks[0].Llvm);
        foreach (var block in il.Blocks) EmitIlBlock(block);

        // End of each finally: resume wherever the code that entered it continues.
        _b.UnwindDest = default;
        foreach (var (end, clause) in il.FinallyEnds)
        {
            _b.PositionAtEnd(end);
            switch (clause.Continuations.Count)
            {
                case 0: _b.Unreachable(); break;
                case 1: _b.Br(clause.Continuations[0]); break;
                default:
                    var selector = _b.Load(L.I32, clause.Selector, "finally.next");
                    _b.Switch(selector, clause.Continuations[0], [.. clause.Continuations.Skip(1).Select((c, i) => (L.ConstInt(L.I32, i + 1), c))]);
                    break;
            }
        }
        Finish();
    }

    // ======================================================================================
    // Blocks and regions
    // ======================================================================================

    private static void BuildIlBlocks(IlState il)
    {
        var instructions = il.Body.Instructions;
        if (instructions.IsEmpty) throw new IlException("empty method body");
        var starts = new HashSet<int>(instructions.Select(i => i.Offset));
        var end = instructions[^1].Next;
        var leaders = new SortedSet<int> { 0 };
        void Leader(int offset)
        {
            if (offset == end) return;
            if (!starts.Contains(offset)) throw new IlException($"branch or region boundary IL_{offset:X4} is not an instruction start");
            leaders.Add(offset);
        }
        foreach (var instruction in instructions)
        {
            if (instruction.Targets is { } targets)
            {
                foreach (var t in targets) Leader(t);
                Leader(instruction.Next);
            }
            else if (IlDecoder.EndsBlock(instruction.OpCode)) Leader(instruction.Next);
        }
        foreach (var r in il.Body.Regions)
        {
            Leader(r.TryOffset); Leader(r.TryOffset + r.TryLength);
            Leader(r.HandlerOffset); Leader(r.HandlerOffset + r.HandlerLength);
            if (r.Kind == ExceptionRegionKind.Filter) Leader(r.FilterOffset);
        }
        var index = 0;
        var ordered = leaders.ToList();
        for (var b = 0; b < ordered.Count; b++)
        {
            var blockEnd = b + 1 < ordered.Count ? ordered[b + 1] : end;
            var first = index;
            while (index < instructions.Length && instructions[index].Offset < blockEnd) index++;
            var block = new IlBlock(ordered[b], blockEnd, first, index - 1);
            il.Blocks.Add(block);
            il.BlockAt[block.Start] = block;
        }
    }

    private void BuildIlRegions(IlState il)
    {
        foreach (var region in il.Body.Regions)
        {
            var owner = il.Trys.FirstOrDefault(t => t.Start == region.TryOffset && t.End == region.TryOffset + region.TryLength);
            if (owner is null) il.Trys.Add(owner = new IlTry(region.TryOffset, region.TryOffset + region.TryLength));
            var clause = new IlClause(region, owner);
            if (region.Kind == ExceptionRegionKind.Catch)
                clause.CatchType = IlAssembly.ResolveType(region.CatchType, IlBody.Context).Type;
            owner.Clauses.Add(clause);
            il.Clauses.Add(clause);
        }
        foreach (var t in il.Trys)
            t.Outer = il.Trys.Where(o => o != t && o.Start <= t.Start && o.End >= t.End && (o.End - o.Start) > (t.End - t.Start))
                .MinBy(o => o.End - o.Start);
        foreach (var block in il.Blocks)
        {
            block.Try = il.Trys.Where(t => t.Contains(block.Start)).MinBy(t => t.End - t.Start);
            // The innermost handler (or filter) whose code contains the block: nested ranges are shorter.
            block.Handler = il.Clauses.Where(c => c.InHandler(block.Start))
                .MinBy(c => block.Start >= c.HandlerStart ? c.HandlerEnd - c.HandlerStart : c.HandlerStart - c.Region.FilterOffset);
        }
        // Handler entries receive the exception object on the stack.
        foreach (var clause in il.Clauses)
        {
            var exceptionType = clause.CatchType ?? _m.Compilation.GetSpecialType(SpecialType.System_Object);
            if (clause.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter)
                il.BlockAt[clause.HandlerStart].Entry = [(IlKind.Ref, exceptionType, L.Ptr, _allocas.Alloca(L.Ptr, "handler.exn"))];
            if (clause.Kind == ExceptionRegionKind.Filter)
                il.BlockAt[clause.Region.FilterOffset].Entry = [(IlKind.Ref, exceptionType, L.Ptr, _allocas.Alloca(L.Ptr, "filter.exn"))];
            if (clause.Kind is ExceptionRegionKind.Finally or ExceptionRegionKind.Fault)
            {
                il.BlockAt[clause.HandlerStart].Entry = [];
                clause.Selector = _allocas.Alloca(L.I32, "finally.sel");
            }
            clause.Slot = _allocas.Alloca(L.Ptr, "exn");
        }
        foreach (var t in il.Trys) t.Slot = _allocas.Alloca(L.Ptr, "try.exn");
    }

    // ======================================================================================
    // Block lowering and the stack across blocks
    // ======================================================================================

    /// <summary>The opcode after the instruction being lowered (null at the end of the body).</summary>
    private ILOpCode? IlNextOpCode()
    {
        var instructions = IlBody.Instructions;
        var index = _ilIndex + 1;
        return index < instructions.Length ? instructions[index].OpCode : null;
    }

    private int _ilIndex;

    private void EmitIlBlock(IlBlock block)
    {
        var il = _il!;
        block.Emitted = true;
        _b.PositionAtEnd(block.Llvm);
        // Inside a filter, an exception means "this handler doesn't match" (ECMA-335 II.19), unless a try
        // nested in the filter itself catches it.
        _b.UnwindDest = block.Try is { } protectedBy && (block.Handler is not { } filter || !filter.InFilter(block.Start) || protectedBy.Start >= filter.Region.FilterOffset)
            ? IlLandingPad(protectedBy)
            : block.Handler is { } f && f.InFilter(block.Start) ? IlFilterPad(f) : default;
        _b.MayThrow = _m.MayThrow;
        // ECMA-335 III.1.7.5: a block reached by neither fall-through nor an earlier branch starts empty.
        block.Entry ??= [];
        var stack = new Stack<IlEntry>();
        foreach (var (kind, type, llvmType, slot) in block.Entry)
            stack.Push(new IlEntry(kind, _b.Load(llvmType, slot), type));

        var terminated = false;
        for (var i = block.First; i <= block.Last; i++)
        {
            var instruction = IlBody.Instructions[i];
            il.Current = instruction;
            _ilIndex = i;
            try { terminated = EmitIlInstruction(instruction, stack, block); }
            catch (IlException e) { throw IlError(e.Message); }
            catch (CompileError e) when (!e.Message.Contains(" [in '", StringComparison.Ordinal)) { throw IlError(e.Message, e.Id, e.Location); }
        }
        if (!terminated)
        {
            if (!il.BlockAt.TryGetValue(block.End, out var next)) throw IlError("control falls off the end of the method body");
            IlTransfer(next, stack);
            _b.Br(next.Llvm);
        }
    }

    /// <summary>A diagnostic naming the method, IL offset and opcode (never a silent guess).</summary>
    private CompileError IlError(string message, string id = "CSLLVM0001", Location? location = null)
    {
        var il = _il!;
        var prefix = id == "CSLLVM0001" && !message.StartsWith("not supported by csharpc yet", StringComparison.Ordinal) ? "not supported by csharpc yet: " : "";
        return new CompileError(id, $"{prefix}{message} [in '{IlBody.Method.OriginalDefinition}' ({IlAssembly.Name}.dll) {IlDescribe(il.Current)}]",
            location ?? il.Syntax.GetLocation());
    }

    /// <summary>The instruction with its token operand resolved to a name, for diagnostics.</summary>
    private string IlDescribe(IlInstruction instruction)
    {
        if (instruction.Token == 0 || instruction.OpCode == ILOpCode.Ldstr) return instruction.ToString();
        try
        {
            var kind = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instruction.Token).Kind;
            ISymbol symbol = kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                ? IlAssembly.ResolveType(instruction.Token, IlBody.Context)
                : IlAssembly.ResolveMember(instruction.Token, IlBody.Context);
            return $"IL_{instruction.Offset:X4}: {IlInstruction.Name(instruction.OpCode)} {symbol.ToDisplayString()}";
        }
        catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { return instruction.ToString(); }
    }

    /// <summary>Hands the current stack to a successor block: its entry slots receive the values.</summary>
    private void IlTransfer(IlBlock target, Stack<IlEntry> stack)
    {
        var values = stack.Reverse().ToList(); // bottom first
        if (target.Entry is null)
        {
            if (target.Emitted && values.Count > 0)
                throw new IlException($"backward branch to IL_{target.Start:X4} with a non-empty stack");
            target.Entry = [.. values.Select(v =>
            {
                if (v.Kind is IlKind.MethodPtr or IlKind.Token) throw new IlException($"{v.Kind} value live across a branch");
                var type = Native.LLVMTypeOf(v.Value);
                return (v.Kind, v.Type, type, _allocas.Alloca(type, "stack"));
            })];
        }
        if (target.Entry.Count != values.Count)
            throw new IlException($"stack depth {values.Count} at a branch to IL_{target.Start:X4}, which expects {target.Entry.Count}");
        for (var i = 0; i < values.Count; i++)
        {
            var (kind, _, llvmType, slot) = target.Entry[i];
            var value = values[i].Value;
            var valueType = Native.LLVMTypeOf(value);
            if (valueType.Handle != llvmType.Handle)
            {
                if (kind == IlKind.Float && valueType.IsFloatingPoint)
                    value = _b.Cast(llvmType.Kind == LLVMTypeKind.Double ? LLVMOpcode.FPExt : LLVMOpcode.FPTrunc, value, llvmType);
                else if (llvmType.IsInteger && valueType.IsInteger)
                    value = _b.Cast(llvmType.IntWidth > valueType.IntWidth ? LLVMOpcode.SExt : LLVMOpcode.Trunc, value, llvmType);
                else if (llvmType.IsPointer && valueType.IsInteger) value = _b.Cast(LLVMOpcode.IntToPtr, value, llvmType);
                else if (llvmType.IsInteger && valueType.IsPointer) value = _b.Cast(LLVMOpcode.PtrToInt, value, llvmType);
                else throw new IlException($"inconsistent stack at IL_{target.Start:X4}: {values[i].Kind} vs {kind}");
            }
            _b.Store(value, slot);
        }
    }

    private IlBlock IlTarget(int offset) =>
        _il!.BlockAt.TryGetValue(offset, out var block) ? block : throw new IlException($"branch target IL_{offset:X4} is not a block start");

    // ======================================================================================
    // Exception handling
    // ======================================================================================

    private LLVMBasicBlockRef IlLandingPad(IlTry t)
    {
        if (!t.Pad.IsNull) return t.Pad;
        EnsurePersonality();
        // Catch clauses of this try and every enclosing one, innermost first, so the search phase
        // finds a handler in this frame; filters are catch-alls here and run in the dispatch.
        List<LLVMValueRef> clauses = [];
        for (var x = t; x is not null; x = x.Outer)
            foreach (var c in x.Clauses)
                if (c.Kind == ExceptionRegionKind.Filter || (c.Kind == ExceptionRegionKind.Catch && IsCatchAllType(S(c.CatchType!))))
                    clauses.Add(LlvmContext.Null(L.Ptr));
                else if (c.Kind == ExceptionRegionKind.Catch)
                    clauses.Add(IlCatchTypeInfo(c.CatchType!));
        var (savedBlock, savedUnwind) = (_b.InsertBlock, _b.UnwindDest);
        _b.UnwindDest = default;
        t.Pad = NewBlock("il.lpad");
        _b.PositionAtEnd(t.Pad);
        var lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, clauses, cleanup: true);
        var exception = CallRuntime("csrt_begin_catch", L.Ptr, [L.Ptr], _b.ExtractValue(lp, 0));
        _b.Store(exception, t.Slot);
        _b.Br(IlDispatch(t));
        _b.PositionAtEnd(savedBlock);
        _b.UnwindDest = savedUnwind;
        return t.Pad;
    }

    private LLVMValueRef IlCatchTypeInfo(ITypeSymbol type) => S(type) switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Class } named when _m.IsUserType(named) || _m.IsLibraryException(named) => _m.GetLayout(named).MethodTable,
        var other => throw new IlException($"catching '{other}'"),
    };

    /// <summary>Tests the handlers of <paramref name="t"/> in order, then propagates outward.</summary>
    private LLVMBasicBlockRef IlDispatch(IlTry t)
    {
        if (!t.Dispatch.IsNull) return t.Dispatch;
        var (savedBlock, savedUnwind) = (_b.InsertBlock, _b.UnwindDest);
        _b.UnwindDest = default;
        t.Dispatch = NewBlock("il.dispatch");
        _b.PositionAtEnd(t.Dispatch);
        foreach (var clause in t.Clauses)
        {
            var next = NewBlock("il.catch.next");
            var exception = _b.Load(L.Ptr, t.Slot);
            switch (clause.Kind)
            {
                case ExceptionRegionKind.Catch:
                {
                    var enter = NewBlock("il.catch");
                    if (IsCatchAllType(S(clause.CatchType!))) _b.Br(enter);
                    else
                    {
                        var matched = CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], exception, IlCatchTypeInfo(clause.CatchType!));
                        _b.CondBr(_b.ICmp(LLVMIntPredicate.NE, matched, LlvmContext.Null(L.Ptr)), enter, next);
                    }
                    _b.PositionAtEnd(enter);
                    _b.Store(exception, clause.Slot);
                    var handler = _il!.BlockAt[clause.HandlerStart];
                    _b.Store(exception, handler.Entry![0].Slot);
                    _b.Br(handler.Llvm);
                    break;
                }
                case ExceptionRegionKind.Filter:
                {
                    _b.Store(exception, clause.Slot);
                    var filter = _il!.BlockAt[clause.Region.FilterOffset];
                    _b.Store(exception, filter.Entry![0].Slot);
                    clause.Reject = next;
                    _b.Br(filter.Llvm);
                    break;
                }
                default: // finally / fault: run it, then keep looking outward
                    IlEnterFinally(clause, next);
                    break;
            }
            _b.PositionAtEnd(next);
        }
        var escaping = _b.Load(L.Ptr, t.Slot);
        if (t.Outer is { } outer)
        {
            _b.Store(escaping, outer.Slot);
            _b.Br(IlDispatch(outer));
        }
        else
        {
            CallRuntime("csrt_rethrow", L.Void, [L.Ptr], escaping); // unwinds into our caller
            _b.Unreachable();
        }
        _b.PositionAtEnd(savedBlock);
        _b.UnwindDest = savedUnwind;
        return t.Dispatch;
    }

    /// <summary>The landing pad of a filter's code: any exception rejects the handler.</summary>
    private LLVMBasicBlockRef IlFilterPad(IlClause filter)
    {
        if (!filter.FilterPad.IsNull) return filter.FilterPad;
        EnsurePersonality();
        var (savedBlock, savedUnwind) = (_b.InsertBlock, _b.UnwindDest);
        _b.UnwindDest = default;
        filter.FilterPad = NewBlock("il.filter.lpad");
        _b.PositionAtEnd(filter.FilterPad);
        var lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, [LlvmContext.Null(L.Ptr)], cleanup: false);
        CallRuntime("csrt_begin_catch", L.Ptr, [L.Ptr], _b.ExtractValue(lp, 0));
        _b.Br(filter.Reject);
        _b.PositionAtEnd(savedBlock);
        _b.UnwindDest = savedUnwind;
        return filter.FilterPad;
    }

    private void IlEnterFinally(IlClause clause, LLVMBasicBlockRef continuation)
    {
        var index = clause.Continuations.Count;
        clause.Continuations.Add(continuation);
        _b.Store(L.ConstInt(L.I32, index), clause.Selector);
        _b.Br(_il!.BlockAt[clause.HandlerStart].Llvm);
    }

    /// <summary>`leave`: run the finally blocks of every try being exited (innermost first), then branch.</summary>
    private void IlLeave(int from, int target)
    {
        var destination = IlTarget(target);
        IlTransfer(destination, new Stack<IlEntry>());
        var dest = destination.Llvm;
        var exited = _il!.Trys.Where(t => t.Contains(from) && !t.Contains(target)).OrderByDescending(t => t.End - t.Start).ToList();
        foreach (var t in exited) // outermost first: each finally continues to the next outer one
            foreach (var clause in Enumerable.Reverse(t.Clauses).Where(c => c.Kind == ExceptionRegionKind.Finally))
            {
                var current = _b.InsertBlock;
                var trampoline = NewBlock("to.finally");
                _b.PositionAtEnd(trampoline);
                IlEnterFinally(clause, dest);
                _b.PositionAtEnd(current);
                dest = trampoline;
            }
        _b.Br(dest);
    }

    private IlClause IlHandlerOf(IlBlock block) =>
        block.Handler ?? throw new IlException($"{IlInstruction.Name(_il!.Current.OpCode)} outside an exception handler");
}
