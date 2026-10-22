using System.Collections.Frozen;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

internal delegate LLVMValueRef IntrinsicHandler(FunctionCodeGen g, IInvocationOperation invocation);
internal delegate LLVMValueRef PropertyHandler(FunctionCodeGen g, IPropertyReferenceOperation property);
internal delegate LLVMValueRef ConstructorHandler(FunctionCodeGen g, IObjectCreationOperation creation);

/// <summary>
/// The bootstrap "corelib": library (BCL) members that csharpc lowers directly to LLVM intrinsics,
/// libm or csrt calls. Roslyn still binds user code against the real .NET reference assemblies, so
/// overload resolution is exact; we just supply the implementation. This table shrinks as the
/// C# corelib grows (docs/04-runtime.md).
/// </summary>
internal static class Intrinsics
{
    public static string Key(IMethodSymbol m) =>
        $"{TypeName(m.ContainingType)}.{m.Name}({string.Join(", ", m.Parameters.Select(p => TypeName(p.Type)))})";

    private static string PropertyKey(IPropertySymbol p) =>
        p.IsIndexer
            ? $"{TypeName(p.ContainingType)}.this[{string.Join(", ", p.Parameters.Select(x => TypeName(x.Type)))}]"
            : $"{TypeName(p.ContainingType)}.{p.Name}";

    private static readonly SymbolDisplayFormat s_keyFormat = SymbolDisplayFormat.CSharpErrorMessageFormat
        .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)
        .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNotNullableReferenceTypeModifier);

    private static string TypeName(ITypeSymbol t) => t.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(s_keyFormat);

    public static bool TryGet(IMethodSymbol method, out IntrinsicHandler handler) =>
        s_methods.TryGetValue(Key(method.OriginalDefinition), out handler!);

    public static bool TryGetProperty(IPropertySymbol property, out PropertyHandler handler) =>
        s_properties.TryGetValue(PropertyKey(property.OriginalDefinition), out handler!);

    public static bool TryGetConstructor(IMethodSymbol constructor, out ConstructorHandler handler) =>
        s_constructors.TryGetValue(Key(constructor.OriginalDefinition), out handler!);

    public static IEnumerable<string> All => s_methods.Keys.Concat(s_properties.Keys).Concat(s_constructors.Keys).Order();

    private static readonly FrozenDictionary<string, ConstructorHandler> s_constructors = BuildConstructors();

    private static readonly FrozenDictionary<string, IntrinsicHandler> s_methods = BuildMethods();
    private static readonly FrozenDictionary<string, PropertyHandler> s_properties = BuildProperties();

    private static LLVMValueRef Arg(FunctionCodeGen g, IInvocationOperation inv, int ordinal)
    {
        var argument = inv.Arguments.First(a => a.Parameter!.Ordinal == ordinal);
        return g.Coerce(g.EmitValue(argument.Value), argument.Value.Type, argument.Parameter!.Type);
    }

    /// <summary>Argument 0 widened to i64 (sign- or zero-extended).</summary>
    private static LLVMValueRef ArgAsI64(FunctionCodeGen g, IInvocationOperation inv, bool signed)
    {
        var value = Arg(g, inv, 0);
        return value.Type.IntWidth == 64 ? value : g.Builder.Cast(signed ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, g.Module.Llvm.I64);
    }

    private static FrozenDictionary<string, IntrinsicHandler> BuildMethods()
    {
        Dictionary<string, IntrinsicHandler> t = [];
        // ---- System.Console ----------------------------------------------------------------
        string[] printable = ["string", "object", "int", "long", "uint", "ulong", "bool", "double", "float", "char"];
        foreach (var type in printable)
        {
            t[$"System.Console.Write({type})"] = (g, inv) => { WriteValue(g, inv.Arguments[0].Value); return default; };
            t[$"System.Console.WriteLine({type})"] = (g, inv) => { WriteValue(g, inv.Arguments[0].Value); NewLine(g); return default; };
        }
        t["System.Console.WriteLine()"] = (g, _) => { NewLine(g); return default; };

        // ---- System.Object / ToString ----------------------------------------------------------
        t["object..ctor()"] = (_, _) => default;
        t["object.ToString()"] = (g, inv) =>
        {
            var self = g.EmitValue(inv.Instance!);
            g.NullCheck(self);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        t["System.Enum.ToString()"] = (g, inv) =>
            g.ToStringValue(inv.Instance is IConversionOperation { Operand.Type.TypeKind: TypeKind.Enum } boxed ? boxed.Operand : inv.Instance!);
        t["object.Equals(object)"] = (g, inv) =>
        {
            var self = g.EmitValue(inv.Instance!);
            g.NullCheck(self);
            return g.VirtualCall(inv.TargetMethod, self, [self, Arg(g, inv, 0)]);
        };
        t["object.GetHashCode()"] = (g, inv) =>
        {
            var self = g.EmitValue(inv.Instance!);
            g.NullCheck(self);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        foreach (var type in new[] { "int", "long", "uint", "ulong", "short", "ushort", "byte", "sbyte", "double", "float", "bool", "char", "string" })
            t[$"{type}.ToString()"] = (g, inv) => g.ToStringValue(inv.Instance!);

        // ---- System.String ---------------------------------------------------------------------
        IntrinsicHandler concat = (g, inv) =>
        {
            LLVMValueRef? acc = null;
            foreach (var a in inv.Arguments.OrderBy(a => a.Parameter!.Ordinal))
            {
                var s = g.ToStringValue(a.Value);
                acc = acc is { } prev ? g.ConcatStrings(prev, s) : s;
            }
            return acc!.Value;
        };
        t["string.Concat(string, string)"] = concat;
        t["string.Concat(string, string, string)"] = concat;
        t["string.Concat(string, string, string, string)"] = concat;
        t["string.Concat(object, object)"] = concat;
        t["string.Concat(object, object, object)"] = concat;
        t["string.IsNullOrEmpty(string)"] = (g, inv) =>
            g.CallRuntime("csrt_string_is_null_or_empty", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], Arg(g, inv, 0));
        t["string.Equals(string, string)"] = (g, inv) =>
            g.CallRuntime("csrt_string_equals", g.Module.Llvm.I1, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1));
        t["string.Equals(string)"] = (g, inv) =>
        {
            var self = g.EmitValue(inv.Instance!);
            g.NullCheck(self);
            return g.CallRuntime("csrt_string_equals", g.Module.Llvm.I1, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], self, Arg(g, inv, 0));
        };

        // String instance methods: (key, runtime fn, return type, extra-arg types, constant extra args)
        AddStringMethods(t);

        // ---- System.Char / parsing (runtime/strings.c) -----------------------------------------
        foreach (var (name, fn) in new[] { ("IsDigit", "csrt_char_is_digit"), ("IsLetter", "csrt_char_is_letter"),
                     ("IsLetterOrDigit", "csrt_char_is_letter_or_digit"), ("IsWhiteSpace", "csrt_char_is_white_space"),
                     ("IsUpper", "csrt_char_is_upper"), ("IsLower", "csrt_char_is_lower"), ("IsPunctuation", "csrt_char_is_punctuation") })
            t[$"char.{name}(char)"] = (g, inv) => g.CallRuntime(fn, g.Module.Llvm.I1, [g.Module.Llvm.I16], Arg(g, inv, 0));
        t["char.ToUpper(char)"] = (g, inv) => g.CallRuntime("csrt_char_to_upper", g.Module.Llvm.I16, [g.Module.Llvm.I16], Arg(g, inv, 0));
        t["char.ToLower(char)"] = (g, inv) => g.CallRuntime("csrt_char_to_lower", g.Module.Llvm.I16, [g.Module.Llvm.I16], Arg(g, inv, 0));
        t["char.ToUpperInvariant(char)"] = t["char.ToUpper(char)"];
        t["char.ToLowerInvariant(char)"] = t["char.ToLower(char)"];
        t["int.Parse(string)"] = (g, inv) => g.CallRuntime("csrt_int32_parse", g.Module.Llvm.I32, [g.Module.Llvm.Ptr], Arg(g, inv, 0));
        t["long.Parse(string)"] = (g, inv) => g.CallRuntime("csrt_int64_parse", g.Module.Llvm.I64, [g.Module.Llvm.Ptr], Arg(g, inv, 0));
        t["double.Parse(string)"] = (g, inv) => g.CallRuntime("csrt_double_parse", g.Module.Llvm.F64, [g.Module.Llvm.Ptr], Arg(g, inv, 0));
        foreach (var (type, fn) in new[] { ("int", "csrt_int32_try_parse"), ("long", "csrt_int64_try_parse"), ("double", "csrt_double_try_parse") })
            t[$"{type}.TryParse(string, {type})"] = (g, inv) =>
            {
                var l = g.Module.Llvm;
                var s = Arg(g, inv, 0);
                var result = g.AddressOf(inv.Arguments.First(a => a.Parameter!.Ordinal == 1).Value);
                return g.CallRuntime(fn, l.I1, [l.Ptr, l.Ptr], s, result);
            };

        // ---- System.Text.StringBuilder (runtime/strings.c) -------------------------------------
        IntrinsicHandler sbAppend(string fn, Func<FunctionCodeGen, IInvocationOperation, LLVMValueRef[]> extra, Func<LlvmContext, LLVMTypeRef[]> types) =>
            (g, inv) =>
            {
                var self = g.EmitValue(inv.Instance!);
                return g.CallRuntime(fn, g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, .. types(g.Module.Llvm)], [self, .. extra(g, inv)]);
            };
        t["System.Text.StringBuilder.Append(string)"] = sbAppend("csrt_sb_append_string", (g, inv) => [Arg(g, inv, 0)], l => [l.Ptr]);
        t["System.Text.StringBuilder.Append(char)"] = sbAppend("csrt_sb_append_char", (g, inv) => [Arg(g, inv, 0), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 1)], l => [l.I16, l.I32]);
        t["System.Text.StringBuilder.Append(char, int)"] = sbAppend("csrt_sb_append_char", (g, inv) => [Arg(g, inv, 0), Arg(g, inv, 1)], l => [l.I16, l.I32]);
        foreach (var type in new[] { "double", "float", "object" })
            t[$"System.Text.StringBuilder.Append({type})"] = sbAppend("csrt_sb_append_string",
                (g, inv) => [g.ToStringValue(inv.Arguments[0].Value)], l => [l.Ptr]);
        // Integers and bools format straight into the builder's buffer (no temporary string).
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("short", true), ("sbyte", true), ("uint", false), ("ulong", false), ("ushort", false), ("byte", false) })
            t[$"System.Text.StringBuilder.Append({type})"] = sbAppend(signed ? "csrt_sb_append_i64" : "csrt_sb_append_u64",
                (g, inv) => [ArgAsI64(g, inv, signed)], l => [l.I64]);
        t["System.Text.StringBuilder.Append(bool)"] = sbAppend("csrt_sb_append_bool", (g, inv) => [Arg(g, inv, 0)], l => [l.I1]);
        t["System.Text.StringBuilder.AppendLine()"] = sbAppend("csrt_sb_append_char",
            (g, _) => [g.Module.Llvm.ConstInt(g.Module.Llvm.I16, '\n'), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 1)], l => [l.I16, l.I32]);
        t["System.Text.StringBuilder.AppendLine(string)"] = (g, inv) =>
        {
            var l = g.Module.Llvm;
            var sb = g.CallRuntime("csrt_sb_append_string", l.Ptr, [l.Ptr, l.Ptr], g.EmitValue(inv.Instance!), Arg(g, inv, 0));
            return g.CallRuntime("csrt_sb_append_char", l.Ptr, [l.Ptr, l.I16, l.I32], sb, l.ConstInt(l.I16, '\n'), l.ConstInt(l.I32, 1));
        };
        t["System.Text.StringBuilder.ToString()"] = (g, inv) => g.CallRuntime("csrt_sb_to_string", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], g.EmitValue(inv.Instance!));
        t["System.Text.StringBuilder.Clear()"] = (g, inv) => g.CallRuntime("csrt_sb_clear", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], g.EmitValue(inv.Instance!));

        // ---- System.Math -----------------------------------------------------------------------
        foreach (var (name, llvm) in new[] { ("Sqrt", "llvm.sqrt"), ("Floor", "llvm.floor"), ("Ceiling", "llvm.ceil"),
                     ("Truncate", "llvm.trunc"), ("Round", "llvm.roundeven") /* MidpointRounding.ToEven */ })
            t[$"System.Math.{name}(double)"] = (g, inv) => CallUnaryF64(g, llvm + ".f64", Arg(g, inv, 0));
        t["System.Math.Abs(double)"] = (g, inv) => CallUnaryF64(g, "llvm.fabs.f64", Arg(g, inv, 0));
        t["System.Math.Abs(float)"] = (g, inv) =>
        {
            var f = g.Module.Intrinsic("llvm.fabs.f32", g.Module.Llvm.F32, g.Module.Llvm.F32);
            return g.Builder.Call(f.Type, f.Fn, [Arg(g, inv, 0)]);
        };
        foreach (var name in new[] { "pow", "atan2" })
            t[$"System.Math.{char.ToUpperInvariant(name[0])}{name[1..]}(double, double)"] = (g, inv) =>
            {
                var l = g.Module.Llvm;
                var f = g.Module.Intrinsic(name, l.F64, l.F64, l.F64);
                return g.Builder.Call(f.Type, f.Fn, [Arg(g, inv, 0), Arg(g, inv, 1)]);
            };
        foreach (var name in new[] { "exp", "log", "log10", "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "cbrt" })
            t[$"System.Math.{char.ToUpperInvariant(name[0])}{name[1..]}(double)"] = (g, inv) => CallUnaryF64(g, name, Arg(g, inv, 0));

        foreach (var type in new[] { "int", "long" })
        {
            t[$"System.Math.Abs({type})"] = (g, inv) =>
            {
                // Math.Abs(int.MinValue) throws OverflowException in .NET.
                var b = g.Builder;
                var v = Arg(g, inv, 0);
                var width = v.Type.IntWidth;
                g.FailIf(b.ICmp(LLVMIntPredicate.EQ, v, g.Module.Llvm.ConstUInt(v.Type, 1UL << (int)(width - 1))), "csrt_throw_overflow");
                return b.Select(b.ICmp(LLVMIntPredicate.SLT, v, g.Module.Llvm.ConstInt(v.Type, 0)), b.Neg(v), v);
            };
        }
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("uint", false), ("ulong", false) })
        {
            t[$"System.Math.Max({type}, {type})"] = (g, inv) => SelectCompare(g, Arg(g, inv, 0), Arg(g, inv, 1), signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT);
            t[$"System.Math.Min({type}, {type})"] = (g, inv) => SelectCompare(g, Arg(g, inv, 0), Arg(g, inv, 1), signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT);
        }
        // IEEE 754-2019 maximum/minimum: NaN-propagating and -0 < +0, exactly .NET Core 3.0+ Math.Max/Min.
        t["System.Math.Max(double, double)"] = (g, inv) => CallBinaryF64(g, "llvm.maximum.f64", Arg(g, inv, 0), Arg(g, inv, 1));
        t["System.Math.Min(double, double)"] = (g, inv) => CallBinaryF64(g, "llvm.minimum.f64", Arg(g, inv, 0), Arg(g, inv, 1));
        t["System.Math.Clamp(int, int, int)"] = (g, inv) =>
        {
            var v = Arg(g, inv, 0);
            var lo = Arg(g, inv, 1);
            var hi = Arg(g, inv, 2);
            return SelectCompare(g, SelectCompare(g, v, lo, LLVMIntPredicate.SGT), hi, LLVMIntPredicate.SLT);
        };

        // ---- foreach over arrays (see FunctionCodeGen.ArrayGetEnumerator) ------------------------
        t["System.Collections.Generic.IEnumerable<T>.GetEnumerator()"] = (g, inv) => g.ArrayGetEnumerator(inv.Instance!, inv);
        t["System.Collections.IEnumerable.GetEnumerator()"] = (g, inv) => g.ArrayGetEnumerator(inv.Instance!, inv);
        t["System.Collections.IEnumerator.MoveNext()"] = (g, inv) => g.ArrayEnumeratorMoveNext(g.EmitValue(inv.Instance!));
        // Only array enumerators exist so far, and they need no disposal. Revisit with interfaces.
        t["System.IDisposable.Dispose()"] = (_, _) => default;

        // ---- System.GC (runtime/gc.c) -----------------------------------------------------------
        t["System.GC.Collect()"] = (g, _) => { g.CallRuntime("csrt_gc_collect", g.Module.Llvm.Void, []); return default; };
        t["System.GC.GetTotalMemory(bool)"] = (g, inv) =>
        {
            g.EmitValue(inv.Arguments[0].Value);
            return g.CallRuntime("csrt_gc_total_memory", g.Module.Llvm.I64, []);
        };

        t["string.GetEnumerator()"] = (g, inv) => g.StringGetEnumerator(g.EmitValue(inv.Instance!));
        t["System.CharEnumerator.MoveNext()"] = (g, inv) => g.StringEnumeratorMoveNext(g.EmitValue(inv.Instance!));
        t["System.CharEnumerator.Dispose()"] = (_, _) => default;

        // ---- System.Environment ----------------------------------------------------------------
        t["System.Environment.Exit(int)"] = (g, inv) =>
        {
            g.CallRuntime("csrt_environment_exit", g.Module.Llvm.Void, [g.Module.Llvm.I32], Arg(g, inv, 0));
            return default;
        };

        return t.ToFrozenDictionary();
    }

    private static void AddStringMethods(Dictionary<string, IntrinsicHandler> t)
    {
        // Shape: key -> (runtime function, return kind, argument builder). Instance is always arg 0.
        void Add(string key, string fn, Func<LlvmContext, LLVMTypeRef> ret, Func<LlvmContext, LLVMTypeRef[]> extraTypes,
            Func<FunctionCodeGen, IInvocationOperation, LLVMValueRef[]> extraArgs)
        {
            t[key] = (g, inv) =>
            {
                var l = g.Module.Llvm;
                var self = g.EmitValue(inv.Instance!);
                g.NullCheck(self);
                return g.CallRuntime(fn, ret(l), [l.Ptr, .. extraTypes(l)], [self, .. extraArgs(g, inv)]);
            };
        }
        static LLVMValueRef I32(FunctionCodeGen g, int v) => g.Module.Llvm.ConstInt(g.Module.Llvm.I32, v);
        Func<LlvmContext, LLVMTypeRef> ptr = l => l.Ptr, i32 = l => l.I32, i1 = l => l.I1;

        Add("string.Substring(int)", "csrt_string_substring_from", ptr, l => [l.I32], (g, i) => [Arg(g, i, 0)]);
        Add("string.Substring(int, int)", "csrt_string_substring", ptr, l => [l.I32, l.I32], (g, i) => [Arg(g, i, 0), Arg(g, i, 1)]);
        Add("string.IndexOf(char)", "csrt_string_index_of_char", i32, l => [l.I16, l.I32], (g, i) => [Arg(g, i, 0), I32(g, 0)]);
        Add("string.IndexOf(char, int)", "csrt_string_index_of_char", i32, l => [l.I16, l.I32], (g, i) => [Arg(g, i, 0), Arg(g, i, 1)]);
        Add("string.IndexOf(string)", "csrt_string_index_of", i32, l => [l.Ptr, l.I32], (g, i) => [Arg(g, i, 0), I32(g, 0)]);
        Add("string.IndexOf(string, int)", "csrt_string_index_of", i32, l => [l.Ptr, l.I32], (g, i) => [Arg(g, i, 0), Arg(g, i, 1)]);
        Add("string.LastIndexOf(char)", "csrt_string_last_index_of_char", i32, l => [l.I16], (g, i) => [Arg(g, i, 0)]);
        Add("string.Contains(string)", "csrt_string_contains", i1, l => [l.Ptr], (g, i) => [Arg(g, i, 0)]);
        Add("string.Contains(char)", "csrt_string_contains_char", i1, l => [l.I16], (g, i) => [Arg(g, i, 0)]);
        Add("string.StartsWith(string)", "csrt_string_starts_with", i1, l => [l.Ptr], (g, i) => [Arg(g, i, 0)]);
        Add("string.EndsWith(string)", "csrt_string_ends_with", i1, l => [l.Ptr], (g, i) => [Arg(g, i, 0)]);
        foreach (var (name, fn) in new[] { ("ToUpper", "csrt_string_to_upper"), ("ToUpperInvariant", "csrt_string_to_upper"),
                     ("ToLower", "csrt_string_to_lower"), ("ToLowerInvariant", "csrt_string_to_lower") })
            Add($"string.{name}()", fn, ptr, _ => [], (_, _) => []);
        Add("string.Trim()", "csrt_string_trim", ptr, l => [l.I32], (g, _) => [I32(g, 3)]);
        Add("string.TrimStart()", "csrt_string_trim", ptr, l => [l.I32], (g, _) => [I32(g, 1)]);
        Add("string.TrimEnd()", "csrt_string_trim", ptr, l => [l.I32], (g, _) => [I32(g, 2)]);
        Add("string.Replace(char, char)", "csrt_string_replace_char", ptr, l => [l.I16, l.I16], (g, i) => [Arg(g, i, 0), Arg(g, i, 1)]);
        Add("string.Replace(string, string)", "csrt_string_replace", ptr, l => [l.Ptr, l.Ptr], (g, i) => [Arg(g, i, 0), Arg(g, i, 1)]);
        Add("string.PadLeft(int)", "csrt_string_pad", ptr, l => [l.I32, l.I16, l.I1], (g, i) => [Arg(g, i, 0), g.Module.Llvm.ConstInt(g.Module.Llvm.I16, ' '), g.Module.Llvm.True]);
        Add("string.PadRight(int)", "csrt_string_pad", ptr, l => [l.I32, l.I16, l.I1], (g, i) => [Arg(g, i, 0), g.Module.Llvm.ConstInt(g.Module.Llvm.I16, ' '), g.Module.Llvm.False]);
        Add("string.PadLeft(int, char)", "csrt_string_pad", ptr, l => [l.I32, l.I16, l.I1], (g, i) => [Arg(g, i, 0), Arg(g, i, 1), g.Module.Llvm.True]);
        Add("string.PadRight(int, char)", "csrt_string_pad", ptr, l => [l.I32, l.I16, l.I1], (g, i) => [Arg(g, i, 0), Arg(g, i, 1), g.Module.Llvm.False]);
        Add("string.Split(char, System.StringSplitOptions)", "csrt_string_split_char", ptr, l => [l.I16, l.I32], (g, i) => [Arg(g, i, 0), Arg(g, i, 1)]);
        Add("string.ToCharArray()", "csrt_string_to_char_array", ptr, _ => [], (_, _) => []);
        Add("string.GetHashCode()", "csrt_string_get_hash_code", i32, _ => [], (_, _) => []);
        Add("string.CompareTo(string)", "csrt_string_compare_ordinal", i32, l => [l.Ptr], (g, i) => [Arg(g, i, 0)]);

        t["string.Join(string, string[])"] = (g, inv) =>
            g.CallRuntime("csrt_string_join", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1));
        t["string.CompareOrdinal(string, string)"] = (g, inv) =>
            g.CallRuntime("csrt_string_compare_ordinal", g.Module.Llvm.I32, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1));
    }

    private static LLVMValueRef CtorArg(FunctionCodeGen g, IObjectCreationOperation c, int ordinal)
    {
        var argument = c.Arguments.First(a => a.Parameter!.Ordinal == ordinal);
        return g.Coerce(g.EmitValue(argument.Value), argument.Value.Type, argument.Parameter!.Type);
    }

    private static FrozenDictionary<string, ConstructorHandler> BuildConstructors()
    {
        Dictionary<string, ConstructorHandler> t = [];
        t["string..ctor(char, int)"] = (g, c) =>
            g.CallRuntime("csrt_string_new_repeat", g.Module.Llvm.Ptr, [g.Module.Llvm.I16, g.Module.Llvm.I32], CtorArg(g, c, 0), CtorArg(g, c, 1));
        t["string..ctor(char[])"] = (g, c) =>
            g.CallRuntime("csrt_string_new_from_chars", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], CtorArg(g, c, 0));
        t["System.Text.StringBuilder..ctor()"] = (g, _) =>
            g.CallRuntime("csrt_sb_new", g.Module.Llvm.Ptr, [g.Module.Llvm.I32], g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 16));
        t["System.Text.StringBuilder..ctor(int)"] = (g, c) =>
            g.CallRuntime("csrt_sb_new", g.Module.Llvm.Ptr, [g.Module.Llvm.I32], CtorArg(g, c, 0));
        t["System.Text.StringBuilder..ctor(string)"] = (g, c) =>
            g.CallRuntime("csrt_sb_new_from_string", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], CtorArg(g, c, 0));
        return t.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, PropertyHandler> BuildProperties()
    {
        Dictionary<string, PropertyHandler> t = [];
        t["System.Text.StringBuilder.Length"] = (g, p) =>
            g.CallRuntime("csrt_sb_length", g.Module.Llvm.I32, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
        t["string.Length"] = (g, p) =>
        {
            var s = g.EmitValue(p.Instance!);
            g.NullCheck(s);
            var l = g.Module.Llvm;
            return g.Builder.Load(l.I32, g.Builder.StructGep(g.Module.Types.StringHeader, s, 1), "strlen");
        };
        t["string.this[int]"] = (g, p) =>
        {
            var l = g.Module.Llvm;
            var s = g.EmitValue(p.Instance!);
            var index = g.EmitValue(p.Arguments[0].Value);
            return g.CallRuntime("csrt_string_char_at", l.I16, [l.Ptr, l.I32], s, index);
        };
        t["System.Array.Length"] = (g, p) =>
            g.Builder.Cast(LLVMOpcode.Trunc, g.ArrayLength(g.EmitValue(p.Instance!)), g.Module.Llvm.I32);
        t["System.Array.LongLength"] = (g, p) => g.ArrayLength(g.EmitValue(p.Instance!));
        t["System.Environment.TickCount64"] = (g, _) =>
            g.CallRuntime("csrt_environment_tick_count64", g.Module.Llvm.I64, []);
        t["System.Collections.Generic.IEnumerator<T>.Current"] = (g, p) =>
            g.ArrayEnumeratorCurrent(g.EmitValue(p.Instance!), p.Type!);
        t["System.Collections.IEnumerator.Current"] = (g, p) =>
        {
            if (!g.TryArrayEnumerator(p.Instance, out var element))
                throw CompileError.Unsupported("IEnumerator.Current on a non-array enumerator", p);
            var value = g.ArrayEnumeratorCurrent(g.EmitValue(p.Instance!), element);
            return element.IsValueType ? g.Box(value, element, p) : value;
        };
        t["System.CharEnumerator.Current"] = (g, p) => g.StringEnumeratorCurrent(g.EmitValue(p.Instance!));
        t["System.Environment.NewLine"] = (g, _) => g.Module.StringLiteral("\n");
        return t.ToFrozenDictionary();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static void NewLine(FunctionCodeGen g) => g.CallRuntime("csrt_console_newline", g.Module.Llvm.Void, []);

    private static void WriteValue(FunctionCodeGen g, IOperation argument)
    {
        var l = g.Module.Llvm;
        var op = FunctionCodeGen.Unbox(argument);
        var type = op.Type;
        if (op.ConstantValue.HasValue || type is null || type.TypeKind == TypeKind.Enum)
        {
            g.CallRuntime("csrt_console_write_string", l.Void, [l.Ptr], g.ToStringValue(op));
            return;
        }
        (string Name, LLVMTypeRef Type)? direct = type.SpecialType switch
        {
            SpecialType.System_String => ("csrt_console_write_string", l.Ptr),
            SpecialType.System_Int32 => ("csrt_console_write_i32", l.I32),
            SpecialType.System_Int64 => ("csrt_console_write_i64", l.I64),
            SpecialType.System_UInt32 => ("csrt_console_write_u32", l.I32),
            SpecialType.System_UInt64 => ("csrt_console_write_u64", l.I64),
            SpecialType.System_Double => ("csrt_console_write_f64", l.F64),
            SpecialType.System_Single => ("csrt_console_write_f32", l.F32),
            SpecialType.System_Boolean => ("csrt_console_write_bool", l.I1),
            SpecialType.System_Char => ("csrt_console_write_char", l.I16),
            _ => null,
        };
        if (direct is { } d)
            g.CallRuntime(d.Name, l.Void, [d.Type], g.EmitValue(op));
        else
            g.CallRuntime("csrt_console_write_string", l.Void, [l.Ptr], g.ToStringValue(op));
    }

    private static LLVMValueRef CallUnaryF64(FunctionCodeGen g, string name, LLVMValueRef x)
    {
        var f = g.Module.Intrinsic(name, g.Module.Llvm.F64, g.Module.Llvm.F64);
        return g.Builder.Call(f.Type, f.Fn, [x]);
    }

    private static LLVMValueRef CallBinaryF64(FunctionCodeGen g, string name, LLVMValueRef x, LLVMValueRef y)
    {
        var l = g.Module.Llvm;
        var f = g.Module.Intrinsic(name, l.F64, l.F64, l.F64);
        return g.Builder.Call(f.Type, f.Fn, [x, y]);
    }

    private static LLVMValueRef SelectCompare(FunctionCodeGen g, LLVMValueRef a, LLVMValueRef b, LLVMIntPredicate keepAWhen) =>
        g.Builder.Select(g.Builder.ICmp(keepAWhen, a, b), a, b);
}
