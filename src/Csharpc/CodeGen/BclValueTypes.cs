using System.Collections.Frozen;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>Lowers one BCL member given the evaluated receiver (default if static) and arguments.</summary>
internal delegate LLVMValueRef BclHandler(FunctionCodeGen g, IMethodSymbol method, LLVMValueRef self, LLVMValueRef[] args, IOperation at);
internal delegate LLVMValueRef BclFieldHandler(FunctionCodeGen g, IOperation at);
/// <summary>Handlers that need the unevaluated invocation (params ReadOnlySpan&lt;T&gt; arguments).</summary>
internal delegate LLVMValueRef BclRawHandler(FunctionCodeGen g, IInvocationOperation invocation);

/// <summary>
/// BCL value types and friends, implemented natively:
///   System.Decimal   — CLR layout { i32 flags, i32 hi, i64 lo }; src/runtime/decimal.c
///   System.TimeSpan — an i64 tick count; System.DateTime — .NET's i64 _dateData (ticks in bits 0-61,
///   Kind in bits 62-63); corelib DateTimeOps.cs, clocks in src/runtime/datetime.c
///   System.Diagnostics.Stopwatch, System.Type (reflection-lite) — runtime objects
///   plus BitOperations, Array helpers, CompareTo, Convert, Environment extras.
/// Keys use the same format as <see cref="Intrinsics.Key"/> (accessors as get_X, ctors as .ctor,
/// operators as op_X). Covers methods, property getters, constructors and operator methods.
/// </summary>
internal static partial class BclValueTypes
{
    public static bool IsDecimal(ITypeSymbol? t) => t?.SpecialType == SpecialType.System_Decimal;
    public static bool IsDateTime(ITypeSymbol? t) => t?.SpecialType == SpecialType.System_DateTime;
    public static bool IsTimeSpan(ITypeSymbol? t) =>
        t is INamedTypeSymbol { Name: "TimeSpan", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
    public static bool IsTicks(ITypeSymbol? t) => IsTimeSpan(t) || IsDateTime(t);
    public static bool IsBigInteger(ITypeSymbol? t) =>
        t is INamedTypeSymbol { Name: "BigInteger", ContainingNamespace: { Name: "Numerics", ContainingNamespace: { Name: "System" } } };

    public static bool TryGet(IMethodSymbol method, out BclHandler handler)
    {
        var key = Intrinsics.Key(method.OriginalDefinition);
        if (s_methods.TryGetValue(key, out handler!)) return true;
        if (Int128Support.TryGet(method, out handler)) return true;
        // TimeSpan.FromDays/Hours/.../Microseconds integer overloads (.NET 9): sum of named units.
        if (IsTimeSpan(method.ContainingType) && method.IsStatic && method.Name.StartsWith("From", StringComparison.Ordinal)
            && method.Parameters.All(p => p.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64))
        {
            handler = TimeSpanFromUnits;
            return true;
        }
        return false;
    }

    public static bool TryGetRaw(IMethodSymbol method, out BclRawHandler handler) =>
        s_raw.TryGetValue(Intrinsics.Key(method.OriginalDefinition), out handler!);

    private static readonly FrozenDictionary<string, BclRawHandler> s_raw = BuildRaw();

    /// <summary>
    /// `params ReadOnlySpan&lt;T&gt;` arguments written as a collection expression or as loose params
    /// (C# 13 prefers these overloads): materialize the elements as strings into a string[].
    /// </summary>
    private static LLVMValueRef SpanArgumentAsStrings(FunctionCodeGen g, IInvocationOperation inv, int ordinal)
    {
        var value = inv.Arguments.First(a => a.Parameter!.Ordinal == ordinal).Value;
        while (value is IConversionOperation { Conversion.IsImplicit: true } c) value = c.Operand;
        var elements = value switch
        {
            ICollectionExpressionOperation coll when !coll.Elements.Any(e => e is ISpreadOperation) => coll.Elements,
            IArrayCreationOperation { Initializer: { } init } => init.ElementValues,
            _ => throw CompileError.Unsupported($"'{Intrinsics.Key(inv.TargetMethod.OriginalDefinition)}' with a span argument", inv),
        };
        var l = g.Llvm;
        var stringType = g.Module.Compilation.GetSpecialType(SpecialType.System_String);
        var array = g.CallRuntime("csrt_new_array", l.Ptr, [l.I64, l.I64], I64(g, (long)g.Types.PointerSize), I64(g, elements.Length));
        g.TagArray(array, stringType);
        for (var i = 0; i < elements.Length; i++)
            g.Builder.Store(g.ToStringValue(elements[i]), g.ArrayElementAddress(array, stringType, I64(g, i)));
        return array;
    }

    private static FrozenDictionary<string, BclRawHandler> BuildRaw()
    {
        Dictionary<string, BclRawHandler> t = [];
        LLVMValueRef separator(FunctionCodeGen g, IInvocationOperation inv)
        {
            var arg = inv.Arguments.First(a => a.Parameter!.Ordinal == 0);
            var v = g.EmitValue(arg.Value);
            return arg.Parameter!.Type.SpecialType == SpecialType.System_Char ? Rt(g, "csrt_string_from_char", g.Llvm.Ptr, (v, g.Llvm.I16)) : v;
        }
        foreach (var sep in new[] { "string", "char" })
            foreach (var element in new[] { "string", "object" })
                t[$"string.Join({sep}, System.ReadOnlySpan<{element}>)"] = (g, inv) =>
                {
                    var s = separator(g, inv);
                    return Rt(g, "csrt_string_join", g.Llvm.Ptr, (s, g.Llvm.Ptr), (SpanArgumentAsStrings(g, inv, 1), g.Llvm.Ptr));
                };
        foreach (var element in new[] { "string", "object" })
            t[$"string.Concat(System.ReadOnlySpan<{element}>)"] = (g, inv) =>
                Rt(g, "csrt_string_join", g.Llvm.Ptr, (g.Module.StringLiteral(""), g.Llvm.Ptr), (SpanArgumentAsStrings(g, inv, 0), g.Llvm.Ptr));
        // Composite formatting with 4+ arguments binds to params ReadOnlySpan<object?>.
        foreach (var (name, newline) in new[] { ("Write", false), ("WriteLine", true) })
            t[$"System.Console.{name}(string, System.ReadOnlySpan<object>)"] = (g, inv) =>
            {
                Write(g, Composite(g, inv), newline);
                return default;
            };
        t["string.Format(string, System.ReadOnlySpan<object>)"] = Composite;
        return t.ToFrozenDictionary();
    }

    /// <summary>string.Format(format, params ReadOnlySpan&lt;object?&gt;): box the arguments into an object[].</summary>
    private static LLVMValueRef Composite(FunctionCodeGen g, IInvocationOperation inv)
    {
        var format = g.EmitValue(inv.Arguments.First(a => a.Parameter!.Ordinal == 0).Value);
        var value = inv.Arguments.First(a => a.Parameter!.Ordinal == 1).Value;
        while (value is IConversionOperation { Conversion.IsImplicit: true } c) value = c.Operand;
        var elements = value switch
        {
            ICollectionExpressionOperation coll when !coll.Elements.Any(e => e is ISpreadOperation) => coll.Elements,
            IArrayCreationOperation { Initializer: { } init } => init.ElementValues,
            _ => throw CompileError.Unsupported("composite formatting with a span argument", inv),
        };
        var args = g.ObjectArray([.. elements]);
        return Rt(g, "csrt_string_format", g.Llvm.Ptr, (format, g.Llvm.Ptr), (args, g.Llvm.Ptr));
    }

    public static bool TryGetField(IFieldSymbol field, out BclFieldHandler handler) =>
        s_fields.TryGetValue($"{Intrinsics.TypeName(field.ContainingType)}.{field.Name}", out handler!);

    /// <summary>Keys of every member handled here (FrameworkIl: library types csharpc implements itself).</summary>
    public static IEnumerable<string> All => s_raw.Keys.Concat(s_methods.Keys).Concat(s_fields.Keys);

    private static readonly FrozenDictionary<string, BclHandler> s_methods = BuildMethods();
    private static readonly FrozenDictionary<string, BclFieldHandler> s_fields = BuildFields();

    private const long TicksPerMs = 10_000, TicksPerSecond = 10_000_000, TicksPerMinute = 600_000_000,
        TicksPerHour = 36_000_000_000, TicksPerDay = 864_000_000_000;

    // ---- helpers --------------------------------------------------------------------------------

    private static LLVMValueRef I64(FunctionCodeGen g, long v) => g.Llvm.ConstInt(g.Llvm.I64, v);

    // DateTime._dateData (dotnet/runtime DateTime.cs; corelib DateTimeOps.cs).
    private const long DateTimeTicksMask = 0x3FFF_FFFF_FFFF_FFFF, DateTimeKindUtc = 0x4000_0000_0000_0000;

    private static LLVMValueRef DateTicks(FunctionCodeGen g, LLVMValueRef data) =>
        g.Builder.BinOp(LLVMOpcode.And, data, I64(g, DateTimeTicksMask));

    /// <summary>The tick count of a TimeSpan or DateTime value: DateTime's Kind never takes part in
    /// equality, ordering or hashing.</summary>
    public static LLVMValueRef TicksOf(FunctionCodeGen g, ITypeSymbol t, LLVMValueRef v) => IsDateTime(t) ? DateTicks(g, v) : v;
    private static LLVMValueRef I32(FunctionCodeGen g, int v) => g.Llvm.ConstInt(g.Llvm.I32, v);
    private static LLVMValueRef ToI64(FunctionCodeGen g, LLVMValueRef v, bool signed = true) =>
        v.Type.IntWidth == 64 ? v : g.Builder.Cast(signed ? LLVMOpcode.SExt : LLVMOpcode.ZExt, v, g.Llvm.I64);
    private static LLVMValueRef ToI32(FunctionCodeGen g, LLVMValueRef v) =>
        v.Type.IntWidth == 32 ? v : g.Builder.Cast(v.Type.IntWidth > 32 ? LLVMOpcode.Trunc : LLVMOpcode.SExt, v, g.Llvm.I32);
    private static LLVMValueRef ToF64(FunctionCodeGen g, LLVMValueRef v) =>
        v.Type.Kind == LLVMTypeKind.Double ? v : v.Type.IsFloatingPoint
            ? g.Builder.Cast(LLVMOpcode.FPExt, v, g.Llvm.F64)
            : g.Builder.Cast(LLVMOpcode.SIToFP, v, g.Llvm.F64);

    private static LLVMValueRef Rt(FunctionCodeGen g, string fn, LLVMTypeRef ret, params (LLVMValueRef Value, LLVMTypeRef Type)[] args) =>
        g.CallRuntime(fn, ret, [.. args.Select(a => a.Type)], [.. args.Select(a => a.Value)]);

    /// <summary>Sign of a three-way comparison as int (CompareTo convention).</summary>
    private static LLVMValueRef ThreeWay(FunctionCodeGen g, LLVMValueRef less, LLVMValueRef greater)
    {
        var b = g.Builder;
        return b.BinOp(LLVMOpcode.Sub, b.Cast(LLVMOpcode.ZExt, greater, g.Llvm.I32), b.Cast(LLVMOpcode.ZExt, less, g.Llvm.I32));
    }

    private static LLVMValueRef ICompare(FunctionCodeGen g, LLVMValueRef a, LLVMValueRef c, bool signed) => ThreeWay(g,
        g.Builder.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, a, c),
        g.Builder.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, a, c));

    /// <summary>double.CompareTo: NaN sorts before everything and equals itself.</summary>
    private static LLVMValueRef FCompare(FunctionCodeGen g, LLVMValueRef a, LLVMValueRef c)
    {
        var b = g.Builder;
        var lt = b.BinOp(LLVMOpcode.Or, b.FCmp(LLVMRealPredicate.OLT, a, c),
            b.BinOp(LLVMOpcode.And, b.FCmp(LLVMRealPredicate.UNO, a, a), b.FCmp(LLVMRealPredicate.ORD, c, c)));
        var gt = b.BinOp(LLVMOpcode.Or, b.FCmp(LLVMRealPredicate.OGT, a, c),
            b.BinOp(LLVMOpcode.And, b.FCmp(LLVMRealPredicate.UNO, c, c), b.FCmp(LLVMRealPredicate.ORD, a, a)));
        return ThreeWay(g, lt, gt);
    }

    private static void Write(FunctionCodeGen g, LLVMValueRef text, bool newline)
    {
        g.CallRuntime("csrt_console_write_string", g.Llvm.Void, [g.Llvm.Ptr], text);
        if (newline) g.CallRuntime("csrt_console_newline", g.Llvm.Void, []);
    }

    /// <summary>The element type of an array argument (looking through conversions to IEnumerable/Array).</summary>
    private static ITypeSymbol? ArrayElementOf(FunctionCodeGen g, IOperation at, int ordinal)
    {
        if (at is not IInvocationOperation inv) return null;
        var arg = inv.Arguments.FirstOrDefault(a => a.Parameter!.Ordinal == ordinal)?.Value;
        while (arg is IConversionOperation c) arg = c.Operand;
        return g.TypeOf(arg) is IArrayTypeSymbol { Rank: 1 } array ? array.ElementType : null; // substituted: generic code copies T[]
    }

    private static bool IsSystemArray(FunctionCodeGen g, IOperation at, int ordinal)
    {
        if (at is not IInvocationOperation inv) return false;
        var arg = inv.Arguments.FirstOrDefault(a => a.Parameter!.Ordinal == ordinal)?.Value;
        while (arg is IConversionOperation c) arg = c.Operand;
        return g.TypeOf(arg) is { SpecialType: SpecialType.System_Array };
    }

    // Generic helpers (Array.Reverse<T>): the method is already substituted, so prefer its T.
    private static ITypeSymbol ElementOrThrow(FunctionCodeGen g, IMethodSymbol method, IOperation at, int ordinal) =>
        (method.TypeArguments.Length == 1 ? method.TypeArguments[0] : null)
        ?? ArrayElementOf(g, at, ordinal)
        ?? throw CompileError.Unsupported($"'{method}' on a non-array argument", at);

    private static LLVMValueRef ArrayCopy(FunctionCodeGen g, IMethodSymbol m, IOperation at, LLVMValueRef src, LLVMValueRef srcIndex,
        LLVMValueRef dst, LLVMValueRef dstIndex, LLVMValueRef length, bool longLength, ITypeSymbol? sourceElement = null, int? destinationOrdinal = null)
    {
        var dstOrdinal = destinationOrdinal ?? (m.Parameters.Length == 3 ? 1 : 2);
        var e = sourceElement ?? ElementOrThrow(g, m, at, 0);
        var de = ArrayElementOf(g, at, dstOrdinal);
        if (de is null && IsSystemArray(g, at, dstOrdinal))
        {
            // The destination's element type is only known at run time (ICollection.CopyTo(Array, int)).
            g.CallCoreLibGenericMethod("ArrayChecks", "CopyToUntyped", [e], src, srcIndex, dst, dstIndex, length, longLength ? g.Llvm.True : g.Llvm.False);
            return default;
        }
        if (de is null) throw CompileError.Unsupported($"'{m}' on a non-array argument", at);
        if (!SymbolEqualityComparer.Default.Equals(e, de)
            && !(e.IsReferenceType && de.IsReferenceType && g.Module.Compilation.ClassifyCommonConversion(e, de) is { IsImplicit: true, IsReference: true } or { IsIdentity: true }))
            throw CompileError.Unsupported($"Array.Copy from {e}[] to {de}[] (element conversion)", at);
        g.CallCoreLib("ArrayChecks", "Copy", src, srcIndex, dst, dstIndex, length, longLength ? g.Llvm.True : g.Llvm.False);
        Rt(g, "csrt_array_copy", g.Llvm.Void, (src, g.Llvm.Ptr), (srcIndex, g.Llvm.I64), (dst, g.Llvm.Ptr), (dstIndex, g.Llvm.I64),
            (length, g.Llvm.I64), (I64(g, (long)g.Types.Target.SizeOf(g.Types.Storage(e))), g.Llvm.I64));
        return default;
    }

    // ---- TimeSpan ----------------------------------------------------------------------------

    private static LLVMValueRef TimeSpanFromUnits(FunctionCodeGen g, IMethodSymbol method, LLVMValueRef self, LLVMValueRef[] args, IOperation at)
    {
        var units = new LLVMValueRef[6];
        string unitOf(string methodName) => methodName["From".Length..].ToLowerInvariant();
        string[] names = ["days", "hours", "minutes", "seconds", "milliseconds", "microseconds"];
        for (var i = 0; i < 6; i++) units[i] = I64(g, 0);
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var name = method.Parameters.Length == 1 ? unitOf(method.Name) : method.Parameters[i].Name.ToLowerInvariant();
            if (name == "ticks") return ToI64(g, args[i]);
            var slot = Array.IndexOf(names, name);
            if (slot < 0) throw CompileError.Unsupported($"'{method}'", at);
            units[slot] = ToI64(g, args[i]);
        }
        var l = g.Llvm;
        return Rt(g, "csrt_timespan_from_units", l.I64, [.. units.Select(u => (u, l.I64))]);
    }

    private static BclHandler FromDouble(long ticksPerUnit) => (g, _, _, a, _) =>
        Rt(g, "csrt_timespan_from_double", g.Llvm.I64, (ToF64(g, a[0]), g.Llvm.F64), (I64(g, ticksPerUnit), g.Llvm.I64));

    private static BclHandler TicksDiv(long unit, long modulo) => (g, _, self, _, _) =>
    {
        var b = g.Builder;
        var v = b.BinOp(LLVMOpcode.SDiv, self, I64(g, unit));
        if (modulo > 0) v = b.BinOp(LLVMOpcode.SRem, v, I64(g, modulo));
        return b.Cast(LLVMOpcode.Trunc, v, g.Llvm.I32);
    };

    private static BclHandler TotalUnits(double unit) => (g, _, self, _, _) =>
        g.Builder.BinOp(LLVMOpcode.FDiv, g.Builder.Cast(LLVMOpcode.SIToFP, self, g.Llvm.F64), LlvmContext.ConstReal(g.Llvm.F64, unit));

    private static BclHandler TicksCompare(LLVMIntPredicate predicate, bool date) => (g, _, self, a, _) =>
    {
        var (x, y) = self.Handle != 0 ? (self, a[0]) : (a[0], a[1]);
        return date ? g.Builder.ICmp(predicate, DateTicks(g, x), DateTicks(g, y)) : g.Builder.ICmp(predicate, x, y);
    };

    // ---- DateTime ----------------------------------------------------------------------------

    private static BclHandler DatePart(int part) => (g, _, self, _, _) =>
        Rt(g, "csrt_datetime_part", g.Llvm.I32, (self, g.Llvm.I64), (I32(g, part), g.Llvm.I32));

    private static BclHandler AddUnits(long ticksPerUnit) => (g, _, self, a, _) =>
        Rt(g, "csrt_datetime_add_units", g.Llvm.I64, (self, g.Llvm.I64), (ToF64(g, a[0]), g.Llvm.F64), (I64(g, ticksPerUnit), g.Llvm.I64));

    /// <summary>new DateTime(y, M, d[, h, m, s[, ms]][, kind]).</summary>
    private static LLVMValueRef NewDateTime(FunctionCodeGen g, LLVMValueRef[] a, int count, bool kind = false)
    {
        var l = g.Llvm;
        var parts = new LLVMValueRef[kind ? 8 : 7];
        for (var i = 0; i < 7; i++) parts[i] = i < count ? ToI32(g, a[i]) : I32(g, 0);
        if (kind)
        {
            parts[7] = ToI32(g, a[count]);
            return g.CallCoreLib("DateTimeOps", "NewKind", parts);
        }
        return Rt(g, "csrt_datetime_new", l.I64, [.. parts.Select(p => (p, l.I32))]);
    }

    // ---- table ---------------------------------------------------------------------------------

    private static FrozenDictionary<string, BclHandler> BuildMethods()
    {
        Dictionary<string, BclHandler> t = [];

        // ===== System.Decimal =====
        const string D = "decimal";
        foreach (var (op, fn) in new[] { ("Add", "csrt_decimal_add"), ("Subtract", "csrt_decimal_sub"), ("Multiply", "csrt_decimal_mul"),
                     ("Divide", "csrt_decimal_div"), ("Remainder", "csrt_decimal_rem") })
            t[$"{D}.{op}({D}, {D})"] = (g, _, _, a, _) => g.DecimalCall(fn, [], a[0], a[1]);
        t[$"{D}.Negate({D})"] = (g, _, _, a, _) => g.DecimalNegate(a[0]);
        t[$"{D}.Compare({D}, {D})"] = (g, _, _, a, _) => g.DecimalCompare(a[0], a[1]);
        t[$"{D}.CompareTo({D})"] = (g, _, self, a, _) => g.DecimalCompare(self, a[0]);
        t[$"{D}.Equals({D})"] = (g, _, self, a, at) => g.DecimalArithmetic(BinaryOperatorKind.Equals, self, a[0], at);
        t[$"{D}.Equals({D}, {D})"] = (g, _, _, a, at) => g.DecimalArithmetic(BinaryOperatorKind.Equals, a[0], a[1], at);
        t[$"{D}.GetHashCode()"] = (g, _, self, _, _) => Rt(g, "csrt_decimal_get_hash_code", g.Llvm.I32, (g.Spill(self), g.Llvm.Ptr));
        t[$"{D}.ToString()"] = (g, _, self, _, _) => g.CallCoreLib("Number", "DecimalToString", self);
        t[$"{D}.ToString(string)"] = (g, _, self, a, _) =>
            g.CallCoreLib("Number", "FormatDecimal", self, a[0]);
        t[$"{D}.ToString(System.IFormatProvider)"] = (g, _, self, a, _) => g.CallCoreLib("Number", "FormatDecimalWith", self, LlvmContext.Null(g.Llvm.Ptr), a[0]);
        t[$"{D}.ToString(string, System.IFormatProvider)"] = (g, _, self, a, _) => g.CallCoreLib("Number", "FormatDecimalWith", self, a[0], a[1]);
        BclHandler round(int? digits, int? mode) => (g, m, _, a, _) =>
        {
            var l = g.Llvm;
            var i = 1;
            var d = digits.HasValue ? I32(g, digits.Value) : ToI32(g, a[i++]);
            var md = mode.HasValue ? I32(g, mode.Value) : ToI32(g, a[i]);
            return g.DecimalCall("csrt_decimal_round", [l.I32, l.I32], a[0], d, md);
        };
        foreach (var owner in new[] { D, "System.Math" })
        {
            t[$"{owner}.Round({D})"] = round(0, 0);
            t[$"{owner}.Round({D}, int)"] = round(null, 0);
            t[$"{owner}.Round({D}, System.MidpointRounding)"] = (g, m, s, a, at) => round(0, null)(g, m, s, [a[0], a[1]], at);
            t[$"{owner}.Round({D}, int, System.MidpointRounding)"] = round(null, null);
            t[$"{owner}.Floor({D})"] = round(0, 3);
            t[$"{owner}.Ceiling({D})"] = round(0, 4);
            t[$"{owner}.Truncate({D})"] = round(0, 2);
        }
        // Round(decimal, MidpointRounding): digits fixed at 0, mode from args[1]
        t[$"{D}.Round({D}, System.MidpointRounding)"] = t[$"System.Math.Round({D}, System.MidpointRounding)"] = (g, _, _, a, _) =>
            g.DecimalCall("csrt_decimal_round", [g.Llvm.I32, g.Llvm.I32], a[0], I32(g, 0), ToI32(g, a[1]));
        t[$"System.Math.Abs({D})"] = t[$"{D}.Abs({D})"] = (g, _, _, a, at) =>
        {
            var b = g.Builder;
            var negative = g.DecimalArithmetic(BinaryOperatorKind.LessThan, a[0], g.DecimalConstant(0m), at);
            var negated = g.DecimalNegate(a[0]);
            return b.Select(negative, negated, a[0]);
        };
        t[$"System.Math.Max({D}, {D})"] = t[$"{D}.Max({D}, {D})"] = (g, _, _, a, at) =>
            g.Builder.Select(g.DecimalArithmetic(BinaryOperatorKind.GreaterThanOrEqual, a[0], a[1], at), a[0], a[1]);
        t[$"System.Math.Min({D}, {D})"] = t[$"{D}.Min({D}, {D})"] = (g, _, _, a, at) =>
            g.Builder.Select(g.DecimalArithmetic(BinaryOperatorKind.LessThanOrEqual, a[0], a[1], at), a[0], a[1]);
        t[$"System.Math.Sign({D})"] = t[$"{D}.Sign({D})"] = (g, _, _, a, _) =>
        {
            var c = g.DecimalCompare(a[0], g.DecimalConstant(0m));
            return ICompare(g, c, I32(g, 0), true);
        };
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("uint", false), ("ulong", false) })
            t[$"{D}..ctor({type})"] = (g, _, _, a, _) => signed
                ? g.DecimalCall("csrt_decimal_from_i64", [g.Llvm.I64], ToI64(g, a[0]))
                : g.DecimalCall("csrt_decimal_from_u64", [g.Llvm.I64], ToI64(g, a[0], false));
        t[$"{D}..ctor(double)"] = (g, _, _, a, _) => g.DecimalCall("csrt_decimal_from_f64", [g.Llvm.F64], a[0]);
        t[$"{D}..ctor(float)"] = (g, _, _, a, _) => g.DecimalCall("csrt_decimal_from_f32", [g.Llvm.F32], a[0]);
        t[$"{D}..ctor(int, int, int, bool, byte)"] = (g, _, _, a, _) =>
        {
            // new decimal(lo, mid, hi, isNegative, scale)
            var b = g.Builder;
            var l = g.Llvm;
            var flags = b.BinOp(LLVMOpcode.Or,
                b.BinOp(LLVMOpcode.Shl, b.Cast(LLVMOpcode.ZExt, a[4], l.I32), I32(g, 16)),
                b.Select(a[3], I32(g, unchecked((int)0x80000000)), I32(g, 0)));
            var lo = b.BinOp(LLVMOpcode.Or, b.Cast(LLVMOpcode.ZExt, a[0], l.I64),
                b.BinOp(LLVMOpcode.Shl, b.Cast(LLVMOpcode.ZExt, a[1], l.I64), I64(g, 32)));
            var v = LlvmContext.Null(g.DecimalType);
            v = b.InsertValue(v, flags, 0);
            v = b.InsertValue(v, a[2], 1);
            return b.InsertValue(v, lo, 2);
        };
        foreach (var w in new[] { "Write", "WriteLine" })
        {
            t[$"System.Console.{w}({D})"] = (g, _, _, a, _) =>
            {
                Write(g, g.CallCoreLib("Number", "DecimalToString", a[0]), w == "WriteLine");
                return default;
            };
        }
        foreach (var target in new[] { "int", "long", "double" })
            t[$"{D}.To{(target == "int" ? "Int32" : target == "long" ? "Int64" : "Double")}({D})"] = (g, m, _, a, at) =>
                g.DecimalConvert(a[0], m.Parameters[0].Type, m.ReturnType, at);

        // ===== System.TimeSpan (i64 ticks) =====
        const string TS = "System.TimeSpan";
        t[$"{TS}..ctor(long)"] = (g, _, _, a, _) => ToI64(g, a[0]);
        t[$"{TS}..ctor(int, int, int)"] = (g, _, _, a, _) => Rt(g, "csrt_timespan_from_units", g.Llvm.I64,
            (I64(g, 0), g.Llvm.I64), (ToI64(g, a[0]), g.Llvm.I64), (ToI64(g, a[1]), g.Llvm.I64), (ToI64(g, a[2]), g.Llvm.I64), (I64(g, 0), g.Llvm.I64), (I64(g, 0), g.Llvm.I64));
        BclHandler tsCtor(int n) => (g, _, _, a, _) =>
        {
            var l = g.Llvm;
            var parts = new (LLVMValueRef, LLVMTypeRef)[6];
            for (var i = 0; i < 6; i++) parts[i] = (i < n ? ToI64(g, a[i]) : I64(g, 0), l.I64);
            return Rt(g, "csrt_timespan_from_units", l.I64, parts);
        };
        t[$"{TS}..ctor(int, int, int, int)"] = tsCtor(4);
        t[$"{TS}..ctor(int, int, int, int, int)"] = tsCtor(5);
        t[$"{TS}..ctor(int, int, int, int, int, int)"] = tsCtor(6);
        t[$"{TS}.FromDays(double)"] = FromDouble(TicksPerDay);
        t[$"{TS}.FromHours(double)"] = FromDouble(TicksPerHour);
        t[$"{TS}.FromMinutes(double)"] = FromDouble(TicksPerMinute);
        t[$"{TS}.FromSeconds(double)"] = FromDouble(TicksPerSecond);
        t[$"{TS}.FromMilliseconds(double)"] = FromDouble(TicksPerMs);
        t[$"{TS}.FromMicroseconds(double)"] = FromDouble(10);
        t[$"{TS}.FromTicks(long)"] = (g, _, _, a, _) => a[0];
        t[$"{TS}.get_Ticks()"] = (_, _, self, _, _) => self;
        t[$"{TS}.get_Days()"] = TicksDiv(TicksPerDay, 0);
        t[$"{TS}.get_Hours()"] = TicksDiv(TicksPerHour, 24);
        t[$"{TS}.get_Minutes()"] = TicksDiv(TicksPerMinute, 60);
        t[$"{TS}.get_Seconds()"] = TicksDiv(TicksPerSecond, 60);
        t[$"{TS}.get_Milliseconds()"] = TicksDiv(TicksPerMs, 1000);
        t[$"{TS}.get_Microseconds()"] = TicksDiv(10, 1000);
        t[$"{TS}.get_Nanoseconds()"] = (g, _, self, _, _) =>
            g.Builder.Cast(LLVMOpcode.Trunc, g.Builder.BinOp(LLVMOpcode.Mul, g.Builder.BinOp(LLVMOpcode.SRem, self, I64(g, 10)), I64(g, 100)), g.Llvm.I32);
        t[$"{TS}.get_TotalDays()"] = TotalUnits(TicksPerDay);
        t[$"{TS}.get_TotalHours()"] = TotalUnits(TicksPerHour);
        t[$"{TS}.get_TotalMinutes()"] = TotalUnits(TicksPerMinute);
        t[$"{TS}.get_TotalSeconds()"] = TotalUnits(TicksPerSecond);
        t[$"{TS}.get_TotalMilliseconds()"] = TotalUnits(TicksPerMs);
        t[$"{TS}.get_TotalMicroseconds()"] = TotalUnits(10);
        t[$"{TS}.get_TotalNanoseconds()"] = (g, _, self, _, _) =>
            g.Builder.BinOp(LLVMOpcode.FMul, g.Builder.Cast(LLVMOpcode.SIToFP, self, g.Llvm.F64), LlvmContext.ConstReal(g.Llvm.F64, 100));
        BclHandler tsBinary(string fn) => (g, _, self, a, _) =>
            Rt(g, fn, g.Llvm.I64, (self.Handle != 0 ? self : a[0], g.Llvm.I64), (self.Handle != 0 ? a[0] : a[1], g.Llvm.I64));
        t[$"{TS}.Add({TS})"] = t[$"{TS}.op_Addition({TS}, {TS})"] = tsBinary("csrt_timespan_add");
        t[$"{TS}.Subtract({TS})"] = t[$"{TS}.op_Subtraction({TS}, {TS})"] = tsBinary("csrt_timespan_sub");
        t[$"{TS}.Negate()"] = (g, _, self, _, _) => Rt(g, "csrt_timespan_negate", g.Llvm.I64, (self, g.Llvm.I64));
        t[$"{TS}.op_UnaryNegation({TS})"] = (g, _, _, a, _) => Rt(g, "csrt_timespan_negate", g.Llvm.I64, (a[0], g.Llvm.I64));
        t[$"{TS}.op_UnaryPlus({TS})"] = (_, _, _, a, _) => a[0];
        t[$"{TS}.Duration()"] = (g, _, self, _, _) => Rt(g, "csrt_timespan_duration", g.Llvm.I64, (self, g.Llvm.I64));
        t[$"{TS}.Multiply(double)"] = (g, _, self, a, _) => Rt(g, "csrt_timespan_multiply", g.Llvm.I64, (self, g.Llvm.I64), (a[0], g.Llvm.F64));
        t[$"{TS}.op_Multiply({TS}, double)"] = (g, _, _, a, _) => Rt(g, "csrt_timespan_multiply", g.Llvm.I64, (a[0], g.Llvm.I64), (a[1], g.Llvm.F64));
        t[$"{TS}.op_Multiply(double, {TS})"] = (g, _, _, a, _) => Rt(g, "csrt_timespan_multiply", g.Llvm.I64, (a[1], g.Llvm.I64), (a[0], g.Llvm.F64));
        t[$"{TS}.Divide(double)"] = (g, _, self, a, _) => Rt(g, "csrt_timespan_divide", g.Llvm.I64, (self, g.Llvm.I64), (a[0], g.Llvm.F64));
        t[$"{TS}.op_Division({TS}, double)"] = (g, _, _, a, _) => Rt(g, "csrt_timespan_divide", g.Llvm.I64, (a[0], g.Llvm.I64), (a[1], g.Llvm.F64));
        BclHandler tsRatio = (g, _, self, a, _) =>
        {
            var b = g.Builder;
            var (x, y) = self.Handle != 0 ? (self, a[0]) : (a[0], a[1]);
            return b.BinOp(LLVMOpcode.FDiv, b.Cast(LLVMOpcode.SIToFP, x, g.Llvm.F64), b.Cast(LLVMOpcode.SIToFP, y, g.Llvm.F64));
        };
        t[$"{TS}.Divide({TS})"] = t[$"{TS}.op_Division({TS}, {TS})"] = tsRatio;
        foreach (var owner in new[] { TS, "System.DateTime" })
        {
            var self = owner;
            // DateTime compares and hashes its ticks only: values of different Kind can be equal (.NET).
            var date = owner != TS;
            LLVMValueRef ticks(FunctionCodeGen g, LLVMValueRef v) => date ? DateTicks(g, v) : v;
            t[$"{owner}.op_Equality({self}, {self})"] = t[$"{owner}.Equals({self})"] = t[$"{owner}.Equals({self}, {self})"] = TicksCompare(LLVMIntPredicate.EQ, date);
            t[$"{owner}.op_Inequality({self}, {self})"] = TicksCompare(LLVMIntPredicate.NE, date);
            t[$"{owner}.op_LessThan({self}, {self})"] = TicksCompare(LLVMIntPredicate.SLT, date);
            t[$"{owner}.op_LessThanOrEqual({self}, {self})"] = TicksCompare(LLVMIntPredicate.SLE, date);
            t[$"{owner}.op_GreaterThan({self}, {self})"] = TicksCompare(LLVMIntPredicate.SGT, date);
            t[$"{owner}.op_GreaterThanOrEqual({self}, {self})"] = TicksCompare(LLVMIntPredicate.SGE, date);
            t[$"{owner}.CompareTo({self})"] = (g, _, s, a, _) => ICompare(g, ticks(g, s), ticks(g, a[0]), true);
            t[$"{owner}.Compare({self}, {self})"] = (g, _, _, a, _) => ICompare(g, ticks(g, a[0]), ticks(g, a[1]), true);
            t[$"{owner}.GetHashCode()"] = (g, _, s, _, _) =>
            {
                s = ticks(g, s);
                return g.Builder.BinOp(LLVMOpcode.Xor, g.Builder.Cast(LLVMOpcode.Trunc, s, g.Llvm.I32),
                    g.Builder.Cast(LLVMOpcode.Trunc, g.Builder.BinOp(LLVMOpcode.AShr, s, I64(g, 32)), g.Llvm.I32));
            };
        }
        t[$"{TS}.ToString()"] = (g, _, self, _, _) => Rt(g, "csrt_timespan_to_string", g.Llvm.Ptr, (self, g.Llvm.I64));
        t[$"{TS}.ToString(string)"] = (g, _, self, a, _) => Rt(g, "csrt_timespan_format", g.Llvm.Ptr, (self, g.Llvm.I64), (a[0], g.Llvm.Ptr));
        t[$"{TS}.ToString(string, System.IFormatProvider)"] = (g, _, self, a, _) => g.CallCoreLib("DateTimeOps", "TimeSpanFormatWith", self, a[0], a[1]);

        // ===== System.DateTime (i64 _dateData: ticks | Kind << 62; more members in corelib DateTimeMembers) =====
        const string DT = "System.DateTime";
        t[$"{DT}..ctor(long)"] = (g, _, _, a, _) => Rt(g, "csrt_datetime_from_ticks", g.Llvm.I64, (a[0], g.Llvm.I64));
        t[$"{DT}..ctor(long, System.DateTimeKind)"] = (g, _, _, a, _) => g.CallCoreLib("DateTimeOps", "FromTicksKind", a[0], ToI32(g, a[1]));
        t[$"{DT}..ctor(int, int, int)"] = (g, _, _, a, _) => NewDateTime(g, a, 3);
        t[$"{DT}..ctor(int, int, int, int, int, int)"] = (g, _, _, a, _) => NewDateTime(g, a, 6);
        t[$"{DT}..ctor(int, int, int, int, int, int, System.DateTimeKind)"] = (g, _, _, a, _) => NewDateTime(g, a, 6, kind: true);
        t[$"{DT}..ctor(int, int, int, int, int, int, int)"] = (g, _, _, a, _) => NewDateTime(g, a, 7);
        t[$"{DT}..ctor(int, int, int, int, int, int, int, System.DateTimeKind)"] = (g, _, _, a, _) => NewDateTime(g, a, 7, kind: true);
        string[] parts = ["Year", "Month", "Day", "DayOfYear", "DayOfWeek", "Hour", "Minute", "Second", "Millisecond", "Microsecond", "Nanosecond"];
        for (var i = 0; i < parts.Length; i++) t[$"{DT}.get_{parts[i]}()"] = DatePart(i);
        t[$"{DT}.get_Ticks()"] = (g, _, self, _, _) => DateTicks(g, self);
        // Date keeps every kind flag (.NET: (ticks - ticks % TicksPerDay) | InternalKind).
        t[$"{DT}.get_Date()"] = (g, _, self, _, _) => g.Builder.BinOp(LLVMOpcode.Sub, self, g.Builder.BinOp(LLVMOpcode.URem, DateTicks(g, self), I64(g, TicksPerDay)));
        t[$"{DT}.get_TimeOfDay()"] = (g, _, self, _, _) => g.Builder.BinOp(LLVMOpcode.URem, DateTicks(g, self), I64(g, TicksPerDay));
        t[$"{DT}.get_Kind()"] = (g, _, self, _, _) => g.CallCoreLib("DateTimeOps", "Kind", self);
        static LLVMValueRef utcNow(FunctionCodeGen g) =>
            g.Builder.BinOp(LLVMOpcode.Or, Rt(g, "csrt_datetime_utc_now", g.Llvm.I64), I64(g, DateTimeKindUtc));
        t[$"{DT}.get_UtcNow()"] = (g, _, _, _, _) => utcNow(g);
        t[$"{DT}.get_Now()"] = (g, _, _, _, _) => g.CallCoreLib("DateTimeOps", "Now", utcNow(g));
        t[$"{DT}.get_Today()"] = (g, _, _, _, _) => g.CallCoreLib("DateTimeOps", "Today", utcNow(g));
        t[$"{DT}.AddDays(double)"] = AddUnits(TicksPerDay);
        t[$"{DT}.AddHours(double)"] = AddUnits(TicksPerHour);
        t[$"{DT}.AddMinutes(double)"] = AddUnits(TicksPerMinute);
        t[$"{DT}.AddSeconds(double)"] = AddUnits(TicksPerSecond);
        t[$"{DT}.AddMilliseconds(double)"] = AddUnits(TicksPerMs);
        t[$"{DT}.AddMicroseconds(double)"] = AddUnits(10);
        t[$"{DT}.AddTicks(long)"] = t[$"{DT}.Add({TS})"] = (g, _, self, a, _) =>
            Rt(g, "csrt_datetime_add_ticks", g.Llvm.I64, (self, g.Llvm.I64), (a[0], g.Llvm.I64));
        t[$"{DT}.Subtract({TS})"] = (g, _, self, a, _) =>
            Rt(g, "csrt_datetime_add_ticks", g.Llvm.I64, (self, g.Llvm.I64), (g.Builder.Neg(a[0]), g.Llvm.I64));
        t[$"{DT}.Subtract({DT})"] = (g, _, self, a, _) => g.Builder.BinOp(LLVMOpcode.Sub, DateTicks(g, self), DateTicks(g, a[0]));
        t[$"{DT}.op_Addition({DT}, {TS})"] = (g, _, _, a, _) => g.CallCoreLib("DateTimeOps", "AddTicksOperator", a[0], a[1]);
        t[$"{DT}.op_Subtraction({DT}, {TS})"] = (g, _, _, a, _) => g.CallCoreLib("DateTimeOps", "AddTicksOperator", a[0], g.Builder.Neg(a[1]));
        t[$"{DT}.op_Subtraction({DT}, {DT})"] = (g, _, _, a, _) => g.Builder.BinOp(LLVMOpcode.Sub, DateTicks(g, a[0]), DateTicks(g, a[1]));
        t[$"{DT}.AddMonths(int)"] = (g, _, self, a, _) => Rt(g, "csrt_datetime_add_months", g.Llvm.I64, (self, g.Llvm.I64), (a[0], g.Llvm.I32));
        t[$"{DT}.AddYears(int)"] = (g, _, self, a, _) => Rt(g, "csrt_datetime_add_years", g.Llvm.I64, (self, g.Llvm.I64), (a[0], g.Llvm.I32));
        t[$"{DT}.DaysInMonth(int, int)"] = (g, _, _, a, _) => Rt(g, "csrt_datetime_days_in_month", g.Llvm.I32, (a[0], g.Llvm.I32), (a[1], g.Llvm.I32));
        t[$"{DT}.IsLeapYear(int)"] = (g, _, _, a, _) => Rt(g, "csrt_datetime_is_leap_year", g.Llvm.I1, (a[0], g.Llvm.I32));
        t[$"{DT}.ToString()"] = (g, _, self, _, _) => Rt(g, "csrt_datetime_to_string", g.Llvm.Ptr, (self, g.Llvm.I64));
        t[$"{DT}.ToString(string)"] = (g, _, self, a, _) => Rt(g, "csrt_datetime_format", g.Llvm.Ptr, (self, g.Llvm.I64), (a[0], g.Llvm.Ptr));
        t[$"{DT}.ToString(System.IFormatProvider)"] = (g, _, self, a, _) => g.CallCoreLib("DateTimeOps", "FormatWith", self, LlvmContext.Null(g.Llvm.Ptr), a[0]);
        t[$"{DT}.ToString(string, System.IFormatProvider)"] = (g, _, self, a, _) => g.CallCoreLib("DateTimeOps", "FormatWith", self, a[0], a[1]);
        // ToShortDateString & co. format with the current culture: corelib DateTimeMembers (DateTimeOps.cs).

        AddBigInteger(t);

        // ===== System.Diagnostics.Stopwatch =====
        const string SW = "System.Diagnostics.Stopwatch";
        t[$"{SW}..ctor()"] = (g, _, _, _, _) => Rt(g, "csrt_stopwatch_new", g.Llvm.Ptr, (g.Llvm.False, g.Llvm.I1));
        t[$"{SW}.StartNew()"] = (g, _, _, _, _) => Rt(g, "csrt_stopwatch_new", g.Llvm.Ptr, (g.Llvm.True, g.Llvm.I1));
        foreach (var (name, fn) in new[] { ("Start", "csrt_stopwatch_start"), ("Stop", "csrt_stopwatch_stop"),
                     ("Reset", "csrt_stopwatch_reset"), ("Restart", "csrt_stopwatch_restart") })
            t[$"{SW}.{name}()"] = (g, _, self, _, _) => { Rt(g, fn, g.Llvm.Void, (self, g.Llvm.Ptr)); return default; };
        t[$"{SW}.get_IsRunning()"] = (g, _, self, _, _) => Rt(g, "csrt_stopwatch_is_running", g.Llvm.I1, (self, g.Llvm.Ptr));
        t[$"{SW}.get_Elapsed()"] = (g, _, self, _, _) => Rt(g, "csrt_stopwatch_elapsed_timespan", g.Llvm.I64, (self, g.Llvm.Ptr));
        t[$"{SW}.get_ElapsedMilliseconds()"] = (g, _, self, _, _) => Rt(g, "csrt_stopwatch_elapsed_ms", g.Llvm.I64, (self, g.Llvm.Ptr));
        t[$"{SW}.get_ElapsedTicks()"] = (g, _, self, _, _) => Rt(g, "csrt_stopwatch_elapsed_raw", g.Llvm.I64, (self, g.Llvm.Ptr));
        t[$"{SW}.GetTimestamp()"] = (g, _, _, _, _) => Rt(g, "csrt_stopwatch_timestamp", g.Llvm.I64);
        // Stopwatch ticks are nanoseconds on Unix; QueryPerformanceCounter units on Windows (as in .NET).
        LLVMValueRef ToTimeSpan(FunctionCodeGen g, LLVMValueRef ticks) => g.Module.Types.Target.IsWindows
            ? Rt(g, "csrt_stopwatch_to_timespan", g.Llvm.I64, (ticks, g.Llvm.I64))
            : g.Builder.BinOp(LLVMOpcode.SDiv, ticks, I64(g, 100));
        t[$"{SW}.GetElapsedTime(long)"] = (g, _, _, a, _) =>
            ToTimeSpan(g, g.Builder.BinOp(LLVMOpcode.Sub, Rt(g, "csrt_stopwatch_timestamp", g.Llvm.I64), a[0]));
        t[$"{SW}.GetElapsedTime(long, long)"] = (g, _, _, a, _) => ToTimeSpan(g, g.Builder.BinOp(LLVMOpcode.Sub, a[1], a[0]));
        t[$"{SW}.ToString()"] = (g, _, self, _, _) =>
            Rt(g, "csrt_timespan_to_string", g.Llvm.Ptr, (Rt(g, "csrt_stopwatch_elapsed_timespan", g.Llvm.I64, (self, g.Llvm.Ptr)), g.Llvm.I64));

        // ===== System.Type (reflection-lite) =====
        BclHandler typeString(string fn) => (g, _, self, _, _) => Rt(g, fn, g.Llvm.Ptr, (self, g.Llvm.Ptr));
        t["System.Reflection.MemberInfo.get_Name()"] = t["System.Type.get_Name()"] = typeString("csrt_type_name");
        t["System.Type.get_Namespace()"] = typeString("csrt_type_namespace");
        t["System.Type.ToString()"] = typeString("csrt_type_full_name");
        t["System.Type.op_Equality(System.Type, System.Type)"] = (g, _, _, a, _) => g.Builder.ICmp(LLVMIntPredicate.EQ, a[0], a[1]);
        t["System.Type.op_Inequality(System.Type, System.Type)"] = (g, _, _, a, _) => g.Builder.ICmp(LLVMIntPredicate.NE, a[0], a[1]);
        t["System.Type.Equals(System.Type)"] = (g, _, self, a, _) => g.Builder.ICmp(LLVMIntPredicate.EQ, self, a[0]);

        // ===== CompareTo / Equals on primitives =====
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("short", true), ("sbyte", true),
                     ("uint", false), ("ulong", false), ("ushort", false), ("byte", false), ("char", false) })
        {
            t[$"{type}.CompareTo({type})"] = (g, _, self, a, _) => ICompare(g, self, a[0], signed);
            t[$"{type}.Equals({type})"] = (g, _, self, a, _) => g.Builder.ICmp(LLVMIntPredicate.EQ, self, a[0]);
        }
        foreach (var type in new[] { "double", "float" })
        {
            t[$"{type}.CompareTo({type})"] = (g, _, self, a, _) => FCompare(g, self, a[0]);
            t[$"{type}.Equals({type})"] = (g, _, self, a, _) =>
            {
                var b = g.Builder; // NaN.Equals(NaN) is true
                return b.BinOp(LLVMOpcode.Or, b.FCmp(LLVMRealPredicate.OEQ, self, a[0]),
                    b.BinOp(LLVMOpcode.And, b.FCmp(LLVMRealPredicate.UNO, self, self), b.FCmp(LLVMRealPredicate.UNO, a[0], a[0])));
            };
            t[$"{type}.IsNaN({type})"] = (g, _, _, a, _) => g.Builder.FCmp(LLVMRealPredicate.UNO, a[0], a[0]);
            t[$"{type}.IsInfinity({type})"] = (g, _, _, a, _) =>
            {
                var f = g.Module.Intrinsic(type == "double" ? "llvm.fabs.f64" : "llvm.fabs.f32", a[0].Type, a[0].Type);
                return g.Builder.FCmp(LLVMRealPredicate.OEQ, g.Builder.Call(f.Type, f.Fn, [a[0]]), LlvmContext.ConstReal(a[0].Type, double.PositiveInfinity));
            };
            t[$"{type}.IsFinite({type})"] = (g, _, _, a, _) =>
            {
                var f = g.Module.Intrinsic(type == "double" ? "llvm.fabs.f64" : "llvm.fabs.f32", a[0].Type, a[0].Type);
                return g.Builder.FCmp(LLVMRealPredicate.OLT, g.Builder.Call(f.Type, f.Fn, [a[0]]), LlvmContext.ConstReal(a[0].Type, double.PositiveInfinity));
            };
        }
        t["string.Compare(string, string)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_string_compare_culture", g.Llvm.I32, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr), (g.Llvm.False, g.Llvm.I1));
        t["string.Compare(string, string, bool)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_string_compare_culture", g.Llvm.I32, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr), (a[2], g.Llvm.I1));
        // OrdinalIgnoreCase (full simple case mapping, surrogate pairs) and argument validation in C#
        // (corelib OrdinalCasing); culture and ordinal comparisons stay in csrt.
        // A constant Ordinal / OrdinalIgnoreCase comparison calls the ordinal code directly: the general
        // entry points also reach the culture-sensitive comparisons, which would link CompareInfo (and the
        // culture model) into every program that compares strings ordinally.
        t["string.Compare(string, string, System.StringComparison)"] = (g, _, _, a, at) => ConstantComparison(at, 2) switch
        {
            StringComparison.Ordinal => g.CallCoreLib("StringOps", "CompareOrdinal", a[0], a[1]),
            StringComparison.OrdinalIgnoreCase => g.CallCoreLib("OrdinalCasing", "Compare", a[0], a[1]),
            _ => g.CallCoreLib("OrdinalCasing", "CompareWith", a[0], a[1], a[2]),
        };
        BclHandler equalsWith(int comparisonParameter) => (g, _, self, a, at) =>
        {
            var instance = comparisonParameter == 1;
            if (instance) g.NullCheck(self); // s.Equals(value, comparisonType) on a null s throws, like any instance call
            var (x, y) = instance ? (self, a[0]) : (a[0], a[1]);
            switch (ConstantComparison(at, comparisonParameter))
            {
                case StringComparison.Ordinal:
                    return g.CallRuntime("csrt_string_equals", g.Llvm.I1, [g.Llvm.Ptr, g.Llvm.Ptr], x, y);
                case StringComparison.OrdinalIgnoreCase:
                    // Against an ASCII constant, OrdinalIgnoreCase is ASCII case folding: .NET's ordinal casing
                    // never maps a non-ASCII char to an ASCII one (not even U+0131 or U+017F), so the Unicode case
                    // tables aren't needed.
                    if (ConstantAsciiOperand(at, instance) is { } which)
                        return g.CallCoreLib("OrdinalCasing", "EqualsAsciiIgnoreCase", which == 0 ? y : x, which == 0 ? x : y);
                    return g.CallCoreLib("OrdinalCasing", "Equals", x, y);
                default:
                    return g.CallCoreLib("OrdinalCasing", "EqualsWith", x, y, a[comparisonParameter]);
            }
        };
        t["string.Equals(string, string, System.StringComparison)"] = equalsWith(2);
        t["string.Equals(string, System.StringComparison)"] = equalsWith(1);
        t["System.Math.Round(double, System.MidpointRounding)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_math_round_mode", g.Llvm.F64, (a[0], g.Llvm.F64), (I32(g, 0), g.Llvm.I32), (a[1], g.Llvm.I32));
        t["System.Math.Round(double, int, System.MidpointRounding)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_math_round_mode", g.Llvm.F64, (a[0], g.Llvm.F64), (a[1], g.Llvm.I32), (a[2], g.Llvm.I32));
        // Generic-math statics (INumber<T> & co.) that code calls as double.Round(x), int.Max(a, b), ...
        foreach (var (type, bits) in new[] { ("double", 64), ("float", 32) })
        {
            var suffix = bits == 64 ? "f64" : "f32";
            BclHandler unary(string llvm) => (g, _, _, a, _) =>
            {
                var f = g.Module.Intrinsic($"llvm.{llvm}.{suffix}", a[0].Type, a[0].Type);
                return g.Builder.Call(f.Type, f.Fn, [a[0]]);
            };
            t[$"{type}.Round({type})"] = unary("roundeven");
            t[$"{type}.Floor({type})"] = unary("floor");
            t[$"{type}.Ceiling({type})"] = unary("ceil");
            t[$"{type}.Truncate({type})"] = unary("trunc");
            t[$"{type}.Sqrt({type})"] = unary("sqrt");
            t[$"{type}.Abs({type})"] = unary("fabs");
            t[$"{type}.Max({type}, {type})"] = (g, _, _, a, _) =>
            {
                var f = g.Module.Intrinsic($"llvm.maximum.{suffix}", a[0].Type, a[0].Type, a[0].Type);
                return g.Builder.Call(f.Type, f.Fn, [a[0], a[1]]);
            };
            t[$"{type}.Min({type}, {type})"] = (g, _, _, a, _) =>
            {
                var f = g.Module.Intrinsic($"llvm.minimum.{suffix}", a[0].Type, a[0].Type, a[0].Type);
                return g.Builder.Call(f.Type, f.Fn, [a[0], a[1]]);
            };
        }
        t["double.Round(double, int)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_math_round_mode", g.Llvm.F64, (a[0], g.Llvm.F64), (a[1], g.Llvm.I32), (I32(g, 0), g.Llvm.I32));
        t["double.Round(double, System.MidpointRounding)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_math_round_mode", g.Llvm.F64, (a[0], g.Llvm.F64), (I32(g, 0), g.Llvm.I32), (a[1], g.Llvm.I32));
        t["double.Round(double, int, System.MidpointRounding)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_math_round_mode", g.Llvm.F64, (a[0], g.Llvm.F64), (a[1], g.Llvm.I32), (a[2], g.Llvm.I32));
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("uint", false), ("ulong", false), ("short", true), ("byte", false) })
        {
            t[$"{type}.Max({type}, {type})"] = (g, _, _, a, _) =>
                g.Builder.Select(g.Builder.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, a[0], a[1]), a[0], a[1]);
            t[$"{type}.Min({type}, {type})"] = (g, _, _, a, _) =>
                g.Builder.Select(g.Builder.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, a[0], a[1]), a[0], a[1]);
            t[$"{type}.Clamp({type}, {type}, {type})"] = (g, _, _, a, _) =>
            {
                var b = g.Builder;
                var lo = b.Select(b.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, a[0], a[1]), a[1], a[0]);
                return b.Select(b.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, lo, a[2]), a[2], lo);
            };
            if (signed)
                t[$"{type}.Abs({type})"] = (g, _, _, a, _) =>
                {
                    var b = g.Builder; // .NET throws OverflowException for MinValue
                    g.FailIf(b.ICmp(LLVMIntPredicate.EQ, a[0], g.Llvm.ConstUInt(a[0].Type, 1UL << (int)(a[0].Type.IntWidth - 1))), "csrt_throw_overflow");
                    return b.Select(b.ICmp(LLVMIntPredicate.SLT, a[0], g.Llvm.ConstInt(a[0].Type, 0)), b.Neg(a[0]), a[0]);
                };
            t[$"{type}.Sign({type})"] = (g, _, _, a, _) => ICompare(g, a[0], g.Llvm.ConstInt(a[0].Type, 0), signed);
            t[$"{type}.IsPow2({type})"] = (g, _, _, a, _) =>
            {
                var b = g.Builder;
                var zero = g.Llvm.ConstInt(a[0].Type, 0);
                var positive = signed ? b.ICmp(LLVMIntPredicate.SGT, a[0], zero) : b.ICmp(LLVMIntPredicate.NE, a[0], zero);
                return b.BinOp(LLVMOpcode.And, positive,
                    b.ICmp(LLVMIntPredicate.EQ, b.BinOp(LLVMOpcode.And, a[0], b.BinOp(LLVMOpcode.Sub, a[0], g.Llvm.ConstInt(a[0].Type, 1))), zero));
            };
        }
        t["string.IsNullOrWhiteSpace(string)"] = (g, _, _, a, _) => Rt(g, "csrt_string_is_null_or_white_space", g.Llvm.I1, (a[0], g.Llvm.Ptr));
        t["object.ReferenceEquals(object, object)"] = (g, _, _, a, _) => g.Builder.ICmp(LLVMIntPredicate.EQ, a[0], a[1]);
        t["object.Equals(object, object)"] = (g, _, _, a, _) => Rt(g, "csrt_object_equals_static", g.Llvm.I1, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr));
        t["nint.get_Size()"] = t["System.IntPtr.get_Size()"] = t["nuint.get_Size()"] = (g, _, _, _, _) => I32(g, (int)g.Types.PointerSize);
        t["bool.CompareTo(bool)"] = (g, _, self, a, _) => ICompare(g, self, a[0], false);
        t["bool.Parse(string)"] = (g, _, _, a, _) => Rt(g, "csrt_bool_parse", g.Llvm.I1, (a[0], g.Llvm.Ptr));
        t["double.ToString(System.IFormatProvider)"] = (g, _, self, a, _) => g.CallCoreLib("Number", "FormatDoubleWith", self, LlvmContext.Null(g.Llvm.Ptr), g.Llvm.False, a[0]);
        t["double.ToString(string, System.IFormatProvider)"] = (g, _, self, a, _) => g.CallCoreLib("Number", "FormatDoubleWith", self, a[0], g.Llvm.False, a[1]);
        t["char.GetNumericValue(char)"] = (g, _, _, a, _) =>
        {
            var b = g.Builder;
            var l = g.Llvm;
            var isDigit = b.BinOp(LLVMOpcode.And, b.ICmp(LLVMIntPredicate.UGE, a[0], l.ConstInt(l.I16, '0')), b.ICmp(LLVMIntPredicate.ULE, a[0], l.ConstInt(l.I16, '9')));
            var value = b.Cast(LLVMOpcode.UIToFP, b.BinOp(LLVMOpcode.Sub, a[0], l.ConstInt(l.I16, '0')), l.F64);
            return b.Select(isDigit, value, LlvmContext.ConstReal(l.F64, -1));
        };

        // ===== System.Convert =====
        t["System.Convert.ToInt32(string)"] = (g, _, _, a, _) => Rt(g, "csrt_int32_parse", g.Llvm.I32, (a[0], g.Llvm.Ptr));
        t["System.Convert.ToInt64(string)"] = (g, _, _, a, _) => Rt(g, "csrt_int64_parse", g.Llvm.I64, (a[0], g.Llvm.Ptr));
        t["System.Convert.ToDouble(string)"] = (g, _, _, a, _) => Rt(g, "csrt_double_parse", g.Llvm.F64, (a[0], g.Llvm.Ptr));
        t["System.Convert.ToInt32(string, int)"] = (g, _, _, a, _) =>
            g.Builder.Cast(LLVMOpcode.Trunc, Rt(g, "csrt_convert_parse_base", g.Llvm.I64, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32), (I32(g, 32), g.Llvm.I32)), g.Llvm.I32);
        t["System.Convert.ToInt64(string, int)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_convert_parse_base", g.Llvm.I64, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32), (I32(g, 64), g.Llvm.I32));
        t["System.Convert.ToString(int, int)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_convert_to_base", g.Llvm.Ptr, (ToI64(g, a[0]), g.Llvm.I64), (a[1], g.Llvm.I32), (I32(g, 32), g.Llvm.I32));
        t["System.Convert.ToString(long, int)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_convert_to_base", g.Llvm.Ptr, (a[0], g.Llvm.I64), (a[1], g.Llvm.I32), (I32(g, 64), g.Llvm.I32));
        t["System.Convert.ToInt32(double)"] = (g, _, _, a, _) =>
            Rt(g, "csrt_convert_double_to_i32", g.Llvm.I32, (a[0], g.Llvm.F64));
        t["System.Convert.ToInt32(bool)"] = (g, _, _, a, _) => g.Builder.Cast(LLVMOpcode.ZExt, a[0], g.Llvm.I32);
        t["System.Convert.ToInt32(char)"] = (g, _, _, a, _) => g.Builder.Cast(LLVMOpcode.ZExt, a[0], g.Llvm.I32);
        t["System.Convert.ToInt32(long)"] = (g, _, _, a, at) => g.ConvertNumeric(a[0], g.Module.Compilation.GetSpecialType(SpecialType.System_Int64),
            g.Module.Compilation.GetSpecialType(SpecialType.System_Int32), at, isChecked: true);
        t["System.Convert.ToDouble(int)"] = (g, _, _, a, _) => g.Builder.Cast(LLVMOpcode.SIToFP, a[0], g.Llvm.F64);
        t["System.Convert.ToChar(int)"] = (g, _, _, a, at) => g.ConvertNumeric(a[0], g.Module.Compilation.GetSpecialType(SpecialType.System_Int32),
            g.Module.Compilation.GetSpecialType(SpecialType.System_Char), at, isChecked: true);
        t["System.Convert.ToBoolean(string)"] = (g, _, _, a, _) => Rt(g, "csrt_bool_parse", g.Llvm.I1, (a[0], g.Llvm.Ptr));

        // ===== System.Environment =====
        t["System.Environment.get_ProcessorCount()"] = (g, _, _, _, _) => Rt(g, "csrt_environment_processor_count", g.Llvm.I32);
        t["System.Environment.get_TickCount()"] = (g, _, _, _, _) =>
            g.Builder.Cast(LLVMOpcode.Trunc, Rt(g, "csrt_environment_tick_count64", g.Llvm.I64), g.Llvm.I32);
        t["System.Environment.GetCommandLineArgs()"] = (g, _, _, _, at) =>
        {
            var array = Rt(g, "csrt_environment_command_line_args", g.Llvm.Ptr);
            g.TagArray(array, g.Module.Compilation.GetSpecialType(SpecialType.System_String));
            return array;
        };
        t["System.Environment.get_Is64BitProcess()"] = (g, _, _, _, _) => g.Llvm.ConstInt(g.Llvm.I1, g.Types.PointerSize == 8 ? 1 : 0);

        // ===== System.Numerics.BitOperations =====
        const string BO = "System.Numerics.BitOperations";
        foreach (var (type, bits) in new[] { ("uint", 32), ("ulong", 64), ("int", 32), ("long", 64), ("nuint", 64) })
        {
            var w = bits;
            t[$"{BO}.PopCount({type})"] = (g, _, _, a, _) => BitIntrinsic(g, "ctpop", a[0], w, null);
            t[$"{BO}.LeadingZeroCount({type})"] = (g, _, _, a, _) => BitIntrinsic(g, "ctlz", a[0], w, g.Llvm.False);
            t[$"{BO}.TrailingZeroCount({type})"] = (g, _, _, a, _) => BitIntrinsic(g, "cttz", a[0], w, g.Llvm.False);
            t[$"{BO}.Log2({type})"] = (g, _, _, a, _) =>
            {
                var b = g.Builder; // 31 - lzcnt(x | 1)
                var x = b.BinOp(LLVMOpcode.Or, a[0], g.Llvm.ConstInt(a[0].Type, 1));
                return b.BinOp(LLVMOpcode.Sub, I32(g, w - 1), BitIntrinsic(g, "ctlz", x, w, g.Llvm.False));
            };
            t[$"{BO}.IsPow2({type})"] = (g, _, _, a, _) =>
            {
                var b = g.Builder;
                var zero = g.Llvm.ConstInt(a[0].Type, 0);
                var positive = type is "int" or "long" ? b.ICmp(LLVMIntPredicate.SGT, a[0], zero) : b.ICmp(LLVMIntPredicate.NE, a[0], zero);
                var single = b.ICmp(LLVMIntPredicate.EQ, b.BinOp(LLVMOpcode.And, a[0], b.BinOp(LLVMOpcode.Sub, a[0], g.Llvm.ConstInt(a[0].Type, 1))), zero);
                return b.BinOp(LLVMOpcode.And, positive, single);
            };
            t[$"{BO}.RotateLeft({type}, int)"] = (g, _, _, a, _) => Rotate(g, "fshl", a[0], a[1]);
            t[$"{BO}.RotateRight({type}, int)"] = (g, _, _, a, _) => Rotate(g, "fshr", a[0], a[1]);
        }
        foreach (var (type, bits) in new[] { ("uint", 32), ("ulong", 64), ("int", 32), ("long", 64) })
        {
            var w = bits;
            t[$"{type}.PopCount({type})"] = (g, _, _, a, _) => Widen(g, BitIntrinsic(g, "ctpop", a[0], w, null), a[0].Type);
            t[$"{type}.LeadingZeroCount({type})"] = (g, _, _, a, _) => Widen(g, BitIntrinsic(g, "ctlz", a[0], w, g.Llvm.False), a[0].Type);
            t[$"{type}.TrailingZeroCount({type})"] = (g, _, _, a, _) => Widen(g, BitIntrinsic(g, "cttz", a[0], w, g.Llvm.False), a[0].Type);
        }

        // ===== System.Array (single-dimensional) =====
        AddArrayMethods(t);
        AddMoreMethods(t);
        AddReflectionMethods(t);

        // ===== string.Join over arrays / IEnumerable<T> backed by arrays =====
        t["string.Join(string, System.Collections.Generic.IEnumerable<string>)"] = (g, m, _, a, at) => JoinArray(g, m, a, at, charSeparator: false);
        t["string.Join(string, System.Collections.Generic.IEnumerable<T>)"] = (g, m, _, a, at) => JoinArray(g, m, a, at, charSeparator: false);
        t["string.Join(string, object[])"] = (g, m, _, a, at) => JoinArray(g, m, a, at, charSeparator: false);
        t["string.Join(char, string[])"] = (g, m, _, a, at) => JoinArray(g, m, a, at, charSeparator: true);
        t["string.Join(char, object[])"] = (g, m, _, a, at) => JoinArray(g, m, a, at, charSeparator: true);
        t["string.Concat(string[])"] = (g, _, _, a, _) => Rt(g, "csrt_string_join", g.Llvm.Ptr, (g.Module.StringLiteral(""), g.Llvm.Ptr), (a[0], g.Llvm.Ptr));
        t["string.Split(string, System.StringSplitOptions)"] = (g, _, self, a, _) =>
        {
            var array = Rt(g, "csrt_string_split_string", g.Llvm.Ptr, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32));
            g.TagArray(array, g.Module.Compilation.GetSpecialType(SpecialType.System_String));
            return array;
        };
        t["string.Split(char[])"] = (g, _, self, a, _) =>
        {
            var array = Rt(g, "csrt_string_split_chars", g.Llvm.Ptr, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (I32(g, 0), g.Llvm.I32));
            g.TagArray(array, g.Module.Compilation.GetSpecialType(SpecialType.System_String));
            return array;
        };
        t["string.Split(char[], System.StringSplitOptions)"] = (g, _, self, a, _) =>
        {
            var array = Rt(g, "csrt_string_split_chars", g.Llvm.Ptr, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32));
            g.TagArray(array, g.Module.Compilation.GetSpecialType(SpecialType.System_String));
            return array;
        };

        // ===== Math extras =====
        t["System.Math.BigMul(int, int)"] = (g, _, _, a, _) => g.Builder.BinOp(LLVMOpcode.Mul, ToI64(g, a[0]), ToI64(g, a[1]));
        t["System.Math.DivRem(int, int, int)"] = (g, _, _, a, at) =>
        {
            var q = g.Arithmetic(BinaryOperatorKind.Divide, a[0], a[1], g.Module.Compilation.GetSpecialType(SpecialType.System_Int32),
                g.Module.Compilation.GetSpecialType(SpecialType.System_Int32), at);
            var r = g.Builder.BinOp(LLVMOpcode.Sub, a[0], g.Builder.BinOp(LLVMOpcode.Mul, q, a[1]));
            g.Builder.Store(r, a[2]);
            return q;
        };
        return t.ToFrozenDictionary();
    }

    // ---- System.Numerics.BigInteger (src/runtime/bigint.c; value = immutable bignum pointer) -----------

    private static LLVMValueRef ToBig(FunctionCodeGen g, LLVMValueRef v, ITypeSymbol type)
    {
        var l = g.Llvm;
        if (IsBigInteger(type)) return v;
        if (TypeMapper.IsSigned(type)) return Rt(g, "csrt_bigint_from_i64", l.Ptr, (ToI64(g, v), l.I64));
        return Rt(g, "csrt_bigint_from_u64", l.Ptr, (ToI64(g, v, false), l.I64));
    }

    private static void AddBigInteger(Dictionary<string, BclHandler> t)
    {
        const string B = "System.Numerics.BigInteger";
        BclHandler binary(string fn) => (g, m, self, a, _) =>
        {
            var l = g.Llvm;
            var (x, xt, y, yt) = self.Handle != 0
                ? (self, (ITypeSymbol)m.ContainingType, a[0], m.Parameters[0].Type)
                : (a[0], m.Parameters[0].Type, a[1], m.Parameters[1].Type);
            return Rt(g, fn, l.Ptr, (ToBig(g, x, xt), l.Ptr), (ToBig(g, y, yt), l.Ptr));
        };
        foreach (var (op, method, fn) in new[] { ("op_Addition", "Add", "csrt_bigint_add"), ("op_Subtraction", "Subtract", "csrt_bigint_sub"),
                     ("op_Multiply", "Multiply", "csrt_bigint_mul"), ("op_Division", "Divide", "csrt_bigint_div"), ("op_Modulus", "Remainder", "csrt_bigint_rem") })
        {
            t[$"{B}.{op}({B}, {B})"] = binary(fn);
            t[$"{B}.{method}({B}, {B})"] = binary(fn);
        }
        t[$"{B}.GreatestCommonDivisor({B}, {B})"] = binary("csrt_bigint_gcd");
        t[$"{B}.op_UnaryNegation({B})"] = t[$"{B}.Negate({B})"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_negate", g.Llvm.Ptr, (a[0], g.Llvm.Ptr));
        t[$"{B}.op_UnaryPlus({B})"] = (_, _, _, a, _) => a[0];
        t[$"{B}.Abs({B})"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_abs", g.Llvm.Ptr, (a[0], g.Llvm.Ptr));
        t[$"{B}.op_Increment({B})"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_add", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (Rt(g, "csrt_bigint_from_i64", g.Llvm.Ptr, (I64(g, 1), g.Llvm.I64)), g.Llvm.Ptr));
        t[$"{B}.op_Decrement({B})"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_sub", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (Rt(g, "csrt_bigint_from_i64", g.Llvm.Ptr, (I64(g, 1), g.Llvm.I64)), g.Llvm.Ptr));
        t[$"{B}.Pow({B}, int)"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_pow", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.I32));
        t[$"{B}.ModPow({B}, {B}, {B})"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_mod_pow", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr), (a[2], g.Llvm.Ptr));
        t[$"{B}.DivRem({B}, {B}, {B})"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_divrem", g.Llvm.Ptr, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr), (a[2], g.Llvm.Ptr));
        BclHandler compare(Func<FunctionCodeGen, LLVMValueRef, LLVMValueRef> fromSign) => (g, m, self, a, _) =>
        {
            var l = g.Llvm;
            var (x, xt, y, yt) = self.Handle != 0
                ? (self, (ITypeSymbol)m.ContainingType, a[0], m.Parameters[0].Type)
                : (a[0], m.Parameters[0].Type, a[1], m.Parameters[1].Type);
            var c = Rt(g, "csrt_bigint_compare", l.I32, (ToBig(g, x, xt), l.Ptr), (ToBig(g, y, yt), l.Ptr));
            return fromSign(g, c);
        };
        LLVMValueRef sign(FunctionCodeGen g, LLVMValueRef c, LLVMIntPredicate p) => g.Builder.ICmp(p, c, I32(g, 0));
        string[] operands = [B, "long", "ulong"];
        foreach (var left in operands)
            foreach (var right in operands)
            {
                if (left != B && right != B) continue;
                foreach (var (op, p) in new[] { ("op_Equality", LLVMIntPredicate.EQ), ("op_Inequality", LLVMIntPredicate.NE), ("op_LessThan", LLVMIntPredicate.SLT),
                             ("op_LessThanOrEqual", LLVMIntPredicate.SLE), ("op_GreaterThan", LLVMIntPredicate.SGT), ("op_GreaterThanOrEqual", LLVMIntPredicate.SGE) })
                    t[$"{B}.{op}({left}, {right})"] = compare((g, c) => sign(g, c, p));
            }
        t[$"{B}.Compare({B}, {B})"] = compare((_, c) => c);
        t[$"{B}.CompareTo({B})"] = t[$"{B}.CompareTo(long)"] = t[$"{B}.CompareTo(ulong)"] = compare((_, c) => c);
        t[$"{B}.Equals({B})"] = t[$"{B}.Equals(long)"] = compare((g, c) => sign(g, c, LLVMIntPredicate.EQ));
        t[$"{B}.Max({B}, {B})"] = (g, _, _, a, _) =>
            g.Builder.Select(sign(g, Rt(g, "csrt_bigint_compare", g.Llvm.I32, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr)), LLVMIntPredicate.SGE), a[0], a[1]);
        t[$"{B}.Min({B}, {B})"] = (g, _, _, a, _) =>
            g.Builder.Select(sign(g, Rt(g, "csrt_bigint_compare", g.Llvm.I32, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr)), LLVMIntPredicate.SLE), a[0], a[1]);
        foreach (var source in new[] { "int", "long", "uint", "ulong", "short", "ushort", "byte", "sbyte", "char" })
        {
            t[$"{B}.op_Implicit({source})"] = (g, m, _, a, _) => ToBig(g, a[0], m.Parameters[0].Type);
            if (source is "int" or "long" or "uint" or "ulong") t[$"{B}..ctor({source})"] = (g, m, _, a, _) => ToBig(g, a[0], m.Parameters[0].Type);
        }
        BclHandler fromDouble = (g, _, _, a, _) => Rt(g, "csrt_bigint_from_f64", g.Llvm.Ptr, (ToF64(g, a[0]), g.Llvm.F64));
        t[$"{B}.op_Explicit(double)"] = t[$"{B}.op_Explicit(float)"] = t[$"{B}..ctor(double)"] = t[$"{B}..ctor(float)"] = fromDouble;
        t[$"{B}.op_Explicit({B})"] = (g, m, _, a, at) =>
        {
            var l = g.Llvm;
            var to = m.ReturnType;
            if (to.SpecialType == SpecialType.System_Double) return Rt(g, "csrt_bigint_to_f64", l.F64, (a[0], l.Ptr));
            if (to.SpecialType == SpecialType.System_Single)
                return g.Builder.Cast(LLVMOpcode.FPTrunc, Rt(g, "csrt_bigint_to_f64", l.F64, (a[0], l.Ptr)), l.F32);
            var kind = to.SpecialType switch
            {
                SpecialType.System_Int32 => 5, SpecialType.System_UInt32 => 6, SpecialType.System_Int64 => 7, SpecialType.System_UInt64 => 8,
                _ => throw CompileError.Unsupported($"(explicit) BigInteger to '{to}'", at),
            };
            var raw = Rt(g, "csrt_bigint_to_integer", l.I64, (a[0], l.Ptr), (I32(g, kind), l.I32));
            return kind is 5 or 6 ? g.Builder.Cast(LLVMOpcode.Trunc, raw, l.I32) : raw;
        };
        t[$"{B}.Parse(string)"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_parse", g.Llvm.Ptr, (a[0], g.Llvm.Ptr));
        t[$"{B}.TryParse(string, {B})"] = (g, _, _, a, _) => Rt(g, "csrt_bigint_try_parse", g.Llvm.I1, (a[0], g.Llvm.Ptr), (a[1], g.Llvm.Ptr));
        t[$"{B}.ToString()"] = (g, _, self, _, _) => Rt(g, "csrt_bigint_to_string", g.Llvm.Ptr, (self, g.Llvm.Ptr));
        t[$"{B}.ToString(string)"] = (g, _, self, a, _) => Rt(g, "csrt_bigint_format", g.Llvm.Ptr, (self, g.Llvm.Ptr), (a[0], g.Llvm.Ptr));
        t[$"{B}.GetHashCode()"] = (g, _, self, _, _) => Rt(g, "csrt_bigint_get_hash_code", g.Llvm.I32, (self, g.Llvm.Ptr));
        t[$"{B}.get_IsZero()"] = (g, _, self, _, _) => g.Builder.ICmp(LLVMIntPredicate.EQ, self, LlvmContext.Null(g.Llvm.Ptr));
        t[$"{B}.get_IsOne()"] = (g, _, self, _, _) =>
            sign(g, Rt(g, "csrt_bigint_compare", g.Llvm.I32, (self, g.Llvm.Ptr), (Rt(g, "csrt_bigint_from_i64", g.Llvm.Ptr, (I64(g, 1), g.Llvm.I64)), g.Llvm.Ptr)), LLVMIntPredicate.EQ);
        t[$"{B}.get_IsEven()"] = (g, _, self, _, _) => Rt(g, "csrt_bigint_is_even", g.Llvm.I1, (self, g.Llvm.Ptr));
        t[$"{B}.get_Sign()"] = (g, _, self, _, _) => Rt(g, "csrt_bigint_sign", g.Llvm.I32, (self, g.Llvm.Ptr));
        t[$"{B}.get_Zero()"] = (g, _, _, _, _) => LlvmContext.Null(g.Llvm.Ptr);
        t[$"{B}.get_One()"] = (g, _, _, _, _) => Rt(g, "csrt_bigint_from_i64", g.Llvm.Ptr, (I64(g, 1), g.Llvm.I64));
        t[$"{B}.get_MinusOne()"] = (g, _, _, _, _) => Rt(g, "csrt_bigint_from_i64", g.Llvm.Ptr, (I64(g, -1), g.Llvm.I64));
    }

    private static LLVMValueRef Widen(FunctionCodeGen g, LLVMValueRef i32, LLVMTypeRef to) =>
        to.IntWidth == 32 ? i32 : g.Builder.Cast(LLVMOpcode.ZExt, i32, to);

    /// <summary>llvm.ctpop/ctlz/cttz, result as int.</summary>
    private static LLVMValueRef BitIntrinsic(FunctionCodeGen g, string name, LLVMValueRef x, int bits, LLVMValueRef? zeroIsPoison)
    {
        var type = x.Type;
        var f = zeroIsPoison is { } flag
            ? g.Module.Intrinsic($"llvm.{name}.i{type.IntWidth}", type, type, g.Llvm.I1)
            : g.Module.Intrinsic($"llvm.{name}.i{type.IntWidth}", type, type);
        var r = zeroIsPoison is { } z ? g.Builder.Call(f.Type, f.Fn, [x, z]) : g.Builder.Call(f.Type, f.Fn, [x]);
        _ = bits;
        return type.IntWidth == 32 ? r : g.Builder.Cast(LLVMOpcode.Trunc, r, g.Llvm.I32);
    }

    private static LLVMValueRef Rotate(FunctionCodeGen g, string name, LLVMValueRef x, LLVMValueRef n)
    {
        var type = x.Type;
        var f = g.Module.Intrinsic($"llvm.{name}.i{type.IntWidth}", type, type, type, type);
        var amount = type.IntWidth == 32 ? n : g.Builder.Cast(LLVMOpcode.ZExt, n, type);
        return g.Builder.Call(f.Type, f.Fn, [x, x, amount]);
    }

    // ---- System.Array ---------------------------------------------------------------------------

    /// <summary>Element kind codes shared with src/runtime/bclmisc.c (sorting, searching, equality).</summary>
    private static int ElementKind(ITypeSymbol element) => element.SpecialType switch
    {
        SpecialType.System_SByte => 1, SpecialType.System_Int16 => 2, SpecialType.System_Int32 => 3, SpecialType.System_Int64 => 4,
        SpecialType.System_Byte or SpecialType.System_Boolean => 5, SpecialType.System_UInt16 or SpecialType.System_Char => 6,
        SpecialType.System_UInt32 => 7, SpecialType.System_UInt64 => 8,
        SpecialType.System_Single => 9, SpecialType.System_Double => 10, SpecialType.System_String => 11,
        _ when element.TypeKind == TypeKind.Enum => ElementKind(((INamedTypeSymbol)element).EnumUnderlyingType!),
        _ when element.IsReferenceType => 12,   // reference: virtual Equals; not sortable
        _ => 0,                                  // other value types: bytewise equality
    };

    private static void AddArrayMethods(Dictionary<string, BclHandler> t)
    {
        const string A = "System.Array";
        LLVMValueRef size(FunctionCodeGen g, ITypeSymbol e) => I64(g, (long)g.Types.Target.SizeOf(g.Types.Storage(e)));

        BclHandler reverse = (g, m, _, a, at) =>
        {
            var e = ElementOrThrow(g, m, at, 0);
            Rt(g, "csrt_array_reverse", g.Llvm.Void, (a[0], g.Llvm.Ptr), (I64(g, 0), g.Llvm.I64), (I64(g, long.MinValue), g.Llvm.I64), (size(g, e), g.Llvm.I64));
            return default;
        };
        t[$"{A}.Reverse(T[])"] = t[$"{A}.Reverse({A})"] = reverse;
        t[$"{A}.Reverse({A}, int, int)"] = t[$"{A}.Reverse(T[], int, int)"] = (g, m, _, a, at) =>
        {
            var e = ElementOrThrow(g, m, at, 0);
            Rt(g, "csrt_array_reverse", g.Llvm.Void, (a[0], g.Llvm.Ptr), (ToI64(g, a[1]), g.Llvm.I64), (ToI64(g, a[2]), g.Llvm.I64), (size(g, e), g.Llvm.I64));
            return default;
        };
        t[$"{A}.Clear({A})"] = (g, m, _, a, at) =>
        {
            var e = ElementOrThrow(g, m, at, 0);
            Rt(g, "csrt_array_clear", g.Llvm.Void, (a[0], g.Llvm.Ptr), (I64(g, 0), g.Llvm.I64), (I64(g, -1), g.Llvm.I64), (size(g, e), g.Llvm.I64));
            return default;
        };
        t[$"{A}.Clear({A}, int, int)"] = (g, m, _, a, at) =>
        {
            var e = ElementOrThrow(g, m, at, 0);
            Rt(g, "csrt_array_clear", g.Llvm.Void, (a[0], g.Llvm.Ptr), (ToI64(g, a[1]), g.Llvm.I64), (ToI64(g, a[2]), g.Llvm.I64), (size(g, e), g.Llvm.I64));
            return default;
        };
        // Arguments are validated in C# (corelib ArrayChecks, .NET's order and messages); the copy is
        // a memmove of the source's element size, so element types must agree (or be a reference
        // conversion that needs no per-element check).
        t[$"{A}.Copy({A}, {A}, int)"] = (g, m, _, a, at) =>
            ArrayCopy(g, m, at, a[0], I64(g, 0), a[1], I64(g, 0), ToI64(g, a[2]), longLength: false);
        t[$"{A}.Copy({A}, {A}, long)"] = (g, m, _, a, at) =>
            ArrayCopy(g, m, at, a[0], I64(g, 0), a[1], I64(g, 0), ToI64(g, a[2]), longLength: true);
        t[$"{A}.Copy({A}, int, {A}, int, int)"] = (g, m, _, a, at) =>
            ArrayCopy(g, m, at, a[0], ToI64(g, a[1]), a[2], ToI64(g, a[3]), ToI64(g, a[4]), longLength: false);
        t[$"{A}.Fill(T[], T)"] = (g, m, _, a, at) =>
        {
            var e = ElementOrThrow(g, m, at, 0);
            var value = g.Spill(g.ToStorage(a[1], e), "fill");
            Rt(g, "csrt_array_fill", g.Llvm.Void, (a[0], g.Llvm.Ptr), (value, g.Llvm.Ptr), (size(g, e), g.Llvm.I64));
            return default;
        };
        t[$"{A}.IndexOf(T[], T)"] = t[$"{A}.IndexOf({A}, object)"] = (g, m, _, a, at) =>
        {
            var e = ElementOrThrow(g, m, at, 0);
            var value = g.Spill(g.ToStorage(a[1], e.IsReferenceType || m.Parameters[1].Type.IsReferenceType == false ? e : e), "needle");
            if (m.Parameters[1].Type.SpecialType == SpecialType.System_Object && e.IsValueType)
                throw CompileError.Unsupported("Array.IndexOf(Array, object) on value-type arrays", at);
            return g.Builder.Cast(LLVMOpcode.Trunc, Rt(g, "csrt_array_index_of", g.Llvm.I64, (a[0], g.Llvm.Ptr), (value, g.Llvm.Ptr),
                (size(g, e), g.Llvm.I64), (I32(g, ElementKind(e)), g.Llvm.I32)), g.Llvm.I32);
        };
        // Array.Sort = .NET's introsort (src/runtime/bclmisc.c), so even ties end up in the CLR's order.
        LLVMValueRef sortRange(FunctionCodeGen g, ITypeSymbol e, LLVMValueRef array, LLVMValueRef index, LLVMValueRef length, IOperation at)
        {
            var kind = ElementKind(e);
            var l = g.Llvm;
            if (kind is not (0 or 12))
            {
                if (index.Handle == 0) return Rt(g, "csrt_array_sort", l.Void, (array, l.Ptr), (I32(g, kind), l.I32));
                throw CompileError.Unsupported("Array.Sort(array, index, length) on primitives", at);
            }
            var cmp = g.Module.ComparableComparer(e) ?? throw CompileError.Unsupported($"Array.Sort on '{e}' (it does not implement IComparable<{e.Name}>)", at);
            return Rt(g, "csrt_array_introsort", l.Void, (array, l.Ptr), (index.Handle == 0 ? I64(g, 0) : ToI64(g, index), l.I64),
                (length.Handle == 0 ? I64(g, -1) : ToI64(g, length), l.I64), (size(g, e), l.I64), (cmp, l.Ptr), (LlvmContext.Null(l.Ptr), l.Ptr));
        }
        t[$"{A}.Sort(T[])"] = t[$"{A}.Sort({A})"] = (g, m, _, a, at) => { sortRange(g, ElementOrThrow(g, m, at, 0), a[0], default, default, at); return default; };
        t[$"{A}.Sort(T[], int, int)"] = (g, m, _, a, at) => { sortRange(g, ElementOrThrow(g, m, at, 0), a[0], a[1], a[2], at); return default; };
        t[$"{A}.Sort(T[], System.Comparison<T>)"] = (g, m, _, a, at) =>
        {
            var e = m.TypeArguments[0];
            var l = g.Llvm;
            g.NullCheck(a[1]);
            var cmp = g.Module.DelegateComparer(e, (INamedTypeSymbol)m.Parameters[1].Type);
            Rt(g, "csrt_array_introsort", l.Void, (a[0], l.Ptr), (I64(g, 0), l.I64), (I64(g, -1), l.I64), (size(g, e), l.I64), (cmp, l.Ptr), (a[1], l.Ptr));
            return default;
        };
        t[$"{A}.BinarySearch(T[], T)"] = (g, m, _, a, at) =>
        {
            var e = ElementOrThrow(g, m, at, 0);
            var kind = ElementKind(e);
            if (kind is 0 or 12) throw CompileError.Unsupported($"Array.BinarySearch on '{e}'", at);
            var value = g.Spill(g.ToStorage(a[1], e), "needle");
            return Rt(g, "csrt_array_binary_search", g.Llvm.I32, (a[0], g.Llvm.Ptr), (value, g.Llvm.Ptr), (I32(g, kind), g.Llvm.I32));
        };
        t[$"{A}.Resize(T[], int)"] = (g, m, _, a, at) =>
        {
            // a[0] is the address of the array variable (ref parameter)
            var e = m.TypeArguments[0];
            var old = g.Builder.Load(g.Llvm.Ptr, a[0]);
            var resized = Rt(g, "csrt_array_resize", g.Llvm.Ptr, (old, g.Llvm.Ptr), (ToI64(g, a[1]), g.Llvm.I64), (size(g, e), g.Llvm.I64));
            g.TagArray(resized, e);
            g.Builder.Store(resized, a[0]);
            return default;
        };
        BclHandler exists(bool wantIndex, bool findValue) => (g, m, _, a, at) =>
        {
            var e = m.TypeArguments[0];
            var b = g.Builder;
            var l = g.Llvm;
            var predicate = (INamedTypeSymbol)m.Parameters[1].Type;
            var result = g.NewTemp(l.I64, "found");
            b.Store(I64(g, -1), result);
            g.NullCheck(a[0]);
            var length = g.ArrayLength(a[0]);
            var done = g.NewBlock("search.done");
            g.EmitCountedLoop(length, i =>
            {
                var element = g.LoadValue(g.ArrayElementAddress(a[0], e, i), e);
                var hit = g.InvokeDelegate(a[1], predicate, element);
                var match = g.NewBlock("search.hit");
                var next = g.NewBlock("search.next");
                b.CondBr(hit, match, next);
                b.PositionAtEnd(match);
                b.Store(i, result);
                b.Br(done);
                b.PositionAtEnd(next);
            });
            b.Br(done);
            b.PositionAtEnd(done);
            var index = b.Load(l.I64, result);
            if (wantIndex) return b.Cast(LLVMOpcode.Trunc, index, l.I32);
            var found = b.ICmp(LLVMIntPredicate.SGE, index, I64(g, 0));
            if (!findValue) return found;
            // Array.Find: element or default(T)
            var safe = b.Select(found, index, I64(g, 0));
            var hasElements = b.ICmp(LLVMIntPredicate.SGT, length, I64(g, 0));
            var loadBlock = g.NewBlock("find.load");
            var endBlock = g.NewBlock("find.end");
            var value = g.NewTemp(g.Types.Value(e), "find.value");
            b.Store(LlvmContext.Null(g.Types.Value(e)), value);
            b.CondBr(b.BinOp(LLVMOpcode.And, found, hasElements), loadBlock, endBlock);
            b.PositionAtEnd(loadBlock);
            b.Store(g.LoadValue(g.ArrayElementAddress(a[0], e, safe), e), value);
            b.Br(endBlock);
            b.PositionAtEnd(endBlock);
            return b.Load(g.Types.Value(e), value);
        };
        t[$"{A}.Exists(T[], System.Predicate<T>)"] = exists(false, false);
        t[$"{A}.FindIndex(T[], System.Predicate<T>)"] = exists(true, false);
        t[$"{A}.Find(T[], System.Predicate<T>)"] = exists(false, true);
        t[$"{A}.TrueForAll(T[], System.Predicate<T>)"] = (g, m, _, a, at) =>
        {
            var e = m.TypeArguments[0];
            var b = g.Builder;
            var l = g.Llvm;
            var predicate = (INamedTypeSymbol)m.Parameters[1].Type;
            var result = g.NewTemp(l.I8, "all");
            b.Store(l.ConstInt(l.I8, 1), result);
            g.NullCheck(a[0]);
            var done = g.NewBlock("all.done");
            g.EmitCountedLoop(g.ArrayLength(a[0]), i =>
            {
                var ok = g.InvokeDelegate(a[1], predicate, g.LoadValue(g.ArrayElementAddress(a[0], e, i), e));
                var fail = g.NewBlock("all.fail");
                var next = g.NewBlock("all.next");
                b.CondBr(ok, next, fail);
                b.PositionAtEnd(fail);
                b.Store(l.ConstInt(l.I8, 0), result);
                b.Br(done);
                b.PositionAtEnd(next);
            });
            b.Br(done);
            b.PositionAtEnd(done);
            return b.Cast(LLVMOpcode.Trunc, b.Load(l.I8, result), l.I1);
        };
        t[$"{A}.ForEach(T[], System.Action<T>)"] = (g, m, _, a, _) =>
        {
            var e = m.TypeArguments[0];
            var action = (INamedTypeSymbol)m.Parameters[1].Type;
            g.NullCheck(a[0]);
            g.EmitCountedLoop(g.ArrayLength(a[0]), i => g.InvokeDelegate(a[1], action, g.LoadValue(g.ArrayElementAddress(a[0], e, i), e)));
            return default;
        };
        // Clone of a one-dimensional array whose element type is known statically: a shallow copy
        // (same element type, so GetType() and covariance checks see the original's type).
        // array.CopyTo(destination, index) is Array.Copy(array, 0, destination, index, array.Length) in .NET.
        BclHandler copyTo = (g, m, self, a, at) =>
        {
            if (at is not IInvocationOperation { Instance: { } instance } || g.TypeOf(instance) is not IArrayTypeSymbol { Rank: 1 } arrayType)
                throw CompileError.Unsupported($"'{m}' on a value not statically known to be a one-dimensional array", at);
            g.NullCheck(self);
            return ArrayCopy(g, m, at, self, I64(g, 0), a[0], ToI64(g, a[1]), g.ArrayLength(self), longLength: false, arrayType.ElementType, destinationOrdinal: 0);
        };
        t[$"{A}.CopyTo({A}, int)"] = t[$"{A}.CopyTo({A}, long)"] = copyTo;
        t[$"{A}.Clone()"] = (g, m, self, _, at) =>
        {
            if (at is not IInvocationOperation { Instance: { } instance } || g.TypeOf(instance) is not IArrayTypeSymbol { Rank: 1 } arrayType)
                throw CompileError.Unsupported($"'{m}' on a value not statically known to be a one-dimensional array", at);
            g.NullCheck(self);
            var e = arrayType.ElementType;
            var length = g.ArrayLength(self);
            var copy = g.NewArray(e, length);
            var header = g.Types.ArrayHeader(g.Types.Storage(e));
            var memmove = g.Module.Intrinsic("llvm.memcpy.p0.p0.i64", g.Llvm.Void, g.Llvm.Ptr, g.Llvm.Ptr, g.Llvm.I64, g.Llvm.I1);
            g.Builder.Call(memmove.Type, memmove.Fn, [g.Builder.StructGep(header, copy, 2), g.Builder.StructGep(header, self, 2),
                g.Builder.BinOp(LLVMOpcode.Mul, length, size(g, e)), g.Llvm.False]);
            return copy;
        };
        t[$"{A}.Empty()"] = (g, m, _, _, _) =>
        {
            var e = m.TypeArguments[0];
            var array = g.CallRuntime("csrt_new_array", g.Llvm.Ptr, [g.Llvm.I64, g.Llvm.I64], size(g, e), I64(g, 0));
            g.TagArray(array, e);
            return array;
        };
    }

    /// <summary>string.Join over an array (or an IEnumerable&lt;T&gt; that is statically an array).</summary>
    private static LLVMValueRef JoinArray(FunctionCodeGen g, IMethodSymbol m, LLVMValueRef[] a, IOperation at, bool charSeparator)
    {
        var l = g.Llvm;
        var element = (ArrayElementOf(g, at, 1) is { } syntactic ? (m.TypeArguments.Length == 1 ? m.TypeArguments[0] : syntactic) : null)
            ?? throw CompileError.Unsupported($"'{Intrinsics.Key(m.OriginalDefinition)}' over a non-array sequence", at);
        var separator = charSeparator ? Rt(g, "csrt_string_from_char", l.Ptr, (a[0], l.I16)) : a[0];
        var array = a[1];
        if (element.SpecialType != SpecialType.System_String)
        {
            // Format each element into a string[] first.
            g.NullCheck(array);
            var length = g.ArrayLength(array);
            var stringType = g.Module.Compilation.GetSpecialType(SpecialType.System_String);
            var strings = g.CallRuntime("csrt_new_array", l.Ptr, [l.I64, l.I64], I64(g, (long)g.Types.PointerSize), length);
            g.TagArray(strings, stringType);
            g.EmitCountedLoop(length, i =>
            {
                var value = g.LoadValue(g.ArrayElementAddress(array, element, i), element);
                g.Builder.Store(g.FormatValue(value, element, at), g.ArrayElementAddress(strings, stringType, i));
            });
            array = strings;
        }
        return Rt(g, "csrt_string_join", l.Ptr, (separator, l.Ptr), (array, l.Ptr));
    }

    // ---- static fields ----------------------------------------------------------------------

    private static FrozenDictionary<string, BclFieldHandler> BuildFields()
    {
        Dictionary<string, BclFieldHandler> t = [];
        t["string.Empty"] = (g, _) => g.Module.StringLiteral("");
        t["System.EventArgs.Empty"] = (g, _) => g.CallCoreLib("EventArgsImpl", "GetEmpty");
        t["System.TimeSpan.Zero"] = (g, _) => I64(g, 0);
        t["System.TimeSpan.MaxValue"] = (g, _) => I64(g, long.MaxValue);
        t["System.TimeSpan.MinValue"] = (g, _) => I64(g, long.MinValue);
        t["System.DateTime.MinValue"] = (g, _) => I64(g, 0);
        t["System.DateTime.MaxValue"] = (g, _) => I64(g, 3155378975999999999);
        t["System.DateTime.UnixEpoch"] = (g, _) => I64(g, 621355968000000000 | DateTimeKindUtc);  // a Utc DateTime
        t["System.Diagnostics.Stopwatch.Frequency"] = (g, _) => g.Module.Types.Target.IsWindows
            ? g.CallRuntime("csrt_stopwatch_frequency", g.Llvm.I64, []) : I64(g, 1_000_000_000);
        t["System.Diagnostics.Stopwatch.IsHighResolution"] = (g, _) => g.Llvm.True;
        t["System.BitConverter.IsLittleEndian"] = (g, _) => g.Llvm.True;
        return t.ToFrozenDictionary();
    }

    /// <summary>Which of the two compared strings (0: first, 1: second) is a non-null ASCII constant, if any.</summary>
    private static int? ConstantAsciiOperand(IOperation at, bool instance)
    {
        if (at is not IInvocationOperation inv) return null;
        IOperation? Operand(int i) => instance && i == 0 ? inv.Instance
            : inv.Arguments.FirstOrDefault(arg => arg.Parameter?.Ordinal == (instance ? i - 1 : i))?.Value;
        for (var i = 0; i < 2; i++)
            if (Operand(i) is { ConstantValue: { HasValue: true, Value: string text } } && text.All(char.IsAscii)) return i;
        return null;
    }

    /// <summary>The StringComparison argument (parameter <paramref name="ordinal"/>) of a call, if it is a constant.</summary>
    private static StringComparison? ConstantComparison(IOperation at, int ordinal) =>
        at is IInvocationOperation inv
        && inv.Arguments.FirstOrDefault(arg => arg.Parameter?.Ordinal == ordinal) is { Value.ConstantValue: { HasValue: true, Value: int value } }
            ? (StringComparison)value
            : null;
}
