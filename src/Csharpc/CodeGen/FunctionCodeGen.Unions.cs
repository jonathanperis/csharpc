using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// C# 15 union types. A `union` declaration is a struct holding its case value as `object? Value`, with a
/// constructor per case type; a union may also be a hand-written struct or class marked [Union], optionally
/// with the non-boxing access members (`HasValue`, `TryGetValue(out T)`) or an IUnionMembers provider
/// interface. Roslyn's operations keep the union semantics implicit: a conversion into a union names the
/// case constructor as its OperatorMethod, and a pattern whose input is a union tests either the union
/// instance or its contents depending on the pattern's form (the language reference's table):
///   - type / declaration patterns, typed property patterns, constants and relational patterns: the contents
///     (`TryGetValue` for type tests and `HasValue` for `null` when the union declares them, else `Value`);
///   - `var`, discard, untyped property patterns and list patterns: the union instance itself;
///   - `not` / `and` / `or`: pass the union through unchanged to their operands.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>`Pet p = new Cat()`: the union's constructor for the case type, on a fresh value.</summary>
    private LLVMValueRef EmitUnionConversion(IConversionOperation c, IMethodSymbol constructor)
    {
        var union = constructor.ContainingType;
        var argument = Coerce(EmitValue(c.Operand), TypeOf(c.Operand), S(constructor.Parameters[0].Type));
        LLVMValueRef result;
        if (union.IsValueType)
        {
            var llvmType = T.Value(union);
            var temp = NewTemp(llvmType, "union");
            _b.Store(LlvmContext.Null(llvmType), temp);
            DirectCall(constructor, [temp, argument]);
            result = _b.Load(llvmType, temp);
        }
        else
        {
            if (!_m.IsUserType(union)) throw CompileError.Unsupported($"converting to library union class '{union}'", c);
            var layout = _m.GetLayout(union);
            result = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
            DirectCall(constructor, [result, argument]);
        }
        // A union conversion to a nullable union struct (`Pet? p = cat`).
        var to = TypeOf(c)!;
        return TypeMapper.IsNullable(to) ? MakeNullable(to, L.True, result) : result;
    }

    /// <summary>A synthesized union constructor: `Value = (object)value`.</summary>
    public void EmitUnionConstructor()
    {
        var valueProperty = _method!.ContainingType.GetMembers("Value").OfType<IPropertySymbol>().First();
        var field = BackingField(valueProperty);
        var parameterType = S(_method.Parameters[0].Type);
        var argument = _fn.Fn.Param(1);
        var boxed = parameterType.IsValueType ? Box(argument, parameterType, null!) : argument;
        StoreValue(ToStorage(boxed, field.Type), FieldAddressOn(_this, field));
        _b.RetVoid();
        Finish();
    }

    /// <summary>A pattern applied to a union value (or a nullable union struct): null when it isn't one.</summary>
    private LLVMValueRef? TryUnionPatternTest(LLVMValueRef value, ITypeSymbol? inputType, IPatternOperation pattern)
    {
        // `pet is null` on `Pet?`: no value, or a union whose contents are null.
        if (TypeMapper.IsNullable(inputType) && TypeMapper.NullableUnderlying(inputType!) is var underlying
            && ModuleCodeGen.IsUnionType(underlying) && IsNullConstant(pattern))
        {
            var (has, payload, _) = NullableParts(value, inputType!);
            return ShortCircuit(isAnd: false, [() => _b.Not(has), () => UnionContentsAreNull(payload, (INamedTypeSymbol)underlying)]);
        }
        if (!ModuleCodeGen.IsUnionType(inputType)) return null;
        var union = (INamedTypeSymbol)inputType!;
        switch (pattern)
        {
            case IDeclarationPatternOperation { MatchesNull: true }:   // `var x`: the union itself
            case IDiscardPatternOperation:
            case IListPatternOperation:
            case INegatedPatternOperation:
            case IBinaryPatternOperation:
            case IRecursivePatternOperation { MatchedType: null }:
                return null;
            case IRecursivePatternOperation recursive when SymbolEqualityComparer.Default.Equals(S(recursive.MatchedType!), union):
                return null;
        }
        // A class union: a null reference matches `null` and nothing else.
        if (!union.IsValueType)
        {
            var isNull = _b.ICmp(LLVMIntPredicate.EQ, value, LlvmContext.Null(L.Ptr));
            if (IsNullConstant(pattern))
                return ShortCircuit(isAnd: false, [() => isNull, () => UnionContentsAreNull(value, union)]);
            return ShortCircuit(isAnd: true, [() => _b.Not(isNull), () => UnionContentsTest(value, union, pattern)]);
        }
        if (IsNullConstant(pattern)) return UnionContentsAreNull(value, union);
        return UnionContentsTest(value, union, pattern);
    }

    private static bool IsNullConstant(IPatternOperation pattern) =>
        pattern is IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } };

    /// <summary>The pattern applied to the union's contents: `TryGetValue(out T)` for a type test when declared, else `Value`.</summary>
    private LLVMValueRef UnionContentsTest(LLVMValueRef union, INamedTypeSymbol unionType, IPatternOperation pattern)
    {
        // Constant and relational patterns use TryGetValue for the constant's type too (`counted is 5`
        // calls TryGetValue(out int), not Value — measured against .NET).
        var matched = pattern switch
        {
            IDeclarationPatternOperation d => d.MatchedType,
            ITypePatternOperation t => t.MatchedType,
            IRecursivePatternOperation r => r.MatchedType,
            IConstantPatternOperation k => TypeOf(k.Value),
            IRelationalPatternOperation rel => TypeOf(rel.Value),
            _ => null,
        };
        if (matched is not null && UnionTryGetValue(unionType, S(matched)) is { } tryGetValue)
        {
            var caseType = S(tryGetValue.Parameters[0].Type);
            var slot = NewTemp(T.Value(caseType), "union.case");
            _b.Store(LlvmContext.Null(T.Value(caseType)), slot);
            var found = AsBool(DirectCall(tryGetValue, [UnionThis(union, unionType), slot]));
            if (pattern is ITypePatternOperation) return found;
            return ShortCircuit(isAnd: true, [() => found, () => PatternTest(_b.Load(T.Value(caseType), slot), caseType, pattern)]);
        }
        var contents = UnionValue(union, unionType);
        return PatternTest(contents, _m.Compilation.GetSpecialType(SpecialType.System_Object), pattern);
    }

    /// <summary>`null` on a union's contents: `!HasValue` when declared, else `Value == null`.</summary>
    private LLVMValueRef UnionContentsAreNull(LLVMValueRef union, INamedTypeSymbol unionType)
    {
        if (ModuleCodeGen.UnionMembers(unionType, "HasValue").OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.Type.SpecialType == SpecialType.System_Boolean && p.GetMethod is not null) is { } hasValue)
            return _b.Not(AsBool(DirectCall(S(hasValue.GetMethod!), [UnionThis(union, unionType)])));
        return _b.ICmp(LLVMIntPredicate.EQ, UnionValue(union, unionType), LlvmContext.Null(L.Ptr));
    }

    private LLVMValueRef UnionValue(LLVMValueRef union, INamedTypeSymbol unionType)
    {
        var getter = ModuleCodeGen.UnionMembers(unionType, "Value").OfType<IPropertySymbol>().FirstOrDefault(p => p.GetMethod is not null)?.GetMethod
            ?? throw CompileError.Unsupported($"union type '{unionType}' without a Value property", unionType);
        return DirectCall(S(getter), [UnionThis(union, unionType)]);
    }

    /// <summary>`bool TryGetValue(out T)` for exactly this case type (or a nullable value type's underlying type).</summary>
    private IMethodSymbol? UnionTryGetValue(INamedTypeSymbol unionType, ITypeSymbol caseType)
    {
        var wanted = TypeMapper.IsNullable(caseType) ? TypeMapper.NullableUnderlying(caseType) : caseType;
        return ModuleCodeGen.UnionMembers(unionType, "TryGetValue").OfType<IMethodSymbol>()
            .Where(m => m.ReturnType.SpecialType == SpecialType.System_Boolean && m.Parameters.Length == 1 && m.Parameters[0].RefKind == RefKind.Out)
            .Select(S)
            .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, wanted));
    }

    /// <summary>The `this` argument for a union member: a struct union's value in a temporary, a class union's reference.</summary>
    private LLVMValueRef UnionThis(LLVMValueRef union, INamedTypeSymbol unionType)
    {
        if (!unionType.IsValueType) return union;
        var temp = NewTemp(T.Value(unionType), "union.this");
        _b.Store(union, temp);
        return temp;
    }

    /// <summary>A `bool` call result as an i1 (functions return C# bools in their storage width).</summary>
    private LLVMValueRef AsBool(LLVMValueRef value) =>
        Native.LLVMGetIntTypeWidth(Native.LLVMTypeOf(value)) == 1 ? value : _b.ICmp(LLVMIntPredicate.NE, value, LlvmContext.Null(Native.LLVMTypeOf(value)));
}
