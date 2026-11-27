using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Classes deriving from System.Threading.Tasks.TaskCompletionSource(&lt;T&gt;) (System.Net.Http's connection
/// waiters and write-queue entries, Kestrel's) or from Task(&lt;T&gt;) itself through its promise-style
/// constructors (CoreLib's SemaphoreSlim.TaskNode : Task&lt;bool&gt;). A TaskCompletionSource is its task (src/runtime/async.c's
/// csrt_Task, docs/10-async.md), so a derived class is laid out over that struct: the header, the result
/// payload of T (at the fixed payload offset, where the compiler and csrt store results), then the
/// derived class's own fields. Its method table chains to csrt_Task_MT, so every test that accepts a task
/// accepts the derived object, and the object keeps the task's identity: `waiter.Task` is the waiter.
/// The base constructor records the payload size in the (zeroed) allocation, as csrt_task_new does.
/// </summary>
public sealed partial class ModuleCodeGen
{
    public static bool IsRuntimeTaskSource(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "TaskCompletionSource" or "Task", ContainingNamespace: { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } };

    internal static bool IsRuntimeTask(ITypeSymbol? type) => type is INamedTypeSymbol { Name: "Task" } && IsRuntimeTaskSource(type);

    /// <summary>Size of TResult's storage in a task's payload (0 for the non-generic TaskCompletionSource).</summary>
    internal int TaskSourcePayloadSize(INamedTypeSymbol source) =>
        source.TypeArguments.FirstOrDefault() is { } result ? (int)Types.Target.SizeOf(Types.Storage(result)) : 0;

    private ClassLayout GetTaskSourceLayout(INamedTypeSymbol type)
    {
        if (_layouts.TryGetValue(type, out var existing)) return existing;
        var structType = Llvm.NamedStruct("class." + Mangler.MetadataName(type));
        // csrt_Task: { mt, state, payload_size, conts, ncont, lock, exception, aggregate, id, flags, payload[] }.
        var payload = (TaskSourcePayloadSize(type) + 7) & ~7;
        List<LLVMTypeRef> fields = [Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32];
        if (payload > 0) fields.Add(LlvmContext.Array(Llvm.I8, (ulong)payload));
        LlvmContext.SetBody(structType, [.. fields]);
        var layout = new ClassLayout { Type = type, InstanceType = structType, Base = null, IsLibrary = true };
        layout.Elements.AddRange(fields);
        layout.InstanceSize = Types.Target.SizeOf(structType);
        AddObjectSlots(layout);
        // The table of the Task<T> the source is (ModuleCodeGen.TaskTypes.cs); plain for a source without a result.
        var source = type;
        while (source is not null && !IsRuntimeTaskSource(source)) source = source.BaseType;
        layout.MethodTable = TaskTypeOf(source) is { } sourceTask ? TaskMethodTable(sourceTask, observed: false) : ExternalGlobal("csrt_Task_MT");
        _layouts[type] = layout;
        return layout;
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>
    /// `: base(...)` of a class deriving from TaskCompletionSource(&lt;T&gt;), or from Task(&lt;T&gt;) through a promise-style constructor
    /// (state and creation options only, as CoreLib's own task subclasses use): the state and options are recorded as for
    /// `new TaskCompletionSource(state, options)`. A Task constructor that takes a body would make the object a delegate task
    /// started by the scheduler, which a runtime task created in place is not: an explicit diagnostic.
    /// </summary>
    private LLVMValueRef EmitTaskSourceBaseConstructor(IMethodSymbol constructor, IInvocationOperation inv)
    {
        var type = (INamedTypeSymbol)S(constructor.ContainingType);
        if (ModuleCodeGen.IsRuntimeTask(type)
            && constructor.Parameters.Any(p => p.Name is not ("state" or "creationOptions")))
            throw CompileError.Unsupported($"deriving from '{type}' through its constructor '{constructor}' (only the promise-style ones)", inv);
        var self = EmitValue(inv.Instance!);
        LLVMValueRef? state = null, options = null;
        foreach (var argument in inv.Arguments)
        {
            var value = EmitArgument(argument);
            if (argument.Parameter!.Name == "state") state = value;
            else if (argument.Parameter.Name == "creationOptions") options = value;
        }
        var size = _m.TaskSourcePayloadSize(type);
        // payload_size is the second int32 of the header, after the method table.
        var at = _b.InBoundsGep(L.I8, self, [L.ConstInt(L.I64, (long)T.PointerSize + 4)]);
        StoreValue(L.ConstInt(L.I32, size), at);
        if (state is not null || options is not null)
            CallCoreLib("TaskState", "Promise", self, state ?? LlvmContext.Null(L.Ptr), options ?? L.ConstInt(L.I32, 0));
        return default;
    }
}
