using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Task&lt;T&gt; instantiations as run-time types (docs/10-async.md). Every task is one csrt_Task object, so
/// `x is Task&lt;string&gt;` could not tell a Task&lt;string&gt; from a Task&lt;int&gt;. The code that creates a Task&lt;T&gt;
/// (an async method, Task.FromResult, a TaskCompletionSource&lt;T&gt;, Task.Run, ...) stores a per-T method table
/// in the object's header instead of csrt_Task_MT: the same layout and flags, a name ("System.Threading.Tasks.Task`1[
/// System.Int32]") and csrt_Task_MT as its base class, so every test that accepts a task (`is Task`, the await
/// fast paths, csrt_object_is_task) accepts it unchanged, and a test against Task&lt;T&gt; is an identity test on
/// that table, like any other class.
///
/// Pay for play: the per-T tables only exist in a program that can tell one task type from another (a type
/// test or cast against a Task&lt;T&gt;, typeof(Task&lt;T&gt;), GetType() on an object that may be a task). Creation sites
/// always store "the table of Task&lt;T&gt;", a placeholder global; a program that never looks, gets csrt_Task_MT in
/// its place at the end of code generation (<see cref="ResolveTaskMethodTables"/>), and no table is emitted.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<string, (LLVMValueRef Global, INamedTypeSymbol Type)> _taskTables = [];
    private bool _taskTypesObserved;

    /// <summary>`Task&lt;T&gt;` with a result the table distinguishes: not the non-generic task (Task&lt;VoidTaskResult&gt;).</summary>
    internal static bool IsTypedTask(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Task", TypeArguments: [var result], ContainingNamespace: { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } }
        && result is not { Name: "VoidTaskResult" } && !TypeSubstitution.IsOpen(result) && result.TypeKind != TypeKind.Error;

    /// <summary>The result type T when <paramref name="type"/> is Task&lt;T&gt; or TaskCompletionSource&lt;T&gt; (an object that is its task).</summary>
    internal INamedTypeSymbol? TaskTypeOf(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { TypeArguments: [var result] } named) return null;
        if (!IsTypedTask(named) && !IsRuntimeTaskSource(named)) return null;
        if (result is { Name: "VoidTaskResult" } || TypeSubstitution.IsOpen(result) || result.TypeKind == TypeKind.Error) return null;
        var task = Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        return task?.Construct(result);
    }

    /// <summary>
    /// The method table of Task&lt;T&gt;. A creation site asks with <paramref name="observed"/> false and stores the
    /// result in the new object's header; a type test, a cast or typeof asks with true, which makes the tables
    /// real for the whole program.
    /// </summary>
    internal LLVMValueRef TaskMethodTable(INamedTypeSymbol task, bool observed)
    {
        if (IsCanonDependent(task)) return CanonMethodTable; // shared generic code: the exact table comes from a dictionary
        var key = "type:" + IdentityKey(CoreLib.Map(TypeSubstitution.EraseTupleNames(task, Compilation)));
        if (!_taskTables.TryGetValue(key, out var entry))
        {
            var global = Module.AddGlobal("csrt.taskmt." + Mangler.MetadataName(task), TaskTableType, constant: true, linkage: LLVMLinkage.Private);
            entry = (global, task);
            _taskTables[key] = entry;
            // typeof(Task<T>) and obj.GetType() must find the same table.
            _arrayMethodTables[key] = global;
            if (_taskTypesObserved) DefineTaskMethodTable(entry.Global, task);
        }
        if (observed) ObserveTaskTypes();
        return entry.Global;
    }

    /// <summary>The program tells task types apart: every Task&lt;T&gt; table created so far (and later) is defined.</summary>
    internal void ObserveTaskTypes()
    {
        if (_taskTypesObserved) return;
        _taskTypesObserved = true;
        foreach (var (global, type) in _taskTables.Values.ToList()) DefineTaskMethodTable(global, type);
    }

    private LLVMTypeRef TaskTableType => Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32, LlvmContext.Array(Llvm.Ptr, 3));

    private void DefineTaskMethodTable(LLVMValueRef global, INamedTypeSymbol task)
    {
        // csrt_Task_MT's flags (CSRT_MT_DISPOSABLE) and System.Object's three virtuals; the interfaces a task
        // implements (IAsyncResult) are looked up through its base class (csrt.c, builtin_interface_slots).
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var slots = defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn).ToArray();
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Task_MT"), CString(Mangler.MetadataName(task)), Llvm.ConstInt(Llvm.I32, 0),
            Llvm.ConstInt(Llvm.I32, 16 /* CSRT_MT_DISPOSABLE */), LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, slots));
        Native.LLVMSetInitializer(global, init);
        Native.LLVMSetAlignment(global, 8);
        RegisterTypeMethodTable(global, task);
    }

    /// <summary>
    /// End of code generation: a program that never asked for the tables stores csrt_Task_MT where its creation
    /// sites store the placeholders.
    /// </summary>
    private void ResolveTaskMethodTables()
    {
        if (_taskTypesObserved) return;
        var plain = ExternalGlobal("csrt_Task_MT");
        foreach (var (key, (global, _)) in _taskTables)
        {
            _arrayMethodTables.Remove(key);
            Native.LLVMReplaceAllUsesWith(global, plain);
            Native.LLVMDeleteGlobal(global);
        }
        _taskTables.Clear();
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>
    /// A new task's header gets the table of its type (Task&lt;T&gt;): the object a creation site just made, which
    /// is never null. Other types (the non-generic Task, open generic code) are left as they are.
    /// </summary>
    internal LLVMValueRef StampTask(LLVMValueRef task, ITypeSymbol? type)
    {
        if (_m.TaskTypeOf(type) is { } taskType) _b.Store(_m.TaskMethodTable(taskType, observed: false), task);
        return task;
    }

    /// <summary><see cref="StampTask"/> for a new Task&lt;<paramref name="result"/>&gt;.</summary>
    internal void StampTaskResult(LLVMValueRef task, ITypeSymbol result)
    {
        if (_m.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1") is { } definition) StampTask(task, definition.Construct(result));
    }
}
