using System.Reflection;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Standalone reflection records for members a program names directly rather than through a type's
/// member lists: what `ldtoken` + MethodBase.GetMethodFromHandle / FieldInfo.GetFieldFromHandle reach
/// in IL, and the MethodInfo/FieldInfo/ConstructorInfo constants of expression trees (both front-ends).
/// NativeAOT keeps the metadata of exactly these members too. A record is a csrt_MethodRecord (shared
/// with ModuleCodeGen.Reflection.cs) or a csrt_FieldRecord; its identity makes every record of one
/// member the same MemberInfo object.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<IMethodSymbol, (LLVMValueRef Global, MethodRecordData Record)> _standaloneMethods = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, LLVMValueRef> _standaloneFields = new(SymbolEqualityComparer.Default);
    private readonly List<(LLVMValueRef Global, Func<LLVMValueRef> Initializer)> _fieldRecordInitializers = [];

    /// <summary>The csrt_MethodRecord of a method or constructor (invokable when csharpc can call it).</summary>
    internal LLVMValueRef MethodRecordGlobal(IMethodSymbol method)
    {
        if (_standaloneMethods.TryGetValue(method, out var existing)) return existing.Global;
        var record = MethodRecord(method, invoker: true);
        var global = Module.AddGlobal("csharpc.reflection.member", MethodRecordType, constant: true, linkage: LLVMLinkage.Private);
        _standaloneMethods[method] = (global, record);
        // A property accessor named this way (Expression.Property(e, getter)) is looked up among its type's
        // properties (Expression.GetProperty): keep that property, as NativeAOT does for ldtoken'd accessors.
        if (method.AssociatedSymbol is IPropertySymbol property && !TypeSubstitution.IsOpen(method.ContainingType))
            MarkDynamicallyAccessed(method.ContainingType, DamProperties, property.MetadataName);
        return global;
    }

    /// <summary>The csrt_FieldRecord of a field (ModuleCodeGen.Reflection.cs builds it as for a type's field list).</summary>
    internal LLVMValueRef FieldRecordGlobal(IFieldSymbol field)
    {
        if (_standaloneFields.TryGetValue(field, out var existing)) return existing;
        var record = FieldRecord(field);
        var global = Module.AddGlobal("csharpc.reflection.field", FieldRecordType, constant: true, linkage: LLVMLinkage.Private);
        _standaloneFields[field] = global;
        _fieldRecordInitializers.Add((global, () => FieldRecordConstant(record)));
        return global;
    }

    private LLVMValueRef FieldRecordConstant(FieldRecordData f) =>
        Llvm.ConstStruct(f.Name, f.Declaring, f.Type, f.Get, f.Set, AttributeArray(f.Attributes), Llvm.ConstInt(Llvm.I32, f.Attributes.Count),
            Llvm.ConstUInt(Llvm.I32, f.FieldAttributes));

    private int _strongBoxesScanned;

    /// <summary>
    /// StrongBox&lt;T&gt;.Value of every StrongBox the program has: System.Linq.Expressions reads it by name
    /// (Utils.GetStrongBoxValueField: Expression.Field(box, "Value"), kept by [DynamicDependency] as NativeAOT does).
    /// </summary>
    private bool DescribeStrongBoxFields()
    {
        if (_reflectionTable.IsNull || !UsesLinqExpressions) return false;
        var any = false;
        while (_strongBoxesScanned < _typeMts.Count)
        {
            // The registered type may be the corelib's implementation (StrongBoxImpl<T>).
            if (_typeMts[_strongBoxesScanned++].Type is not INamedTypeSymbol { Arity: 1 } box || TypeSubstitution.IsOpen(box)
                || RealType(box) is not INamedTypeSymbol { Name: "StrongBox", ContainingNamespace: { Name: "CompilerServices", ContainingNamespace: { Name: "Runtime", ContainingNamespace.Name: "System" } } })
                continue;
            MarkDynamicallyAccessed(box, DamPublicFields, "Value");
            any = true;
        }
        return any;
    }

    private void FinishMemberRecords()
    {
        foreach (var (global, record) in _standaloneMethods.Values)
            Native.LLVMSetInitializer(global, MethodRecordConstant(record));
        foreach (var (global, initializer) in _fieldRecordInitializers)
            Native.LLVMSetInitializer(global, initializer());
    }
}
