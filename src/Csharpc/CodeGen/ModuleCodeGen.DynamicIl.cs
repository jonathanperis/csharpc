using Csharpc.Frontend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Binding the dynamic call sites of code compiled from IL (docs/adr/0019-dynamic.md, "From IL";
/// FunctionCodeGen.IlDynamic.cs decodes them). A source site binds at its own position, whose accessibility is
/// its context type's; an IL site's context (csc's `typeof(Context)`) is a metadata type, which no source position
/// is inside. So IL sites bind in a synthesized method (IlDynamicBinding) through a semantic model that ignores
/// accessibility, and what accessibility decides is checked against the real context:
///
/// - lookups that feed planning and errors (simple names, IsEvent, accessors) filter by the context's access;
/// - a binding that succeeds must use only members the context can access: then it is the binder's (an accessible
///   best member stays best when inaccessible candidates leave the set);
/// - a binding error is the binder's only when every candidate member is accessible to the context (else removing
///   inaccessible candidates could turn an ambiguity into a call).
///
/// Anything else is a NotSupportedException where the case runs, never an answer the binder would not give.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private (SemanticModel Model, int Position)? _ilDynamicBinding;

    /// <summary>The model and position IL call sites bind at.</summary>
    internal (SemanticModel Model, int Position) IlDynamicBindingContext()
    {
        if (_ilDynamicBinding is { } known) return known;
        var tree = Compilation.SyntaxTrees.FirstOrDefault(t => t.FilePath == IlDynamicBinding.TreePath)
            ?? throw new Frontend.Il.IlException("'dynamic' in compiled code: the binding compilation has no context for IL call sites");
        var binding = (Compilation.GetSemanticModel(tree, ignoreAccessibility: true), IlDynamicBinding.Position(tree));
        _ilDynamicBinding = binding;
        return binding;
    }

    /// <summary>The assembly whose view names run-time types when no context is given: the program's (its IL when it is IL).</summary>
    private ISymbol DynamicDefaultWithin => (ISymbol?)MainIl?.Symbol ?? Compilation.Assembly;

    /// <summary>
    /// Whose view a site's run-time type names take in binder messages (null: the program's): a call site of an IL
    /// library names the program's internal types as their nearest base the library can access.
    /// </summary>
    internal IAssemblySymbol? DynamicNameView(DynamicSite site) =>
        site.FromIl && site.Context?.ContainingAssembly is { } assembly && !SymbolEqualityComparer.Default.Equals(assembly, DynamicDefaultWithin) ? assembly : null;

    /// <summary>The type of a site's implicit `this` (a simple-name call).</summary>
    private static ITypeSymbol? DynamicThisType(DynamicSite site) =>
        site.Operands.FirstOrDefault(o => o.Kind == DynamicOperandKind.This)?.Type ?? site.Context;

    /// <summary>The members a simple name can mean at the site.</summary>
    private IEnumerable<ISymbol> DynamicSimpleNameLookup(DynamicSite site) =>
        !site.FromIl ? site.Model.LookupSymbols(site.Position, name: site.Name)
        : DynamicThisType(site) is { } self ? MembersNamed(self, site.Name!).Where(m => DynamicAccessible(site, m, null)) : [];

    /// <summary>Member lookup in <paramref name="container"/> as the site's context sees it.</summary>
    private IEnumerable<ISymbol> DynamicLookup(DynamicSite site, INamespaceOrTypeSymbol container, string? name) =>
        !site.FromIl ? site.Model.LookupSymbols(site.Position, container, name)
        : site.Model.LookupSymbols(site.Position, container, name).Where(m => DynamicAccessible(site, m, container as ITypeSymbol));

    /// <summary>Can the site's context access <paramref name="symbol"/> (through an expression of <paramref name="through"/>)?</summary>
    private bool DynamicAccessible(DynamicSite site, ISymbol symbol, ITypeSymbol? through)
    {
        if (!site.FromIl) return site.Model.IsAccessible(site.Position, symbol);
        if (CoreLib.IsCoreLib(symbol)) return true; // csharpc's own helpers in bound code
        // Protected access goes through the type of the receiver, which must be the context's (or derived).
        through = through is INamedTypeSymbol or IArrayTypeSymbol ? through : null;
        return site.Context is { } context
            ? Compilation.IsSymbolAccessibleWithin(symbol, context, through)
            : Compilation.IsSymbolAccessibleWithin(symbol, DynamicDefaultWithin, through);
    }

    /// <summary>The first member a binding of an IL site uses that its context can't access (null: none).</summary>
    private ISymbol? IlDynamicInaccessibleMember(DynamicSite site, IOperation? root)
    {
        if (root is null) return null;
        foreach (var op in root.DescendantsAndSelf())
        {
            foreach (var (member, through) in UsedMembers(op))
                if (member is not null && !DynamicAccessible(site, member, through)) return member;
        }
        return null;
    }

    private static IEnumerable<(ISymbol? Member, ITypeSymbol? Through)> UsedMembers(IOperation op)
    {
        switch (op)
        {
            case IInvocationOperation i: yield return (i.TargetMethod, i.Instance?.Type); break;
            case IFieldReferenceOperation f: yield return (f.Field, f.Instance?.Type); break;
            case IEventReferenceOperation e: yield return (e.Event, e.Instance?.Type); break;
            case IEventAssignmentOperation { EventReference: IEventReferenceOperation e } a:
                yield return (a.Adds ? e.Event.AddMethod : e.Event.RemoveMethod, e.Instance?.Type);
                break;
            case IMethodReferenceOperation m: yield return (m.Method, m.Instance?.Type); break;
            case IObjectCreationOperation c: yield return (c.Constructor, null); break;
            case IBinaryOperation b: yield return (b.OperatorMethod, null); break;
            case IUnaryOperation u: yield return (u.OperatorMethod, null); break;
            case IConversionOperation c: yield return (c.OperatorMethod, null); break;
            case ICompoundAssignmentOperation c: yield return (c.OperatorMethod, null); break;
            case IIncrementOrDecrementOperation c: yield return (c.OperatorMethod, null); break;
            case IPropertyReferenceOperation p:
            {
                var through = p.Instance?.Type;
                yield return (p.Property, through);
                // The accessors the use calls: a get, a set (assignment target), or both (compound, ++).
                var writes = p.Parent switch
                {
                    ISimpleAssignmentOperation a when ReferenceEquals(a.Target, p) => 1,
                    ICompoundAssignmentOperation a when ReferenceEquals(a.Target, p) => 2,
                    ICoalesceAssignmentOperation a when ReferenceEquals(a.Target, p) => 2,
                    IIncrementOrDecrementOperation => 2,
                    _ => 0,
                };
                if (writes != 1) yield return (p.Property.GetMethod, through);
                if (writes != 0) yield return (p.Property.SetMethod, through);
                break;
            }
        }
    }

    /// <summary>
    /// The members whose accessibility decides a site's lookup and overload resolution: an error is the binder's
    /// only if the context can access all of them (null: one it can't).
    /// </summary>
    private bool IlDynamicCandidatesAccessible(DynamicSite site, DynamicClass?[] chosen)
    {
        var receiver = site.StaticReceiver ?? ReceiverType(site, chosen) ?? (site.Operands.Count > 0 && site.Operands[0].Role is DynamicRole.IndexReceiver
            ? BindingType(site.Operands[0], chosen[0]) : null);
        IEnumerable<ISymbol> candidates = site.Kind switch
        {
            DynamicKind.InvokeMember or DynamicKind.GetMember or DynamicKind.SetMember or DynamicKind.IsEvent when receiver is not null => MembersNamed(receiver, site.Name!),
            DynamicKind.GetIndex or DynamicKind.SetIndex when receiver is not null => AllMembers(receiver).Where(m => m is IPropertySymbol { IsIndexer: true }),
            DynamicKind.New when site.ResultType is INamedTypeSymbol created => created.InstanceConstructors,
            _ => [],
        };
        foreach (var member in candidates)
        {
            if (!DynamicAccessible(site, member, receiver)) return false;
            if (member is IPropertySymbol property)
                foreach (var accessor in new[] { property.GetMethod, property.SetMethod })
                    if (accessor is not null && !DynamicAccessible(site, accessor, receiver)) return false;
        }
        return true;
    }

    // ======================================================================================
    // Compiler-generated types
    // ======================================================================================

    /// <summary>Placeholder identifiers of the bound text -> the metadata names they stand for.</summary>
    private readonly Dictionary<string, string> _ilUnspeakable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ilUnspeakableByName = new(StringComparer.Ordinal);
    private const string UnspeakablePrefix = "__csdynN";

    /// <summary>
    /// A type of an IL assembly whose name C# can't spell (`&lt;&gt;f__AnonymousType0&lt;int, string&gt;`, an iterator's
    /// `&lt;Items&gt;d__3`): the binder sees it like any type its context can access. From source such values are
    /// Roslyn's anonymous types (rebuilt by shape); from IL they are ordinary metadata types, written here with
    /// placeholder names that <see cref="IlSpeakable"/> swaps for the real ones after parsing.
    /// </summary>
    private string? IlUnspeakableTypeText(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IlUnspeakableTypeText(array.ElementType) is { } element ? element + "[" + new string(',', array.Rank - 1) + "]" : null;
            case INamedTypeSymbol { IsAnonymousType: false } named when Il.Contains(named.ContainingAssembly) || TypeText(named) is not null:
            {
                if (TypeText(named) is { } text) return text;
                var prefix = named.ContainingType is { } outer ? IlUnspeakableTypeText(outer) is { } o ? o + "." : null
                    : named.ContainingNamespace is { IsGlobalNamespace: false } ns ? "global::" + ns.ToDisplayString() + "." : "global::";
                if (prefix is null) return null;
                var name = named.Name;
                if (!SyntaxFacts.IsValidIdentifier(name))
                {
                    if (!_ilUnspeakableByName.TryGetValue(name, out var placeholder))
                    {
                        placeholder = UnspeakablePrefix + _ilUnspeakable.Count;
                        _ilUnspeakable[placeholder] = name;
                        _ilUnspeakableByName[name] = placeholder;
                    }
                    name = placeholder;
                }
                else name = "@" + name;
                if (named.TypeArguments.Length == 0) return prefix + name;
                var arguments = new List<string>();
                foreach (var argument in named.TypeArguments)
                    if ((TypeText(argument) ?? IlUnspeakableTypeText(argument)) is { } a) arguments.Add(a);
                    else return null;
                return prefix + name + "<" + string.Join(", ", arguments) + ">";
            }
            default:
                return TypeText(type);
        }
    }

    /// <summary>The bound text with the placeholders of <see cref="IlUnspeakableTypeText"/> replaced by the real names.</summary>
    private BlockSyntax IlSpeakable(DynamicSite site, BlockSyntax block) =>
        !site.FromIl || _ilUnspeakable.Count == 0 ? block : (BlockSyntax)new UnspeakableNames(_ilUnspeakable).Visit(block);

    private sealed class UnspeakableNames(Dictionary<string, string> names) : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token) =>
            token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken) && names.TryGetValue(token.ValueText, out var real)
                ? Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Identifier(token.LeadingTrivia, Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken, real, real, token.TrailingTrivia)
                : base.VisitToken(token);
    }

    /// <summary>A binding of an IL site, kept only where the context's accessibility can't change it.</summary>
    private DynamicLeaf IlCheckedLeaf(DynamicSite site, DynamicClass?[] chosen, DynamicLeaf leaf, IOperation? bound)
    {
        if (!site.FromIl) return leaf;
        if (leaf.Error is not null || leaf.ErrorTemplate is not null)
            return IlDynamicCandidatesAccessible(site, chosen) ? leaf
                : new DynamicLeaf { Unsupported = $"the dynamic operation '{site.Syntax}' fails to bind with members its context can't access (a binding error csharpc can't reproduce from IL)" };
        if (leaf.Unsupported is null && IlDynamicInaccessibleMember(site, bound) is { } member)
            return new DynamicLeaf { Unsupported = $"the dynamic operation '{site.Syntax}' binds to '{member.ToDisplayString()}', which its context can't access (a binding csharpc can't reproduce from IL)" };
        return leaf;
    }
}
