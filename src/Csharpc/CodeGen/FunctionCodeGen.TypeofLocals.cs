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

    /// <summary>The types the method's body names with typeof (ldtoken followed by Type.GetTypeFromHandle).</summary>
    private IEnumerable<ITypeSymbol> IlTypeofOperandsOfMethod()
    {
        var instructions = IlBody.Instructions;
        for (var i = 1; i < instructions.Length; i++)
            if (instructions[i] is { OpCode: ILOpCode.Call, Token: var call } && instructions[i - 1].OpCode == ILOpCode.Ldtoken
                && IlMethod(call) is { Name: "GetTypeFromHandle" } getter && IsSystemType(getter.ContainingType))
                yield return ResolveIlType(instructions[i - 1].Token);
    }

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
}
