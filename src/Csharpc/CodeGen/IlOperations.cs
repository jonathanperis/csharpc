// Roslyn marks IOperation [InternalImplementationOnly]: Roslyn may add interface members in a new
// version. These classes are csharpc-internal adapters (docs/adr/0010-il-frontend.md); a Roslyn
// upgrade that changes the interfaces fails the build, it cannot silently change behaviour.
#pragma warning disable RS1009
using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Synthetic operations for the IL front-end. An IL instruction whose semantics the source path already
/// implements over IOperation (calls, object creation, library fields and properties, delegates, array
/// elements) is handed to FunctionCodeGen in the shape Roslyn would have produced for equivalent C#,
/// with <see cref="IlValueOperation"/> leaves holding the already-evaluated stack values. Types are the
/// generic *definition's* (as in Roslyn's CFG); FunctionCodeGen substitutes them per instantiation.
/// </summary>
internal abstract class IlOperation(SyntaxNode syntax, ITypeSymbol? type) : IOperation
{
    public IOperation? Parent => null;
    public abstract OperationKind Kind { get; }
    public SyntaxNode Syntax { get; } = syntax;
    public ITypeSymbol? Type { get; } = type;
    public virtual Optional<object?> ConstantValue => default;
    [Obsolete("Use ChildOperations")] public IEnumerable<IOperation> Children => [];
    public IOperation.OperationList ChildOperations => default;
    public string Language => "IL";
    public bool IsImplicit => true;
    public SemanticModel? SemanticModel => null;
    public void Accept(OperationVisitor visitor) => visitor.DefaultVisit(this);
    public TResult? Accept<TArgument, TResult>(OperationVisitor<TArgument, TResult> visitor, TArgument argument) => visitor.DefaultVisit(this, argument);
}

/// <summary>
/// A value already on the IL evaluation stack, seen as an operand of type <see cref="IOperation.Type"/>.
/// When the stack entry is a managed pointer to that type (`ldloca`, `ldflda`, `this` of a struct), the
/// leaf is an lvalue: <see cref="Address"/> is the location and reading it loads through the pointer.
/// </summary>
internal sealed class IlValueOperation(SyntaxNode syntax, ITypeSymbol type, Func<LLVMValueRef> value, LLVMValueRef address = default, bool nonNull = false)
    : IlOperation(syntax, type)
{
    public override OperationKind Kind => OperationKind.None;
    public LLVMValueRef Address { get; } = address;
    public bool HasAddress => Address.Handle != 0;
    public bool NonNull { get; } = nonNull;
    /// <summary>The C# constant of an operand loaded by `ldc.*`/`ldstr` (FunctionCodeGen.TypedConstant).</summary>
    public Optional<object?> Constant { get; init; }
    public override Optional<object?> ConstantValue => Constant;
    /// <summary>T, when the value is `typeof(T)` (`ldtoken T; call Type.GetTypeFromHandle`): what source sees as an ITypeOfOperation.</summary>
    public ITypeSymbol? TypeOfOperand { get; init; }
    /// <summary>
    /// The value a `box` made this object from: what source sees as the operand of a boxing conversion, for
    /// lowering that looks through one to the static type (Enum.CompareTo, Enum.IsDefined). Reading the
    /// operation itself still yields the box the IL made.
    /// </summary>
    public IlValueOperation? BoxedFrom { get; init; }
    /// <summary>The static type the IL stack entry had (more derived than <see cref="IOperation.Type"/> when passed as object).</summary>
    public ITypeSymbol? EntryType { get; init; }
    /// <summary>The operand is IL's `ldnull` (C#'s null literal, or a defaulted optional argument).</summary>
    public bool IsNullLiteral { get; init; }
    public LLVMValueRef Emit() => value();
}

internal sealed class IlArgumentOperation(SyntaxNode syntax, IParameterSymbol parameter, IOperation value) : IlOperation(syntax, null), IArgumentOperation
{
    public override OperationKind Kind => OperationKind.Argument;
    public ArgumentKind ArgumentKind => ArgumentKind.Explicit;
    public IParameterSymbol? Parameter { get; } = parameter;
    public IOperation Value { get; } = value;
    public CommonConversion InConversion => default;
    public CommonConversion OutConversion => default;
}

internal sealed class IlInvocationOperation(SyntaxNode syntax, IMethodSymbol target, IOperation? instance, bool isVirtual,
    ImmutableArray<IArgumentOperation> arguments, ITypeSymbol? constrainedTo)
    : IlOperation(syntax, target.ReturnsVoid ? null : target.ReturnType), IInvocationOperation
{
    public override OperationKind Kind => OperationKind.Invocation;
    public IMethodSymbol TargetMethod { get; } = target;
    public ITypeSymbol? ConstrainedToType { get; } = constrainedTo;
    public IOperation? Instance { get; } = instance;
    public bool IsVirtual { get; } = isVirtual;
    public ImmutableArray<IArgumentOperation> Arguments { get; } = arguments;
}

internal sealed class IlObjectCreationOperation(SyntaxNode syntax, IMethodSymbol constructor, ImmutableArray<IArgumentOperation> arguments)
    : IlOperation(syntax, constructor.ContainingType), IObjectCreationOperation
{
    public override OperationKind Kind => OperationKind.ObjectCreation;
    public IMethodSymbol? Constructor { get; } = constructor;
    public IObjectOrCollectionInitializerOperation? Initializer => null;
    public ImmutableArray<IArgumentOperation> Arguments { get; } = arguments;
}

internal sealed class IlFieldReferenceOperation(SyntaxNode syntax, IFieldSymbol field, IOperation? instance)
    : IlOperation(syntax, field.Type), IFieldReferenceOperation
{
    public override OperationKind Kind => OperationKind.FieldReference;
    public IFieldSymbol Field { get; } = field;
    public bool IsDeclaration => false;
    public IOperation? Instance { get; } = instance;
    public ISymbol Member => Field;
    public ITypeSymbol? ConstrainedToType => null;
}

internal sealed class IlPropertyReferenceOperation(SyntaxNode syntax, IPropertySymbol property, IOperation? instance,
    ImmutableArray<IArgumentOperation> arguments, ITypeSymbol? constrainedTo)
    : IlOperation(syntax, property.Type), IPropertyReferenceOperation
{
    public override OperationKind Kind => OperationKind.PropertyReference;
    public IPropertySymbol Property { get; } = property;
    public ImmutableArray<IArgumentOperation> Arguments { get; } = arguments;
    public IOperation? Instance { get; } = instance;
    public ISymbol Member => Property;
    public ITypeSymbol? ConstrainedToType { get; } = constrainedTo;
}

internal sealed class IlArrayElementReferenceOperation(SyntaxNode syntax, ITypeSymbol elementType, IOperation array, IOperation index)
    : IlOperation(syntax, elementType), IArrayElementReferenceOperation
{
    public override OperationKind Kind => OperationKind.ArrayElementReference;
    public IOperation ArrayReference { get; } = array;
    public ImmutableArray<IOperation> Indices { get; } = [index];
}

internal sealed class IlMethodReferenceOperation(SyntaxNode syntax, IMethodSymbol method, IOperation? instance, bool isVirtual)
    : IlOperation(syntax, null), IMethodReferenceOperation
{
    public override OperationKind Kind => OperationKind.MethodReference;
    public IMethodSymbol Method { get; } = method;
    public ITypeSymbol? ConstrainedToType => null;
    public bool IsVirtual { get; } = isVirtual;
    public IOperation? Instance { get; } = instance;
    public ISymbol Member => Method;
}

internal sealed class IlDelegateCreationOperation(SyntaxNode syntax, ITypeSymbol delegateType, IOperation target)
    : IlOperation(syntax, delegateType), IDelegateCreationOperation
{
    public override OperationKind Kind => OperationKind.DelegateCreation;
    public IOperation Target { get; } = target;
}
