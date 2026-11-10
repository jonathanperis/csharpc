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
        // Where the next round continues in the candidate lists (ModuleCodeGen.NextCases), and the candidates passed over
        // because their method table was not constructed yet.
        public int ClassCursor, StructCursor;
        public List<int> ClassPending { get; } = [];
        public List<int> StructPending { get; } = [];
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
        using var rounds = Stats.Time("generic interface dispatchers/rounds");
        foreach (var d in _genericInterfaceDispatchers.Values.ToList())
            Guard(d.Target, () =>
            {
                if (d.Closed) return;
                List<(INamedTypeSymbol Type, LLVMValueRef MethodTable, IMethodSymbol Callee)> cases;
                using (Stats.Time("generic interface dispatchers/implementing types")) cases = NextCases(d);
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
    /// <summary>
    /// The implementation a generic virtual method (or generic interface method) <paramref name="target"/> runs for
    /// <paramref name="type"/>, or null when the type has none or is not one the dispatcher handles. A fact of the symbols
    /// alone: it never changes once the type exists.
    /// </summary>
    private IMethodSymbol? DispatchCallee(IMethodSymbol target, INamedTypeSymbol type)
    {
        // The candidates' types are in their runtime identity (tuple element names erased, as the layouts are keyed); a call
        // through `Iterator<(string Name, int Price)>` must still find `OrderedIterator<(string, int)>` derived from it.
        var itf = TypeSubstitution.EraseTupleNames(target.ContainingType, Compilation);
        var typeArgs = target.TypeArguments.ToArray();
        if (itf.TypeKind != TypeKind.Interface)
        {
            // A generic virtual method of a class overridden in generic classes: the classes deriving from the
            // method's class, each calling its most-derived override.
            if (TypeSubstitution.IsOpen(type)) return null;
            bool derives = false;
            for (INamedTypeSymbol? t = type; t is not null && !derives; t = t.BaseType)
                derives = SymbolEqualityComparer.Default.Equals(t, itf);
            if (!derives) return null;
            var root = ClassLayout.RootOf(target.OriginalDefinition);
            IMethodSymbol? callee = null;
            for (INamedTypeSymbol? t = type; t is not null && callee is null; t = t.BaseType)
                callee = t.GetMembers(target.Name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsGenericMethod && !m.IsAbstract
                    && m.TypeParameters.Length == typeArgs.Length && SymbolEqualityComparer.Default.Equals(ClassLayout.RootOf(m.OriginalDefinition), root));
            return callee?.Construct(typeArgs);
        }
        var member = target.ConstructedFrom;
        if (!ReferenceEquals(itf, target.ContainingType))
            member = itf.GetMembers(target.Name).OfType<IMethodSymbol>().First(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, target.OriginalDefinition));
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

    /// <summary>The candidate classes and structs of a dispatcher, in layout order: the types implementing its interface, or every concrete class.</summary>
    private (List<INamedTypeSymbol> Classes, List<INamedTypeSymbol> Structs) CandidatesOf(IMethodSymbol target)
    {
        IndexImplementers();
        if (target.ContainingType.TypeKind != TypeKind.Interface) return (_classSequence, []);
        var itf = TypeSubstitution.EraseTupleNames(target.ContainingType, Compilation);
        return (_classesByInterface.GetValueOrDefault(itf) ?? [], _structsByInterface.GetValueOrDefault(itf) ?? []);
    }

    /// <summary>Is the method table of this candidate there to dispatch on (a class: and constructed)?</summary>
    private bool IsDispatchable(INamedTypeSymbol type, bool isStruct, out LLVMValueRef methodTable)
    {
        methodTable = default;
        if (isStruct) { if (!_structs.TryGetValue(type, out var sl) || sl.MethodTable.IsNull) return false; methodTable = sl.MethodTable; return true; }
        if (!_layouts.TryGetValue(type, out var layout) || layout.MethodTable.IsNull || !IsConstructed(layout)) return false;
        methodTable = layout.MethodTable;
        return true;
    }

    /// <summary>
    /// The cases a dispatcher does not have yet, in layout order (classes, then structs). A dispatcher continues where
    /// its last round stopped: candidates seen before are not examined again, except the ones that had no constructed
    /// method table then. (Every round used to resolve the implementation of every candidate of every dispatcher again.)
    /// </summary>
    private List<(INamedTypeSymbol Type, LLVMValueRef MethodTable, IMethodSymbol Callee)> NextCases(GenericInterfaceDispatcher d)
    {
        var (classes, structs) = CandidatesOf(d.Target);
        List<(INamedTypeSymbol Type, LLVMValueRef MethodTable, IMethodSymbol Callee)> found = [];
        void Scan(List<INamedTypeSymbol> list, ref int cursor, List<int> pending, bool isStruct)
        {
            var positions = pending.ToList();
            pending.Clear();
            for (; cursor < list.Count; cursor++) positions.Add(cursor);
            foreach (var i in positions)
            {
                var type = list[i];
                if (d.Covered.Contains(type)) continue;
                if (!IsDispatchable(type, isStruct, out var methodTable)) pending.Add(i);
                else if (DispatchCallee(d.Target, type) is { } callee) found.Add((type, methodTable, callee));
            }
        }
        Scan(classes, ref d.ClassCursor, d.ClassPending, isStruct: false);
        Scan(structs, ref d.StructCursor, d.StructPending, isStruct: true);
        return found;
    }

    private void CheckGenericInterfaceDispatchers()
    {
        foreach (var d in _genericInterfaceDispatchers.Values)
            if (UncoveredCase(d) is { Type: { } missed })
                Errors.Add(CompileError.Unsupported($"generic interface method '{d.Target}' implemented by '{missed}', which appeared after its dispatcher was closed", d.Target));
        foreach (var t in _variantDelegateTests.Values)
            if (DelegatesConvertibleTo(t.Target).FirstOrDefault(d => !t.Covered.Contains(d)) is { } late)
                Errors.Add(CompileError.Unsupported($"type test against '{t.Target}' with delegate type '{late}' created after the test was closed", t.Target));
    }

    /// <summary>The first case a closed dispatcher lacks (a full pass over the candidates, once per dispatcher).</summary>
    private (INamedTypeSymbol Type, LLVMValueRef MethodTable, IMethodSymbol Callee)? UncoveredCase(GenericInterfaceDispatcher d)
    {
        var (classes, structs) = CandidatesOf(d.Target);
        foreach (var type in classes.ToArray())
            if (!d.Covered.Contains(type) && IsDispatchable(type, false, out var methodTable) && DispatchCallee(d.Target, type) is { } callee) return (type, methodTable, callee);
        foreach (var type in structs.ToArray())
            if (!d.Covered.Contains(type) && IsDispatchable(type, true, out var methodTable) && DispatchCallee(d.Target, type) is { } callee) return (type, methodTable, callee);
        return null;
    }

    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _classesByInterface = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _structsByInterface = new(SymbolEqualityComparer.Default);
    private readonly List<INamedTypeSymbol> _classSequence = [];
    private readonly HashSet<INamedTypeSymbol> _indexedImplementers = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Files the concrete classes and structs with a layout, in the order their layouts appeared, in one list and under
    /// each interface (instantiation) they implement: what a dispatcher has to consider.
    /// </summary>
    private void IndexImplementers()
    {
        using var timer = Stats.Time("generic interface dispatchers/index");
        foreach (var (type, _) in _layouts)
        {
            if (type is not { TypeKind: TypeKind.Class, IsAbstract: false } || !_indexedImplementers.Add(type)) continue;
            _classSequence.Add(type);
            foreach (var itf in type.AllInterfaces) AddImplementer(_classesByInterface, itf, type);
        }
        foreach (var (type, _) in _structs)
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
