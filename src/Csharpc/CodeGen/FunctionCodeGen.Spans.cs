using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Span&lt;T&gt; / ReadOnlySpan&lt;T&gt; (docs/03-lowering.md#spans): a ref struct lowered to the aggregate
/// { ptr reference, i32 length }, special-cased like Nullable&lt;T&gt; and ValueTuple (no generic code is
/// compiled for it). The indexer returns `ref T`, so `span[i] = x` and `ref var e = ref span[i]`
/// write through. Span enumerators are { ptr, i32 length, i32 index } and become index loops.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private LLVMTypeRef SpanType => L.Struct(L.Ptr, L.I32);

    internal LLVMValueRef MakeSpan(LLVMValueRef pointer, LLVMValueRef length)
    {
        var span = _b.InsertValue(LlvmContext.Null(SpanType), pointer, 0);
        return _b.InsertValue(span, length, 1);
    }

    private LLVMValueRef SpanPointer(LLVMValueRef span) => _b.ExtractValue(span, 0, "span.ptr");
    private LLVMValueRef SpanLength(LLVMValueRef span) => _b.ExtractValue(span, 1, "span.len");

    private static ITypeSymbol SpanElement(ITypeSymbol spanType) => ((INamedTypeSymbol)spanType).TypeArguments[0];

    private LLVMValueRef ElementSize(ITypeSymbol element) => L.ConstInt(L.I64, (long)T.Target.SizeOf(T.Storage(element)));

    private void ThrowArgumentOutOfRangeIf(LLVMValueRef failed) =>
        ThrowLibraryExceptionIf(failed, "System.ArgumentOutOfRangeException", "Specified argument was out of the range of valid values.");

    /// <summary>Span over an array's elements (null array → empty span), optionally a [start, start+length) window.</summary>
    private LLVMValueRef ArraySpan(LLVMValueRef array, ITypeSymbol element, LLVMValueRef? start = null, LLVMValueRef? length = null)
    {
        var header = T.ArrayHeader(T.Storage(element));
        var result = NewTemp(SpanType, "array.span");
        _b.Store(LlvmContext.Null(SpanType), result);
        var nonNull = NewBlock("span.array");
        var done = NewBlock("span.done");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, array, LlvmContext.Null(L.Ptr)), start is null ? done : ThrowNullBlock(), nonNull);
        _b.PositionAtEnd(nonNull);
        var count = _b.Cast(LLVMOpcode.Trunc, _b.Load(L.I64, _b.StructGep(header, array, 1)), L.I32);
        var data = _b.StructGep(header, array, 2);
        LLVMValueRef span;
        if (start is { } s)
        {
            var len = length ?? _b.BinOp(LLVMOpcode.Sub, count, s);
            CheckWindow(s, len, count);
            span = MakeSpan(_b.InBoundsGep(T.Storage(element), data, [_b.Cast(LLVMOpcode.ZExt, s, L.I64)]), len);
        }
        else span = MakeSpan(data, count);
        _b.Store(span, result);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(SpanType, result);
    }

    private LLVMBasicBlockRef ThrowNullBlock()
    {
        var block = NewBlock("span.null");
        var current = _b.InsertBlock;
        _b.PositionAtEnd(block);
        CallRuntime("csrt_throw_null_reference", L.Void, []);
        _b.Unreachable();
        _b.PositionAtEnd(current);
        return block;
    }

    /// <summary>.NET's check: (ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)total → ArgumentOutOfRange.</summary>
    private void CheckWindow(LLVMValueRef start, LLVMValueRef length, LLVMValueRef total)
    {
        var s = _b.Cast(LLVMOpcode.ZExt, start, L.I64);
        var l = _b.Cast(LLVMOpcode.ZExt, length, L.I64);
        var t = _b.Cast(LLVMOpcode.ZExt, total, L.I64);
        ThrowArgumentOutOfRangeIf(_b.ICmp(LLVMIntPredicate.UGT, _b.BinOp(LLVMOpcode.Add, s, l), t));
    }

    private LLVMValueRef StringSpan(LLVMValueRef str)
    {
        // string -> ReadOnlySpan<char>; a null string gives an empty span.
        var isNull = _b.ICmp(LLVMIntPredicate.EQ, str, LlvmContext.Null(L.Ptr));
        var safe = _b.Select(isNull, _m.StringLiteral(""), str);
        var length = _b.Load(L.I32, _b.StructGep(T.StringHeader, safe, 1));
        return MakeSpan(_b.StructGep(T.StringHeader, safe, 2), length);
    }

    /// <summary>Conversions into spans: T[] → Span/ReadOnlySpan, string → ReadOnlySpan&lt;char&gt;, Span → ReadOnlySpan.</summary>
    private LLVMValueRef? TrySpanConversion(IConversionOperation c)
    {
        if (!TypeMapper.IsSpan(TypeOf(c))) return null;
        var from = TypeOf(c.Operand);
        if (c.Operand is ICollectionExpressionOperation collection) return SpanFromCollection(collection, SpanElement(TypeOf(c)!));
        if (TypeMapper.IsSpan(from)) return EmitValue(c.Operand);
        if (from is IArrayTypeSymbol { Rank: 1 } array) return ArraySpan(EmitValue(c.Operand), array.ElementType);
        if (from?.SpecialType == SpecialType.System_String) return StringSpan(EmitValue(c.Operand));
        if (from is null) return LlvmContext.Null(SpanType);
        if (TypeMapper.IsSystemStruct(_m.CoreLib.Unmap(from), "ArraySegment`1")) return ArraySegmentSpan(c.Operand);
        return null;
    }

    /// <summary>
    /// Span/Memory's implicit conversion from ArraySegment&lt;T&gt; (the corelib's ArraySegmentImpl):
    /// new Span&lt;T&gt;(segment.Array, segment.Offset, segment.Count); a default segment gives an empty span.
    /// The segment's constructors already validated the window.
    /// </summary>
    private LLVMValueRef ArraySegmentSpan(IOperation segment)
    {
        var type = (INamedTypeSymbol)TypeOf(segment)!;
        var element = type.TypeArguments[0];
        var address = AddressOrSpill(segment);
        IFieldSymbol Field(string name) => type.GetMembers(name).OfType<IFieldSymbol>().Single();
        var array = LoadValue(FieldAddressOn(address, Field("_array")), Field("_array").Type);
        var offset = LoadValue(FieldAddressOn(address, Field("_offset")), Field("_offset").Type);
        var count = LoadValue(FieldAddressOn(address, Field("_count")), Field("_count").Type);
        var header = T.ArrayHeader(T.Storage(element));
        var result = NewTemp(SpanType, "segment.span");
        _b.Store(LlvmContext.Null(SpanType), result);
        var nonNull = NewBlock("segment.array");
        var done = NewBlock("segment.done");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, array, LlvmContext.Null(L.Ptr)), done, nonNull);
        _b.PositionAtEnd(nonNull);
        var data = _b.InBoundsGep(T.Storage(element), _b.StructGep(header, array, 2), [_b.Cast(LLVMOpcode.ZExt, offset, L.I64)]);
        _b.Store(MakeSpan(data, count), result);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(SpanType, result);
    }

    /// <summary>Collection expressions targeting spans: backed by a fresh GC array (safe for any span lifetime).</summary>
    private LLVMValueRef SpanFromCollection(ICollectionExpressionOperation collection, ITypeSymbol element)
    {
        if (collection.Elements.Any(e => e is ISpreadOperation)) throw CompileError.Unsupported("spread elements", collection);
        var array = NewArray(element, L.ConstInt(L.I64, collection.Elements.Length));
        StoreElements(array, element, collection.Elements);
        var header = T.ArrayHeader(T.Storage(element));
        return MakeSpan(_b.StructGep(header, array, 2), L.ConstInt(L.I32, collection.Elements.Length));
    }

    private LLVMValueRef EmitUtf8String(IUtf8StringOperation op) => EmitUtf8Text(op.Value);

    private LLVMValueRef EmitUtf8Text(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
        return MakeSpan(_m.CString(text), L.ConstInt(L.I32, bytes));
    }

    /// <summary>`"a"u8 + "b"u8`: UTF-8 literal concatenation is a compile-time operation.</summary>
    private static bool TryUtf8Concatenation(IBinaryOperation op, out string text)
    {
        text = "";
        if (op.OperatorKind != BinaryOperatorKind.Add) return false;
        static string? Part(IOperation o) => o switch
        {
            IUtf8StringOperation u => u.Value,
            IBinaryOperation b when TryUtf8Concatenation(b, out var t) => t,
            _ => null,
        };
        if (Part(op.LeftOperand) is not { } left || Part(op.RightOperand) is not { } right) return false;
        text = left + right;
        return true;
    }

    /// <summary>`ref T` to span[index], bounds-checked (IndexOutOfRangeException, as in .NET).</summary>
    private LLVMValueRef SpanElementAddress(LLVMValueRef span, ITypeSymbol element, LLVMValueRef index)
    {
        FailIf(_b.ICmp(LLVMIntPredicate.UGE, index, SpanLength(span)), "csrt_throw_index_out_of_range");
        return DataAddress(_b.InBoundsGep(T.Storage(element), SpanPointer(span), [_b.Cast(LLVMOpcode.ZExt, index, L.I64)]), DataKind.Any);
    }

    private LLVMValueRef SpanByteCount(LLVMValueRef span, ITypeSymbol element) =>
        _b.BinOp(LLVMOpcode.Mul, _b.Cast(LLVMOpcode.ZExt, SpanLength(span), L.I64), ElementSize(element));

    // ======================================================================================
    // Members
    // ======================================================================================

    private LLVMValueRef SpanReceiver(IOperation instance) => EmitValue(instance);

    private LLVMValueRef ArgValue(IInvocationOperation inv, int ordinal)
    {
        var argument = inv.Arguments.First(a => a.Parameter!.Ordinal == ordinal);
        return Coerce(EmitValue(argument.Value), TypeOf(argument.Value), S(argument.Parameter!.Type));
    }

    /// <summary>Address of a `ref`-returning property: span indexer / enumerator Current / user getter.</summary>
    internal LLVMValueRef RefPropertyAddress(IPropertyReferenceOperation p)
    {
        var property = S(p.Property);
        var owner = property.ContainingType;
        if (TypeMapper.IsSpan(owner) && property.IsIndexer)
        {
            var span = SpanReceiver(p.Instance!);
            var index = Coerce(EmitValue(p.Arguments[0].Value), TypeOf(p.Arguments[0].Value), _m.Compilation.GetSpecialType(SpecialType.System_Int32));
            return SpanElementAddress(span, SpanElement(owner), index);
        }
        if (TypeMapper.IsSpanEnumerator(owner) && property.Name == "Current")
        {
            var e = AddressOrSpill(p.Instance!);
            var enumType = T.Value(owner);
            var ptr = _b.Load(L.Ptr, _b.StructGep(enumType, e, 0));
            var index = _b.Load(L.I32, _b.StructGep(enumType, e, 2));
            return DataAddress(_b.InBoundsGep(T.Storage(SpanElement(owner.ContainingType!)), ptr, [_b.Cast(LLVMOpcode.ZExt, index, L.I64)]), DataKind.Any);
        }
        if (_m.IsUserType(owner))
        {
            var getter = property.GetMethod!;
            var self = property.IsStatic ? default : Receiver(p.Instance!, getter);
            return CallAccessor(p, getter, self, []);
        }
        throw CompileError.Unsupported($"ref-returning property '{property}'", p);
    }

    private bool TrySpanProperty(IPropertyReferenceOperation p, out LLVMValueRef result)
    {
        if (TryIndexRangeMember(p, out result) || TryLowLevelLibraryMember(p, out result)) return true;
        var owner = S(p.Property).ContainingType;
        if (p.Property.RefKind != RefKind.None && (TypeMapper.IsSpan(owner) || TypeMapper.IsSpanEnumerator(owner)))
        {
            result = LoadValue(RefPropertyAddress(p), TypeOf(p)!);
            return true;
        }
        if (!TypeMapper.IsSpan(owner)) return false;
        switch (p.Property.Name)
        {
            case "Length": result = SpanLength(SpanReceiver(p.Instance!)); return true;
            case "IsEmpty": result = _b.ICmp(LLVMIntPredicate.EQ, SpanLength(SpanReceiver(p.Instance!)), L.ConstInt(L.I32, 0)); return true;
            case "Empty": result = LlvmContext.Null(SpanType); return true;
            case "Span": result = SpanReceiver(p.Instance!); return true; // Memory<T>.Span
        }
        return false;
    }

    private bool TrySpanConstruction(IObjectCreationOperation creation, out LLVMValueRef result)
    {
        result = default;
        var type = TypeOf(creation) as INamedTypeSymbol;
        var args = creation.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        LLVMValueRef Arg(int i) => Coerce(EmitValue(args[i].Value), TypeOf(args[i].Value), S(args[i].Parameter!.Type));
        if (type?.SpecialType == SpecialType.System_String && args.Length == 1 && TypeMapper.IsSpan(S(args[0].Parameter!.Type)))
        {
            var span = Arg(0);
            result = CallRuntime("csrt_string_from_chars", L.Ptr, [L.Ptr, L.I32], SpanPointer(span), SpanLength(span));
            return true;
        }
        if (!TypeMapper.IsSpan(type)) return false;
        var element = SpanElement(type!);
        switch (args.Length)
        {
            case 0: result = LlvmContext.Null(SpanType); return true;
            case 1 when S(args[0].Parameter!.Type) is IArrayTypeSymbol: result = ArraySpan(Arg(0), element); return true;
            // new Span<T>(ref x) / new ReadOnlySpan<T>(in x): a one-element span over the variable.
            case 1 when args[0].Parameter!.RefKind != RefKind.None: result = MakeSpan(AddressOf(args[0].Value), L.ConstInt(L.I32, 1)); return true;
            case 3 when S(args[0].Parameter!.Type) is IArrayTypeSymbol: result = ArraySpan(Arg(0), element, Arg(1), Arg(2)); return true;
            case 2 when S(args[0].Parameter!.Type) is IPointerTypeSymbol:
            {
                var length = Arg(1);
                ThrowArgumentOutOfRangeIf(_b.ICmp(LLVMIntPredicate.SLT, length, L.ConstInt(L.I32, 0)));
                result = MakeSpan(Arg(0), length);
                return true;
            }
        }
        throw CompileError.Unsupported($"span constructor '{creation.Constructor}'", creation);
    }

    private bool TrySpanMethod(IInvocationOperation inv, out LLVMValueRef result)
    {
        if (TryIndexRangeMember(inv, out result) || TryLowLevelLibraryMember(inv, out result)) return true;
        var method = S(inv.TargetMethod);
        var owner = method.ContainingType;
        if (TypeMapper.IsSpanEnumerator(owner) && method.Name == "MoveNext")
        {
            var e = AddressOrSpill(inv.Instance!);
            var enumType = T.Value(owner);
            var indexAddress = _b.StructGep(enumType, e, 2);
            var index = _b.BinOp(LLVMOpcode.Add, _b.Load(L.I32, indexAddress), L.ConstInt(L.I32, 1));
            _b.Store(index, indexAddress);
            result = _b.ICmp(LLVMIntPredicate.SLT, index, _b.Load(L.I32, _b.StructGep(enumType, e, 1)));
            return true;
        }
        if (TypeMapper.IsSpan(owner))
        {
            result = SpanInstanceMethod(inv, owner);
            return true;
        }
        if (owner.Name == "MemoryExtensions" && owner.ContainingNamespace.ToDisplayString() == "System")
        {
            result = MemoryExtensionsMethod(inv);
            return true;
        }
        return TryParamsSpanMethod(inv, out result);
    }

    private LLVMValueRef SpanInstanceMethod(IInvocationOperation inv, INamedTypeSymbol owner)
    {
        var element = SpanElement(owner);
        var span = SpanReceiver(inv.Instance!);
        var method = inv.TargetMethod;
        switch (method.Name, method.Parameters.Length)
        {
            case ("GetEnumerator", 0):
            {
                var enumType = T.Value(method.ReturnType);
                var e = _b.InsertValue(LlvmContext.Null(enumType), SpanPointer(span), 0);
                e = _b.InsertValue(e, SpanLength(span), 1);
                return _b.InsertValue(e, L.ConstInt(L.I32, -1), 2);
            }
            case ("Slice", 1):
            {
                var start = ArgValue(inv, 0);
                var length = SpanLength(span);
                ThrowArgumentOutOfRangeIf(_b.ICmp(LLVMIntPredicate.UGT, start, length));
                return MakeSpan(_b.InBoundsGep(T.Storage(element), SpanPointer(span), [_b.Cast(LLVMOpcode.ZExt, start, L.I64)]),
                    _b.BinOp(LLVMOpcode.Sub, length, start));
            }
            case ("Slice", 2):
            {
                var start = ArgValue(inv, 0);
                var length = ArgValue(inv, 1);
                CheckWindow(start, length, SpanLength(span));
                return MakeSpan(_b.InBoundsGep(T.Storage(element), SpanPointer(span), [_b.Cast(LLVMOpcode.ZExt, start, L.I64)]), length);
            }
            case ("ToArray", 0):
                return CallRuntime("csrt_span_to_array", L.Ptr, [L.Ptr, L.I32, L.I64], SpanPointer(span), SpanLength(span), ElementSize(element));
            case ("Clear", 0):
                MemSet(SpanPointer(span), SpanByteCount(span, element));
                return default;
            case ("Fill", 1):
            {
                var value = ToStorage(ArgValue(inv, 0), element);
                ForEachIndex(SpanLength(span), i => _b.Store(value, _b.InBoundsGep(T.Storage(element), SpanPointer(span), [i])));
                return default;
            }
            case ("CopyTo", 1) or ("TryCopyTo", 1):
            {
                var destination = ArgValue(inv, 0);
                var tooShort = _b.ICmp(LLVMIntPredicate.ULT, SpanLength(destination), SpanLength(span));
                if (method.Name == "TryCopyTo")
                {
                    var copy = NewBlock("trycopy.copy");
                    var done = NewBlock("trycopy.done");
                    _b.CondBr(tooShort, done, copy);
                    _b.PositionAtEnd(copy);
                    MemMove(SpanPointer(destination), SpanPointer(span), SpanByteCount(span, element));
                    _b.Br(done);
                    _b.PositionAtEnd(done);
                    return _b.Not(tooShort);
                }
                ThrowLibraryExceptionIf(tooShort, "System.ArgumentException", "Destination is too short. (Parameter 'destination')");
                MemMove(SpanPointer(destination), SpanPointer(span), SpanByteCount(span, element));
                return default;
            }
            case ("ToString", 0) when element.SpecialType == SpecialType.System_Char:
                return CallRuntime("csrt_string_from_chars", L.Ptr, [L.Ptr, L.I32], SpanPointer(span), SpanLength(span));
            case ("ToString", 0):
            {
                var prefix = _m.StringLiteral($"System.{owner.Name}<{element.MetadataName}>[");
                var text = ConcatStrings(prefix, FormatPrimitive(SpanLength(span), _m.Compilation.GetSpecialType(SpecialType.System_Int32))!.Value);
                return ConcatStrings(text, _m.StringLiteral("]"));
            }
        }
        throw CompileError.Unsupported($"'{method.ToDisplayString()}' on spans", inv);
    }

    /// <summary>Runs <paramref name="body"/> for i in [0, count) (i is an i64).</summary>
    private void ForEachIndex(LLVMValueRef count, Action<LLVMValueRef> body)
    {
        var counter = NewTemp(L.I64, "i");
        _b.Store(L.ConstInt(L.I64, 0), counter);
        var test = NewBlock("loop.test");
        var loop = NewBlock("loop.body");
        var done = NewBlock("loop.done");
        var limit = _b.Cast(LLVMOpcode.ZExt, count, L.I64);
        _b.Br(test);
        _b.PositionAtEnd(test);
        var i = _b.Load(L.I64, counter);
        _b.CondBr(_b.ICmp(LLVMIntPredicate.ULT, i, limit), loop, done);
        _b.PositionAtEnd(loop);
        body(i);
        _b.Store(_b.BinOp(LLVMOpcode.Add, i, L.ConstInt(L.I64, 1)), counter);
        _b.Br(test);
        _b.PositionAtEnd(done);
    }

    /// <summary>The span from an argument that is a span, an array, or a string (MemoryExtensions overloads).</summary>
    private LLVMValueRef SpanArgument(IArgumentOperation argument, out ITypeSymbol element)
    {
        var type = S(argument.Parameter!.Type);
        if (TypeMapper.IsSpan(type)) { element = SpanElement(type); return Coerce(EmitValue(argument.Value), TypeOf(argument.Value), type); }
        if (type is IArrayTypeSymbol array) { element = ((IArrayTypeSymbol)TypeOf(argument.Value)!).ElementType; return ArraySpan(EmitValue(argument.Value), element); }
        if (type.SpecialType == SpecialType.System_String) { element = _m.Compilation.GetSpecialType(SpecialType.System_Char); return StringSpan(EmitValue(argument.Value)); }
        throw CompileError.Unsupported($"span argument of type '{type}'", argument);
    }

    private LLVMValueRef MemoryExtensionsMethod(IInvocationOperation inv)
    {
        var method = inv.TargetMethod;
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        // Evaluate arguments in source order first.
        switch (method.Name)
        {
            case "AsSpan" or "AsMemory" when args.Length is >= 1 and <= 3 && args.Skip(1).All(a => S(a.Parameter!.Type).SpecialType == SpecialType.System_Int32):
            {
                var source = args[0];
                LLVMValueRef? start = args.Length > 1 ? Coerce(EmitValue(args[1].Value), TypeOf(args[1].Value), S(args[1].Parameter!.Type)) : null;
                LLVMValueRef? length = args.Length > 2 ? Coerce(EmitValue(args[2].Value), TypeOf(args[2].Value), S(args[2].Parameter!.Type)) : null;
                if (S(source.Parameter!.Type) is IArrayTypeSymbol)
                {
                    var element = ((IArrayTypeSymbol)TypeOf(source.Value)!).ElementType;
                    return ArraySpan(EmitValue(source.Value), element, start, length);
                }
                var span = StringSpan(EmitValue(source.Value));
                if (start is null) return span;
                var len = length ?? _b.BinOp(LLVMOpcode.Sub, SpanLength(span), start.Value);
                CheckWindow(start.Value, len, SpanLength(span));
                return MakeSpan(_b.InBoundsGep(L.I16, SpanPointer(span), [_b.Cast(LLVMOpcode.ZExt, start.Value, L.I64)]), len);
            }
            case "IndexOf" or "LastIndexOf" or "Contains" when args.Length == 2 && !TypeMapper.IsSpan(S(args[1].Parameter!.Type)):
            {
                var span = SpanArgument(args[0], out var element);
                var value = Spill(ToStorage(Coerce(EmitValue(args[1].Value), TypeOf(args[1].Value), element), element));
                var index = CallRuntime(method.Name == "LastIndexOf" ? "csrt_span_last_index_of" : "csrt_span_index_of", L.I32,
                    [L.Ptr, L.I32, L.I64, L.Ptr], SpanPointer(span), SpanLength(span), ElementSize(element), value);
                return method.Name == "Contains" ? _b.ICmp(LLVMIntPredicate.SGE, index, L.ConstInt(L.I32, 0)) : index;
            }
            case "IndexOf" when args.Length == 2:
            {
                var span = SpanArgument(args[0], out var element);
                var needle = SpanArgument(args[1], out _);
                return CallRuntime("csrt_span_index_of_span", L.I32, [L.Ptr, L.I32, L.Ptr, L.I32, L.I64],
                    SpanPointer(span), SpanLength(span), SpanPointer(needle), SpanLength(needle), ElementSize(element));
            }
            case "SequenceEqual" or "StartsWith" or "EndsWith" when args.Length == 2:
            {
                var a = SpanArgument(args[0], out var element);
                var b = SpanArgument(args[1], out _);
                var mode = method.Name switch { "SequenceEqual" => 0, "StartsWith" => 1, _ => 2 };
                return CallRuntime("csrt_span_compare_mode", L.I1, [L.Ptr, L.I32, L.Ptr, L.I32, L.I64, L.I32],
                    SpanPointer(a), SpanLength(a), SpanPointer(b), SpanLength(b), ElementSize(element), L.ConstInt(L.I32, mode));
            }
            case "Reverse" when args.Length == 1:
            {
                var span = SpanArgument(args[0], out var element);
                CallRuntime("csrt_span_reverse", L.Void, [L.Ptr, L.I32, L.I64], SpanPointer(span), SpanLength(span), ElementSize(element));
                return default;
            }
            case "Trim" or "TrimStart" or "TrimEnd" when args.Length == 1:
            {
                var span = SpanArgument(args[0], out _);
                var mode = method.Name switch { "TrimStart" => 1, "TrimEnd" => 2, _ => 3 };
                var trimmed = Spill(LlvmContext.Null(SpanType));
                CallRuntime("csrt_span_trim", L.Void, [L.Ptr, L.I32, L.I32, L.Ptr], SpanPointer(span), SpanLength(span), L.ConstInt(L.I32, mode), trimmed);
                return _b.Load(SpanType, trimmed);
            }
        }
        throw CompileError.MissingIntrinsic(method, inv);
    }

    // ======================================================================================
    // C# 13 `params ReadOnlySpan<T>` BCL overloads (Console.WriteLine(format, a, b, c, d), ...)
    // ======================================================================================

    /// <summary>Elements of a params-span argument as an array of <paramref name="element"/> (materialized).</summary>
    private LLVMValueRef ParamsArray(IArgumentOperation argument, ITypeSymbol element, bool asStrings)
    {
        if (argument.Value is ICollectionExpressionOperation collection && collection.Elements.All(e => e is not ISpreadOperation))
        {
            var array = NewArray(element, L.ConstInt(L.I64, collection.Elements.Length));
            var storage = T.Storage(element);
            for (var i = 0; i < collection.Elements.Length; i++)
            {
                var value = asStrings ? ToStringValue(collection.Elements[i]) : Coerce(EmitValue(collection.Elements[i]), TypeOf(collection.Elements[i]), element);
                var address = _b.InBoundsGep(T.ArrayHeader(storage), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, i)]);
                StoreValue(ToStorage(value, element), address);
            }
            return array;
        }
        var span = Coerce(EmitValue(argument.Value), TypeOf(argument.Value), S(argument.Parameter!.Type));
        if (asStrings)
        {
            // A span built elsewhere (IL: an InlineArrayN of objects): copy it out, then format each element.
            var spanElement = ((INamedTypeSymbol)S(argument.Parameter!.Type)).TypeArguments[0];
            if (spanElement.SpecialType != SpecialType.System_Object) throw CompileError.Unsupported("joining a non-literal span of objects", argument);
            var objects = CallRuntime("csrt_span_to_array", L.Ptr, [L.Ptr, L.I32, L.I64], SpanPointer(span), SpanLength(span), ElementSize(spanElement));
            return CallCoreLib("ParamsSpans", "ToStrings", objects);
        }
        return CallRuntime("csrt_span_to_array", L.Ptr, [L.Ptr, L.I32, L.I64], SpanPointer(span), SpanLength(span), ElementSize(element));
    }

    /// <summary>string.Join(sep, IEnumerable&lt;T&gt;) over an array: format each element, then join.</summary>
    private bool TryJoinArray(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        var method = inv.TargetMethod;
        if (method is not { Name: "Join", ContainingType.SpecialType: SpecialType.System_String, Parameters.Length: 2 }
            || method.Parameters[1].Type is not INamedTypeSymbol { MetadataName: "IEnumerable`1" }) return false;
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        var source = args[1].Value;
        while (source is IConversionOperation { Conversion.IsImplicit: true } c) source = c.Operand;
        if (TypeOf(source) is not IArrayTypeSymbol { Rank: 1 } arrayType) return false;
        var separator = S(args[0].Parameter!.Type).SpecialType == SpecialType.System_Char
            ? CallRuntime("csrt_string_from_char", L.Ptr, [L.I16], EmitValue(args[0].Value))
            : EmitValue(args[0].Value);
        var element = arrayType.ElementType;
        var array = EmitValue(source);
        var length = ArrayLength(array);
        var strings = NewArray(_m.Compilation.GetSpecialType(SpecialType.System_String), length);
        var header = T.ArrayHeader(T.Storage(element));
        var stringsHeader = T.ArrayHeader(L.Ptr);
        ForEachIndex(_b.Cast(LLVMOpcode.Trunc, length, L.I32), i =>
        {
            var value = LoadValue(_b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), i]), element);
            var text = FormatValue(value, element, inv); // strings, primitives, enums, structs (ToString), objects
            _b.Store(text, _b.InBoundsGep(stringsHeader, strings, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), i]));
        });
        result = CallRuntime("csrt_string_join", L.Ptr, [L.Ptr, L.Ptr], separator, strings);
        return true;
    }

    private bool TryParamsSpanMethod(IInvocationOperation inv, out LLVMValueRef result)
    {
        result = default;
        if (TryJoinArray(inv, out result)) return true;
        var method = inv.TargetMethod;
        var spanParam = method.Parameters.LastOrDefault();
        if (spanParam is null || !TypeMapper.IsSpan(S(spanParam.Type))) return false;
        var owner = method.ContainingType.ToDisplayString();
        var element = SpanElement(S(spanParam.Type));
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        var stringType = _m.Compilation.GetSpecialType(SpecialType.System_String);
        switch (owner, method.Name, method.Parameters.Length)
        {
            case ("System.Console", "WriteLine" or "Write", 2) or ("string", "Format", 2) when S(args[0].Parameter!.Type).SpecialType == SpecialType.System_String:
            {
                var format = EmitValue(args[0].Value);
                var array = ParamsArray(args[1], element, asStrings: false);
                var text = CallRuntime("csrt_string_format", L.Ptr, [L.Ptr, L.Ptr], format, array);
                if (method.Name == "Format") { result = text; return true; }
                CallRuntime("csrt_console_write_string", L.Void, [L.Ptr], text);
                if (method.Name == "WriteLine") CallRuntime("csrt_console_newline", L.Void, []);
                return true;
            }
            case ("string", "Join", 2):
            {
                var separatorArg = args[0];
                var separator = S(separatorArg.Parameter!.Type).SpecialType == SpecialType.System_Char
                    ? CallRuntime("csrt_string_from_char", L.Ptr, [L.I16], EmitValue(separatorArg.Value))
                    : EmitValue(separatorArg.Value);
                var array = ParamsArray(args[1], stringType, asStrings: element.SpecialType != SpecialType.System_String);
                result = CallRuntime("csrt_string_join", L.Ptr, [L.Ptr, L.Ptr], separator, array);
                return true;
            }
            case ("string", "Concat", 1):
            {
                var array = ParamsArray(args[0], stringType, asStrings: element.SpecialType != SpecialType.System_String);
                result = CallRuntime("csrt_string_join", L.Ptr, [L.Ptr, L.Ptr], _m.StringLiteral(""), array);
                return true;
            }
        }
        return false;
    }
}
