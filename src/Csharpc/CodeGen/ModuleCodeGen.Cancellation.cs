using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Classes deriving from System.Threading.CancellationTokenSource (Kestrel's pooled sources). The source
/// is csrt's CancellationSource (src/runtime/async.c): a derived class is laid out over that struct, with
/// its own fields after it, and a method table chained to csrt_CancellationTokenSource_MT. Its vtable is
/// the runtime's: slot 3 IDisposable.Dispose, slot 4 the protected virtual Dispose(bool) a derived class
/// overrides; CancellationTokenSource.Dispose() calls slot 4 (csrt_cts_dispose_virtual), as .NET's
/// Dispose() calls Dispose(true). The base constructors initialize the zeroed allocation in place.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private const int CancellationDisposeSlot = 3, CancellationDisposeBoolSlot = 4;

    public static bool IsRuntimeCancellationSource(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "CancellationTokenSource", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };

    private ClassLayout GetCancellationSourceLayout(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (_layouts.TryGetValue(type, out var existing)) return existing;
        var structType = Llvm.NamedStruct("class." + Mangler.MetadataName(type));
        // CancellationSource: { mt, canceled, ncallbacks, callbacks, disposed, nparents, parents, executing, executing_thread }.
        LLVMTypeRef[] fields = [Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.Ptr, Types.NativeInt];
        LlvmContext.SetBody(structType, fields);
        var layout = new ClassLayout { Type = type, InstanceType = structType, Base = null, IsLibrary = true };
        layout.Elements.AddRange(fields);
        layout.InstanceSize = Types.Target.SizeOf(structType);
        AddObjectSlots(layout);
        layout.VTable.Add(null);
        layout.VTable.Add(null);
        layout.RuntimeSlots[CancellationDisposeSlot] = "csrt_cts_dispose_virtual";
        layout.RuntimeSlots[CancellationDisposeBoolSlot] = "csrt_cts_dispose_bool";
        var disposeBool = type.GetMembers("Dispose").OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == 1);
        if (disposeBool is not null) layout.SlotOf[disposeBool.OriginalDefinition] = CancellationDisposeBoolSlot;
        layout.MethodTable = ExternalGlobal("csrt_CancellationTokenSource_MT");
        _layouts[type] = layout;
        return layout;
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>The base constructor call of a class deriving from CancellationTokenSource: the allocation is zeroed, an unset source.</summary>
    private LLVMValueRef EmitCancellationSourceBaseConstructor(IMethodSymbol constructor, IInvocationOperation inv)
    {
        var self = EmitValue(inv.Instance!);
        switch (constructor.Parameters)
        {
            case []:
                return default;
            case [{ Type.SpecialType: SpecialType.System_Int32 }]:
                CallRuntime("csrt_cts_init_after", L.Void, [L.Ptr, L.I32], self, EmitArgument(inv.Arguments[0]));
                return default;
            default:
                throw CompileError.Unsupported($"deriving from CancellationTokenSource with the base constructor '{constructor}'", inv);
        }
    }
}
