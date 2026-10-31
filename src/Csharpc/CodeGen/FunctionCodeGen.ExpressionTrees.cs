using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Expression trees: a lambda converted to System.Linq.Expressions.Expression&lt;TDelegate&gt; becomes the
/// calls of System.Linq.Expressions' factory methods that build it, exactly as Roslyn's
/// ExpressionLambdaRewriter emits them for IL (dotnet/roslyn, src/Compilers/CSharp/Portable/Lowering/
/// ClosureConversion/ExpressionLambdaRewriter.cs), so the trees — and their ToString() — are the ones .NET
/// builds. System.Linq.Expressions itself is compiled from the shared framework's IL (ADR 0013) and runs
/// Compile() through its interpreter, as under NativeAOT. Members appear as MethodInfo/FieldInfo/
/// ConstructorInfo objects over standalone reflection records (ModuleCodeGen.MemberRecords.cs).
///
/// Variables the tree captures stay live: Roslyn hoists them into a display class and the tree reads
/// Expression.Field(Expression.Constant(displayClassInstance), field). csharpc's closure environment is
/// that instance (it gets a method table named as Roslyn names the class, ModuleCodeGen.DisplayClasses.cs).
/// What csharpc can't reproduce exactly is a "not supported by csharpc yet" error, never a different tree.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>The Expression&lt;TDelegate&gt; a lambda converted to <paramref name="target"/> denotes.</summary>
    internal LLVMValueRef EmitExpressionTree(IMethodSymbol lambda, SyntaxNode syntax, ITypeSymbol target, IOperation at)
    {
        var model = _m.ModelFor(syntax.SyntaxTree);
        if (model.GetOperation(syntax) is not IAnonymousFunctionOperation tree)
            throw CompileError.Unsupported("an expression tree whose lambda has no operation tree", at);
        // Expression<D>, or (C# 10) Expression / LambdaExpression with the lambda's natural delegate type.
        var delegateType = S(target) is INamedTypeSymbol { Arity: 1 } generic ? generic.TypeArguments[0] as INamedTypeSymbol
            : model.GetTypeInfo(syntax).Type as INamedTypeSymbol;
        if (delegateType is not { TypeKind: TypeKind.Delegate })
            throw CompileError.Unsupported($"an expression tree of type '{target}'", at);
        return new ExpressionTreeBuilder(this, at, syntax).Lambda(tree, (INamedTypeSymbol)S(delegateType));
    }

    private sealed class ExpressionTreeBuilder
    {
        private readonly FunctionCodeGen _g;
        private readonly IOperation _at;
        private readonly SyntaxNode _syntax, _lambdaSyntax;
        private readonly Compilation _c;
        private readonly INamedTypeSymbol _expression, _parameterExpression, _type, _object;
        private readonly Dictionary<IParameterSymbol, LLVMValueRef> _parameters = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<string, IMethodSymbol[]> _factories = [];

        public ExpressionTreeBuilder(FunctionCodeGen g, IOperation at, SyntaxNode lambdaSyntax)
        {
            _g = g;
            _at = at;
            _syntax = at.Syntax;
            _lambdaSyntax = lambdaSyntax;
            _c = g._m.Compilation;
            _expression = Named("System.Linq.Expressions.Expression");
            // The factories must run: System.Linq.Expressions compiled from IL (ADR 0013), not a reference assembly.
            if (!g._m.Il.Contains(_expression.ContainingAssembly))
                throw CompileError.Unsupported("expression trees without System.Linq.Expressions compiled from IL (pass --framework-il System.Linq.Expressions)", at);
            _parameterExpression = Named("System.Linq.Expressions.ParameterExpression");
            _type = Named("System.Type");
            _object = _c.GetSpecialType(SpecialType.System_Object);
        }

        private INamedTypeSymbol Named(string name) =>
            _c.GetTypeByMetadataName(name) ?? throw CompileError.Unsupported($"expression trees without '{name}'", _at);

        private CompileError Unsupported(string what, IOperation? op = null) =>
            CompileError.Unsupported($"{what} in an expression tree", op ?? _at);

        private ITypeSymbol S(ITypeSymbol type) => _g.S(type);
        private IMethodSymbol S(IMethodSymbol method) => _g.Substitution.Apply(method);

        // ---- factory calls ------------------------------------------------------------------------

        /// <summary>Expression.name(args) with the overload whose parameter types are <paramref name="types"/>.</summary>
        private LLVMValueRef Factory(string name, ITypeSymbol[] types, LLVMValueRef[] args, ITypeSymbol? typeArgument = null)
        {
            if (!_factories.TryGetValue(name, out var overloads))
                _factories[name] = overloads = [.. _expression.GetMembers(name).OfType<IMethodSymbol>().Where(m => m.IsStatic)];
            IMethodSymbol? method = null;
            foreach (var candidate in overloads)
            {
                var m = typeArgument is null ? candidate : candidate.IsGenericMethod && candidate.TypeParameters.Length == 1 ? candidate.Construct(typeArgument) : null;
                if (m is null || m.IsGenericMethod != (typeArgument is not null) || m.Parameters.Length != types.Length) continue;
                if (m.Parameters.Select(p => p.Type).SequenceEqual(types, SymbolEqualityComparer.Default)) { method = m; break; }
            }
            if (method is null)
                throw Unsupported($"Expression.{name}({string.Join(", ", types.Select(t => t.ToDisplayString()))})");
            var arguments = method.Parameters.Select((p, i) => (IArgumentOperation)new IlArgumentOperation(_syntax, p,
                new IlValueOperation(_syntax, p.Type, () => args[i]))).ToImmutableArray();
            return _g.EmitValue(new IlInvocationOperation(_syntax, method, null, false, arguments, null));
        }

        private ITypeSymbol Expr => _expression;
        private ITypeSymbol ExprArray => _c.CreateArrayTypeSymbol(_expression);
        private ITypeSymbol MethodInfoType => Named("System.Reflection.MethodInfo");
        private ITypeSymbol MemberInfoType => Named("System.Reflection.MemberInfo");
        private ITypeSymbol Bool => _c.GetSpecialType(SpecialType.System_Boolean);

        private LLVMValueRef Call(string name, params LLVMValueRef[] expressions) =>
            Factory(name, [.. expressions.Select(_ => Expr)], expressions);

        /// <summary>A new T[] of the given elements (Roslyn's ArrayOrEmpty).</summary>
        private LLVMValueRef Array(ITypeSymbol elementType, IReadOnlyList<LLVMValueRef> elements)
        {
            var array = _g.NewArray(elementType, _g.L.ConstInt(_g.L.I64, elements.Count));
            for (var i = 0; i < elements.Count; i++) _g.StoreObjectElement(array, i, elements[i]);
            return array;
        }

        private LLVMValueRef TypeOf(ITypeSymbol type)
        {
            var substituted = S(type);
            if (substituted is ITypeParameterSymbol or IErrorTypeSymbol || TypeSubstitution.IsOpen(substituted)) throw Unsupported($"the type '{type}'");
            return _g.TypeObject(substituted, _at);
        }

        private LLVMValueRef RecordAddress(LLVMValueRef global) => _g._b.Cast(LLVMOpcode.PtrToInt, global, _g.T.NativeInt);

        private LLVMValueRef MethodInfo(IMethodSymbol method) =>
            _g.CallCoreLib("MemberRecords", "Method", RecordAddress(_g._m.MethodRecordGlobal(S(method))));

        private LLVMValueRef ConstructorInfo(IMethodSymbol constructor) =>
            _g.CallCoreLib("MemberRecords", "Constructor", RecordAddress(_g._m.MethodRecordGlobal(S(constructor))));

        private LLVMValueRef FieldInfo(IFieldSymbol field) =>
            _g.CallCoreLib("MemberRecords", "Field", RecordAddress(_g._m.FieldRecordGlobal(_g.Substitution.Apply(field))));

        private LLVMValueRef Null => LlvmContext.Null(_g.L.Ptr);

        /// <summary>Expression.Constant((object)value, typeof(T)).</summary>
        private LLVMValueRef ConstantNode(LLVMValueRef boxed, ITypeSymbol type) =>
            Factory("Constant", [_object, _type], [boxed, TypeOf(type)]);

        private LLVMValueRef ConstantValue(object? value, ITypeSymbol type)
        {
            type = S(type);
            if (value is null) return ConstantNode(Null, type);
            var underlying = TypeMapper.IsNullable(type) ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
            LLVMValueRef boxed = value is string s ? _g._m.StringLiteral(s)
                : _g.Box(_g.Coerce(_g.Constant(value, _g.ConstantTypeOf(value), _at), _g.ConstantTypeOf(value), underlying), underlying, _at);
            return ConstantNode(boxed, type);
        }

        // ---- lambdas ------------------------------------------------------------------------------

        public LLVMValueRef Lambda(IAnonymousFunctionOperation lambda, INamedTypeSymbol delegateType)
        {
            var parameters = new List<LLVMValueRef>();
            foreach (var p in lambda.Symbol.Parameters)
            {
                var parameter = Factory("Parameter", [_type, _c.GetSpecialType(SpecialType.System_String)], [TypeOf(p.Type), _g._m.StringLiteral(p.Name)]);
                parameters.Add(parameter);
                _parameters[p] = parameter;
            }
            var body = Body(lambda.Body, delegateType.DelegateInvokeMethod!.ReturnType);
            var result = Factory("Lambda", [Expr, _c.CreateArrayTypeSymbol(_parameterExpression)], [body, Array(_parameterExpression, parameters)], delegateType);
            foreach (var p in lambda.Symbol.Parameters) _parameters.Remove(p);
            return result;
        }

        private LLVMValueRef Body(IBlockOperation block, ITypeSymbol returnType)
        {
            foreach (var statement in block.Operations)
                switch (statement)
                {
                    // `() => null`: the literal is the delegate's return type (Roslyn converts it before folding).
                    case IReturnOperation { ReturnedValue: { Type: null, ConstantValue: { HasValue: true, Value: null } } }:
                        return ConstantValue(null, returnType);
                    case IReturnOperation { ReturnedValue: { } value }: return Visit(value);
                    case IExpressionStatementOperation e: return Visit(e.Operation);
                    default: throw Unsupported($"a lambda body with a {statement.Kind} statement", statement);
                }
            throw Unsupported("an empty lambda body");
        }

        /// <summary>A lambda nested in the tree: Expression.Lambda, quoted when it is itself converted to an expression tree.</summary>
        private LLVMValueRef NestedLambda(IAnonymousFunctionOperation lambda, ITypeSymbol convertedTo)
        {
            var target = S(convertedTo);
            var isTree = ClosureAnalysis.IsExpressionType(target);
            var delegateType = isTree ? (target as INamedTypeSymbol)?.TypeArguments.FirstOrDefault() as INamedTypeSymbol : target as INamedTypeSymbol;
            if (delegateType is not { TypeKind: TypeKind.Delegate }) throw Unsupported($"a lambda converted to '{convertedTo}'", lambda);
            var result = Lambda(lambda, delegateType);
            return isTree ? Call("Quote", result) : result;
        }

        // ---- expressions --------------------------------------------------------------------------

        private LLVMValueRef Visit(IOperation op, bool exactType = false)
        {
            // Roslyn folds constants before building the tree (LocalRewriter): `() => 1 + 2` is `() => 3`.
            if (op.ConstantValue.HasValue && op.Type is not null)
                return ConstantValue(op.ConstantValue.Value, op.Type);
            switch (op)
            {
                case IConversionOperation conversion: return VisitConversion(conversion, exactType);
                case IParameterReferenceOperation p when _parameters.TryGetValue(p.Parameter, out var parameter): return parameter;
                case IParameterReferenceOperation p: return Captured(p.Parameter, p);
                case ILocalReferenceOperation l: return Captured(l.Local, l);
                case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } self: return This(self);
                case IFieldReferenceOperation f:
                    return Factory("Field", [Expr, Named("System.Reflection.FieldInfo")],
                        [f.Field.IsStatic ? Null : Visit(f.Instance!), FieldInfo(f.Field)]);
                case IPropertyReferenceOperation p: return VisitProperty(p);
                case IInvocationOperation call: return VisitInvocation(call);
                case IBinaryOperation binary: return VisitBinary(binary);
                case IUnaryOperation unary: return VisitUnary(unary);
                case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                {
                    var condition = Visit(conditional.Condition);
                    var whenTrue = Visit(conditional.WhenTrue, exactType: true);
                    return Call("Condition", condition, whenTrue, Visit(whenFalse, exactType: true));
                }
                case ICoalesceOperation coalesce:
                {
                    if (coalesce.ValueConversion.IsUserDefined) throw Unsupported("a ?? with a user-defined conversion", coalesce);
                    var value = Visit(coalesce.Value);
                    return Call("Coalesce", value, Visit(coalesce.WhenNull));
                }
                case IIsTypeOperation { IsNegated: false } isType:
                    return Factory("TypeIs", [Expr, _type], [Visit(isType.ValueOperand), TypeOf(isType.TypeOperand)]);
                case IObjectCreationOperation creation: return VisitObjectCreation(creation);
                case ITypeParameterObjectCreationOperation newT:
                    return ObjectCreationContinued(NewOfType(newT.Type!), newT.Initializer);
                case IArrayCreationOperation array: return VisitArrayCreation(array);
                case IArrayElementReferenceOperation element: return VisitArrayElement(element);
                case IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } creation:
                    return NestedLambda(lambda, creation.Type!);
                case IAnonymousFunctionOperation lambda: return NestedLambda(lambda, op.Type ?? throw Unsupported("a lambda without a type", op));
                case IDefaultValueOperation { Type: { } defaultType }:
                    return ConstantNode(defaultType.IsReferenceType ? Null : _g.Box(LlvmContext.Null(_g.T.Value(S(defaultType))), S(defaultType), _at), defaultType);
                case ITypeOfOperation typeOf: return ConstantNode(TypeOf(typeOf.TypeOperand), _type);
                case IAnonymousObjectCreationOperation: throw Unsupported("an anonymous type", op);
                case IInterpolatedStringOperation: throw Unsupported("an interpolated string", op);
                default: throw Unsupported($"a {op.Kind} operation", op);
            }
        }

        private IReadOnlyList<LLVMValueRef> VisitAll(IEnumerable<IOperation> operations) => [.. operations.Select(o => Visit(o))];

        private LLVMValueRef Expressions(IEnumerable<IOperation> operations) => Array(_expression, VisitAll(operations));

        private static IEnumerable<IOperation> ArgumentValues(ImmutableArray<IArgumentOperation> arguments) =>
            arguments.OrderBy(a => a.Parameter?.Ordinal ?? 0).Select(a => a.Value);

        private LLVMValueRef This(IInstanceReferenceOperation self)
        {
            // In the method itself `this` is a constant; inside a lambda Roslyn reads it through the closure.
            if (_g.CurrentClosure != _g.CurrentClosure.Analysis.Root) throw Unsupported("'this' from inside a lambda or local function", self);
            var type = S(self.Type!);
            var value = _g.EmitValue(self);
            return ConstantNode(type.IsValueType ? _g.Box(value, type, _at) : value, type);
        }

        /// <summary>A captured local or parameter: Field(Constant(display class), field), read live from the environment.</summary>
        private LLVMValueRef Captured(ISymbol variable, IOperation at)
        {
            var closure = _g.CurrentClosure;
            var analysis = closure.Analysis;
            if (closure != analysis.Root)
                throw Unsupported($"capturing '{variable.Name}' from inside a lambda or local function", at);
            if (analysis.ScopeOfVar(variable) is not { } scope)
                throw Unsupported($"the variable '{variable.Name}' (not captured by the closure analysis)", at);
            var model = RoslynClosureModel.For(analysis, _g._m);
            if (model.Invalid is { } reason) throw Unsupported($"capturing '{variable.Name}': {reason}", at);
            var target = model.EnvOf(scope) ?? throw Unsupported($"capturing '{variable.Name}' from a scope csharpc can't place", at);
            var chain = model.Enclosing(_lambdaSyntax).ToList();
            var index = chain.IndexOf(target);
            if (index < 0) throw Unsupported($"capturing '{variable.Name}' from a scope that doesn't enclose the tree", at);
            var fieldInfo = Named("System.Reflection.FieldInfo");

            // Roslyn's FramePointer: a frame is read through the parent link of the frame nested right inside
            // it when that frame has one (`value(<>c__DisplayClass0_1).CS$<>8__locals1.x`), else from its local.
            LLVMValueRef Frame(int k, EnvScope csharpcScope)
            {
                if (k > 0 && chain[k - 1].Parent == chain[k])
                {
                    var child = chain[k - 1];
                    var childScope = child.Representative;
                    var inner = Frame(k - 1, childScope);
                    var link = _g._m.EnvParentFieldRecordGlobal(childScope, csharpcScope, "CS$<>8__locals" + child.ParentFieldId,
                        _g.DisplayClassMethodTable(childScope), _g.DisplayClassMethodTable(csharpcScope), _g._subst);
                    return Factory("Field", [Expr, fieldInfo], [inner, _g.CallCoreLib("MemberRecords", "Field", RecordAddress(link))]);
                }
                var mt = _g.DisplayClassMethodTable(csharpcScope);
                var type = _g.CallRuntime("csrt_type_from_mt", _g.L.Ptr, [_g.L.Ptr], mt);
                return ConstantNode2(_g.EnvValue(csharpcScope), type);
            }
            var instance = Frame(index, scope);
            var field = _g.CallCoreLib("MemberRecords", "Field", RecordAddress(_g._m.EnvFieldRecordGlobal(scope, variable, _g.DisplayClassMethodTable(scope), _g._subst)));
            return Factory("Field", [Expr, fieldInfo], [instance, field]);
        }

        private LLVMValueRef ConstantNode2(LLVMValueRef value, LLVMValueRef typeObject) =>
            Factory("Constant", [_object, _type], [value, typeObject]);

        private LLVMValueRef VisitProperty(IPropertyReferenceOperation p)
        {
            var property = p.Property;
            // array.Length is ArrayLength (Roslyn's BoundArrayLength).
            if (property is { Name: "Length", ContainingType.SpecialType: SpecialType.System_Array } && p.Instance?.Type is IArrayTypeSymbol { IsSZArray: true })
                return Call("ArrayLength", Visit(p.Instance));
            var getter = GetterOf(property) ?? throw Unsupported($"the property '{property}' without a getter", p);
            if (!p.Arguments.IsEmpty)
                // An indexer is a call of its getter (lowered before the tree is built).
                return Factory("Call", [Expr, MethodInfoType, ExprArray],
                    [property.IsStatic ? Null : Visit(p.Instance!), MethodInfo(getter), Expressions(ArgumentValues(p.Arguments))]);
            var receiver = property.IsStatic ? Null : Visit(p.Instance!);
            // Roslyn's compatibility cast of a type-parameter receiver that isn't known to be a reference type.
            if (p.Instance?.Type is ITypeParameterSymbol { IsReferenceType: false })
                receiver = Convert(receiver, getter.ContainingType, isChecked: false);
            return Factory("Property", [Expr, MethodInfoType], [receiver, MethodInfo(getter)]);
        }

        private static IMethodSymbol? GetterOf(IPropertySymbol property)
        {
            for (var p = property; p is not null; p = p.OverriddenProperty)
                if (p.GetMethod is { } getter) return getter;
            return null;
        }

        private static IMethodSymbol? SetterOf(IPropertySymbol property)
        {
            for (var p = property; p is not null; p = p.OverriddenProperty)
                if (p.SetMethod is { } setter) return setter;
            return null;
        }

        private LLVMValueRef VisitInvocation(IInvocationOperation call)
        {
            var method = call.TargetMethod;
            if (call.Arguments.Any(a => a.Parameter?.RefKind is RefKind.Ref or RefKind.Out))
                throw Unsupported("a call with ref or out arguments", call);
            if (method.MethodKind == MethodKind.DelegateInvoke)
                return Factory("Invoke", [Expr, ExprArray], [Visit(call.Instance!), Expressions(ArgumentValues(call.Arguments))]);
            var receiver = method.IsStatic ? Null : Visit(call.Instance!);
            return Factory("Call", [Expr, MethodInfoType, ExprArray], [receiver, MethodInfo(method), Expressions(ArgumentValues(call.Arguments))]);
        }

        // ---- conversions --------------------------------------------------------------------------

        private LLVMValueRef Convert(LLVMValueRef operand, ITypeSymbol type, bool isChecked) =>
            Factory(isChecked ? "ConvertChecked" : "Convert", [Expr, _type], [operand, TypeOf(type)]);

        private LLVMValueRef Convert(LLVMValueRef operand, ITypeSymbol? oldType, ITypeSymbol newType, bool isChecked, bool isExplicit) =>
            oldType is not null && SymbolEqualityComparer.Default.Equals(S(oldType), S(newType)) && !isExplicit ? operand : Convert(operand, newType, isChecked);

        private LLVMValueRef VisitConversion(IConversionOperation c, bool exactType)
        {
            var type = c.Type!;
            var operand = c.Operand;
            if (operand is IAnonymousFunctionOperation lambda) return NestedLambda(lambda, type);
            if (c.IsTryCast) return Factory("TypeAs", [Expr, _type], [Visit(operand), TypeOf(type)]);
            // `null` converted to a reference type is folded to a typed null constant first.
            if (operand is { Type: null, ConstantValue: { HasValue: true, Value: null } } && c.IsImplicit && !S(type).IsValueType)
                return ConstantValue(null, type);
            var conversion = c.GetConversion();
            var isChecked = c.IsChecked;
            // Roslyn keeps an implicit conversion as a cast where the tree needs the exact type (?: branches).
            var isExplicit = !c.IsImplicit || exactType;
            if (conversion.IsMethodGroup) throw Unsupported("a method group conversion", c);
            if (conversion.IsUserDefined || conversion.IsIntPtr && c.OperatorMethod is not null)
            {
                var method = c.OperatorMethod ?? throw Unsupported("a user-defined conversion without its method", c);
                var operandType = operand.Type!;
                var stripped = StrippedType(operandType);
                var input = method.Parameters[0].Type;
                var isLifted = !Same(operandType, input) && Same(stripped, input);
                var requireAdditionalCast = !Same(stripped, conversion.IsExplicit ? input : StrippedType(input));
                var resultType = isLifted && method.ReturnType.IsValueType && !TypeMapper.IsNullable(method.ReturnType) && TypeMapper.IsNullable(type)
                    ? _c.GetSpecialType(SpecialType.System_Nullable_T).Construct(method.ReturnType) : method.ReturnType;
                var e1 = requireAdditionalCast ? Convert(Visit(operand), operandType, input, isChecked, false) : Visit(operand);
                var e2 = Factory(isChecked && IsCheckedOperator(method.Name) ? "ConvertChecked" : "Convert", [Expr, _type, MethodInfoType],
                    [e1, TypeOf(resultType), MethodInfo(method)]);
                return Convert(e2, resultType, type, isChecked, false);
            }
            if (conversion.IsIdentity || conversion.IsReference && conversion.IsImplicit)
            {
                var visited = Visit(operand);
                return isExplicit ? Convert(visited, type, false) : visited;
            }
            if (conversion.IsNullable && conversion.IsImplicit)
            {
                if (TypeMapper.IsNullable(S(operand.Type!))) return Convert(Visit(operand), operand.Type, type, isChecked, isExplicit);
                // The native compiler converts in two steps; Roslyn follows suit.
                var intermediate = ((INamedTypeSymbol)S(type)).TypeArguments[0];
                var e1 = Convert(Visit(operand), operand.Type, intermediate, isChecked, false);
                return Convert(e1, intermediate, type, isChecked, false);
            }
            if (conversion.IsNullLiteral || operand.Type is null && operand.ConstantValue is { HasValue: true, Value: null })
                return Convert(ConstantNode(Null, _object), _object, type, false, isExplicit);
            return Convert(Visit(operand), operand.Type, type, isChecked, isExplicit);
        }

        private static bool IsCheckedOperator(string name) => name.StartsWith("op_Checked", StringComparison.Ordinal);

        private bool Same(ITypeSymbol a, ITypeSymbol b) => SymbolEqualityComparer.Default.Equals(S(a), S(b));

        private ITypeSymbol StrippedType(ITypeSymbol type) =>
            S(type) is INamedTypeSymbol n && TypeMapper.IsNullable(n) ? n.TypeArguments[0] : S(type);

        // ---- operators ----------------------------------------------------------------------------

        private static bool IsComparison(BinaryOperatorKind kind) => kind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual;

        private string BinaryFactory(IBinaryOperation b, IMethodSymbol? method)
        {
            var useChecked = b.IsChecked || method is not null && IsCheckedOperator(method.Name);
            return b.OperatorKind switch
            {
                BinaryOperatorKind.Add => useChecked ? "AddChecked" : "Add",
                BinaryOperatorKind.Subtract => useChecked ? "SubtractChecked" : "Subtract",
                BinaryOperatorKind.Multiply => useChecked ? "MultiplyChecked" : "Multiply",
                BinaryOperatorKind.Divide => "Divide",
                BinaryOperatorKind.Remainder => "Modulo",
                BinaryOperatorKind.And => "And",
                BinaryOperatorKind.Or => "Or",
                BinaryOperatorKind.ExclusiveOr => "ExclusiveOr",
                BinaryOperatorKind.ConditionalAnd => "AndAlso",
                BinaryOperatorKind.ConditionalOr => "OrElse",
                BinaryOperatorKind.LeftShift => "LeftShift",
                BinaryOperatorKind.RightShift => "RightShift",
                BinaryOperatorKind.Equals => "Equal",
                BinaryOperatorKind.NotEquals => "NotEqual",
                BinaryOperatorKind.LessThan => "LessThan",
                BinaryOperatorKind.LessThanOrEqual => "LessThanOrEqual",
                BinaryOperatorKind.GreaterThan => "GreaterThan",
                BinaryOperatorKind.GreaterThanOrEqual => "GreaterThanOrEqual",
                _ => throw Unsupported($"the operator {b.OperatorKind}", b),
            };
        }

        private LLVMValueRef VisitBinary(IBinaryOperation b)
        {
            var method = b.OperatorMethod;
            var left = b.LeftOperand;
            var right = b.RightOperand;
            var type = b.Type!;
            // String concatenation: string.Concat(string, string), or (object, object) once an operand is boxed.
            if (method is null && b.OperatorKind == BinaryOperatorKind.Add && type.SpecialType == SpecialType.System_String)
            {
                var strings = left.Type?.SpecialType == SpecialType.System_String && right.Type?.SpecialType == SpecialType.System_String;
                var concatParameter = strings ? type : _object;
                method = _c.GetSpecialType(SpecialType.System_String).GetMembers("Concat").OfType<IMethodSymbol>()
                    .First(m => m.Parameters.Length == 2 && m.Parameters.All(p => SymbolEqualityComparer.Default.Equals(p.Type, concatParameter)));
            }
            else if (method is null && left.Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate } || method is null && right.Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate })
                throw Unsupported("delegate combination", b);
            var factory = BinaryFactory(b, method);
            var isLifted = b.IsLifted;
            var isChecked = b.IsChecked;

            // Enums are handled as their promoted underlying type.
            var leftEnum = left.Type is not null && StrippedType(left.Type).TypeKind == TypeKind.Enum;
            var rightEnum = right.Type is not null && StrippedType(right.Type).TypeKind == TypeKind.Enum;
            if (b.OperatorMethod is null && (leftEnum || rightEnum))
            {
                var enumOperand = leftEnum ? left : right;
                ITypeSymbol promoted = PromotedType(((INamedTypeSymbol)StrippedType(enumOperand.Type!)).EnumUnderlyingType!);
                if (isLifted) promoted = _c.GetSpecialType(SpecialType.System_Nullable_T).Construct(promoted);
                var l = PromoteEnumOperand(left, promoted, isChecked);
                var r = PromoteEnumOperand(right, promoted, isChecked);
                return Demote(MakeBinary(factory, method, type, isLifted, IsComparison(b.OperatorKind), l, r), type, isChecked);
            }
            // A nullable compared with `null`: the literal is default(T?) of the other side's type.
            var loweredLeft = IsUntypedNull(left) && right.Type is not null ? ConstantValue(null, right.Type) : Visit(left);
            var loweredRight = IsUntypedNull(right) && left.Type is not null ? ConstantValue(null, left.Type) : Visit(right);
            return MakeBinary(factory, method, type, isLifted, IsComparison(b.OperatorKind), loweredLeft, loweredRight);
        }

        private static bool IsUntypedNull(IOperation op) => op is { Type: null, ConstantValue: { HasValue: true, Value: null } };

        private LLVMValueRef MakeBinary(string factory, IMethodSymbol? method, ITypeSymbol type, bool isLifted, bool requiresLifted, LLVMValueRef left, LLVMValueRef right)
        {
            if (method is null) return Call(factory, left, right);
            if (requiresLifted)
            {
                var liftToNull = isLifted && !Same(method.ReturnType, type);
                return Factory(factory, [Expr, Expr, Bool, MethodInfoType], [left, right, _g.L.ConstInt(_g.L.I1, liftToNull ? 1 : 0), MethodInfo(method)]);
            }
            return Factory(factory, [Expr, Expr, MethodInfoType], [left, right, MethodInfo(method)]);
        }

        private ITypeSymbol PromotedType(ITypeSymbol underlying) => underlying.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char =>
                _c.GetSpecialType(SpecialType.System_Int32),
            _ => underlying,
        };

        private LLVMValueRef PromoteEnumOperand(IOperation operand, ITypeSymbol promoted, bool isChecked)
        {
            // For compatibility, enum literals become constants of the promoted type directly.
            if (operand.ConstantValue.HasValue)
            {
                var value = operand.ConstantValue.Value;
                var underlying = TypeMapper.IsNullable(promoted) ? ((INamedTypeSymbol)promoted).TypeArguments[0] : promoted;
                return ConstantValue(value is null ? null : NumericValue(value, underlying), promoted);
            }
            // An operand converted to the enum is unconverted first (op -> underlying, not op -> enum -> underlying).
            var demoted = operand is IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false } c && !c.GetConversion().IsNullLiteral
                && c.Type is not null && StrippedType(c.Type).TypeKind == TypeKind.Enum ? c.Operand : operand;
            return Convert(Visit(demoted), operand.Type, promoted, isChecked, false);
        }

        private static object NumericValue(object value, ITypeSymbol type) => type.SpecialType switch
        {
            SpecialType.System_Int32 => System.Convert.ToInt32(value),
            SpecialType.System_UInt32 => System.Convert.ToUInt32(value),
            SpecialType.System_Int64 => System.Convert.ToInt64(value),
            SpecialType.System_UInt64 => System.Convert.ToUInt64(value),
            SpecialType.System_Boolean => System.Convert.ToBoolean(value),
            _ => value,
        };

        private LLVMValueRef Demote(LLVMValueRef node, ITypeSymbol type, bool isChecked)
        {
            if (S(type) is not INamedTypeSymbol e) return node;
            if (StrippedType(e).TypeKind == TypeKind.Enum) return Convert(node, type, isChecked);
            ITypeSymbol promoted = TypeMapper.IsNullable(e)
                ? _c.GetSpecialType(SpecialType.System_Nullable_T).Construct(PromotedType(e.TypeArguments[0])) : PromotedType(e);
            return Same(promoted, type) ? node : Convert(node, type, isChecked);
        }

        private LLVMValueRef VisitUnary(IUnaryOperation u)
        {
            var method = u.OperatorMethod;
            var operand = Visit(u.Operand);
            string factory;
            switch (u.OperatorKind)
            {
                case UnaryOperatorKind.Plus:
                    if (method is null) return operand;
                    factory = "UnaryPlus";
                    break;
                case UnaryOperatorKind.Minus:
                    factory = u.IsChecked || method is not null && IsCheckedOperator(method.Name) ? "NegateChecked" : "Negate";
                    break;
                case UnaryOperatorKind.BitwiseNegation or UnaryOperatorKind.Not:
                    factory = "Not";
                    break;
                default:
                    throw Unsupported($"the operator {u.OperatorKind}", u);
            }
            if (method is null && u.IsLifted && u.Operand.Type is not null && StrippedType(u.Operand.Type).TypeKind == TypeKind.Enum)
            {
                ITypeSymbol promoted = _c.GetSpecialType(SpecialType.System_Nullable_T).Construct(PromotedType(((INamedTypeSymbol)StrippedType(u.Operand.Type)).EnumUnderlyingType!));
                var converted = Convert(operand, u.Operand.Type, promoted, u.IsChecked, false);
                return Demote(Call(factory, converted), u.Type!, u.IsChecked);
            }
            return method is null ? Call(factory, operand) : Factory(factory, [Expr, MethodInfoType], [operand, MethodInfo(method)]);
        }

        // ---- object and array creation --------------------------------------------------------------

        private LLVMValueRef NewOfType(ITypeSymbol type)
        {
            // Expression.New(Type) looks the parameterless constructor up among all constructors: keep them.
            _g._m.MarkDynamicallyAccessed(S(type), ModuleCodeGen.DamConstructors);
            return Factory("New", [_type], [TypeOf(type)]);
        }

        private LLVMValueRef VisitObjectCreation(IObjectCreationOperation creation)
        {
            var type = S(creation.Type!);
            var ctor = creation.Constructor;
            LLVMValueRef created;
            if (ctor is null || creation.Arguments.IsEmpty && !type.IsValueType || ctor.IsImplicitlyDeclared && type.IsValueType && ctor.Parameters.IsEmpty)
                created = NewOfType(type);
            else
            {
                if (type.IsAnonymousType) throw Unsupported("an anonymous type", creation);
                var enumerable = _c.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T).Construct(_expression);
                created = Factory("New", [Named("System.Reflection.ConstructorInfo"), enumerable],
                    [ConstructorInfo(ctor), Expressions(ArgumentValues(creation.Arguments))]);
            }
            return ObjectCreationContinued(created, creation.Initializer);
        }

        private LLVMValueRef ObjectCreationContinued(LLVMValueRef creation, IObjectOrCollectionInitializerOperation? initializer)
        {
            if (initializer is null) return creation;
            var newExpression = Named("System.Linq.Expressions.NewExpression");
            var (kind, init) = VisitInitializer(initializer);
            return kind == InitializerKind.Collection
                ? Factory("ListInit", [newExpression, _c.CreateArrayTypeSymbol(Named("System.Linq.Expressions.ElementInit"))], [creation, init])
                : Factory("MemberInit", [newExpression, _c.CreateArrayTypeSymbol(Named("System.Linq.Expressions.MemberBinding"))], [creation, init]);
        }

        private enum InitializerKind { Expression, Member, Collection }

        private (InitializerKind Kind, LLVMValueRef Value) VisitInitializer(IOperation node)
        {
            var memberBinding = Named("System.Linq.Expressions.MemberBinding");
            var elementInit = Named("System.Linq.Expressions.ElementInit");
            switch (node)
            {
                case IObjectOrCollectionInitializerOperation { Initializers: var initializers } when initializers.All(i => i is ISimpleAssignmentOperation or IMemberInitializerOperation)
                                                                                                && initializers.Length > 0:
                {
                    var bindings = new List<LLVMValueRef>();
                    foreach (var item in initializers)
                    {
                        var (member, value) = item switch
                        {
                            ISimpleAssignmentOperation a => (MemberOf(a.Target), (IOperation)a.Value),
                            IMemberInitializerOperation m => (MemberOf(m.InitializedMember), m.Initializer),
                            _ => throw Unsupported($"an initializer {item.Kind}", item),
                        };
                        var (elementKind, init) = VisitInitializer(value);
                        var isProperty = member is IPropertySymbol;
                        switch (elementKind)
                        {
                            case InitializerKind.Collection:
                                bindings.Add(Factory("ListBind", [isProperty ? MethodInfoType : MemberInfoType, _c.CreateArrayTypeSymbol(elementInit)], [MemberGetter(member, item), init]));
                                break;
                            case InitializerKind.Expression:
                                bindings.Add(Factory("Bind", [isProperty ? MethodInfoType : MemberInfoType, Expr], [MemberSetter(member, item), init]));
                                break;
                            default:
                                bindings.Add(Factory("MemberBind", [isProperty ? MethodInfoType : MemberInfoType, _c.CreateArrayTypeSymbol(memberBinding)], [MemberGetter(member, item), init]));
                                break;
                        }
                    }
                    return (InitializerKind.Member, Array(memberBinding, bindings));
                }
                case IObjectOrCollectionInitializerOperation { Initializers: var initializers } when initializers.All(i => i is IInvocationOperation) && initializers.Length > 0:
                {
                    var inits = new List<LLVMValueRef>();
                    foreach (IInvocationOperation add in initializers)
                        inits.Add(Factory("ElementInit", [MethodInfoType, ExprArray], [MethodInfo(add.TargetMethod), Expressions(ArgumentValues(add.Arguments))]));
                    return (InitializerKind.Collection, Array(elementInit, inits));
                }
                case IObjectOrCollectionInitializerOperation empty:
                    throw Unsupported(empty.Initializers.IsEmpty ? "an empty object initializer" : "a mixed or indexed initializer", empty);
                default:
                    return (InitializerKind.Expression, Visit(node));
            }
        }

        private ISymbol MemberOf(IOperation target) => target switch
        {
            IPropertyReferenceOperation { Arguments.IsEmpty: true } p => p.Property,
            IFieldReferenceOperation f => f.Field,
            _ => throw Unsupported($"an initializer of {target.Kind}", target),
        };

        private LLVMValueRef MemberSetter(ISymbol member, IOperation at) => member switch
        {
            IPropertySymbol p => MethodInfo(SetterOf(p) ?? throw Unsupported($"the property '{p}' without a setter", at)),
            IFieldSymbol f => FieldInfo(f),
            _ => throw Unsupported($"an initializer of '{member}'", at),
        };

        private LLVMValueRef MemberGetter(ISymbol member, IOperation at) => member switch
        {
            IPropertySymbol p => MethodInfo(GetterOf(p) ?? throw Unsupported($"the property '{p}' without a getter", at)),
            IFieldSymbol f => FieldInfo(f),
            _ => throw Unsupported($"an initializer of '{member}'", at),
        };

        private LLVMValueRef VisitArrayCreation(IArrayCreationOperation creation)
        {
            var arrayType = (IArrayTypeSymbol)S(creation.Type!);
            var elementType = TypeOf(arrayType.ElementType);
            // The interpreter makes the array with Array.CreateInstance: a value-type T[] needs its method table.
            if (arrayType.IsSZArray) _g._m.ArrayMethodTable(arrayType.ElementType);
            if (creation.Initializer is { } initializer)
            {
                if (!arrayType.IsSZArray) throw Unsupported("a multidimensional array initializer", creation);
                return Factory("NewArrayInit", [_type, ExprArray], [elementType, Expressions(initializer.ElementValues)]);
            }
            return Factory("NewArrayBounds", [_type, ExprArray], [elementType, Expressions(creation.DimensionSizes)]);
        }

        private LLVMValueRef VisitArrayElement(IArrayElementReferenceOperation element)
        {
            var array = Visit(element.ArrayReference);
            var int32 = _c.GetSpecialType(SpecialType.System_Int32);
            LLVMValueRef Index(IOperation index)
            {
                var visited = Visit(index);
                if (index.Type is null || Same(index.Type, int32)) return visited;
                // Roslyn's ConvertIndex: an explicit numeric conversion is checked.
                var conversion = ((CSharpCompilation)_c).ClassifyConversion(index.Type, int32);
                return conversion.IsIdentity ? visited : Convert(visited, int32, isChecked: conversion.IsExplicit && conversion.IsNumeric);
            }
            // A rectangular array's element is a call of the array type's Get method, which csharpc has no
            // reflection metadata for.
            if (element.Indices.Length != 1) throw Unsupported("a multidimensional array element access", element);
            return Call("ArrayIndex", array, Index(element.Indices[0]));
        }
    }

    /// <summary>
    /// The method table that makes a closure environment the display class Roslyn would have generated
    /// (`Program+&lt;&gt;c__DisplayClass1_0`), for the scopes expression trees capture from.
    /// </summary>
    internal LLVMValueRef DisplayClassMethodTable(EnvScope scope)
    {
        var model = RoslynClosureModel.For(scope.Owner.Analysis, _m);
        var at = (ISymbol?)scope.Owner.Analysis.Root.Symbol ?? scope.Vars[0];
        var variables = string.Join(", ", scope.Vars.Select(v => $"'{v.Name}'"));
        if (model.Invalid is { } reason) throw CompileError.Unsupported($"an expression tree capturing {variables}: {reason}", at);
        var env = model.EnvOf(scope) ?? throw CompileError.Unsupported($"an expression tree capturing {variables} from a scope csharpc can't place", at);
        return _m.DisplayClassMethodTable(scope, env.Name, model.Owner!, T.Target.SizeOf(scope.TypeFor(_subst)));
    }

}
