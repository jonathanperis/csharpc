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

    internal static string TypeName(ITypeSymbol t) => t.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(s_keyFormat);

    public static bool TryGet(IMethodSymbol method, out IntrinsicHandler handler) =>
        s_methods.TryGetValue(Key(method.OriginalDefinition), out handler!);

    public static bool TryGetProperty(IPropertySymbol property, out PropertyHandler handler) =>
        s_properties.TryGetValue(PropertyKey(property.OriginalDefinition), out handler!);

    public static bool TryGetConstructor(IMethodSymbol constructor, out ConstructorHandler handler) =>
        s_constructors.TryGetValue(Key(constructor.OriginalDefinition), out handler!);

    public static IEnumerable<string> All => s_methods.Keys.Concat(s_properties.Keys).Concat(s_constructors.Keys).Order();

    private static readonly FrozenDictionary<string, ConstructorHandler> s_constructors = BuildConstructors();

    private static readonly FrozenDictionary<string, IntrinsicHandler> s_methods = BuildMethods();
    private static readonly FrozenDictionary<string, IntrinsicHandler> s_consoleFastPaths = BuildConsoleFastPaths();

    /// <summary>
    /// `Console.Out.WriteLine(x)` / `Console.Error.Write(x)` / `Console.In.ReadLine()` written literally: skip the
    /// TextWriter/TextReader objects (corelib shadows) and go straight to the console runtime. Any other receiver
    /// (a stored TextWriter, a StreamWriter...) takes the normal virtual call.
    /// </summary>
    public static bool TryGetConsoleFastPath(IInvocationOperation inv, out IntrinsicHandler handler)
    {
        handler = null!;
        return inv.Instance is IPropertyReferenceOperation { Property: { ContainingType.Name: "Console", Name: "Out" or "Error" or "In" } property }
               && property.ContainingType.ContainingNamespace?.ToDisplayString() == "System"
               && s_consoleFastPaths.TryGetValue(Key(inv.TargetMethod.OriginalDefinition), out handler!);
    }

    private static FrozenDictionary<string, IntrinsicHandler> BuildConsoleFastPaths()
    {
        Dictionary<string, IntrinsicHandler> t = [];
        string[] printable = ["string", "object", "int", "long", "uint", "ulong", "bool", "double", "float", "char"];
        foreach (var type in printable)
        {
            t[$"System.IO.TextWriter.Write({type})"] = (g, inv) => { WriteTo(g, inv, inv.Arguments[0].Value, newline: false); return default; };
            t[$"System.IO.TextWriter.WriteLine({type})"] = (g, inv) => { WriteTo(g, inv, inv.Arguments[0].Value, newline: true); return default; };
        }
        t["System.IO.TextWriter.WriteLine()"] = (g, inv) => { WriteTo(g, inv, null, newline: true); return default; };
        t["System.IO.TextWriter.Flush()"] = (g, inv) => { ConsoleStream(inv); g.CallRuntime("csrt_console_flush", g.Module.Llvm.Void, []); return default; };
        t["System.IO.TextReader.ReadLine()"] = (g, inv) => { ConsoleStream(inv); return g.CallRuntime("csrt_console_read_line", g.Module.Llvm.Ptr, []); };
        t["System.IO.TextReader.ReadToEnd()"] = (g, inv) => { ConsoleStream(inv); return g.CallRuntime("csrt_console_read_to_end", g.Module.Llvm.Ptr, []); };
        return t.ToFrozenDictionary();
    }
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
            // Console.Out is synchronized in .NET: one Write/WriteLine never interleaves with another thread's.
            // The argument is evaluated first, then the write (+ newline) happens under the stdout lock.
            t[$"System.Console.Write({type})"] = (g, inv) => { WriteValue(g, inv.Arguments[0].Value, newline: false); return default; };
            t[$"System.Console.WriteLine({type})"] = (g, inv) => { WriteValue(g, inv.Arguments[0].Value, newline: true); return default; };
        }
        t["System.Console.WriteLine()"] = (g, _) => { NewLine(g); return default; };
        t["System.Console.ReadLine()"] = (g, _) => g.CallRuntime("csrt_console_read_line", g.Module.Llvm.Ptr, []);
        t["System.Console.Read()"] = (g, _) => g.CallRuntime("csrt_console_read", g.Module.Llvm.I32, []);

        // ---- System.Object / ToString ----------------------------------------------------------
        t["object..ctor()"] = (_, _) => default;
        // T[].Clone() (the static type gives the element type: all arrays share one method table).
        t["System.Array.Clone()"] = (g, inv) => inv.Instance!.Type is IArrayTypeSymbol { Rank: 1 } array
            ? g.CallCoreLibGeneric("ArrayChecks", "CloneArray", array.ElementType, g.EmitValue(inv.Instance!))
            : throw CompileError.Unsupported("Array.Clone() on a receiver that is not statically a one-dimensional array", inv);
        // A shallow copy of a class instance: same method table, fields copied (the GC needs no barriers).
        t["object.MemberwiseClone()"] = (g, inv) =>
            g.CallRuntime("csrt_object_memberwise_clone", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], g.EmitValue(inv.Instance!));
        t["object.ToString()"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        t["System.Array.GetLength(int)"] = (g, inv) =>
            g.Builder.Cast(LLVMOpcode.Trunc, g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0)), g.Module.Llvm.I32);
        t["System.Array.GetLongLength(int)"] = (g, inv) => g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0));
        t["System.Array.GetUpperBound(int)"] = (g, inv) => g.Builder.BinOp(LLVMOpcode.Sub,
            g.Builder.Cast(LLVMOpcode.Trunc, g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0)), g.Module.Llvm.I32),
            g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 1));
        // Always 0 (csharpc arrays are zero-based), after the same null and dimension checks as GetLength.
        t["System.Array.GetLowerBound(int)"] = (g, inv) =>
        {
            g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0));
            return g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 0);
        };
        t["object.ReferenceEquals(object, object)"] = (g, inv) =>
            g.Builder.ICmp(LLVMIntPredicate.EQ, Arg(g, inv, 0), Arg(g, inv, 1));
        // Enum.HasFlag(flag): (value & flag) == flag on the underlying integers (both arrive boxed to Enum).
        t["System.Enum.HasFlag(System.Enum)"] = (g, inv) =>
        {
            var b = g.Builder;
            var value = g.EmitValue(UnboxEnum(inv.Instance!));
            var flag = g.EmitValue(UnboxEnum(inv.Arguments[0].Value));
            return b.ICmp(LLVMIntPredicate.EQ, b.BinOp(LLVMOpcode.And, value, flag), flag);
        };
        t["System.Enum.GetName(System.Type, object)"] = (g, inv) =>
        {
            if (inv.Arguments.First(a => a.Parameter!.Ordinal == 0).Value is not ITypeOfOperation { TypeOperand: INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType })
                throw CompileError.Unsupported("Enum.GetName with a non-constant Type", inv);
            var raw = UnboxEnum(inv.Arguments.First(a => a.Parameter!.Ordinal == 1).Value);
            var value = g.Coerce(g.EmitValue(raw), raw.Type, enumType.EnumUnderlyingType!);
            var lookup = g.Module.EnumNameOrNull(enumType);
            return g.Builder.Call(lookup.Type, lookup.Fn, [value]);
        };
        t["System.Enum.GetName<TEnum>(TEnum)"] = (g, inv) =>
        {
            var arg = inv.Arguments[0].Value;
            var enumType = (INamedTypeSymbol)(g.TypeOf(arg) ?? arg.Type!);
            var lookup = g.Module.EnumNameOrNull(enumType);
            return g.Builder.Call(lookup.Type, lookup.Fn, [g.EmitValue(arg)]);
        };
        t["System.Enum.ToString()"] = (g, inv) =>
            g.ToStringValue(inv.Instance is IConversionOperation { Operand.Type.TypeKind: TypeKind.Enum } boxed ? boxed.Operand : inv.Instance!);
        t["System.Enum.ToString(string)"] = (g, inv) =>
        {
            var instance = UnboxEnum(inv.Instance!);
            if (g.TypeOf(instance) is { TypeKind: TypeKind.Enum }) return g.FormatWith(instance, Arg(g, inv, 0));
            // A boxed enum of unknown type: its method table implements IFormattable (EnumMethodTable).
            var boxed = g.EmitValue(inv.Instance!);
            g.NullCheck(boxed);
            return g.CallCoreLib("Number", "FormatObject", boxed, Arg(g, inv, 0));
        };
        // Enum.CompareTo(object): both sides statically the same enum compare by underlying value.
        t["System.Enum.CompareTo(object)"] = (g, inv) =>
        {
            var self = UnboxEnum(inv.Instance!);
            var other = UnboxEnum(inv.Arguments[0].Value);
            if (g.TypeOf(self) is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                || !SymbolEqualityComparer.Default.Equals(g.TypeOf(other), enumType))
                throw CompileError.Unsupported("Enum.CompareTo with operands of different or unknown enum types", inv);
            var signed = TypeMapper.IsSigned(enumType.EnumUnderlyingType!);
            var a = g.EmitValue(self);
            var b = g.EmitValue(other);
            var l = g.Module.Llvm;
            var less = g.Builder.ICmp(signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT, a, b);
            var greater = g.Builder.ICmp(signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, a, b);
            return g.Builder.Select(less, l.ConstInt(l.I32, -1), g.Builder.Select(greater, l.ConstInt(l.I32, 1), l.ConstInt(l.I32, 0)));
        };
        t["System.Enum.GetHashCode()"] = (g, inv) =>
        {
            var instance = UnboxEnum(inv.Instance!);
            if (g.TypeOf(instance) is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
                return g.Module.EnumHash(g.Builder, g.EmitValue(instance), enumType);
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        t["System.Enum.Equals(object)"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self, Arg(g, inv, 0)]);
        };
        // ValueType overrides of object's virtuals: dispatch on the (boxed) receiver like object's.
        t["System.ValueType.ToString()"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        t["System.ValueType.Equals(object)"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self, Arg(g, inv, 0)]);
        };
        t["System.ValueType.GetHashCode()"] = (g, inv) =>
        {
            var self = ObjectReceiver(g, inv.Instance!);
            return g.VirtualCall(inv.TargetMethod, self, [self]);
        };
        t["System.Array.GetLength(int)"] = (g, inv) =>
            g.Builder.Cast(LLVMOpcode.Trunc, g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0)), g.Module.Llvm.I32);
        t["System.Array.GetLongLength(int)"] = (g, inv) => g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0));
        t["System.Array.GetUpperBound(int)"] = (g, inv) => g.Builder.BinOp(LLVMOpcode.Sub,
            g.Builder.Cast(LLVMOpcode.Trunc, g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0)), g.Module.Llvm.I32),
            g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 1));
        // Always 0 (csharpc arrays are zero-based), after the same null and dimension checks as GetLength.
        t["System.Array.GetLowerBound(int)"] = (g, inv) =>
        {
            g.ArrayDimensionLength(g.EmitValue(inv.Instance!), inv.Instance!.Type!, Arg(g, inv, 0));
            return g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 0);
        };
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
        {
            t[$"{type}.ToString(string)"] = (g, inv) => g.FormatWith(inv.Instance!, Arg(g, inv, 0));
            // The provider (CultureInfo, NumberFormatInfo, or null for the current culture) goes to the corelib.
            t[$"{type}.ToString(string, System.IFormatProvider)"] = (g, inv) => g.FormatWithProvider(inv.Instance!, Arg(g, inv, 0), Arg(g, inv, 1));
            t[$"{type}.ToString(System.IFormatProvider)"] = (g, inv) => g.FormatWithProvider(inv.Instance!, LlvmContext.Null(g.Module.Llvm.Ptr), Arg(g, inv, 0));
        }

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
        // string.Format(provider, format, ...): the corelib's composite formatter with the provider's culture.
        IntrinsicHandler providerComposite = (g, inv) =>
        {
            var ordered = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToList();
            var provider = g.EmitValue(ordered[0].Value);
            var format = g.EmitValue(ordered[1].Value);
            var args = ordered.Count == 3 && ordered[2].Value.Type is IArrayTypeSymbol && ordered[2].ArgumentKind != ArgumentKind.ParamArray
                ? g.EmitValue(ordered[2].Value)
                : g.ObjectArray([.. ordered.Skip(2).Select(a => a.Value)]);
            return g.CallCoreLib("Number", "FormatCompositeWith", provider, format, args);
        };
        foreach (var arity in new[] { "object", "object, object", "object, object, object", "object[]" })
        {
            t[$"string.Format(string, {arity})"] = composite(null, false);
            t[$"string.Format(System.IFormatProvider, string, {arity})"] = providerComposite;
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
        // string == / != called as methods (compiled code, the IL front-end).
        t["string.op_Equality(string, string)"] = (g, inv) =>
            g.CallRuntime("csrt_string_equals", g.Module.Llvm.I1, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1));
        t["string.op_Inequality(string, string)"] = (g, inv) => g.Builder.Not(
            g.CallRuntime("csrt_string_equals", g.Module.Llvm.I1, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1)));
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

        // ---- parsing (src/runtime/strings.c) ---------------------------------------------------------
        // System.Char classification/casing: src/Csharpc/corelib/StringOps.cs (CharOps).
        // Numeric Parse/TryParse: corelib NumberParsingMembers.cs (culture-aware, Number.Parsing.Culture.cs).

        // System.Text.StringBuilder: src/Csharpc/corelib/StringBuilder.cs (StringBuilderImpl).

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
        foreach (var (type, signed) in new[] { ("int", true), ("long", true), ("uint", false), ("ulong", false),
                     ("short", true), ("sbyte", true), ("ushort", false), ("byte", false), ("nint", true), ("nuint", false) })
        {
            t[$"System.Math.Max({type}, {type})"] = (g, inv) => SelectCompare(g, Arg(g, inv, 0), Arg(g, inv, 1), signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT);
            t[$"System.Math.Min({type}, {type})"] = (g, inv) => SelectCompare(g, Arg(g, inv, 0), Arg(g, inv, 1), signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT);
        }
        t["System.Math.Round(double, int)"] = (g, inv) =>
            g.CallRuntime("csrt_math_round_digits", g.Module.Llvm.F64, [g.Module.Llvm.F64, g.Module.Llvm.I32], Arg(g, inv, 0), Arg(g, inv, 1));
        foreach (var (name, to) in new[] { ("DoubleToInt64Bits(double)", "i64"), ("Int64BitsToDouble(long)", "f64"), ("DoubleToUInt64Bits(double)", "i64"),
                     ("UInt64BitsToDouble(ulong)", "f64"), ("SingleToInt32Bits(float)", "i32"), ("Int32BitsToSingle(int)", "f32"),
                     ("SingleToUInt32Bits(float)", "i32"), ("UInt32BitsToSingle(uint)", "f32") })
            t[$"System.BitConverter.{name}"] = (g, inv) =>
            {
                var l = g.Module.Llvm;
                return g.Builder.Cast(LLVMOpcode.BitCast, Arg(g, inv, 0), to switch { "i64" => l.I64, "f64" => l.F64, "i32" => l.I32, _ => l.F32 });
            };
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

        // Math.Clamp for the other numeric types: value < min ? min : value > max ? max : value.
        foreach (var (type, signed) in new[] { ("long", true), ("short", true), ("sbyte", true), ("uint", false), ("ulong", false), ("byte", false), ("ushort", false) })
            t[$"System.Math.Clamp({type}, {type}, {type})"] = (g, inv) =>
            {
                var (v, lo, hi) = (Arg(g, inv, 0), Arg(g, inv, 1), Arg(g, inv, 2));
                return SelectCompare(g, SelectCompare(g, v, lo, signed ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT), hi, signed ? LLVMIntPredicate.SLT : LLVMIntPredicate.ULT);
            };
        foreach (var type in new[] { "double", "float" })
            t[$"System.Math.Clamp({type}, {type}, {type})"] = (g, inv) =>
            {
                var b = g.Builder;
                var (v, lo, hi) = (Arg(g, inv, 0), Arg(g, inv, 1), Arg(g, inv, 2));
                var low = b.Select(b.FCmp(LLVMRealPredicate.OLT, v, lo), lo, v);   // NaN stays NaN
                return b.Select(b.FCmp(LLVMRealPredicate.OGT, low, hi), hi, low);
            };
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

        // ---- System.GC (src/runtime/gc.c) -----------------------------------------------------------
        t["System.GC.Collect()"] = (g, _) => { g.CallRuntime("csrt_gc_collect", g.Module.Llvm.Void, []); return default; };
        // KeepAlive: the conservative GC scans the stack, so a volatile store of the reference into a
        // frame slot keeps the object reachable at least until this point, which is all .NET promises.
        t["System.GC.KeepAlive(object)"] = (g, inv) =>
        {
            var value = Arg(g, inv, 0);
            var slot = g.NewTemp(g.Module.Llvm.Ptr, "keepalive");
            Native.LLVMSetVolatile(g.Builder.Store(value, slot), 1);
            return default;
        };
        // csharpc runs no finalizers (.NET does not promise to run them either, and never at exit), so
        // there is nothing to suppress or re-register.
        t["System.GC.SuppressFinalize(object)"] = (g, inv) => { Arg(g, inv, 0); return default; };
        t["System.GC.ReRegisterForFinalize(object)"] = (g, inv) => { Arg(g, inv, 0); return default; };
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

        Add("string.GetHashCode()", "csrt_string_get_hash_code", i32, _ => [], (_, _) => []);
        // Culture-sensitive (ICU root collation approximation, src/runtime/bclmisc.c), like .NET's CompareTo.
        Add("string.CompareTo(string)", "csrt_string_compare_culture", i32, l => [l.Ptr, l.I1], (g, i) => [Arg(g, i, 0), g.Module.Llvm.False]);

    }

    private static LLVMValueRef CtorArg(FunctionCodeGen g, IObjectCreationOperation c, int ordinal)
    {
        var argument = c.Arguments.First(a => a.Parameter!.Ordinal == ordinal);
        return g.Coerce(g.EmitValue(argument.Value), g.TypeOf(argument.Value), g.S(argument.Parameter!.Type));
    }

    // ---- System.Threading.Tasks (src/runtime/async.c; docs/10-async.md) ---------------------------
    private const string Tasks = "System.Threading.Tasks";
    private const string Rcs = "System.Runtime.CompilerServices";

    /// <summary>The awaiters that are a csrt task pointer (a property: the tables above are built first).</summary>
    private static string[] TaskAwaiterTypes =>
    [
        $"{Rcs}.TaskAwaiter", $"{Rcs}.TaskAwaiter<TResult>", $"{Rcs}.ValueTaskAwaiter", $"{Rcs}.ValueTaskAwaiter<TResult>",
        $"{Rcs}.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter", $"{Rcs}.ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter",
        $"{Rcs}.ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter", $"{Rcs}.ConfiguredValueTaskAwaitable<TResult>.ConfiguredValueTaskAwaiter",
    ];

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

        // Faulted tasks.
        IntrinsicHandler fromException = (g, inv) =>
        {
            var result = ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments.FirstOrDefault();
            var size = result is null ? 0 : (long)g.Module.Types.Target.SizeOf(g.Module.Types.Storage(result));
            return g.CallRuntime("csrt_task_from_exception", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], Arg(g, inv, 0),
                g.Module.Llvm.ConstInt(g.Module.Llvm.I32, size));
        };
        t[$"{Tasks}.Task.FromException(System.Exception)"] = fromException;
        t[$"{Tasks}.Task.FromException<TResult>(System.Exception)"] = fromException;
        t[$"{Tasks}.ValueTask.FromException(System.Exception)"] = fromException;
        t[$"{Tasks}.ValueTask.FromException<TResult>(System.Exception)"] = fromException;

        // TaskCompletionSource(<T>): the source object is its task.
        foreach (var tcs in new[] { $"{Tasks}.TaskCompletionSource", $"{Tasks}.TaskCompletionSource<TResult>" })
        {
            var generic = tcs.EndsWith('>');
            foreach (var tryMode in new[] { false, true })
            {
                var prefix = tryMode ? "TrySet" : "Set";
                t[generic ? $"{tcs}.{prefix}Result(TResult)" : $"{tcs}.{prefix}Result()"] = (g, inv) =>
                {
                    var self = g.EmitValue(inv.Instance!);
                    if (!generic) return g.SetTaskResult(self, null, null, tryMode);
                    var type = ((INamedTypeSymbol)g.TypeOf(inv.Instance!)!).TypeArguments[0];
                    return g.SetTaskResult(self, Arg(g, inv, 0), type, tryMode);
                };
                t[$"{tcs}.{prefix}Exception(System.Exception)"] = (g, inv) =>
                {
                    var self = g.EmitValue(inv.Instance!);
                    g.NullCheck(self);
                    var e = Arg(g, inv, 0);
                    return tryMode
                        ? g.CallRuntime("csrt_task_try_fault", g.Module.Llvm.I1, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], self, e)
                        : g.CallRuntime("csrt_task_fault", g.Module.Llvm.Void, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], self, e);
                };
                t[$"{tcs}.{prefix}Canceled()"] = (g, inv) =>
                {
                    var self = g.EmitValue(inv.Instance!);
                    g.NullCheck(self);
                    return tryMode
                        ? g.CallRuntime("csrt_task_try_cancel", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], self)
                        : g.CallRuntime("csrt_task_cancel", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], self);
                };
            }
        }

        // Cancellation: a CancellationToken is its source pointer (CancellationToken.None == null).
        const string Th = "System.Threading";
        // Callbacks registered with CancellationToken.Register are corelib C# (corelib/Cancellation.cs).
        t[$"{Th}.CancellationTokenSource.Cancel()"] = (g, inv) =>
        {
            g.CallRuntime("csrt_cts_cancel_checked", g.Module.Llvm.Void, [g.Module.Llvm.Ptr, g.Module.Llvm.I1], g.EmitValue(inv.Instance!), g.Module.Llvm.False);
            return default;
        };
        t[$"{Th}.CancellationTokenSource.Cancel(bool)"] = (g, inv) =>
        {
            g.CallRuntime("csrt_cts_cancel_checked", g.Module.Llvm.Void, [g.Module.Llvm.Ptr, g.Module.Llvm.I1], g.EmitValue(inv.Instance!), Arg(g, inv, 0));
            return default;
        };
        t[$"{Th}.CancellationTokenSource.TryReset()"] = (g, inv) =>
            g.CallRuntime("csrt_cts_try_reset", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], g.EmitValue(inv.Instance!));
        t[$"{Th}.CancellationTokenSource.CancelAfter(int)"] = (g, inv) =>
        {
            g.CallRuntime("csrt_cts_cancel_after", g.Module.Llvm.Void, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], g.EmitValue(inv.Instance!), Arg(g, inv, 0));
            return default;
        };
        t[$"{Th}.CancellationTokenSource.CreateLinkedTokenSource({Th}.CancellationToken, {Th}.CancellationToken)"] = (g, inv) =>
            g.CallRuntime("csrt_cts_new_linked", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1));
        // Tokens are equal when they come from the same source (None and default are both null).
        IntrinsicHandler sameSource = (g, inv) =>
        {
            var a = inv.Instance is { } instance ? g.EmitValue(instance) : Arg(g, inv, 0);
            var b = Arg(g, inv, inv.Instance is null ? 1 : 0);
            return g.Builder.ICmp(LLVMIntPredicate.EQ, a, b);
        };
        t[$"{Th}.CancellationToken.Equals({Th}.CancellationToken)"] = sameSource;
        t[$"{Th}.CancellationToken.op_Equality({Th}.CancellationToken, {Th}.CancellationToken)"] = sameSource;
        t[$"{Th}.CancellationToken.op_Inequality({Th}.CancellationToken, {Th}.CancellationToken)"] = (g, inv) =>
            g.Builder.Not(sameSource(g, inv));
        t[$"{Th}.CancellationTokenSource.Dispose()"] = (g, inv) =>
        { g.CallRuntime("csrt_cts_dispose", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], g.EmitValue(inv.Instance!)); return default; };
        t[$"{Th}.CancellationToken.ThrowIfCancellationRequested()"] = (g, inv) =>
        {
            g.CallRuntime("csrt_token_throw_if_cancellation_requested", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], g.EmitValue(inv.Instance!));
            return default;
        };
        t[$"{Tasks}.Task.Delay(int, {Th}.CancellationToken)"] = (g, inv) =>
            g.CallRuntime("csrt_task_delay_token", g.Module.Llvm.Ptr, [g.Module.Llvm.I32, g.Module.Llvm.Ptr], Arg(g, inv, 0), Arg(g, inv, 1));

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

        // Task.Run queues the work on the thread pool (runtime/async.c, docs/10-async.md).
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
        // Func<T> with a non-task T: a compiler-generated per-T thunk runs it on the pool.
        t[$"{Tasks}.Task.Run(System.Func<TResult>)"] = (g, inv) =>
        {
            var l = g.Module.Llvm;
            var result = ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments[0];
            var size = (long)g.Module.Types.Target.SizeOf(g.Module.Types.Storage(result));
            var thunk = g.Module.TaskRunThunk(result);
            return g.CallRuntime("csrt_task_run_thunk", l.Ptr, [l.Ptr, l.Ptr, l.I32], thunk.Fn, Arg(g, inv, 0), l.ConstInt(l.I32, size));
        };

        foreach (var type in new[] { "Task", "Task<TResult>", "ValueTask", "ValueTask<TResult>" })
        {
            t[$"{Tasks}.{type}.GetAwaiter()"] = self;
            t[$"{Tasks}.{type}.ConfigureAwait(bool)"] = self;
            t[$"{Tasks}.{type}.Wait()"] = (g, inv) => { ThrowAggregate(g, Wait(g, g.EmitValue(inv.Instance!))); return default; };
        }
        t[$"{Tasks}.ValueTask.AsTask()"] = (g, inv) =>
            g.CallRuntime("csrt_valuetask_as_task", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], g.EmitValue(inv.Instance!), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, 0));
        t[$"{Tasks}.ValueTask<TResult>.AsTask()"] = (g, inv) =>
        {
            var result = ((INamedTypeSymbol)g.TypeOf(inv)!).TypeArguments[0];
            var size = (long)g.Module.Types.Target.SizeOf(g.Module.Types.Storage(result));
            return g.CallRuntime("csrt_valuetask_as_task", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32], g.EmitValue(inv.Instance!), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, size));
        };
        foreach (var awaiter in TaskAwaiterTypes)
            t[$"{awaiter}.GetResult()"] = (g, inv) =>
            {
                var task = Wait(g, g.EmitValue(inv.Instance!));
                g.CallRuntime("csrt_task_throw_if_faulted", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], task); // the original exception
                return g.TaskResult(task, ResultOf(g, inv));
            };
        foreach (var awaitable in new[] { $"{Rcs}.ConfiguredTaskAwaitable", $"{Rcs}.ConfiguredTaskAwaitable<TResult>",
                     $"{Rcs}.ConfiguredValueTaskAwaitable", $"{Rcs}.ConfiguredValueTaskAwaitable<TResult>" })
            t[$"{awaitable}.GetAwaiter()"] = self;

        // The awaiter pattern as compiled async state machines (IL) and custom awaitables call it:
        // OnCompleted/UnsafeOnCompleted register an Action continuation on the task (docs/10-async.md).
        IntrinsicHandler onCompleted = (g, inv) =>
        {
            var task = g.EmitValue(inv.Instance!);
            g.CallRuntime("csrt_task_on_completed_action", g.Module.Llvm.Void, [g.Module.Llvm.Ptr, g.Module.Llvm.Ptr], task, Arg(g, inv, 0));
            return default;
        };
        foreach (var awaiter in TaskAwaiterTypes)
        {
            t[$"{awaiter}.OnCompleted(System.Action)"] = onCompleted;
            t[$"{awaiter}.UnsafeOnCompleted(System.Action)"] = onCompleted;
        }
        // Task.Yield(): YieldAwaitable/YieldAwaiter are a null task pointer; the continuation always
        // goes to the pool (no SynchronizationContext or TaskScheduler to post to).
        const string yieldAwaiter = $"{Rcs}.YieldAwaitable.YieldAwaiter";
        t[$"{Rcs}.YieldAwaitable.GetAwaiter()"] = self;
        t[$"{yieldAwaiter}.GetResult()"] = (g, inv) => { g.EmitValue(inv.Instance!); return default; };
        IntrinsicHandler queue = (g, inv) =>
        {
            g.EmitValue(inv.Instance!);
            g.CallRuntime("csrt_queue_action", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], Arg(g, inv, 0));
            return default;
        };
        t[$"{yieldAwaiter}.OnCompleted(System.Action)"] = queue;
        t[$"{yieldAwaiter}.UnsafeOnCompleted(System.Action)"] = queue;
    }

    private static void ThrowAggregate(FunctionCodeGen g, LLVMValueRef task) =>
        g.CallRuntime("csrt_task_throw_aggregate_if_faulted", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], task);

    private static void AddTaskProperties(Dictionary<string, PropertyHandler> t)
    {
        foreach (var type in new[] { $"{Tasks}.Task", $"{Tasks}.ValueTask", $"{Tasks}.ValueTask<TResult>" })
        {
            t[$"{type}.IsFaulted"] = (g, p) => g.CallRuntime("csrt_task_is_faulted", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
            t[$"{type}.IsCanceled"] = (g, p) => g.CallRuntime("csrt_task_is_canceled", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
        }
        t[$"{Tasks}.Task.Exception"] = (g, p) => g.CallRuntime("csrt_task_exception", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
        foreach (var tcs in new[] { $"{Tasks}.TaskCompletionSource", $"{Tasks}.TaskCompletionSource<TResult>" })
            t[$"{tcs}.Task"] = (g, p) => g.EmitValue(p.Instance!);
        const string Th = "System.Threading";
        t[$"{Th}.CancellationTokenSource.Token"] = (g, p) =>
            g.CallRuntime("csrt_cts_token", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!)); // ObjectDisposedException once disposed
        t[$"{Th}.CancellationTokenSource.IsCancellationRequested"] = (g, p) =>
            g.CallRuntime("csrt_token_is_cancellation_requested", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
        t[$"{Th}.CancellationToken.IsCancellationRequested"] = t[$"{Th}.CancellationTokenSource.IsCancellationRequested"];
        t[$"{Th}.CancellationToken.CanBeCanceled"] = (g, p) =>
            g.Builder.ICmp(LLVMIntPredicate.NE, g.EmitValue(p.Instance!), LlvmContext.Null(g.Module.Llvm.Ptr));
        t[$"{Th}.CancellationToken.None"] = (g, _) => LlvmContext.Null(g.Module.Llvm.Ptr);
        PropertyHandler isCompleted = (g, p) => g.CallRuntime("csrt_task_is_completed", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
        foreach (var type in TaskAwaiterTypes.Concat([$"{Tasks}.Task", $"{Tasks}.ValueTask", $"{Tasks}.ValueTask<TResult>"]))
            t[$"{type}.IsCompleted"] = isCompleted;
        t[$"{Rcs}.YieldAwaitable.YieldAwaiter.IsCompleted"] = (g, p) => { g.EmitValue(p.Instance!); return g.Module.Llvm.False; };
        t[$"{Tasks}.Task.IsCompletedSuccessfully"] = (g, p) =>
            g.CallRuntime("csrt_task_is_completed_successfully", g.Module.Llvm.I1, [g.Module.Llvm.Ptr], g.EmitValue(p.Instance!));
        t[$"{Tasks}.ValueTask.IsCompletedSuccessfully"] = t[$"{Tasks}.Task.IsCompletedSuccessfully"];
        t[$"{Tasks}.ValueTask<TResult>.IsCompletedSuccessfully"] = t[$"{Tasks}.Task.IsCompletedSuccessfully"];
        PropertyHandler result = (g, p) =>
        {
            var task = g.EmitValue(p.Instance!);
            g.CallRuntime("csrt_task_wait", g.Module.Llvm.Void, [g.Module.Llvm.Ptr], task);
            ThrowAggregate(g, task); // .Result wraps failures in an AggregateException
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
        ConstructorHandler newTcs = (g, c) =>
        {
            foreach (var a in c.Arguments) g.EmitValue(a.Value); // TaskCreationOptions / state: ignored (continuations are always queued)
            var result = ((INamedTypeSymbol)g.TypeOf(c)!).TypeArguments.FirstOrDefault();
            var size = result is null ? 0 : (long)g.Module.Types.Target.SizeOf(g.Module.Types.Storage(result));
            return g.CallRuntime("csrt_task_new", g.Module.Llvm.Ptr, [g.Module.Llvm.I32], g.Module.Llvm.ConstInt(g.Module.Llvm.I32, size));
        };
        foreach (var tcs in new[] { $"{Tasks}.TaskCompletionSource", $"{Tasks}.TaskCompletionSource<TResult>" })
        {
            t[$"{tcs}..ctor()"] = newTcs;
            t[$"{tcs}..ctor({Tasks}.TaskCreationOptions)"] = newTcs;
        }
        t["System.Threading.CancellationTokenSource..ctor()"] = (g, _) => g.CallRuntime("csrt_cts_new", g.Module.Llvm.Ptr, []);
        t["System.Threading.CancellationToken..ctor(bool)"] = (g, c) =>
            g.CallRuntime("csrt_token_new", g.Module.Llvm.Ptr, [g.Module.Llvm.I1], CtorArg(g, c, 0));
        t["System.Threading.CancellationTokenSource..ctor(int)"] = (g, c) =>
            g.CallRuntime("csrt_cts_new_after", g.Module.Llvm.Ptr, [g.Module.Llvm.I32], CtorArg(g, c, 0));
        t[$"{Tasks}.ValueTask<TResult>..ctor({Tasks}.Task<TResult>)"] = (g, c) => CtorArg(g, c, 0);
        // ValueTasks over an IValueTaskSource (async iterators' MoveNextAsync): bridged to a csrt task (corelib/AsyncIterators.cs).
        t[$"{Tasks}.ValueTask<TResult>..ctor({Tasks}.Sources.IValueTaskSource<TResult>, short)"] = (g, c) =>
            g.CallCoreLibGeneric("ValueTaskSourceBridge`1", "Create", [((INamedTypeSymbol)g.TypeOf(c)!).TypeArguments[0]], CtorArg(g, c, 0), CtorArg(g, c, 1));
        t[$"{Tasks}.ValueTask..ctor({Tasks}.Sources.IValueTaskSource, short)"] = (g, c) =>
            g.CallCoreLibGeneric("ValueTaskSourceBridge", "Create", [], CtorArg(g, c, 0), CtorArg(g, c, 1));
        t["string..ctor(char, int)"] = (g, c) =>
            g.CallRuntime("csrt_string_new_repeat", g.Module.Llvm.Ptr, [g.Module.Llvm.I16, g.Module.Llvm.I32], CtorArg(g, c, 0), CtorArg(g, c, 1));
        // Strings from native memory: new string(char*, int, int) / (char*) / (sbyte*).
        t["string..ctor(char*, int, int)"] = (g, c) =>
        {
            var l = g.Module.Llvm;
            var start = g.Builder.Cast(LLVMOpcode.SExt, CtorArg(g, c, 1), l.I64);
            var first = g.Builder.InBoundsGep(l.I16, CtorArg(g, c, 0), [start]);
            return g.CallRuntime("csrt_string_new_from_utf16", l.Ptr, [l.Ptr, l.I32], first, CtorArg(g, c, 2));
        };
        t["string..ctor(char*)"] = (g, c) =>
            g.CallRuntime("csrt_pinvoke_string_from_native", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32, g.Module.Llvm.I1],
                CtorArg(g, c, 0), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, ModuleCodeGen.StringUtf16), g.Module.Llvm.False);
        // .NET Core decodes new string(sbyte*) as UTF-8 on every platform (not the ANSI code page).
        t["string..ctor(sbyte*)"] = (g, c) =>
            g.CallRuntime("csrt_pinvoke_string_from_native", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32, g.Module.Llvm.I1],
                CtorArg(g, c, 0), g.Module.Llvm.ConstInt(g.Module.Llvm.I32, ModuleCodeGen.StringUtf8), g.Module.Llvm.False);
        t["string..ctor(char[], int, int)"] = (g, c) =>
            g.CallRuntime("csrt_string_new_from_char_range", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr, g.Module.Llvm.I32, g.Module.Llvm.I32],
                CtorArg(g, c, 0), CtorArg(g, c, 1), CtorArg(g, c, 2));
        t["string..ctor(char[])"] = (g, c) =>
            g.CallRuntime("csrt_string_new_from_chars", g.Module.Llvm.Ptr, [g.Module.Llvm.Ptr], CtorArg(g, c, 0));
        return t.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, PropertyHandler> BuildProperties()
    {
        Dictionary<string, PropertyHandler> t = [];
        AddTaskProperties(t);
        t["string.Length"] = (g, p) =>
        {
            var s = g.EmitValue(p.Instance!);
            g.NullCheck(s);
            var l = g.Module.Llvm;
            return g.LoadStringLength(s);
        };
        t["string.this[int]"] = (g, p) =>
        {
            var s = g.EmitValue(p.Instance!);
            return g.StringCharAt(s, g.EmitValue(p.Arguments[0].Value));
        };
        t["System.Array.Length"] = (g, p) =>
            g.Builder.Cast(LLVMOpcode.Trunc, g.ArrayLength(g.EmitValue(p.Instance!)), g.Module.Llvm.I32);
        t["System.Array.Rank"] = (g, p) =>
        {
            var array = g.EmitValue(p.Instance!);
            if (g.TypeOf(p.Instance!) is IArrayTypeSymbol a) { g.NullCheck(array); return g.Module.Llvm.ConstInt(g.Module.Llvm.I32, a.Rank); }
            // An `Array`: only the object's method table knows (rectangular tables carry their rank).
            g.NullCheck(array);
            return g.CallRuntime("csrt_array_rank", g.Module.Llvm.I32, [g.Module.Llvm.Ptr], array);
        };
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
        // Int128 / UInt128 constants (i128; built with shifts since LLVMConstInt takes 64 bits).
        foreach (var type in new[] { "System.Int128", "System.UInt128" })
        {
            var signed = type == "System.Int128";
            LLVMValueRef AllOnes(FunctionCodeGen g) => g.Module.Llvm.ConstInt(g.Module.Llvm.IntTypeOf(128), -1);
            LLVMValueRef TopBit(FunctionCodeGen g) => g.Builder.BinOp(LLVMOpcode.Shl, g.Module.Llvm.ConstInt(g.Module.Llvm.IntTypeOf(128), 1),
                g.Module.Llvm.ConstInt(g.Module.Llvm.IntTypeOf(128), 127));
            t[$"{type}.Zero"] = (g, _) => g.Module.Llvm.ConstInt(g.Module.Llvm.IntTypeOf(128), 0);
            t[$"{type}.One"] = (g, _) => g.Module.Llvm.ConstInt(g.Module.Llvm.IntTypeOf(128), 1);
            t[$"{type}.MinValue"] = (g, _) => signed ? TopBit(g) : g.Module.Llvm.ConstInt(g.Module.Llvm.IntTypeOf(128), 0);
            t[$"{type}.MaxValue"] = (g, _) => signed ? g.Builder.BinOp(LLVMOpcode.LShr, AllOnes(g), g.Module.Llvm.ConstInt(g.Module.Llvm.IntTypeOf(128), 1)) : AllOnes(g);
        }
        t["System.Environment.NewLine"] = (g, _) => g.Module.StringLiteral(g.Module.NewLine);
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
        if (newline) text = g.ConcatStrings(text, g.Module.StringLiteral(g.Module.NewLine));
        g.CallRuntime("csrt_console_error_write_string", l.Void, [l.Ptr], text);
    }

    private static LLVMValueRef Separator(FunctionCodeGen g, IInvocationOperation inv)
    {
        var l = g.Module.Llvm;
        var sep = Arg(g, inv, 0);
        return sep.Type.IsPointer ? sep : g.CallRuntime("csrt_string_from_char", l.Ptr, [l.I16], sep);
    }

    private static LLVMValueRef AppendJoined(FunctionCodeGen g, IInvocationOperation inv, LLVMValueRef joined)
    {
        var l = g.Module.Llvm;
        return g.CallRuntime("csrt_sb_append_string", l.Ptr, [l.Ptr, l.Ptr], g.EmitValue(inv.Instance!), joined);
    }

    private static void ConsoleLock(FunctionCodeGen g, bool acquire) =>
        g.CallRuntime(acquire ? "csrt_console_lock" : "csrt_console_unlock", g.Module.Llvm.Void, []);

    /// <summary>Strips the boxing conversion of an enum/integer value passed as Enum/object.</summary>
    private static IOperation UnboxEnum(IOperation op)
    {
        while (op is IConversionOperation { Operand.Type: { IsValueType: true } } c) op = c.Operand;
        return op;
    }

    private static void NewLine(FunctionCodeGen g) => g.CallRuntime("csrt_console_newline", g.Module.Llvm.Void, []);

    private static void WriteValue(FunctionCodeGen g, IOperation argument) => WriteValue(g, argument, null);

    /// <summary>Writes a value; with <paramref name="newline"/> set, the write (+ newline) is atomic w.r.t. other threads.</summary>
    private static void WriteValue(FunctionCodeGen g, IOperation argument, bool? newline)
    {
        var l = g.Module.Llvm;
        var op = FunctionCodeGen.Unbox(argument);
        var type = g.TypeOf(op);
        void Emit(string name, LLVMTypeRef argType, LLVMValueRef value)
        {
            if (newline is null) { g.CallRuntime(name, l.Void, [argType], value); return; }
            ConsoleLock(g, true);
            g.CallRuntime(name, l.Void, [argType], value);
            if (newline == true) NewLine(g);
            ConsoleLock(g, false);
        }
        if (op.ConstantValue.HasValue || type is null || type.TypeKind == TypeKind.Enum)
        {
            Emit("csrt_console_write_string", l.Ptr, g.ToStringValue(op));
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
        if (direct is { } d && type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64)
        {
            // A negative number starts with the current culture's NegativeSign (U+2212 in sv-SE, for
            // instance), as int.ToString() does: only non-negative values take the culture-free digit writer.
            var value = g.EmitValue(op);
            var negative = g.NewBlock("write.negative");
            var digits = g.NewBlock("write.digits");
            var done = g.NewBlock("write.done");
            g.Builder.CondBr(g.Builder.ICmp(LLVMIntPredicate.SLT, value, l.ConstInt(value.Type, 0)), negative, digits);
            g.Builder.PositionAtEnd(negative);
            var wide = value.Type.IntWidth == 64 ? value : g.Builder.Cast(LLVMOpcode.SExt, value, l.I64);
            Emit("csrt_console_write_string", l.Ptr, g.CallCoreLib("Number", "Int64ToDecStr", wide));
            g.Builder.Br(done);
            g.Builder.PositionAtEnd(digits);
            Emit(d.Name, d.Type, value);
            g.Builder.Br(done);
            g.Builder.PositionAtEnd(done);
        }
        else if (direct is { } d2)
            Emit(d2.Name, d2.Type, g.EmitValue(op));
        else
            Emit("csrt_console_write_string", l.Ptr, g.ToStringValue(op));
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
