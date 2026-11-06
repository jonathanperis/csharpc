using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Generic interface methods (`ILogger.Log&lt;TState&gt;`, visitors' `Accept&lt;TResult&gt;`): interface maps
/// have no slots for them (each instantiation would need one). The program is closed, so a call goes
/// through a dispatcher per method instantiation that compares the receiver's method table with every
/// class (and boxed struct) implementing the interface and calls that type's implementation
/// instantiated with the same type arguments.
///
/// Which types exist is only known once lowering is done (generic classes are instantiated on demand,
/// and lowering an implementation can instantiate more), so dispatchers are filled in rounds: each
/// round appends the types discovered since the last one and ends with a call to the next round's
/// (still empty) continuation; the last continuation, emitted when nothing changes any more, throws.
/// Types are matched by exact method table, so the order of the rounds doesn't matter.
///
/// Generic virtual methods of classes overridden in generic classes (FrozenDictionary's
/// GetAlternateLookupDelegate&lt;T&gt; in DefaultFrozenDictionary&lt;K, V&gt;) use the same dispatchers: the cases
/// are the classes deriving from the method's class, each calling its most-derived override.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private sealed class GenericInterfaceDispatcher
    {
        public required IMethodSymbol Target { get; init; }
        public required LLVMTypeRef Type { get; init; }
        public required LLVMValueRef Entry { get; init; }
        public required LLVMValueRef Tail { get; set; }
        public int Rounds { get; set; }
        public bool Closed { get; set; }
        public HashSet<INamedTypeSymbol> Covered { get; } = new(SymbolEqualityComparer.Default);
    }

    private readonly Dictionary<IMethodSymbol, GenericInterfaceDispatcher> _genericInterfaceDispatchers = new(SymbolEqualityComparer.Default);

    /// <summary>The deepest type arguments a dispatcher case is compiled for (see EmitGenericInterfaceDispatchers).</summary>
    private const int MaxDispatcherDepth = 6;

    /// <summary>The dispatcher for a call to the generic interface method instantiation <paramref name="target"/>.</summary>
    internal LoweredFunction GenericInterfaceDispatcherFor(IMethodSymbol target)
    {
        if (_genericInterfaceDispatchers.TryGetValue(target, out var existing)) return new LoweredFunction(existing.Entry, existing.Type);
        var type = FunctionTypeOf(target);
        var fn = NewDispatcherFunction(Mangler.Method(target) + "$gim", type);
        _genericInterfaceDispatchers[target] = new GenericInterfaceDispatcher { Target = target, Type = type, Entry = fn, Tail = fn };
        return new LoweredFunction(fn, type);
    }

    private LLVMValueRef NewDispatcherFunction(string name, LLVMTypeRef type)
    {
        var fn = Module.AddFunction(name, type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "uwtable", 2); // implementations may throw (ADR 0005)
        return fn;
    }

    /// <summary>
    /// One round: every dispatcher gets cases for the implementing types that exist now and it doesn't
    /// cover yet. <paramref name="final"/> closes them instead. True when a case was added (lowering
    /// the implementations may have queued more work).
    /// </summary>
    private bool EmitGenericInterfaceDispatchers(bool final)
    {
        var changed = false;
        using var _ = Stats.Time("generic interface dispatchers/rounds");
        foreach (var d in _genericInterfaceDispatchers.Values.ToList())
            Guard(d.Target, () =>
            {
                if (d.Closed) return;
                List<(INamedTypeSymbol Type, LLVMValueRef MethodTable, IMethodSymbol Callee)> cases;
                using (Stats.Time("generic interface dispatchers/implementing types")) cases = ImplementingTypes(d.Target, d.Covered).ToList();
                if (cases.Count == 0 && !final) return;
                changed |= cases.Count > 0;
                using var b = new IrBuilder(Llvm);
                b.PositionAtEnd(b.AppendBlock(d.Tail, "entry"));
                var parameters = Enumerable.Range(0, (int)Native.LLVMCountParams(d.Tail)).Select(i => d.Tail.Param((uint)i)).ToArray();
                var self = parameters[0];
                var mt = b.Load(Llvm.Ptr, self, "mt");
                var returnsVoid = d.Target.ReturnsVoid;
                foreach (var (type, methodTable, callee) in cases)
                {
                    d.Covered.Add(type);
                    var hit = b.AppendBlock(d.Tail, "hit");
                    var next = b.AppendBlock(d.Tail, "next");
                    b.CondBr(b.ICmp(LLVMIntPredicate.EQ, mt, methodTable), hit, next);
                    b.PositionAtEnd(hit);
                    // The dispatcher instantiates every candidate type's implementation, more than the program
                    // can call: a generic virtual method whose overrides wrap its type argument (PLINQ's
                    // WrapPartitionedStream<Pair<TKey, ...>>) would expand without end. Past a depth no real
                    // program reaches, a case ends the program with a diagnostic instead of being compiled.
                    var f = callee.TypeArguments.Select(GenericDepth).DefaultIfEmpty(0).Max() > MaxDispatcherDepth
                        ? UnsupportedStub(Mangler.Method(callee) + "$toodeep", FunctionTypeOf(callee),
                            $"not supported by csharpc yet: '{callee}', a generic method instantiation nested more than {MaxDispatcherDepth} levels deep")
                        : SlotFunction(callee);
                    var args = (LLVMValueRef[])parameters.Clone();
                    // A boxed struct's implementation runs on the payload after the method-table pointer.
                    if (type.IsValueType) args[0] = b.InBoundsGep(Llvm.I8, self, [Llvm.ConstInt(Llvm.I64, 8)]);
                    var result = b.Call(FunctionTypeOf(callee), f, args);
                    if (returnsVoid) b.RetVoid(); else b.Ret(result);
                    b.PositionAtEnd(next);
                }
                if (final)
                {
                    d.Closed = true;
                    var fail = Runtime("csrt_throw_null_reference", Llvm.Void); // unreachable for receivers of the interface type
                    b.Call(fail.Type, fail.Fn, []);
                    b.Unreachable();
                    return;
                }
                var tail = NewDispatcherFunction(Mangler.Method(d.Target) + "$gim" + ++d.Rounds, d.Type);
                var rest = b.Call(d.Type, tail, parameters);
                if (returnsVoid) b.RetVoid(); else b.Ret(rest);
                d.Tail = tail;
            });
        return changed;
    }

    /// <summary>
    /// Types that only appeared while lowering the closed dispatchers' last cases would be missing from the
    /// switch: report them instead of dispatching wrongly.
    /// </summary>
    /// <summary>Classes (instantiations included) deriving from a generic virtual method's class, with the override each runs.</summary>
    private IEnumerable<(INamedTypeSymbol Type, LLVMValueRef MethodTable, IMethodSymbol Callee)> OverridingClasses(IMethodSymbol target)
    {
        var owner = target.ContainingType;
        var root = ClassLayout.RootOf(target.OriginalDefinition);
        var typeArgs = target.TypeArguments.ToArray();
        foreach (var (type, layout) in _layouts.ToList())
        {
            if (type is not { TypeKind: TypeKind.Class, IsAbstract: false } || layout.MethodTable.IsNull || TypeSubstitution.IsOpen(type) || !IsConstructed(layout)) continue;
            bool derives = false;
            for (INamedTypeSymbol? t = type; t is not null && !derives; t = t.BaseType)
                derives = SymbolEqualityComparer.Default.Equals(t, owner);
            if (!derives) continue;
            IMethodSymbol? callee = null;
            for (INamedTypeSymbol? t = type; t is not null && callee is null; t = t.BaseType)
                callee = t.GetMembers(target.Name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsGenericMethod && !m.IsAbstract
                    && m.TypeParameters.Length == typeArgs.Length && SymbolEqualityComparer.Default.Equals(ClassLayout.RootOf(m.OriginalDefinition), root));
            if (callee is not null) yield return (type, layout.MethodTable, callee.Construct(typeArgs));
        }
    }

    private void CheckGenericInterfaceDispatchers()
    {
        foreach (var d in _genericInterfaceDispatchers.Values)
            if (ImplementingTypes(d.Target, d.Covered).FirstOrDefault() is { Type: { } missed })
                Errors.Add(CompileError.Unsupported($"generic interface method '{d.Target}' implemented by '{missed}', which appeared after its dispatcher was closed", d.Target));
        foreach (var t in _variantDelegateTests.Values)
            if (DelegatesConvertibleTo(t.Target).FirstOrDefault(d => !t.Covered.Contains(d)) is { } late)
                Errors.Add(CompileError.Unsupported($"type test against '{t.Target}' with delegate type '{late}' created after the test was closed", t.Target));
    }

    /// <summary>Instantiable types implementing the target's interface that the program has method tables for, with their implementation.</summary>
    private IEnumerable<(INamedTypeSymbol Type, LLVMValueRef MethodTable, IMethodSymbol Callee)> ImplementingTypes(IMethodSymbol target, HashSet<INamedTypeSymbol> covered)
    {
        if (target.ContainingType.TypeKind != TypeKind.Interface)
        {
            foreach (var entry in OverridingClasses(target)) if (!covered.Contains(entry.Type)) yield return entry;
            yield break;
        }
        var itf = target.ContainingType;
        var member = target.ConstructedFrom;
        var typeArgs = target.TypeArguments.ToArray();
        // A generic virtual method of a class (GenericVirtualCall, when generic classes override it): the
        // derived types' most-derived override.
        IMethodSymbol? MostDerived(INamedTypeSymbol type, IMethodSymbol root)
        {
            for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
                if (t.GetMembers(root.Name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsGenericMethod && !m.IsAbstract
                        && SymbolEqualityComparer.Default.Equals(ClassLayout.RootOf(m.OriginalDefinition), root)) is { } found)
                    return found;
            return null;
        }
        IMethodSymbol? CalleeIn(INamedTypeSymbol type)
        {
            if (itf.TypeKind != TypeKind.Interface)
            {
                for (INamedTypeSymbol? b = type; b is not null; b = b.BaseType)
                    if (SymbolEqualityComparer.Default.Equals(b, itf))
                        return MostDerived(type, ClassLayout.RootOf(target.OriginalDefinition)) is { } classImpl ? classImpl.Construct(typeArgs) : null;
                return null;
            }
            if (!type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, itf))) return null;
            var impl = type.FindImplementationForInterfaceMember(member) as IMethodSymbol;
            if (impl is null) return member.IsAbstract ? null : target; // the interface's default body
            if (impl.ContainingType.TypeKind == TypeKind.Interface) return impl.IsAbstract ? null : impl.Construct(typeArgs);
            if (impl is { IsVirtual: true } or { IsOverride: true } or { IsAbstract: true })
            {
                // Implemented by a virtual method: the type's most-derived override runs.
                var root = ClassLayout.RootOf(impl.OriginalDefinition);
                impl = null;
                for (INamedTypeSymbol? t = type; t is not null && impl is null; t = t.BaseType)
                    impl = t.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.IsGenericMethod && !m.IsAbstract
                        && SymbolEqualityComparer.Default.Equals(ClassLayout.RootOf(m.OriginalDefinition), root));
                if (impl is null) return null;
            }
            return impl.Construct(typeArgs);
        }
        // Only the types that implement the interface (indexed as layouts appear), in layout order;
        // the ones the dispatcher covers already are not looked at again (every round used to resolve the
        // implementation of every class of the program for every dispatcher).
        IndexImplementers();
        if (_classesByInterface.TryGetValue(itf, out var classes))
            foreach (var type in classes.ToArray())
                if (!covered.Contains(type) && _layouts.TryGetValue(type, out var layout) && !layout.MethodTable.IsNull && IsConstructed(layout) && CalleeIn(type) is { } callee)
                    yield return (type, layout.MethodTable, callee);
        if (_structsByInterface.TryGetValue(itf, out var structs))
            foreach (var type in structs.ToArray())
                if (!covered.Contains(type) && _structs.TryGetValue(type, out var layout) && !layout.MethodTable.IsNull && CalleeIn(type) is { } callee)
                    yield return (type, layout.MethodTable, callee);
    }

    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _classesByInterface = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _structsByInterface = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _indexedImplementers = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Files the concrete classes and structs with a layout under each interface (instantiation) they implement, in the
    /// order their layouts appeared: what a dispatcher for an interface method has to consider.
    /// </summary>
    private void IndexImplementers()
    {
        using var _ = Stats.Time("generic interface dispatchers/index");
        foreach (var (type, layout) in _layouts)
        {
            if (type is not { TypeKind: TypeKind.Class, IsAbstract: false } || !_indexedImplementers.Add(type)) continue;
            foreach (var itf in type.AllInterfaces) AddImplementer(_classesByInterface, itf, type);
        }
        foreach (var (type, layout) in _structs)
        {
            if (!_indexedImplementers.Add(type)) continue;
            foreach (var itf in type.AllInterfaces) AddImplementer(_structsByInterface, itf, type);
        }
    }

    private static void AddImplementer(Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> index, INamedTypeSymbol itf, INamedTypeSymbol type)
    {
        if (!index.TryGetValue(itf, out var list)) index[itf] = list = [];
        list.Add(type);
    }
}
