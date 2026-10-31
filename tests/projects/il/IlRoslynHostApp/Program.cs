using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The snippet brings its own core library, so the compilation needs no reference assemblies and the
// result does not depend on the machine (or on where the framework lives).
const string CoreLib = """
namespace System
{
    public class Object { public virtual string ToString() => null; }
    public abstract class ValueType { }
    public struct Void { }
    public struct Boolean { }
    public struct Char { }
    public struct Int32 { }
    public struct Int64 { }
    public sealed class String { public int Length => 0; }
    public abstract class Enum : ValueType { }
    public abstract class Array { }
    public class Attribute { }
    public abstract class Delegate { }
    public abstract class MulticastDelegate : Delegate { }
    public struct IntPtr { }
    public struct RuntimeTypeHandle { }
    public struct RuntimeMethodHandle { }
    public struct RuntimeFieldHandle { }
    public class Exception { }
    public enum AttributeTargets { All = 32767 }
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn) { }
        public bool AllowMultiple { get => false; set { } }
        public bool Inherited { get => false; set { } }
    }
}
namespace System.Collections
{
    public interface IEnumerable { IEnumerator GetEnumerator(); }
    public interface IEnumerator { object Current { get; } bool MoveNext(); }
}
""";

const string Source = """
namespace Demo
{
    public enum Color { Red, Green = 5, Blue }

    public interface IShape { int Area(); }

    public sealed class Rect : IShape
    {
        private readonly int _w, _h;
        public Rect(int w, int h) { _w = w; _h = h; }
        public int Area() => _w * _h;
        public const string Name = "rect";
    }

    public static class Calc
    {
        public const int Answer = 6 * 7;
        public static long Fib(int n) { long a = 0, b = 1; for (int i = 0; i < n; i++) { long t = a + b; a = b; b = t; } return a; }
        public static int Sum(int[] xs) { int s = 0; foreach (var x in xs) s += x; return s; }
    }
}
""";

const string Broken = """
namespace Demo
{
    public static class Oops
    {
        public static int F(int x) { return x + undefinedName; }
        public static void G() { int unused = 1 }
        public static string H() => 42;
    }
}
""";

var parse = new CSharpParseOptions(LanguageVersion.CSharp12);

// 1. Syntax: parse, walk, normalize.
var tree = CSharpSyntaxTree.ParseText(Source, parse, path: "Source.cs");
var root = (CompilationUnitSyntax)tree.GetRoot();
Console.WriteLine($"nodes: {root.DescendantNodes().Count()}, tokens: {root.DescendantTokens().Count()}, trivia: {root.DescendantTrivia().Count()}");
foreach (var g in root.DescendantNodes().GroupBy(n => n.Kind()).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.ToString(), StringComparer.Ordinal).Take(8))
    Console.WriteLine($"  {g.Key}: {g.Count()}");
var fib = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.ValueText == "Fib");
Console.WriteLine(fib.NormalizeWhitespace().ToFullString());
var span = fib.GetLocation().GetLineSpan();
Console.WriteLine($"Fib at {span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})");

// A syntax rewrite: every integer literal doubled.
var doubled = new DoubleLiterals().Visit(fib);
Console.WriteLine(doubled.NormalizeWhitespace().ToFullString());

// 2. Binding: a compilation over the core library and the source.
var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true, optimizationLevel: OptimizationLevel.Release);
var core = CSharpSyntaxTree.ParseText(CoreLib, parse, path: "CoreLib.cs");
var compilation = CSharpCompilation.Create("Demo", [core, tree], [], options);
PrintDiagnostics("Demo", compilation.GetDiagnostics());

var model = compilation.GetSemanticModel(tree);
foreach (var decl in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
    foreach (var v in decl.Declaration.Variables)
    {
        var field = (IFieldSymbol)model.GetDeclaredSymbol(v)!;
        Console.WriteLine($"field {field.ToDisplayString()}: const={field.IsConst} value={field.ConstantValue ?? "-"} type={field.Type.ToDisplayString()}");
    }
foreach (var type in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type).OfType<INamedTypeSymbol>()
             .Where(t => t.ContainingNamespace.Name == "Demo").OrderBy(t => t.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"{type.TypeKind} {type.ToDisplayString()} : {type.BaseType?.ToDisplayString() ?? "-"} [{string.Join(", ", type.AllInterfaces.Select(i => i.Name))}]");
    foreach (var m in type.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.ToDisplayString(), StringComparer.Ordinal))
        Console.WriteLine($"  {m.Kind} {m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}");
}
foreach (var bin in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
{
    var c = model.GetConstantValue(bin);
    Console.WriteLine($"{bin} : {model.GetTypeInfo(bin).Type?.ToDisplayString()} const={(c.HasValue ? c.Value : "-")}");
}

// 3. Emit, deterministically, and read the image back.
using var pe = new MemoryStream();
var result = compilation.Emit(pe);
Console.WriteLine($"emit: {result.Success}, {pe.Length > 0}");
PrintDiagnostics("Emit", result.Diagnostics);
pe.Position = 0;
using (var reader = new PEReader(pe, PEStreamOptions.LeaveOpen))
{
    var md = reader.GetMetadataReader();
    var asm = md.GetAssemblyDefinition();
    Console.WriteLine($"assembly {md.GetString(asm.Name)} {asm.Version}, mvid {md.GetGuid(md.GetModuleDefinition().Mvid)}");
    Console.WriteLine($"types {md.TypeDefinitions.Count}, methods {md.MethodDefinitions.Count}, fields {md.FieldDefinitions.Count}, user strings {md.GetHeapSize(HeapIndex.UserString)} bytes, blobs {md.GetHeapSize(HeapIndex.Blob)} bytes");
    foreach (var h in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(h);
        if (md.GetString(t.Namespace) != "Demo") continue;
        Console.WriteLine($"  {md.GetString(t.Name)} {t.Attributes}");
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            var body = m.RelativeVirtualAddress == 0 ? null : reader.GetMethodBody(m.RelativeVirtualAddress);
            Console.WriteLine($"    {md.GetString(m.Name)} il={body?.GetILBytes()?.Length ?? 0} maxstack={body?.MaxStack ?? 0}");
        }
    }
}
var bytes = pe.ToArray();
uint hash = 2166136261;
foreach (var b in bytes) hash = (hash ^ b) * 16777619;
Console.WriteLine($"image {bytes.Length} bytes, fnv1a {hash:x8}");

// 4. Diagnostics of broken code: ids, locations and the messages from Roslyn's resources.
var broken = CSharpCompilation.Create("Broken", [core, CSharpSyntaxTree.ParseText(Broken, parse, path: "Broken.cs")], [], options);
PrintDiagnostics("Broken", broken.GetDiagnostics());
using var sink = new MemoryStream();
var failed = broken.Emit(sink);
Console.WriteLine($"emit: {failed.Success}, {failed.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} errors");

static void PrintDiagnostics(string name, ImmutableArray<Diagnostic> diagnostics)
{
    Console.WriteLine($"{name}: {diagnostics.Length} diagnostics");
    foreach (var d in diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ThenBy(d => d.Id, StringComparer.Ordinal))
        Console.WriteLine($"  {d}");
}

sealed class DoubleLiterals : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node) =>
        node.IsKind(SyntaxKind.NumericLiteralExpression) && node.Token.Value is int i
            ? SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(i * 2))
            : base.VisitLiteralExpression(node);
}
