using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>An anonymous type: { mt, property values... } with synthesized ToString/Equals/GetHashCode.</summary>
public sealed class AnonymousLayout
{
    public required INamedTypeSymbol Type { get; init; }
    public required LLVMTypeRef InstanceType { get; init; }
    public required IReadOnlyList<IPropertySymbol> Properties { get; init; }
    public ulong InstanceSize { get; set; }
    public LLVMValueRef MethodTable { get; set; }
}

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<INamedTypeSymbol, AnonymousLayout> _anonymous = new(SymbolEqualityComparer.Default);

    public AnonymousLayout GetAnonymousLayout(INamedTypeSymbol type)
    {
        if (_anonymous.TryGetValue(type, out var existing)) return existing;
        var properties = type.GetMembers().OfType<IPropertySymbol>().ToList();
        if (properties.Any(p => TypeSubstitution.IsOpen(p.Type)))
            throw CompileError.Unsupported("anonymous types with generic member types", type);
        var instanceType = Llvm.NamedStruct("anon." + _anonymous.Count);
        LlvmContext.SetBody(instanceType, [Llvm.Ptr, .. properties.Select(p => Types.Storage(p.Type))]);
        var layout = new AnonymousLayout { Type = type, InstanceType = instanceType, Properties = properties };
        layout.InstanceSize = Types.Target.SizeOf(instanceType);
        _anonymous[type] = layout;

        // Method table: same header as classes, vtable = [ToString, Equals, GetHashCode].
        var index = _anonymous.Count;
        LoweredFunction Make(string name, LLVMTypeRef ret, params LLVMTypeRef[] parameters)
        {
            var fnType = Llvm.FunctionType(ret, parameters);
            var fn = Module.AddFunction($"__anon{index}_{name}", fnType);
            Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
            return new LoweredFunction(fn, fnType);
        }
        var toString = Make("ToString", Llvm.Ptr, Llvm.Ptr);
        var equals = Make("Equals", Llvm.I1, Llvm.Ptr, Llvm.Ptr);
        var hash = Make("GetHashCode", Llvm.I32, Llvm.Ptr);

        var mtType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32, LlvmContext.Array(Llvm.Ptr, 3));
        var mt = Module.AddGlobal($"__anon{index}_MT", mtType, constant: true, linkage: LLVMLinkage.Internal);
        layout.MethodTable = mt;
        Native.LLVMSetInitializer(mt, Llvm.ConstStruct(
            ExternalGlobal("csrt_Object_MT"), CString("<>f__AnonymousType" + (index - 1)),
            Llvm.ConstInt(Llvm.I32, (long)layout.InstanceSize), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, toString.Fn, equals.Fn, hash.Fn)));

        new FunctionCodeGen(this, null, toString).EmitAnonymousToString(layout);
        new FunctionCodeGen(this, null, equals).EmitAnonymousEquals(layout);
        new FunctionCodeGen(this, null, hash).EmitAnonymousGetHashCode(layout);
        return layout;
    }
}
