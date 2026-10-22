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

    private static bool IsProtectedTry(ControlFlowRegion region) =>
        region.Kind == ControlFlowRegionKind.Try
        && region.EnclosingRegion?.Kind is ControlFlowRegionKind.TryAndCatch or ControlFlowRegionKind.TryAndFinally;

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

    private bool IsCatchAll(ITypeSymbol? type) =>
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
        _b.UnwindDest = tryRegion is null ? default : LandingPad(tryRegion);
        _b.MayThrow = _m.MayThrow;
    }

    private void EndCfg()
    {
        _b.UnwindDest = default;
        _currentBlock = null;
    }

    // ======================================================================================
    // Landing pads and dispatch
    // ======================================================================================

    private LLVMBasicBlockRef LandingPad(ControlFlowRegion tryRegion)
    {
        if (_landingPads.TryGetValue(tryRegion, out var pad)) return pad;
        if (!_hasPersonality)
        {
            Native.LLVMSetPersonalityFn(_fn.Fn, _m.Runtime("csrt_personality", L.I32).Fn);
            _hasPersonality = true;
        }

        // Clauses for this try and every enclosing try (innermost first) so the search phase sees all handlers.
        List<LLVMValueRef> clauses = [];
        for (var t = tryRegion; t is not null; t = OuterTry(t))
            if (t.EnclosingRegion!.Kind == ControlFlowRegionKind.TryAndCatch)
                foreach (var handler in Handlers(t.EnclosingRegion))
                    clauses.Add(IsCatchAll(handler.ExceptionType) ? LlvmContext.Null(L.Ptr) : CatchTypeInfo(handler.ExceptionType!));

        var (savedBlock, savedUnwind) = (_b.InsertBlock, _b.UnwindDest);
        _b.UnwindDest = default;
        pad = _landingPads[tryRegion] = NewBlock("lpad");
        _b.PositionAtEnd(pad);
        var lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, clauses, cleanup: true);
        var exception = CallRuntime("csrt_begin_catch", L.Ptr, [L.Ptr], _b.ExtractValue(lp, 0));
        _b.Store(exception, ExceptionSlot(tryRegion));
        _b.Br(Dispatch(tryRegion));
        _b.PositionAtEnd(savedBlock);
        _b.UnwindDest = savedUnwind;
        return pad;
    }

    private LLVMValueRef CatchTypeInfo(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Class } named when _m.IsUserType(named) || _m.IsLibraryException(named) =>
            _m.GetLayout(named).MethodTable,
        _ => throw CompileError.Unsupported($"catching '{type}'", type),
    };

    /// <summary>Finds a handler for the exception in R's slot; otherwise finally, then outward.</summary>
    private LLVMBasicBlockRef Dispatch(ControlFlowRegion tryRegion)
    {
        if (_dispatch.TryGetValue(tryRegion, out var dispatch)) return dispatch;
        var (savedBlock, savedUnwind) = (_b.InsertBlock, _b.UnwindDest);
        _b.UnwindDest = default; // dispatch code only calls non-throwing runtime helpers
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
        _b.UnwindDest = savedUnwind;
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
        CallRuntime("csrt_rethrow", L.Void, [L.Ptr], _b.Load(L.Ptr, ExceptionSlot(handler)));
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
        ["System.NotSupportedException"] = "Specified method is not supported.",
        ["System.NotImplementedException"] = "The method or operation is not implemented.",
        ["System.NullReferenceException"] = "Object reference not set to an instance of an object.",
        ["System.IndexOutOfRangeException"] = "Index was outside the bounds of the array.",
        ["System.ArithmeticException"] = "Overflow or underflow in the arithmetic operation.",
        ["System.DivideByZeroException"] = "Attempted to divide by zero.",
        ["System.OverflowException"] = "Arithmetic operation resulted in an overflow.",
        ["System.InvalidCastException"] = "Specified cast is not valid.",
        ["System.FormatException"] = "One of the identified items was in an invalid format.",
        ["System.Collections.Generic.KeyNotFoundException"] = "The given key was not present in the dictionary.",
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
        LLVMValueRef? message = null, inner = null, paramName = null;
        foreach (var argument in arguments)
        {
            var value = EmitValue(argument.Value);
            switch (argument.Parameter?.Name)
            {
                case "message": message = value; break;
                case "innerException": inner = value; break;
                case "paramName": paramName = value; break;
                // other parameters (actualValue, unmatchedValue, ...) are evaluated for side effects only
            }
        }
        if (message is null && s_defaultMessages.TryGetValue(Mangler.MetadataName(constructor.ContainingType), out var text))
            message = _m.StringLiteral(text);
        var none = LlvmContext.Null(L.Ptr);
        CallRuntime("csrt_exception_init", L.Void, [L.Ptr, L.Ptr, L.Ptr, L.Ptr], exception, message ?? none, inner ?? none, paramName ?? none);
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
            result = inv.Instance?.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax
                ? CallRuntime("csrt_exception_to_string", L.Ptr, [L.Ptr], self) // base.ToString(): non-virtual
                : VirtualCall(target, self, [self]);
            return true;
        }
        return false;
    }

    private bool TryEmitLibraryExceptionProperty(IPropertyReferenceOperation p, out LLVMValueRef result)
    {
        result = default;
        var property = p.Property;
        if (!_m.IsLibraryException(property.ContainingType) || property.IsStatic) return false;
        uint? field = property.Name switch
        {
            "InnerException" => ModuleCodeGen.ExceptionInnerField,
            "ParamName" => ModuleCodeGen.ExceptionParamNameField,
            _ => null,
        };
        if (field is null && property.Name != "Message") return false;
        var self = EmitValue(p.Instance!);
        NullCheck(self);
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
}
