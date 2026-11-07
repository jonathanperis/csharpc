using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.Frontend;

/// <summary>
/// The attribute classes csc embeds into an assembly that has nullable annotations (`[Nullable]`, `[NullableContext]`,
/// internal, in System.Runtime.CompilerServices), so that reflection can list them on source members as the JIT does
/// (ModuleCodeGen.NullableAttributes.cs decides where csc puts them). The reference assemblies don't have these types,
/// and Roslyn only synthesizes them when emitting. Added only to a compilation that has an annotations context
/// somewhere (the project's `Nullable` setting or a `#nullable` directive), and not when the program declares them
/// itself (netstandard shims do): programs without nullable annotations compile exactly as before.
/// </summary>
internal static class NullableAttributeDeclarations
{
    public const string TreePath = "<csharpc-nullable-attributes>.g.cs";
    public const string NullableName = "System.Runtime.CompilerServices.NullableAttribute";
    public const string NullableContextName = "System.Runtime.CompilerServices.NullableContextAttribute";

    private const string Text = """
        namespace System.Runtime.CompilerServices
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct | global::System.AttributeTargets.Method
                | global::System.AttributeTargets.Interface | global::System.AttributeTargets.ReturnValue | global::System.AttributeTargets.GenericParameter
                | global::System.AttributeTargets.Parameter | global::System.AttributeTargets.Field | global::System.AttributeTargets.Event
                | global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
            internal sealed class NullableAttribute : global::System.Attribute
            {
                public readonly byte[] NullableFlags;
                public NullableAttribute(byte value) { NullableFlags = new byte[] { value }; }
                public NullableAttribute(byte[] value) { NullableFlags = value; }
            }

            [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Struct | global::System.AttributeTargets.Method
                | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Constructor | global::System.AttributeTargets.Property
                | global::System.AttributeTargets.Field | global::System.AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
            internal sealed class NullableContextAttribute : global::System.Attribute
            {
                public readonly byte Flag;
                public NullableContextAttribute(byte value) { Flag = value; }
            }
        }
        """;

    /// <summary>
    /// Adds the declarations when the program has an annotations context (<paramref name="projectNullable"/> turns it on for
    /// every file, `#nullable enable` per file) and something may list custom attributes: the program's own code asks for
    /// them, or it uses libraries (which do, e.g. dependency injection and serializers). Every program type is in the
    /// run-time type table, so the two classes would cost the small programs bytes for nothing.
    /// </summary>
    public static CSharpCompilation Add(CSharpCompilation compilation, CSharpParseOptions options, IEnumerable<SyntaxTree> programTrees,
        NullableContextOptions projectNullable, bool usesLibraries)
    {
        var texts = programTrees.Select(t => t.GetText().ToString()).ToList();
        var annotations = (projectNullable & NullableContextOptions.Annotations) != 0 || texts.Any(t => t.Contains("#nullable", StringComparison.Ordinal));
        if (!annotations) return compilation;
        var listsAttributes = usesLibraries || texts.Any(t => t.Contains("CustomAttribute", StringComparison.Ordinal) || t.Contains("IsDefined", StringComparison.Ordinal));
        if (!listsAttributes) return compilation;
        if (compilation.Assembly.GetTypeByMetadataName(NullableName) is not null || compilation.Assembly.GetTypeByMetadataName(NullableContextName) is not null) return compilation;
        return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Text, options, path: TreePath));
    }
}
