using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.Frontend;

/// <summary>
/// Compile-time metadata for System.Text.Json's reflection-based serializer (docs/12-corelib.md#systemtextjson).
///
/// csharpc is closed-world: there is no run-time reflection. NativeAOT solves the same problem with the
/// JSON source generator, and so does csharpc, transparently: every `JsonSerializer.Serialize/Deserialize`
/// call that has no JsonTypeInfo/JsonSerializerContext argument contributes its type to a synthesized
///
///     [JsonSerializable(typeof(T))] internal sealed partial class __CsharpcJsonContextN : JsonSerializerContext { }
///
/// The real JSON source generator (the one the SDK runs) then produces exactly .NET's metadata for those
/// types: member discovery and order, attributes, constructors, converters. The corelib's
/// JsonSerializerOptions falls back to these contexts where .NET would use DefaultJsonTypeInfoResolver.
///
/// Types the generator can only see from inside their declaring class (private nested types) get a
/// context nested in that class, which is made `partial` for the purpose.
/// </summary>
public static class JsonReflectionContexts
{
    private const string ContextPrefix = "__CsharpcJsonContext";
    private const string TreePath = "<csharpc-json-contexts>.g.cs";
    private const string BuiltInsTreePath = "<csharpc-json-builtins>.g.cs";

    private static readonly DiagnosticDescriptor s_unsupported = new(
        "CSLLVM0001", "Not supported by csharpc yet", "not supported by csharpc yet: {0}", "csharpc", DiagnosticSeverity.Error, true);

    public static bool IsSynthesizedTree(SyntaxTree tree) => tree.FilePath is TreePath or BuiltInsTreePath;

    /// <summary>Generated sources of synthesized contexts are named after them.</summary>
    public static bool IsSynthesizedContextFile(string path) => Path.GetFileName(path).Contains(ContextPrefix, StringComparison.Ordinal);

    public static CSharpCompilation Add(CSharpCompilation compilation, IReadOnlyList<SyntaxTree> userTrees, bool? reflectionEnabled,
        List<Diagnostic> diagnostics, out IReadOnlyList<SyntaxTree> newUserTrees)
    {
        newUserTrees = userTrees;
        var texts = userTrees.Select(t => t.GetText().ToString()).ToList();
        if (userTrees.Count == 0 || !texts.Any(t => t.Contains("Json", StringComparison.Ordinal))) return compilation;
        var parseOptions = (CSharpParseOptions)userTrees[0].Options;
        var enabled = reflectionEnabled ?? FileBasedReflectionDefault(userTrees);

        // DefaultJsonTypeInfoResolver's knowledge of the built-in types (every primitive converter) is only
        // linked in where reflection-based metadata can be asked for: a program that uses source-generated
        // contexts only doesn't pay for it.
        if (enabled || texts.Any(t => t.Contains("DefaultJsonTypeInfoResolver", StringComparison.Ordinal)))
        {
            var builtIns = """
                namespace CsharpcCoreLib
                {
                    internal static partial class JsonReflection
                    {
                        static partial void CreateBuiltIn(global::System.Type type, global::CsharpcCoreLib.JsonSerializerOptionsImpl options, ref global::CsharpcCoreLib.JsonTypeInfoImpl info)
                        {
                            info = global::CsharpcCoreLib.JsonBuiltInTypeInfos.TryCreate(type, options);
                        }
                    }
                }
                """;
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(builtIns, parseOptions, path: BuiltInsTreePath));
        }

        if (!enabled)
        {
            // dotnet: JsonSerializerIsReflectionEnabledByDefault=false (trimmed/AOT apps, file-based apps by default).
            var disabled = """
                namespace CsharpcCoreLib
                {
                    internal static partial class JsonReflection
                    {
                        static partial void Configure(ref bool reflectionEnabled, ref global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver resolver)
                        {
                            reflectionEnabled = false;
                        }
                    }
                }
                """;
            return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(disabled, parseOptions, path: TreePath));
        }
        if (!texts.Any(t => t.Contains("JsonSerializer", StringComparison.Ordinal))) return compilation;
        if (compilation.GetTypeByMetadataName("System.Text.Json.JsonSerializer") is not { } serializer) return compilation;

        var finder = new RootFinder(compilation, serializer, diagnostics);
        foreach (var tree in userTrees) finder.Scan(tree);
        var roots = finder.ResolveRoots();
        if (roots.Count == 0) return compilation;

        // Group the roots by the class a context must be nested in to see them.
        var groups = new Dictionary<INamedTypeSymbol, List<ITypeSymbol>>(SymbolEqualityComparer.Default);
        var topLevel = new List<ITypeSymbol>();
        foreach (var (root, at) in roots)
        {
            if (!TryGetHost(root, out var host))
            {
                diagnostics.Add(Diagnostic.Create(s_unsupported, at,
                    $"System.Text.Json serialization of '{root.ToDisplayString()}' (its type is only accessible from several unrelated classes)"));
                continue;
            }
            if (host is null) topLevel.Add(root);
            else
            {
                if (!groups.TryGetValue(host, out var list)) groups[host] = list = [];
                list.Add(root);
            }
        }

        // Hosts (and their containing types) must be partial in source.
        var toPartial = new Dictionary<SyntaxTree, List<TypeDeclarationSyntax>>();
        foreach (var host in groups.Keys.ToList())
        {
            for (var t = host; t is not null; t = t.ContainingType)
            {
                if (t.IsGenericType || t.DeclaringSyntaxReferences.Length == 0 || t.IsImplicitlyDeclared)
                {
                    foreach (var r in groups[host])
                        diagnostics.Add(Diagnostic.Create(s_unsupported, roots.First(x => SymbolEqualityComparer.Default.Equals(x.Type, r)).At,
                            $"System.Text.Json serialization of '{r.ToDisplayString()}' (a type nested in '{t.ToDisplayString()}')"));
                    groups.Remove(host);
                    break;
                }
                foreach (var reference in t.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is not TypeDeclarationSyntax declaration) continue;
                    if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) continue;
                    if (!toPartial.TryGetValue(declaration.SyntaxTree, out var list)) toPartial[declaration.SyntaxTree] = list = [];
                    if (!list.Contains(declaration)) list.Add(declaration);
                }
            }
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/> csharpc: System.Text.Json metadata for reflection-based JsonSerializer calls.");
        source.AppendLine("#nullable disable");
        var contexts = new List<string>();
        var counter = 0;
        var rootIndex = 0;
        string EmitContext(StringBuilder sb, List<ITypeSymbol> types, string indent, string accessibility)
        {
            var name = ContextPrefix + counter++;
            // The generator names each type's metadata property after the type (ListString, Person); two
            // roots with the same name in different namespaces get explicit names. (Built-in types must
            // keep theirs: the generated code refers to JsonMetadataServices.<Name>Converter.)
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in types)
            {
                var propertyName = GeneratorTypeName(type);
                var named = used.Add(propertyName) ? "" : $", TypeInfoPropertyName = \"__CsharpcJsonRoot{rootIndex++}\"";
                sb.AppendLine($"{indent}[global::System.Text.Json.Serialization.JsonSerializable(typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){named})]");
            }
            sb.AppendLine($"{indent}{accessibility} sealed partial class {name} : global::System.Text.Json.Serialization.JsonSerializerContext {{ }}");
            return name;
        }

        if (topLevel.Count > 0)
        {
            source.AppendLine("namespace CsharpcJsonContexts");
            source.AppendLine("{");
            contexts.Add("new global::CsharpcJsonContexts." + EmitContext(source, topLevel, "    ", "internal") + "()");
            source.AppendLine("}");
        }
        foreach (var (host, types) in groups)
        {
            var chain = new List<INamedTypeSymbol>();
            for (var t = host; t is not null; t = t.ContainingType) chain.Insert(0, t);
            var ns = host.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : null;
            if (ns is not null) source.AppendLine($"namespace {ns}").AppendLine("{");
            // The context is private (its generated members mention the private types); each enclosing type
            // gets an internal factory forwarding to the next one, so JsonReflection can reach it.
            var name = ContextPrefix + counter;
            var factory = name + "Create";
            const string contextType = "global::System.Text.Json.Serialization.JsonSerializerContext";
            for (var i = 0; i < chain.Count; i++)
            {
                source.AppendLine($"partial {TypeKeyword(chain[i])} {chain[i].Name}").AppendLine("{");
                var target = i + 1 < chain.Count ? $"{chain[i + 1].Name}.{factory}()" : $"new {name}()";
                source.AppendLine($"    internal static {contextType} {factory}() => {target};");
            }
            EmitContext(source, types, "    ", "private");
            contexts.Add($"{chain[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{factory}()");
            foreach (var _ in chain) source.AppendLine("}");
            if (ns is not null) source.AppendLine("}");
        }
        source.AppendLine("namespace CsharpcCoreLib");
        source.AppendLine("{");
        source.AppendLine("    internal static partial class JsonReflection");
        source.AppendLine("    {");
        source.AppendLine("        static partial void Configure(ref bool reflectionEnabled, ref global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver resolver)");
        source.AppendLine("        {");
        source.AppendLine($"            resolver = global::System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine({string.Join(", ", contexts)});");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");

        // Make host classes partial (a modifier only: no line moves, so diagnostics and debug info keep their lines).
        var replaced = new List<SyntaxTree>(userTrees);
        foreach (var (tree, declarations) in toPartial)
        {
            var root = tree.GetRoot().ReplaceNodes(declarations, (_, rewritten) =>
            {
                var keyword = rewritten.Keyword;
                var partial = SyntaxFactory.Token(keyword.LeadingTrivia, SyntaxKind.PartialKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space));
                return rewritten.WithKeyword(keyword.WithLeadingTrivia()).AddModifiers(partial);
            });
            var newTree = tree.WithRootAndOptions(root, tree.Options);
            compilation = compilation.ReplaceSyntaxTree(tree, newTree);
            replaced[replaced.IndexOf(tree)] = newTree;
        }
        newUserTrees = replaced;
        return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source.ToString(), parseOptions, path: TreePath));
    }

    /// <summary>The JSON generator's default TypeInfoPropertyName (type name plus type arguments, "Array" suffix).</summary>
    private static string GeneratorTypeName(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol a => GeneratorTypeName(a.ElementType) + "Array",
        INamedTypeSymbol { IsGenericType: true } n => n.Name + string.Concat(n.TypeArguments.Select(GeneratorTypeName)),
        _ => type.Name,
    };

    private static string TypeKeyword(INamedTypeSymbol t) => t switch
    {
        { IsRecord: true, IsValueType: true } => "record struct",
        { IsRecord: true } => "record",
        { TypeKind: TypeKind.Struct } => "struct",
        { TypeKind: TypeKind.Interface } => "interface",
        _ => "class",
    };

    /// <summary>
    /// `dotnet run app.cs` publishes trimmed (PublishAot defaults to true for file-based apps), and the
    /// SDK then sets JsonSerializerIsReflectionEnabledByDefault=false: reflection-based serialization
    /// throws unless a `#:property` turns it back on.
    /// </summary>
    private static bool FileBasedReflectionDefault(IReadOnlyList<SyntaxTree> trees)
    {
        string? Property(string name)
        {
            string? value = null;
            foreach (var tree in trees)
                foreach (var line in tree.GetText().Lines)
                {
                    var text = line.ToString().Trim();
                    if (!text.StartsWith("#:property", StringComparison.Ordinal)) continue;
                    var body = text["#:property".Length..].Trim();
                    var eq = body.IndexOfAny(['=', ' ']);
                    if (eq <= 0) continue;
                    if (body[..eq].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) value = body[(eq + 1)..].Trim().Trim('"');
                }
            return value;
        }
        if (Property("JsonSerializerIsReflectionEnabledByDefault") is { } explicitValue)
            return !explicitValue.Equals("false", StringComparison.OrdinalIgnoreCase);
        var trimmed = Property("PublishTrimmed") is { } t ? t.Equals("true", StringComparison.OrdinalIgnoreCase)
            : !(Property("PublishAot") is { } aot && aot.Equals("false", StringComparison.OrdinalIgnoreCase));
        return !trimmed;
    }

    /// <summary>
    /// The class a context must be nested in to name <paramref name="type"/> (null: namespace level).
    /// False when no single class works.
    /// </summary>
    private static bool TryGetHost(ITypeSymbol type, out INamedTypeSymbol? host)
    {
        host = null;
        var requirements = new List<INamedTypeSymbol>();
        Collect(type, requirements);
        foreach (var r in requirements)
        {
            if (host is null || IsNestedIn(r, host)) host = r;
            else if (!IsNestedIn(host, r)) return false;
        }
        return true;

        static void Collect(ITypeSymbol t, List<INamedTypeSymbol> into)
        {
            switch (t)
            {
                case IArrayTypeSymbol a:
                    Collect(a.ElementType, into);
                    break;
                case INamedTypeSymbol n:
                    foreach (var arg in n.TypeArguments) Collect(arg, into);
                    for (var c = n; c.ContainingType is { } outer; c = outer)
                        if (c.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
                        {
                            into.Add(outer);
                            break;
                        }
                    break;
            }
        }

        static bool IsNestedIn(INamedTypeSymbol inner, INamedTypeSymbol outer)
        {
            for (var t = inner; t is not null; t = t.ContainingType)
                if (SymbolEqualityComparer.Default.Equals(t, outer)) return true;
            return false;
        }
    }

    /// <summary>Finds the types of reflection-based serializer calls, through generic code.</summary>
    private sealed class RootFinder(CSharpCompilation compilation, INamedTypeSymbol serializer, List<Diagnostic> diagnostics)
    {
        private readonly List<(ITypeSymbol Type, Location At)> _roots = [];
        // Constructed generic methods and types used anywhere, to resolve type parameters of roots.
        private readonly List<IMethodSymbol> _methodUses = [];
        private readonly List<INamedTypeSymbol> _typeUses = [];

        private bool IsJsonMetadataParameter(IParameterSymbol p) => p.Type is INamedTypeSymbol t
            && t.ContainingNamespace?.ToDisplayString() is "System.Text.Json.Serialization.Metadata" or "System.Text.Json.Serialization"
            && t.Name is "JsonTypeInfo" or "JsonSerializerContext";

        public void Scan(SyntaxTree tree)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case InvocationExpressionSyntax invocation:
                    {
                        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) break;
                        if (method.IsGenericMethod && !method.IsDefinition) _methodUses.Add(method);
                        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, serializer)) AddSerializerCall(model, invocation, method);
                        else if (method is { Name: "GetTypeInfo" or "TryGetTypeInfo", ContainingType.Name: "JsonSerializerOptions" })
                            AddTypeArguments(model, invocation, method);
                        break;
                    }
                    case ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                        if (model.GetTypeInfo(node).Type is INamedTypeSymbol { IsGenericType: true } created) _typeUses.Add(created);
                        break;
                    case GenericNameSyntax generic:
                        if (model.GetSymbolInfo(generic).Symbol is INamedTypeSymbol { IsGenericType: true } named && !named.IsDefinition) _typeUses.Add(named);
                        break;
                }
            }
        }

        private void AddSerializerCall(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
        {
            if (method.Parameters.Any(IsJsonMetadataParameter)) return;
            var at = invocation.GetLocation();
            foreach (var t in method.TypeArguments) Add(t, at);
            AddTypeArguments(model, invocation, method);
            // Serialize<object>(person): the value's own type is what gets serialized.
            if (model.GetOperation(invocation) is IInvocationOperation op)
                foreach (var arg in op.Arguments)
                    if (arg.Parameter is { Name: "value" } && arg.Value is IConversionOperation { IsImplicit: true } conversion
                        && conversion.Operand.Type is { } operandType && operandType.SpecialType != SpecialType.System_Object)
                        Add(operandType, at);
        }

        private void AddTypeArguments(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
        {
            if (model.GetOperation(invocation) is not IInvocationOperation op) return;
            foreach (var arg in op.Arguments)
            {
                if (arg.Parameter?.Type is not { Name: "Type", ContainingNamespace.Name: "System" }) continue;
                var value = arg.Value;
                while (value is IConversionOperation c) value = c.Operand;
                switch (value)
                {
                    case ITypeOfOperation typeOf:
                        Add(typeOf.TypeOperand, invocation.GetLocation());
                        break;
                    case IInvocationOperation { TargetMethod.Name: "GetType", Instance: { Type: { } instanceType } }
                        when instanceType.SpecialType != SpecialType.System_Object:
                        Add(instanceType, invocation.GetLocation());
                        break;
                }
            }
        }

        private void Add(ITypeSymbol type, Location at)
        {
            if (type.TypeKind == TypeKind.Error || type.TypeKind == TypeKind.Dynamic || type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) return;
            if (ContainsAnonymous(type))
            {
                diagnostics.Add(Diagnostic.Create(s_unsupported, at, "System.Text.Json serialization of anonymous types"));
                return;
            }
            // Generated metadata lives in another file, which can't name a file-local type.
            if (FileLocalPart(type) is { } fileLocal)
            {
                diagnostics.Add(Diagnostic.Create(s_unsupported, at, $"System.Text.Json serialization of file-local type '{fileLocal.Name}'"));
                return;
            }
            _roots.Add((type, at));
        }

        private static INamedTypeSymbol? FileLocalPart(ITypeSymbol t)
        {
            switch (t)
            {
                case IArrayTypeSymbol a:
                    return FileLocalPart(a.ElementType);
                case INamedTypeSymbol n:
                    for (var c = n; c is not null; c = c.ContainingType)
                        if (c.IsFileLocal) return c;
                    foreach (var arg in n.TypeArguments)
                        if (FileLocalPart(arg) is { } inner) return inner;
                    return null;
                default:
                    return null;
            }
        }

        private static bool ContainsAnonymous(ITypeSymbol t) => t switch
        {
            { IsAnonymousType: true } => true,
            IArrayTypeSymbol a => ContainsAnonymous(a.ElementType),
            INamedTypeSymbol n => n.TypeArguments.Any(ContainsAnonymous),
            _ => false,
        };

        private static bool ContainsTypeParameters(ITypeSymbol t) => t switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol a => ContainsTypeParameters(a.ElementType),
            INamedTypeSymbol n => n.TypeArguments.Any(ContainsTypeParameters),
            _ => false,
        };

        /// <summary>Closed root types; roots inside generic code are instantiated with every use of that code.</summary>
        public List<(ITypeSymbol Type, Location At)> ResolveRoots()
        {
            var result = new List<(ITypeSymbol, Location)>();
            var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            var pending = new Queue<(ITypeSymbol Type, Location At, int Depth)>(_roots.Select(r => (r.Type, r.At, 0)));
            while (pending.Count > 0)
            {
                var (type, at, depth) = pending.Dequeue();
                if (!ContainsTypeParameters(type))
                {
                    if (seen.Add(type)) result.Add((type, at));
                    continue;
                }
                if (depth > 4) continue;
                foreach (var substituted in Substitutions(type))
                    pending.Enqueue((substituted, at, depth + 1));
            }
            return result;
        }

        private IEnumerable<ITypeSymbol> Substitutions(ITypeSymbol type)
        {
            var parameters = new List<ITypeParameterSymbol>();
            CollectTypeParameters(type, parameters);
            var first = parameters[0];
            IEnumerable<IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>> maps = first.TypeParameterKind switch
            {
                TypeParameterKind.Method => _methodUses
                    .Where(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, first.DeclaringMethod?.OriginalDefinition))
                    .Select(m => (IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>)m.OriginalDefinition.TypeParameters
                        .Zip(m.TypeArguments).ToDictionary(p => p.First, p => p.Second, (IEqualityComparer<ITypeParameterSymbol>)SymbolEqualityComparer.Default)),
                TypeParameterKind.Type => _typeUses
                    .Where(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, first.DeclaringType?.OriginalDefinition))
                    .Select(t => (IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>)t.OriginalDefinition.TypeParameters
                        .Zip(t.TypeArguments).ToDictionary(p => p.First, p => p.Second, (IEqualityComparer<ITypeParameterSymbol>)SymbolEqualityComparer.Default)),
                _ => [],
            };
            foreach (var map in maps)
                if (Substitute(type, map) is { } substituted) yield return substituted;
        }

        private static void CollectTypeParameters(ITypeSymbol t, List<ITypeParameterSymbol> into)
        {
            switch (t)
            {
                case ITypeParameterSymbol p: into.Add(p); break;
                case IArrayTypeSymbol a: CollectTypeParameters(a.ElementType, into); break;
                case INamedTypeSymbol n: foreach (var arg in n.TypeArguments) CollectTypeParameters(arg, into); break;
            }
        }

        private ITypeSymbol? Substitute(ITypeSymbol t, IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol> map)
        {
            switch (t)
            {
                case ITypeParameterSymbol p:
                    return map.TryGetValue((ITypeParameterSymbol)p.OriginalDefinition, out var r) ? r : map.TryGetValue(p, out r) ? r : p;
                case IArrayTypeSymbol a:
                    return Substitute(a.ElementType, map) is { } e ? compilation.CreateArrayTypeSymbol(e, a.Rank) : null;
                case INamedTypeSymbol { IsGenericType: true } n:
                {
                    var args = new ITypeSymbol[n.TypeArguments.Length];
                    for (var i = 0; i < args.Length; i++)
                        if ((args[i] = Substitute(n.TypeArguments[i], map)!) is null) return null;
                    return n.OriginalDefinition.Construct(args);
                }
                default:
                    return t;
            }
        }
    }
}
