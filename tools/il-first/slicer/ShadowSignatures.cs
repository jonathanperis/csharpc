using System.Text.RegularExpressions;
using Mono.Cecil;

/// <summary>
/// Method signatures as "Name(UInt64,UInt32,UInt64)": simple type names (no namespace, by-ref and modifiers dropped, a generic type as
/// "ReadOnlySpan`1"), read from the corelib's C# shadow sources and from CoreLib's metadata alike, so the slicer can tell whether a
/// shadow declares a given overload of a helper (Math.BigMul(ulong, ulong, out ulong) but not CoreLib's internal BigMul(ulong, uint, out ulong)).
/// </summary>
static class ShadowSignatures
{
    static readonly Regex Declaration = new(@"\b(?:public|internal|private|protected)\b[^;{}=()]*?\b([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*\(([^()]*)\)\s*(?:where\b[^{;=]*)?(?:\{|=>)", RegexOptions.Compiled);

    // A property ("public static ReadOnlySpan<ulong> PowersOf10 => [...]", "{ get"): its getter, "get_PowersOf10()".
    static readonly Regex Property = new(@"\b(?:public|internal|private|protected)\b[^;{}=()]*?\b([A-Za-z_][A-Za-z0-9_]*)\s*(?:=>|\{\s*get\b)", RegexOptions.Compiled);

    static readonly Dictionary<string, string> Keywords = new()
    {
        ["bool"] = "Boolean", ["byte"] = "Byte", ["sbyte"] = "SByte", ["char"] = "Char", ["short"] = "Int16", ["ushort"] = "UInt16",
        ["int"] = "Int32", ["uint"] = "UInt32", ["long"] = "Int64", ["ulong"] = "UInt64", ["nint"] = "IntPtr", ["nuint"] = "UIntPtr",
        ["float"] = "Single", ["double"] = "Double", ["decimal"] = "Decimal", ["string"] = "String", ["object"] = "Object", ["void"] = "Void",
    };

    public static HashSet<string> Declared(string source)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Declaration.Matches(source))
            set.Add(m.Groups[1].Value + "(" + string.Join(",", SplitTopLevel(m.Groups[2].Value).Select(ParameterType).Where(t => t.Length > 0)) + ")");
        foreach (Match m in Property.Matches(source)) set.Add("get_" + m.Groups[1].Value + "()");
        return set;
    }

    public static string Of(MethodDefinition m) => m.Name + "(" + string.Join(",", m.Parameters.Select(p => Simple(p.ParameterType))) + ")";

    static string Simple(TypeReference t) => t switch
    {
        ByReferenceType r => Simple(r.ElementType),
        RequiredModifierType rm => Simple(rm.ElementType),
        OptionalModifierType om => Simple(om.ElementType),
        ArrayType a => Simple(a.ElementType) + "[]",
        PointerType p => Simple(p.ElementType) + "*",
        GenericInstanceType g => g.ElementType.Name,
        _ => t.Name,
    };

    static string ParameterType(string parameter)
    {
        var text = Regex.Replace(parameter, @"\[[^\]]*\]", " ");          // attributes
        var eq = text.IndexOf('=');
        if (eq >= 0) text = text[..eq];                                     // default value
        var tokens = Regex.Split(text.Trim(), @"\s+(?![^<]*>)").Where(x => x.Length > 0).ToList();
        tokens.RemoveAll(x => x is "this" or "out" or "ref" or "in" or "params" or "scoped" or "readonly");
        if (tokens.Count < 2) return "";
        var type = string.Join("", tokens.Take(tokens.Count - 1)).Replace("?", "");
        var array = type.EndsWith("[]", StringComparison.Ordinal) ? "[]" : "";
        if (array.Length > 0) type = type[..^2];
        var pointer = type.EndsWith('*') ? "*" : "";
        if (pointer.Length > 0) type = type.TrimEnd('*');
        var lt = type.IndexOf('<');
        if (lt >= 0) type = type[..lt] + "`" + (SplitTopLevel(type[(lt + 1)..^1]).Count);
        type = type[(type.LastIndexOf('.') + 1)..];
        return (Keywords.GetValueOrDefault(type) ?? type) + array + pointer;
    }

    static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '<' or '(' or '[') depth++;
            else if (text[i] is '>' or ')' or ']') depth--;
            else if (text[i] == ',' && depth == 0) { parts.Add(text[start..i]); start = i + 1; }
        }
        if (text[start..].Trim().Length > 0) parts.Add(text[start..]);
        return parts;
    }
}
