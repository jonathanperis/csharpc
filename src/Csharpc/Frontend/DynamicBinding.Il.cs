using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.Frontend;

/// <summary>
/// `dynamic` in assemblies compiled from IL (docs/adr/0019-dynamic.md, "From IL"). csharpc binds a dynamic
/// operation speculatively with Roslyn, at a position in source; an IL call site has none. This tree gives it
/// one: an empty method of the binding compilation, bound by a semantic model that ignores accessibility —
/// codegen then checks every member a binding chose against the call site's real context type
/// (ModuleCodeGen.DynamicIl.cs). It is added only when an IL assembly uses Microsoft.CSharp's binder, so
/// programs without dynamic call sites in IL compile exactly as before.
/// </summary>
internal static class IlDynamicBinding
{
    public const string TreePath = "<csharpc-dynamic-il>.g.cs";
    private const string Text = "namespace CsharpcDynamicIl { internal static class Binding { internal static void Bind() { } } }";

    /// <summary>A position inside the method's body: where an IL call site's operations are bound.</summary>
    public static int Position(SyntaxTree tree) => tree.GetText().ToString().IndexOf("{ }", StringComparison.Ordinal) + 1;

    /// <summary>Does any IL assembly (other than the precompiled corelib) create Microsoft.CSharp binder call sites?</summary>
    public static bool Needed(IlAssemblySet? il)
    {
        if (il is null) return false;
        foreach (var assembly in il.Assemblies)
        {
            if (assembly.IsCoreLib) continue;
            var reader = assembly.Reader;
            foreach (var handle in reader.TypeReferences)
            {
                var reference = reader.GetTypeReference(handle);
                if (reader.StringComparer.Equals(reference.Name, "Binder") && reader.StringComparer.Equals(reference.Namespace, "Microsoft.CSharp.RuntimeBinder"))
                    return true;
            }
        }
        return false;
    }

    public static SyntaxTree Tree(CSharpParseOptions options) => CSharpSyntaxTree.ParseText(Text, options, path: TreePath);
}
