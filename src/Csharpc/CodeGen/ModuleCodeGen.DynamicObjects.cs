using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Dynamic operations on ExpandoObject and DynamicObject: their meta-objects' rules, bound as C# that calls
/// corelib/Dynamic/DynamicObjects.cs (docs/adr/0019-dynamic.md).
///
/// ExpandoObject (MetaExpando): a member get/invoke looks in the dictionary first, then falls back to the C#
/// binding against ExpandoObject; a member set always stores. Other operations are the C# binding.
/// DynamicObject (MetaDynamic): the C# binding against the subclass first (its real members win); when that
/// fails, the matching Try* method; when that returns false, the C# binder's error. An invoked member that
/// TryInvokeMember doesn't handle is fetched with TryGetMember and its value invoked dynamically.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private const string Objects = "global::CsharpcCoreLib.DynamicObjects";

    /// <summary>The speculative trees of bound dynamic code -> the site they bind (a dynamic operation inside binds in its context).</summary>
    private readonly Dictionary<SyntaxTree, DynamicSite> _dynamicOrigins = [];

    internal void NoteDynamicOrigin(SyntaxTree tree, DynamicSite site) => _dynamicOrigins[tree] = site;

    /// <summary>Where a dynamic operation binds: its own source position, or the site whose bound code contains it.</summary>
    internal (SemanticModel Model, int Position, INamedTypeSymbol? Context, bool FromIl) DynamicContext(SyntaxNode syntax, int position, INamedTypeSymbol? context) =>
        _dynamicOrigins.TryGetValue(syntax.SyntaxTree, out var origin)
            ? (origin.Model, origin.Position, origin.Context, origin.FromIl)
            : (DynamicSemanticModel(syntax.SyntaxTree), position, context, false);

    private bool IsExpandoObject(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "ExpandoObject" } && type.ContainingNamespace?.ToDisplayString() == "System.Dynamic";

    private static bool IsDynamicObject(ITypeSymbol type)
    {
        for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
            if (t.Name == "DynamicObject" && t.ContainingNamespace?.ToDisplayString() == "System.Dynamic") return true;
        return false;
    }

    private static string ExpressionTypeOf(DynamicSite site) => (site.Kind, site.Operator, site.Compound) switch
    {
        (DynamicKind.Binary, "+", false) => "Add", (DynamicKind.Binary, "-", false) => "Subtract", (DynamicKind.Binary, "*", false) => "Multiply",
        (DynamicKind.Binary, "/", false) => "Divide", (DynamicKind.Binary, "%", false) => "Modulo", (DynamicKind.Binary, "&", false) => "And",
        (DynamicKind.Binary, "|", false) => "Or", (DynamicKind.Binary, "^", false) => "ExclusiveOr", (DynamicKind.Binary, "<<", false) => "LeftShift",
        (DynamicKind.Binary, ">>", false) => "RightShift", (DynamicKind.Binary, "==", _) => "Equal", (DynamicKind.Binary, "!=", _) => "NotEqual",
        (DynamicKind.Binary, "<", _) => "LessThan", (DynamicKind.Binary, "<=", _) => "LessThanOrEqual", (DynamicKind.Binary, ">", _) => "GreaterThan",
        (DynamicKind.Binary, ">=", _) => "GreaterThanOrEqual",
        (DynamicKind.Binary, "+", true) => "AddAssign", (DynamicKind.Binary, "-", true) => "SubtractAssign", (DynamicKind.Binary, "*", true) => "MultiplyAssign",
        (DynamicKind.Binary, "/", true) => "DivideAssign", (DynamicKind.Binary, "%", true) => "ModuloAssign", (DynamicKind.Binary, "&", true) => "AndAssign",
        (DynamicKind.Binary, "|", true) => "OrAssign", (DynamicKind.Binary, "^", true) => "ExclusiveOrAssign", (DynamicKind.Binary, "<<", true) => "LeftShiftAssign",
        (DynamicKind.Binary, ">>", true) => "RightShiftAssign",
        (DynamicKind.Unary, "-", _) => "Negate", (DynamicKind.Unary, "+", _) => "UnaryPlus", (DynamicKind.Unary, "!", _) => "Not",
        (DynamicKind.Unary, "~", _) => "OnesComplement", (DynamicKind.Unary, "++", _) => "Increment", (DynamicKind.Unary, "--", _) => "Decrement",
        (DynamicKind.IsTrue, _, _) => "IsTrue", (DynamicKind.IsFalse, _, _) => "IsFalse",
        _ => "",
    };

    /// <summary>A case whose meta-object operand (operand 0) is an ExpandoObject or a DynamicObject.</summary>
    private DynamicLeaf BindMetaObject(DynamicSite site, DynamicClass?[] chosen, ITypeSymbol type)
    {
        var expando = IsExpandoObject(type);
        if (!expando && !IsDynamicObject(type))
            return new DynamicLeaf { Unsupported = $"dynamic operations on '{type.ToDisplayString()}' (an IDynamicMetaObjectProvider other than ExpandoObject and DynamicObject)" };
        // The C# binding against the type itself (what the meta-object falls back to).
        var typed = (DynamicClass?[])chosen.Clone();
        typed[0] = new DynamicClass { Key = new DynamicClassKey(DynamicClassKind.Typed, type) };
        var language = BindDynamicLeaf(site, typed);
        if (site.Kind == DynamicKind.IsEvent) return language;
        var languageBinds = language is { Error: null, ErrorTemplate: null, Unsupported: null };
        if (DynamicExpressionText(site, typed, out var unsupported) is not { } text) return unsupported!;
        if (site.Operands.Any(o => o.RefKind != RefKind.None) && site.Kind is not (DynamicKind.GetMember or DynamicKind.SetMember))
            return languageBinds ? language : new DynamicLeaf { Unsupported = $"ref or out arguments to a dynamic call on '{type.ToDisplayString()}'" };

        var receiver = text.Texts[0];
        var name = site.Name is { } n ? SyntaxFactory.Literal(n).Text : "null";
        var hasReceiver = site.Operands[0].Role is DynamicRole.Receiver or DynamicRole.IndexReceiver or DynamicRole.InvokeReceiver;
        var argumentEnd = site.Kind is DynamicKind.SetMember or DynamicKind.SetIndex ? site.Operands.Count - 1 : site.Operands.Count;
        var argStart = hasReceiver ? 1 : 0;
        string Values() => string.Join(", ", Enumerable.Range(argStart, argumentEnd - argStart).Select(i => text.Texts[i]));
        string Names()
        {
            var names = Enumerable.Range(argStart, argumentEnd - argStart).Select(i => site.Operands[i].Name).OfType<string>().ToList();
            return names.Count == 0 ? "new string[0]" : "new string[] { " + string.Join(", ", names.Select(x => SyntaxFactory.Literal(x).Text)) + " }";
        }
        var value = site.Kind is DynamicKind.SetMember or DynamicKind.SetIndex ? text.Texts[^1] : "";
        // The value of a member invoked dynamically (Expando entries; DynamicObject's TryGetMember): `((dynamic)m)(args)`.
        var invokeValue = $"((dynamic)__csdynm)({text.Arguments(argStart, argumentEnd, true)})";
        string Result(string expression) => site.ResultDiscarded ? $"{expression};" : $"{ResultLocal} = {expression};";

        if (expando)
        {
            string LanguageTail() => !languageBinds ? Throw(language)
                : language.Result is null ? $"{text.Expression};" : Result(text.Expression);
            switch (site.Kind)
            {
                case DynamicKind.GetMember:
                    return BindMetaText(site, typed, text, $"object {ResultLocal}; if (!{Objects}.ExpandoTryGetMember({receiver}, {name}, out {ResultLocal})) {LanguageTail()}");
                case DynamicKind.SetMember when !site.Compound:
                    return BindMetaText(site, typed, text, $"{Objects}.ExpandoSetMember({receiver}, {name}, {value}); object {ResultLocal} = {value};");
                case DynamicKind.InvokeMember:
                    return BindMetaText(site, typed, text,
                        $"object {ResultLocal} = null; if ({Objects}.ExpandoTryGetMember({receiver}, {name}, out var __csdynm)) {Result(invokeValue)} else {LanguageTail()}");
                default:
                    return language;
            }
        }

        if (languageBinds) return language;
        var fail = Throw(language);
        switch (site.Kind)
        {
            case DynamicKind.GetMember:
                return BindMetaText(site, typed, text, $"object {ResultLocal}; if (!{Objects}.TryGetMember({receiver}, {name}, out {ResultLocal})) {fail}");
            case DynamicKind.SetMember when !site.Compound:
                return BindMetaText(site, typed, text, $"if (!{Objects}.TrySetMember({receiver}, {name}, {value})) {fail} object {ResultLocal} = {value};");
            case DynamicKind.InvokeMember when !site.SimpleName && site.StaticReceiver is null:
                return BindMetaText(site, typed, text,
                    $"object {ResultLocal} = null; if (!{Objects}.TryInvokeMember({receiver}, {name}, new object[] {{ {Values()} }}, {Names()}, out var __csdyni)) " +
                    $"{{ if (!{Objects}.TryGetMember({receiver}, {name}, out var __csdynm)) {fail} {Result(invokeValue)} }} else {ResultLocal} = __csdyni;");
            case DynamicKind.GetIndex:
                return BindMetaText(site, typed, text, $"object {ResultLocal}; if (!{Objects}.TryGetIndex({receiver}, new object[] {{ {Values()} }}, {Names()}, out {ResultLocal})) {fail}");
            case DynamicKind.SetIndex:
                return BindMetaText(site, typed, text, $"if (!{Objects}.TrySetIndex({receiver}, new object[] {{ {Values()} }}, {Names()}, {value})) {fail} object {ResultLocal} = {value};");
            case DynamicKind.Invoke:
                return BindMetaText(site, typed, text, $"object {ResultLocal}; if (!{Objects}.TryInvoke({receiver}, new object[] {{ {Values()} }}, {Names()}, out {ResultLocal})) {fail}");
            case DynamicKind.Binary when ExpressionTypeOf(site) is { Length: > 0 } operation:
                return BindMetaText(site, typed, text,
                    $"object {ResultLocal}; if (!{Objects}.TryBinaryOperation({receiver}, global::System.Linq.Expressions.ExpressionType.{operation}, {text.Texts[1]}, out {ResultLocal})) {fail}");
            case DynamicKind.Unary when ExpressionTypeOf(site) is { Length: > 0 } operation:
                return BindMetaText(site, typed, text,
                    $"object {ResultLocal}; if (!{Objects}.TryUnaryOperation({receiver}, global::System.Linq.Expressions.ExpressionType.{operation}, out {ResultLocal})) {fail}");
            case DynamicKind.IsTrue or DynamicKind.IsFalse:
                return BindMetaText(site, typed, text,
                    $"object __csdynt; if (!{Objects}.TryUnaryOperation({receiver}, global::System.Linq.Expressions.ExpressionType.{ExpressionTypeOf(site)}, out __csdynt)) {fail} bool {ResultLocal} = (bool)__csdynt;");
            case DynamicKind.Convert:
            {
                var target = TypeText(site.ResultType!)!;
                return BindMetaText(site, typed, text,
                    $"object __csdynt; if (!{Objects}.TryConvert({receiver}, typeof({target}), {(site.Explicit ? "true" : "false")}, out __csdynt)) {fail} {target} {ResultLocal} = ({target})__csdynt;");
            }
            default:
                return language;
        }
    }

    /// <summary>`throw new RuntimeBinderException(...)` with the C# binder's error for the fallback binding.</summary>
    private static string Throw(DynamicLeaf language) =>
        language.Error is { } message
            ? $"throw new global::Microsoft.CSharp.RuntimeBinder.RuntimeBinderException({SyntaxFactory.Literal(message).Text});"
            : $"throw new global::System.NotSupportedException({SyntaxFactory.Literal("Not supported by csharpc yet: " + (language.Unsupported ?? "a dynamic operation whose error names run-time types") + ".").Text});";

    /// <summary>Binds statements (operand declarations, then <paramref name="statements"/>, which set the result local).</summary>
    private DynamicLeaf BindMetaText(DynamicSite site, DynamicClass?[] chosen, DynamicText text, string statements)
    {
        var source = text.Jump
            ? $"{{ goto __csdynL; {text.Declarations} __csdynL: ; {statements} }}"
            : $"{{ {text.Declarations} {statements} }}";
        var block = IlSpeakable(site, (BlockSyntax)SyntaxFactory.ParseStatement(source));
        if (block.ContainsDiagnostics)
            throw CompileError.Unsupported($"dynamic operation csharpc can't express ({source})");
        if (!site.Model.TryGetSpeculativeSemanticModel(site.Position, block, out var model))
            throw CompileError.Unsupported("dynamic operation in this position (no speculative binding)");
        NoteDynamicOrigin(block.SyntaxTree, site);
        var locals = new ILocalSymbol?[site.Operands.Count];
        ILocalSymbol? result = null;
        foreach (var declarator in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var localName = declarator.Identifier.ValueText;
            if (localName == ResultLocal) result = (ILocalSymbol?)model.GetDeclaredSymbol(declarator);
            else if (localName.StartsWith(Local, StringComparison.Ordinal) && int.TryParse(localName.AsSpan(Local.Length), out var index))
                locals[index] = (ILocalSymbol?)model.GetDeclaredSymbol(declarator);
        }
        if (model.GetOperation(block) is not { } operation || HasBindingErrors(operation))
            return new DynamicLeaf { Unsupported = $"the dynamic operation '{site.Syntax}' on '{chosen[0]?.Key.Type?.ToDisplayString()}'" };
        if (site.FromIl && IlCheckedLeaf(site, chosen, new DynamicLeaf(), operation) is { Unsupported: not null } inaccessible) return inaccessible;
        return new DynamicLeaf { Locals = locals, Result = result, Graph = ControlFlowGraph.Create(block, model) };
    }
}
