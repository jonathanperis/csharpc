using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Bodies of the functions behind member reflection (ModuleCodeGen.Reflection.cs): the invoker of a
/// constructor or method, the factory of a custom attribute and of its CustomAttributeData, and a
/// parameter's default value. Each builds synthetic operations, so calls reach the same lowering
/// (intrinsics, corelib shadows, virtual dispatch) as compiled code.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>`typeof(T)` arguments of a source call site for the [DynamicallyAccessedMembers] dataflow.</summary>
    private void NoteDynamicallyAccessedArguments(IMethodSymbol target, ImmutableArray<IArgumentOperation> arguments)
    {
        if (!arguments.Any(a => a.Value is ITypeOfOperation)) return;
        var byOrdinal = new ITypeSymbol?[target.Parameters.Length];
        foreach (var a in arguments)
            if (a.Value is ITypeOfOperation t && a.Parameter is { } p && p.Ordinal < byOrdinal.Length) byOrdinal[p.Ordinal] = S(t.TypeOperand);
        _m.NoteDynamicallyAccessedArguments(target, i => byOrdinal[i]);
    }

    /// <summary>Field <paramref name="index"/> (1 target, 2 function) of a delegate, or of the last delegate of its invocation list.</summary>
    private LLVMValueRef LastDelegateField(LLVMValueRef d, uint index)
    {
        NullCheck(d);
        var list = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 3));
        var last = NewTemp(L.Ptr, "delegate.last");
        _b.Store(d, last);
        var multi = NewBlock("delegate.multicast");
        var done = NewBlock("delegate.last.done");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, list, LlvmContext.Null(L.Ptr)), done, multi);
        _b.PositionAtEnd(multi);
        var length = _b.Load(L.I64, _b.StructGep(T.ArrayHeader(L.Ptr), list, 1));
        var element = _b.InBoundsGep(T.ArrayHeader(L.Ptr), list, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), _b.BinOp(LLVMOpcode.Sub, length, L.ConstInt(L.I64, 1))]);
        _b.Store(_b.Load(L.Ptr, element), last);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(L.Ptr, _b.StructGep(DelegateType, _b.Load(L.Ptr, last), index));
    }

    private ITypeSymbol ObjectType => _m.Compilation.GetSpecialType(SpecialType.System_Object);

    private LLVMValueRef ObjectElement(LLVMValueRef array, int index) =>
        _b.Load(L.Ptr, _b.InBoundsGep(T.ArrayHeader(L.Ptr), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, index)]));

    private void StoreObjectElement(LLVMValueRef array, int index, LLVMValueRef value) =>
        _b.Store(value, _b.InBoundsGep(T.ArrayHeader(L.Ptr), array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, index)]));

    /// <summary>An argument as reflection passes it (object) converted to <paramref name="type"/>: null is the default value.</summary>
    private LLVMValueRef FromReflectionArgument(LLVMValueRef boxed, ITypeSymbol type)
    {
        var st = S(type);
        if (st.IsReferenceType) return boxed;
        if (TypeMapper.IsNullable(st))
        {
            var underlying = ((INamedTypeSymbol)st).TypeArguments[0];
            var result = NewTemp(T.Value(st), "reflect.nullable");
            _b.Store(LlvmContext.Null(T.Value(st)), result);
            var set = NewBlock("reflect.nullable.set");
            var done = NewBlock("reflect.nullable.done");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, boxed, LlvmContext.Null(L.Ptr)), done, set);
            _b.PositionAtEnd(set);
            _b.Store(Coerce(LoadValue(_b.InBoundsGep(L.I8, boxed, [L.ConstInt(L.I64, 8)]), underlying), underlying, st), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(T.Value(st), result);
        }
        var value = NewTemp(T.Value(st), "reflect.value");
        _b.Store(LlvmContext.Null(T.Value(st)), value);
        var load = NewBlock("reflect.unbox");
        var end = NewBlock("reflect.unbox.done");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, boxed, LlvmContext.Null(L.Ptr)), end, load);
        _b.PositionAtEnd(load);
        _b.Store(LoadValue(_b.InBoundsGep(L.I8, boxed, [L.ConstInt(L.I64, 8)]), st), value);
        _b.Br(end);
        _b.PositionAtEnd(end);
        return _b.Load(T.Value(st), value);
    }

    private LLVMValueRef ToReflectionResult(LLVMValueRef value, ITypeSymbol type, IOperation at) =>
        S(type).IsReferenceType ? value : Box(value, S(type), at);

    /// <summary>object Invoke(object target, object[] args).</summary>
    internal void EmitReflectionInvoker(IMethodSymbol method, SyntaxNode syntax)
    {
        var target = ParamOf(0);
        var args = ParamOf(1);
        var at = new IlValueOperation(syntax, ObjectType, () => target);
        var arguments = ImmutableArray.CreateBuilder<IArgumentOperation>(method.Parameters.Length);
        foreach (var p in method.Parameters)
        {
            var value = FromReflectionArgument(ObjectElement(args, p.Ordinal), p.Type);
            arguments.Add(new IlArgumentOperation(syntax, p, new IlValueOperation(syntax, p.Type, () => value)));
        }
        LLVMValueRef result;
        if (method.MethodKind == MethodKind.Constructor)
        {
            result = EmitValue(new IlObjectCreationOperation(syntax, method, arguments.MoveToImmutable()));
            result = ToReflectionResult(result, method.ContainingType, at);
        }
        else
        {
            IOperation? instance = null;
            if (!method.IsStatic)
            {
                var owner = method.ContainingType;
                instance = owner.IsValueType
                    ? new IlValueOperation(syntax, owner, () => LoadValue(_b.InBoundsGep(L.I8, target, [L.ConstInt(L.I64, 8)]), S(owner)),
                        address: _b.InBoundsGep(L.I8, target, [L.ConstInt(L.I64, 8)]))
                    : new IlValueOperation(syntax, owner, () => target, nonNull: true);
            }
            var isVirtual = !method.IsStatic && (method.IsVirtual || method.IsAbstract || method.IsOverride || method.ContainingType.TypeKind == TypeKind.Interface);
            result = EmitValue(new IlInvocationOperation(syntax, method, instance, isVirtual, arguments.MoveToImmutable(), null));
            result = method.ReturnsVoid ? LlvmContext.Null(L.Ptr) : ToReflectionResult(result, method.ReturnType, at);
        }
        _b.Ret(result);
        Finish();
    }

    /// <summary>A factory that throws NotSupportedException (ModuleCodeGen.AttributeFactory).</summary>
    internal void EmitThrowingFactory(string message)
    {
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!, message);
        Finish();
    }

    /// <summary>A parameter's default value, boxed as ParameterInfo.DefaultValue reports it.</summary>
    internal void EmitDefaultValueFactory(IParameterSymbol p, SyntaxNode syntax)
    {
        var at = new IlValueOperation(syntax, ObjectType, () => default);
        var value = p.ExplicitDefaultValue;
        var type = S(p.Type);
        if (TypeMapper.IsNullable(type)) type = ((INamedTypeSymbol)type).TypeArguments[0]; // a raw value, as .NET reports it
        LLVMValueRef result = value switch
        {
            null => LlvmContext.Null(L.Ptr),
            string s => _m.StringLiteral(s),
            _ when type.IsReferenceType => Box(Constant(value, ConstantTypeOf(value), at), ConstantTypeOf(value), at),
            _ => Box(Coerce(Constant(value, ConstantTypeOf(value), at), ConstantTypeOf(value), type), type, at),
        };
        _b.Ret(result);
        Finish();
    }

    /// <summary>The primitive a constant value is (int for 5, double for 1.5).</summary>
    private ITypeSymbol ConstantTypeOf(object value) => _m.Compilation.GetSpecialType(value switch
    {
        bool => SpecialType.System_Boolean, char => SpecialType.System_Char, sbyte => SpecialType.System_SByte,
        byte => SpecialType.System_Byte, short => SpecialType.System_Int16, ushort => SpecialType.System_UInt16,
        int => SpecialType.System_Int32, uint => SpecialType.System_UInt32, long => SpecialType.System_Int64,
        ulong => SpecialType.System_UInt64, float => SpecialType.System_Single, double => SpecialType.System_Double,
        decimal => SpecialType.System_Decimal, string => SpecialType.System_String,
        _ => throw CompileError.Unsupported($"constant of type {value.GetType().Name}", (ISymbol?)null!),
    });

    /// <summary>`new A(args) { Named = value }` from metadata, as GetCustomAttributes creates it.</summary>
    internal void EmitAttributeFactory(ModuleCodeGen.AttributeInstance instance, SyntaxNode syntax)
    {
        var at = new IlValueOperation(syntax, ObjectType, () => default);
        var ctor = instance.Constructor;
        var arguments = ctor.Parameters.Select((p, i) => (IArgumentOperation)new IlArgumentOperation(syntax, p,
            AttributeArgument(instance.Fixed[i], p.Type, syntax, at))).ToImmutableArray();
        var created = EmitValue(new IlObjectCreationOperation(syntax, ctor, arguments));
        foreach (var (name, isField, value) in instance.Named)
        {
            var self = new IlValueOperation(syntax, ctor.ContainingType, () => created, nonNull: true);
            ISymbol? member = null;
            for (INamedTypeSymbol? t = ctor.ContainingType; t is not null && member is null; t = t.BaseType)
                member = t.GetMembers(name).FirstOrDefault(m => isField ? m is IFieldSymbol : m is IPropertySymbol);
            switch (member)
            {
                case IPropertySymbol property:
                    EmitPropertySet(new IlPropertyReferenceOperation(syntax, property, self, [], null),
                        () => EmitValue(AttributeArgument(value, property.Type, syntax, at)), property.Type);
                    break;
                case IFieldSymbol field:
                    StoreValue(ToStorage(EmitValue(AttributeArgument(value, field.Type, syntax, at)), S(field.Type)), FieldAddressOn(created, S(field)));
                    break;
                default:
                    throw CompileError.Unsupported($"named attribute argument '{name}' of '{ctor.ContainingType}'", ctor);
            }
        }
        _b.Ret(created);
        Finish();
    }

    /// <summary>An attribute argument converted to the parameter/member type it is assigned to.</summary>
    private IOperation AttributeArgument(ModuleCodeGen.AttributeValue value, ITypeSymbol declared, SyntaxNode syntax, IOperation at) =>
        new IlValueOperation(syntax, declared, () => AttributeArgumentValue(value, S(declared), at));

    private LLVMValueRef AttributeArgumentValue(ModuleCodeGen.AttributeValue value, ITypeSymbol declared, IOperation at)
    {
        switch (value.Value)
        {
            case null:
                return LlvmContext.Null(T.Value(declared));
            case string s:
                return _m.StringLiteral(s);
            case ITypeSymbol type:
                return TypeObject(S(type), at);
            case ImmutableArray<ModuleCodeGen.AttributeValue> elements:
            {
                var arrayType = (IArrayTypeSymbol)(declared is IArrayTypeSymbol ? declared : value.Type);
                var array = NewArray(arrayType.ElementType, L.ConstInt(L.I64, elements.Length));
                var storage = T.Storage(S(arrayType.ElementType));
                for (var i = 0; i < elements.Length; i++)
                {
                    var element = AttributeArgumentValue(elements[i], S(arrayType.ElementType), at);
                    _b.Store(ToStorage(element, S(arrayType.ElementType)), _b.InBoundsGep(T.ArrayHeader(storage), array,
                        [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I64, i)]));
                }
                return array;
            }
            default:
            {
                // A primitive or an enum (its underlying value): boxed when the target is object.
                var source = value.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } e ? e : ConstantTypeOf(value.Value);
                var raw = Constant(value.Value, source is INamedTypeSymbol { EnumUnderlyingType: { } u } ? u : source, at);
                if (declared.IsValueType) return Coerce(raw, source is INamedTypeSymbol { EnumUnderlyingType: { } u2 } ? u2 : source, declared);
                return Box(raw, source, at);
            }
        }
    }

    /// <summary>The CustomAttributeData of an attribute (corelib RuntimeCustomAttributeDataImpl.Create).</summary>
    internal void EmitAttributeDataFactory(ModuleCodeGen.AttributeInstance instance, SyntaxNode syntax)
    {
        var at = new IlValueOperation(syntax, ObjectType, () => default);
        var typeType = _m.Compilation.GetTypeByMetadataName("System.Type")!;
        var ctor = instance.Constructor;
        LLVMValueRef Types(IEnumerable<ITypeSymbol> types)
        {
            var list = types.ToList();
            var array = NewArray(typeType, L.ConstInt(L.I64, list.Count));
            for (var i = 0; i < list.Count; i++) StoreObjectElement(array, i, TypeObject(S(list[i]), at));
            return array;
        }
        LLVMValueRef Objects(IReadOnlyList<ModuleCodeGen.AttributeValue> values)
        {
            var array = NewArray(ObjectType, L.ConstInt(L.I64, values.Count));
            for (var i = 0; i < values.Count; i++) StoreObjectElement(array, i, TypedValue(values[i]));
            return array;
        }
        // Enums and primitives are kept boxed as their underlying primitive; arrays as object[] of elements.
        LLVMValueRef TypedValue(ModuleCodeGen.AttributeValue v) => v.Value switch
        {
            null => LlvmContext.Null(L.Ptr),
            string s => _m.StringLiteral(s),
            ITypeSymbol t => TypeObject(S(t), at),
            ImmutableArray<ModuleCodeGen.AttributeValue> elements => Objects(elements),
            var primitive => Box(Constant(primitive, ConstantTypeOf(primitive), at), ConstantTypeOf(primitive), at),
        };
        var names = NewArray(_m.Compilation.GetSpecialType(SpecialType.System_String), L.ConstInt(L.I64, instance.Named.Length));
        for (var i = 0; i < instance.Named.Length; i++)
            StoreObjectElement(names, i, _m.StringLiteral((instance.Named[i].IsField ? "F:" : "P:") + instance.Named[i].Name));
        var result = CallCoreLib("RuntimeCustomAttributeDataImpl", "Create",
            TypeObject(S(ctor.ContainingType), at), Types(ctor.Parameters.Select(p => p.Type)),
            Types(instance.Fixed.Select(f => f.Type)), Objects(instance.Fixed),
            names, Types(instance.Named.Select(n => n.Value.Type)), Objects(instance.Named.Select(n => n.Value).ToList()));
        _b.Ret(result);
        Finish();
    }
}
