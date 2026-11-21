using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Nested types for reflection (Type.GetNestedType(s), ADR 0018): a type whose nested types reach a
/// [DynamicallyAccessedMembers(PublicNestedTypes | NonPublicNestedTypes)] location keeps them, and each of them whole (its
/// members of every kind), as ILLink keeps the nested types it marks for NativeAOT. EventSource's manifest reads the
/// constants of an event source's nested Keywords, Tasks and Opcodes classes this way (EventSource carries the annotation
/// on the class, so every event source keeps them, ModuleCodeGen.NoteAnnotatedHierarchy). <c>csharpc.nestedtypes(Type, which)</c>
/// answers with a null-terminated array of the method tables of the kept public (which 0) or non-public (1) nested types, or
/// null for a type whose nested types were not kept (the corelib's query is then an explicit NotSupportedException, corelib/Reflection/NestedTypes.cs). Its
/// body is generated last, once every marked type is known; a program that never asks has no such function.
/// </summary>
public sealed partial class ModuleCodeGen
{
    internal const int DamPublicNestedTypes = 0x80, DamNonPublicNestedTypes = 0x100, DamNestedTypes = DamPublicNestedTypes | DamNonPublicNestedTypes;

    // What a kept nested type keeps of itself (DynamicallyAccessedMemberTypes.All without the interfaces csharpc always has).
    private const int DamWholeType = DamConstructors | DamMethods | DamProperties | DamFields | DamEvents | DamNestedTypes;

    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _nestedTypes = new(SymbolEqualityComparer.Default);
    private LoweredFunction? _nestedTypesFunction;

    /// <summary>The nested types of <paramref name="owner"/> that <paramref name="kinds"/> names are kept (and described whole).</summary>
    private void MarkNestedTypes(INamedTypeSymbol owner, int kinds)
    {
        // A generic type's nested types are generic over its parameters: there is no method table to hand out for them.
        if (owner.IsGenericType || owner.TypeKind == TypeKind.Error) return;
        if (!_nestedTypes.TryGetValue(owner, out var kept))
        {
            // (the method tables exist before the tables are finished: FinishNestedTypes only looks them up)
            try { MetadataMethodTable(owner); }
            catch (CompileError) { return; }
            _nestedTypes[owner] = kept = [];
        }
        foreach (var nested in owner.GetTypeMembers())
        {
            var wanted = nested.DeclaredAccessibility == Accessibility.Public ? DamPublicNestedTypes : DamNonPublicNestedTypes;
            if ((kinds & wanted) == 0 || nested.IsGenericType || kept.Contains(nested, SymbolEqualityComparer.Default)) continue;
            try { MetadataMethodTable(nested); }
            catch (CompileError) { continue; }
            kept.Add(nested);
            MarkDynamicallyAccessed(nested, DamWholeType);
        }
    }

    /// <summary>
    /// [DynamicallyAccessedMembers] on a class or interface applies to every class derived from it (or implementing it), as ILLink
    /// and NativeAOT treat an annotated type hierarchy: `GetType()` on any instance is then a Type whose members are known.
    /// </summary>
    private void NoteAnnotatedHierarchy(INamedTypeSymbol type)
    {
        if (IsCanonDependent(type) || TypeSubstitution.IsOpen(type)) return;
        var kinds = 0;
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
        {
            kinds |= DamKindsOf(t.OriginalDefinition.GetAttributes());
            if (CoreLib.TryMap(t) is INamedTypeSymbol mapped && !SymbolEqualityComparer.Default.Equals(mapped, t)) kinds |= DamKindsOf(mapped.OriginalDefinition.GetAttributes());
        }
        foreach (var itf in type.AllInterfaces) kinds |= DamKindsOf(itf.OriginalDefinition.GetAttributes());
        if (kinds == 0) return;
        MarkDynamicallyAccessed(type, kinds);
        // An enum parameter of a kept method: its constants are reflectable too (NativeAOT keeps an enum's fields whenever the enum is visible
        // to reflection; EventSource's manifest maps each enum parameter's values by Type.GetFields).
        if ((kinds & DamMethods) != 0)
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                foreach (var parameter in method.Parameters)
                    if (parameter.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) MarkDynamicallyAccessed(enumType, DamPublicFields);
    }

    internal LoweredFunction NestedTypesFunction()
    {
        if (_nestedTypesFunction is { } existing) return existing;
        var type = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.I32);
        var fn = Module.AddFunction("csharpc.nestedtypes", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        _nestedTypesFunction = new LoweredFunction(fn, type);
        return _nestedTypesFunction.Value;
    }

    private void FinishNestedTypes()
    {
        if (_nestedTypesFunction is not { } lowered) return;
        var fn = lowered.Fn;
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        // A Type object is { Type_MT, described method table } (runtime/type.c): the second pointer.
        var target = b.Load(Llvm.Ptr, b.InBoundsGep(Llvm.Ptr, fn.Param(0), [Llvm.ConstInt(Llvm.I64, 1)]));
        foreach (var (owner, kept) in _nestedTypes)
        {
            LLVMValueRef ownerTable;
            try { ownerTable = MetadataMethodTable(owner); }
            catch (CompileError) { continue; }
            LLVMValueRef List(bool isPublic)
            {
                LLVMValueRef[] entries = [.. kept.Where(n => n.DeclaredAccessibility == Accessibility.Public == isPublic).Select(n => MetadataMethodTable(n)), LlvmContext.Null(Llvm.Ptr)];
                var init = LlvmContext.ConstArray(Llvm.Ptr, entries);
                return Module.AddGlobal("csharpc.nestedtypes.list", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
            }
            var (publicList, nonPublicList) = (List(true), List(false));
            var hit = b.AppendBlock(fn, "nested.hit");
            var next = b.AppendBlock(fn, "nested.next");
            b.CondBr(b.ICmp(LLVMIntPredicate.EQ, target, ownerTable), hit, next);
            b.PositionAtEnd(hit);
            b.Ret(b.Select(b.ICmp(LLVMIntPredicate.EQ, fn.Param(1), Llvm.ConstInt(Llvm.I32, 0)), publicList, nonPublicList));
            b.PositionAtEnd(next);
        }
        b.Ret(LlvmContext.Null(Llvm.Ptr));
    }
}
