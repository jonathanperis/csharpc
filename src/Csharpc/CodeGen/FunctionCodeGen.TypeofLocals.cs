using System.Reflection.Metadata;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// The trimming analysis NativeAOT uses follows `typeof(X)` through local variables (`var t = typeof(C); t.GetFields()`):
/// ILLink merges the values a local is assigned. csharpc keeps member metadata on the same terms (a type is described
/// when a reflection query or an annotated parameter can reach it), so a local's `typeof` values count the same.
/// The flow is insensitive and only adds metadata (the query itself still runs on the Type object the program holds), so
/// a local assigned something else, or from a conditional, simply contributes the `typeof`s it is assigned directly.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private readonly Dictionary<ILocalSymbol, IReadOnlyList<ITypeSymbol>> _typeofLocals = new(SymbolEqualityComparer.Default);

    /// <summary>The types a `typeof(X)` expression or a local assigned `typeof(X)` can hold (empty: unknown).</summary>
    private IReadOnlyList<ITypeSymbol> KnownTypeofValues(IOperation? value)
    {
        while (value is IConversionOperation { Operand: { } inner }) value = inner;
        return value switch
        {
            ITypeOfOperation t => [t.TypeOperand],
            ILocalReferenceOperation { Local: { IsConst: false, RefKind: RefKind.None } local } => TypeofAssignedTo(local),
            // typeof(X).GetTypeInfo() is the Type object itself (`var info = typeof(X).GetTypeInfo(); info.GetDeclaredProperty(..)`).
            IInvocationOperation { TargetMethod.Name: "GetTypeInfo", Arguments: [{ Value: { } viaInfo }] } => KnownTypeofValues(viaInfo),
            _ => [],
        };
    }

    private IReadOnlyList<ITypeSymbol> TypeofAssignedTo(ILocalSymbol local)
    {
        if (_typeofLocals.TryGetValue(local, out var known)) return known;
        var types = new List<ITypeSymbol>();
        void Add(IOperation? value)
        {
            while (value is IConversionOperation { Operand: { } inner }) value = inner;
            if (value is IInvocationOperation { TargetMethod.Name: "GetTypeInfo", Arguments: [{ Value: ITypeOfOperation viaInfo }] }) value = viaInfo;
            if (value is ITypeOfOperation t && !types.Any(x => SymbolEqualityComparer.Default.Equals(x, t.TypeOperand))) types.Add(t.TypeOperand);
        }
        if ((local.Type is { Name: "Type", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
                || local.Type is { Name: "TypeInfo", ContainingNamespace: { Name: "Reflection", ContainingNamespace.Name: "System" } })
            && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declarator
            && _m.Compilation.ContainsSyntaxTree(declarator.SyntaxTree)) // not the synthesized compilations that bind `dynamic` operations
        {
            var model = _m.Compilation.GetSemanticModel(declarator.SyntaxTree);
            if (declarator.Initializer is { } initializer) Add(model.GetOperation(initializer.Value));
            // The scope: the enclosing member (top-level statements share the compilation unit).
            SyntaxNode scope = declarator.Ancestors().FirstOrDefault(n => n is MemberDeclarationSyntax and not GlobalStatementSyntax)
                ?? declarator.SyntaxTree.GetRoot();
            foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Left is IdentifierNameSyntax { Identifier.ValueText: var name } && name == local.Name
                    && model.GetOperation(assignment) is ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } simple
                    && SymbolEqualityComparer.Default.Equals(target.Local, local))
                    Add(simple.Value);
        }
        return _typeofLocals[local] = types;
    }

    private static bool IsSystemType(ITypeSymbol? type) =>
        type is { Name: "Type", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    // ---- the IL front-end: `ldtoken T; call Type.GetTypeFromHandle; stloc N` ----

    private Dictionary<int, List<ITypeSymbol>>? _ilTypeofLocals;

    /// <summary>The types an IL stack entry can hold as far as reflection is concerned: its known type, or what its local was stored.</summary>
    private IReadOnlyList<ITypeSymbol> IlKnownTypeofValues(IlEntry entry) => entry switch
    {
        { Kind: IlKind.Ref, Symbol: ITypeSymbol type } => [type],
        { Kind: IlKind.Ref, FromLocal: { } local } => IlTypeofStoredIn(local),
        _ => [],
    };

    private IReadOnlyList<ITypeSymbol> IlTypeofStoredIn(int local)
    {
        if (_ilTypeofLocals is null)
        {
            _ilTypeofLocals = [];
            var instructions = IlBody.Instructions;
            for (var i = 2; i < instructions.Length; i++)
            {
                var op = instructions[i].OpCode;
                int? stored = op is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 ? op - ILOpCode.Stloc_0
                    : op is ILOpCode.Stloc_s or ILOpCode.Stloc ? (int)instructions[i].Int : null;
                if (stored is not { } index || instructions[i - 2].OpCode != ILOpCode.Ldtoken
                    || instructions[i - 1] is not { OpCode: ILOpCode.Call, Token: var call } || IlMethod(call) is not { Name: "GetTypeFromHandle" } getter || !IsSystemType(getter.ContainingType))
                    continue;
                var type = ResolveIlType(instructions[i - 2].Token);
                if (!_ilTypeofLocals.TryGetValue(index, out var list)) _ilTypeofLocals[index] = list = [];
                if (!list.Any(x => SymbolEqualityComparer.Default.Equals(x, type))) list.Add(type);
            }
        }
        return _ilTypeofLocals.TryGetValue(local, out var known) ? known : [];
    }

    // ---- exact values: a local that holds `typeof(T)` wherever it is read ----
    // Unlike the flow-insensitive lists above (metadata: extra types are harmless), these feed lowerings that need the one
    // type an argument is (Assembly.GetCustomAttributes(t), Enum.GetName(t, v), Marshal.SizeOf(t)): a local qualifies only
    // when its single store is a typeof and nothing else can change it (`Type t = typeof(A); asm.GetCustomAttributes(t, false)`,
    // as the Mono-derived WinForms' Control.ProductName does).

    /// <summary>T when <paramref name="operation"/> is `typeof(T)`, in source or in IL, or a local that is exactly `typeof(T)`; null otherwise.</summary>
    internal ITypeSymbol? ExactTypeOf(IOperation operation)
    {
        if (TypeOfOperand(operation) is { } known) return known;
        while (operation is IConversionOperation { Operand: { } inner }) operation = inner;
        return operation is ILocalReferenceOperation { Local: { IsConst: false, RefKind: RefKind.None } local } ? ExactTypeofLocal(local) : null;
    }

    private readonly Dictionary<ILocalSymbol, ITypeSymbol?> _exactTypeofLocals = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// T when a source local is declared `= typeof(T)` and never written again: no assignment (deconstructions included),
    /// no ref/out argument, no `ref` to it, anywhere in its member (lambdas and local functions too).
    /// </summary>
    private ITypeSymbol? ExactTypeofLocal(ILocalSymbol local)
    {
        if (_exactTypeofLocals.TryGetValue(local, out var cached)) return cached;
        ITypeSymbol? result = null;
        if (IsSystemType(local.Type)
            && local.DeclaringSyntaxReferences is [var reference] && reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: { } initializer } declarator
            && _m.Compilation.ContainsSyntaxTree(declarator.SyntaxTree))
        {
            var model = _m.Compilation.GetSemanticModel(declarator.SyntaxTree);
            IOperation? value = model.GetOperation(initializer.Value);
            while (value is IConversionOperation { Operand: { } inner }) value = inner;
            SyntaxNode scope = declarator.Ancestors().FirstOrDefault(n => n is MemberDeclarationSyntax and not GlobalStatementSyntax)
                ?? declarator.SyntaxTree.GetRoot();
            if (value is ITypeOfOperation typeOf && !scope.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id =>
                    id.Identifier.ValueText == local.Name && IsWrite(id) && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id).Symbol, local)))
                result = typeOf.TypeOperand;
        }
        return _exactTypeofLocals[local] = result;

        static bool IsWrite(IdentifierNameSyntax id) =>
            id.Ancestors().OfType<AssignmentExpressionSyntax>().Any(a => a.Left.Span.Contains(id.Span))
            || id.Parent is ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword }
            || id.Parent is RefExpressionSyntax;
    }

    /// <summary>
    /// T when IL local <paramref name="local"/> is exactly `typeof(T)` wherever it is read: its one store is
    /// `ldtoken T; call Type.GetTypeFromHandle` and its address is never taken.
    /// </summary>
    private ITypeSymbol? IlExactTypeofLocal(int local)
    {
        if (IlTypeofStoredIn(local) is not [var type]) return null;
        var stores = 0;
        foreach (var instruction in IlBody.Instructions)
        {
            var op = instruction.OpCode;
            int? index = op is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 ? op - ILOpCode.Stloc_0
                : op is ILOpCode.Stloc_s or ILOpCode.Stloc or ILOpCode.Ldloca_s or ILOpCode.Ldloca ? (int)instruction.Int : null;
            if (index != local) continue;
            if (op is ILOpCode.Ldloca_s or ILOpCode.Ldloca || ++stores > 1) return null;
        }
        return type;
    }
}
