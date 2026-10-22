using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>Anonymous types (`new { Name = "Ada", Age = 36 }`): creation, members, synthesized object methods.</summary>
public sealed partial class FunctionCodeGen
{
    private LLVMValueRef EmitAnonymousCreation(IAnonymousObjectCreationOperation creation)
    {
        var type = (INamedTypeSymbol)TypeOf(creation)!;
        var layout = _m.GetAnonymousLayout(type);
        var values = creation.Initializers.Select(init => init switch
        {
            ISimpleAssignmentOperation { Target: IPropertyReferenceOperation target } a =>
                (Property: target.Property, Value: Coerce(EmitValue(a.Value), TypeOf(a.Value), S(target.Property.Type))),
            _ => throw CompileError.Unsupported($"anonymous type initializer {init.Kind}", init),
        }).ToList();
        var obj = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
        foreach (var (property, value) in values)
            StoreValue(ToStorage(value, S(property.Type)), AnonymousFieldAddress(layout, obj, property));
        return obj;
    }

    private LLVMValueRef AnonymousFieldAddress(AnonymousLayout layout, LLVMValueRef obj, IPropertySymbol property)
    {
        var index = layout.Properties.ToList().FindIndex(p => p.Name == property.Name);
        return _b.StructGep(layout.InstanceType, obj, (uint)(index + 1), property.Name);
    }

    private LLVMValueRef AnonymousPropertyGet(IPropertyReferenceOperation p)
    {
        var layout = _m.GetAnonymousLayout((INamedTypeSymbol)S(p.Property.ContainingType));
        var obj = EmitValue(p.Instance!);
        NullCheck(obj);
        return LoadValue(AnonymousFieldAddress(layout, obj, p.Property), S(p.Property.Type));
    }

    // "{ Name = Ada, Age = 36 }" (empty: "{ }")
    internal void EmitAnonymousToString(AnonymousLayout layout)
    {
        var self = _fn.Fn.Param(0);
        var sb = CallRuntime("csrt_sb_new", L.Ptr, [L.I32], L.ConstInt(L.I32, 16));
        SbText(sb, "{ ");
        for (var i = 0; i < layout.Properties.Count; i++)
        {
            var property = layout.Properties[i];
            SbText(sb, (i > 0 ? ", " : "") + property.Name + " = ");
            var value = LoadValue(AnonymousFieldAddress(layout, self, property), property.Type);
            Sb("csrt_sb_append_string", sb, FormatValue(value, property.Type, null!), L.Ptr);
        }
        SbText(sb, layout.Properties.Count > 0 ? " }" : "}");
        _b.Ret(CallRuntime("csrt_sb_to_string", L.Ptr, [L.Ptr], sb));
        Finish();
    }

    internal void EmitAnonymousEquals(AnonymousLayout layout)
    {
        var (self, other) = (_fn.Fn.Param(0), _fn.Fn.Param(1));
        List<Func<LLVMValueRef>> conditions =
        [
            () => _b.ICmp(LLVMIntPredicate.NE, other, LlvmContext.Null(L.Ptr)),
            () => _b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, other), layout.MethodTable),
        ];
        foreach (var property in layout.Properties)
        {
            var p = property;
            conditions.Add(() => FieldEquals(LoadValue(AnonymousFieldAddress(layout, self, p), p.Type),
                LoadValue(AnonymousFieldAddress(layout, other, p), p.Type), p.Type));
        }
        _b.Ret(ShortCircuit(isAnd: true, conditions));
        Finish();
    }

    internal void EmitAnonymousGetHashCode(AnonymousLayout layout)
    {
        var self = _fn.Fn.Param(0);
        LLVMValueRef hash = L.ConstInt(L.I32, unchecked((int)0x9E3779B9));
        foreach (var p in layout.Properties)
            hash = _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Mul, hash, L.ConstInt(L.I32, -1521134295)),
                FieldHash(LoadValue(AnonymousFieldAddress(layout, self, p), p.Type), p.Type));
        _b.Ret(hash);
        Finish();
    }
}
