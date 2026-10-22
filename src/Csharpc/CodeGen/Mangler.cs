using System.Text;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Symbol mangling, loosely modelled on the Itanium C++ ABI and Rust's v0 scheme
/// (docs/03-lowering.md#symbol-mangling):
///
///   _CN &lt;len&gt;&lt;ns&gt;... &lt;len&gt;&lt;type&gt;... &lt;len&gt;&lt;member&gt; E &lt;param-types&gt;
///
///   Program.Fib(int)          =>  _CN7Program3FibEi
///   Geo.Shape.Area()          =>  _CN3Geo5Shape4AreaEv
///   Geo.Circle..ctor(double)  =>  _CN3Geo6Circle4ctorEd
///
/// Names are length-prefixed, so compiler-generated names like "&lt;Main&gt;$" need no escaping.
/// </summary>
public static class Mangler
{
    public static string Method(IMethodSymbol method)
    {
        var sb = new StringBuilder("_CN");
        AppendPath(sb, method.ContainingSymbol);
        var name = method.MethodKind switch
        {
            MethodKind.Constructor => "ctor",
            MethodKind.StaticConstructor => "cctor",
            _ => method.Name,
        };
        AppendName(sb, name);
        if (method.IsGenericMethod) AppendTypeArguments(sb, method.TypeArguments);
        sb.Append('E');
        if (method.Parameters.IsEmpty) sb.Append('v');
        foreach (var p in method.Parameters)
        {
            if (p.RefKind != RefKind.None) sb.Append('R');
            AppendType(sb, p.Type);
        }
        return sb.ToString();
    }

    public static string StaticField(IFieldSymbol field)
    {
        var sb = new StringBuilder("_CN");
        AppendPath(sb, field.ContainingType);
        AppendName(sb, field.Name);
        return sb.Append('E').ToString();
    }

    public static string TypeSymbol(INamedTypeSymbol type, string suffix)
    {
        var sb = new StringBuilder("_CN");
        AppendPath(sb, type);
        AppendName(sb, suffix);
        return sb.Append('E').ToString();
    }

    /// <summary>
    /// CLR-style metadata name ("Ns.Outer+Inner", "Box`1[System.Int32]"), used for Type.FullName /
    /// default ToString().
    /// </summary>
    /// <summary>Set by codegen: the BCL name a corelib shadow type stands for (CoreLib.RealNameOf).</summary>
    public static Func<INamedTypeSymbol, string?>? RealNameOf { get; set; }

    public static string MetadataName(INamedTypeSymbol type)
    {
        var name = type.MetadataName;
        if (RealNameOf?.Invoke(type) is { } real) name = real;
        else if (type.ContainingType is { } outer) name = MetadataName(outer) + "+" + name;
        else if (type.ContainingNamespace is { IsGlobalNamespace: false } ns) name = ns.ToDisplayString() + "." + name;
        if (type.TypeArguments.Length > 0 && !type.IsDefinition)
            name += "[" + string.Join(",", type.TypeArguments.Select(TypeName)) + "]";
        return name;

        static string TypeName(ITypeSymbol t) => t switch
        {
            INamedTypeSymbol n => MetadataName(n),
            IArrayTypeSymbol a => TypeName(a.ElementType) + "[]",
            _ => t.Name,
        };
    }

    private static void AppendTypeArguments(StringBuilder sb, IEnumerable<ITypeSymbol> arguments)
    {
        sb.Append('I');
        foreach (var t in arguments) AppendType(sb, t);
        sb.Append('E');
    }

    private static void AppendPath(StringBuilder sb, ISymbol? symbol)
    {
        switch (symbol)
        {
            case null:
            case INamespaceSymbol { IsGlobalNamespace: true }:
                return;
            case IMethodSymbol containingMethod: // local functions nest under their declaring method
                AppendPath(sb, containingMethod.ContainingSymbol);
                AppendName(sb, containingMethod.MethodKind == MethodKind.Constructor ? "ctor" : containingMethod.Name);
                return;
            default:
                AppendPath(sb, symbol.ContainingSymbol);
                AppendName(sb, symbol.MetadataName);
                if (symbol is INamedTypeSymbol { IsDefinition: false, TypeArguments.Length: > 0 } constructed)
                    AppendTypeArguments(sb, constructed.TypeArguments);
                return;
        }
    }

    private static void AppendName(StringBuilder sb, string name) =>
        sb.Append(Encoding.UTF8.GetByteCount(name)).Append(name);

    private static void AppendType(StringBuilder sb, ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Void: sb.Append('v'); return;
            case SpecialType.System_Boolean: sb.Append('b'); return;
            case SpecialType.System_Char: sb.Append('w'); return;
            case SpecialType.System_SByte: sb.Append('a'); return;
            case SpecialType.System_Byte: sb.Append('h'); return;
            case SpecialType.System_Int16: sb.Append('s'); return;
            case SpecialType.System_UInt16: sb.Append('t'); return;
            case SpecialType.System_Int32: sb.Append('i'); return;
            case SpecialType.System_UInt32: sb.Append('j'); return;
            case SpecialType.System_Int64: sb.Append('l'); return;
            case SpecialType.System_UInt64: sb.Append('m'); return;
            case SpecialType.System_Single: sb.Append('f'); return;
            case SpecialType.System_Double: sb.Append('d'); return;
            case SpecialType.System_String: sb.Append('u'); return;
            case SpecialType.System_Object: sb.Append('o'); return;
        }
        if (type is ITypeParameterSymbol tp)
        {
            sb.Append('T');
            AppendName(sb, tp.Name);
            return;
        }
        if (type is IArrayTypeSymbol array)
        {
            sb.Append('A');
            AppendType(sb, array.ElementType);
            return;
        }
        sb.Append('N');
        AppendPath(sb, type);
        if (type is INamedTypeSymbol { TypeArguments.Length: > 0 } generic)
        {
            sb.Append('I');
            foreach (var argument in generic.TypeArguments) AppendType(sb, argument);
            sb.Append('E');
        }
        sb.Append('E');
    }
}
