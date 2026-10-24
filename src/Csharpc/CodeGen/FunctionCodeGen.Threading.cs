using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// System.Threading surface (docs/13-threading.md): Thread, Monitor/`lock`, Lock, wait handles and
/// Parallel map onto src/runtime/threads.c; Interlocked and Volatile are LLVM atomics inline.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private static bool IsThreading(ITypeSymbol? type, string name) => IsLibraryClass(type, "System.Threading", name);

    /// <summary>
    /// Library types lowered here by name rather than through an intrinsic key or a corelib shadow:
    /// Frontend.FrameworkIl must not compile their assemblies from IL instead.
    /// </summary>
    internal static readonly string[] TypesLoweredByName =
    [
        "System.Threading.Tasks.Parallel", "System.Threading.Thread", "System.Threading.Monitor", "System.Threading.Lock",
        "System.Threading.Interlocked", "System.Threading.Volatile", "System.Threading.ManualResetEventSlim",
        "System.Threading.ManualResetEvent", "System.Threading.AutoResetEvent", "System.Threading.SemaphoreSlim",
        "System.Threading.CountdownEvent", "System.Threading.EventWaitHandle", "System.Threading.WaitHandle",
    ];

    private LLVMValueRef TArg(IInvocationOperation inv, int ordinal)
    {
        var a = inv.Arguments.First(x => x.Parameter!.Ordinal == ordinal);
        return Coerce(EmitValue(a.Value), TypeOf(a.Value), S(a.Parameter!.Type));
    }

    private LLVMValueRef ArgAddress(IInvocationOperation inv, int ordinal) =>
        AddressOf(inv.Arguments.First(x => x.Parameter!.Ordinal == ordinal).Value);

    private LLVMValueRef Rt(string name, LLVMTypeRef ret, LLVMTypeRef[] parameters, params LLVMValueRef[] args) =>
        CallRuntime(name, ret, parameters, args);

    // ======================================================================================
    // Object creation
    // ======================================================================================

    private bool TryThreadingCreation(IObjectCreationOperation creation, out LLVMValueRef result)
    {
        result = default;
        if (creation.Type is not INamedTypeSymbol type || creation.Constructor is not { } ctor) return false;
        LLVMValueRef CtorArg(int i)
        {
            var a = creation.Arguments.First(x => x.Parameter!.Ordinal == i);
            return Coerce(EmitValue(a.Value), TypeOf(a.Value), S(a.Parameter!.Type));
        }
        LLVMValueRef I32(long v) => L.ConstInt(L.I32, v);
        LLVMValueRef Event(int kind, LLVMValueRef count, LLVMValueRef max) =>
            Rt("csrt_event_new", L.Ptr, [L.I32, L.I32, L.I32], I32(kind), count, max);
        LLVMValueRef BoolCount(int i) => _b.Cast(LLVMOpcode.ZExt, CtorArg(i), L.I32);

        switch (type.Name)
        {
            case "Thread" when IsThreading(type, "Thread"):
                var parameterized = ctor.Parameters[0].Type.Name == "ParameterizedThreadStart";
                result = Rt("csrt_thread_new", L.Ptr, [L.Ptr, L.I1], CtorArg(0), parameterized ? L.True : L.False);
                return true;
            case "Lock" when IsThreading(type, "Lock"):
                result = Rt("csrt_lock_new", L.Ptr, []);
                return true;
            case "ManualResetEventSlim" when IsThreading(type, "ManualResetEventSlim"):
                result = Event(0, ctor.Parameters.IsEmpty ? I32(0) : BoolCount(0), I32(0));
                return true;
            case "ManualResetEvent" when IsThreading(type, "ManualResetEvent"):
                result = Event(0, BoolCount(0), I32(1));
                return true;
            case "AutoResetEvent" when IsThreading(type, "AutoResetEvent"):
                result = Event(1, BoolCount(0), I32(1));
                return true;
            case "SemaphoreSlim" when IsThreading(type, "SemaphoreSlim"):
                result = Event(2, CtorArg(0), ctor.Parameters.Length > 1 ? CtorArg(1) : I32(int.MaxValue));
                return true;
            case "CountdownEvent" when IsThreading(type, "CountdownEvent"):
                result = Event(3, CtorArg(0), I32(0));
                return true;
        }
        return false;
    }

    // ======================================================================================
    // Members
    // ======================================================================================

    private bool TryThreadingMember(IOperation op, out LLVMValueRef result)
    {
        result = default;
        switch (op)
        {
            case IInvocationOperation inv:
                return TryThreadingInvocation(inv, out result);
            case IPropertyReferenceOperation p:
                return TryThreadingProperty(p, out result);
        }
        return false;
    }

    private bool TryThreadingProperty(IPropertyReferenceOperation p, out LLVMValueRef result)
    {
        result = default;
        var owner = p.Property.ContainingType;
        LLVMValueRef Self() => EmitValue(p.Instance!);
        var name = p.Property.Name;
        if (IsThreading(owner, "Thread"))
        {
            result = name switch
            {
                "CurrentThread" => Rt("csrt_thread_current", L.Ptr, []),
                "IsAlive" => Rt("csrt_thread_is_alive", L.I1, [L.Ptr], Self()),
                "ManagedThreadId" => Rt("csrt_thread_id", L.I32, [L.Ptr], Self()),
                "Name" => Rt("csrt_thread_get_name", L.Ptr, [L.Ptr], Self()),
                "IsBackground" => Rt("csrt_thread_get_background", L.I1, [L.Ptr], Self()),
                "IsThreadPoolThread" => L.False,
                "CurrentCulture" or "CurrentUICulture" => CallCoreLib("CultureStrings", "GetThreadCulture",
                    _b.ICmp(LLVMIntPredicate.EQ, Self(), Rt("csrt_thread_current", L.Ptr, [])), name == "CurrentUICulture" ? L.True : L.False),
                _ => default,
            };
            return result.Handle != 0;
        }
        if (IsLibraryClass(owner, "System", "Environment") && name == "CurrentManagedThreadId")
        {
            result = Rt("csrt_thread_id", L.I32, [L.Ptr], Rt("csrt_thread_current", L.Ptr, []));
            return true;
        }
        if (IsLibraryClass(owner, "System", "Environment") && name == "ProcessorCount")
        {
            result = Rt("csrt_processor_count", L.I32, []);
            return true;
        }
        if (IsThreading(owner, "Lock") && name == "IsHeldByCurrentThread")
        {
            result = Rt("csrt_lock_is_held", L.I1, [L.Ptr], Self());
            return true;
        }
        if (owner is INamedTypeSymbol { ContainingNamespace: { } ns } && ns.ToDisplayString() == "System.Threading"
            && owner.Name is "ManualResetEventSlim" or "ManualResetEvent" or "AutoResetEvent" or "SemaphoreSlim" or "CountdownEvent" or "EventWaitHandle" or "WaitHandle")
        {
            result = name switch
            {
                "IsSet" => Rt("csrt_event_is_set", L.I1, [L.Ptr], Self()),
                "CurrentCount" => Rt("csrt_event_count", L.I32, [L.Ptr], Self()),
                _ => default,
            };
            return result.Handle != 0;
        }
        return false;
    }

    private bool TryThreadingPropertySet(IPropertyReferenceOperation p, Func<LLVMValueRef> value, ITypeSymbol? valueType, out LLVMValueRef result)
    {
        result = default;
        if (!IsThreading(p.Property.ContainingType, "Thread")) return false;
        var self = EmitValue(p.Instance!);
        var v = Coerce(value(), valueType, p.Property.Type);
        switch (p.Property.Name)
        {
            case "Name": Rt("csrt_thread_set_name", L.Void, [L.Ptr, L.Ptr], self, v); break;
            case "IsBackground": Rt("csrt_thread_set_background", L.Void, [L.Ptr, L.I1], self, v); break;
            case "CurrentCulture" or "CurrentUICulture":
                CallCoreLib("CultureStrings", "SetThreadCulture", _b.ICmp(LLVMIntPredicate.EQ, self, Rt("csrt_thread_current", L.Ptr, [])), v,
                    p.Property.Name == "CurrentUICulture" ? L.True : L.False);
                break;
            default: return false;
        }
        result = v;
        return true;
    }

    private bool TryThreadingInvocation(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        var m = inv.TargetMethod;
        var owner = m.ContainingType;
        var n = m.Parameters.Length;
        LLVMValueRef Self() => EmitValue(inv.Instance!);
        LLVMValueRef I32(long v) => L.ConstInt(L.I32, v);

        if (IsThreading(owner, "Thread"))
        {
            switch (m.Name)
            {
                case "Start" when n == 0: Rt("csrt_thread_start", L.Void, [L.Ptr], Self()); return true;
                case "Start" when n == 1: Rt("csrt_thread_start_with", L.Void, [L.Ptr, L.Ptr], Self(), TArg(inv, 0)); return true;
                case "Join" when n == 0: Rt("csrt_thread_join", L.Void, [L.Ptr], Self()); return true;
                case "Join" when n == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Int32:
                    result = Rt("csrt_thread_join_timeout", L.I1, [L.Ptr, L.I32], Self(), TArg(inv, 0)); return true;
                case "Sleep" when n == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Int32:
                    Rt("csrt_thread_sleep", L.Void, [L.I32], TArg(inv, 0)); return true;
                case "Yield": result = Rt("csrt_thread_yield", L.I1, []); return true;
                case "SpinWait": EmitValue(inv.Arguments[0].Value); return true;
                case "MemoryBarrier": Native.LLVMBuildFence(_b.Ref, LLVMAtomicOrdering.SequentiallyConsistent, 0, ""); return true;
            }
            return false;
        }

        if (IsThreading(owner, "Monitor"))
        {
            var obj = TArg(inv, 0);
            void SetTaken(int ordinal, LLVMValueRef taken) => StoreValue(_b.Cast(LLVMOpcode.ZExt, taken, L.I8), ArgAddress(inv, ordinal));
            var refIndex = m.Parameters.FirstOrDefault(p => p.RefKind == RefKind.Ref)?.Ordinal ?? -1;
            var timeoutIndex = m.Parameters.FirstOrDefault(p => p.RefKind == RefKind.None && p.Ordinal > 0 && p.Type.SpecialType == SpecialType.System_Int32)?.Ordinal ?? -1;
            if (m.Parameters.Any(p => p.Ordinal > 0 && p.RefKind == RefKind.None && p.Type.SpecialType != SpecialType.System_Int32))
                return false; // TimeSpan overloads: not mapped
            switch (m.Name)
            {
                case "Enter":
                    Rt("csrt_monitor_enter", L.Void, [L.Ptr], obj);
                    if (refIndex >= 0) SetTaken(refIndex, L.True);
                    return true;
                case "TryEnter":
                {
                    var taken = Rt("csrt_monitor_try_enter", L.I1, [L.Ptr, L.I32], obj, timeoutIndex >= 0 ? TArg(inv, timeoutIndex) : I32(0));
                    if (refIndex >= 0) SetTaken(refIndex, taken); else result = taken;
                    return true;
                }
                case "Exit": Rt("csrt_monitor_exit", L.Void, [L.Ptr], obj); return true;
                case "IsEntered": result = Rt("csrt_monitor_is_entered", L.I1, [L.Ptr], obj); return true;
                case "Wait":
                    result = Rt("csrt_monitor_wait", L.I1, [L.Ptr, L.I32], obj, timeoutIndex >= 0 ? TArg(inv, timeoutIndex) : I32(-1));
                    return true;
                case "Pulse": Rt("csrt_monitor_pulse", L.Void, [L.Ptr], obj); return true;
                case "PulseAll": Rt("csrt_monitor_pulse_all", L.Void, [L.Ptr], obj); return true;
            }
            return false;
        }

        if (IsThreading(owner, "Lock") || owner is { Name: "Scope", ContainingType.Name: "Lock" })
        {
            var self = Self();
            switch (m.Name)
            {
                case "Enter": Rt("csrt_lock_enter", L.Void, [L.Ptr], self); return true;
                case "Exit": Rt("csrt_lock_exit", L.Void, [L.Ptr], self); return true;
                case "TryEnter" when n == 0: result = Rt("csrt_lock_try_enter", L.I1, [L.Ptr, L.I32], self, I32(0)); return true;
                case "TryEnter" when m.Parameters[0].Type.SpecialType == SpecialType.System_Int32:
                    result = Rt("csrt_lock_try_enter", L.I1, [L.Ptr, L.I32], self, TArg(inv, 0)); return true;
                case "EnterScope": result = Rt("csrt_lock_enter_scope", L.Ptr, [L.Ptr], self); return true;
                case "Dispose": Rt("csrt_lock_exit", L.Void, [L.Ptr], self); return true; // Lock.Scope
            }
            return false;
        }

        if (owner is INamedTypeSymbol { ContainingNamespace: { } ns } && ns.ToDisplayString() == "System.Threading"
            && owner.Name is "ManualResetEventSlim" or "ManualResetEvent" or "AutoResetEvent" or "SemaphoreSlim" or "CountdownEvent" or "EventWaitHandle" or "WaitHandle")
        {
            if (m.Parameters.Any(p => p.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Boolean))) return false;
            var self = Self();
            var arg0 = n > 0 ? TArg(inv, 0) : default;
            switch (m.Name)
            {
                case "Set": result = Rt("csrt_event_set", L.I32, [L.Ptr, L.I32], self, I32(1)); result = m.ReturnsVoid ? default : L.True; return true;
                case "Reset": Rt("csrt_event_reset", L.Void, [L.Ptr], self); result = m.ReturnsVoid ? default : L.True; return true;
                case "Wait" or "WaitOne":
                    result = Rt("csrt_event_wait", L.I1, [L.Ptr, L.I32], self, n > 0 ? arg0 : I32(-1));
                    if (m.ReturnsVoid) result = default;
                    return true;
                case "Release": result = Rt("csrt_event_set", L.I32, [L.Ptr, L.I32], self, n > 0 ? arg0 : I32(1)); return true;
                case "Signal":
                    result = _b.ICmp(LLVMIntPredicate.NE, Rt("csrt_event_set", L.I32, [L.Ptr, L.I32], self, n > 0 ? arg0 : I32(1)), I32(0));
                    return true;
                case "AddCount": Rt("csrt_countdown_add", L.Void, [L.Ptr, L.I32], self, n > 0 ? arg0 : I32(1)); return true;
                case "Dispose" or "Close": return true;
            }
            return false;
        }

        if (IsThreading(owner, "Interlocked")) return TryInterlocked(inv, out result);
        if (IsThreading(owner, "Volatile")) return TryVolatile(inv, out result);
        if (IsLibraryClass(owner, "System.Threading.Tasks", "Parallel")) return TryParallel(inv, out result);
        return false;
    }

    // ======================================================================================
    // Interlocked / Volatile: inline LLVM atomics (seq_cst like .NET's full fences)
    // ======================================================================================

    /// Atomic ops need integer operands: floats are bitcast, pointers converted.
    private (LLVMValueRef Value, Func<LLVMValueRef, LLVMValueRef> Back) AsAtomicInt(LLVMValueRef v)
    {
        var type = v.Type;
        if (type.IsFloatingPoint)
        {
            var it = type.Kind == LLVMTypeKind.Double ? L.I64 : L.I32;
            return (_b.Cast(LLVMOpcode.BitCast, v, it), r => _b.Cast(LLVMOpcode.BitCast, r, type));
        }
        if (type.IsPointer) return (_b.Cast(LLVMOpcode.PtrToInt, v, T.NativeInt), r => _b.Cast(LLVMOpcode.IntToPtr, r, L.Ptr));
        return (v, r => r);
    }

    private bool TryInterlocked(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        var m = inv.TargetMethod;
        const LLVMAtomicOrdering seq = LLVMAtomicOrdering.SequentiallyConsistent;
        LLVMValueRef Rmw(LLVMAtomicRMWBinOp op, LLVMValueRef address, LLVMValueRef value) =>
            Native.LLVMBuildAtomicRMW(_b.Ref, op, address, value, seq, 0);
        switch (m.Name)
        {
            case "MemoryBarrier" or "MemoryBarrierProcessWide":
                Native.LLVMBuildFence(_b.Ref, seq, 0, "");
                return true;
            case "Increment" or "Decrement":
            {
                var address = ArgAddress(inv, 0);
                var type = T.Value(S(m.Parameters[0].Type));
                var one = L.ConstInt(type, 1);
                var increment = m.Name == "Increment";
                var old = Rmw(increment ? LLVMAtomicRMWBinOp.Add : LLVMAtomicRMWBinOp.Sub, address, one);
                result = _b.BinOp(increment ? LLVMOpcode.Add : LLVMOpcode.Sub, old, one);
                return true;
            }
            case "Add":
            {
                var address = ArgAddress(inv, 0);
                var value = TArg(inv, 1);
                result = _b.BinOp(LLVMOpcode.Add, Rmw(LLVMAtomicRMWBinOp.Add, address, value), value);
                return true;
            }
            case "And" or "Or":
                result = Rmw(m.Name == "And" ? LLVMAtomicRMWBinOp.And : LLVMAtomicRMWBinOp.Or, ArgAddress(inv, 0), TArg(inv, 1));
                return true;
            case "Exchange":
            {
                var address = ArgAddress(inv, 0);
                var (value, back) = AsAtomicInt(TArg(inv, 1));
                result = back(Rmw(LLVMAtomicRMWBinOp.Xchg, address, value));
                return true;
            }
            case "CompareExchange":
            {
                var address = ArgAddress(inv, 0);
                var (value, back) = AsAtomicInt(TArg(inv, 1));
                var (comparand, _) = AsAtomicInt(TArg(inv, 2));
                var pair = Native.LLVMBuildAtomicCmpXchg(_b.Ref, address, comparand, value, seq, seq, 0);
                result = back(_b.ExtractValue(pair, 0));
                return true;
            }
            case "Read":
            {
                var type = S(m.Parameters[0].Type);
                var load = _b.Load(T.Storage(type), ArgAddress(inv, 0));
                Native.LLVMSetOrdering(load, seq);
                result = load;
                return true;
            }
        }
        return false;
    }

    private static bool IsAtomicCapable(LLVMTypeRef storage) => storage.IsInteger || storage.IsFloatingPoint || storage.IsPointer;

    /// Acquire load (Volatile.Read, `volatile` fields).
    internal LLVMValueRef VolatileLoad(LLVMValueRef address, ITypeSymbol type)
    {
        var storage = T.Storage(type);
        if (!IsAtomicCapable(storage)) return LoadValue(address, type);
        var load = _b.Load(storage, address);
        Native.LLVMSetOrdering(load, LLVMAtomicOrdering.Acquire);
        return type.SpecialType == SpecialType.System_Boolean ? _b.Cast(LLVMOpcode.Trunc, load, L.I1) : load;
    }

    /// Release store (Volatile.Write, `volatile` fields).
    internal void VolatileStore(LLVMValueRef value, LLVMValueRef address, ITypeSymbol type)
    {
        var stored = ToStorage(value, type);
        var store = _b.Store(stored, address);
        if (IsAtomicCapable(stored.Type)) Native.LLVMSetOrdering(store, LLVMAtomicOrdering.Release);
    }

    private bool TryVolatile(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        // Volatile.ReadBarrier/WriteBarrier (.NET 10): acquire / release fences, no operand.
        if (inv.TargetMethod.Parameters.IsEmpty)
        {
            switch (inv.TargetMethod.Name)
            {
                case "ReadBarrier": Native.LLVMBuildFence(_b.Ref, LLVMAtomicOrdering.Acquire, 0, ""); return true;
                case "WriteBarrier": Native.LLVMBuildFence(_b.Ref, LLVMAtomicOrdering.Release, 0, ""); return true;
            }
            return false;
        }
        var type = S(inv.TargetMethod.Parameters[0].Type);
        switch (inv.TargetMethod.Name)
        {
            case "Read": result = VolatileLoad(ArgAddress(inv, 0), type); return true;
            case "Write":
            {
                var address = ArgAddress(inv, 0);
                VolatileStore(TArg(inv, 1), address, type);
                return true;
            }
        }
        return false;
    }

    // ======================================================================================
    // Parallel.For / ForEach / Invoke
    // ======================================================================================

    private static int s_parallelBodies;

    private LLVMTypeRef ParallelContextType => L.Struct(L.Ptr, L.Ptr);

    private bool TryParallel(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        var m = inv.TargetMethod;
        if (m.Name == "Invoke" && m.Parameters.Length == 1 && m.Parameters[0].Type is IArrayTypeSymbol)
        {
            var arg = inv.Arguments[0];
            var actions = arg.Value is IArrayCreationOperation { Initializer: { } init } && arg.ArgumentKind == ArgumentKind.ParamArray
                ? EmitValue(arg.Value)
                : EmitValue(arg.Value);
            Rt("csrt_parallel_invoke", L.Void, [L.Ptr], actions);
            return true;
        }
        if (m.Name == "For" && m.Parameters.Length == 3 && m.Parameters[2].Type is INamedTypeSymbol { Name: "Action", TypeArguments.Length: 1 } action)
        {
            var indexType = action.TypeArguments[0];
            var from = TArg(inv, 0);
            var to = TArg(inv, 1);
            var body = TArg(inv, 2);
            NullCheck(body);
            var wide = indexType.SpecialType == SpecialType.System_Int64;
            RunParallel(wide ? from : _b.Cast(LLVMOpcode.SExt, from, L.I64), wide ? to : _b.Cast(LLVMOpcode.SExt, to, L.I64),
                body, LlvmContext.Null(L.Ptr), ParallelBody(null, wide));
            return true;
        }
        if (m.Name == "ForEach" && m.Parameters.Length == 2 && m.Parameters[1].Type is INamedTypeSymbol { Name: "Action", TypeArguments.Length: 1 })
        {
            var source = inv.Arguments.First(a => a.Parameter!.Ordinal == 0).Value;
            while (source is IConversionOperation { Conversion.IsImplicit: true } c) source = c.Operand;
            if (TypeOf(source) is not IArrayTypeSymbol { Rank: 1 } arrayType)
                throw CompileError.Unsupported($"Parallel.ForEach over '{TypeOf(source)}' (only arrays so far)", inv);
            var array = EmitValue(source);
            NullCheck(array);
            var body = TArg(inv, 1);
            NullCheck(body);
            var length = LoadArrayLength(array);
            RunParallel(L.ConstInt(L.I64, 0), length, body, array, ParallelBody(S(arrayType.ElementType), false));
            return true;
        }
        return false;
    }

    private void RunParallel(LLVMValueRef from, LLVMValueRef to, LLVMValueRef body, LLVMValueRef array, LoweredFunction thunk)
    {
        // The context lives in this frame (scanned by the GC) for the whole blocking call.
        var context = NewTemp(ParallelContextType, "parallel.ctx");
        _b.Store(body, _b.StructGep(ParallelContextType, context, 0));
        _b.Store(array, _b.StructGep(ParallelContextType, context, 1));
        Rt("csrt_parallel_for", L.Void, [L.I64, L.I64, L.Ptr, L.Ptr], from, to, thunk.Fn, context);
    }

    /// thunk(ctx, i): ctx->delegate(i) for Parallel.For, ctx->delegate(ctx->array[i]) for ForEach.
    private LoweredFunction ParallelBody(ITypeSymbol? elementType, bool wideIndex)
    {
        var type = L.FunctionType(L.Void, L.Ptr, L.I64);
        var fn = _m.Module.AddFunction($"__csharpc_parallel_body{Interlocked.Increment(ref s_parallelBodies)}", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        var lowered = new LoweredFunction(fn, type);
        new FunctionCodeGen(_m, null, lowered).EmitParallelBody(elementType, wideIndex);
        return lowered;
    }

    private void EmitParallelBody(ITypeSymbol? elementType, bool wideIndex)
    {
        var context = _fn.Fn.Param(0);
        var index = _fn.Fn.Param(1);
        var d = _b.Load(L.Ptr, _b.StructGep(ParallelContextType, context, 0), "body");
        LLVMValueRef value;
        LLVMTypeRef valueType;
        if (elementType is null)
        {
            value = wideIndex ? index : _b.Cast(LLVMOpcode.Trunc, index, L.I32);
            valueType = wideIndex ? L.I64 : L.I32;
        }
        else
        {
            var array = _b.Load(L.Ptr, _b.StructGep(ParallelContextType, context, 1), "source");
            var address = _b.InBoundsGep(T.ArrayHeader(T.Storage(elementType)), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), index]);
            value = LoadValue(address, elementType);
            valueType = T.Value(elementType);
        }
        InvokeDelegate(d, L.Void, (target, fn) => _b.Call(L.FunctionType(L.Void, L.Ptr, valueType), fn, [target, value]));
        _b.RetVoid();
        Finish();
    }
}
