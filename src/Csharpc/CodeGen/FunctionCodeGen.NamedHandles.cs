using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// System.Threading.Mutex and Semaphore: their constructors and the static OpenExisting/TryOpenExisting. Both are the
/// runtime's one event object (src/runtime/threads.c, EVENT_MUTEX / EVENT_SEMAPHORE) like the other wait handles; the
/// argument checks, names and scopes are corelib C# (corelib/Threading/MutexHandles.cs) and the waits, releases and
/// disposal go through the generic wait-handle lowering in FunctionCodeGen.Threading.cs.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private LLVMValueRef ArgByName(ImmutableArray<IArgumentOperation> arguments, string name)
    {
        var a = arguments.First(x => x.Parameter!.Name == name);
        return Coerce(EmitValue(a.Value), TypeOf(a.Value), S(a.Parameter!.Type));
    }

    private static IArgumentOperation? OutArgument(ImmutableArray<IArgumentOperation> arguments) =>
        arguments.FirstOrDefault(x => x.Parameter!.RefKind == RefKind.Out);

    /// <summary>Stores a bool into an out argument (a bool is one byte in memory).</summary>
    private void StoreOutBool(IArgumentOperation argument, LLVMValueRef value) =>
        StoreValue(_b.Cast(LLVMOpcode.ZExt, value, L.I8), AddressOf(argument.Value));

    private LLVMValueRef EmitMutexCreation(IObjectCreationOperation creation)
    {
        var arguments = creation.Arguments;
        bool Has(string name) => arguments.Any(x => x.Parameter!.Name == name);
        var owned = Has("initiallyOwned") ? ArgByName(arguments, "initiallyOwned") : L.False;
        var name = Has("name") ? ArgByName(arguments, "name") : LlvmContext.Null(L.Ptr);
        var handle = Has("options")
            ? CallCoreLib("MutexHandles", "CreateWithOptions", owned, name, ArgByName(arguments, "options"))
            : CallCoreLib("MutexHandles", "Create", owned, name);
        if (OutArgument(arguments) is { } created) StoreOutBool(created, CallCoreLib("MutexHandles", "CreatedNew"));
        return handle;
    }

    private LLVMValueRef EmitSemaphoreCreation(IObjectCreationOperation creation)
    {
        var arguments = creation.Arguments;
        var name = arguments.Any(x => x.Parameter!.Name == "name") ? ArgByName(arguments, "name") : LlvmContext.Null(L.Ptr);
        var handle = CallCoreLib("SemaphoreHandles", "Create", ArgByName(arguments, "initialCount"), ArgByName(arguments, "maximumCount"), name);
        if (OutArgument(arguments) is { } created) StoreOutBool(created, L.True); // a name is not supported: always an unnamed semaphore
        return handle;
    }

    /// <summary>Mutex.OpenExisting / TryOpenExisting; the same members of Semaphore, which are named-only and not supported on Unix either.</summary>
    private bool TryNamedHandleStatic(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        var m = inv.TargetMethod;
        if (m.Name is not ("OpenExisting" or "TryOpenExisting")) return false;
        var arguments = inv.Arguments;
        var type = m.ContainingType.Name;
        var withOptions = arguments.Any(x => x.Parameter!.Name == "options");
        var name = ArgByName(arguments, "name");
        if (m.Name == "OpenExisting")
        {
            result = withOptions
                ? CallCoreLib(type + "Handles", "OpenExistingWithOptions", name, ArgByName(arguments, "options"))
                : CallCoreLib(type + "Handles", "OpenExisting", name);
            return true;
        }
        var handle = withOptions
            ? CallCoreLib(type + "Handles", "TryOpenWithOptions", name, ArgByName(arguments, "options"))
            : CallCoreLib(type + "Handles", "TryOpen", name);
        StoreValue(handle, AddressOf(OutArgument(arguments)!.Value));
        result = _b.ICmp(LLVMIntPredicate.NE, handle, LlvmContext.Null(L.Ptr));
        return true;
    }
}
