using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Assembly.GetCustomAttribute&lt;T&gt;() as compile-time metadata (closed world, ADR 0006): the attributes of
/// every IL assembly the program has are known when it is compiled, so the call becomes a switch on the
/// assembly's identity whose cases construct the matching attribute from its metadata, as .NET's
/// CustomAttribute does at run time (a new object per call). Assemblies whose attributes csharpc doesn't
/// have — the program's own (no SDK-generated AssemblyInfo), the shared framework — throw
/// NotSupportedException instead of answering null.
/// </summary>
public sealed partial class FunctionCodeGen
{
    internal LLVMValueRef AssemblyCustomAttribute(ITypeSymbol attributeType, LLVMValueRef assembly, IOperation at)
    {
        ThrowLibraryExceptionIf(_b.ICmp(LLVMIntPredicate.EQ, assembly, LlvmContext.Null(L.Ptr)),
            "System.ArgumentNullException", "Value cannot be null. (Parameter 'element')");
        var name = CallCoreLib("AssemblyImpl", "NameOf", assembly);
        var result = NewTemp(L.Ptr, "attribute");
        _b.Store(LlvmContext.Null(L.Ptr), result);
        var done = NewBlock("attribute.done");
        foreach (var il in _m.Il.Assemblies)
        {
            var symbol = il.Symbol;
            var matches = symbol.GetAttributes().Where(a => a.AttributeClass is { } c && DerivesFromOrEquals(c, attributeType)).ToList();
            var hit = NewBlock("attribute.assembly");
            var next = NewBlock("attribute.next");
            var same = CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], name, _m.StringLiteral(symbol.Identity.GetDisplayName()));
            _b.CondBr(same, hit, next);
            _b.PositionAtEnd(hit);
            if (matches.Count > 1)
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.Reflection.AmbiguousMatchException")!,
                    $"Multiple custom attributes of the same type '{attributeType.ToDisplayString()}' found.");
            else
            {
                if (matches.Count == 1) _b.Store(ConstructAttribute(matches[0], at), result);
                _b.Br(done);
            }
            _b.PositionAtEnd(next);
        }
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!,
            "The custom attributes of this assembly are not available to csharpc yet (only those of assemblies compiled from IL are).");
        _b.PositionAtEnd(done);
        return _b.Load(L.Ptr, result);
    }

    private static bool DerivesFromOrEquals(INamedTypeSymbol type, ITypeSymbol baseType)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
        return false;
    }

    /// <summary>`new A(args) { Named = value }` from an attribute's metadata.</summary>
    private LLVMValueRef ConstructAttribute(AttributeData data, IOperation at)
    {
        var syntax = at.Syntax;
        var constructor = data.AttributeConstructor ?? throw CompileError.Unsupported($"attribute '{data.AttributeClass}' without a resolvable constructor", at);
        var arguments = constructor.Parameters.Select((p, i) => (IArgumentOperation)new IlArgumentOperation(syntax, p,
            AttributeValue(data.ConstructorArguments[i], p.Type, at))).ToImmutableArray();
        var instance = EmitValue(new IlObjectCreationOperation(syntax, constructor, arguments));
        foreach (var (memberName, value) in data.NamedArguments)
        {
            var self = new IlValueOperation(syntax, data.AttributeClass!, () => instance, nonNull: true);
            var member = data.AttributeClass!.GetMembers(memberName).FirstOrDefault();
            switch (member)
            {
                case IPropertySymbol property:
                    EmitPropertySet(new IlPropertyReferenceOperation(syntax, property, self, [], null),
                        () => EmitValue(AttributeValue(value, property.Type, at)), property.Type);
                    break;
                case IFieldSymbol field:
                    StoreValue(ToStorage(EmitValue(AttributeValue(value, field.Type, at)), S(field.Type)), FieldAddressOn(instance, S(field)));
                    break;
                default:
                    throw CompileError.Unsupported($"named attribute argument '{memberName}' of '{data.AttributeClass}'", at);
            }
        }
        return instance;
    }

    private IOperation AttributeValue(TypedConstant constant, ITypeSymbol type, IOperation at)
    {
        var syntax = at.Syntax;
        return constant.Kind switch
        {
            TypedConstantKind.Primitive or TypedConstantKind.Enum when constant.Type is { } valueType =>
                new IlValueOperation(syntax, type, () => Coerce(Constant(constant.Value, valueType, at), valueType, S(type))),
            TypedConstantKind.Type when constant.Value is ITypeSymbol typeOperand =>
                new IlValueOperation(syntax, type, () => TypeObject(S(typeOperand), at)),
            _ when constant.IsNull => new IlValueOperation(syntax, type, () => LlvmContext.Null(T.Value(S(type)))),
            _ => throw CompileError.Unsupported($"attribute argument of kind {constant.Kind}", at),
        };
    }
}
