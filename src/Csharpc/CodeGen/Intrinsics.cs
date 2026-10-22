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
        return g.Coerce(g.EmitValue(argument.Value), g.TypeOf(argument.Value), g.S(argument.Parameter!.Type));
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
        t["System.Console.ReadLine()"] = (g, _) => g.CallRuntime("csrt_console_read_line", g.Module.Llvm.Ptr, []);
        t["System.Console.Read()"] = (g, _) => g.CallRuntime("csrt_console_read", g.Module.Llvm.I32, []);

        // Console.Out / Console.Error / Console.In: TextWriter/TextReader calls on those properties.
        foreach (var type in printable)
        {
            t[$"System.IO.TextWriter.Write({type})"] = (g, inv) => { WriteTo(g, inv, inv.Arguments[0].Value, newline: false); return default; };
            t[$"System.IO.TextWriter.WriteLine({type})"] = (g, inv) => { WriteTo(g, inv, inv.Arguments[0].Value, newline: true); return default; };
        }
        t["System.IO.TextWriter.WriteLine()"] = (g, inv) => { WriteTo(g, inv, null, newline: true); return default; };
        t["System.IO.TextWriter.Flush()"] = (g, inv) => { ConsoleStream(inv); g.CallRuntime("csrt_console_flush", g.Module.Llvm.Void, []); return default; };
        t["System.IO.TextReader.ReadLine()"] = (g, inv) => { ConsoleStream(inv); return g.CallRuntime("csrt_console_read_line", g.Module.Llvm.Ptr, []); };
        t["System.IO.TextReader.ReadToEnd()"] = (g, inv) => { ConsoleStream(inv); return g.CallRuntime("csrt_console_read_to_end", g.Module.Llvm.Ptr, []); };

        // ---- System.Object / ToString ----------------------------------------------------------
        t["object..ctor()"] = (_, _) => default;
        t["object.ToString()"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        t["object.ReferenceEquals(object, object)"] = (g, inv) =>
            g.Builder.ICmp(LLVMIntPredicate.EQ, Arg(g, inv, 0), Arg(g, inv, 1));
        t["System.Enum.ToString()"] = (g, inv) =>
            g.ToStringValue(inv.Instance is IConversionOperation { Operand.Type.TypeKind: TypeKind.Enum } boxed ? boxed.Operand : inv.Instance!);
        t["object.ReferenceEquals(object, object)"] = (g, inv) =>
            g.Builder.ICmp(LLVMIntPredicate.EQ, Arg(g, inv, 0), Arg(g, inv, 1));
        t["object.Equals(object, object)"] = (g, inv) =>
            g.CallRuntime("csrt_object_equals_static", g.Module.Llvm.I1, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1));
        t["object.Equals(object)"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self, Arg(g, inv, 0)]);
        };
        t["object.GetHashCode()"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        foreach (var type in new[] { "int", "long", "uint", "ulong", "short", "ushort", "byte", "sbyte", "double", "float", "bool", "char", "string" })
            t[$"{type}.ToString()"] = (g, inv) => g.ToStringValue(inv.Instance!);

        // ---- IComparable<T>.CompareTo on primitives (reached via generic constraints) -------------
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("short", true), ("sbyte", true),
                     ("uint", false), ("ulong", false), ("ushort", false), ("byte", false), ("char", false) })
            t[$"{type}.CompareTo({type})"] = (g, inv) =>
            {
                var b = g.Builder;
                var x = g.EmitValue(g.ConcreteReceiver(inv.Instance!));
                var y = Arg(g, inv, 0);
                var l = g.Module.Llvm;
                var lt = b.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, x, y);
                var gt = b.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, x, y);
                return b.Select(lt, l.ConstInt(l.I32, -1), b.Cast(LLVMOpcode.ZExt, gt, l.I32));
            };
        foreach (var type in new[] { "double", "float" })
            t[$"{type}.CompareTo({type})"] = (g, inv) =>
            {
                // NaN sorts first and equals itself, as in .NET.
                var b = g.Builder;
                var l = g.Module.Llvm;
                var x = g.EmitValue(g.ConcreteReceiver(inv.Instance!));
                var y = Arg(g, inv, 0);
                var lt = b.FCmp(LLVMRealPredicate.OLT, x, y);
                var gt = b.FCmp(LLVMRealPredicate.OGT, x, y);
                var xNaN = b.FCmp(LLVMRealPredicate.UNO, x, x);
                var yNaN = b.FCmp(LLVMRealPredicate.UNO, y, y);
                var result = b.Select(lt, l.ConstInt(l.I32, -1), b.Cast(LLVMOpcode.ZExt, gt, l.I32));
                result = b.Select(b.BinOp(LLVMOpcode.And, xNaN, b.Not(yNaN)), l.ConstInt(l.I32, -1), result);
                result = b.Select(b.BinOp(LLVMOpcode.And, yNaN, b.Not(xNaN)), l.ConstInt(l.I32, 1), result);
                return result;
            };

        foreach (var type in new[] { "int", "long", "uint", "ulong", "short", "ushort", "byte", "sbyte", "double", "float" })
            t[$"{type}.ToString(string)"] = (g, inv) => g.FormatWith(inv.Instance!, Arg(g, inv, 0));

        // Composite formatting: fixed-arity overloads (the params ReadOnlySpan<object> ones need spans).
        IntrinsicHandler composite(Action<FunctionCodeGen, LLVMValueRef>? sink, bool newline) => (g, inv) =>
        {
            var l = g.Module.Llvm;
            var ordered = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToList();
            var format = g.EmitValue(ordered[0].Value);
            var args = ordered.Count == 2 && ordered[1].Value.Type is IArrayTypeSymbol && ordered[1].ArgumentKind != ArgumentKind.ParamArray
                ? g.EmitValue(ordered[1].Value)
                : ordered.Count == 2 && ordered[1].Value is IArrayCreationOperation { Initializer: { } init }
                    ? g.ObjectArray(init.ElementValues)
                    : g.ObjectArray([.. ordered.Skip(1).Select(a => a.Value)]);
            var text = g.CallRuntime("csrt_string_format", l.Ptr, [l.Ptr, l.Ptr], format, args);
            if (sink is null) return text;
            sink(g, text);
            if (newline) NewLine(g);
            return default;
        };
        Action<FunctionCodeGen, LLVMValueRef> write = (g, s) => g.CallRuntime("csrt_console_write_string", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], s);
        foreach (var arity in new[] { "object", "object, object", "object, object, object", "object[]" })
        {
            t[$"string.Format(string, {arity})"] = composite(null, false);
            t[$"System.Console.WriteLine(string, {arity})"] = composite(write, true);
            t[$"System.Console.Write(string, {arity})"] = composite(write, false);
        }

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
        AddTaskMethods(t);

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
                g.FailIf(b.ICmp(LLVMIntPredicate.EQ, v, g.Module.Llvm.ConstUInt(v.Type, 1UL << (int)(width - 1))), "csrt_throw_negate_overflow");
                return b.Select(b.ICmp(LLVMIntPredicate.SLT, v, g.Module.Llvm.ConstInt(v.Type, 0)), b.Neg(v), v);
            };
        }
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("uint", false), ("ulong", false) })
        {
            t[$"System.Math.Max({type}, {type})"] = (g, inv) => SelectCompare(g, Arg(g, inv, 0), Arg(g, inv, 1), signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT);
            t[$"System.Math.Min({type}, {type})"] = (g, inv) => SelectCompare(g, Arg(g, inv, 0), Arg(g, inv, 1), signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT);
        }
        t["System.Math.Round(double, int)"] = (g, inv) =>
            g.CallRuntime("csrt_math_round_digits", g.Module.Llvm.F64, [g.Module.Llvm.F64, g.Module.Llvm.I32], Arg(g, inv, 0), Arg(g, inv, 1));
        t["System.Math.Log2(double)"] = (g, inv) => CallUnaryF64(g, "llvm.log2.f64", Arg(g, inv, 0));
        foreach (var type in new[] { "int", "long", "double", "float", "short", "sbyte" })
            t[$"System.Math.Sign({type})"] = (g, inv) =>
            {
                var b = g.Builder;
                var l = g.Module.Llvm;
                var v = Arg(g, inv, 0);
                LLVMValueRef gt, lt;
                if (v.Type.IsFloatingPoint)
                {
                    var zero = LlvmContext.ConstReal(v.Type, 0);
                    // Math.Sign(NaN) throws ArithmeticException in .NET.
                    g.FailIf(b.FCmp(LLVMRealPredicate.UNO, v, v), "csrt_throw_overflow");
                    (gt, lt) = (b.FCmp(LLVMRealPredicate.OGT, v, zero), b.FCmp(LLVMRealPredicate.OLT, v, zero));
                }
                else
                {
                    var zero = l.ConstInt(v.Type, 0);
                    (gt, lt) = (b.ICmp(LLVMIntPredicate.SGT, v, zero), b.ICmp(LLVMIntPredicate.SLT, v, zero));
                }
                return b.BinOp(LLVMOpcode.Sub, b.Cast(LLVMOpcode.ZExt, gt, l.I32), b.Cast(LLVMOpcode.ZExt, lt, l.I32));
            };
        t["System.Math.Max(float, float)"] = (g, inv) => CallBinary(g, "llvm.maximum.f32", g.Module.Llvm.F32, Arg(g, inv, 0), Arg(g, inv, 1));
        t["System.Math.Min(float, float)"] = (g, inv) => CallBinary(g, "llvm.minimum.f32", g.Module.Llvm.F32, Arg(g, inv, 0), Arg(g, inv, 1));
        t["System.MathF.Sqrt(float)"] = (g, inv) =>
        {
            var f = g.Module.Intrinsic("llvm.sqrt.f32", g.Module.Llvm.F32, g.Module.Llvm.F32);
            return g.Builder.Call(f.Type, f.Fn, [Arg(g, inv, 0)]);
        };

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
        return g.Coerce(g.EmitValue(argument.Value), g.TypeOf(argument.Value), g.S(argument.Parameter!.Type));
    }

    // ---- System.Threading.Tasks (runtime/async.c; docs/10-async.md) ---------------------------
    private const string Tasks = "System.Threading.Tasks";
    private const string Rcs = "System.Runtime.CompilerServices";

    private static void AddTaskMethods(Dictionary<string, IntrinsicHandler> t)
    {
        static LLVMValueRef Wait(FunctionCodeGen g, LLVMValueRef task)
        {
            g.CallRuntime("csrt_task_wait", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], task);
            return task;
        }
        static ITypeSymbol? ResultOf(FunctionCodeGen g, IInvocationOperation inv) => g.TypeOf(inv) is { SpecialType: SpecialType.System_Void } ? null : g.TypeOf(inv);
        // Tasks, awaiters and configured awaitables are the same task pointer.
        IntrinsicHandler self = (g, inv) => { foreach (var a in inv.Arguments) g.EmitValue(a.Value); return g.EmitValue(inv.Instance!); };

        t[$"{Tasks}.Task.Delay(int)"] = (g, inv) => g.CallRuntime("csrt_task_delay", g.Module.Llvm.Ptr, [g.Module.Llvm.I32], Arg(g, inv, 0));
        t[$"{Tasks}.Task.Yield()"] = (g, _) => LlvmContext.Null(g.Module.Llvm.Ptr);
        t[$"{Tasks}.Task.FromResult(TResult)"] = (g, inv) =>
            g.CompletedTask(Arg(g, inv, 0), ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments[0]);
        t[$"{Tasks}.ValueTask.FromResult(TResult)"] = t[$"{Tasks}.Task.FromResult(TResult)"];

        IntrinsicHandler whenAll = (g, inv) =>
        {
            var tasks = g.TaskArrayArgument(inv.Arguments[0]);
            var result = ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments.FirstOrDefault() is IArrayTypeSymbol array ? array.ElementType : null;
            var size = result is null ? 0 : (long)g.Module.Types.Target.SizeOf(g.Module.Types.Storage(result));
            return g.CallRuntime("csrt_task_when_all", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], tasks, g.Module.Llvm.ConstInt(g.Module.Llvm.I32, size));
        };
        t[$"{Tasks}.Task.WhenAll({Tasks}.Task[])"] = whenAll;
        t[$"{Tasks}.Task.WhenAll(System.ReadOnlySpan<{Tasks}.Task>)"] = whenAll;
        t[$"{Tasks}.Task.WhenAll({Tasks}.Task<TResult>[])"] = whenAll;
        t[$"{Tasks}.Task.WhenAll(System.ReadOnlySpan<{Tasks}.Task<TResult>>)"] = whenAll;
        IntrinsicHandler whenAny = (g, inv) =>
            g.CallRuntime("csrt_task_when_any", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], g.TaskArrayArgument(inv.Arguments[0]));
        t[$"{Tasks}.Task.WhenAny({Tasks}.Task[])"] = whenAny;
        IntrinsicHandler whenAnyPair = (g, inv) =>
            g.CallRuntime("csrt_task_when_any", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr],
                g.ObjectArray([.. inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(a => a.Value)]));
        t[$"{Tasks}.Task.WhenAny({Tasks}.Task, {Tasks}.Task)"] = whenAnyPair;
        t[$"{Tasks}.Task.WhenAny({Tasks}.Task<TResult>, {Tasks}.Task<TResult>)"] = whenAnyPair;
        t[$"{Tasks}.Task.WhenAny(System.ReadOnlySpan<{Tasks}.Task>)"] = whenAny;
        t[$"{Tasks}.Task.WhenAny({Tasks}.Task<TResult>[])"] = whenAny;
        t[$"{Tasks}.Task.WhenAny(System.ReadOnlySpan<{Tasks}.Task<TResult>>)"] = whenAny;

        // Task.Run queues the work on the single-threaded scheduler (see docs/10-async.md).
        t[$"{Tasks}.Task.Run(System.Action)"] = (g, inv) =>
            g.CallRuntime("csrt_task_run_action", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], Arg(g, inv, 0));
        t[$"{Tasks}.Task.Run(System.Func<{Tasks}.Task>)"] = (g, inv) =>
            g.CallRuntime("csrt_task_run_func_task", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], Arg(g, inv, 0), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 0));
        t[$"{Tasks}.Task.Run(System.Func<{Tasks}.Task<TResult>>)"] = (g, inv) =>
        {
            var result = ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments[0];
            var size = (long)g.Module.Types.Target.SizeOf(g.Module.Types.Storage(result));
            return g.CallRuntime("csrt_task_run_func_task", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], Arg(g, inv, 0), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, size));
        };
        // Func<T> with a non-task T: run it now (a C runtime can't call an arbitrary-signature delegate).
        t[$"{Tasks}.Task.Run(System.Func<TResult>)"] = (g, inv) =>
        {
            var result = ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments[0];
            var value = g.InvokeDelegate0(Arg(g, inv, 0), g.Module.Types.Value(result));
            return g.CompletedTask(value, result);
        };

        foreach (var type in new[] { "Task", "Task<TResult>", "ValueTask", "ValueTask<TResult>" })
        {
            t[$"{Tasks}.{type}.GetAwaiter()"] = self;
            t[$"{Tasks}.{type}.ConfigureAwait(bool)"] = self;
            t[$"{Tasks}.{type}.Wait()"] = (g, inv) => { Wait(g, g.EmitValue(inv.Instance!)); return default; };
        }
        t[$"{Tasks}.ValueTask.AsTask()"] = (g, inv) =>
            g.CallRuntime("csrt_valuetask_as_task", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], g.EmitValue(inv.Instance!), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 0));
        t[$"{Tasks}.ValueTask<TResult>.AsTask()"] = (g, inv) =>
        {
            var result = ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments[0];
            var size = (long)g.Module.Types.Target.SizeOf(g.Module.Types.Storage(result));
            return g.CallRuntime("csrt_valuetask_as_task", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], g.EmitValue(inv.Instance!), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, size));
        };
        foreach (var awaiter in new[] { $"{Rcs}.TaskAwaiter", $"{Rcs}.TaskAwaiter<TResult>", $"{Rcs}.ValueTaskAwaiter", $"{Rcs}.ValueTaskAwaiter<TResult>",
                     $"{Rcs}.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter", $"{Rcs}.ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter",
                     $"{Rcs}.ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter", $"{Rcs}.ConfiguredValueTaskAwaitable<TResult>.ConfiguredValueTaskAwaiter" })
            t[$"{awaiter}.GetResult()"] = (g, inv) => g.TaskResult(Wait(g, g.EmitValue(inv.Instance!)), ResultOf(g, inv));
        foreach (var awaitable in new[] { $"{Rcs}.ConfiguredTaskAwaitable", $"{Rcs}.ConfiguredTaskAwaitable<TResult>",
                     $"{Rcs}.ConfiguredValueTaskAwaitable", $"{Rcs}.ConfiguredValueTaskAwaitable<TResult>" })
            t[$"{awaitable}.GetAwaiter()"] = self;
    }

    private static void AddTaskProperties(Dictionary<string, PropertyHandler> t)
    {
        PropertyHandler isCompleted = (g, p) => g.CallRuntime("csrt_task_is_completed", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
        foreach (var type in new[] { $"{Tasks}.Task", $"{Tasks}.ValueTask", $"{Tasks}.ValueTask<TResult>", $"{Rcs}.TaskAwaiter", $"{Rcs}.TaskAwaiter<TResult>",
                     $"{Rcs}.ValueTaskAwaiter", $"{Rcs}.ValueTaskAwaiter<TResult>" })
            t[$"{type}.IsCompleted"] = isCompleted;
        t[$"{Tasks}.Task.IsCompletedSuccessfully"] = isCompleted;
        PropertyHandler result = (g, p) =>
        {
            var task = g.EmitValue(p.Instance!);
            g.CallRuntime("csrt_task_wait", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], task);
            return g.TaskResult(task, g.TypeOf(p));
        };
        t[$"{Tasks}.Task<TResult>.Result"] = result;
        t[$"{Tasks}.ValueTask<TResult>.Result"] = result;
        t[$"{Tasks}.Task.CompletedTask"] = (g, _) => g.CallRuntime("csrt_task_completed_task", g.Module.Llvm.Ptr, []);
        t[$"{Tasks}.ValueTask.CompletedTask"] = t[$"{Tasks}.Task.CompletedTask"];
    }

    private static FrozenDictionary<string, ConstructorHandler> BuildConstructors()
    {
        Dictionary<string, ConstructorHandler> t = [];
        t[$"{Tasks}.ValueTask<TResult>..ctor(TResult)"] = (g, c) =>
            g.CompletedTask(CtorArg(g, c, 0), ((INamedTypeSymbol)g.TypeOf(c)!).TypeArguments[0]);
        t[$"{Tasks}.ValueTask..ctor({Tasks}.Task)"] = (g, c) => CtorArg(g, c, 0);
        t[$"{Tasks}.ValueTask<TResult>..ctor({Tasks}.Task<TResult>)"] = (g, c) => CtorArg(g, c, 0);
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
        AddTaskProperties(t);
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
            g.ArrayEnumeratorCurrent(g.EmitValue(p.Instance!), g.TypeOf(p)!);
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

    /// <summary>Receiver for System.Object's virtuals: value types (without an override) are boxed first.</summary>
    private static LLVMValueRef ObjectReceiver(FunctionCodeGen g, IOperation instance)
    {
        var value = g.EmitValue(instance);
        if (instance.Type is { IsValueType: true } valueType) return g.Box(value, valueType, instance);
        g.NullCheck(value);
        return value;
    }

    /// <summary>Which console stream a TextWriter/TextReader receiver is ("Out", "Error", "In").</summary>
    private static string ConsoleStream(IInvocationOperation inv) =>
        inv.Instance is IPropertyReferenceOperation { Property.ContainingType.Name: "Console", Property.Name: var name }
            ? name
            : throw CompileError.Unsupported("TextWriter/TextReader other than Console.Out/Error/In", inv);

    private static void WriteTo(FunctionCodeGen g, IInvocationOperation inv, IOperation? value, bool newline)
    {
        var l = g.Module.Llvm;
        if (ConsoleStream(inv) == "Out")
        {
            if (value is not null) WriteValue(g, value);
            if (newline) NewLine(g);
            return;
        }
        var text = value is null ? g.Module.StringLiteral("") : g.ToStringValue(value);
        if (newline) text = g.ConcatStrings(text, g.Module.StringLiteral("\n"));
        g.CallRuntime("csrt_console_error_write_string", l.Void, [l.Ptr], text);
    }

    private static void NewLine(FunctionCodeGen g) => g.CallRuntime("csrt_console_newline", g.Module.Llvm.Void, []);

    private static void WriteValue(FunctionCodeGen g, IOperation argument)
    {
        var l = g.Module.Llvm;
        var op = FunctionCodeGen.Unbox(argument);
        var type = g.TypeOf(op);
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

    private static LLVMValueRef CallBinary(FunctionCodeGen g, string name, LLVMTypeRef type, LLVMValueRef x, LLVMValueRef y)
    {
        var f = g.Module.Intrinsic(name, type, type, type);
        return g.Builder.Call(f.Type, f.Fn, [x, y]);
    }

    private static LLVMValueRef SelectCompare(FunctionCodeGen g, LLVMValueRef a, LLVMValueRef b, LLVMIntPredicate keepAWhen) =>
        g.Builder.Select(g.Builder.ICmp(keepAWhen, a, b), a, b);
}
