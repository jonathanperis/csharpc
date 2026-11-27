using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Devirtualization from an allocation's exact type (#370): a virtual call whose receiver is known to be an object of
/// one exact class (`var r = new Random(1); r.Next(10)`) is a direct call to that class's implementation, which LLVM can
/// then inline, as NativeAOT does. Class hierarchy analysis (ModuleCodeGen.Devirtualize) can't see this once any reached
/// subclass overrides the method (Random.Shared's ThreadSafeRandom overrides Next).
///
/// A wrong exact type would be a silent miscompile, so the proof is strict and flow-insensitive: a local qualifies only
/// when every definition of it in the function's graph is an object creation of the same class (through reference
/// conversions, flow captures and copies of other such locals; a null may be among them, since the call's null check
/// stays), and nothing can write it some other way: it is not captured by a lambda or local function, not passed or
/// taken by reference (ref/out/in arguments, Interlocked.Exchange, Unsafe.AsRef, address-of, ref locals), not a
/// deconstruction, compound or coalescing assignment target, and not declared by a pattern, a catch clause or
/// foreach (those assign it without an assignment operation). The IL front-end's version follows: an IL local whose
/// every store directly follows a `newobj` of the same class, and whose address is never taken.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private sealed class ExactLocals
    {
        public HashSet<ILocalSymbol> Disqualified { get; } = new(SymbolEqualityComparer.Default);
        public Dictionary<ILocalSymbol, List<IOperation>> Definitions { get; } = new(SymbolEqualityComparer.Default);
        public Dictionary<CaptureId, List<IOperation>> Captures { get; } = [];
        public Dictionary<ILocalSymbol, INamedTypeSymbol?> Known { get; } = new(SymbolEqualityComparer.Default);
    }

    private ControlFlowGraph? _exactLocalsGraph;
    private ExactLocals? _exactLocals;

    /// <summary>The direct implementation a virtual call to <paramref name="target"/> reaches when its receiver's exact class is known; null otherwise.</summary>
    private IMethodSymbol? ExactReceiverImplementation(IMethodSymbol target, IOperation? receiver)
    {
        if (receiver is null || target.IsGenericMethod) return null;
        if (ExactClassOf(receiver, []) is not { } exact || exact.IsGenericType || exact.IsAbstract) return null;
        if (TypeSubstitution.IsGenericContext(exact) || target.ContainingType is not INamedTypeSymbol owner || !ModuleCodeGen.DerivesFromOrEquals(exact, owner)) return null;
        var implementation = ModuleCodeGen.ResolveSlot(exact, ClassLayout.RootOf(target.OriginalDefinition));
        return implementation is { IsAbstract: false } && _m.IsUserMethod(implementation) ? implementation : null;
    }

    /// <summary>The class an operand's object is known to be exactly, null if unknown. <paramref name="visiting"/> breaks cycles of copies.</summary>
    private INamedTypeSymbol? ExactClassOf(IOperation operation, HashSet<ILocalSymbol> visiting)
    {
        while (operation is IConversionOperation { OperatorMethod: null, Conversion: { IsIdentity: true } or { IsReference: true } } conversion)
            operation = conversion.Operand;
        switch (operation)
        {
            case IObjectCreationOperation creation when TypeOf(creation) is INamedTypeSymbol { TypeKind: TypeKind.Class } created:
                return created;
            case IlValueOperation { ExactClass: { } fromIl }:
                return S(fromIl) as INamedTypeSymbol;
            case ILocalReferenceOperation { Local: var local } when _cfg is not null:
                return ExactClassOfLocal(local, visiting);
            case IFlowCaptureReferenceOperation reference when _cfg is not null && !_cfg.LvalueCaptures.Contains(reference.Id):
                return ExactClassOfAll(Analysis().Captures.GetValueOrDefault(reference.Id), visiting);
            default:
                return null;
        }
    }

    /// <summary>The one class every value is exactly (nulls aside); null when any value is unknown, the classes differ, or there is none.</summary>
    private INamedTypeSymbol? ExactClassOfAll(List<IOperation>? values, HashSet<ILocalSymbol> visiting)
    {
        if (values is null) return null;
        INamedTypeSymbol? result = null;
        foreach (var value in values)
        {
            if (IsNullValue(value)) continue;
            if (ExactClassOf(value, visiting) is not { } exact) return null;
            if (result is null) result = exact;
            else if (!SymbolEqualityComparer.Default.Equals(result, exact)) return null;
        }
        return result;
    }

    private static bool IsNullValue(IOperation value)
    {
        while (value is IConversionOperation { OperatorMethod: null } conversion) value = conversion.Operand;
        return value is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } or IDefaultValueOperation;
    }

    private INamedTypeSymbol? ExactClassOfLocal(ILocalSymbol local, HashSet<ILocalSymbol> visiting)
    {
        var analysis = Analysis();
        if (analysis.Known.TryGetValue(local, out var known)) return known;
        if (analysis.Disqualified.Contains(local) || local.IsRef || !visiting.Add(local)) return null;
        var result = ExactClassOfAll(analysis.Definitions.GetValueOrDefault(local), visiting);
        visiting.Remove(local);
        analysis.Known[local] = result;
        return result;
    }

    /// <summary>The definitions of the current graph's own locals, and the locals something else may write.</summary>
    private ExactLocals Analysis()
    {
        var graph = _cfg!.Graph;
        if (_exactLocals is not null && ReferenceEquals(_exactLocalsGraph, graph)) return _exactLocals;
        var analysis = new ExactLocals();
        var own = new HashSet<ILocalSymbol>(ClosureAnalysis.Regions(graph.Root).SelectMany(r => r.Locals), SymbolEqualityComparer.Default);
        // A local held in a closure environment (captured by a lambda or local function, or an outer function's local seen
        // from a nested one) can be written by code outside this graph.
        foreach (var local in own)
            if (_cfg.Closure.Analysis.ScopeOfVar(local) is not null) analysis.Disqualified.Add(local);

        void Disqualify(IOperation? target)
        {
            if (target is ILocalReferenceOperation { Local: var local }) analysis.Disqualified.Add(local);
            else if (target is not null)
                foreach (var reference in target.DescendantsAndSelf().OfType<ILocalReferenceOperation>()) analysis.Disqualified.Add(reference.Local);
        }

        foreach (var block in graph.Blocks)
        {
            IEnumerable<IOperation> roots = block.BranchValue is { } branch ? [.. block.Operations, branch] : block.Operations;
            foreach (var operation in roots.SelectMany(r => r.DescendantsAndSelf()))
            {
                switch (operation)
                {
                    case ISimpleAssignmentOperation { IsRef: true } refAssignment:
                        Disqualify(refAssignment.Target);
                        Disqualify(refAssignment.Value); // `ref var r = ref x;`: x is written through r
                        break;
                    case ISimpleAssignmentOperation { Target: ILocalReferenceOperation { Local: var assigned } } assignment:
                        if (!analysis.Definitions.TryGetValue(assigned, out var definitions)) analysis.Definitions[assigned] = definitions = [];
                        definitions.Add(assignment.Value);
                        break;
                    case IVariableDeclaratorOperation { Symbol: var declared } declarator:
                        if (declared.IsRef) { analysis.Disqualified.Add(declared); Disqualify(declarator.Initializer?.Value); }
                        else if (declarator.Initializer?.Value is { } initial)
                        {
                            if (!analysis.Definitions.TryGetValue(declared, out var initialDefinitions)) analysis.Definitions[declared] = initialDefinitions = [];
                            initialDefinitions.Add(initial);
                        }
                        break;
                    case ICompoundAssignmentOperation compound: Disqualify(compound.Target); break;
                    case ICoalesceAssignmentOperation coalesce: Disqualify(coalesce.Target); break;
                    case IIncrementOrDecrementOperation increment: Disqualify(increment.Target); break;
                    case IDeconstructionAssignmentOperation deconstruction: Disqualify(deconstruction.Target); break;
                    // By reference: ref, out and in arguments (Interlocked.Exchange(ref x, ..), Unsafe.AsRef(in x)), address-of, ref returns.
                    case IArgumentOperation { Parameter.RefKind: not RefKind.None } byRef: Disqualify(byRef.Value); break;
                    case IAddressOfOperation addressOf: Disqualify(addressOf.Reference); break;
                    case IReturnOperation { ReturnedValue: { } returned } when _method is { ReturnsByRef: true } or { ReturnsByRefReadonly: true }: Disqualify(returned); break;
                    // `out var x`, `var (a, b) = ..`. (A plain `var x = new C()` is an assignment whose target is a declaring reference: a definition.)
                    case IDeclarationExpressionOperation declaration: Disqualify(declaration.Expression); break;
                    // Declared and assigned without an assignment operation.
                    case IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol pattern }: analysis.Disqualified.Add(pattern); break;
                    case IRecursivePatternOperation { DeclaredSymbol: ILocalSymbol recursive }: analysis.Disqualified.Add(recursive); break;
                    case IListPatternOperation { DeclaredSymbol: ILocalSymbol list }: analysis.Disqualified.Add(list); break;
                    case IFlowCaptureOperation capture:
                        if (!analysis.Captures.TryGetValue(capture.Id, out var values)) analysis.Captures[capture.Id] = values = [];
                        values.Add(capture.Value);
                        // A capture of the local itself used as an assignment target (`x = c ? a : b` can capture x first).
                        if (_cfg.LvalueCaptures.Contains(capture.Id)) Disqualify(capture.Value);
                        break;
                    // Anything Roslyn can't describe (`__makeref(x)`) may take the local's address.
                    case { Kind: OperationKind.None or OperationKind.Invalid } unknown when unknown is not IlOperation: Disqualify(unknown); break;
                }
            }
        }
        // A local of an enclosing function assigned here (it lives in a closure environment) is written by more than this graph.
        // Catch and foreach variables need nothing special: the graph assigns them the caught exception and Current, not creations.
        foreach (var local in analysis.Definitions.Keys.Where(l => !own.Contains(l)).ToList()) analysis.Disqualified.Add(local);
        _exactLocalsGraph = graph;
        return _exactLocals = analysis;
    }

    // ---- the IL front-end ----

    private Dictionary<int, ITypeSymbol>? _ilExactLocals;

    /// <summary>
    /// The class an IL local always holds exactly: every store to it directly follows a `newobj` of that class (and is not a
    /// branch target or a handler's start, where another value could arrive), and its address is never taken.
    /// </summary>
    private ITypeSymbol? IlExactLocal(int index)
    {
        if (_ilExactLocals is null)
        {
            var instructions = IlBody.Instructions;
            var targets = new HashSet<int>();
            foreach (var instruction in instructions)
                if (instruction.Targets is { } branchTargets) targets.UnionWith(branchTargets);
            foreach (var region in IlBody.Regions)
            {
                targets.Add(region.TryOffset);
                targets.Add(region.HandlerOffset);
                if (region.FilterOffset >= 0) targets.Add(region.FilterOffset);
            }
            var stored = new Dictionary<int, ITypeSymbol?>();
            for (var i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                var op = instruction.OpCode;
                int? address = op is ILOpCode.Ldloca or ILOpCode.Ldloca_s ? (int)instruction.Int : null;
                if (address is { } taken) { stored[taken] = null; continue; }
                int? local = op is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 ? op - ILOpCode.Stloc_0
                    : op is ILOpCode.Stloc_s or ILOpCode.Stloc ? (int)instruction.Int : null;
                if (local is not { } index2) continue;
                ITypeSymbol? created = null;
                // (A multi-dimensional array's `newobj T[,]::.ctor` is a runtime-provided accessor, not a method IlMethod resolves.)
                if (i > 0 && instructions[i - 1] is { OpCode: ILOpCode.Newobj, Token: var token } && !targets.Contains(instruction.Offset)
                    && !IlAssembly.TryArrayAccessor(token, IlBody.Context, out _, out _, out _)
                    && IlMethod(token) is { MethodKind: MethodKind.Constructor, ContainingType: { TypeKind: TypeKind.Class } type })
                    created = type;
                stored[index2] = !stored.TryGetValue(index2, out var previous) ? created
                    : previous is not null && created is not null && SymbolEqualityComparer.Default.Equals(previous, created) ? previous : null;
            }
            _ilExactLocals = [];
            foreach (var (index2, type) in stored)
                if (type is not null && !IlLocal(index2).IsByRef) _ilExactLocals[index2] = type;
        }
        return _ilExactLocals.GetValueOrDefault(index);
    }
}
