using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>More BCL members: string overloads with sets/comparisons, char classes, primitive
/// Equals/GetHashCode, BitConverter, Math extras, generic-math Create*, Environment.ExitCode.</summary>
internal static partial class BclValueTypes
{
    private static void AddMoreMethods(Dictionary<string, BclHandler> t)
    {
        // ---- string.Trim / TrimStart / TrimEnd with explicit characters --------------------------
        foreach (var (name, mode) in new[] { ("Trim", 3), ("TrimStart", 1), ("TrimEnd", 2) })
        {
            t[$"string.{name}(char)"] = (g, _, self, a, _) =>
            {
                var slot = g.NewTemp(g.Llvm.I16, "trimchar");
                g.Builder.Store(a[0], slot);
                return Rt(g, "csrt_string_trim_set", g.Llvm.Ptr, (self, g.Llvm.Ptr), (slot, g.Llvm.Ptr), (I32(g, 1), g.Llvm.I32), (I32(g, mode), g.Llvm.I32));
            };
            t[$"string.{name}(char[])"] = (g, _, self, a, _) =>
                Rt(g, "csrt_string_trim_set_array", g.Llvm.Ptr, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (I32(g, mode), g.Llvm.I32));
            t[$"string.{name}(System.ReadOnlySpan<char>)"] = (g, _, self, a, _) =>
                Rt(g, "csrt_string_trim_set", g.Llvm.Ptr, (self, g.Llvm.Ptr), (g.Builder.ExtractValue(a[0], 0), g.Llvm.Ptr),
                    (g.Builder.ExtractValue(a[0], 1), g.Llvm.I32), (I32(g, mode), g.Llvm.I32));
        }

        // ---- Split / IndexOf / Contains / StartsWith / EndsWith overloads ------------------------
        t["string.Split(char, int, System.StringSplitOptions)"] = (g, _, self, a, _) =>
        {
            var array = Rt(g, "csrt_string_split_char_count", g.Llvm.Ptr, (self, g.Llvm.Ptr), (a[0], g.Llvm.I16), (a[1], g.Llvm.I32), (a[2], g.Llvm.I32));
            g.TagArray(array, g.Module.Compilation.GetSpecialType(SpecialType.System_String));
            return array;
        };
        t["string.IndexOf(string, System.StringComparison)"] = (g, _, self, a, _) =>
            Rt(g, "csrt_string_index_of_cmp", g.Llvm.I32, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (I32(g, 0), g.Llvm.I32), (a[1], g.Llvm.I32));
        t["string.IndexOf(string, int, System.StringComparison)"] = (g, _, self, a, _) =>
            Rt(g, "csrt_string_index_of_cmp", g.Llvm.I32, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32), (a[2], g.Llvm.I32));
        t["string.LastIndexOf(string)"] = (g, _, self, a, _) =>
            Rt(g, "csrt_string_last_index_of_cmp", g.Llvm.I32, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (I32(g, 4), g.Llvm.I32));
        t["string.LastIndexOf(string, System.StringComparison)"] = (g, _, self, a, _) =>
            Rt(g, "csrt_string_last_index_of_cmp", g.Llvm.I32, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32));
        t["string.Contains(string, System.StringComparison)"] = (g, _, self, a, _) =>
            g.Builder.ICmp(LLVMIntPredicate.SGE, Rt(g, "csrt_string_index_of_cmp", g.Llvm.I32, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr),
                (I32(g, 0), g.Llvm.I32), (a[1], g.Llvm.I32)), I32(g, 0));
        t["string.StartsWith(string, System.StringComparison)"] = (g, _, self, a, _) =>
            Rt(g, "csrt_string_starts_with_cmp", g.Llvm.I1, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32));
        t["string.EndsWith(string, System.StringComparison)"] = (g, _, self, a, _) =>
            Rt(g, "csrt_string_ends_with_cmp", g.Llvm.I1, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32));
        t["string.StartsWith(char)"] = (g, _, self, a, _) => FirstOrLastCharIs(g, self, a[0], last: false);
        t["string.EndsWith(char)"] = (g, _, self, a, _) => FirstOrLastCharIs(g, self, a[0], last: true);
        t["string.Equals(object)"] = (g, _, self, a, _) =>
            Rt(g, "csrt_string_equals_object", g.Llvm.I1, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr));

        // ---- char classes -------------------------------------------------------------------------
        foreach (var (name, fn) in new[] { ("IsSurrogate", "csrt_char_is_surrogate"), ("IsHighSurrogate", "csrt_char_is_high_surrogate"),
                     ("IsLowSurrogate", "csrt_char_is_low_surrogate"), ("IsSymbol", "csrt_char_is_symbol"),
                     ("IsControl", "csrt_char_is_control"), ("IsSeparator", "csrt_char_is_separator") })
            t[$"char.{name}(char)"] = (g, _, _, a, _) => Rt(g, fn, g.Llvm.I1, (a[0], g.Llvm.I16));

        // ---- primitive Equals(object) / GetHashCode() ------------------------------------------------
        foreach (var type in new[] { "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort", "char", "bool", "double", "float" })
        {
            t[$"{type}.Equals(object)"] = (g, m, self, a, at) =>
            {
                var boxed = g.Box(self, m.ContainingType, at);
                return Rt(g, "csrt_object_equals_static", g.Llvm.I1, (boxed, g.Llvm.Ptr), (a[0], g.Llvm.Ptr));
            };
            t[$"{type}.GetHashCode()"] = (g, m, self, _, _) => PrimitiveHash(g, self, m.ContainingType);
        }

        // ---- Console / StringBuilder ----------------------------------------------------------------
        t["System.Console.Write(char[])"] = (g, _, _, a, _) => { WriteChars(g, a[0], newline: false); return default; };
        t["System.Console.WriteLine(char[])"] = (g, _, _, a, _) => { WriteChars(g, a[0], newline: true); return default; };
        foreach (var arity in new[] { 1, 2, 3 })
        {
            var key = "System.Text.StringBuilder.AppendFormat(string, " + string.Join(", ", Enumerable.Repeat("object", arity)) + ")";
            t[key] = (g, _, self, a, _) =>
            {
                var nul = LlvmContext.Null(g.Llvm.Ptr);
                var array = Rt(g, "csrt_object_array3", g.Llvm.Ptr, (I32(g, arity), g.Llvm.I32), (a[1], g.Llvm.Ptr),
                    (arity > 1 ? a[2] : nul, g.Llvm.Ptr), (arity > 2 ? a[3] : nul, g.Llvm.Ptr));
                var text = Rt(g, "csrt_string_format", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (array, g.Llvm.Ptr));
                return Rt(g, "csrt_sb_append_string", g.Llvm.Ptr, (self, g.Llvm.Ptr), (text, g.Llvm.Ptr));
            };
        }
        t["System.Text.StringBuilder.AppendFormat(string, object[])"] = (g, _, self, a, _) =>
        {
            var text = Rt(g, "csrt_string_format", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr));
            return Rt(g, "csrt_sb_append_string", g.Llvm.Ptr, (self, g.Llvm.Ptr), (text, g.Llvm.Ptr));
        };

        // ---- Math / BitConverter ----------------------------------------------------------------------
        t["System.Math.IEEERemainder(double, double)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_math_ieee_remainder", g.Llvm.F64, (a[0], g.Llvm.F64), (a[1], g.Llvm.F64));
        foreach (var type in new[] { "double", "float" })
            t[$"System.Math.Clamp({type}, {type}, {type})"] = (g, _, _, a, _) =>
            {
                var b = g.Builder;
                // value < min ? min : value > max ? max : value (NaN passes through, like .NET)
                var lo = b.Select(b.FCmp(LLVMRealPredicate.OLT, a[0], a[1]), a[1], a[0]);
                return b.Select(b.FCmp(LLVMRealPredicate.OGT, lo, a[2]), a[2], lo);
            };
        foreach (var (type, signed) in new[] { ("long", true), ("uint", false), ("ulong", false), ("short", true), ("byte", false) })
            t[$"System.Math.Clamp({type}, {type}, {type})"] = (g, _, _, a, _) =>
            {
                var b = g.Builder;
                var lo = b.Select(b.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, a[0], a[1]), a[1], a[0]);
                return b.Select(b.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, lo, a[2]), a[2], lo);
            };
        t["System.BitConverter.DoubleToInt64Bits(double)"] = (g, _, _, a, _) => g.Builder.Cast(LLVMOpcode.BitCast, a[0], g.Llvm.I64);
        t["System.BitConverter.Int64BitsToDouble(long)"] = (g, _, _, a, _) => g.Builder.Cast(LLVMOpcode.BitCast, a[0], g.Llvm.F64);
        t["System.BitConverter.SingleToInt32Bits(float)"] = (g, _, _, a, _) => g.Builder.Cast(LLVMOpcode.BitCast, a[0], g.Llvm.I32);
        t["System.BitConverter.Int32BitsToSingle(int)"] = (g, _, _, a, _) => g.Builder.Cast(LLVMOpcode.BitCast, a[0], g.Llvm.F32);
        t["System.BitConverter.DoubleToUInt64Bits(double)"] = t["System.BitConverter.DoubleToInt64Bits(double)"];

        // ---- generic-math INumberBase<T>.Create{Checked,Saturating,Truncating}<TOther> on primitives ----
        foreach (var type in new[] { "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort", "double", "float", "char" })
            foreach (var (name, isChecked) in new[] { ("CreateChecked", true), ("CreateSaturating", false), ("CreateTruncating", false) })
            {
                BclHandler create = (g, m, _, a, at) =>
                {
                    var from = m.Parameters[0].Type;
                    var to = m.ContainingType;
                    return SymbolEqualityComparer.Default.Equals(from, to) ? a[0] : g.ConvertNumeric(a[0], from, to, at, isChecked);
                };
                t[$"{type}.{name}(TOther)"] = create;
                foreach (var other in new[] { "int", "long", "double", "float", "short", "byte", "uint", "ulong", "decimal" })
                    t[$"{type}.{name}({other})"] = create;
            }

        t["System.Delegate.GetInvocationList()"] = (g, _, self, _, _) =>
        {
            var array = Rt(g, "csrt_delegate_get_invocation_list", g.Llvm.Ptr, (self, g.Llvm.Ptr));
            g.TagArray(array, g.Module.Compilation.GetTypeByMetadataName("System.Delegate")!);
            return array;
        };

        // ---- Environment.ExitCode -----------------------------------------------------------------------
        t["System.Environment.get_ExitCode()"] = (g, _, _, _, _) => Rt(g, "csrt_environment_get_exit_code", g.Llvm.I32);
        t["System.Environment.set_ExitCode(int)"] = (g, _, _, a, _) =>
        {
            Rt(g, "csrt_environment_set_exit_code", g.Llvm.Void, (a[0], g.Llvm.I32));
            return default;
        };
    }

    private static LLVMValueRef FirstOrLastCharIs(FunctionCodeGen g, LLVMValueRef s, LLVMValueRef c, bool last)
    {
        var len = Rt(g, "csrt_string_length_checked", g.Llvm.I32, (s, g.Llvm.Ptr));
        var index = last ? g.Builder.BinOp(LLVMOpcode.Sub, len, I32(g, 1)) : I32(g, 0);
        var nonEmpty = g.Builder.ICmp(LLVMIntPredicate.SGT, len, I32(g, 0));
        var safeIndex = g.Builder.Select(nonEmpty, index, I32(g, 0));
        var ch = Rt(g, "csrt_string_char_at_unchecked", g.Llvm.I16, (s, g.Llvm.Ptr), (safeIndex, g.Llvm.I32));
        return g.Builder.BinOp(LLVMOpcode.And, nonEmpty, g.Builder.ICmp(LLVMIntPredicate.EQ, ch, c));
    }

    private static void WriteChars(FunctionCodeGen g, LLVMValueRef chars, bool newline)
    {
        var text = Rt(g, "csrt_string_new_from_chars", g.Llvm.Ptr, (chars, g.Llvm.Ptr));
        Rt(g, "csrt_console_write_string", g.Llvm.Void, (text, g.Llvm.Ptr));
        if (newline) Rt(g, "csrt_console_newline", g.Llvm.Void);
    }

    /// <summary>GetHashCode() of a primitive, with .NET's exact formulas.</summary>
    private static LLVMValueRef PrimitiveHash(FunctionCodeGen g, LLVMValueRef v, ITypeSymbol type)
    {
        var b = g.Builder;
        var l = g.Llvm;
        switch (type.SpecialType)
        {
            case SpecialType.System_Int32 or SpecialType.System_UInt32: return v;
            case SpecialType.System_Int64 or SpecialType.System_UInt64:
                return b.BinOp(LLVMOpcode.Xor, b.Cast(LLVMOpcode.Trunc, v, l.I32), b.Cast(LLVMOpcode.Trunc, b.BinOp(LLVMOpcode.LShr, v, l.ConstInt(l.I64, 32)), l.I32));
            case SpecialType.System_Int16:   // (ushort)v | (v << 16)
                return b.BinOp(LLVMOpcode.Or, b.Cast(LLVMOpcode.ZExt, v, l.I32), b.BinOp(LLVMOpcode.Shl, b.Cast(LLVMOpcode.SExt, v, l.I32), I32(g, 16)));
            case SpecialType.System_Char:    // c | (c << 16)
                return b.BinOp(LLVMOpcode.Or, b.Cast(LLVMOpcode.ZExt, v, l.I32), b.BinOp(LLVMOpcode.Shl, b.Cast(LLVMOpcode.ZExt, v, l.I32), I32(g, 16)));
            case SpecialType.System_UInt16 or SpecialType.System_Byte: return b.Cast(LLVMOpcode.ZExt, v, l.I32);
            case SpecialType.System_SByte:   // v ^ (v << 8)
            {
                var w = b.Cast(LLVMOpcode.SExt, v, l.I32);
                return b.BinOp(LLVMOpcode.Xor, w, b.BinOp(LLVMOpcode.Shl, w, I32(g, 8)));
            }
            case SpecialType.System_Boolean: return b.Cast(LLVMOpcode.ZExt, v, l.I32);
            case SpecialType.System_Double: return Rt(g, "csrt_hash_double", l.I32, (v, l.F64));
            case SpecialType.System_Single: return Rt(g, "csrt_hash_float", l.I32, (v, l.F32));
        }
        throw CompileError.Unsupported($"GetHashCode on '{type}'", type);
    }
}
