using System.Runtime.CompilerServices;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// `dynamic` operations (ModuleCodeGen.Dynamic.cs, docs/adr/0019-dynamic.md): each one evaluates its operands
/// like the C# compiler's call-site code does, then calls its site's dispatch function. Compound forms
/// (`d.P += x`, `d[i]++`) are the binder's sequences of simpler operations (get, operation, set), as csc emits them.
/// </summary>
public sealed partial class FunctionCodeGen
{
    internal static bool IsDynamic(ITypeSymbol? type) => type is { TypeKind: TypeKind.Dynamic };

    /// <summary>A conversion the runtime binder performs (from dynamic to a static type other than object).</summary>
    internal static bool IsDynamicConversion(IConversionOperation c) =>
        IsDynamic(c.Operand.Type) && !IsDynamic(c.Type) && c.Type is { SpecialType: not SpecialType.System_Object };

    internal static bool IsDynamicCompound(ICompoundAssignmentOperation c) => IsDynamic(c.Type) || IsDynamic(c.Value.Type) || IsDynamic(c.Target.Type);

    private sealed class DynamicCall
    {
        public readonly List<DynamicOperand> Operands = [];
        public readonly List<LLVMValueRef> Values = [];
    }

    // ======================================================================================
    // Operands
    // ======================================================================================

    /// <summary>Evaluates an operand as csc passes it to a call site.</summary>
    private void AddDynamicOperand(DynamicCall call, IOperation value, DynamicRole role, string? name = null, RefKind refKind = RefKind.None)
    {
        if (IsDynamic(value.Type))
        {
            if (refKind != RefKind.None)
                throw CompileError.Unsupported("a dynamic value passed by reference to a dynamic call", value);
            call.Operands.Add(new DynamicOperand { Kind = DynamicOperandKind.Dynamic, Role = role, Name = name });
            call.Values.Add(EmitValue(value));
            return;
        }
        if (value.ConstantValue.HasValue && refKind == RefKind.None)
        {
            call.Operands.Add(new DynamicOperand { Kind = DynamicOperandKind.Constant, Role = role, Name = name, Constant = value.ConstantValue.Value,
                Type = value.Type is { } constantType ? _subst.Apply(constantType) : null });
            return;
        }
        var type = value.Type is { } t ? _subst.Apply(t) : throw CompileError.Unsupported("a typeless argument to a dynamic call", value);
        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
            throw CompileError.Unsupported("Dynamic calls cannot be used in conjunction with pointers", value);
        // A struct variable as receiver goes by reference, as csc passes it: the bound call mutates it.
        if (refKind == RefKind.None && role is DynamicRole.Receiver or DynamicRole.IndexReceiver or DynamicRole.InvokeReceiver
            && type.IsValueType && value is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation or IArrayElementReferenceOperation)
            refKind = RefKind.Ref;
        call.Operands.Add(new DynamicOperand { Kind = DynamicOperandKind.Static, Role = role, Name = name, RefKind = refKind, Type = type });
        call.Values.Add(refKind != RefKind.None ? AddressOf(value) : EmitValue(value));
    }

    /// <summary>An operand computed already: a dynamic value (object), or a value of static type <paramref name="staticType"/>.</summary>
    private static void AddDynamicValue(DynamicCall call, LLVMValueRef value, ITypeSymbol? staticType, DynamicRole role)
    {
        call.Operands.Add(staticType is null
            ? new DynamicOperand { Kind = DynamicOperandKind.Dynamic, Role = role }
            : new DynamicOperand { Kind = DynamicOperandKind.Static, Role = role, Type = staticType });
        call.Values.Add(value);
    }

    private (string? Name, RefKind RefKind)[] DynamicArgumentInfo(IOperation op, int count)
    {
        var syntax = op.Syntax switch
        {
            InvocationExpressionSyntax i => i.ArgumentList.Arguments,
            ElementAccessExpressionSyntax e => e.ArgumentList.Arguments,
            BaseObjectCreationExpressionSyntax o => o.ArgumentList?.Arguments ?? default,
            _ => default,
        };
        var result = new (string?, RefKind)[count];
        if (syntax.Count != count) return result;
        for (var i = 0; i < count; i++)
        {
            var a = syntax[i];
            result[i] = (a.NameColon?.Name.Identifier.ValueText, a.RefKindKeyword.Kind() switch
            {
                SyntaxKind.RefKeyword => RefKind.Ref,
                SyntaxKind.OutKeyword => RefKind.Out,
                SyntaxKind.InKeyword => RefKind.In,
                _ => RefKind.None,
            });
        }
        return result;
    }

    private void AddDynamicArguments(DynamicCall call, IOperation op, IReadOnlyList<IOperation> arguments)
    {
        var info = DynamicArgumentInfo(op, arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
            AddDynamicOperand(call, arguments[i], DynamicRole.Argument, info[i].Name, info[i].RefKind);
    }

    // ======================================================================================
    // Sites
    // ======================================================================================

    private LLVMValueRef CallDynamicSite(IOperation at, string part, DynamicCall call, DynamicKind kind, Action<DynamicSiteBuilder>? configure = null)
    {
        var key = $"{RuntimeHelpers.GetHashCode(at.Syntax.SyntaxTree)}:{at.Syntax.SpanStart}:{at.Syntax.Span.Length}:{at.Syntax.RawKind}:{kind}:{part}|{_subst.Key}";
        var site = _m.DynamicSiteFor(key, () =>
        {
            var builder = new DynamicSiteBuilder { Kind = kind, Operands = call.Operands };
            configure?.Invoke(builder);
            return builder.Build(_m, at.Syntax, _method?.ContainingType, _subst);
        });
        return _b.Call(site.Function.Type, site.Function.Fn, [.. call.Values]);
    }

    /// <summary>The parts of a site besides its operands.</summary>
    internal sealed class DynamicSiteBuilder
    {
        public required DynamicKind Kind;
        public required List<DynamicOperand> Operands;
        public string? Name, Operator, MessageOperator;
        public ITypeSymbol[] TypeArguments = [];
        public INamedTypeSymbol? StaticReceiver;
        public bool SimpleName, Compound, Checked, Explicit, ResultDiscarded;
        public ITypeSymbol? ResultType;

        public DynamicSite Build(ModuleCodeGen m, SyntaxNode syntax, INamedTypeSymbol? context, TypeSubstitution subst)
        {
            // The binder's context: the statement the operation is in (its scope and accessibility) — or, for a
            // dynamic operation inside code bound for another site, that site's.
            var (model, position, origin) = m.DynamicContext(syntax, DynamicPosition(syntax), context);
            return new()
            {
                Kind = Kind, Name = Name, TypeArguments = TypeArguments, StaticReceiver = StaticReceiver, SimpleName = SimpleName,
                Operator = Operator, MessageOperator = MessageOperator ?? Operator, Compound = Compound, Checked = Checked, Explicit = Explicit,
                ResultDiscarded = ResultDiscarded, ResultType = ResultType, Operands = Operands,
                Model = model, Position = position, Context = origin, Subst = subst, Syntax = syntax,
            };
        }
    }

    /// <summary>
    /// Where a site binds: the statement it is in. A field or property initializer has none; a body of the same
    /// type has the same accessibility (the binder's only use of the context), so the site binds there.
    /// </summary>
    private static int DynamicPosition(SyntaxNode syntax)
    {
        if (syntax.FirstAncestorOrSelf<StatementSyntax>() is { } statement) return statement.SpanStart;
        if (syntax.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>() is { Parent: BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax } arrow)
            return arrow.Expression.SpanStart;
        if (syntax.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } type)
            foreach (var member in type.Members)
            {
                var body = member switch
                {
                    BaseMethodDeclarationSyntax { Body: { } b } => b,
                    PropertyDeclarationSyntax { AccessorList: { } accessors } => accessors.Accessors.Select(a => a.Body).FirstOrDefault(b => b is not null),
                    _ => null,
                };
                if (body is not null) return body.OpenBraceToken.Span.End;
            }
        return syntax.SpanStart;
    }

    /// <summary>Dispatches a dynamic operation (EmitValue's hook).</summary>
    internal LLVMValueRef EmitDynamicOperation(IOperation op) => op switch
    {
        IDynamicInvocationOperation i => EmitDynamicInvocation(i),
        IDynamicMemberReferenceOperation m => EmitDynamicGetMember(m),
        IDynamicIndexerAccessOperation x => EmitDynamicGetIndex(x),
        IDynamicObjectCreationOperation n => EmitDynamicNew(n),
        ISimpleAssignmentOperation { Target: IDynamicMemberReferenceOperation or IDynamicIndexerAccessOperation } a => EmitDynamicAssignment(a),
        ICompoundAssignmentOperation c => EmitDynamicCompound(c),
        IIncrementOrDecrementOperation i => EmitDynamicIncrement(i),
        IBinaryOperation b => EmitDynamicBinary(b),
        IUnaryOperation u => EmitDynamicUnary(u),
        IConversionOperation c => EmitDynamicConversion(c),
        _ => throw CompileError.Unsupported($"dynamic operation {op.Kind}", op),
    };

    private bool IsDiscarded(IOperation op) => op.Parent is IExpressionStatementOperation;

    /// <summary>The receiver of a member access: a value, the implicit `this` of a simple name, or a type.</summary>
    private void AddDynamicReceiver(DynamicCall call, IDynamicMemberReferenceOperation member, DynamicSiteBuilder site)
    {
        if (member.Instance is null)
        {
            if (member.Syntax is MemberAccessExpressionSyntax && member.ContainingType is { } type)
                site.StaticReceiver = (INamedTypeSymbol)_subst.Apply(type);
            else site.SimpleName = true;
            return;
        }
        if (member.Instance is IInstanceReferenceOperation { IsImplicit: true } self)
        {
            if (InStructMember) throw CompileError.Unsupported("a dynamic call to a member of a struct through its implicit 'this'", member);
            site.SimpleName = true;
            call.Operands.Add(new DynamicOperand { Kind = DynamicOperandKind.This, Role = DynamicRole.Receiver });
            call.Values.Add(ThisValue(self));
            return;
        }
        AddDynamicOperand(call, member.Instance, DynamicRole.Receiver);
    }

    private LLVMValueRef EmitDynamicInvocation(IDynamicInvocationOperation inv)
    {
        var call = new DynamicCall();
        if (inv.Operation is IDynamicMemberReferenceOperation member)
        {
            var site = new DynamicSiteBuilder { Kind = DynamicKind.InvokeMember, Operands = call.Operands };
            AddDynamicReceiver(call, member, site);
            AddDynamicArguments(call, inv, inv.Arguments);
            return CallDynamicSite(inv, "invoke", call, DynamicKind.InvokeMember, s =>
            {
                s.Name = member.MemberName;
                s.TypeArguments = [.. member.TypeArguments.Select(_subst.Apply)];
                s.StaticReceiver = site.StaticReceiver;
                s.SimpleName = site.SimpleName;
                s.ResultDiscarded = IsDiscarded(inv);
            });
        }
        AddDynamicOperand(call, inv.Operation, DynamicRole.InvokeReceiver);
        AddDynamicArguments(call, inv, inv.Arguments);
        return CallDynamicSite(inv, "invoke", call, DynamicKind.Invoke, s => s.ResultDiscarded = IsDiscarded(inv));
    }

    private LLVMValueRef EmitDynamicGetMember(IDynamicMemberReferenceOperation member)
    {
        var call = new DynamicCall();
        var site = new DynamicSiteBuilder { Kind = DynamicKind.GetMember, Operands = call.Operands };
        AddDynamicReceiver(call, member, site);
        return CallDynamicSite(member, "get", call, DynamicKind.GetMember, s =>
        {
            s.Name = member.MemberName;
            s.StaticReceiver = site.StaticReceiver;
            s.SimpleName = site.SimpleName;
        });
    }

    private LLVMValueRef EmitDynamicGetIndex(IDynamicIndexerAccessOperation access)
    {
        var call = new DynamicCall();
        AddDynamicOperand(call, access.Operation, DynamicRole.IndexReceiver);
        AddDynamicArguments(call, access, access.Arguments);
        return CallDynamicSite(access, "get", call, DynamicKind.GetIndex);
    }

    private LLVMValueRef EmitDynamicNew(IDynamicObjectCreationOperation creation)
    {
        if (creation.Initializer is { Initializers.Length: > 0 })
            throw CompileError.Unsupported("an object initializer on a dynamically bound constructor call", creation);
        var call = new DynamicCall();
        AddDynamicArguments(call, creation, creation.Arguments);
        return CallDynamicSite(creation, "new", call, DynamicKind.New, s => s.ResultType = _subst.Apply(creation.Type!));
    }

    private LLVMValueRef EmitDynamicAssignment(ISimpleAssignmentOperation a)
    {
        var call = new DynamicCall();
        if (a.Target is IDynamicMemberReferenceOperation member)
        {
            var site = new DynamicSiteBuilder { Kind = DynamicKind.SetMember, Operands = call.Operands };
            AddDynamicReceiver(call, member, site);
            AddDynamicOperand(call, a.Value, DynamicRole.Argument);
            return CallDynamicSite(a, "set", call, DynamicKind.SetMember, s =>
            {
                s.Name = member.MemberName;
                s.StaticReceiver = site.StaticReceiver;
                s.SimpleName = site.SimpleName;
            });
        }
        var access = (IDynamicIndexerAccessOperation)a.Target;
        AddDynamicOperand(call, access.Operation, DynamicRole.IndexReceiver);
        AddDynamicArguments(call, access, access.Arguments);
        AddDynamicOperand(call, a.Value, DynamicRole.Argument);
        return CallDynamicSite(a, "set", call, DynamicKind.SetIndex);
    }

    // ======================================================================================
    // Operators
    // ======================================================================================

    private static string BinaryToken(BinaryOperatorKind kind, IOperation at) => kind switch
    {
        BinaryOperatorKind.Add => "+", BinaryOperatorKind.Subtract => "-", BinaryOperatorKind.Multiply => "*",
        BinaryOperatorKind.Divide => "/", BinaryOperatorKind.Remainder => "%", BinaryOperatorKind.LeftShift => "<<",
        BinaryOperatorKind.RightShift => ">>", BinaryOperatorKind.UnsignedRightShift => ">>>", BinaryOperatorKind.And => "&",
        BinaryOperatorKind.Or => "|", BinaryOperatorKind.ExclusiveOr => "^", BinaryOperatorKind.Equals => "==",
        BinaryOperatorKind.NotEquals => "!=", BinaryOperatorKind.LessThan => "<", BinaryOperatorKind.LessThanOrEqual => "<=",
        BinaryOperatorKind.GreaterThan => ">", BinaryOperatorKind.GreaterThanOrEqual => ">=",
        BinaryOperatorKind.ConditionalAnd => "&", BinaryOperatorKind.ConditionalOr => "|",
        _ => throw CompileError.Unsupported($"dynamic binary operator {kind}", at),
    };

    private LLVMValueRef EmitDynamicBinary(IBinaryOperation b)
    {
        var call = new DynamicCall();
        AddDynamicOperand(call, b.LeftOperand, DynamicRole.Operator);
        AddDynamicOperand(call, b.RightOperand, DynamicRole.Operator);
        var token = BinaryToken(b.OperatorKind, b);
        // `d && x` reaches here as the binder's logical And (after IsFalse): bound as `&`, reported as `&&`.
        var message = b.Syntax.Kind() switch { SyntaxKind.LogicalAndExpression => "&&", SyntaxKind.LogicalOrExpression => "||", _ => token };
        return DynamicBinaryCall(b, "binary", call, token, message, compound: false, b.IsChecked);
    }

    private LLVMValueRef DynamicBinaryCall(IOperation at, string part, DynamicCall call, string token, string message, bool compound, bool isChecked) =>
        CallDynamicSite(at, part, call, DynamicKind.Binary, s =>
        {
            s.Operator = token;
            s.MessageOperator = compound ? message + "=" : message;
            s.Compound = compound;
            s.Checked = isChecked;
        });

    private LLVMValueRef EmitDynamicUnary(IUnaryOperation u)
    {
        var call = new DynamicCall();
        AddDynamicOperand(call, u.Operand, DynamicRole.Operator);
        if (u.OperatorKind is UnaryOperatorKind.True or UnaryOperatorKind.False)
            return CallDynamicSite(u, "unary", call, u.OperatorKind == UnaryOperatorKind.True ? DynamicKind.IsTrue : DynamicKind.IsFalse);
        var token = u.OperatorKind switch
        {
            UnaryOperatorKind.Plus => "+", UnaryOperatorKind.Minus => "-", UnaryOperatorKind.BitwiseNegation => "~", UnaryOperatorKind.Not => "!",
            _ => throw CompileError.Unsupported($"dynamic unary operator {u.OperatorKind}", u),
        };
        return CallDynamicSite(u, "unary", call, DynamicKind.Unary, s => { s.Operator = token; s.Checked = u.IsChecked; });
    }

    private LLVMValueRef EmitDynamicConversion(IConversionOperation c)
    {
        var target = _subst.Apply(c.Type!);
        var value = EmitValue(c.Operand);
        if (target.SpecialType == SpecialType.System_Object || IsDynamic(target)) return value;
        var call = new DynamicCall();
        AddDynamicValue(call, value, null, DynamicRole.Convert);
        // `foreach (var x in d)`, `using (d)`: csc converts implicitly; a cast in source is explicit.
        var isExplicit = !c.IsImplicit && c.Syntax is CastExpressionSyntax;
        return CallDynamicSite(c, "convert", call, DynamicKind.Convert, s =>
        {
            s.ResultType = target;
            s.Explicit = isExplicit;
            s.Checked = c.IsChecked;
        });
    }

    /// <summary>Converts a dynamic value to <paramref name="target"/> as the binder does (implicitly).</summary>
    private LLVMValueRef DynamicConvertTo(IOperation at, string part, LLVMValueRef value, ITypeSymbol target)
    {
        if (target.SpecialType == SpecialType.System_Object || IsDynamic(target)) return value;
        var call = new DynamicCall();
        AddDynamicValue(call, value, null, DynamicRole.Convert);
        return CallDynamicSite(at, part, call, DynamicKind.Convert, s => s.ResultType = target);
    }

    // ======================================================================================
    // Compound assignment, ++ and --
    // ======================================================================================

    private LLVMValueRef EmitDynamicCompound(ICompoundAssignmentOperation c)
    {
        var token = BinaryToken(c.OperatorKind, c);
        if (c.Target is IDynamicMemberReferenceOperation member)
            return DynamicMemberUpdate(c, member, (old, n) =>
            {
                var call = new DynamicCall();
                AddDynamicValue(call, old, null, DynamicRole.Operator);
                AddDynamicOperand(call, c.Value, DynamicRole.Operator);
                var updated = DynamicBinaryCall(c, "binary" + n, call, token, token, compound: true, c.IsChecked);
                return (updated, updated);
            }, eventOperator: c.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract ? token : null, eventValue: c.Value);
        if (c.Target is IDynamicIndexerAccessOperation access)
            return DynamicIndexUpdate(c, access, old =>
            {
                var call = new DynamicCall();
                AddDynamicValue(call, old, null, DynamicRole.Operator);
                AddDynamicOperand(call, c.Value, DynamicRole.Operator);
                var updated = DynamicBinaryCall(c, "binary", call, token, token, compound: true, c.IsChecked);
                return (updated, updated);
            });
        var targetType = _subst.Apply(c.Target.Type!);
        return ReadModifyWrite(c.Target, old =>
        {
            var call = new DynamicCall();
            AddDynamicValue(call, old, IsDynamic(targetType) ? null : targetType, DynamicRole.Operator);
            AddDynamicOperand(call, c.Value, DynamicRole.Operator);
            var updated = DynamicBinaryCall(c, "binary", call, token, token, compound: true, c.IsChecked);
            var stored = IsDynamic(targetType) ? updated : DynamicConvertTo(c, "convert", updated, targetType);
            return (stored, stored);
        });
    }

    private LLVMValueRef EmitDynamicIncrement(IIncrementOrDecrementOperation op)
    {
        var token = op.Kind == OperationKind.Increment ? "++" : "--";
        (LLVMValueRef, LLVMValueRef) Step(LLVMValueRef old, string part)
        {
            var call = new DynamicCall();
            AddDynamicValue(call, old, null, DynamicRole.Operator);
            var updated = CallDynamicSite(op, part, call, DynamicKind.Unary, s => { s.Operator = token; s.Checked = op.IsChecked; });
            return (updated, op.IsPostfix ? old : updated);
        }
        if (op.Target is IDynamicMemberReferenceOperation member)
            return DynamicMemberUpdate(op, member, (old, n) => Step(old, "unary" + n), eventOperator: null, eventValue: null);
        if (op.Target is IDynamicIndexerAccessOperation access)
            return DynamicIndexUpdate(op, access, old => Step(old, "unary"));
        return ReadModifyWrite(op.Target, old => Step(old, "unary"));
    }

    /// <summary>
    /// `d.M op= v`: get, operate, set on one evaluation of the receiver — after the binder's IsEvent check
    /// for `+=`/`-=`, which turns an event's into `d.E += v` (its add accessor).
    /// </summary>
    private LLVMValueRef DynamicMemberUpdate(IOperation at, IDynamicMemberReferenceOperation member,
        Func<LLVMValueRef, string, (LLVMValueRef Stored, LLVMValueRef Result)> modify, string? eventOperator, IOperation? eventValue)
    {
        var receiver = new DynamicCall();
        var shape = new DynamicSiteBuilder { Kind = DynamicKind.GetMember, Operands = receiver.Operands };
        AddDynamicReceiver(receiver, member, shape);
        DynamicCall Receiver()
        {
            var call = new DynamicCall();
            call.Operands.AddRange(receiver.Operands);
            call.Values.AddRange(receiver.Values);
            return call;
        }
        void Shape(DynamicSiteBuilder s)
        {
            s.Name = member.MemberName;
            s.StaticReceiver = shape.StaticReceiver;
            s.SimpleName = shape.SimpleName;
        }
        LLVMValueRef Update(string n)
        {
            var old = CallDynamicSite(at, "get" + n, Receiver(), DynamicKind.GetMember, Shape);
            var (stored, result) = modify(old, n);
            var set = Receiver();
            AddDynamicValue(set, stored, null, DynamicRole.Argument);
            CallDynamicSite(at, "set" + n, set, DynamicKind.SetMember, Shape);
            return result;
        }
        if (eventOperator is null || receiver.Operands.Count == 0 || receiver.Operands[0].Kind != DynamicOperandKind.Dynamic) return Update("");

        var isEvent = CallDynamicSite(at, "isevent", Receiver(), DynamicKind.IsEvent, Shape);
        var resultSlot = NewTemp(L.Ptr, "dyn.update");
        var eventBlock = NewBlock("dyn.event");
        var memberBlock = NewBlock("dyn.member");
        var done = NewBlock("dyn.update.done");
        _b.CondBr(isEvent, eventBlock, memberBlock);
        _b.PositionAtEnd(eventBlock);
        var add = Receiver();
        AddDynamicOperand(add, eventValue!, DynamicRole.Argument);
        CallDynamicSite(at, "event", add, DynamicKind.SetMember, s =>
        {
            Shape(s);
            s.Compound = true;
            s.Operator = eventOperator;
            s.ResultDiscarded = true;
        });
        _b.Store(LlvmContext.Null(L.Ptr), resultSlot);
        _b.Br(done);
        _b.PositionAtEnd(memberBlock);
        _b.Store(Update(""), resultSlot);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(L.Ptr, resultSlot);
    }

    private LLVMValueRef DynamicIndexUpdate(IOperation at, IDynamicIndexerAccessOperation access, Func<LLVMValueRef, (LLVMValueRef Stored, LLVMValueRef Result)> modify)
    {
        var target = new DynamicCall();
        AddDynamicOperand(target, access.Operation, DynamicRole.IndexReceiver);
        AddDynamicArguments(target, access, access.Arguments);
        var get = new DynamicCall();
        get.Operands.AddRange(target.Operands);
        get.Values.AddRange(target.Values);
        var old = CallDynamicSite(at, "get", get, DynamicKind.GetIndex);
        var (stored, result) = modify(old);
        var set = new DynamicCall();
        set.Operands.AddRange(target.Operands);
        set.Values.AddRange(target.Values);
        AddDynamicValue(set, stored, null, DynamicRole.Argument);
        CallDynamicSite(at, "set", set, DynamicKind.SetIndex);
        return result;
    }

    // ======================================================================================
    // Site bodies
    // ======================================================================================

    /// <summary>The dispatch function of <paramref name="site"/>: a case per run-time type class of each dynamic operand.</summary>
    internal void EmitDynamicSite(DynamicSite site, DynamicPlan plan)
    {
        var count = site.Operands.Count;
        var values = new LLVMValueRef[count];
        uint index = 0;
        for (var i = 0; i < count; i++)
            if (site.Operands[i].Passed) values[i] = ParamOf(index++);
        var chosen = new DynamicClass?[count];

        void Dispatch(int i)
        {
            if (i == count)
            {
                var leaf = _m.DynamicCaseFunction(site, chosen);
                _b.Ret(_b.Call(leaf.Type, leaf.Fn, [.. Enumerable.Range(0, (int)index).Select(p => ParamOf((uint)p))]));
                return;
            }
            if (plan.Classes[i] is not { } classes)
            {
                Dispatch(i + 1);
                return;
            }
            var obj = values[i];
            var isNull = NewBlock("dyn.null");
            var notNull = NewBlock("dyn.value");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, obj, LlvmContext.Null(L.Ptr)), isNull, notNull);
            _b.PositionAtEnd(isNull);
            chosen[i] = classes.First(c => c.Key.Kind == DynamicClassKind.Null);
            Dispatch(i + 1);
            _b.PositionAtEnd(notNull);
            var mt = _b.Load(L.Ptr, obj, "mt");
            foreach (var c in classes)
            {
                if (c.Key.Kind is DynamicClassKind.Null or DynamicClassKind.Default or DynamicClassKind.Unsupported) continue;
                var hit = NewBlock("dyn.case");
                var next = NewBlock("dyn.next");
                LLVMValueRef matches = default;
                foreach (var candidate in c.Mts)
                {
                    var equal = _b.ICmp(LLVMIntPredicate.EQ, mt, candidate);
                    matches = matches.Handle == 0 ? equal : _b.BinOp(LLVMOpcode.Or, matches, equal);
                }
                _b.CondBr(matches, hit, next);
                _b.PositionAtEnd(hit);
                chosen[i] = c;
                Dispatch(i + 1);
                _b.PositionAtEnd(next);
            }
            chosen[i] = classes.First(c => c.Key.Kind is DynamicClassKind.Default or DynamicClassKind.Unsupported);
            Dispatch(i + 1);
        }

        Dispatch(0);
        Finish();
    }

    /// <summary>One case of a site: the operands' values (the site's parameters) bound for <paramref name="chosen"/>.</summary>
    internal void EmitDynamicCase(DynamicSite site, DynamicClass?[] chosen)
    {
        var values = new LLVMValueRef[site.Operands.Count];
        uint index = 0;
        for (var i = 0; i < site.Operands.Count; i++)
            if (site.Operands[i].Passed) values[i] = ParamOf(index++);
        EmitDynamicLeaf(site, chosen, values);
        Finish();
    }

    private void EmitDynamicLeaf(DynamicSite site, DynamicClass?[] chosen, LLVMValueRef[] values)
    {
        var leaf = _m.BindDynamicLeaf(site, chosen);
        if (leaf.Error is { } error)
        {
            ThrowRuntimeBinderException(_m.StringLiteral(error));
            return;
        }
        if (leaf.ErrorTemplate is { } template)
        {
            ThrowRuntimeBinderException(DynamicMessage(template, values));
            return;
        }
        if (leaf.Unsupported is { } unsupported)
        {
            ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!, $"Not supported by csharpc yet: {unsupported}.");
            return;
        }
        if (leaf.ConstantBool is { } constant)
        {
            _b.Ret(constant ? L.True : L.False);
            return;
        }
        foreach (var (a, b) in leaf.ReferenceChecks ?? [])
            EmitDynamicReferenceCheck(site, chosen, values, a, b);

        for (var i = 0; i < site.Operands.Count; i++)
        {
            var operand = site.Operands[i];
            if (operand.Kind == DynamicOperandKind.This) _this = values[i];
            if (leaf.Locals[i] is not { } local) continue;
            var localType = S(local.Type);
            switch (operand.Kind)
            {
                case DynamicOperandKind.Static when operand.RefKind != RefKind.None:
                    _slots[local] = values[i]; // the caller's variable
                    break;
                case DynamicOperandKind.Static:
                {
                    var slot = NewTemp(T.Storage(localType), local.Name);
                    _b.Store(ToStorage(values[i], localType), slot);
                    _slots[local] = slot;
                    break;
                }
                default:
                {
                    if (localType.IsValueType)
                    {
                        var payload = _b.InBoundsGep(L.I8, values[i], [L.ConstInt(L.I64, 8)]);
                        // A boxed struct receiver is used in place: the binder's call mutates the box (as on .NET).
                        if (operand.Role is DynamicRole.Receiver or DynamicRole.IndexReceiver) _slots[local] = payload;
                        else
                        {
                            var copy = NewTemp(T.Storage(localType), local.Name);
                            _b.Store(_b.Load(T.Storage(localType), payload), copy);
                            _slots[local] = copy;
                        }
                    }
                    else
                    {
                        var slot = NewTemp(L.Ptr, local.Name);
                        _b.Store(values[i], slot);
                        _slots[local] = slot;
                    }
                    break;
                }
            }
        }

        var done = NewBlock("dyn.done");
        LowerCfg(leaf.Graph!, done);
        _b.PositionAtEnd(done);
        if (site.ReturnsBool)
        {
            _b.Ret(LoadValue(LocalSlot(leaf.Result!), S(leaf.Result!.Type)));
            return;
        }
        if (leaf.Result is not { } result)
        {
            _b.Ret(site.ResultType is { } rt ? LlvmContext.Null(T.Value(S(rt))) : LlvmContext.Null(L.Ptr));
            return;
        }
        var resultType = S(result.Type);
        var value = LoadValue(LocalSlot(result), resultType);
        if (site.ResultType is { } target)
        {
            _b.Ret(Coerce(value, resultType, S(target)));
            return;
        }
        var at = new IlValueOperation(site.Syntax, result.Type, () => value);
        _b.Ret(resultType.IsValueType ? Box(value, resultType, at) : value);
    }

    /// <summary>`a == b` bound as object==object for a type with no operators: legal only between related reference types.</summary>
    private void EmitDynamicReferenceCheck(DynamicSite site, DynamicClass?[] chosen, LLVMValueRef[] values, int a, int b)
    {
        var obj = values[a];
        var other = site.Operands[b];
        var ok = NewBlock("dyn.refeq.ok");
        var fail = NewBlock("dyn.refeq.fail");
        LLVMValueRef compatible;
        if (other.Kind == DynamicOperandKind.Dynamic)
        {
            var otherObj = values[b];
            var forward = CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], obj, _b.Load(L.Ptr, otherObj));
            var backward = CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], otherObj, _b.Load(L.Ptr, obj));
            compatible = _b.BinOp(LLVMOpcode.Or, _b.ICmp(LLVMIntPredicate.NE, forward, LlvmContext.Null(L.Ptr)),
                _b.ICmp(LLVMIntPredicate.NE, backward, LlvmContext.Null(L.Ptr)));
        }
        else if (other.Type is INamedTypeSymbol { TypeKind: TypeKind.Class } cls)
        {
            // obj converts to the static type, or the static type to obj's type (obj's type is one of its bases).
            var mapped = (INamedTypeSymbol)S(cls);
            compatible = _b.ICmp(LLVMIntPredicate.NE, CallRuntime("csrt_isinst_class", L.Ptr, [L.Ptr, L.Ptr], obj, MethodTableFor(mapped, new IlValueOperation(site.Syntax, cls, () => default))), LlvmContext.Null(L.Ptr));
            var mt = _b.Load(L.Ptr, obj);
            for (INamedTypeSymbol? t = cls; t is not null; t = t.BaseType)
            {
                LLVMValueRef baseMt;
                try { baseMt = MethodTableFor(S(t), new IlValueOperation(site.Syntax, t, () => default)); }
                catch (CompileError) { continue; }
                compatible = _b.BinOp(LLVMOpcode.Or, compatible, _b.ICmp(LLVMIntPredicate.EQ, mt, baseMt));
            }
        }
        else return; // interfaces, value types: bound as object already reports what the binder does
        _b.CondBr(compatible, ok, fail);
        _b.PositionAtEnd(fail);
        string Name(int i) => site.Operands[i].Kind switch
        {
            DynamicOperandKind.Dynamic when chosen[i]!.Key.Kind == DynamicClassKind.Typed => _m.BinderTypeName(chosen[i]!.Key.Type!),
            DynamicOperandKind.Dynamic => "\u0001",
            _ => _m.BinderTypeName(site.Operands[i].Type!),
        };
        var parts = new List<object> { $"Operator '{site.MessageOperator}' cannot be applied to operands of type '" };
        if (Name(0) == "\u0001") parts.Add(0); else parts.Add(Name(0));
        parts.Add("' and '");
        if (Name(1) == "\u0001") parts.Add(1); else parts.Add(Name(1));
        parts.Add("'");
        ThrowRuntimeBinderException(DynamicMessage(parts, values));
        _b.PositionAtEnd(ok);
    }

    /// <summary>A binder message naming run-time types: literal parts and operand indices (~i: as member lookup names it).</summary>
    private LLVMValueRef DynamicMessage(List<object> parts, LLVMValueRef[] values)
    {
        var names = _m.DynamicTypeNameFunction();
        LLVMValueRef message = default;
        foreach (var part in parts)
        {
            LLVMValueRef piece;
            if (part is string text)
            {
                if (text.Length == 0) continue;
                piece = _m.StringLiteral(text);
            }
            else
            {
                var operand = (int)part;
                var lookup = operand < 0;
                if (lookup) operand = ~operand;
                piece = _b.Call(names.Type, names.Fn, [_b.Load(L.Ptr, values[operand], "mt"), L.ConstInt(L.I32, lookup ? 1 : 0)]);
            }
            message = message.Handle == 0 ? piece : ConcatStrings(message, piece);
        }
        return message.Handle == 0 ? _m.StringLiteral("") : message;
    }

    private void ThrowRuntimeBinderException(LLVMValueRef message)
    {
        var type = _m.Compilation.GetTypeByMetadataName("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException")
            ?? throw CompileError.Unsupported("dynamic without Microsoft.CSharp (RuntimeBinderException)");
        var layout = _m.GetLayout(type);
        var exception = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
        var none = LlvmContext.Null(L.Ptr);
        CallRuntime("csrt_exception_init", L.Void, [L.Ptr, L.Ptr, L.Ptr, L.Ptr], exception, message, none, none);
        CallRuntime("csrt_throw_object", L.Void, [L.Ptr], exception);
        _b.Unreachable();
    }

    /// <summary>`string typename(mt, lookup)`: the binder's name for each run-time type; unknown types throw.</summary>
    internal void EmitDynamicTypeNames(List<(ITypeSymbol Type, LLVMValueRef Mt)> types, ModuleCodeGen m)
    {
        var mt = ParamOf(0);
        var lookup = _b.ICmp(LLVMIntPredicate.NE, ParamOf(1), L.ConstInt(L.I32, 0));
        foreach (var (type, table) in types)
        {
            string name;
            try { name = m.BinderTypeName(m.BestAccessibleType(type, null)); }
            catch (Exception e) when (e is not OutOfMemoryException) { continue; }
            var hit = NewBlock("name");
            var next = NewBlock("next");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, mt, table), hit, next);
            _b.PositionAtEnd(hit);
            if (type is IArrayTypeSymbol)
                _b.Ret(_b.Select(lookup, m.StringLiteral("System.Array"), m.StringLiteral(name)));
            else _b.Ret(m.StringLiteral(name));
            _b.PositionAtEnd(next);
        }
        ThrowNewLibraryException(m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!,
            "Not supported by csharpc yet: a dynamic operation on an object of a type csharpc did not see at compile time.");
        Finish();
    }
}
