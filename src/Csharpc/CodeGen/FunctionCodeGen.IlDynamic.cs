using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.CodeGen;

/// <summary>
/// One of csc's `dynamic` call sites, decoded from the IL that creates it:
/// `Binder.X(flags, name, typeArgs, typeof(Context), new[] { CSharpArgumentInfo.Create(flags, name), ... })`.
/// </summary>
internal sealed class IlDynamicSpec
{
    public required string Binder { get; init; }
    public int Flags { get; init; }
    public string? Name { get; init; }
    public ITypeSymbol[] TypeArguments { get; init; } = [];
    public required ITypeSymbol Context { get; init; }
    /// <summary>BinaryOperation / UnaryOperation: the System.Linq.Expressions.ExpressionType.</summary>
    public int ExpressionType { get; init; } = -1;
    /// <summary>Convert: the target type.</summary>
    public ITypeSymbol? Type { get; init; }
    public (int Flags, string? Name)[] Arguments { get; init; } = [];
    public int Offset { get; set; }
}

/// <summary>The value of a call-site field on the IL stack (or of its Target delegate): no run-time value, csharpc binds the site.</summary>
internal sealed record IlDynamicMarker(ISymbol Field, IlDynamicSpec Spec, bool IsTarget);

/// <summary>
/// `dynamic` in code compiled from IL (docs/adr/0019-dynamic.md, "From IL"). csc caches each dynamic operation in a
/// static `CallSite&lt;TDelegate&gt;` field of a nested `&lt;&gt;o__N` class, created behind `ldsfld site; brtrue` from
/// constants only, and invokes it as `site.Target(site, operands...)`. csharpc decodes that creation into the
/// same <see cref="DynamicSite"/> the source path builds from Roslyn's operations, treats the guard as always
/// taken (the creation is never lowered: no binder, no CallSite), and lowers the Target invocation as a call of
/// the site's dispatch function. Anything about a site csharpc can't decode exactly is an explicit diagnostic.
/// </summary>
public sealed partial class FunctionCodeGen
{
    // Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags and CSharpBinderFlags.
    private const int ArgUseCompileTimeType = 1, ArgConstant = 2, ArgNamed = 4, ArgIsRef = 8, ArgIsOut = 16, ArgIsStaticType = 32;
    private const int BinderChecked = 1, BinderSimpleName = 2, BinderSpecialName = 4, BinderLogical = 8, BinderExplicit = 16,
        BinderArrayIndex = 32, BinderResultDiscarded = 256;

    /// <summary>Per call-site field of the method: its decoded description, or why it can't be decoded.</summary>
    private Dictionary<ISymbol, object>? _ilDynamicSites;

    internal static bool IsCallSiteField(IFieldSymbol field) =>
        field.IsStatic && field.ContainingType.Name.StartsWith("<>o__", StringComparison.Ordinal)
        && field.Type is INamedTypeSymbol { Name: "CallSite", Arity: 1, ContainingNamespace: { Name: "CompilerServices", ContainingNamespace: { Name: "Runtime", ContainingNamespace.Name: "System" } } };

    /// <summary>The method creates dynamic call sites (`typeof` operands then keep their type across branches).</summary>
    private bool IlHasDynamicSites => (_ilDynamicSites ??= DecodeIlDynamicSites()).Count > 0;

    private object? IlDynamicSite(IFieldSymbol field) =>
        (_ilDynamicSites ??= DecodeIlDynamicSites()).TryGetValue(field, out var site) ? site : null;

    /// <summary>`ldsfld site` followed by the guard's brtrue/brfalse: the site is decoded, so the guard always skips its creation.</summary>
    private bool IlIsDynamicGuard(int token)
    {
        IFieldSymbol? field;
        try { field = IlAssembly.ResolveMember(token, IlBody.Context) as IFieldSymbol; }
        catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { return false; }
        return field is not null && IsCallSiteField(field) && IlDynamicSite(field) is IlDynamicSpec;
    }

    private Dictionary<ISymbol, object> DecodeIlDynamicSites()
    {
        var result = new Dictionary<ISymbol, object>(SymbolEqualityComparer.Default);
        var instructions = IlBody.Instructions;
        for (var g = 0; g + 1 < instructions.Length; g++)
        {
            if (instructions[g].OpCode != ILOpCode.Ldsfld) continue;
            IFieldSymbol? field;
            try { field = IlAssembly.ResolveMember(instructions[g].Token, IlBody.Context) as IFieldSymbol; }
            catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { continue; }
            if (field is null || !IsCallSiteField(field)) continue;
            var branch = instructions[g + 1];
            // Release: `ldsfld; brtrue skip; <create>; stsfld`. Debug: `ldsfld; brfalse create; br skip; create: ...; stsfld`.
            int start;
            if (branch.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s) start = g + 2;
            else if (branch.OpCode is ILOpCode.Brfalse or ILOpCode.Brfalse_s) start = instructions.IndexOf(instructions.FirstOrDefault(i => i.Offset == branch.Target));
            else continue;
            if (result.ContainsKey(field))
            {
                result[field] = "a call site created twice in one method";
                continue;
            }
            try
            {
                if (start < 0) throw new IlException("the creation of a call site is not where its guard branches");
                var spec = DecodeIlDynamicCreation(field, start);
                spec.Offset = instructions[g].Offset;
                result[field] = spec;
            }
            catch (IlException e) { result[field] = e.Message; }
            catch (Exception e) when (e is InvalidCastException or InvalidOperationException or IndexOutOfRangeException or NullReferenceException or BadImageFormatException)
            {
                result[field] = $"a call site created by IL csharpc doesn't recognize ({e.Message})";
            }
        }
        return result;
    }

    private sealed record IlTypeToken(ITypeSymbol Type);
    private sealed record IlArgumentInfo(int Flags, string? Name);

    /// <summary>Evaluates the creation of a call site (constants, `typeof`, arrays, the binder factory) up to its `stsfld`.</summary>
    private IlDynamicSpec DecodeIlDynamicCreation(IFieldSymbol field, int start)
    {
        var instructions = IlBody.Instructions;
        var stack = new Stack<object?>();
        object? Pop() => stack.Count > 0 ? stack.Pop() : throw new IlException("a call site's creation underflows the stack");
        for (var i = start; i < instructions.Length; i++)
        {
            var ins = instructions[i];
            var op = ins.OpCode;
            switch (op)
            {
                case ILOpCode.Nop: break;
                case >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4_8: stack.Push((int)op - (int)ILOpCode.Ldc_i4_0); break;
                case ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s: stack.Push((int)ins.Int); break;
                case ILOpCode.Ldstr: stack.Push(IlAssembly.UserString(ins.Token)); break;
                case ILOpCode.Ldnull: stack.Push(null); break;
                case ILOpCode.Ldtoken: stack.Push(new IlTypeToken(ResolveIlType(ins.Token))); break;
                case ILOpCode.Newarr: stack.Push(new object?[(int)Pop()!]); break;
                case ILOpCode.Dup: stack.Push(stack.Count > 0 ? stack.Peek() : throw new IlException("a call site's creation underflows the stack")); break;
                case ILOpCode.Stelem_ref or ILOpCode.Stelem:
                {
                    var value = Pop();
                    var index = (int)Pop()!;
                    ((object?[])Pop()!)[index] = value;
                    break;
                }
                case ILOpCode.Call:
                {
                    var method = IlMethod(ins.Token);
                    var owner = method.ContainingType;
                    var ns = owner.ContainingNamespace?.ToDisplayString();
                    if (owner.Name == "Type" && ns == "System" && method.Name == "GetTypeFromHandle")
                        stack.Push(((IlTypeToken)Pop()!).Type);
                    else if (owner.Name == "CSharpArgumentInfo" && ns == "Microsoft.CSharp.RuntimeBinder" && method.Name == "Create")
                    {
                        var name = (string?)Pop();
                        stack.Push(new IlArgumentInfo((int)Pop()!, name));
                    }
                    else if (owner.Name == "Binder" && ns == "Microsoft.CSharp.RuntimeBinder")
                        stack.Push(DecodeIlBinder(method, Pop));
                    else if (owner.Name == "CallSite" && owner.Arity == 1 && ns == "System.Runtime.CompilerServices" && method.Name == "Create")
                    {
                        if (Pop() is not IlDynamicSpec spec) throw new IlException("a call site created from something other than a C# binder");
                        stack.Push(spec);
                    }
                    else throw new IlException($"a call to '{method}' in a call site's creation");
                    break;
                }
                case ILOpCode.Stsfld:
                {
                    if (!SymbolEqualityComparer.Default.Equals(IlField(ins.Token), field) || stack.Count != 1 || stack.Pop() is not IlDynamicSpec done)
                        throw new IlException("a call site's creation that stores something else");
                    return done;
                }
                default:
                    throw new IlException($"'{IlInstruction.Name(op)}' in a call site's creation");
            }
        }
        throw new IlException("a call site's creation without its store");
    }

    private IlDynamicSpec DecodeIlBinder(IMethodSymbol method, Func<object?> pop)
    {
        (int, string?)[] Infos(object? value) =>
            [.. ((object?[])value!).Select(v => v is IlArgumentInfo a ? (a.Flags, a.Name) : throw new IlException("a call site argument without CSharpArgumentInfo"))];
        ITypeSymbol Context(object? value) => value as ITypeSymbol ?? throw new IlException("a call site whose context is not a typeof");
        var parameters = method.Parameters.Length;
        switch (method.Name)
        {
            case "InvokeMember" when parameters == 5:
            {
                var args = Infos(pop());
                var context = Context(pop());
                var typeArguments = pop() is object?[] types ? types.Select(t => t as ITypeSymbol ?? throw new IlException("a type argument that is not a typeof")).ToArray() : [];
                var name = (string)pop()!;
                return new IlDynamicSpec { Binder = method.Name, Arguments = args, Context = context, TypeArguments = typeArguments, Name = name, Flags = (int)pop()! };
            }
            case "GetMember" or "SetMember" when parameters == 4:
            {
                var args = Infos(pop());
                var context = Context(pop());
                var name = (string)pop()!;
                return new IlDynamicSpec { Binder = method.Name, Arguments = args, Context = context, Name = name, Flags = (int)pop()! };
            }
            case "IsEvent" when parameters == 3:
            {
                var context = Context(pop());
                var name = (string)pop()!;
                return new IlDynamicSpec { Binder = method.Name, Arguments = [(0, null)], Context = context, Name = name, Flags = (int)pop()! };
            }
            case "GetIndex" or "SetIndex" or "Invoke" or "InvokeConstructor" when parameters == 3:
            {
                var args = Infos(pop());
                var context = Context(pop());
                return new IlDynamicSpec { Binder = method.Name, Arguments = args, Context = context, Flags = (int)pop()! };
            }
            case "BinaryOperation" or "UnaryOperation" when parameters == 4:
            {
                var args = Infos(pop());
                var context = Context(pop());
                var expressionType = (int)pop()!;
                return new IlDynamicSpec { Binder = method.Name, Arguments = args, Context = context, ExpressionType = expressionType, Flags = (int)pop()! };
            }
            case "Convert" when parameters == 3:
            {
                var context = Context(pop());
                var type = pop() as ITypeSymbol ?? throw new IlException("a dynamic conversion to a type that is not a typeof");
                return new IlDynamicSpec { Binder = method.Name, Arguments = [(0, null)], Context = context, Type = type, Flags = (int)pop()! };
            }
            default:
                throw new IlException($"the call site binder Microsoft.CSharp.RuntimeBinder.Binder.{method.Name}");
        }
    }

    // ======================================================================================
    // Lowering hooks
    // ======================================================================================

    /// <summary>`ldsfld` of a call-site field: a marker (the site has no run-time object).</summary>
    private IlEntry? IlDynamicSiteLoad(IFieldSymbol field)
    {
        if (!IsCallSiteField(field)) return null;
        return IlDynamicSite(field) switch
        {
            IlDynamicSpec spec => new IlEntry(IlKind.Ref, LlvmContext.Null(L.Ptr), field.Type) { Dynamic = new IlDynamicMarker(field, spec, false), NonNull = true },
            string why => throw new IlException($"'dynamic' in compiled code: {why}"),
            _ => throw new IlException("'dynamic' in compiled code: a call site used without csc's guarded creation"),
        };
    }

    /// <summary>An entry reloaded at a block start: the compile-time facts of the value that entered it.</summary>
    private static IlEntry IlCarriedEntry(IlEntry carried, LLVMValueRef loaded) =>
        carried.Dynamic is not null ? carried
        : new IlEntry(carried.Kind, loaded, carried.Type) { Symbol = carried.Symbol, NonNull = carried.NonNull, Constant = carried.Constant };

    /// <summary>
    /// Values a dynamic call site needs to know at compile time, which csc leaves on the stack while it branches
    /// around nested sites' creation: the sites themselves, `typeof` operands and constant operands.
    /// </summary>
    private bool IlCarries(IlEntry e) => e.Dynamic is not null
        || ((e is { Kind: IlKind.Ref, Symbol: ITypeSymbol } || e.Constant is not null) && IlHasDynamicSites);

    private IlEntry?[]? IlCarry(List<IlEntry> values) =>
        values.Any(IlCarries) ? [.. values.Select(v => IlCarries(v) ? v : null)] : null;

    private void IlCheckCarried(IlBlock target, List<IlEntry> values)
    {
        for (var i = 0; i < values.Count && i < target.Entry!.Count; i++)
        {
            var expected = target.Carried?[i];
            var actual = values[i];
            if (expected?.Dynamic is not null || actual.Dynamic is not null)
            {
                if (!Equals(expected?.Dynamic, actual.Dynamic))
                    throw new IlException($"a dynamic call site merged with another value at IL_{target.Start:X4}");
                continue;
            }
            if (expected is not null && (!SymbolEqualityComparer.Default.Equals(expected.Symbol, actual.Symbol) || !Equals(expected.Constant, actual.Constant)))
            {
                if (target.Emitted) throw new IlException($"a constant or 'typeof' operand of a dynamic call merged with another value at IL_{target.Start:X4}");
                target.Carried![i] = null;
            }
        }
    }

    /// <summary>`callvirt TDelegate::Invoke` on a site's Target: the site's dispatch function.</summary>
    private void IlDynamicInvoke(IMethodSymbol invoke, IlDynamicMarker marker, IlEntry[] args, Stack<IlEntry> stack)
    {
        if (!marker.IsTarget || invoke.Name != "Invoke")
            throw new IlException($"'dynamic' in compiled code: a call site used by '{invoke.Name}'");
        if (args.Length == 0 || args[0].Dynamic is not { IsTarget: false } site || !SymbolEqualityComparer.Default.Equals(site.Field, marker.Field))
            throw new IlException("'dynamic' in compiled code: a call site's Target invoked with another site");
        var spec = marker.Spec;
        var parameters = invoke.Parameters;
        var count = parameters.Length - 1;
        if (spec.Arguments.Length != count)
            throw new IlException($"'dynamic' in compiled code: a call site with {spec.Arguments.Length} argument infos invoked with {count} operands");
        var flags = spec.Flags;
        DynamicKind kind;
        string? name = spec.Name, token = null, message = null;
        bool compound = false, simpleName = (flags & BinderSimpleName) != 0, discarded = (flags & BinderResultDiscarded) != 0;
        switch (spec.Binder)
        {
            case "InvokeMember" when (flags & BinderSpecialName) != 0:
                // `d.E += h` on an event: csc calls the accessor by its special name (after Binder.IsEvent).
                if (name is not { } accessor || !(accessor.StartsWith("add_", StringComparison.Ordinal) || accessor.StartsWith("remove_", StringComparison.Ordinal)))
                    throw new IlException($"'dynamic' in compiled code: a call of the special-name member '{name}'");
                kind = DynamicKind.SetMember;
                compound = true;
                token = accessor.StartsWith("add_", StringComparison.Ordinal) ? "+" : "-";
                name = accessor[(accessor.IndexOf('_') + 1)..];
                discarded = true;
                simpleName = false;
                break;
            case "InvokeMember":
                if (name == "GetAwaiter") throw new IlException("awaiting a dynamic value (a dynamically bound GetAwaiter)");
                kind = DynamicKind.InvokeMember;
                break;
            case "GetMember": kind = DynamicKind.GetMember; break;
            case "SetMember": kind = DynamicKind.SetMember; break;
            case "GetIndex": kind = DynamicKind.GetIndex; break;
            case "SetIndex": kind = DynamicKind.SetIndex; break;
            case "Invoke": kind = DynamicKind.Invoke; break;
            case "InvokeConstructor": kind = DynamicKind.New; break;
            case "IsEvent": kind = DynamicKind.IsEvent; break;
            case "Convert":
                kind = DynamicKind.Convert;
                if ((flags & BinderArrayIndex) != 0) throw new IlException("a dynamic value as the index of an array (Binder.Convert with ConvertArrayIndex)");
                break;
            case "BinaryOperation":
                kind = DynamicKind.Binary;
                (token, compound) = IlBinaryOperator(spec.ExpressionType);
                message = (flags & BinderLogical) != 0 ? token switch { "&" => "&&", "|" => "||", _ => throw new IlException($"the logical binary operation {spec.ExpressionType}") } : token;
                if (compound) message += "=";
                break;
            case "UnaryOperation":
                (kind, token) = spec.ExpressionType switch
                {
                    83 => (DynamicKind.IsTrue, (string?)null), 84 => (DynamicKind.IsFalse, null),
                    28 => (DynamicKind.Unary, "-"), 29 => (DynamicKind.Unary, "+"), 34 => (DynamicKind.Unary, "!"),
                    82 => (DynamicKind.Unary, "~"), 54 => (DynamicKind.Unary, "++"), 49 => (DynamicKind.Unary, "--"),
                    _ => throw new IlException($"'dynamic' in compiled code: the unary operation {spec.ExpressionType}"),
                };
                break;
            default:
                throw new IlException($"'dynamic' in compiled code: the binder {spec.Binder}");
        }

        var operands = new List<DynamicOperand>();
        var values = new List<LLVMValueRef>();
        INamedTypeSymbol? staticReceiver = null;
        ITypeSymbol? resultType = kind == DynamicKind.Convert ? _subst.Apply(spec.Type!) : null;
        var hasReceiver = kind is DynamicKind.InvokeMember or DynamicKind.GetMember or DynamicKind.SetMember or DynamicKind.IsEvent
            or DynamicKind.GetIndex or DynamicKind.SetIndex or DynamicKind.Invoke;
        for (var j = 0; j < count; j++)
        {
            var (argFlags, argName) = spec.Arguments[j];
            var parameter = parameters[j + 1];
            var entry = args[j + 1];
            var type = _subst.Apply(parameter.Type);
            var role = kind switch
            {
                DynamicKind.Binary or DynamicKind.Unary or DynamicKind.IsTrue or DynamicKind.IsFalse => DynamicRole.Operator,
                DynamicKind.Convert => DynamicRole.Convert,
                _ when j > 0 || !hasReceiver => DynamicRole.Argument,
                DynamicKind.GetIndex or DynamicKind.SetIndex => DynamicRole.IndexReceiver,
                DynamicKind.Invoke => DynamicRole.InvokeReceiver,
                _ => DynamicRole.Receiver,
            };
            if ((argFlags & ArgIsStaticType) != 0)
            {
                // `Console.WriteLine(d)`, `new T(d)`: the type is the operand (always csc's `typeof`).
                var named = entry.Symbol is ITypeSymbol t ? _subst.Apply(t) as INamedTypeSymbol : null;
                if (j != 0 || named is null || kind is not (DynamicKind.InvokeMember or DynamicKind.New))
                    throw new IlException("'dynamic' in compiled code: a static-type operand csharpc can't place");
                if (kind == DynamicKind.New) resultType = named;
                else
                {
                    staticReceiver = named;
                    simpleName = false;
                }
                continue;
            }
            if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
                throw new IlException("Dynamic calls cannot be used in conjunction with pointers");
            var byRef = parameter.RefKind != RefKind.None;
            var refKind = (argFlags & ArgIsOut) != 0 ? RefKind.Out : (argFlags & ArgIsRef) != 0 || byRef ? RefKind.Ref : RefKind.None;
            if (refKind != RefKind.None && !byRef) throw new IlException("'dynamic' in compiled code: a by-reference operand passed by value");
            var operandName = (argFlags & ArgNamed) != 0 ? argName : null;
            if (j == 0 && simpleName && kind == DynamicKind.InvokeMember)
            {
                // A simple-name call (`Helper(d)` in an instance member): csc passes `this` (statically typed).
                if ((argFlags & ArgUseCompileTimeType) == 0) throw new IlException("'dynamic' in compiled code: a simple-name call with a dynamic receiver");
                operands.Add(new DynamicOperand { Kind = DynamicOperandKind.This, Role = role, Type = type, RefKind = refKind == RefKind.None ? RefKind.None : RefKind.Ref });
                values.Add(refKind != RefKind.None ? IlAddress(entry) : IlTyped(entry, type));
                continue;
            }
            if ((argFlags & ArgUseCompileTimeType) == 0)
            {
                if (refKind != RefKind.None) throw new IlException("a dynamic value passed by reference to a dynamic call");
                if (!type.IsReferenceType) throw new IlException($"'dynamic' in compiled code: a dynamic operand of type '{type}'");
                operands.Add(new DynamicOperand { Kind = DynamicOperandKind.Dynamic, Role = role, Name = operandName });
                values.Add(IlTyped(entry, Special(SpecialType.System_Object)));
                continue;
            }
            if ((argFlags & ArgConstant) != 0 && refKind == RefKind.None)
            {
                if (entry is { Kind: IlKind.Ref, Type: null } && Native.LLVMIsNull(entry.Value) != 0)
                {
                    operands.Add(new DynamicOperand { Kind = DynamicOperandKind.Constant, Role = role, Name = operandName, Constant = null,
                        Type = type.SpecialType == SpecialType.System_Object ? null : type });
                    continue;
                }
                if (entry.Constant is { } constant && TypedConstant(constant, type) is { HasValue: true } typed)
                {
                    operands.Add(new DynamicOperand { Kind = DynamicOperandKind.Constant, Role = role, Name = operandName, Constant = typed.Value, Type = type });
                    continue;
                }
                // Only integral constants convert specially (to a narrower type, 0 to an enum): any other constant
                // binds as a value of its type (decimal literals arrive as a constructor call).
                if (type.SpecialType is >= SpecialType.System_SByte and <= SpecialType.System_UInt64 or SpecialType.System_Char or SpecialType.System_Boolean
                    || type.TypeKind == TypeKind.Enum)
                    throw new IlException($"'dynamic' in compiled code: a constant operand of type '{type}' whose value csharpc can't read");
            }
            operands.Add(new DynamicOperand { Kind = DynamicOperandKind.Static, Role = role, Name = operandName, RefKind = refKind, Type = type });
            values.Add(refKind != RefKind.None ? IlAddress(entry) : IlTyped(entry, type));
        }
        if (kind == DynamicKind.New && resultType is null) throw new IlException("'dynamic' in compiled code: a constructor call without its type");

        var context = _subst.Apply(spec.Context) as INamedTypeSymbol ?? throw new IlException("'dynamic' in compiled code: a call site context that is not a named type");
        var key = $"il:{RuntimeHelpers.GetHashCode(IlAssembly)}:{marker.Field.OriginalDefinition.MetadataToken}|{_subst.Key}";
        var siteKind = kind;
        var dynamicSite = _m.DynamicSiteFor(key, () =>
        {
            var (model, position) = _m.IlDynamicBindingContext();
            var description = $"{spec.Binder}{(spec.Name is { } n ? " '" + n + "'" : "")} at IL_{spec.Offset:X4} in {IlBody.Method.OriginalDefinition.ToDisplayString()}";
            var syntax = CSharpSyntaxTree.Create(SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(description)), path: IlAssembly.Path).GetRoot();
            return new DynamicSite
            {
                Kind = siteKind, Name = name, TypeArguments = [.. spec.TypeArguments.Select(_subst.Apply)], StaticReceiver = staticReceiver,
                SimpleName = simpleName && staticReceiver is null, Operator = token, MessageOperator = message ?? token, Compound = compound,
                Checked = (flags & BinderChecked) != 0, Explicit = (flags & BinderExplicit) != 0, ResultDiscarded = discarded, ResultType = resultType,
                Operands = operands, Model = model, Position = position, Context = context, Subst = _subst, Syntax = syntax, FromIl = true,
            };
        });
        var result = _b.Call(dynamicSite.Function.Type, dynamicSite.Function.Fn, [.. values]);

        var returnType = _subst.Apply(invoke.ReturnType);
        if (returnType.SpecialType == SpecialType.System_Void) return;
        if (dynamicSite.ReturnsBool)
        {
            if (returnType.SpecialType != SpecialType.System_Boolean) throw new IlException($"'dynamic' in compiled code: a {spec.Binder} site returning '{returnType}'");
            stack.Push(IlPush(result, returnType));
            return;
        }
        if (dynamicSite.ResultType is { } produced && !SymbolEqualityComparer.Default.Equals(produced, returnType))
        {
            // `new S(d)` as object: csc's site returns the created value boxed.
            if (!returnType.IsReferenceType) throw new IlException($"'dynamic' in compiled code: a {spec.Binder} site of '{produced}' returning '{returnType}'");
            if (produced.IsValueType) result = Box(result, produced, IlAt());
            stack.Push(new IlEntry(IlKind.Ref, result, returnType));
            return;
        }
        if (dynamicSite.ResultType is null && !returnType.IsReferenceType)
            throw new IlException($"'dynamic' in compiled code: a {spec.Binder} site returning '{returnType}'");
        stack.Push(IlPush(result, returnType));
    }

    /// <summary>System.Linq.Expressions.ExpressionType of a binary operation: the C# operator and whether it is compound.</summary>
    private static (string Token, bool Compound) IlBinaryOperator(int expressionType) => expressionType switch
    {
        0 => ("+", false), 2 => ("&", false), 12 => ("/", false), 13 => ("==", false), 14 => ("^", false), 15 => (">", false),
        16 => (">=", false), 19 => ("<<", false), 20 => ("<", false), 21 => ("<=", false), 25 => ("%", false), 26 => ("*", false),
        35 => ("!=", false), 36 => ("|", false), 41 => (">>", false), 42 => ("-", false),
        63 => ("+", true), 64 => ("&", true), 65 => ("/", true), 66 => ("^", true), 67 => ("<<", true), 68 => ("%", true),
        69 => ("*", true), 70 => ("|", true), 72 => (">>", true), 73 => ("-", true),
        _ => throw new IlException($"'dynamic' in compiled code: the binary operation {expressionType}"),
    };
}
