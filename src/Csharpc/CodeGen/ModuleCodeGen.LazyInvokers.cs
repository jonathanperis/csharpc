using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Pay-for-play invokers of the methods a type's member lists describe (GetMethods, [DynamicallyAccessedMembers],
/// [DynamicDependency]). A method record's invoker (MethodBase.Invoke's stub) and function (MethodInfo.CreateDelegate)
/// call the method, so making them compiles it and everything it reaches. They are made only once the program
/// can use them: when the corelib's RuntimeMethodInfo.Invoke or CreateDelegate is lowered. A program that lists a
/// slice's ~100 Write overloads to find properties or attributes, and never invokes a method through reflection,
/// carries their metadata but not their code. Constructors and property accessors keep eager invokers
/// (Activator, DI and PropertyInfo.GetValue reach them through other paths).
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly List<MethodRecordData> _lazyInvokerRecords = [];
    private bool _methodInvokeLowered, _methodDelegateLowered;
    private int _lazyInvokersFilled = -1;

    /// <summary>Notes the corelib members that read a method record's invoker or function.</summary>
    private void NoteReflectionUse(IMethodSymbol method)
    {
        if (method.ContainingType is not { Name: "RuntimeMethodInfoImpl" } || !CoreLib.IsCoreLib(method)) return;
        if (method.Name == "Invoke" && method.Parameters.Length == 5) _methodInvokeLowered = true;
        else if (method.Name == "CreateDelegate" && method.Parameters.Length == 2) _methodDelegateLowered = true;
    }

    /// <summary>Makes the invokers and functions the program can now use; true when that created work.</summary>
    private bool EmitLazyInvokers()
    {
        if (!_methodInvokeLowered && !_methodDelegateLowered) return false;
        // Every record is visited once per state of the two flags (records added meanwhile are visited too).
        var state = (_methodInvokeLowered ? 1 : 0) | (_methodDelegateLowered ? 2 : 0);
        if (_lazyInvokersFilled == state && _lazyInvokerRecords.Count == _lazyInvokerScanned) return false;
        if (_lazyInvokersFilled != state) _lazyInvokerScanned = 0;
        _lazyInvokersFilled = state;
        var any = false;
        while (_lazyInvokerScanned < _lazyInvokerRecords.Count)
            any |= FillLazyInvoker(_lazyInvokerRecords[_lazyInvokerScanned++], _methodInvokeLowered, _methodDelegateLowered);
        return any;
    }

    private int _lazyInvokerScanned;

    /// <summary>
    /// Makes a listed method's invoker and/or function. They aren't roots of the program: what they reach counts as
    /// reached from the method itself, so an IL method below one that csharpc can't compile (a Razor slice's
    /// Write(decimal) reaching decimal's IUtf8SpanFormattable) becomes the explicit run-time stub of an unreached
    /// method, not a compile error of a program that never invokes it.
    /// </summary>
    private bool FillLazyInvoker(MethodRecordData record, bool invoke, bool function)
    {
        if (!(invoke && record.InvokePending) && !(function && record.FunctionPending) && !(function && record.OpenPending)) return false;
        var outerCaller = _currentCaller;
        _currentCaller = record.Method;
        try
        {
            if (invoke && record.InvokePending)
            {
                record.InvokePending = false;
                record.Invoke = ReflectionInvoker(record.Method);
            }
            if (function && record.FunctionPending)
            {
                record.FunctionPending = false;
                record.Function = RecordFunction(record.Method);
            }
            if (function && record.OpenPending)
            {
                record.OpenPending = false;
                record.OpenFunction = OpenInstanceFunction(record.Method);
            }
        }
        finally { _currentCaller = outerCaller; }
        return true;
    }

    /// <summary>Records whose invoker or function the program never needed: none (nothing reads them).</summary>
    private void FinishLazyInvokers()
    {
        foreach (var record in _lazyInvokerRecords)
        {
            if (record.InvokePending) record.Invoke = LlvmContext.Null(Llvm.Ptr);
            if (record.FunctionPending) record.Function = LlvmContext.Null(Llvm.Ptr);
            if (record.OpenPending) record.OpenFunction = LlvmContext.Null(Llvm.Ptr);
            record.InvokePending = record.FunctionPending = record.OpenPending = false;
        }
    }

    /// <summary>
    /// The method's own function for MethodInfo.CreateDelegate: an instance method of a class is called as a delegate
    /// calls its function, (target, args...); a static method through the thunk with the delegate calling convention
    /// (an ignored target first); null otherwise.
    /// </summary>
    /// A method csharpc can't compile (a parameter of a type it doesn't support) has none: CreateDelegate of it throws
    /// NotSupportedException, as invoking it does (ReflectionInvoker).
    private LLVMValueRef RecordFunction(IMethodSymbol method)
    {
        try
        {
            return !method.IsStatic && method.MethodKind == MethodKind.Ordinary && method.ContainingType.IsReferenceType
                    && !TypeSubstitution.IsOpen(method.ContainingType) && IsUserMethod(method) ? GetFunction(method).Fn
                : method is { IsStatic: true, MethodKind: MethodKind.Ordinary } && IsUserMethod(method) ? StaticThunk(method)
                : LlvmContext.Null(Llvm.Ptr);
        }
        catch (CompileError error)
        {
            if (Environment.GetEnvironmentVariable("CSHARPC_TRACE") == "1") Console.Error.WriteLine($"[csharpc] no delegate function for {method}: {error.Message}");
            return LlvmContext.Null(Llvm.Ptr);
        }
    }

    /// <summary>
    /// Can a record's method be the target of an open instance delegate (CreateDelegate without a target)? An instance
    /// method of a class or interface, by-value parameters and result, no open generics: the delegate's first parameter
    /// is the receiver, and a value type's would be a `ref` one.
    /// </summary>
    private static bool OpenInstanceDelegatable(IMethodSymbol method) =>
        !method.IsStatic && method.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet
        && method.ContainingType is { TypeKind: TypeKind.Class or TypeKind.Interface }
        && method.Parameters.All(p => p.RefKind == RefKind.None && p.Type is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol) && !p.Type.IsRefLikeType)
        && method.ReturnType is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol) && !method.ReturnType.IsRefLikeType && method.RefKind == RefKind.None
        && !method.IsGenericMethod && !TypeSubstitution.IsOpen(method);

    /// <summary>
    /// The entry point (ignored target, this, args...) of an open instance delegate over <paramref name="method"/>
    /// (MethodInfo.CreateDelegate(Type) of an instance method); null when csharpc can't call the method, which makes
    /// the corelib's CreateDelegate throw NotSupportedException.
    /// </summary>
    private LLVMValueRef OpenInstanceFunction(IMethodSymbol method)
    {
        // The signature's types are inside the try: a method whose parameter or result is a type csharpc can't map
        // (Type.TypeHandle returns System.RuntimeTypeHandle) has no open delegate function, it is not a compile error.
        try
        {
            List<LLVMTypeRef> parameters = [Llvm.Ptr, Llvm.Ptr];
            foreach (var p in method.Parameters) parameters.Add(Types.Value(p.Type));
            var type = Llvm.FunctionType(Types.Value(method.ReturnType), [.. parameters]);
            return SyntheticFunction(Mangler.Method(method) + "$opendelegate", type, (g, syntax) => g.EmitOpenInstanceThunk(method, syntax));
        }
        catch (CompileError error)
        {
            if (Environment.GetEnvironmentVariable("CSHARPC_TRACE") == "1") Console.Error.WriteLine($"[csharpc] no open delegate function for {method}: {error.Message}");
            return LlvmContext.Null(Llvm.Ptr);
        }
    }
}
