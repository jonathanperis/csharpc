using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Exception handling (docs/adr/0005-exceptions.md). Zero-cost, table-driven, Itanium ABI:
///
///  * Calls inside a try region are emitted as `invoke`s whose unwind edge goes to that region's
///    landing pad (IrBuilder.UnwindDest). Code outside try regions uses plain calls.
///  * Each try region R gets one `landingpad { ptr, i32 }` carrying a catch clause per handler of R
///    *and of every enclosing try in this function* (type-info = the catch type's method table,
///    null = catch-all), plus `cleanup`. csrt_personality matches clauses in the search phase.
///  * The landing pad unwraps the exception (csrt_begin_catch) into R's slot and jumps to R's
///    dispatch: test handlers in order (type test, then `when` filter), enter the first match;
///    otherwise run R's finally (if any) and move on to the enclosing try's dispatch, or rethrow
///    out of the function.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private readonly Dictionary<ControlFlowRegion, LLVMBasicBlockRef> _landingPads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ControlFlowRegion, LLVMBasicBlockRef> _dispatch = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ControlFlowRegion, LLVMValueRef> _exceptionSlots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ControlFlowRegion, LLVMBasicBlockRef> _filterRejected = new(ReferenceEqualityComparer.Instance);
    private BasicBlock? _currentBlock;
    private bool _hasPersonality;

    private LLVMTypeRef LandingPadType => L.Struct(L.Ptr, L.I32);

    // ======================================================================================
    // Region queries
    // ======================================================================================

    private bool IsProtectedTry(ControlFlowRegion region) =>
        region.Kind == ControlFlowRegionKind.Try
        && region.EnclosingRegion?.Kind switch
        {
            ControlFlowRegionKind.TryAndCatch => true,
            ControlFlowRegionKind.TryAndFinally => !IsNoOpFinally(region.EnclosingRegion),
            _ => false,
        };

    /// <summary>
    /// Innermost try region of the graph being lowered that protects code in <paramref name="region"/>.
    /// Never looks past the graph's root: a lambda's graph must not see the enclosing method's trys.
    /// </summary>
    private ControlFlowRegion? ProtectingTry(ControlFlowRegion? region)
    {
        var root = _cfg?.Graph.Root;
        for (var r = region; r is not null && !ReferenceEquals(r, root); r = r.EnclosingRegion)
            if (IsProtectedTry(r)) return r;
        return null;
    }

    private readonly Dictionary<ControlFlowRegion, bool> _noOpFinally = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// A try/finally whose finally provably does nothing: the one Roslyn wraps around foreach over
    /// an array, a string or a span, whose enumerator's Dispose is a no-op here (array enumerators
    /// are never IDisposable). Its try needs no landing pad, so calls in the loop body stay plain
    /// calls and LLVM doesn't have to carry finally state around the loop; exceptions go straight
    /// to the enclosing handler, which is what running the empty finally would amount to.
    /// </summary>
    private bool IsNoOpFinally(ControlFlowRegion tryAndFinally)
    {
        if (_noOpFinally.TryGetValue(tryAndFinally, out var known)) return known;
        var finallyRegion = tryAndFinally.NestedRegions.First(r => r.Kind == ControlFlowRegionKind.Finally);
        var graph = _cfg!.Graph;
        HashSet<CaptureId> nullCaptures = [];

        // Side-effect-free values: capture references, reference/identity conversions, null tests.
        bool Pure(IOperation op) => op switch
        {
            IFlowCaptureReferenceOperation or ILocalReferenceOperation => true,
            IConversionOperation c when c.Conversion.IsIdentity || c.Conversion.IsReference => Pure(c.Operand),
            IIsNullOperation n => Pure(n.Operand),
            _ => false,
        };
        // Mirrors EmitInvocation / EmitInterfaceCall: these Dispose calls emit nothing (not even the
        // receiver): span enumerators, and CharEnumerator (an intrinsic) also through IDisposable.
        bool NoOpDispose(IInvocationOperation inv)
        {
            if (inv is not { TargetMethod.Name: "Dispose", Arguments.IsEmpty: true, Instance: { } instance }) return false;
            if (TypeMapper.IsSpanEnumerator(TypeOf(instance))) return true;
            var method = S(inv.TargetMethod);
            if (method.ContainingType.TypeKind == TypeKind.Interface)
                method = TypeOf(ConcreteReceiver(instance)) is { TypeKind: TypeKind.Class } type
                    ? type.FindImplementationForInterfaceMember(method) as IMethodSymbol : null;
            return method is not null && !_m.IsUserMethod(method) && method.ContainingType.ToDisplayString() == "System.CharEnumerator";
        }
        bool NoOp(IOperation op)
        {
            switch (op)
            {
                // `enumerator as IDisposable` of an array enumerator is null (EmitConversion).
                case IFlowCaptureOperation { Value: IConversionOperation c } capture when TryArrayEnumerator(c.Operand, out _):
                    nullCaptures.Add(capture.Id);
                    return true;
                case IFlowCaptureOperation capture: return Pure(capture.Value);
                case IExpressionStatementOperation e: return NoOp(e.Operation);
                case IInvocationOperation inv: return NoOpDispose(inv);
                default: return false;
            }
        }
        static bool KnownNull(IOperation op, HashSet<CaptureId> nulls) =>
            op is IIsNullOperation { Operand: IFlowCaptureReferenceOperation r } && nulls.Contains(r.Id);

        // Walk what can actually run, from the finally's entry to its exit.
        var result = true;
        var seen = new HashSet<int>();
        var work = new Stack<BasicBlock>([graph.Blocks[finallyRegion.FirstBlockOrdinal]]);
        while (result && work.TryPop(out var block))
        {
            if (!seen.Add(block.Ordinal)) continue;
            if (block.Ordinal < finallyRegion.FirstBlockOrdinal || block.Ordinal > finallyRegion.LastBlockOrdinal) { result = false; break; }
            if (!block.Operations.All(NoOp)) { result = false; break; }
            var successors = new List<ControlFlowBranch?>();
            if (block.BranchValue is { } value && block.ConditionKind != ControlFlowConditionKind.None)
            {
                if (KnownNull(value, nullCaptures)) // the IsNull test is always true
                    successors.Add(block.ConditionKind == ControlFlowConditionKind.WhenTrue ? block.ConditionalSuccessor : block.FallThroughSuccessor);
                else if (Pure(value)) successors.AddRange([block.ConditionalSuccessor, block.FallThroughSuccessor]);
                else { result = false; break; }
            }
            else if (block.BranchValue is not null) { result = false; break; }
            else successors.Add(block.FallThroughSuccessor);
            foreach (var branch in successors)
            {
                if (branch is null) continue;
                if (branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling) continue; // end of the finally
                if (branch.Semantics != ControlFlowBranchSemantics.Regular || branch.Destination is null) { result = false; break; }
                work.Push(branch.Destination);
            }
        }
        return _noOpFinally[tryAndFinally] = result;
    }

    /// <summary>The try enclosing the whole try/catch or try/finally statement that <paramref name="tryRegion"/> belongs to.</summary>
    private ControlFlowRegion? OuterTry(ControlFlowRegion tryRegion) => ProtectingTry(tryRegion.EnclosingRegion!.EnclosingRegion);

    /// <summary>Catch handlers (Catch or FilterAndHandler regions) of a TryAndCatch, in source order.</summary>
    private static IEnumerable<ControlFlowRegion> Handlers(ControlFlowRegion tryAndCatch) =>
        tryAndCatch.NestedRegions.Where(r => r.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler);

    /// <summary>The handler a block belongs to (a Catch region, or the FilterAndHandler for filter + handler).</summary>
    private static ControlFlowRegion? HandlerOf(BasicBlock? block)
    {
        for (var r = block?.EnclosingRegion; r is not null; r = r.EnclosingRegion)
        {
            if (r.Kind == ControlFlowRegionKind.Filter) return r.EnclosingRegion;
            if (r.Kind == ControlFlowRegionKind.Catch)
                return r.EnclosingRegion?.Kind == ControlFlowRegionKind.FilterAndHandler ? r.EnclosingRegion : r;
        }
        return null;
    }

    private bool IsCatchAll(ITypeSymbol? type) => IsCatchAllType(type is null ? null : S(type));

    private static bool IsCatchAllType(ITypeSymbol? type) =>
        type is null or { SpecialType: SpecialType.System_Object }
        || (type is INamedTypeSymbol { MetadataName: "Exception" } t && t.ContainingNamespace?.ToDisplayString() == "System");

    private LLVMValueRef ExceptionSlot(ControlFlowRegion region)
    {
        if (!_exceptionSlots.TryGetValue(region, out var slot))
            _exceptionSlots[region] = slot = _allocas.Alloca(L.Ptr, "exn");
        return slot;
    }

    // ======================================================================================
    // Per-block setup (called by LowerCfg)
    // ======================================================================================

    private void BeginBlock(BasicBlock block)
    {
        _currentBlock = block;
        var tryRegion = ProtectingTry(block.EnclosingRegion);
        _b.UnwindDest = tryRegion is not null ? LandingPad(tryRegion) : _coro is not null ? CoroutinePad() : default;
        _b.MayThrow = _m.MayThrow;
        EnableTraceHook();
    }

    private void EndCfg()
    {
        _b.UnwindDest = default;
        _b.UnwindHook = null;
        _currentBlock = null;
    }

    // ======================================================================================
    // Landing pads and dispatch
    // ======================================================================================

    private LLVMBasicBlockRef LandingPad(ControlFlowRegion tryRegion)
    {
        if (_landingPads.TryGetValue(tryRegion, out var pad)) return pad;
        EnsurePersonality();

        // Clauses for this try and every enclosing try (innermost first) so the search phase sees all handlers.
        List<LLVMValueRef> clauses = [];
        for (var t = tryRegion; t is not null; t = OuterTry(t))
            if (t.EnclosingRegion!.Kind == ControlFlowRegionKind.TryAndCatch)
                foreach (var handler in Handlers(t.EnclosingRegion))
                    clauses.Add(IsCatchAll(handler.ExceptionType) ? LlvmContext.Null(L.Ptr) : CatchTypeInfo(handler.ExceptionType!));
        // A coroutine body catches everything at its outermost level (the exception goes into the task).
        if (_coro is not null) clauses.Add(LlvmContext.Null(L.Ptr));

        var (savedBlock, savedUnwind, savedHook) = (_b.InsertBlock, _b.UnwindDest, _b.UnwindHook);
        (_b.UnwindDest, _b.UnwindHook) = (default, null);
        LLVMValueRef lp;
        if (Traced)
        {
            // Calls reach this through per-site pads (FunctionCodeGen.StackTraces.cs), which record the frame.
            var target = NewSitePadTarget(clauses, cleanup: true, "lpad.record");
            pad = _landingPads[tryRegion] = target.Body;
            lp = target.Pad;
        }
        else
        {
            pad = _landingPads[tryRegion] = NewBlock("lpad");
            _b.PositionAtEnd(pad);
            lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, clauses, cleanup: true);
        }
        var exception = CallRuntime("csrt_begin_catch", L.Ptr, [L.Ptr], _b.ExtractValue(lp, 0));
        _b.Store(exception, ExceptionSlot(tryRegion));
        _b.Br(Dispatch(tryRegion));
        _b.PositionAtEnd(savedBlock);
        (_b.UnwindDest, _b.UnwindHook) = (savedUnwind, savedHook);
        return pad;
    }

    private void EnsurePersonality()
    {
        if (_hasPersonality) return;
        Native.LLVMSetPersonalityFn(_fn.Fn, _m.Runtime("csrt_personality", L.I32).Fn);
        _hasPersonality = true;
    }

    private LLVMValueRef CatchTypeInfo(ITypeSymbol type) => S(type) switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Class } named when _m.IsUserType(named) || _m.IsLibraryException(named) =>
            _m.GetLayout(named).MethodTable,
        _ => throw CompileError.Unsupported($"catching '{type}'", type),
    };

    /// <summary>Finds a handler for the exception in R's slot; otherwise finally, then outward.</summary>
    private LLVMBasicBlockRef Dispatch(ControlFlowRegion tryRegion)
    {
        if (_dispatch.TryGetValue(tryRegion, out var dispatch)) return dispatch;
        var (savedBlock, savedUnwind, savedHook) = (_b.InsertBlock, _b.UnwindDest, _b.UnwindHook);
        // Dispatch code only calls non-throwing runtime helpers, and csrt_rethrow, which leaves the
        // frame (already recorded by the landing pad).
        (_b.UnwindDest, _b.UnwindHook) = (default, null);
        dispatch = _dispatch[tryRegion] = NewBlock("dispatch");
        _b.PositionAtEnd(dispatch);
        var statement = tryRegion.EnclosingRegion!;
        var slot = ExceptionSlot(tryRegion);

        if (statement.Kind == ControlFlowRegionKind.TryAndCatch)
        {
            foreach (var handler in Handlers(statement))
            {
                var next = NewBlock("catch.next");
                if (handler.Kind == ControlFlowRegionKind.FilterAndHandler) _filterRejected[handler] = next;
                var entry = _cfg!.Graph.Blocks[handler.FirstBlockOrdinal];
                if (!_cfg.Blocks.TryGetValue(entry, out var entryBlock)) { _b.Br(next); _b.PositionAtEnd(next); continue; }

                var exception = _b.Load(L.Ptr, slot);
                var enter = NewBlock("catch");
                if (IsCatchAll(handler.ExceptionType)) _b.Br(enter);
                else
                {
                    var matched = CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], exception, CatchTypeInfo(handler.ExceptionType!));
                    _b.CondBr(_b.ICmp(LLVMIntPredicate.NE, matched, LlvmContext.Null(L.Ptr)), enter, next);
                }
                _b.PositionAtEnd(enter);
                _b.Store(exception, ExceptionSlot(handler));
                _b.Br(entryBlock);
                _b.PositionAtEnd(next);
            }
            EmitPropagate(tryRegion);
        }
        else
        {
            // try/finally: run the finally, then keep propagating.
            var finallyRegion = statement.NestedRegions.First(r => r.Kind == ControlFlowRegionKind.Finally);
            var propagate = NewBlock("propagate");
            EnterFinally(finallyRegion, propagate);
            _b.PositionAtEnd(propagate);
            EmitPropagate(tryRegion);
        }

        _b.PositionAtEnd(savedBlock);
        (_b.UnwindDest, _b.UnwindHook) = (savedUnwind, savedHook);
        return dispatch;
    }

    private void EmitPropagate(ControlFlowRegion tryRegion)
    {
        var exception = _b.Load(L.Ptr, ExceptionSlot(tryRegion));
        if (OuterTry(tryRegion) is { } outer)
        {
            _b.Store(exception, ExceptionSlot(outer));
            _b.Br(Dispatch(outer));
        }
        else if (_coro is not null)
        {
            _b.Store(exception, _coro.ExceptionSlot); // escapes the coroutine body: fault the task
            _b.Br(_coro.Fault);
        }
        else
        {
            CallRuntime("csrt_rethrow", L.Void, [L.Ptr], exception); // plain call: unwinds into our caller
            _b.Unreachable();
        }
    }

    /// <summary>Jumps into a finally block, registering <paramref name="continuation"/> as where it resumes.</summary>
    private void EnterFinally(ControlFlowRegion finallyRegion, LLVMBasicBlockRef continuation)
    {
        if (!_cfg!.Finally.TryGetValue(finallyRegion, out var info))
            _cfg.Finally[finallyRegion] = info = (_allocas.Alloca(L.I32, "finally.sel"), []);
        var index = info.Continuations.Count;
        info.Continuations.Add(continuation);
        _b.Store(L.ConstInt(L.I32, index), info.Selector);
        NoteFinallyEntry(finallyRegion, normal: false);
        _b.Br(_cfg.Blocks[_cfg.Graph.Blocks[finallyRegion.FirstBlockOrdinal]]);
    }

    /// <summary>A `when` filter evaluated to false: continue with the next handler.</summary>
    private LLVMBasicBlockRef FilterRejected(BasicBlock filterBlock)
    {
        var handler = HandlerOf(filterBlock) ?? throw CompileError.Unsupported("exception filter outside a handler");
        var tryRegion = handler.EnclosingRegion!.NestedRegions.First(r => r.Kind == ControlFlowRegionKind.Try);
        Dispatch(tryRegion);
        return _filterRejected[handler];
    }

    // ======================================================================================
    // throw / rethrow / catch (e)
    // ======================================================================================

    private void EmitThrow(IOperation? exception)
    {
        if (exception is null) throw CompileError.Unsupported("throw without an exception");
        var value = EmitValue(exception);
        CallRuntime("csrt_throw_object", L.Void, [L.Ptr], value);
        _b.Unreachable();
    }

    /// <summary>Throws a new library exception (e.g. InvalidOperationException) with a fixed message.</summary>
    internal void ThrowNewLibraryException(INamedTypeSymbol type, string message)
    {
        var layout = _m.GetLayout(type);
        var exception = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
        var none = LlvmContext.Null(L.Ptr);
        CallRuntime("csrt_exception_init", L.Void, [L.Ptr, L.Ptr, L.Ptr, L.Ptr], exception, _m.StringLiteral(message), none, none);
        CallRuntime("csrt_throw_object", L.Void, [L.Ptr], exception);
        _b.Unreachable();
    }

    private void EmitRethrow(IOperation? at)
    {
        var handler = HandlerOf(_currentBlock) ?? throw CompileError.Unsupported("rethrow outside a catch block", at);
        var exception = _b.Load(L.Ptr, ExceptionSlot(handler));
        _traceSuppressed = true; // the frame is in the trace already (where the exception was caught)
        try { CallRuntime("csrt_rethrow", L.Void, [L.Ptr], exception); }
        finally { _traceSuppressed = false; }
        _b.Unreachable();
    }

    private LLVMValueRef EmitCaughtException(ICaughtExceptionOperation op)
    {
        var handler = HandlerOf(_currentBlock) ?? throw CompileError.Unsupported("caught exception outside a handler", op);
        return _b.Load(L.Ptr, ExceptionSlot(handler));
    }

    // ======================================================================================
    // Library exception classes: construction and members
    // ======================================================================================

    /// <summary>Messages the BCL constructors use when no message is passed (what .NET prints).</summary>
    private static readonly Dictionary<string, string> s_defaultMessages = new()
    {
        ["System.SystemException"] = "System error.",
        ["System.ArgumentException"] = "Value does not fall within the expected range.",
        ["System.ArgumentNullException"] = "Value cannot be null.",
        ["System.ArgumentOutOfRangeException"] = "Specified argument was out of the range of valid values.",
        ["System.InvalidOperationException"] = "Operation is not valid due to the current state of the object.",
        ["System.ObjectDisposedException"] = "Cannot access a disposed object.",
        ["System.Threading.ThreadStateException"] = "Thread was in an invalid state for the operation being executed.",
        ["System.Threading.SynchronizationLockException"] = "Object synchronization method was called from an unsynchronized block of code.",
        ["System.NotSupportedException"] = "Specified method is not supported.",
        ["System.PlatformNotSupportedException"] = "Operation is not supported on this platform.",
        ["System.NotImplementedException"] = "The method or operation is not implemented.",
        ["System.NullReferenceException"] = "Object reference not set to an instance of an object.",
        ["System.IndexOutOfRangeException"] = "Index was outside the bounds of the array.",
        ["System.ArithmeticException"] = "Overflow or underflow in the arithmetic operation.",
        ["System.DivideByZeroException"] = "Attempted to divide by zero.",
        ["System.OverflowException"] = "Arithmetic operation resulted in an overflow.",
        ["System.InvalidCastException"] = "Specified cast is not valid.",
        ["System.FormatException"] = "One of the identified items was in an invalid format.",
        ["System.UriFormatException"] = "One of the identified items was in an invalid format.",
        ["System.Collections.Generic.KeyNotFoundException"] = "The given key was not present in the dictionary.",
        ["System.OperationCanceledException"] = "The operation was canceled.",
        ["System.Threading.Tasks.TaskCanceledException"] = "A task was canceled.",
        ["System.AggregateException"] = "One or more errors occurred.",
        ["System.TimeoutException"] = "The operation has timed out.",
        ["System.IO.IOException"] = "I/O error occurred.",
        ["System.UnauthorizedAccessException"] = "Attempted to perform an unauthorized operation.",
        ["System.BadImageFormatException"] = "Format of the executable (.exe) or library (.dll) is invalid.",
        ["System.Runtime.CompilerServices.SwitchExpressionException"] = "Non-exhaustive switch expression failed to match its input.",
    };

    private LLVMValueRef EmitLibraryExceptionCreation(IObjectCreationOperation creation)
    {
        var layout = _m.GetLayout((INamedTypeSymbol)creation.Type!);
        var exception = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
        EmitExceptionConstructor(creation.Constructor!, exception, creation.Arguments);
        return exception;
    }

    /// <summary>Runs a BCL exception constructor: binds message / innerException / paramName by name.</summary>
    private void EmitExceptionConstructor(IMethodSymbol constructor, LLVMValueRef exception, ImmutableArray<IArgumentOperation> arguments)
    {
        LLVMValueRef? message = null, inner = null, paramName = null, actualValue = null, objectName = null, inners = null;
        var aggregate = Mangler.MetadataName(constructor.ContainingType) == "System.AggregateException";
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.Name == "actualValue")
            {
                // ArgumentOutOfRangeException(paramName, actualValue, message): keep the formatted value
                // (null when the argument is null), shown by Message as "\nActual value was X.".
                var raw = argument.Value;
                while (raw is IConversionOperation { Type.SpecialType: SpecialType.System_Object } c) raw = c.Operand;
                actualValue = raw.ConstantValue is { HasValue: true, Value: null } ? null : ToStringValue(argument.Value);
                continue;
            }
            var value = EmitValue(argument.Value);
            var uriFormat = Mangler.MetadataName(constructor.ContainingType) == "System.UriFormatException";
            switch (argument.Parameter?.Name)
            {
                case "message": message = value; break;
                case "textString" when uriFormat: message = value; break; // UriFormatException(string textString, Exception e)
                case "e" when uriFormat: inner = value; break;
                case "innerException": inner = value; break;
                case "paramName": paramName = value; break;
                case "fileName": paramName = value; break; // FileNotFoundException.FileName (csrt keeps it in param_name)
                case "objectName": objectName = value; break;
                case "innerExceptions" when aggregate: // Exception[] or IEnumerable<Exception>
                    inners = CallCoreLib("AggregateExceptionSupport", argument.Parameter!.Type is IArrayTypeSymbol ? "FromArray" : "FromEnumerable", value);
                    break;
                // other parameters (unmatchedValue, ...) are evaluated for side effects only
            }
        }
        if (aggregate && (inners is not null || inner is not null))
        {
            // AggregateException(message, innerException) has one inner exception; its Message appends
            // " (inner message)" per inner exception, after the default text when no message is passed.
            inners ??= CallCoreLib("AggregateExceptionSupport", "FromSingle", inner!.Value);
            var baseMessage = message ?? _m.StringLiteral(s_defaultMessages["System.AggregateException"]);
            message = CallCoreLib("AggregateExceptionSupport", "Message", baseMessage, inners.Value);
            inner = CallCoreLib("AggregateExceptionSupport", "First", inners.Value);
        }
        if (objectName is { } name)  // ObjectDisposedException: "\nObject name: 'X'." is part of Message
            message = CallRuntime("csrt_object_disposed_message", L.Ptr, [L.Ptr, L.Ptr], message ?? LlvmContext.Null(L.Ptr), name);
        if (message is null && s_defaultMessages.TryGetValue(Mangler.MetadataName(constructor.ContainingType), out var text))
            message = _m.StringLiteral(text);
        var none = LlvmContext.Null(L.Ptr);
        CallRuntime("csrt_exception_init", L.Void, [L.Ptr, L.Ptr, L.Ptr, L.Ptr], exception, message ?? none, inner ?? none, paramName ?? none);
        if (actualValue is { } actual) CallRuntime("csrt_exception_set_actual_value", L.Void, [L.Ptr, L.Ptr], exception, actual);
    }

    private bool TryEmitLibraryExceptionCall(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        var target = inv.TargetMethod;
        if (!_m.IsLibraryException(target.ContainingType)) return false;
        if (target.MethodKind == MethodKind.Constructor)
        {
            // `: base(message)` from a user exception class.
            EmitExceptionConstructor(target, EmitValue(inv.Instance!), inv.Arguments);
            return true;
        }
        if (!target.IsStatic && (target.IsVirtual || target.IsOverride) && target.Parameters.IsEmpty && target.Name == "ToString")
        {
            var self = EmitValue(inv.Instance!);
            NullCheck(self);
            _m.WarnExceptionToString(inv.Instance?.Type, inv);
            result = inv.Instance?.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax
                ? CallRuntime("csrt_exception_to_string", L.Ptr, [L.Ptr], self) // base.ToString(): non-virtual
                : VirtualCall(target, self, [self]);
            return true;
        }
        return false;
    }

    private static bool IsFileException(INamedTypeSymbol type) =>
        type.ToDisplayString() is "System.IO.FileNotFoundException" or "System.IO.FileLoadException";

    private bool TryEmitLibraryExceptionProperty(IPropertyReferenceOperation p, out LLVMValueRef result)
    {
        result = default;
        var property = p.Property;
        if (!_m.IsLibraryException(property.ContainingType) || property.IsStatic) return false;
        uint? field = property.Name switch
        {
            "InnerException" => ModuleCodeGen.ExceptionInnerField,
            "ParamName" => ModuleCodeGen.ExceptionParamNameField,
            "FileName" when IsFileException(property.ContainingType) => ModuleCodeGen.ExceptionParamNameField,
            _ => null,
        };
        if (field is null && property.Name is not ("Message" or "StackTrace")) return false;
        var self = EmitValue(p.Instance!);
        NullCheck(self);
        if (property.Name == "StackTrace")
        {
            // Exceptions only carry frames when the program records them (ModuleCodeGen.StackTraces),
            // which reading StackTrace anywhere turns on.
            _m.WarnStackTraceFrames(p);
            result = CallRuntime("csrt_exception_get_stack_trace", L.Ptr, [L.Ptr], self);
            return true;
        }
        if (field is { } index)
        {
            var layout = _m.GetLayout(property.ContainingType);
            result = _b.Load(L.Ptr, _b.StructGep(layout.InstanceType, self, index), property.Name);
        }
        else if (p.Instance?.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax)
            result = CallRuntime("csrt_exception_get_message", L.Ptr, [L.Ptr], self); // base.Message: non-virtual
        else result = VirtualCall(property.GetMethod!, self, [self]); // Message is virtual
        return true;
    }

    /// <summary>The body of a bodyless user `extern` method: throws BadImageFormatException like .NET does when it is called.</summary>
    internal void EmitBodylessExternStub()
    {
        CallCoreLib("BodylessExtern", "Throw");
        _b.Unreachable();
        Finish();
    }
}
