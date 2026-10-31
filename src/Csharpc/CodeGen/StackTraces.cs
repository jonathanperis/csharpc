using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Csharpc.Frontend;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Csharpc.CodeGen;

/// <summary>
/// Stack-trace metadata (docs/adr/0012-stack-traces.md): what <c>Exception.StackTrace</c> prints for
/// each call site of the program's own code — the method, formatted like .NET's
/// <c>System.Diagnostics.StackTrace</c>, and the document and line the PDB maps the call to.
///
/// Names and lines come from the program as .NET's own build sees it: the user's sources are
/// emitted once with Roslyn (as `dotnet build` would) and the portable PDB is read back. Its
/// sequence points give the line of every statement exactly as the CLR reports it, and its methods
/// give the names Roslyn generates for lambdas, local functions and closure classes
/// (<c>Program.&lt;&gt;c.&lt;Main&gt;b__0_0()</c>), which depend on ordinals that are easy to get
/// subtly wrong by imitation.
///
/// Only programs that can observe a stack trace (<see cref="FindObservation"/>) pay for any of this.
/// </summary>
public sealed class StackTraceInfo
{
    private readonly SourceFileResolver _resolver;
    /// <summary>Non-hidden sequence points of every method, by PDB document; sorted by start.</summary>
    private readonly Dictionary<string, List<SequencePointSpan>> _points = new(StringComparer.Ordinal);
    /// <summary>Compiler-generated methods (lambdas, local functions), by document; sorted by first sequence point.</summary>
    private readonly Dictionary<string, List<(SequencePointSpan First, string Name)>> _synthesized = new(StringComparer.Ordinal);
    private readonly List<string> _methods = [];
    private readonly Dictionary<string, int> _methodIndex = new(StringComparer.Ordinal);
    private readonly List<string> _files = [];
    private readonly Dictionary<string, int> _fileIndex = new(StringComparer.Ordinal);
    private readonly List<(int Method, int File, int Line)> _sites = [];
    private readonly Dictionary<(int, int, int), int> _siteIndex = [];
    private readonly Dictionary<ISymbol, int> _frameOf = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<SyntaxNode, int> _frameOfSyntax = [];

    /// <summary>Release configuration (optimized IL): where CoreCLR reports some frames depends on it.</summary>
    public bool Release { get; }

    /// <summary>Frames print " in file:line N" (the build writes a PDB).</summary>
    private readonly bool _lines;

    /// <summary>csc's /pathmap: "from=to" pairs separated by commas (a literal comma or '=' is doubled).</summary>
    private static System.Collections.Immutable.ImmutableArray<KeyValuePair<string, string>> ParsePathMap(string? pathMap)
    {
        if (string.IsNullOrEmpty(pathMap)) return [];
        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();
        foreach (var pair in pathMap.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=', 2) is [var from, var to] && from.Length > 0)
                builder.Add(new(from, to));
        return builder.ToImmutable();
    }

    /// <summary>False when the program could not be emitted: lines then come from syntax, lambda names are approximated.</summary>
    public bool HasPdb { get; }

    public List<CompileError> Warnings { get; } = [];

    public int SiteCount => _sites.Count;

    private readonly record struct SequencePointSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
    {
        public bool Contains(SequencePointSpan other) =>
            (StartLine, StartColumn).CompareTo((other.StartLine, other.StartColumn)) <= 0
            && (EndLine, EndColumn).CompareTo((other.EndLine, other.EndColumn)) >= 0;
        public long Size => (EndLine - StartLine) * 100_000L + (EndColumn - StartColumn);
    }

    /// <param name="program">The user's program before csharpc's own rewrites (and with its corelib).</param>
    /// <param name="symbols">How .NET's build would compile it: optimized IL has other sequence points (and
    /// names), no PDB means frames without " in file:line N", PathMap rewrites the document paths.</param>
    public StackTraceInfo(CSharpCompilation program, DebugSymbolSettings symbols)
    {
        Release = symbols.Optimize;
        _lines = symbols.HasPdb;
        var baseDirectory = program.SyntaxTrees.Select(t => t.FilePath).FirstOrDefault(Path.IsPathRooted) is { } first
            ? Path.GetDirectoryName(first) : null;
        // What csc does in `dotnet build`: #line paths are resolved against the file containing them,
        // and PathMap applies to every document path.
        _resolver = new SourceFileResolver([], baseDirectory, ParsePathMap(symbols.PathMap));
        var user = program.RemoveSyntaxTrees(program.SyntaxTrees.Where(RoslynFrontend.IsCoreLibTree));
        // The PDB needs each document's encoding (csharpc parses from strings): the same text as UTF-8.
        foreach (var tree in user.SyntaxTrees.Where(t => t.Encoding is null).ToList())
            user = user.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(tree.GetText().ToString(), Encoding.UTF8)));
        user = user.WithOptions(user.Options
            .WithOptimizationLevel(Release ? OptimizationLevel.Release : OptimizationLevel.Debug)
            .WithSourceReferenceResolver(_resolver));
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        string? failure;
        try
        {
            var result = user.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            failure = result.Success ? null : result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.ToString() ?? "emit failed";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { failure = e.Message; }
        if (failure is not null)
        {
            Warnings.Add(new CompileError("CSLLVM0006",
                $"stack traces: the program could not be emitted as .NET would build it ({failure}), so lines are approximated and lambda frames are named '<lambda>'", null));
            return;
        }
        HasPdb = true;
        pe.Position = 0;
        pdb.Position = 0;
        ReadPdb(pe, pdb);
    }

    // ======================================================================================
    // A program given as IL: names and lines from its own assembly and PDB
    // ======================================================================================

    private readonly MetadataReader? _ilMetadata;
    /// <summary>Per method token: its sequence points (IL offset, document, start line; hidden ones as line -1), by offset.</summary>
    private readonly Dictionary<int, List<(int Offset, string? Document, int Line)>> _ilPoints = [];
    /// <summary>MoveNext token → (kickoff token, the kickoff is an iterator) for state machines csc generated.</summary>
    private readonly Dictionary<int, (int Kickoff, bool Iterator)> _ilKickoff = [];
    private readonly Dictionary<int, int> _ilFrames = [];

    /// <summary>
    /// Stack-trace metadata of a program compiled from its main assembly (ADR 0010, "The program as IL"):
    /// frame names from the assembly's metadata (the names csc chose, as .NET prints them), lines from its
    /// portable PDB (next to it or embedded), as System.Diagnostics.StackTrace reads them: the last
    /// non-hidden sequence point at or before the frame's IL offset. Without a PDB, frames have no location.
    /// </summary>
    public StackTraceInfo(Frontend.Il.IlAssembly program)
    {
        _resolver = new SourceFileResolver([], null);
        _ilMetadata = program.Reader;
        Release = !DisablesOptimizations(program.Reader);
        MetadataReaderProvider? provider = null;
        try
        {
            var pdbPath = Path.ChangeExtension(program.Path, ".pdb");
            if (program.Pe.ReadDebugDirectory().FirstOrDefault(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb) is { DataSize: > 0 } embedded)
                provider = program.Pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
            else if (File.Exists(pdbPath))
                provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
            if (provider is not null) ReadIlPdb(provider.GetMetadataReader());
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or InvalidOperationException)
        {
            Warnings.Add(new CompileError("CSLLVM0006", $"stack traces: the PDB of {program.Name}.dll could not be read ({e.Message}); frames have no file and line", null));
            _ilPoints.Clear();
            provider = null;
        }
        finally { provider?.Dispose(); }
        _lines = HasPdb = provider is not null;
    }

    private void ReadIlPdb(MetadataReader pdb)
    {
        var documents = new Dictionary<DocumentHandle, string>();
        foreach (var handle in _ilMetadata!.MethodDefinitions)
        {
            var info = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
            var token = MetadataTokens.GetToken(handle);
            List<(int, string?, int)> points = [];
            foreach (var sp in info.GetSequencePoints())
            {
                if (sp.IsHidden) { points.Add((sp.Offset, null, -1)); continue; }
                if (!documents.TryGetValue(sp.Document, out var name)) documents[sp.Document] = name = pdb.GetString(pdb.GetDocument(sp.Document).Name);
                points.Add((sp.Offset, name, sp.StartLine));
            }
            if (points.Count > 0) _ilPoints[token] = points;
            if (info.GetStateMachineKickoffMethod() is { IsNil: false } kickoff)
            {
                var kickoffMethod = _ilMetadata.GetMethodDefinition(kickoff);
                var iterator = kickoffMethod.GetCustomAttributes().Any(a => AttributeName(_ilMetadata, a) is "IteratorStateMachineAttribute" or "AsyncIteratorStateMachineAttribute");
                _ilKickoff[token] = (MetadataTokens.GetToken(kickoff), iterator);
            }
        }
    }

    /// <summary>[assembly: Debuggable(... DisableOptimizations ...)]: csc's Debug configuration.</summary>
    private static bool DisablesOptimizations(MetadataReader md)
    {
        foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            if (AttributeName(md, handle) != "DebuggableAttribute") continue;
            var blob = md.GetBlobReader(md.GetCustomAttribute(handle).Value);
            if (blob.Length < 4 || blob.ReadUInt16() != 1) return false;
            // Debuggable(DebuggingModes) or Debuggable(bool isJITTrackingEnabled, bool isJITOptimizerDisabled).
            if (blob.RemainingBytes >= 6) return (blob.ReadInt32() & 0x100) != 0;
            blob.ReadBoolean();
            return blob.ReadBoolean();
        }
        return false;
    }

    private static string? AttributeName(MetadataReader md, CustomAttributeHandle handle)
    {
        var ctor = md.GetCustomAttribute(handle).Constructor;
        var type = ctor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)ctor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType(),
            _ => default,
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)type).Name),
            HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)type).Name),
            _ => null,
        };
    }

    /// <summary>The frame of a method of the IL program: its metadata name; a state machine's MoveNext is its kickoff method (+MoveNext() for iterators).</summary>
    public int IlFrameOf(IMethodSymbol method)
    {
        var token = method.OriginalDefinition.MetadataToken;
        if (_ilFrames.TryGetValue(token, out var known)) return known;
        var md = _ilMetadata!;
        string name;
        if (_ilKickoff.TryGetValue(token, out var kickoff))
            name = FormatMethod(md, MetadataTokens.MethodDefinitionHandle(kickoff.Kickoff & 0xFFFFFF)) + (kickoff.Iterator ? "+MoveNext()" : "");
        else name = FormatMethod(md, MetadataTokens.MethodDefinitionHandle(token & 0xFFFFFF));
        return _ilFrames[token] = Intern(_methods, _methodIndex, name);
    }

    /// <summary>The site of the first non-hidden sequence point at or after <paramref name="fromOffset"/> (CoreCLR's parent frame of a finally funclet).</summary>
    public int IlFirstSite(int frame, IMethodSymbol method, int fromOffset)
    {
        if (_lines && _ilPoints.TryGetValue(method.OriginalDefinition.MetadataToken, out var points))
            foreach (var (offset, _, startLine) in points)
                if (offset >= fromOffset && startLine >= 0) return IlSite(frame, method, offset);
        return IlSite(frame, method, fromOffset);
    }

    /// <summary>The site of IL offset <paramref name="ilOffset"/> in a method of the IL program.</summary>
    public int IlSite(int frame, IMethodSymbol method, int ilOffset)
    {
        string? file = null;
        var line = 0;
        if (_lines && _ilPoints.TryGetValue(method.OriginalDefinition.MetadataToken, out var points))
            foreach (var (offset, document, startLine) in points)
            {
                if (offset > ilOffset) break;
                if (startLine >= 0) (file, line) = (document, startLine);
            }
        var key = (frame, file is null ? -1 : Intern(_files, _fileIndex, file), file is null ? 0 : line);
        if (_siteIndex.TryGetValue(key, out var site)) return site;
        _sites.Add(key);
        return _siteIndex[key] = _sites.Count - 1;
    }

    /// <summary>
    /// Whether a program given as IL may turn an exception into text: it names an exception type or
    /// System.Diagnostics.StackTrace (catch clauses, `new`, ToString, StackTrace all do).
    /// </summary>
    public static bool IlMayObserve(MetadataReader md)
    {
        foreach (var handle in md.TypeReferences)
        {
            var name = md.GetString(md.GetTypeReference(handle).Name);
            if (name.EndsWith("Exception", StringComparison.Ordinal) || name == "StackTrace") return true;
        }
        return false;
    }

    // ======================================================================================
    // Does the program look at stack traces?
    // ======================================================================================

    /// <summary>
    /// The first place where the program turns an exception into text (<c>ToString()</c>, string
    /// interpolation or concatenation, passing it as <c>object</c>, e.g. to Console.WriteLine) or
    /// reads <c>StackTrace</c>; null when it never does. Stack traces cost code size (a landing pad per
    /// call site), so only such programs record them. Exceptions that only ever reach the
    /// unhandled-exception report (stderr) are not looked for.
    /// </summary>
    public static Location? FindObservation(CSharpCompilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (RoslynFrontend.IsCoreLibTree(tree)) continue;
            var text = tree.GetText().ToString();
            if (!text.Contains("StackTrace", StringComparison.Ordinal) && !text.Contains("Exception", StringComparison.Ordinal)
                && !text.Contains("catch", StringComparison.Ordinal))
                continue;
            SemanticModel? model = null;
            bool IsException(ExpressionSyntax expression)
            {
                model ??= compilation.GetSemanticModel(tree);
                return DerivesFromException(model.GetTypeInfo(expression).Type);
            }
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                var found = node switch
                {
                    MemberAccessExpressionSyntax { Name.Identifier.ValueText: "StackTrace" or "ToString" } access => IsException(access.Expression),
                    ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax or InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax } } conditional
                        when BoundMember(conditional.WhenNotNull) is "StackTrace" or "ToString" => IsException(conditional.Expression),
                    InterpolationSyntax interpolation => IsException(interpolation.Expression),
                    BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } add => IsException(add.Left) || IsException(add.Right),
                    ArgumentSyntax argument when IsException(argument.Expression) =>
                        (model!.GetOperation(argument) as IArgumentOperation)?.Parameter?.Type is { SpecialType: SpecialType.System_Object }
                        || model.GetOperation(argument) is null,
                    _ => false,
                };
                if (found) return node.GetLocation();
            }
        }
        return null;
    }

    private static string? BoundMember(ExpressionSyntax whenNotNull) => whenNotNull switch
    {
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } => binding.Name.Identifier.ValueText,
        _ => null,
    };

    private static bool DerivesFromException(ITypeSymbol? type)
    {
        for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
            if (t.MetadataName == "Exception" && t.ContainingNamespace?.ToDisplayString() == "System") return true;
        return false;
    }

    // ======================================================================================
    // Frames and sites
    // ======================================================================================

    /// <summary>
    /// The frame index of a function csharpc lowers from the program's source (a method, accessor,
    /// constructor, lambda or local function), or -1 for code .NET wouldn't list (the corelib,
    /// synthesized helpers).
    /// </summary>
    public int FrameOf(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        if (_frameOf.TryGetValue(definition, out var known)) return known;
        var frame = -1;
        var syntax = (definition.PartialImplementationPart ?? definition).DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is not null && !RoslynFrontend.IsCoreLibTree(syntax.SyntaxTree)
            && !syntax.HasAnnotation(MethodGroupRewriter.Synthesized))
        {
            string? name = definition.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction
                ? SynthesizedName(syntax)
                : FormatMethod(definition);
            if (name is not null)
            {
                if (Coroutines.KindOf(definition) is CoroutineKind.Iterator or CoroutineKind.AsyncIterator) name += "+MoveNext()";
                frame = Intern(_methods, _methodIndex, name);
            }
        }
        else if (syntax is null && definition.MethodKind == MethodKind.Constructor && definition.IsImplicitlyDeclared
                 && definition.ContainingType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is { } typeSyntax
                 && !RoslynFrontend.IsCoreLibTree(typeSyntax.SyntaxTree))
            frame = Intern(_methods, _methodIndex, FormatMethod(definition)); // runs the field initializers
        return _frameOf[definition] = frame;
    }

    /// <summary>
    /// The site table index of a call at <paramref name="node"/> (at its last token: <paramref name="atEnd"/>)
    /// in frame <paramref name="frame"/>.
    /// </summary>
    public int Site(int frame, SyntaxNode node, bool atEnd = false)
    {
        var (file, line) = _lines ? Locate(node, atEnd) : (null, 0);
        var key = (frame, file is null ? -1 : Intern(_files, _fileIndex, file), line);
        if (_siteIndex.TryGetValue(key, out var site)) return site;
        _sites.Add(key);
        return _siteIndex[key] = _sites.Count - 1;
    }

    /// <summary>A frame of .NET's library code (no source location), for CoreIntrinsics.LibraryFrames.</summary>
    public int LibrarySite(string frame)
    {
        var key = (Intern(_methods, _methodIndex, frame), -1, 0);
        if (_siteIndex.TryGetValue(key, out var site)) return site;
        _sites.Add(key);
        return _siteIndex[key] = _sites.Count - 1;
    }

    private static int Intern(List<string> list, Dictionary<string, int> index, string value)
    {
        if (index.TryGetValue(value, out var i)) return i;
        list.Add(value);
        return index[value] = list.Count - 1;
    }

    /// <summary>The document and line .NET reports for code at <paramref name="node"/>.</summary>
    private (string? File, int Line) Locate(SyntaxNode node, bool atEnd)
    {
        var (tree, span) = MethodGroupRewriter.Original(node.SyntaxTree, atEnd ? node.GetLastToken().Span : node.Span);
        var mapped = tree.GetMappedLineSpan(span);
        var document = Document(tree, mapped);
        if (HasPdb && document is not null && _points.TryGetValue(document, out var points))
        {
            var query = Span(mapped);
            if (Innermost(points, query) is { } point) return (document, point.StartLine);
        }
        // No sequence point covers it (implicit calls of using/foreach, or no PDB): the statement's line.
        var statement = node.AncestorsAndSelf().FirstOrDefault(n => n is StatementSyntax or ArrowExpressionClauseSyntax
            or LambdaExpressionSyntax or EqualsValueClauseSyntax or ConstructorInitializerSyntax) ?? node;
        var (statementTree, statementSpan) = MethodGroupRewriter.Original(statement.SyntaxTree, statement.Span);
        var start = statementTree.GetMappedLineSpan(new TextSpan(statementSpan.Start, 0));
        return (Document(statementTree, start), start.StartLinePosition.Line + 1);
    }

    private string? Document(SyntaxTree tree, FileLinePositionSpan mapped)
    {
        if (!mapped.HasMappedPath) return tree.FilePath is { Length: > 0 } path ? path : null;
        return _resolver.NormalizePath(mapped.Path, tree.FilePath) ?? mapped.Path;
    }

    private static SequencePointSpan Span(FileLinePositionSpan mapped) => new(
        mapped.StartLinePosition.Line + 1, mapped.StartLinePosition.Character + 1,
        mapped.EndLinePosition.Line + 1, mapped.EndLinePosition.Character + 1);

    /// <summary>The smallest sequence point containing <paramref name="query"/>; else the one containing its start.</summary>
    private static SequencePointSpan? Innermost(List<SequencePointSpan> points, SequencePointSpan query)
    {
        SequencePointSpan? best = null, atStart = null;
        var startOnly = query with { EndLine = query.StartLine, EndColumn = query.StartColumn };
        // Points are sorted by start; candidates start at or before the query.
        var hi = UpperBound(points, query);
        for (var i = hi - 1; i >= 0 && points[i].StartLine >= query.StartLine - 500; i--)
        {
            var p = points[i];
            if (p.Contains(query) && (best is null || p.Size < best.Value.Size)) best = p;
            else if (best is null && p.Contains(startOnly) && (atStart is null || p.Size < atStart.Value.Size)) atStart = p;
        }
        return best ?? atStart;
    }

    private static int UpperBound(List<SequencePointSpan> points, SequencePointSpan query)
    {
        int lo = 0, hi = points.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if ((points[mid].StartLine, points[mid].StartColumn).CompareTo((query.StartLine, query.StartColumn)) <= 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>The metadata name Roslyn gave a lambda or local function: the generated method whose body starts first inside it.</summary>
    private string? SynthesizedName(SyntaxNode syntax)
    {
        if (!HasPdb) return null;
        var (tree, span) = MethodGroupRewriter.Original(syntax.SyntaxTree, syntax.Span);
        var mapped = tree.GetMappedLineSpan(span);
        if (Document(tree, mapped) is not { } document || !_synthesized.TryGetValue(document, out var methods)) return null;
        var range = Span(mapped);
        foreach (var (first, name) in methods)
            if (range.Contains(first)) return name; // sorted: the first one inside is the outermost
        return null;
    }

    // ======================================================================================
    // .NET's frame text: Namespace.Type.Method[T](Int32 x, String& s)
    // ======================================================================================

    /// <summary>A method as System.Diagnostics.StackTrace prints it (nested types joined with '.').</summary>
    public static string FormatMethod(IMethodSymbol method)
    {
        var sb = new StringBuilder();
        var type = method.ContainingType;
        List<IParameterSymbol> parameters = [.. method.Parameters];
        if (type.TypeKind == TypeKind.Extension)
        {
            // C# 14 extension members are static methods of the enclosing class, the receiver first.
            if (!method.IsStatic && type.ExtensionParameter is { } receiver) parameters.Insert(0, receiver);
            type = type.ContainingType;
        }
        AppendTypeName(sb, type);
        sb.Append('.').Append(method.MetadataName);
        if (method.IsGenericMethod) sb.Append('[').AppendJoin(',', method.TypeParameters.Select(t => t.Name)).Append(']');
        sb.Append('(');
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (i > 0) sb.Append(", ");
            sb.Append(TypeName(p.Type));
            if (p.RefKind != RefKind.None) sb.Append('&');
            if (p.Name.Length > 0) sb.Append(' ').Append(p.Name);
        }
        return sb.Append(')').ToString();
    }

    private static void AppendTypeName(StringBuilder sb, INamedTypeSymbol type)
    {
        if (type.ContainingType is { } outer) AppendTypeName(sb, outer);
        else if (type.ContainingNamespace is { IsGlobalNamespace: false } ns) sb.Append(ns.ToDisplayString());
        if (sb.Length > 0) sb.Append('.');
        sb.Append(type.MetadataName);
    }

    /// <summary>System.Type.Name: no namespace, generic arity instead of arguments (List`1).</summary>
    private static string TypeName(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => TypeName(array.ElementType) + (array.Rank == 1 ? "[]" : "[" + new string(',', array.Rank - 1) + "]"),
        IPointerTypeSymbol pointer => TypeName(pointer.PointedAtType) + "*",
        IFunctionPointerTypeSymbol => "", // what Type.Name says for a function pointer type
        ITypeParameterSymbol parameter => parameter.Name,
        IDynamicTypeSymbol => "Object",
        INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying } => underlying.MetadataName,
        _ => type.MetadataName,
    };

    // ======================================================================================
    // Reading the PDB
    // ======================================================================================

    private void ReadPdb(Stream peStream, Stream pdbStream)
    {
        using var peReader = new PEReader(peStream);
        using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
        var md = peReader.GetMetadataReader();
        var pdb = pdbProvider.GetMetadataReader();
        var documents = new Dictionary<DocumentHandle, string>();
        string DocumentName(DocumentHandle h) =>
            documents.TryGetValue(h, out var name) ? name : documents[h] = pdb.GetString(pdb.GetDocument(h).Name);

        foreach (var handle in md.MethodDefinitions)
        {
            var info = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
            SequencePointSpan? first = null;
            string? firstDocument = null;
            foreach (var sp in info.GetSequencePoints())
            {
                if (sp.IsHidden) continue;
                var name = DocumentName(sp.Document);
                if (!_points.TryGetValue(name, out var list)) _points[name] = list = [];
                var span = new SequencePointSpan(sp.StartLine, sp.StartColumn, sp.EndLine, sp.EndColumn);
                list.Add(span);
                if (first is null) { first = span; firstDocument = name; }
            }
            if (first is null) continue;
            // A state machine's MoveNext carries the body; the frame is named after its kickoff method.
            var named = info.GetStateMachineKickoffMethod() is { IsNil: false } kickoff ? kickoff : handle;
            if (!IsCompilerGenerated(md, named)) continue;
            if (!_synthesized.TryGetValue(firstDocument!, out var methods)) _synthesized[firstDocument!] = methods = [];
            methods.Add((first.Value, FormatMethod(md, named)));
        }
        static int Compare(SequencePointSpan a, SequencePointSpan b) =>
            (a.StartLine, a.StartColumn).CompareTo((b.StartLine, b.StartColumn));
        foreach (var list in _points.Values) list.Sort(Compare);
        foreach (var list in _synthesized.Values) list.Sort((a, b) => Compare(a.First, b.First));
    }

    private static bool IsCompilerGenerated(MetadataReader md, MethodDefinitionHandle handle)
    {
        var method = md.GetMethodDefinition(handle);
        if (md.GetString(method.Name).StartsWith('<')) return true;
        for (var type = method.GetDeclaringType(); !type.IsNil; type = md.GetTypeDefinition(type).GetDeclaringType())
            if (md.GetString(md.GetTypeDefinition(type).Name).StartsWith('<')) return true;
        return false;
    }

    private static string FormatMethod(MetadataReader md, MethodDefinitionHandle handle)
    {
        var method = md.GetMethodDefinition(handle);
        var sb = new StringBuilder();
        var declaring = method.GetDeclaringType();
        List<string> typeNames = [];
        var outermost = declaring;
        for (var t = declaring; !t.IsNil; t = md.GetTypeDefinition(t).GetDeclaringType())
        {
            typeNames.Insert(0, md.GetString(md.GetTypeDefinition(t).Name));
            outermost = t;
        }
        var ns = md.GetString(md.GetTypeDefinition(outermost).Namespace);
        if (ns.Length > 0) sb.Append(ns).Append('.');
        sb.AppendJoin('.', typeNames).Append('.').Append(md.GetString(method.Name));
        var typeParameters = md.GetTypeDefinition(declaring).GetGenericParameters().Select(p => md.GetString(md.GetGenericParameter(p).Name)).ToArray();
        var methodParameters = method.GetGenericParameters().Select(p => md.GetString(md.GetGenericParameter(p).Name)).ToArray();
        if (methodParameters.Length > 0) sb.Append('[').AppendJoin(',', methodParameters).Append(']');
        var signature = method.DecodeSignature(new TypeNameProvider(), (typeParameters, methodParameters));
        var names = new string[signature.ParameterTypes.Length];
        foreach (var p in method.GetParameters())
        {
            var parameter = md.GetParameter(p);
            if (parameter.SequenceNumber > 0 && parameter.SequenceNumber <= names.Length) names[parameter.SequenceNumber - 1] = md.GetString(parameter.Name);
        }
        sb.Append('(');
        for (var i = 0; i < names.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(signature.ParameterTypes[i]);
            if (names[i] is { Length: > 0 } name) sb.Append(' ').Append(name);
        }
        return sb.Append(')').ToString();
    }

    /// <summary>Decodes signature types to System.Type.Name text.</summary>
    private sealed class TypeNameProvider : ISignatureTypeProvider<string, (string[] Type, string[] Method)>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);
        public string GetTypeFromSpecification(MetadataReader reader, (string[] Type, string[] Method) context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) => genericType;
        public string GetGenericTypeParameter((string[] Type, string[] Method) context, int index) => index < context.Type.Length ? context.Type[index] : "T" + index;
        public string GetGenericMethodParameter((string[] Type, string[] Method) context, int index) => index < context.Method.Length ? context.Method[index] : "M" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "";
    }

    // ======================================================================================
    // Tables for the runtime (csrt_TraceTables in src/runtime/eh.c)
    // ======================================================================================

    private readonly Dictionary<int, LLVMValueRef> _descriptors = [];

    /// <summary>The csrt_TraceSite magic (src/runtime/eh.c): not a canonical address, so no method table starts with it.</summary>
    private const ulong SiteMagic = 0xC5A7AC5E7A1E5175UL;

    /// <summary>
    /// A site's descriptor { magic, method, file, line }, used as the type info of the site's catch
    /// clause: the personality records it during the search phase (src/runtime/eh.c). One global per
    /// site, because LLVM's LSDA only takes globals as type infos.
    /// </summary>
    public LLVMValueRef SiteDescriptor(ModuleCodeGen m, int site)
    {
        if (_descriptors.TryGetValue(site, out var known)) return known;
        var l = m.Llvm;
        var (method, file, line) = _sites[site];
        var init = l.ConstStruct(l.ConstUInt(l.I64, SiteMagic), l.ConstInt(l.I32, method), l.ConstInt(l.I32, file),
            l.ConstInt(l.I32, line), l.ConstInt(l.I32, 0));
        var global = m.Module.AddGlobal($"csharpc.trace.site.{site}", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        return _descriptors[site] = global;
    }

    /// <summary>{ methods, files } (csrt_TraceTables): the strings the descriptors index.</summary>
    public LLVMValueRef EmitTables(ModuleCodeGen m)
    {
        var l = m.Llvm;
        var methods = LlvmContext.ConstArray(l.Ptr, [.. _methods.Select(m.CString)]);
        var files = LlvmContext.ConstArray(l.Ptr, [.. _files.Select(m.CString)]);
        LLVMValueRef Global(string name, LLVMValueRef init) =>
            m.Module.AddGlobal(name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        return l.ConstStruct(Global("csharpc.trace.methods", methods), Global("csharpc.trace.files", files));
    }
}
