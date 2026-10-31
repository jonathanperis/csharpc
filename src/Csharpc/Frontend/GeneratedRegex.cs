using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.Frontend;

/// <summary>
/// csharpc's replacement for the SDK's Regex source generator ([GeneratedRegex]). .NET's generator emits a
/// Regex-derived class whose matching code is C# built on RegexRunner internals; csharpc instead implements
/// each [GeneratedRegex] member as a cached instance of the corelib's interpreter (corelib/Regex), built from
/// the attribute's pattern, options, timeout and culture — the same matching semantics. The one observable
/// difference: GetType() of the instance is Regex, not the generated type.
///
/// Diagnostics follow the SDK generator (SYSLIB1040–SYSLIB1043, same ids and messages); patterns are validated
/// with the real System.Text.RegularExpressions parser of the .NET csharpc runs on.
/// </summary>
internal sealed class GeneratedRegexGenerator : IIncrementalGenerator
{
    private const string AttributeName = "System.Text.RegularExpressions.GeneratedRegexAttribute";
    private const string RegexName = "System.Text.RegularExpressions.Regex";
    private const string Title = "Invalid GeneratedRegexAttribute usage";

    private static readonly DiagnosticDescriptor s_invalidAttribute = new("SYSLIB1040", Title,
        "The GeneratedRegexAttribute is malformed", "Performance", DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.NotConfigurable);
    private static readonly DiagnosticDescriptor s_multipleAttributes = new("SYSLIB1041", Title,
        "Multiple GeneratedRegexAttributes were applied to the same method, but only one is allowed.", "Performance", DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.NotConfigurable);
    private static readonly DiagnosticDescriptor s_invalidArguments = new("SYSLIB1042", Title,
        "The specified regex is invalid. '{0}'", "Performance", DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.NotConfigurable);
    private static readonly DiagnosticDescriptor s_invalidSignature = new("SYSLIB1043", Title,
        "GeneratedRegexAttribute method or property must be partial, parameterless, non-generic, non-abstract, and return Regex. If a property, it must also be get-only.",
        "Performance", DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.NotConfigurable);

    private sealed record Member(string Namespace, List<string> ContainingTypes, string Modifiers, bool IsProperty, bool Nullable,
        string Name, string Pattern, int Options, int? MatchTimeout, string CultureName);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var members = context.SyntaxProvider.ForAttributeWithMetadataName(AttributeName,
            (node, _) => node is MethodDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax or AccessorDeclarationSyntax,
            (ctx, _) => Analyze(ctx));
        context.RegisterSourceOutput(members.Collect(), Emit);
    }

    /// <summary>A <see cref="Member"/> or the <see cref="Diagnostic"/> the SDK generator reports for it.</summary>
    private static object? Analyze(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetNode is IndexerDeclarationSyntax or AccessorDeclarationSyntax)
            return Diagnostic.Create(s_invalidSignature, context.TargetNode.GetLocation());
        var syntax = (MemberDeclarationSyntax)context.TargetNode;
        var regexType = context.SemanticModel.Compilation.GetTypeByMetadataName(RegexName);
        if (regexType is null || syntax.Parent is not TypeDeclarationSyntax typeDeclaration) return null;
        if (context.Attributes.Length != 1) return Diagnostic.Create(s_multipleAttributes, syntax.GetLocation());

        var attribute = context.Attributes[0];
        if (attribute.ConstructorArguments.Any(a => a.Kind == TypedConstantKind.Error))
            return Diagnostic.Create(s_invalidAttribute, syntax.GetLocation());
        var args = attribute.ConstructorArguments;
        if (args.Length is 0 or > 4) return Diagnostic.Create(s_invalidAttribute, syntax.GetLocation());

        var pattern = args[0].Value as string;
        int? options = null, matchTimeout = null;
        string? cultureName = "";
        if (args.Length >= 2)
        {
            options = args[1].Value as int?;
            if (args.Length == 4) { matchTimeout = args[2].Value as int?; cultureName = args[3].Value as string; }
            else if (args.Length == 3)
            {
                if (args[2].Type?.SpecialType == SpecialType.System_Int32) matchTimeout = args[2].Value as int?;
                else cultureName = args[2].Value as string;
            }
        }
        if (pattern is null || cultureName is null) return Diagnostic.Create(s_invalidArguments, syntax.GetLocation(), "(null)");

        bool nullable;
        if (context.TargetSymbol is IMethodSymbol method)
        {
            if (!method.IsPartialDefinition || method.IsAbstract || method.Parameters.Length != 0 || method.Arity != 0
                || !SymbolEqualityComparer.Default.Equals(method.ReturnType, regexType))
                return Diagnostic.Create(s_invalidSignature, syntax.GetLocation());
            nullable = method.ReturnNullableAnnotation == NullableAnnotation.Annotated;
        }
        else if (context.TargetSymbol is IPropertySymbol property)
        {
            if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword) || property.IsAbstract || property.SetMethod is not null
                || !SymbolEqualityComparer.Default.Equals(property.Type, regexType))
                return Diagnostic.Create(s_invalidSignature, syntax.GetLocation());
            nullable = property.NullableAnnotation == NullableAnnotation.Annotated;
        }
        else return null;

        var regexOptions = (RegexOptions)(options ?? 0);
        // The SDK generator parses the pattern with its own copy of the engine; the parser of the .NET csharpc
        // runs on gives the same errors (NonBacktracking only changes the engine, not the syntax).
        // Option combinations Regex's constructor rejects (ECMAScript with others) are not rejected by the
        // generator, so parse without ECMAScript then.
        var parseOptions = regexOptions & ~RegexOptions.NonBacktracking & (RegexOptions)0xFFF;
        if ((parseOptions & RegexOptions.ECMAScript) != 0
            && (parseOptions & ~(RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant)) != 0)
            parseOptions &= ~RegexOptions.ECMAScript;
        try
        {
            _ = new Regex(pattern, parseOptions);
        }
        catch (RegexParseException e)
        {
            return Diagnostic.Create(s_invalidArguments, syntax.GetLocation(), e.Message);
        }

        // The culture only matters for case-insensitive patterns (option or inline (?i)).
        var ignoreCase = (regexOptions & RegexOptions.IgnoreCase) != 0 || Regex.IsMatch(pattern, @"\(\?[imnsx-]*i");
        if (!ignoreCase) cultureName = "";
        if (cultureName.Length > 0)
        {
            // csharpc runs with invariant globalization, so CultureInfo can't look the name up; accept
            // well-formed BCP 47 names (the ones CultureInfo.GetCultureInfo would know).
            if ((regexOptions & RegexOptions.CultureInvariant) != 0 || !Regex.IsMatch(cultureName, @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$"))
                return Diagnostic.Create(s_invalidArguments, syntax.GetLocation(), "cultureName");
        }

        const RegexOptions SupportedOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ECMAScript
            | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline
            | RegexOptions.NonBacktracking | RegexOptions.RightToLeft | RegexOptions.Singleline | (RegexOptions)0x800 /* AnyNewLine */;
        if ((regexOptions & ~SupportedOptions) != 0) return Diagnostic.Create(s_invalidArguments, syntax.GetLocation(), "options");
        if (matchTimeout is 0 or < -1) return Diagnostic.Create(s_invalidArguments, syntax.GetLocation(), "matchTimeout");

        var ns = context.TargetSymbol.ContainingType?.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : "";
        List<string> types = [];
        for (SyntaxNode? t = typeDeclaration; t is TypeDeclarationSyntax td; t = t.Parent)
        {
            var keyword = td is RecordDeclarationSyntax rd && !rd.ClassOrStructKeyword.IsKind(SyntaxKind.None)
                ? $"{td.Keyword.ValueText} {rd.ClassOrStructKeyword.ValueText}" : td.Keyword.ValueText;
            types.Insert(0, $"partial {keyword} {td.Identifier}{td.TypeParameterList}");
        }
        return new Member(ns, types, syntax.Modifiers.ToString(), context.TargetSymbol is IPropertySymbol, nullable,
            context.TargetSymbol.Name, pattern, (int)regexOptions, matchTimeout, cultureName);
    }

    private static void Emit(SourceProductionContext context, System.Collections.Immutable.ImmutableArray<object?> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// csharpc: [GeneratedRegex] members are cached instances of the corelib's Regex interpreter.");
        sb.AppendLine("#nullable enable annotations");
        sb.AppendLine("#pragma warning disable CS0282");
        var index = 0;
        // .NET's generator emits one implementation per (pattern, options, timeout): members that share one return
        // the same instance, with the culture of the first of them.
        var shared = new Dictionary<(string, int, int?), string>();
        foreach (var item in items)
        {
            if (item is Diagnostic diagnostic) { context.ReportDiagnostic(diagnostic); continue; }
            if (item is not Member m) continue;
            var key = (m.Pattern, m.Options, m.MatchTimeout);
            var existing = shared.TryGetValue(key, out var holder);
            if (!existing) shared[key] = holder = $"CsharpcGeneratedRegex_{index++}";
            var depth = 0;
            if (m.Namespace.Length > 0) { sb.AppendLine($"namespace {m.Namespace}"); sb.AppendLine("{"); }
            foreach (var type in m.ContainingTypes) { sb.AppendLine(type); sb.AppendLine("{"); depth++; }
            sb.Append($"    {m.Modifiers} global::{RegexName}{(m.Nullable ? "?" : "")} {m.Name}{(m.IsProperty ? "" : "()")}");
            sb.AppendLine($" => global::System.Text.RegularExpressions.Generated.{holder}.Instance;");
            for (var i = 0; i < depth; i++) sb.AppendLine("}");
            if (m.Namespace.Length > 0) sb.AppendLine("}");
            if (existing) continue;

            sb.AppendLine("namespace System.Text.RegularExpressions.Generated");
            sb.AppendLine("{");
            sb.AppendLine($"    internal static class {holder}");
            sb.AppendLine("    {");
            sb.AppendLine($"        internal static readonly global::{RegexName} Instance = (global::{RegexName})(object)"
                + $"global::CsharpcCoreLib.RegularExpressions.Regex.CreateForGeneratedRegex({Literal(m.Pattern)}, "
                + $"(global::System.Text.RegularExpressions.RegexOptions)({m.Options}), {(m.MatchTimeout is { } t ? t.ToString() : "int.MinValue")}, {Literal(m.CultureName)});");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
        context.AddSource("RegexGenerator.g.cs", sb.ToString());
    }

    private static string Literal(string s) => SymbolDisplay.FormatLiteral(s, quote: true);
}
