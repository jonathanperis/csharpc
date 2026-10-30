using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>What a dynamic operation does: the C# runtime binder's binder kinds (Microsoft.CSharp.RuntimeBinder.Binder).</summary>
internal enum DynamicKind { InvokeMember, GetMember, SetMember, GetIndex, SetIndex, Invoke, New, Binary, Unary, Convert, IsTrue, IsFalse, IsEvent }

/// <summary>
/// How an operand reaches the binder: a dynamic value (bound by its run-time type), a statically typed value
/// (CSharpArgumentInfoFlags.UseCompileTimeType), a constant (Constant: the binder sees its value), or the
/// implicit `this` of a simple-name call.
/// </summary>
internal enum DynamicOperandKind { Dynamic, Static, Constant, This }

/// <summary>What a run-time type means for an operand: which types need their own binding, and what the rest do.</summary>
internal enum DynamicRole { Receiver, IndexReceiver, InvokeReceiver, Argument, Operator, Convert }

internal sealed class DynamicOperand
{
    public required DynamicOperandKind Kind { get; init; }
    public required DynamicRole Role { get; init; }
    /// <summary>Static: the static type (substituted, as C# names it); Constant: its type (null: the null literal).</summary>
    public ITypeSymbol? Type { get; init; }
    /// <summary>ref/out arguments, and a struct receiver passed by reference: the site gets the variable's address.</summary>
    public RefKind RefKind { get; init; }
    public string? Name { get; init; }
    public object? Constant { get; init; }
    public bool Passed => Kind is DynamicOperandKind.Dynamic or DynamicOperandKind.Static or DynamicOperandKind.This;
}

/// <summary>One dynamic operation of the program (per generic instantiation of its method).</summary>
internal sealed class DynamicSite
{
    public required DynamicKind Kind { get; init; }
    public string? Name { get; init; }
    public ITypeSymbol[] TypeArguments { get; init; } = [];
    /// <summary>`Console.WriteLine(d)`: the type of a static call.</summary>
    public INamedTypeSymbol? StaticReceiver { get; init; }
    /// <summary>`Foo(d)` inside a type: no receiver expression (the binder's InvokeSimpleName).</summary>
    public bool SimpleName { get; init; }
    /// <summary>The C# operator token (Binary, Unary); `&amp;`/`|` for the logical `&amp;&amp;`/`||`.</summary>
    public string? Operator { get; init; }
    /// <summary>The operator as binder messages print it.</summary>
    public string? MessageOperator { get; init; }
    /// <summary>`x op= y`: binds as the compound assignment of a local (with its implicit narrowing).</summary>
    public bool Compound { get; init; }
    public bool Checked { get; init; }
    public bool Explicit { get; init; }
    public bool ResultDiscarded { get; init; }
    /// <summary>Convert: the target type; New: the created type (as C# names them).</summary>
    public ITypeSymbol? ResultType { get; init; }
    public required List<DynamicOperand> Operands { get; init; }
    public required SemanticModel Model { get; init; }
    public required int Position { get; init; }
    public required INamedTypeSymbol? Context { get; init; }
    public required TypeSubstitution Subst { get; init; }
    public required SyntaxNode Syntax { get; init; }
    public LoweredFunction Function { get; set; }
    public string? Fingerprint { get; set; }
    /// <summary>The functions of its cases, by combination of operand classes.</summary>
    public Dictionary<string, LoweredFunction> Cases { get; } = new(StringComparer.Ordinal);
    /// <summary>The bindings of its cases (a binding depends only on the combination).</summary>
    public Dictionary<string, DynamicLeaf> Leaves { get; } = new(StringComparer.Ordinal);
    /// <summary>Its emission failed (reported once).</summary>
    public bool Failed { get; set; }
    /// <summary>Per operand: run-time type -> its class key (memoized classification).</summary>
    public Dictionary<(int Operand, ITypeSymbol Type), DynamicClassKey> Classified { get; } = new(DynamicClassifiedComparer.Instance);

    public bool ReturnsBool => Kind is DynamicKind.IsTrue or DynamicKind.IsFalse or DynamicKind.IsEvent;

    public LLVMTypeRef ReturnLlvmType(ModuleCodeGen m) =>
        ReturnsBool ? m.Llvm.I1 : ResultType is { } t ? m.Types.Value(m.CoreLib.Map(t)) : m.Llvm.Ptr;

    public IEnumerable<LLVMTypeRef> ParameterLlvmTypes(ModuleCodeGen m)
    {
        foreach (var o in Operands)
        {
            if (!o.Passed) continue;
            yield return o.Kind != DynamicOperandKind.Static || o.RefKind != RefKind.None ? m.Llvm.Ptr : m.Types.Value(m.CoreLib.Map(o.Type!));
        }
    }
}

/// <summary>
/// A dispatch case of a dynamic operand. <see cref="Type"/> is the type bound against (the best accessible type of
/// the run-time types in <see cref="Mts"/>); a default case covers every type not listed elsewhere.
/// </summary>
internal sealed class DynamicClass
{
    public required DynamicClassKey Key { get; init; }
    public List<LLVMValueRef> Mts { get; } = [];
    /// <summary>The run-time types of <see cref="Mts"/>, in the same order.</summary>
    public List<ITypeSymbol> Types { get; } = [];
    public ITypeSymbol? Type => Key.Type;
}

internal enum DynamicClassKind
{
    /// <summary>Bound against <see cref="DynamicClassKey.Type"/>.</summary>
    Typed,
    /// <summary>The null reference.</summary>
    Null,
    /// <summary>Every other type: bound as object (arguments, operators) or a binder error naming the run-time type.</summary>
    Default,
    /// <summary>Convert: a reference conversion to the target (implicit: the value itself; explicit: a cast).</summary>
    Reference,
    /// <summary>Convert: only an explicit reference conversion exists and the conversion is implicit (binder error).</summary>
    ExplicitOnly,
    /// <summary>An IDynamicMetaObjectProvider csharpc implements the protocol of (ExpandoObject, DynamicObject).</summary>
    MetaObject,
    /// <summary>A type csharpc doesn't bind this operand for (a generic method would be instantiated with it): NotSupportedException.</summary>
    Unsupported,
}

internal sealed record DynamicClassKey(DynamicClassKind Kind, ITypeSymbol? Type)
{
    public static readonly DynamicClassKey Null = new(DynamicClassKind.Null, null);
    public static readonly DynamicClassKey Default = new(DynamicClassKind.Default, null);
    public static readonly DynamicClassKey Reference = new(DynamicClassKind.Reference, null);
    public static readonly DynamicClassKey ExplicitOnly = new(DynamicClassKind.ExplicitOnly, null);
    public static readonly DynamicClassKey Unsupported = new(DynamicClassKind.Unsupported, null);

    public bool Equals(DynamicClassKey? other) =>
        other is not null && Kind == other.Kind && SymbolEqualityComparer.Default.Equals(Type, other.Type);

    public override int GetHashCode() => HashCode.Combine(Kind, Type is null ? 0 : SymbolEqualityComparer.Default.GetHashCode(Type));
}

internal sealed class DynamicClassifiedComparer : IEqualityComparer<(int Operand, ITypeSymbol Type)>
{
    public static readonly DynamicClassifiedComparer Instance = new();
    public bool Equals((int Operand, ITypeSymbol Type) x, (int Operand, ITypeSymbol Type) y) =>
        x.Operand == y.Operand && SymbolEqualityComparer.Default.Equals(x.Type, y.Type);
    public int GetHashCode((int Operand, ITypeSymbol Type) obj) => HashCode.Combine(obj.Operand, SymbolEqualityComparer.Default.GetHashCode(obj.Type));
}

/// <summary>The dispatch of a site: per passed dynamic operand, its cases (null for operands that aren't dynamic).</summary>
internal sealed class DynamicPlan
{
    public required List<DynamicClass>?[] Classes { get; init; }
    public required string Fingerprint { get; init; }
}
