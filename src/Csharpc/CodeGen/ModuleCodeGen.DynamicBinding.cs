using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>The outcome of binding a dynamic site for one combination of operand cases.</summary>
internal sealed class DynamicLeaf
{
    /// <summary>A RuntimeBinderException with this message.</summary>
    public string? Error;
    /// <summary>A RuntimeBinderException whose message names run-time types: string parts and operand indices.</summary>
    public List<object>? ErrorTemplate;
    /// <summary>A NotSupportedException (what csharpc can't bind yet).</summary>
    public string? Unsupported;
    public ControlFlowGraph? Graph;
    /// <summary>Per operand: the local of the bound code that holds it (null: not a local).</summary>
    public ILocalSymbol?[] Locals = [];
    /// <summary>The local holding the result (null: no value).</summary>
    public ILocalSymbol? Result;
    /// <summary>IsEvent: known at compile time.</summary>
    public bool? ConstantBool;
    /// <summary>==/!= on a reference type bound as object: the operand pairs that must be reference-compatible at run time.</summary>
    public List<(int A, int B)>? ReferenceChecks;
    /// <summary>The bound code reads a dynamic member and invokes its value dynamically: that Invoke site.</summary>
    public DynamicSite? DeferredInvoke;
}

public sealed partial class ModuleCodeGen
{
    // Microsoft.CSharp's messages (src/libraries/Microsoft.CSharp/src/Resources/Strings.resx).
    internal const string BinderNullReference = "Cannot perform runtime binding on a null reference";
    private const string BinderVoidResult = "Cannot implicitly convert type 'void' to 'object'";
    private const string BinderNonDelegate = "Cannot invoke a non-delegate type";

    // ======================================================================================
    // Planning: which run-time types get their own case
    // ======================================================================================

    internal DynamicPlan PlanDynamicSite(DynamicSite site)
    {
        var runtime = DynamicRuntimeTypes();
        var classes = new List<DynamicClass>?[site.Operands.Count];
        var fingerprint = new StringBuilder();
        // Receivers first: the arguments' cases depend on the receivers' candidate members.
        var order = Enumerable.Range(0, site.Operands.Count)
            .OrderBy(i => site.Operands[i].Role is DynamicRole.Receiver or DynamicRole.IndexReceiver or DynamicRole.InvokeReceiver ? 0 : 1).ToList();
        DynamicCandidates? candidates = null;
        foreach (var i in order)
        {
            var operand = site.Operands[i];
            if (operand.Kind != DynamicOperandKind.Dynamic) continue;
            if (operand.Role is DynamicRole.Argument or DynamicRole.Operator) candidates ??= Candidates(site, classes);
            var byKey = new Dictionary<DynamicClassKey, DynamicClass>();
            // A generic method's type arguments inferred from the value: every type gets its instantiation, and a
            // type csharpc didn't instantiate it for fails explicitly (binding it as object would be a different call).
            var generic = operand.Role == DynamicRole.Argument && candidates!.Generic;
            var list = new List<DynamicClass>
            {
                new() { Key = DynamicClassKey.Null },
                new() { Key = generic ? DynamicClassKey.Unsupported : DynamicClassKey.Default },
            };
            foreach (var (type, mt) in runtime)
            {
                var key = Classify(site, i, type, candidates);
                if (key.Kind == DynamicClassKind.Default || (generic && key.Kind == DynamicClassKind.Unsupported)) continue;
                if (!byKey.TryGetValue(key, out var c))
                {
                    byKey[key] = c = new DynamicClass { Key = key };
                    list.Add(c);
                }
                c.Mts.Add(mt);
                c.Types.Add(type);
            }
            if (list.Count > 4096)
                throw new CompileError("CSLLVM0001", $"not supported by csharpc yet: dynamic operation with {list.Count} distinct run-time operand types (csharpc's limit is 4096)", site.Syntax.GetLocation());
            classes[i] = list;
            fingerprint.Append(i).Append(':');
            foreach (var c in list)
            {
                fingerprint.Append(c.Key.Kind).Append('/').Append(c.Key.Type?.ToDisplayString()).Append('[');
                foreach (var mt in c.Mts) fingerprint.Append(mt.Handle).Append(',');
                fingerprint.Append(']');
            }
            fingerprint.Append(';');
        }
        return new DynamicPlan { Classes = classes, Fingerprint = fingerprint.ToString() };
    }

    private sealed class DynamicCandidates
    {
        public readonly List<ITypeSymbol> Parameters = [];
        public bool Generic;
        public readonly List<ITypeSymbol> OperatorParameters = [];
    }

    private DynamicClassKey Classify(DynamicSite site, int index, ITypeSymbol runtimeType, DynamicCandidates? candidates)
    {
        var operand = site.Operands[index];
        // Arguments and operators depend on the other operands' cases: not memoized.
        if (operand.Role is not (DynamicRole.Argument or DynamicRole.Operator) && site.Classified.TryGetValue((index, runtimeType), out var known)) return known;
        var key = ClassifyCore(site, operand, BestAccessibleType(runtimeType, site.Context), candidates);
        if (operand.Role is not (DynamicRole.Argument or DynamicRole.Operator)) site.Classified[(index, runtimeType)] = key;
        return key;
    }

    private DynamicClassKey ClassifyCore(DynamicSite site, DynamicOperand operand, ITypeSymbol b, DynamicCandidates? candidates)
    {
        DynamicClassKey Typed() => new(DynamicClassKind.Typed, b);
        // The meta-object of a receiver, a converted value, or an operator's (left) operand binds the operation.
        if (IsMetaObjectProvider(b) && (operand.Role is not (DynamicRole.Argument or DynamicRole.Operator)
                || (operand.Role == DynamicRole.Operator && ReferenceEquals(operand, site.Operands[0]))))
            return new DynamicClassKey(DynamicClassKind.MetaObject, b);
        switch (operand.Role)
        {
            case DynamicRole.Receiver:
                return HasMember(b, site.Name!) ? new DynamicClassKey(DynamicClassKind.Typed, LookupLevel(b, m => m.Name == site.Name)) : DynamicClassKey.Default;
            case DynamicRole.IndexReceiver:
                return b is IArrayTypeSymbol ? Typed()
                    : HasIndexer(b) ? new DynamicClassKey(DynamicClassKind.Typed, LookupLevel(b, m => m is IPropertySymbol { IsIndexer: true }))
                    : DynamicClassKey.Default;
            case DynamicRole.InvokeReceiver:
                return b.TypeKind == TypeKind.Delegate ? Typed() : DynamicClassKey.Default;
            case DynamicRole.Argument:
                // Bounded nesting keeps instantiations from creating ever deeper types (Gen<T> making List<T>...).
                if (candidates!.Generic) return ReflectionNestingDepth(b) <= 2 ? Typed() : DynamicClassKey.Unsupported;
                if (b.SpecialType == SpecialType.System_Object) return DynamicClassKey.Default;
                // The value of `x op= y` converts explicitly: a type with only an explicit conversion binds as itself
                // (as object it would unbox); the rest fail to convert (CompoundCastError).
                var compoundValue = site.CompoundValue && ReferenceEquals(operand, site.Operands[^1]);
                foreach (var p in candidates.Parameters)
                    if (compoundValue ? ExplicitlyConvertible(b, p) : ImplicitlyConvertible(b, p)) return Typed();
                return DynamicClassKey.Default;
            case DynamicRole.Operator:
                // A struct with no operators or conversions behaves as object would (errors name its run-time type,
                // `s + "x"` concatenates) — except `==`, which is reference equality for object and an error for it.
                if (b.SpecialType != SpecialType.None || b.TypeKind is TypeKind.Enum or TypeKind.Delegate || HasUserOperators(b)
                    || (b.IsValueType && site.Operator is "==" or "!=")) return Typed();
                foreach (var p in candidates!.OperatorParameters)
                    if (ImplicitlyConvertible(b, p)) return Typed();
                return DynamicClassKey.Default;
            case DynamicRole.Convert:
            {
                var conversion = ((CSharpCompilation)Compilation).ClassifyConversion(b, site.ResultType!);
                if (b.IsReferenceType && (conversion.IsIdentity || conversion.IsReference))
                    return conversion.IsImplicit || site.Explicit ? DynamicClassKey.Reference : DynamicClassKey.ExplicitOnly;
                return conversion.Exists ? Typed() : DynamicClassKey.Default;
            }
            default:
                throw new InvalidOperationException(operand.Role.ToString());
        }
    }

    private bool ExplicitlyConvertible(ITypeSymbol from, ITypeSymbol to) =>
        to.SpecialType != SpecialType.System_Object && to.TypeKind is not (TypeKind.Dynamic or TypeKind.Error)
        && ((CSharpCompilation)Compilation).ClassifyConversion(from, to).Exists;

    private bool ImplicitlyConvertible(ITypeSymbol from, ITypeSymbol to)
    {
        if (to.SpecialType == SpecialType.System_Object || to.TypeKind is TypeKind.Dynamic or TypeKind.Error) return false;
        var conversion = ((CSharpCompilation)Compilation).ClassifyConversion(from, to);
        return conversion.Exists && conversion.IsImplicit;
    }

    /// <summary>The parameter types the dynamic arguments may be converted to, over every candidate member.</summary>
    private DynamicCandidates Candidates(DynamicSite site, List<DynamicClass>?[] classes)
    {
        var result = new DynamicCandidates();
        var receivers = new List<ITypeSymbol>();
        if (site.StaticReceiver is { } staticReceiver) receivers.Add(staticReceiver);
        if (site.Operands.Count > 0 && site.Operands[0].Role is DynamicRole.Receiver or DynamicRole.IndexReceiver or DynamicRole.InvokeReceiver)
        {
            var receiver = site.Operands[0];
            if (receiver.Kind == DynamicOperandKind.Dynamic)
            {
                foreach (var c in classes[0] ?? []) if (c.Key.Kind is DynamicClassKind.Typed or DynamicClassKind.MetaObject) receivers.Add(c.Key.Type!); // a meta-object's real members bind first
            }
            else if (receiver.Type is { } t) receivers.Add(t);
            else if (receiver.Kind == DynamicOperandKind.This && DynamicThisType(site) is { } self) receivers.Add(self);
        }
        void AddParameters(IEnumerable<IParameterSymbol> parameters, bool generic)
        {
            foreach (var p in parameters)
            {
                if (generic && ContainsMethodTypeParameter(p.Type)) result.Generic = true;
                var type = site.Subst.Apply(p.Type); // a generic type's members, as the instantiation has them
                result.Parameters.Add(type);
                if (p.IsParams && type is IArrayTypeSymbol array) result.Parameters.Add(array.ElementType);
            }
        }
        void AddMember(ISymbol member)
        {
            switch (member)
            {
                case IMethodSymbol m when m.IsGenericMethod && site.TypeArguments.Length == m.Arity:
                    AddParameters(m.Construct(site.TypeArguments).Parameters, false);
                    break;
                case IMethodSymbol m:
                    AddParameters(m.Parameters, m.IsGenericMethod && site.TypeArguments.Length == 0);
                    break;
                case IPropertySymbol { IsIndexer: true } indexer when site.Kind is DynamicKind.GetIndex or DynamicKind.SetIndex:
                    AddParameters(indexer.Parameters, false);
                    if (site.Kind == DynamicKind.SetIndex) result.Parameters.Add(indexer.Type);
                    break;
                case IPropertySymbol p when site.Kind == DynamicKind.SetMember:
                    result.Parameters.Add(p.Type);
                    break;
                case IFieldSymbol f when site.Kind == DynamicKind.SetMember:
                    result.Parameters.Add(f.Type);
                    break;
                case IPropertySymbol p when site.Kind == DynamicKind.InvokeMember && p.Type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke }:
                    AddParameters(invoke.Parameters, false);
                    break;
                case IFieldSymbol f when site.Kind == DynamicKind.InvokeMember && f.Type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke }:
                    AddParameters(invoke.Parameters, false);
                    break;
            }
        }
        switch (site.Kind)
        {
            case DynamicKind.InvokeMember or DynamicKind.SetMember when site.SimpleName:
                foreach (var member in DynamicSimpleNameLookup(site)) AddMember(member);
                break;
            case DynamicKind.InvokeMember or DynamicKind.SetMember:
                foreach (var r in receivers) foreach (var member in MembersNamed(r, site.Name!)) AddMember(member);
                break;
            case DynamicKind.GetIndex or DynamicKind.SetIndex:
                foreach (var r in receivers)
                {
                    if (r is IArrayTypeSymbol array)
                    {
                        foreach (var special in new[] { SpecialType.System_Int32, SpecialType.System_UInt32, SpecialType.System_Int64, SpecialType.System_UInt64 })
                            result.Parameters.Add(Compilation.GetSpecialType(special));
                        if (site.Kind == DynamicKind.SetIndex) result.Parameters.Add(array.ElementType);
                    }
                    else foreach (var member in AllMembers(r)) AddMember(member);
                }
                break;
            case DynamicKind.Invoke:
                foreach (var r in receivers)
                    if (r is INamedTypeSymbol { DelegateInvokeMethod: { } invoke }) AddParameters(invoke.Parameters, false);
                break;
            case DynamicKind.New:
                if (site.ResultType is INamedTypeSymbol created)
                    foreach (var ctor in created.InstanceConstructors) AddParameters(ctor.Parameters, false);
                break;
        }
        // Operators: the parameter types of every user-defined operator a value could meet.
        if (site.Kind is DynamicKind.Binary or DynamicKind.Unary or DynamicKind.IsTrue or DynamicKind.IsFalse)
        {
            var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            void AddOperators(ITypeSymbol type)
            {
                for (var t = type as INamedTypeSymbol; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                {
                    if (!seen.Add(t)) return;
                    foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
                        if (m.MethodKind == MethodKind.UserDefinedOperator)
                            foreach (var p in m.Parameters) result.OperatorParameters.Add(p.Type);
                }
            }
            foreach (var (type, _) in DynamicRuntimeTypes()) if (HasUserOperators(type)) AddOperators(type);
            foreach (var o in site.Operands) if (o.Type is { } t) AddOperators(t);
        }
        return result;
    }

    private static bool ContainsMethodTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } => true,
        IArrayTypeSymbol a => ContainsMethodTypeParameter(a.ElementType),
        INamedTypeSymbol n => n.TypeArguments.Any(ContainsMethodTypeParameter),
        _ => false,
    };

    /// <summary>Members called <paramref name="name"/> of a type and its base types (arrays: System.Array's).</summary>
    internal IEnumerable<ISymbol> MembersNamed(ITypeSymbol type, string name)
    {
        for (var t = type is IArrayTypeSymbol ? Compilation.GetSpecialType(SpecialType.System_Array) : type as INamedTypeSymbol; t is not null; t = t.BaseType)
            foreach (var m in t.GetMembers(name)) yield return m;
        if (type.IsValueType || type is IArrayTypeSymbol) yield break;
    }

    private IEnumerable<ISymbol> AllMembers(ITypeSymbol type)
    {
        for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
            foreach (var m in t.GetMembers()) yield return m;
    }

    private bool HasMember(ITypeSymbol type, string name) => MembersNamed(type, name).Any();

    /// <summary>
    /// The class a receiver binds as: the most derived one declaring a matching member that isn't an override.
    /// C# member lookup ignores overrides, so every type whose lookup stops at the same class binds to the same
    /// members, called virtually: one case (object.ToString for every type) instead of one per run-time type,
    /// each pulling in that type's own ToString. A struct binds as itself when it declares the member (its
    /// calls mutate the box); else as the reference type declaring it (the box is that object).
    /// </summary>
    private ITypeSymbol LookupLevel(ITypeSymbol type, Func<ISymbol, bool> matches)
    {
        for (var t = type is IArrayTypeSymbol ? Compilation.GetSpecialType(SpecialType.System_Array) : type as INamedTypeSymbol; t is not null; t = t.BaseType)
            if (t.GetMembers().Any(m => matches(m) && !m.IsOverride && m is not IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor }))
                // Only System.Object's members are shared: binding at another base (MemberInfo for Type.Name)
                // would call through references csharpc's runtime types may not support.
                return t.SpecialType == SpecialType.System_Object ? t : type;
        return type;
    }

    private static bool HasIndexer(ITypeSymbol type)
    {
        for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
            if (t.GetMembers().Any(m => m is IPropertySymbol { IsIndexer: true })) return true;
        return false;
    }

    private static bool HasUserOperators(ITypeSymbol type)
    {
        for (var t = type as INamedTypeSymbol; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            if (t.GetMembers().Any(m => m is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion })) return true;
        return false;
    }

    internal static bool IsMetaObjectProvider(ITypeSymbol type) =>
        type.AllInterfaces.Any(i => i.Name == "IDynamicMetaObjectProvider" && i.ContainingNamespace?.ToDisplayString() == "System.Dynamic");

    /// <summary>
    /// The binder's "best accessible type" (Microsoft.CSharp TypeManager.GetBestAccessibleType): the run-time type
    /// if the calling context can see it, else its nearest accessible base (arrays: of the best accessible element
    /// type, else System.Array) — "something the user could have written". csharpc's own corelib types stand in for
    /// framework internals, which user code can't see either.
    /// </summary>
    internal ITypeSymbol BestAccessibleType(ITypeSymbol type, ISymbol? context)
    {
        if (IsAccessibleForDynamic(type, context)) return type;
        switch (type)
        {
            case IArrayTypeSymbol array:
                if (array.ElementType.IsReferenceType)
                {
                    var element = BestAccessibleType(array.ElementType, context);
                    var candidate = Compilation.CreateArrayTypeSymbol(element, array.Rank);
                    if (IsAccessibleForDynamic(candidate, context)) return candidate;
                }
                return Compilation.GetSpecialType(SpecialType.System_Array);
            case INamedTypeSymbol named:
                for (var b = named.BaseType; b is not null; b = b.BaseType)
                    if (IsAccessibleForDynamic(b, context)) return b;
                return Compilation.GetSpecialType(SpecialType.System_Object);
            default:
                return Compilation.GetSpecialType(SpecialType.System_Object);
        }
    }

    private bool IsAccessibleForDynamic(ITypeSymbol type, ISymbol? context)
    {
        switch (type)
        {
            case IArrayTypeSymbol array: return IsAccessibleForDynamic(array.ElementType, context);
            case INamedTypeSymbol named:
                if (CoreLib.IsCoreLib(named)) return false;
                foreach (var argument in named.TypeArguments) if (!IsAccessibleForDynamic(argument, context)) return false;
                if (named.ContainingType is { } outer && !IsAccessibleForDynamic(outer, context)) return false;
                return context is null ? Compilation.IsSymbolAccessibleWithin(named.OriginalDefinition, DynamicDefaultWithin)
                    : Compilation.IsSymbolAccessibleWithin(named.OriginalDefinition, context);
            default: return true;
        }
    }

    // ======================================================================================
    // Binding one case
    // ======================================================================================

    private const string Local = "__csdyn";
    private const string ResultLocal = "__csdynr";

    /// <summary>Binds <paramref name="site"/> for one case per dynamic operand (null for operands that aren't dynamic).</summary>
    internal DynamicLeaf BindDynamicLeaf(DynamicSite site, DynamicClass?[] chosen)
    {
        if (chosen.Any(c => c?.Key.Kind == DynamicClassKind.Unsupported))
            return new DynamicLeaf { Unsupported = $"the dynamic call '{site.Syntax}' with an argument whose run-time type csharpc did not instantiate the generic method for" };
        // Receivers: null, an unknown member, the meta-object protocol, IsEvent.
        if (site.Operands.Count > 0 && site.Operands[0] is { Kind: DynamicOperandKind.Dynamic } receiver
            && receiver.Role is DynamicRole.Receiver or DynamicRole.IndexReceiver or DynamicRole.InvokeReceiver)
        {
            var c = chosen[0]!;
            switch (c.Key.Kind)
            {
                case DynamicClassKind.Null:
                    return new DynamicLeaf { Error = BinderNullReference };
                case DynamicClassKind.Default:
                    return receiver.Role switch
                    {
                        DynamicRole.Receiver => new DynamicLeaf { ErrorTemplate = ["'", ~0, $"' does not contain a definition for '{site.Name}'"] },
                        DynamicRole.IndexReceiver => new DynamicLeaf { ErrorTemplate = ["Cannot apply indexing with [] to an expression of type '", 0, "'"] },
                        _ => new DynamicLeaf { Error = BinderNonDelegate },
                    };
                case DynamicClassKind.MetaObject:
                    return BindMetaObject(site, chosen, c.Key.Type!);
            }
            if (site.Kind == DynamicKind.IsEvent)
                return new DynamicLeaf { ConstantBool = DynamicLookup(site, (INamespaceOrTypeSymbol)NamedOrObject(c.Key.Type!), site.Name).FirstOrDefault() is IEventSymbol };
        }
        if (site.Kind == DynamicKind.IsEvent)
        {
            var type = site.Operands[0].Type ?? site.Context;
            return new DynamicLeaf { ConstantBool = type is not null && DynamicLookup(site, (INamespaceOrTypeSymbol)NamedOrObject(type), site.Name).FirstOrDefault() is IEventSymbol };
        }
        // Conversions with no binding of their own.
        if (site.Kind == DynamicKind.Convert && chosen[0] is { } converted)
        {
            switch (converted.Key.Kind)
            {
                case DynamicClassKind.Default:
                    return new DynamicLeaf { ErrorTemplate = [site.Explicit ? "Cannot convert type '" : "Cannot implicitly convert type '", 0, $"' to '{BinderTypeName(site.ResultType!)}'"] };
                case DynamicClassKind.ExplicitOnly:
                    return new DynamicLeaf { ErrorTemplate = ["Cannot implicitly convert type '", 0, $"' to '{BinderTypeName(site.ResultType!)}'. An explicit conversion exists (are you missing a cast?)"] };
                case DynamicClassKind.MetaObject:
                    return BindMetaObject(site, chosen, converted.Key.Type!);
            }
        }
        if (site.Kind is DynamicKind.Binary or DynamicKind.Unary or DynamicKind.IsTrue or DynamicKind.IsFalse
            && chosen[0] is { Key.Kind: DynamicClassKind.MetaObject } meta)
            return BindMetaObject(site, chosen, meta.Key.Type!);
        return BindSpeculatively(site, chosen);
    }

    private ITypeSymbol NamedOrObject(ITypeSymbol type) => type is INamedTypeSymbol ? type : Compilation.GetSpecialType(SpecialType.System_Object);

    /// <summary>The type an operand is bound as in this case (null: the null literal or a constant).</summary>
    private ITypeSymbol? BindingType(DynamicOperand operand, DynamicClass? c) => operand.Kind switch
    {
        DynamicOperandKind.Dynamic => c!.Key.Kind switch
        {
            DynamicClassKind.Typed => c.Key.Type,
            DynamicClassKind.Null => null,
            _ => Compilation.GetSpecialType(SpecialType.System_Object),
        },
        DynamicOperandKind.Static => operand.Type,
        _ => null,
    };

    private enum SimpleReceiver { None, StaticType, ThisLocal }

    /// <summary>The C# text of a site for one case: operand declarations, operand expressions, the operation.</summary>
    private sealed record DynamicText(string[] Texts, string Declarations, bool Jump, string Expression, Func<int, int, bool, string> Arguments);

    private DynamicLeaf BindSpeculatively(DynamicSite site, DynamicClass?[] chosen)
    {
        if (DynamicExpressionText(site, chosen, out var unsupported) is not { } text) return unsupported!;
        var (declarations, needsJump, expression) = (text.Declarations, text.Jump, text.Expression);
        string resultDeclaration = site.Kind switch
        {
            DynamicKind.Convert => $"{TypeText(site.ResultType!)} {ResultLocal} = {expression};",
            DynamicKind.IsTrue or DynamicKind.IsFalse => $"bool {ResultLocal} = {expression};",
            _ when site.ResultDiscarded && site.Kind is DynamicKind.InvokeMember or DynamicKind.SetMember or DynamicKind.SetIndex or DynamicKind.Invoke => $"{expression};",
            _ => $"var {ResultLocal} = {expression};",
        };
        var leaf = BindText(site, chosen, declarations, needsJump, resultDeclaration, expression);
        // `var r = Void();`: the method returns nothing — fine when the value isn't used.
        if (leaf.Error == VoidMarker)
        {
            if (!site.ResultDiscarded) return new DynamicLeaf { Error = BinderVoidResult };
            leaf = BindText(site, chosen, declarations, needsJump, expression + ";", expression);
        }
        return leaf;
    }

    private DynamicText? DynamicExpressionText(DynamicSite site, DynamicClass?[] chosen, out DynamicLeaf? unsupported)
    {
        unsupported = null;
        var operands = site.Operands;
        var texts = new string[operands.Count];
        var declarations = new StringBuilder();
        var needsJump = false;
        // A simple name inside a generic type binds, at this position, to the generic definition's members; the
        // binder binds in the instantiation: name the instantiation explicitly (all candidates static: the type;
        // all instance: `this` as a local of that type).
        var simpleReceiver = SimpleReceiver.None;
        INamedTypeSymbol? simpleOwner = null;
        // A struct's `this` (passed by reference) is always a local aliasing the instance.
        var structThis = operands.Count > 0 && operands[0] is { Kind: DynamicOperandKind.This, RefKind: not RefKind.None };
        // From IL there is no position inside the context: always name the receiver.
        if (site.SimpleName && site.Context is { } context
            && (structThis || site.FromIl || (!site.Subst.IsIdentity && TypeSubstitution.IsOpen(context.OriginalDefinition)))
            && DynamicSimpleNameLookup(site).OfType<IMethodSymbol>().ToList() is { Count: > 0 } candidates)
        {
            if (candidates.All(m => m.IsStatic) && candidates.All(m => SymbolEqualityComparer.Default.Equals(m.ContainingType, candidates[0].ContainingType)))
            {
                simpleReceiver = SimpleReceiver.StaticType;
                simpleOwner = candidates[0].ContainingType;
            }
            else if (candidates.All(m => !m.IsStatic) && operands.Count > 0 && operands[0].Kind == DynamicOperandKind.This)
                simpleReceiver = SimpleReceiver.ThisLocal;
        }
        if (site.FromIl && site.SimpleName && simpleReceiver == SimpleReceiver.None)
        {
            unsupported = new DynamicLeaf { Unsupported = $"the simple-name dynamic call '{site.Syntax}' to static and instance overloads at once, from IL" };
            return null;
        }
        if (structThis && simpleReceiver == SimpleReceiver.None)
        {
            unsupported = new DynamicLeaf { Unsupported = $"the dynamic call '{site.Syntax}' through a struct's 'this' to static and instance overloads at once" };
            return null;
        }
        for (var i = 0; i < operands.Count; i++)
        {
            var o = operands[i];
            switch (o.Kind)
            {
                case DynamicOperandKind.This when simpleReceiver == SimpleReceiver.ThisLocal:
                    // A generic type's instance member, called through `this` typed as the instantiation.
                    texts[i] = Local + i;
                    declarations.Append(TypeText(site.Subst.Apply(o.Type ?? site.Context!))).Append(' ').Append(Local).Append(i).Append(';');
                    break;
                case DynamicOperandKind.This:
                    texts[i] = "this";
                    break;
                case DynamicOperandKind.Constant:
                    texts[i] = ConstantText(o.Constant, o.Type);
                    break;
                case DynamicOperandKind.Dynamic when chosen[i]!.Key.Kind == DynamicClassKind.Null:
                    texts[i] = "null";
                    break;
                default:
                {
                    var type = BindingType(o, chosen[i])!;
                    texts[i] = Local + i;
                    if ((TypeText(type) ?? (site.FromIl ? IlUnspeakableTypeText(type) : null)) is { } typeText) declarations.Append(typeText).Append(' ').Append(Local).Append(i).Append(';');
                    else if (AnonymousInitializer(type) is { } init)
                    {
                        needsJump = true;
                        declarations.Append("var ").Append(Local).Append(i).Append(" = ").Append(init).Append(';');
                    }
                    else
                    {
                        unsupported = new DynamicLeaf { Unsupported = $"a dynamic operation on a value of type '{type.ToDisplayString()}' (a type csharpc can't name) is not supported by csharpc yet" };
                        return null;
                    }
                    break;
                }
            }
        }

        // Arguments as C# text; `dynamic`: dynamic operands cast back to dynamic (a nested dynamic call binds them by run-time type).
        string ArgumentList(int start, int end, bool dynamic)
        {
            var sb = new StringBuilder();
            for (var i = start; i < end; i++)
            {
                if (i > start) sb.Append(", ");
                var o = operands[i];
                if (o.Name is { } name) sb.Append('@').Append(name).Append(": ");
                sb.Append(o.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", _ => "" });
                sb.Append(dynamic && o.Kind == DynamicOperandKind.Dynamic && texts[i] != "null" ? "(dynamic)" + texts[i] : texts[i]);
            }
            return sb.ToString();
        }
        string Arguments(int start, int end) => ArgumentList(start, end, false);
        var hasReceiver = operands.Count > 0 && operands[0].Role is DynamicRole.Receiver or DynamicRole.IndexReceiver or DynamicRole.InvokeReceiver;
        var argStart = hasReceiver ? 1 : 0;
        var typeArguments = site.TypeArguments.Length == 0 ? "" : "<" + string.Join(", ", site.TypeArguments.Select(t => TypeText(t) ?? "object")) + ">";
        string Receiver() => site.StaticReceiver is { } s ? TypeText(s)! + "."
            : !site.SimpleName ? Parenthesized(texts[0]) + "."
            : simpleReceiver switch
            {
                SimpleReceiver.StaticType => TypeText(site.Subst.Apply(simpleOwner!))! + ".",
                SimpleReceiver.ThisLocal => texts[0] + ".",
                _ => "",
            };
        string expression;
        switch (site.Kind)
        {
            case DynamicKind.InvokeMember:
                expression = $"{Receiver()}@{site.Name}{typeArguments}({Arguments(argStart, operands.Count)})";
                break;
            case DynamicKind.GetMember:
                expression = $"{Receiver()}@{site.Name}";
                break;
            case DynamicKind.SetMember:
                expression = $"{Receiver()}@{site.Name} {(site.Compound ? site.Operator : "")}= {CompoundValueText(site, declarations.ToString(), needsJump, $"{Receiver()}@{site.Name}", texts[^1])}";
                break;
            case DynamicKind.GetIndex:
                expression = $"{Parenthesized(texts[0])}[{Arguments(1, operands.Count)}]";
                break;
            case DynamicKind.SetIndex:
            {
                var access = $"{Parenthesized(texts[0])}[{Arguments(1, operands.Count - 1)}]";
                expression = $"{access} = {CompoundValueText(site, declarations.ToString(), needsJump, access, texts[^1])}";
                break;
            }
            case DynamicKind.Invoke:
                expression = $"{Parenthesized(texts[0])}({Arguments(1, operands.Count)})";
                break;
            case DynamicKind.New:
                expression = $"new {TypeText(site.ResultType!)}({Arguments(0, operands.Count)})";
                break;
            case DynamicKind.Binary:
                // `x op= y` is the binder's plain `x op y` (Microsoft.CSharp maps AddAssign to Add): its result keeps its
                // type (a dynamic variable gets it as is), and the store converts it back (CompoundValue, csc's explicit Convert).
                expression = $"{Parenthesized(texts[0])} {site.Operator} {Parenthesized(texts[1])}";
                break;
            case DynamicKind.Unary:
                expression = $"{site.Operator}{Parenthesized(texts[0])}";
                break;
            case DynamicKind.Convert:
                expression = site.Explicit || chosen[0]?.Key.Kind == DynamicClassKind.Reference ? $"({TypeText(site.ResultType!)}){Parenthesized(texts[0])}" : texts[0];
                break;
            case DynamicKind.IsTrue:
                expression = $"{texts[0]} ? true : false";
                break;
            case DynamicKind.IsFalse:
                expression = $"{texts[0]} ? false : true";
                break;
            default:
                throw new InvalidOperationException(site.Kind.ToString());
        }
        if (site.Checked && site.Kind is DynamicKind.Binary or DynamicKind.Unary or DynamicKind.Convert) expression = $"checked({expression})";
        return new DynamicText(texts, declarations.ToString(), needsJump, expression, ArgumentList);
    }

    private const string VoidMarker = "\u0000void";

    private static string Parenthesized(string text) => text.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '@') ? text : "(" + text + ")";

    private DynamicLeaf BindText(DynamicSite site, DynamicClass?[] chosen, string declarations, bool jump, string resultStatement, string expressionText)
    {
        var text = jump
            ? $"{{ goto __csdynL; {declarations} __csdynL: ; {resultStatement} }}"
            : $"{{ {declarations} {resultStatement} }}";
        var block = IlSpeakable(site, (BlockSyntax)SyntaxFactory.ParseStatement(text));
        if (block.ContainsDiagnostics)
            throw CompileError.Unsupported($"dynamic operation csharpc can't express ({text})");
        if (!site.Model.TryGetSpeculativeSemanticModel(site.Position, block, out var model))
            throw CompileError.Unsupported("dynamic operation in this position (no speculative binding)");
        NoteDynamicOrigin(block.SyntaxTree, site);

        var locals = new ILocalSymbol?[site.Operands.Count];
        ILocalSymbol? result = null;
        ExpressionSyntax? expression = null;
        foreach (var declarator in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var name = declarator.Identifier.ValueText;
            if (name == ResultLocal)
            {
                result = (ILocalSymbol?)model.GetDeclaredSymbol(declarator);
                expression = declarator.Initializer!.Value;
            }
            else if (name.StartsWith(Local, StringComparison.Ordinal) && int.TryParse(name.AsSpan(Local.Length), out var index))
                locals[index] = (ILocalSymbol?)model.GetDeclaredSymbol(declarator);
        }
        expression ??= block.Statements.OfType<ExpressionStatementSyntax>().Last().Expression;
        // A `checked(...)` expression has no operation of its own.
        var operation = model.GetOperation(expression) ?? model.GetOperation(Unwrapped(expression));

        if (result is { Type: IErrorTypeSymbol } && operation?.Type is { SpecialType: SpecialType.System_Void })
            return new DynamicLeaf { Error = VoidMarker };
        var error = DescribeBindingError(site, chosen, model, expression, operation, result);
        if (error is not null) return IlCheckedLeaf(site, chosen, error, null);
        if (site.FromIl && IlCheckedLeaf(site, chosen, new DynamicLeaf(), model.GetOperation(block)) is { Unsupported: not null } inaccessible) return inaccessible;

        var leaf = new DynamicLeaf { Locals = locals, Result = result };
        // ==/!= bound as object for a reference type: valid only if one operand converts to the other (else CS0019).
        if (site.Kind == DynamicKind.Binary && site.Operator is "==" or "!=" && !site.Compound
            && operation is IBinaryOperation { OperatorMethod: null } binary && binary.LeftOperand.Type is not null)
        {
            for (var i = 0; i < 2; i++)
                if (chosen[i] is { Key.Kind: DynamicClassKind.Default } && site.Operands[1 - i] is var other
                    && (other.Kind == DynamicOperandKind.Dynamic ? chosen[1 - i]!.Key.Kind != DynamicClassKind.Null : other.Type is { IsReferenceType: true }))
                {
                    leaf.ReferenceChecks ??= [];
                    leaf.ReferenceChecks.Add((i, 1 - i));
                }
        }
        leaf.Graph = ControlFlowGraph.Create(block, model);
        return leaf;
    }

    // ======================================================================================
    // Binder errors
    // ======================================================================================

    /// <summary>The RuntimeBinderException the binder throws for this binding, or null when it binds.</summary>
    private DynamicLeaf? DescribeBindingError(DynamicSite site, DynamicClass?[] chosen, SemanticModel model, ExpressionSyntax expression, IOperation? operation, ILocalSymbol? result)
    {
        var hasErrors = operation is null || HasBindingErrors(operation) || result?.Type is IErrorTypeSymbol;
        // `int r = longValue;`: the declaration's conversion isn't in the operation tree.
        if (result is not null && site.Kind is DynamicKind.Convert or DynamicKind.IsTrue or DynamicKind.IsFalse
            && model.GetConversion(expression) is { } declared && !(declared.Exists && (declared.IsImplicit || site.Explicit)))
            hasErrors = true;
        string OperandName(int i) => site.Operands[i].Kind switch
        {
            DynamicOperandKind.Dynamic => chosen[i]!.Key.Kind switch
            {
                DynamicClassKind.Null => "<null>",
                DynamicClassKind.Typed => BinderTypeName(chosen[i]!.Key.Type!),
                _ => "\u0001" + i,
            },
            DynamicOperandKind.Constant => site.Operands[i].Type is { } ct ? BinderTypeName(ct) : "<null>",
            DynamicOperandKind.Static => BinderTypeName(site.Operands[i].Type!),
            _ => site.Context is { } c ? BinderTypeName(c) : "object",
        };
        DynamicLeaf Fail(string message)
        {
            if (message.StartsWith('\u0002')) return new DynamicLeaf { Unsupported = message[1..] };
            if (!message.Contains('\u0001')) return new DynamicLeaf { Error = message };
            // Operands bound as object in the default case: their run-time type names, at run time.
            var parts = new List<object>();
            var start = 0;
            for (var i = message.IndexOf('\u0001'); i >= 0; i = message.IndexOf('\u0001', start))
            {
                parts.Add(message[start..i]);
                parts.Add(message[i + 1] - '0');
                start = i + 2;
            }
            parts.Add(message[start..]);
            return new DynamicLeaf { ErrorTemplate = parts };
        }
        var argStart = site.Operands.Count > 0 && site.Operands[0].Role is DynamicRole.Receiver or DynamicRole.IndexReceiver or DynamicRole.InvokeReceiver ? 1 : 0;
        int ArgumentCount(int end) => end - argStart;

        switch (site.Kind)
        {
            case DynamicKind.InvokeMember:
            {
                var info = model.GetSymbolInfo(expression);
                if (info.Symbol is IMethodSymbol method && !hasErrors)
                {
                    if (method.ReducedFrom is null) return null;
                    // Extension methods aren't part of dynamic binding: what the binder says about the type's own members.
                    return Fail(OverloadFailure(site, model, expression, MembersNamed(ReceiverType(site, chosen) ?? method.ReceiverType!, site.Name!).OfType<IMethodSymbol>().ToList()));
                }
                var candidates = info.Symbol is { } s ? [s] : info.CandidateSymbols.ToList();
                if (candidates.Count == 0 && ReceiverType(site, chosen) is { } r) candidates = [.. MembersNamed(r, site.Name!)];
                return Fail(info.CandidateReason switch
                {
                    CandidateReason.Inaccessible => $"'{BinderMemberName(candidates[0])}' is inaccessible due to its protection level",
                    CandidateReason.StaticInstanceMismatch when candidates[0].IsStatic => $"Member '{BinderMemberName(candidates[0])}' cannot be accessed with an instance reference; qualify it with a type name instead",
                    CandidateReason.StaticInstanceMismatch => $"An object reference is required for the non-static field, method, or property '{BinderMemberName(candidates[0])}'",
                    CandidateReason.NotInvocable => $"Non-invocable member '{BinderMemberName(candidates[0])}' cannot be used like a method.",
                    CandidateReason.Ambiguous when candidates.Count >= 2 => $"The call is ambiguous between the following methods or properties: '{BinderMemberName(candidates[0])}' and '{BinderMemberName(candidates[1])}'",
                    _ when candidates.OfType<IMethodSymbol>().Any() => OverloadFailure(site, model, expression, candidates.OfType<IMethodSymbol>().ToList()),
                    _ when candidates.Count > 0 && candidates[0] is not IMethodSymbol => $"Non-invocable member '{BinderMemberName(candidates[0])}' cannot be used like a method.",
                    _ => $"'{OperandName(0)}' does not contain a definition for '{site.Name}'",
                });
            }
            case DynamicKind.GetMember or DynamicKind.SetMember:
            {
                var access = site.Kind == DynamicKind.SetMember ? ((AssignmentExpressionSyntax)expression).Left : expression;
                var info = model.GetSymbolInfo(access);
                var member = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                if (info.CandidateReason == CandidateReason.Inaccessible && member is not null)
                    return Fail($"'{BinderMemberName(member)}' is inaccessible due to its protection level");
                if (info.CandidateReason == CandidateReason.StaticInstanceMismatch && member is not null)
                    return Fail(member.IsStatic
                        ? $"Member '{BinderMemberName(member)}' cannot be accessed with an instance reference; qualify it with a type name instead"
                        : $"An object reference is required for the non-static field, method, or property '{BinderMemberName(member)}'");
                if (member is IMethodSymbol || info.CandidateReason == CandidateReason.MemberGroup)
                    return Fail(site.Kind == DynamicKind.GetMember
                        ? $"The name '{site.Name}' is bound to a method and cannot be used like a property"
                        : "The left-hand side of an assignment must be a variable, property or indexer");
                if (site.Kind == DynamicKind.GetMember)
                {
                    if (member is IPropertySymbol { GetMethod: null } p) return Fail($"The property or indexer '{BinderMemberName(p)}' cannot be used in this context because it lacks the get accessor");
                    if (member is IPropertySymbol { GetMethod: { } getter } gp && !DynamicAccessible(site, getter, ReceiverType(site, chosen)))
                        return Fail($"The property or indexer '{BinderMemberName(gp)}' cannot be used in this context because the get accessor is inaccessible");
                    if (member is IEventSymbol ev) return Fail($"The event '{BinderMemberName(ev)}' can only appear on the left hand side of +");
                }
                else if (!site.Compound)
                {
                    var setError = SetterError(site, model, member);
                    if (setError is not null) return Fail(setError);
                    if (CompoundCastError(site, chosen, model, ((AssignmentExpressionSyntax)expression).Right, OperandName) is { } castError) return Fail(castError);
                    if (ConversionError(site, model, ((AssignmentExpressionSyntax)expression).Right, OperandName, site.Operands.Count - 1) is { } conversionError) return Fail(conversionError);
                }
                if (!hasErrors) return null;
                if (member is null) return Fail($"'{OperandName(0)}' does not contain a definition for '{site.Name}'");
                return Fail(UnexpectedBindingError(site));
            }
            case DynamicKind.GetIndex or DynamicKind.SetIndex:
            {
                var access = site.Kind == DynamicKind.SetIndex ? (ElementAccessExpressionSyntax)((AssignmentExpressionSyntax)expression).Left : (ElementAccessExpressionSyntax)expression;
                var receiverType = BindingType(site.Operands[0], chosen[0]);
                var argumentEnd = site.Kind == DynamicKind.SetIndex ? site.Operands.Count - 1 : site.Operands.Count;
                if (receiverType is IArrayTypeSymbol array && ArgumentCount(argumentEnd) != array.Rank)
                    return Fail($"Wrong number of indices inside []; expected '{array.Rank}'");
                var info = model.GetSymbolInfo(access);
                var indexer = info.Symbol as IPropertySymbol;
                if (site.Kind == DynamicKind.SetIndex && indexer is not null && SetterError(site, model, indexer) is { } setError) return Fail(setError);
                if (site.Kind == DynamicKind.SetIndex && CompoundCastError(site, chosen, model, ((AssignmentExpressionSyntax)expression).Right, OperandName) is { } castError) return Fail(castError);
                if (site.Kind == DynamicKind.SetIndex && ConversionError(site, model, ((AssignmentExpressionSyntax)expression).Right, OperandName, site.Operands.Count - 1) is { } conversionError) return Fail(conversionError);
                if (!hasErrors) return null;
                if (receiverType is IArrayTypeSymbol)
                {
                    for (var i = 1; i < argumentEnd; i++)
                        if (ConversionError(site, model, access.ArgumentList.Arguments[i - 1].Expression, OperandName, i, "int") is { } indexError) return Fail(indexError);
                    return Fail(UnexpectedBindingError(site));
                }
                var indexers = info.CandidateSymbols.OfType<IPropertySymbol>().ToList();
                if (indexers.Count == 0 && receiverType is not null) indexers = AllMembers(receiverType).OfType<IPropertySymbol>().Where(p => p.IsIndexer).ToList();
                if (info.CandidateReason == CandidateReason.Inaccessible && indexers.Count > 0)
                    return Fail($"'{BinderMemberName(indexers[0])}' is inaccessible due to its protection level");
                if (info.CandidateReason == CandidateReason.Ambiguous && indexers.Count >= 2)
                    return Fail($"The call is ambiguous between the following methods or properties: '{BinderMemberName(indexers[0])}' and '{BinderMemberName(indexers[1])}'");
                var count = ArgumentCount(argumentEnd);
                var matching = indexers.FirstOrDefault(p => p.Parameters.Length == count);
                return Fail(matching is not null
                    ? $"The best overloaded method match for '{BinderMemberName(matching)}' has some invalid arguments"
                    : $"Wrong number of indices inside []; expected '{indexers.FirstOrDefault()?.Parameters.Length ?? 1}'");
            }
            case DynamicKind.Invoke:
            {
                if (!hasErrors) return null;
                var delegateType = BindingType(site.Operands[0], chosen[0]) as INamedTypeSymbol;
                var invoke = delegateType?.DelegateInvokeMethod;
                var count = ArgumentCount(site.Operands.Count);
                if (invoke is null) return Fail(BinderNonDelegate);
                return Fail(invoke.Parameters.Length != count
                    ? $"Delegate '{BinderTypeName(delegateType!)}' does not take {count} arguments"
                    : $"Delegate '{BinderTypeName(delegateType!)}' has some invalid arguments");
            }
            case DynamicKind.New:
            {
                if (!hasErrors) return null;
                var info = model.GetSymbolInfo(expression);
                var created = (INamedTypeSymbol)site.ResultType!;
                if (info.CandidateReason == CandidateReason.Inaccessible && info.CandidateSymbols.FirstOrDefault() is { } inaccessible)
                    return Fail($"'{BinderMemberName(inaccessible)}' is inaccessible due to its protection level");
                var ctors = created.InstanceConstructors.ToList();
                var count = site.Operands.Count;
                if (info.CandidateReason == CandidateReason.Ambiguous && info.CandidateSymbols.Length >= 2)
                    return Fail($"The call is ambiguous between the following methods or properties: '{BinderMemberName(info.CandidateSymbols[0])}' and '{BinderMemberName(info.CandidateSymbols[1])}'");
                var matching = ctors.Where(c => Accepts(c, count)).ToList();
                return Fail(matching.Count == 0
                    ? $"'{BinderTypeName(created)}' does not contain a constructor that takes {count} arguments"
                    : $"The best overloaded method match for '{BinderMemberName(matching[0])}' has some invalid arguments");
            }
            case DynamicKind.Binary or DynamicKind.Unary:
            {
                // Roslyn keeps an operator it can't resolve as an ordinary operation: its symbol tells.
                var info = model.GetSymbolInfo(Unwrapped(expression));
                if (!hasErrors && operation?.ConstantValue.HasValue == true) return null;
                if (!hasErrors && info.Symbol is IMethodSymbol op && OperandsConvert(model, Unwrapped(expression), op)) return null;
                var ambiguous = info.CandidateReason == CandidateReason.Ambiguous;
                if (site.Kind == DynamicKind.Binary)
                    return Fail(ambiguous
                        ? $"Operator '{site.MessageOperator}' is ambiguous on operands of type '{OperandName(0)}' and '{OperandName(1)}'"
                        : $"Operator '{site.MessageOperator}' cannot be applied to operands of type '{OperandName(0)}' and '{OperandName(1)}'");
                return Fail(ambiguous
                    ? $"Operator '{site.MessageOperator}' is ambiguous on an operand of type '{OperandName(0)}'"
                    : $"Operator '{site.MessageOperator}' cannot be applied to operand of type '{OperandName(0)}'");
            }
            case DynamicKind.Convert:
            {
                if (!hasErrors) return null;
                var target = BinderTypeName(site.ResultType!);
                if (chosen[0]!.Key.Kind == DynamicClassKind.Null)
                    return Fail(site.ResultType!.IsValueType && !TypeMapper.IsNullable(site.ResultType)
                        ? $"Cannot convert null to '{target}' because it is a non-nullable value type"
                        : UnexpectedBindingError(site));
                var source = chosen[0]!.Key.Type!;
                var conversion = ((CSharpCompilation)Compilation).ClassifyConversion(source, site.ResultType!);
                if (site.Explicit || !conversion.Exists) return Fail($"Cannot {(site.Explicit ? "" : "implicitly ")}convert type '{OperandName(0)}' to '{target}'");
                return Fail($"Cannot implicitly convert type '{OperandName(0)}' to '{target}'. An explicit conversion exists (are you missing a cast?)");
            }
            case DynamicKind.IsTrue or DynamicKind.IsFalse:
                if (!hasErrors) return null;
                if (chosen[0]!.Key.Kind == DynamicClassKind.Null) return Fail("Cannot convert null to 'bool' because it is a non-nullable value type");
                return Fail($"Cannot implicitly convert type '{OperandName(0)}' to 'bool'");
            default:
                return hasErrors ? Fail(UnexpectedBindingError(site)) : null;
        }
    }

    private static string UnexpectedBindingError(DynamicSite site) =>
        $"\u0002this dynamic operation fails to bind in a way csharpc can't describe yet ({site.Kind} '{site.Syntax}')";

    /// <summary>
    /// Each operand converts implicitly to the operator's parameter (or, lifted, to its nullable form). Roslyn
    /// names the operator of `decimal += object` though no conversion applies (CS0019 is only a diagnostic).
    /// </summary>
    private bool OperandsConvert(SemanticModel model, ExpressionSyntax expression, IMethodSymbol op)
    {
        ExpressionSyntax[] operands = expression switch
        {
            BinaryExpressionSyntax b => [b.Left, b.Right],
            AssignmentExpressionSyntax a => [a.Left, a.Right],
            PrefixUnaryExpressionSyntax u => [u.Operand],
            _ => [],
        };
        if (operands.Length != op.Parameters.Length) return true;
        for (var i = 0; i < operands.Length; i++)
        {
            var target = op.Parameters[i].Type;
            if (model.ClassifyConversion(operands[i], target) is { Exists: true, IsImplicit: true }) continue;
            if (target.IsValueType && !TypeMapper.IsNullable(target)
                && Compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(target) is var lifted
                && model.ClassifyConversion(operands[i], lifted) is { Exists: true, IsImplicit: true }) continue;
            // Shifts take an int count; a compound assignment's left operand may narrow back (byte += int).
            if (expression is AssignmentExpressionSyntax && i == 0) continue;
            return false;
        }
        return true;
    }

    private static ExpressionSyntax Unwrapped(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case CheckedExpressionSyntax c: expression = c.Expression; continue;
                case ParenthesizedExpressionSyntax p: expression = p.Expression; continue;
                default: return expression;
            }
        }
    }

    private ITypeSymbol? ReceiverType(DynamicSite site, DynamicClass?[] chosen)
    {
        if (site.StaticReceiver is { } s) return s;
        if (site.Operands.Count == 0) return null;
        var receiver = site.Operands[0];
        return receiver.Role is DynamicRole.Receiver ? receiver.Kind switch
        {
            DynamicOperandKind.Dynamic => BindingType(receiver, chosen[0]),
            DynamicOperandKind.This => DynamicThisType(site),
            _ => receiver.Type,
        } : null;
    }

    private string? SetterError(DynamicSite site, SemanticModel model, ISymbol? member) => member switch
    {
        IPropertySymbol { SetMethod: null } p =>
            $"Property or indexer '{BinderMemberName(p)}' cannot be assigned to -- it is read only",
        IPropertySymbol { SetMethod: { } setter } p when !DynamicAccessible(site, setter, null) =>
            $"The property or indexer '{BinderMemberName(p)}' cannot be used in this context because the set accessor is inaccessible",
        IFieldSymbol { IsReadOnly: true, IsStatic: true } => "A static readonly field cannot be assigned to (except in a static constructor or a variable initializer)",
        IFieldSymbol { IsReadOnly: true } => "A readonly field cannot be assigned to (except in a constructor or a variable initializer)",
        IFieldSymbol { IsConst: true } or IEventSymbol => "The left-hand side of an assignment must be a variable, property or indexer",
        _ => null,
    };

    /// <summary>
    /// The value of a compound assignment's store: `checked((T)value)` (or unchecked), T the type of the member or
    /// indexer stored to — bound first as a read. Not a compound store, or a target that doesn't read: the value.
    /// </summary>
    private string CompoundValueText(DynamicSite site, string declarations, bool jump, string access, string value)
    {
        if (!site.CompoundValue || site.Compound) return value;
        var text = jump
            ? $"{{ goto __csdynL; {declarations} __csdynL: ; var __csdynt = {access}; }}"
            : $"{{ {declarations} var __csdynt = {access}; }}";
        var block = IlSpeakable(site, (BlockSyntax)SyntaxFactory.ParseStatement(text));
        if (block.ContainsDiagnostics || !site.Model.TryGetSpeculativeSemanticModel(site.Position, block, out var model)) return value;
        var read = block.DescendantNodes().OfType<VariableDeclaratorSyntax>().Last().Initializer!.Value;
        if (model.GetTypeInfo(read).Type is not { TypeKind: not TypeKind.Error } type
            || (TypeText(type) ?? (site.FromIl ? IlUnspeakableTypeText(type) : null)) is not { } typeText) return value;
        return $"{(site.Checked ? "checked" : "unchecked")}(({typeText}){Parenthesized(value)})";
    }

    /// <summary>The binder's error for a compound store whose value doesn't convert (explicitly) to the member's type.</summary>
    private string? CompoundCastError(DynamicSite site, DynamicClass?[] chosen, SemanticModel model, ExpressionSyntax right, Func<int, string> operandName)
    {
        if (!site.CompoundValue || site.Compound || Unwrapped(right) is not CastExpressionSyntax cast) return null;
        if (model.GetTypeInfo(cast.Type).Type is not { TypeKind: not TypeKind.Error } target) return null;
        var value = site.Operands.Count - 1;
        var source = operandName(value);
        var targetName = BinderTypeName(target);
        if (source == "<null>")
            return target.IsValueType && !TypeMapper.IsNullable(target) ? $"Cannot convert null to '{targetName}' because it is a non-nullable value type" : null;
        // A value bound as object (its type converts to no member type) would unbox: the binder sees its run-time type.
        if (site.Operands[value].Kind == DynamicOperandKind.Dynamic && chosen[value]?.Key.Kind == DynamicClassKind.Default)
            return target.SpecialType == SpecialType.System_Object ? null : $"Cannot convert type '{source}' to '{targetName}'";
        var conversion = model.ClassifyConversion(cast.Expression, target, isExplicitInSource: true);
        if (!conversion.Exists) return $"Cannot convert type '{source}' to '{targetName}'";
        if (conversion.IsUserDefined) return "\u0002a compound assignment whose result converts to the member's type through a user-defined conversion";
        return null;
    }

    /// <summary>An argument that doesn't convert to its target (an assigned value, an array index).</summary>
    private string? ConversionError(DynamicSite site, SemanticModel model, ExpressionSyntax value, Func<int, string> operandName, int operand, string? target = null)
    {
        var conversion = model.GetConversion(value);
        var converted = model.GetTypeInfo(value).ConvertedType;
        if (conversion.Exists && conversion.IsImplicit) return null;
        var targetName = target ?? (converted is null or IErrorTypeSymbol ? null : BinderTypeName(converted));
        if (targetName is null) return null;
        var source = operandName(operand);
        if (source == "<null>") return $"Cannot convert null to '{targetName}' because it is a non-nullable value type";
        return conversion.Exists
            ? $"Cannot implicitly convert type '{source}' to '{targetName}'. An explicit conversion exists (are you missing a cast?)"
            : $"Cannot implicitly convert type '{source}' to '{targetName}'";
    }

    /// <summary>
    /// A call with no unique applicable method: Roslyn reports ambiguity, static/instance mismatches and bad
    /// arguments alike as an overload resolution failure, so tell them apart as the binder's messages do — no
    /// candidate takes the count; two applicable (ambiguous); the applicable one is static on an instance (or the
    /// reverse); else the first candidate taking the count "has some invalid arguments".
    /// </summary>
    private string OverloadFailure(DynamicSite site, SemanticModel model, ExpressionSyntax expression, List<IMethodSymbol> candidates)
    {
        var invocation = Unwrapped(expression) is AssignmentExpressionSyntax assignment ? Unwrapped(assignment.Right) : Unwrapped(expression);
        var arguments = invocation is InvocationExpressionSyntax inv ? inv.ArgumentList.Arguments : default;
        var count = arguments.Count;
        if (candidates.Count == 0) return UnexpectedBindingError(site);
        var matching = candidates.Where(c => Accepts(c, count)).ToList();
        if (matching.Count == 0) return $"No overload for method '{site.Name}' takes {count} arguments";
        foreach (var name in arguments.Select(a => a.NameColon?.Name.Identifier.ValueText).OfType<string>())
            if (!matching[0].Parameters.Any(p => p.Name == name)) return $"The best overload for '{site.Name}' does not have a parameter named '{name}'";
        var applicable = matching.Where(m => Applicable(model, m, arguments)).ToList();
        var instanceReceiver = !site.SimpleName && site.StaticReceiver is null;
        if (applicable.Count == 1 && applicable[0].IsStatic && instanceReceiver)
            return $"Member '{BinderMemberName(applicable[0])}' cannot be accessed with an instance reference; qualify it with a type name instead";
        if (applicable.Count == 1 && !applicable[0].IsStatic && !instanceReceiver)
            return $"An object reference is required for the non-static field, method, or property '{BinderMemberName(applicable[0])}'";
        if (applicable.Count >= 2)
            return $"The call is ambiguous between the following methods or properties: '{BinderMemberName(applicable[0])}' and '{BinderMemberName(applicable[1])}'";
        return $"The best overloaded method match for '{BinderMemberName(matching[0])}' has some invalid arguments";
    }

    /// <summary>Each argument converts implicitly to its parameter (normal or expanded form).</summary>
    private static bool Applicable(SemanticModel model, IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var parameters = method.Parameters;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            IParameterSymbol? parameter = argument.NameColon is { } nameColon
                ? parameters.FirstOrDefault(p => p.Name == nameColon.Name.Identifier.ValueText)
                : i < parameters.Length ? parameters[i] : parameters.LastOrDefault(p => p.IsParams);
            if (parameter is null) return false;
            var target = parameter.Type;
            var conversion = model.ClassifyConversion(argument.Expression, target);
            if (conversion.Exists && conversion.IsImplicit) continue;
            if (parameter.IsParams && target is IArrayTypeSymbol array && model.ClassifyConversion(argument.Expression, array.ElementType) is { Exists: true, IsImplicit: true })
                continue;
            return false;
        }
        return true;
    }

    private static bool Accepts(IMethodSymbol method, int count)
    {
        var parameters = method.Parameters;
        if (parameters.Length > 0 && parameters[^1].IsParams) return count >= parameters.Length - 1;
        var required = parameters.Count(p => !p.IsOptional);
        return count >= required && count <= parameters.Length;
    }

    private static bool HasBindingErrors(IOperation operation)
    {
        foreach (var op in operation.DescendantsAndSelf())
        {
            switch (op)
            {
                case IInvalidOperation:
                case IConversionOperation { Conversion.Exists: false }:
                // `decimal += string`: concatenation binds, its string result doesn't convert back.
                case ICompoundAssignmentOperation { OutConversion.Exists: false } or ICompoundAssignmentOperation { InConversion.Exists: false }:
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.None }:
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.None }:
                    return true;
            }
            if (op.Type is IErrorTypeSymbol) return true;
        }
        return false;
    }

    // ======================================================================================
    // Names as the binder prints them
    // ======================================================================================

    /// <summary>A type as Microsoft.CSharp's messages print it: keywords, `Ns.Outer.Inner`, `Name&lt;A,B&gt;` (no spaces).</summary>
    internal string BinderTypeName(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return BinderTypeName(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
            case ITypeParameterSymbol p:
                return p.Name;
            case IPointerTypeSymbol pointer:
                return BinderTypeName(pointer.PointedAtType) + "*";
            case INamedTypeSymbol named:
            {
                if (Keyword(named.SpecialType) is { } keyword) return keyword;
                if (TypeMapper.IsNullable(named)) return BinderTypeName(named.TypeArguments[0]) + "?";
                if (named.TypeKind == TypeKind.Dynamic) return "dynamic";
                if (named.IsAnonymousType)
                {
                    var index = _anonymous.TryGetValue(named, out var layout) ? _anonymous.Values.ToList().IndexOf(layout) : 0;
                    var properties = named.GetMembers().OfType<IPropertySymbol>().Select(p => BinderTypeName(p.Type));
                    return "<>f__AnonymousType" + index + "<" + string.Join(",", properties) + ">";
                }
                var definition = named.IsTupleType && named.TupleUnderlyingType is { } underlying ? underlying : named;
                var prefix = definition.ContainingType is { } outer ? BinderTypeName(outer) + "."
                    : definition.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." : "";
                var arguments = definition.TypeArguments.Length == 0 ? "" : "<" + string.Join(",", definition.TypeArguments.Select(BinderTypeName)) + ">";
                return prefix + definition.Name + arguments;
            }
            default:
                return type.ToDisplayString();
        }
    }

    private static string? Keyword(SpecialType special) => special switch
    {
        SpecialType.System_Object => "object", SpecialType.System_String => "string", SpecialType.System_Boolean => "bool",
        SpecialType.System_Char => "char", SpecialType.System_SByte => "sbyte", SpecialType.System_Byte => "byte",
        SpecialType.System_Int16 => "short", SpecialType.System_UInt16 => "ushort", SpecialType.System_Int32 => "int",
        SpecialType.System_UInt32 => "uint", SpecialType.System_Int64 => "long", SpecialType.System_UInt64 => "ulong",
        SpecialType.System_Single => "float", SpecialType.System_Double => "double", SpecialType.System_Decimal => "decimal",
        SpecialType.System_Void => "void",
        _ => null,
    };

    /// <summary>A member as the binder's messages print it: `Type.Name(int, string)`, `Type.Type(int)`, `Type.this[int]`.</summary>
    internal string BinderMemberName(ISymbol member)
    {
        var container = member.ContainingType is { } t ? BinderTypeName(t) : "";
        string Parameters(IEnumerable<IParameterSymbol> parameters) => string.Join(", ", parameters.Select(p =>
            (p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", _ => "" }) + (p.IsParams ? "params " : "") + BinderTypeName(p.Type)));
        return member switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } ctor => $"{container}.{ctor.ContainingType.Name}({Parameters(ctor.Parameters)})",
            IMethodSymbol m => $"{container}.{m.Name}{(m.IsGenericMethod ? "<" + string.Join(",", m.TypeParameters.Select(p => p.Name)) + ">" : "")}({Parameters(m.Parameters)})",
            IPropertySymbol { IsIndexer: true } indexer => $"{container}.this[{Parameters(indexer.Parameters)}]",
            _ => $"{container}.{member.Name}",
        };
    }

    // ======================================================================================
    // C# text
    // ======================================================================================

    /// <summary>A type as C# source names it from anywhere (null: it can't be named, e.g. an anonymous type).</summary>
    internal static string? TypeText(ITypeSymbol type)
    {
        if (!Nameable(type)) return null;
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool Nameable(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol a => Nameable(a.ElementType),
        INamedTypeSymbol { IsAnonymousType: true } => false,
        INamedTypeSymbol n => n.CanBeReferencedByName && !n.Name.Contains('<') && n.TypeArguments.All(Nameable) && (n.ContainingType is null || Nameable(n.ContainingType)),
        ITypeParameterSymbol => false,
        IPointerTypeSymbol or IFunctionPointerTypeSymbol => false,
        _ => true,
    };

    /// <summary>`new { A = default(int), B = new { ... } }`: an expression of an anonymous type (C# unifies them by shape).</summary>
    private static string? AnonymousInitializer(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsAnonymousType: true } anonymous) return null;
        var sb = new StringBuilder("new { ");
        var first = true;
        foreach (var p in anonymous.GetMembers().OfType<IPropertySymbol>())
        {
            if (!first) sb.Append(", ");
            first = false;
            var value = TypeText(p.Type) is { } text ? $"default({text})" : AnonymousInitializer(p.Type);
            if (value is null) return null;
            sb.Append('@').Append(p.Name).Append(" = ").Append(value);
        }
        return sb.Append(" }").ToString();
    }

    /// <summary>A constant argument as a C# constant expression of the same type and value.</summary>
    private static string ConstantText(object? value, ITypeSymbol? type)
    {
        if (value is null) return type is null or { SpecialType: SpecialType.System_Object } ? "null" : $"(({TypeText(type)})null)";
        string Literal() => value switch
        {
            bool b => b ? "true" : "false",
            string s => SyntaxFactory.Literal(s).Text,
            char c => SyntaxFactory.Literal(c).Text,
            // Explicit suffixes: Roslyn's literal text of 2.0 is "2", an int (a different binding).
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            uint u => u.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            float f => float.IsFinite(f) ? f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "F"
                : f switch { float.NaN => "global::System.Single.NaN", > 0 => "global::System.Single.PositiveInfinity", _ => "global::System.Single.NegativeInfinity" },
            double d => double.IsFinite(d) ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "D"
                : d switch { double.NaN => "global::System.Double.NaN", > 0 => "global::System.Double.PositiveInfinity", _ => "global::System.Double.NegativeInfinity" },
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M",
            sbyte or byte or short or ushort => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
            _ => throw CompileError.Unsupported($"dynamic argument constant of type {value.GetType().Name}"),
        };
        var literal = Literal();
        if (literal.StartsWith('-')) literal = "(" + literal + ")";
        // Enums and the small integer types: a cast keeps it a constant of that type.
        return type is null || type.SpecialType is SpecialType.System_Boolean or SpecialType.System_String or SpecialType.System_Char
            or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
            ? literal : $"(({TypeText(type)}){literal})";
    }
}
