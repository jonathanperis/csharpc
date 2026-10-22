using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Recursive patterns — `{ Prop: p, Nested.Prop: q }`, positional `(p, q)` via Deconstruct or tuples —
/// and short-circuiting `and`/`or` patterns. Sub-patterns are tested in order and stop at the
/// first failure, so `x is not null and { Length: > 3 }` never touches a null.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>Value the current property sub-pattern reads members from (InstanceReferenceKind.PatternInput).</summary>
    private (LLVMValueRef Value, ITypeSymbol Type)? _patternInput;

    internal LLVMValueRef PatternInputValue(IOperation at) =>
        _patternInput?.Value ?? throw CompileError.Unsupported("pattern input outside a property pattern", at);

    /// <summary>Evaluates tests left to right, stopping at the first false (or first true for `or`).</summary>
    private LLVMValueRef ShortCircuit(bool isAnd, IEnumerable<Func<LLVMValueRef>> tests)
    {
        var result = NewTemp(L.I8, isAnd ? "pat.and" : "pat.or");
        _b.Store(L.ConstInt(L.I8, isAnd ? 0 : 1), result);
        var done = NewBlock("pat.done");
        foreach (var test in tests)
        {
            var value = test();
            var next = NewBlock("pat.next");
            if (isAnd) _b.CondBr(value, next, done); else _b.CondBr(value, done, next);
            _b.PositionAtEnd(next);
        }
        _b.Store(L.ConstInt(L.I8, isAnd ? 1 : 0), result);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Cast(LLVMOpcode.Trunc, _b.Load(L.I8, result), L.I1);
    }

    private LLVMValueRef BinaryPatternTest(LLVMValueRef value, ITypeSymbol? inputType, IBinaryPatternOperation binary)
    {
        if (binary.OperatorKind == BinaryOperatorKind.Or)
            return ShortCircuit(false, [() => PatternTest(value, inputType, binary.LeftPattern), () => PatternTest(value, inputType, binary.RightPattern)]);
        // `o is int n and > 40`: the right pattern's input is the left pattern's narrowed type.
        return ShortCircuit(true,
        [
            () => PatternTest(value, inputType, binary.LeftPattern),
            () =>
            {
                var narrowed = binary.LeftPattern.NarrowedType is { } n ? S(n) : inputType;
                var rightValue = value;
                if (narrowed is { IsValueType: true } && value.Type.IsPointer && !TypeMapper.IsNullable(narrowed))
                    rightValue = LoadValue(_b.InBoundsGep(L.I8, value, [L.ConstInt(L.I64, 8)]), narrowed); // unbox (test passed)
                else if (narrowed is not null && inputType is not null && !value.Type.IsPointer)
                    rightValue = Coerce(value, inputType, narrowed);
                return PatternTest(rightValue, narrowed ?? inputType, binary.RightPattern);
            },
        ]);
    }

    private LLVMValueRef RecursivePatternTest(LLVMValueRef value, ITypeSymbol? inputType, IRecursivePatternOperation pattern)
    {
        var matched = pattern.MatchedType is { } m ? S(m) : inputType!;
        var narrowed = value;
        List<Func<LLVMValueRef>> steps = [];

        // 1. Type / null test.
        var needsTypeTest = inputType is null || inputType.IsReferenceType || !SymbolEqualityComparer.Default.Equals(inputType, matched);
        if (needsTypeTest) steps.Add(() => IsInstance(value, inputType, matched, pattern));
        steps.Add(() =>
        {
            // Narrow (unbox when a value type is matched out of a reference), then bind `var` designation.
            narrowed = matched.IsValueType && value.Type.IsPointer
                ? LoadValue(_b.InBoundsGep(L.I8, value, [L.ConstInt(L.I64, 8)]), matched)
                : value;
            if (pattern.DeclaredSymbol is ILocalSymbol local)
                StoreValue(ToStorage(Coerce(narrowed, matched, S(local.Type)), S(local.Type)), LocalSlot(local));
            return L.True;
        });

        // 2. Positional sub-patterns: Deconstruct(out ...) or tuple elements.
        if (!pattern.DeconstructionSubpatterns.IsEmpty)
        {
            LLVMValueRef[]? elements = null;
            ITypeSymbol[] elementTypes = [];
            steps.Add(() =>
            {
                (elements, elementTypes) = Deconstruct(narrowed, matched, pattern);
                return L.True;
            });
            for (var i = 0; i < pattern.DeconstructionSubpatterns.Length; i++)
            {
                var index = i;
                steps.Add(() => PatternTest(elements![index], elementTypes[index], pattern.DeconstructionSubpatterns[index]));
            }
        }

        // 3. Property sub-patterns, read from the narrowed input.
        foreach (var sub in pattern.PropertySubpatterns)
        {
            steps.Add(() =>
            {
                var saved = _patternInput;
                _patternInput = (narrowed, matched);
                try
                {
                    var memberValue = EmitValue(sub.Member);
                    return PatternTest(memberValue, TypeOf(sub.Member), sub.Pattern);
                }
                finally { _patternInput = saved; }
            });
        }
        return ShortCircuit(isAnd: true, steps);
    }

    /// <summary>`var (a, b) = obj;` through a user Deconstruct(out ..., out ...) method.</summary>
    private LLVMValueRef? UserDeconstruct(ITupleOperation targets, IOperation value)
    {
        var syntax = value.Parent?.Syntax ?? value.Syntax;
        var model = _m.ModelFor(syntax.SyntaxTree);
        var info = syntax switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax a => model.GetDeconstructionInfo(a),
            Microsoft.CodeAnalysis.CSharp.Syntax.ForEachVariableStatementSyntax f => model.GetDeconstructionInfo(f),
            _ => default,
        };
        var method = info.Method;
        // `foreach (var (k, v) in dict)`: the operation's syntax is the declaration; look further up.
        for (var node = syntax.Parent; method is null && node is not null && node is not Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax; node = node.Parent)
            method = node switch
            {
                Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax a => model.GetDeconstructionInfo(a).Method,
                _ => null,
            };
        if (method is null && syntax.FirstAncestorOrSelf<Microsoft.CodeAnalysis.CSharp.Syntax.ForEachVariableStatementSyntax>() is { } loop)
            method = model.GetDeconstructionInfo(loop).Method;
        // Last resort: an instance Deconstruct(out ...) with the right arity on the (corelib-mapped) type.
        method ??= TypeOf(value)?.GetMembers("Deconstruct").OfType<IMethodSymbol>()
            .FirstOrDefault(m => !m.IsStatic && m.Parameters.Length == targets.Elements.Length && m.Parameters.All(p => p.RefKind == RefKind.Out));
        if (method is null || targets.Elements.Any(e => e is ITupleOperation)) return null;
        var addresses = targets.Elements.Select(e => e is IDiscardOperation ? default : AddressOf(e)).ToArray();
        var (values, types) = CallDeconstruct(method, EmitValue(value), TypeOf(value)!, value);
        for (var i = 0; i < addresses.Length; i++)
        {
            if (addresses[i].Handle == 0) continue;
            var targetType = TypeOf(targets.Elements[i])!;
            StoreValue(ToStorage(Coerce(values[i], types[i], targetType), targetType), addresses[i]);
        }
        return default(LLVMValueRef);
    }

    /// <summary>List patterns `[p0, p1, .., pk]` over arrays, strings and spans.</summary>
    private LLVMValueRef ListPatternTest(LLVMValueRef value, ITypeSymbol? inputType, IListPatternOperation pattern)
    {
        var type = S(inputType!);
        Func<LLVMValueRef> length;
        Func<LLVMValueRef, LLVMValueRef> element; // index (i32) -> element value
        ITypeSymbol elementType;
        switch (type)
        {
            case IArrayTypeSymbol { Rank: 1 } array:
                elementType = array.ElementType;
                length = () => _b.Cast(LLVMOpcode.Trunc, _b.Load(L.I64, _b.StructGep(T.ArrayHeader(L.I8), value, 1), "len"), L.I32);
                element = i => LoadValue(_b.InBoundsGep(T.ArrayHeader(T.Storage(elementType)), value,
                    [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), _b.Cast(LLVMOpcode.SExt, i, L.I64)]), elementType);
                break;
            case { SpecialType: SpecialType.System_String }:
                elementType = _m.Compilation.GetSpecialType(SpecialType.System_Char);
                length = () => _b.Load(L.I32, _b.StructGep(T.StringHeader, value, 1), "strlen");
                element = i => _b.Load(L.I16, _b.InBoundsGep(T.StringHeader, value, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), i]));
                break;
            case var span when TypeMapper.IsSpan(span):
                elementType = SpanElement(span);
                length = () => _b.ExtractValue(value, 1);
                element = i => LoadValue(_b.InBoundsGep(T.Storage(elementType), _b.ExtractValue(value, 0), [_b.Cast(LLVMOpcode.SExt, i, L.I64)]), elementType);
                break;
            default:
                throw CompileError.Unsupported($"list pattern on '{type}'", pattern);
        }

        var patterns = pattern.Patterns;
        var slice = patterns.ToList().FindIndex(p => p is ISlicePatternOperation);
        var fixedCount = patterns.Length - (slice >= 0 ? 1 : 0);
        if (slice >= 0 && ((ISlicePatternOperation)patterns[slice]).Pattern is { } slicePattern and not IDiscardPatternOperation)
            throw CompileError.Unsupported("sub-pattern on a slice (`.. var rest`)", slicePattern);

        LLVMValueRef len = default;
        List<Func<LLVMValueRef>> steps = [];
        if (type.IsReferenceType) steps.Add(() => _b.ICmp(LLVMIntPredicate.NE, value, LlvmContext.Null(L.Ptr)));
        steps.Add(() =>
        {
            len = length();
            return slice >= 0
                ? _b.ICmp(LLVMIntPredicate.SGE, len, L.ConstInt(L.I32, fixedCount))
                : _b.ICmp(LLVMIntPredicate.EQ, len, L.ConstInt(L.I32, fixedCount));
        });
        if (pattern.DeclaredSymbol is ILocalSymbol local)
            steps.Add(() => { StoreValue(ToStorage(value, S(local.Type)), LocalSlot(local)); return L.True; });
        for (var i = 0; i < patterns.Length; i++)
        {
            if (i == slice) continue;
            var position = i;
            var fromEnd = slice >= 0 && i > slice ? patterns.Length - i : 0;
            steps.Add(() =>
            {
                var index = fromEnd > 0 ? _b.BinOp(LLVMOpcode.Sub, len, L.ConstInt(L.I32, fromEnd)) : L.ConstInt(L.I32, position);
                return PatternTest(element(index), elementType, patterns[position]);
            });
        }
        return ShortCircuit(isAnd: true, steps);
    }

    private (LLVMValueRef[] Values, ITypeSymbol[] Types) Deconstruct(LLVMValueRef value, ITypeSymbol type, IRecursivePatternOperation pattern) =>
        pattern.DeconstructSymbol is IMethodSymbol method ? CallDeconstruct(method, value, type, pattern) : DeconstructTuple(value, type, pattern);

    private (LLVMValueRef[] Values, ITypeSymbol[] Types) CallDeconstruct(IMethodSymbol method, LLVMValueRef value, ITypeSymbol type, IOperation pattern)
    {
        {
            method = S(method);
            var outs = method.Parameters.Skip(method.IsExtensionMethod ? 1 : 0).ToArray();
            var temps = outs.Select(p => NewTemp(T.Storage(S(p.Type)), "deconstruct")).ToArray();
            List<LLVMValueRef> args = [];
            if (method.IsExtensionMethod || method.IsStatic) args.Add(value);
            else if (type.IsValueType) args.Add(Spill(value)); // struct `this` is an address
            else { NullCheck(value); args.Add(value); }
            args.AddRange(temps);
            if (!_m.IsUserMethod(method)) throw CompileError.Unsupported($"library Deconstruct '{method}'", pattern);
            DirectCall(method, [.. args]);
            var types = outs.Select(p => S(p.Type)).ToArray();
            return (temps.Select((t, i) => LoadValue(t, types[i])).ToArray(), types);
        }
    }

    private (LLVMValueRef[] Values, ITypeSymbol[] Types) DeconstructTuple(LLVMValueRef value, ITypeSymbol type, IOperation pattern)
    {
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            var types = tuple.TupleElements.Select(e => S(e.Type)).ToArray();
            return (types.Select((_, i) => TupleElement(value, tuple, i)).ToArray(), types);
        }
        throw CompileError.Unsupported($"positional pattern on '{type}' (ITuple)", pattern);
    }
}
