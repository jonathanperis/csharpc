using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Delegates of any of the program's delegate types over a `Func&lt;object[], object&gt;`: what
/// System.Linq.Expressions' interpreter returns from LambdaExpression.Compile() (LightLambda.MakeDelegate
/// through DelegateHelpers.CreateObjectArrayDelegate), as NativeAOT's DynamicDelegateAugments does it.
/// Each delegate type gets a thunk with its Invoke signature that boxes the arguments into an object[],
/// calls the handler, copies by-ref arguments back and unboxes the result. Only programs that reach
/// ObjectArrayDelegates.Create (corelib/Reflection/ObjectArrayDelegates.cs) carry the table and thunks.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private LLVMValueRef _objectArrayThunkTable;
    private readonly List<(LLVMValueRef Mt, LLVMValueRef Thunk)> _objectArrayThunks = [];
    private int _objectArrayThunksScanned;

    internal LLVMValueRef ObjectArrayThunkTable()
    {
        if (_objectArrayThunkTable.IsNull)
            _objectArrayThunkTable = Module.AddGlobal("csharpc.objectarraythunks", TypeTableHeader, constant: true, linkage: LLVMLinkage.Private);
        return _objectArrayThunkTable;
    }

    private int _delegateInvokesDescribed;

    /// <summary>System.Linq.Expressions is compiled from IL (its reflection needs are kept, see below).</summary>
    internal bool UsesLinqExpressions => _usesLinqExpressions ??= Il.Assemblies.Any(a => a.Name == "System.Linq.Expressions");
    private bool _dynamicInvokeUsed;

    /// <summary>Delegate.DynamicInvoke is lowered: delegate types keep their Invoke method for reflection.</summary>
    internal void NoteDynamicInvoke() => _dynamicInvokeUsed = true;
    private bool? _usesLinqExpressions;

    /// <summary>
    /// System.Linq.Expressions reads delegate types' Invoke methods through reflection (TypeExtensions.GetInvokeMethod:
    /// Expression.Lambda's validation, the interpreter's calls): programs that compile it from IL keep that
    /// method for each delegate type they have, as NativeAOT keeps it for every constructed delegate type.
    /// </summary>
    private bool DescribeDelegateInvokes()
    {
        if (_reflectionTable.IsNull) return false;
        _usesLinqExpressions ??= Il.Assemblies.Any(a => a.Name == "System.Linq.Expressions");
        if (_usesLinqExpressions != true && !_dynamicInvokeUsed) return false;
        var any = false;
        var creator = _usesLinqExpressions == true ? Compilation.GetTypeByMetadataName("System.Linq.Expressions.ExpressionCreator`1") : null;
        while (_delegateInvokesDescribed < _delegateTypes.Count)
        {
            var type = _delegateTypes[_delegateInvokesDescribed++];
            MarkDynamicallyAccessed(type, DamMethods, "Invoke");
            // Expression.Lambda without a delegate type makes typeof(ExpressionCreator<>).MakeGenericType(type)
            // and binds its CreateExpressionFunc (Expression.CreateLambda): NativeAOT shares that code over
            // reference types; csharpc has the instantiation for each delegate type it has.
            if (creator is not null) MarkDynamicallyAccessed(creator.Construct(type), DamPublicMethods, "CreateExpressionFunc");
            any = true;
        }
        if (_usesLinqExpressions != true) return any;
        // Expression.Equal/Add/Convert/... without a MethodInfo look the operator up on the operand types
        // (TypeExtensions.GetAnyStaticMethodValidated, GetUserDefinedCoercionMethod): keep the operators of
        // the program's types, or none, so that lookup answers instead of failing (`decimal != decimal`,
        // `string == string`, a class compared with null).
        while (_operatorTypesScanned < _typeMts.Count)
        {
            if (_typeMts[_operatorTypesScanned++].Type is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } type
                || type.IsUnboundGenericType || TypeSubstitution.IsOpen(type) || type.SpecialType is not (SpecialType.None or SpecialType.System_String or SpecialType.System_Decimal or SpecialType.System_DateTime))
                continue;
            var real = RealType(type) as INamedTypeSymbol ?? type;
            var found = false;
            foreach (var name in s_operatorNames)
                if (real.GetMembers(name).Any(m => m is IMethodSymbol { IsStatic: true }))
                {
                    MarkDynamicallyAccessed(type, DamMethods, name);
                    found = true;
                }
            // A class without operators needs no entry: its GetMethod("op_...") answers null
            // (ReflectionRuntime.NoOperator). A struct keeps an empty one: conversions are looked up with GetMethods.
            if (!found && type.IsValueType) MarkDynamicallyAccessed(type, DamMethods, s_operatorNames[0]);
            any = true;
        }
        return any;
    }

    private int _operatorTypesScanned;

    private static readonly string[] s_operatorNames =
    [
        "op_Equality", "op_Inequality", "op_LessThan", "op_LessThanOrEqual", "op_GreaterThan", "op_GreaterThanOrEqual",
        "op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_Modulus", "op_BitwiseAnd", "op_BitwiseOr",
        "op_ExclusiveOr", "op_LeftShift", "op_RightShift", "op_UnaryNegation", "op_UnaryPlus", "op_LogicalNot",
        "op_OnesComplement", "op_Implicit", "op_Explicit", "op_True", "op_False", "op_Increment", "op_Decrement",
        "op_CheckedAddition", "op_CheckedSubtraction", "op_CheckedMultiply", "op_CheckedUnaryNegation",
    ];

    /// <summary>Thunks for the delegate types created since the last call (generating one can create more).</summary>
    private bool EmitObjectArrayThunks()
    {
        var any = DescribeDelegateInvokes() | DescribeStrongBoxFields();
        if (_objectArrayThunkTable.IsNull) return any;
        while (_objectArrayThunksScanned < _delegateTypes.Count)
        {
            var type = _delegateTypes[_objectArrayThunksScanned++];
            any = true;
            var invoke = type.DelegateInvokeMethod;
            if (invoke is null || invoke.RefKind != RefKind.None || !Representable(invoke.ReturnType)
                || invoke.Parameters.Any(p => !Representable(p.Type)))
                continue; // ObjectArrayDelegates.Create reports the type as not supported
            List<LLVMTypeRef> parameters = [Llvm.Ptr];
            foreach (var p in invoke.Parameters) parameters.Add(p.RefKind == RefKind.None ? Types.Value(p.Type) : Llvm.Ptr);
            var fnType = Llvm.FunctionType(Types.Value(invoke.ReturnType), [.. parameters]);
            try
            {
                var thunk = SyntheticFunction("csharpc.objectarraythunk." + Mangler.MetadataName(type), fnType, (g, syntax) => g.EmitObjectArrayThunk(type, syntax));
                _objectArrayThunks.Add((DelegateMethodTable(type), thunk));
            }
            catch (CompileError) { }
        }
        return any;

        static bool Representable(ITypeSymbol t) => t is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol) && !t.IsRefLikeType;
    }

    private void FinishObjectArrayThunks()
    {
        if (_objectArrayThunkTable.IsNull) return;
        var entries = _objectArrayThunks.Select(t => Llvm.ConstStruct(t.Mt, t.Thunk)).ToList();
        Native.LLVMSetInitializer(_objectArrayThunkTable, Llvm.ConstStruct(Llvm.ConstInt(Llvm.I64, entries.Count),
            ConstTable("csharpc.objectarraythunks.entries", Llvm.Struct(Llvm.Ptr, Llvm.Ptr), entries)));
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>R thunk(Func&lt;object[], object&gt; handler, args...) for delegate type <paramref name="type"/>.</summary>
    internal void EmitObjectArrayThunk(INamedTypeSymbol type, SyntaxNode syntax)
    {
        var invoke = type.DelegateInvokeMethod!;
        var handler = ParamOf(0);
        var at = new IlValueOperation(syntax, ObjectType, () => handler);
        var objects = (IArrayTypeSymbol)_m.Compilation.CreateArrayTypeSymbol(ObjectType);
        var array = NewArray(ObjectType, L.ConstInt(L.I64, invoke.Parameters.Length));
        foreach (var p in invoke.Parameters)
        {
            var value = ParamOf((uint)p.Ordinal + 1);
            if (p.RefKind != RefKind.None) value = LoadValue(value, S(p.Type));
            StoreObjectElement(array, p.Ordinal, ToReflectionResult(value, p.Type, at));
        }
        var func = _m.Compilation.GetTypeByMetadataName("System.Func`2")!.Construct(objects, ObjectType);
        var call = new IlInvocationOperation(syntax, func.DelegateInvokeMethod!, new IlValueOperation(syntax, func, () => handler, nonNull: true), false,
            [new IlArgumentOperation(syntax, func.DelegateInvokeMethod!.Parameters[0], new IlValueOperation(syntax, objects, () => array, nonNull: true))], null);
        var result = EmitValue(call);
        // ref/out arguments: the interpreter left their final values in the array (LightLambda.Run).
        foreach (var p in invoke.Parameters)
            if (p.RefKind is RefKind.Ref or RefKind.Out)
                StoreValue(ToStorage(FromReflectionArgument(ObjectElement(array, p.Ordinal), p.Type), S(p.Type)), ParamOf((uint)p.Ordinal + 1));
        if (invoke.ReturnsVoid) _b.RetVoid();
        else _b.Ret(ToStorageIfNeeded(FromReflectionArgument(result, invoke.ReturnType), S(invoke.ReturnType)));
        Finish();
    }
}
