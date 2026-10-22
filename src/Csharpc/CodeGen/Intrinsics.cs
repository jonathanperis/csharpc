using System.Collections.Frozen;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

internal delegate LLVMValueRef IntrinsicHandler(FunctionCodeGen g, IInvocationOperation invocation);
internal delegate LLVMValueRef PropertyHandler(FunctionCodeGen g, IPropertyReferenceOperation property);

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

    private static string TypeName(ITypeSymbol t) => t.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();

    public static bool TryGet(IMethodSymbol method, out IntrinsicHandler handler) =>
        s_methods.TryGetValue(Key(method.OriginalDefinition), out handler!);

    public static bool TryGetProperty(IPropertySymbol property, out PropertyHandler handler) =>
        s_properties.TryGetValue(PropertyKey(property.OriginalDefinition), out handler!);

    public static IEnumerable<string> All => s_methods.Keys.Concat(s_properties.Keys).Order();

    private static readonly FrozenDictionary<string, IntrinsicHandler> s_methods = BuildMethods();
    private static readonly FrozenDictionary<string, PropertyHandler> s_properties = BuildProperties();

    private static LLVMValueRef Arg(FunctionCodeGen g, IInvocationOperation inv, int ordinal)
    {
        var argument = inv.Arguments.First(a => a.Parameter!.Ordinal == ordinal);
        return g.Coerce(g.EmitValue(argument.Value), argument.Value.Type, argument.Parameter!.Type);
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

        // ---- System.Environment ----------------------------------------------------------------
        t["System.Environment.Exit(int)"] = (g, inv) =>
        {
            g.CallRuntime("csrt_environment_exit", g.Module.Llvm.Void, [g.Module.Llvm.I32], Arg(g, inv, 0));
            return default;
        };

        return t.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, PropertyHandler> BuildProperties()
    {
        Dictionary<string, PropertyHandler> t = [];
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
