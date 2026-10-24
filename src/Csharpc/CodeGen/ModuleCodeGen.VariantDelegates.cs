using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Type tests against a delegate type that generic variance makes compatible with other instantiations
/// (`o is Func&lt;IServiceProvider, object&gt;` holds for a Func&lt;IServiceProvider, string&gt;). Every delegate of
/// a type shares that type's method table, so the test is "is the method table one of the delegate types
/// the program creates that convert to the target". Those types are only all known at the end of
/// lowering, so the test function is filled in rounds like the generic interface dispatchers
/// (ModuleCodeGen.GenericInterfaces.cs): `ptr test(ptr obj)` returns obj or null, like csrt_isinst_class.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private sealed class VariantDelegateTest
    {
        public required INamedTypeSymbol Target { get; init; }
        public required LLVMValueRef Entry { get; init; }
        public required LLVMValueRef Tail { get; set; }
        public int Rounds { get; set; }
        public bool Closed { get; set; }
        public HashSet<INamedTypeSymbol> Covered { get; } = new(SymbolEqualityComparer.Default);
    }

    private readonly Dictionary<INamedTypeSymbol, VariantDelegateTest> _variantDelegateTests = new(SymbolEqualityComparer.Default);
    /// <summary>Delegate types with a method table (DelegateMethodTable), in creation order.</summary>
    private readonly List<INamedTypeSymbol> _delegateTypes = [];

    private LLVMTypeRef VariantTestType => Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr);

    /// <summary>The isinst-like test for a delegate type that variance may make compatible with others.</summary>
    internal LoweredFunction VariantDelegateTestFor(INamedTypeSymbol target)
    {
        if (_variantDelegateTests.TryGetValue(target, out var existing)) return new LoweredFunction(existing.Entry, VariantTestType);
        var fn = NewDispatcherFunction("csrt.isinst." + Mangler.MetadataName(target), VariantTestType);
        _variantDelegateTests[target] = new VariantDelegateTest { Target = target, Entry = fn, Tail = fn };
        DelegateMethodTable(target); // the target's own instances match too
        return new LoweredFunction(fn, VariantTestType);
    }

    private IEnumerable<INamedTypeSymbol> DelegatesConvertibleTo(INamedTypeSymbol target) =>
        _delegateTypes.Where(d => SymbolEqualityComparer.Default.Equals(d, target)
            || Compilation.ClassifyCommonConversion(d, target) is { IsImplicit: true, IsReference: true });

    private bool EmitVariantDelegateTests(bool final)
    {
        var changed = false;
        foreach (var t in _variantDelegateTests.Values.ToList())
        {
            if (t.Closed) continue;
            var cases = DelegatesConvertibleTo(t.Target).Where(d => !t.Covered.Contains(d)).ToList();
            if (cases.Count == 0 && !final) continue;
            changed |= cases.Count > 0;
            using var b = new IrBuilder(Llvm);
            b.PositionAtEnd(b.AppendBlock(t.Tail, "entry"));
            var obj = t.Tail.Param(0);
            if (t.Rounds == 0)
            {
                var notNull = b.AppendBlock(t.Tail, "notnull");
                var isNull = b.AppendBlock(t.Tail, "null");
                b.CondBr(b.ICmp(LLVMIntPredicate.EQ, obj, LlvmContext.Null(Llvm.Ptr)), isNull, notNull);
                b.PositionAtEnd(isNull);
                b.Ret(LlvmContext.Null(Llvm.Ptr));
                b.PositionAtEnd(notNull);
            }
            var mt = b.Load(Llvm.Ptr, obj, "mt");
            foreach (var d in cases)
            {
                t.Covered.Add(d);
                var hit = b.AppendBlock(t.Tail, "hit");
                var next = b.AppendBlock(t.Tail, "next");
                b.CondBr(b.ICmp(LLVMIntPredicate.EQ, mt, DelegateMethodTable(d)), hit, next);
                b.PositionAtEnd(hit);
                b.Ret(obj);
                b.PositionAtEnd(next);
            }
            if (final)
            {
                t.Closed = true;
                b.Ret(LlvmContext.Null(Llvm.Ptr));
                continue;
            }
            var tail = NewDispatcherFunction("csrt.isinst." + Mangler.MetadataName(t.Target) + "." + ++t.Rounds, VariantTestType);
            b.Ret(b.Call(VariantTestType, tail, [obj]));
            t.Tail = tail;
        }
        return changed;
    }
}
