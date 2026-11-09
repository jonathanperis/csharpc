using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.CodeGen;

/// <summary>
/// The [NullableContext] and [Nullable] attributes csc writes for source in an annotations context, so that reflection lists
/// them as the JIT does (docs/adr/0018-reflection-metadata.md). Roslyn's symbols hide them, and codegen's compilation has the
/// nullable context off, so the annotations are read from the declarations' syntax.
///
/// csc's rules (Roslyn's MostCommonNullableValueBuilder and the attribute emission, checked against the JIT's reflection):
///  * A type is described by bytes in preorder: 0 oblivious, 1 not annotated, 2 annotated, for every reference type, array
///    and type parameter in it (containing types' arguments first); a generic struct contributes 0 and its arguments; other
///    value types contribute nothing and `X?` for a struct X what X does (`int?` nothing, `T?` with T : struct a 0); a
///    struct-constrained type parameter contributes 0. No bytes: no attribute.
///  * A member's type votes for its byte when all its bytes are equal. A method votes once, with the most common value of its
///    return type, parameters and type parameters; a type votes with its base type (bytes [0, arguments...]), the interfaces it
///    lists itself (all inherited ones, but those of its base type), its type parameters and every member (fields, properties
///    and events, their accessors, auto-property backing fields, methods, nested types by their own value; lambdas, local
///    functions and an event's backing field don't).
///  * The most common value wins; ties go to 0, then 1. The type or method carries [NullableContext] with it when it differs
///    from what the container inherits (nothing at the top: 0). A type with a base type also gets [Nullable] for it unless its
///    bytes are all the inherited value.
///  * What differs from the context of its container gets [Nullable] (one byte when all are equal, else the array) on fields,
///    properties, events, parameters, return values and type parameters.
/// Declarations csharpc can't read exactly (aliases, records, delegates, indexers, primary constructors, constraints with types...)
/// keep the explicit "unknown" answer of the callers.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private sealed class NullableMethod
    {
        public byte? Common;                  // the method's own most common value
        public bool Emitted;                  // [NullableContext] on the method
        public byte Effective;                // what its parameters inherit
        public List<byte> Return = [];
        public readonly Dictionary<int, List<byte>> Parameters = [];
        public readonly List<byte> TypeParameters = [];
    }

    private sealed class NullableType
    {
        public bool Exact, Resolved;
        public bool Explicit, Oblivious;                 // not Exact, but every member's full [Nullable] bytes are known (records, primary constructors)
        public byte? Common;
        public bool Emitted;
        public byte Effective;
        public List<byte>? Base;              // [0, arguments...] of the base type (classes and structs)
        public readonly Dictionary<ISymbol, List<byte>> Slots = new(SymbolEqualityComparer.Default);     // fields, properties, events
        public readonly Dictionary<ISymbol, NullableMethod> Methods = new(SymbolEqualityComparer.Default);
        public readonly List<byte> TypeParameters = [];
    }

    private readonly Dictionary<INamedTypeSymbol, NullableType> _nullableTypes = new(SymbolEqualityComparer.Default);

    private sealed class NullableUnsupported : Exception { }

    // Public in the .NET 11 reference assemblies (csc uses them from System.Private.CoreLib, it embeds nothing); the corelib shadows them.
    private INamedTypeSymbol? NullableAttributeType => Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.NullableAttribute");
    private INamedTypeSymbol? NullableContextAttributeType => Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.NullableContextAttribute");

    /// <summary>The analysis of a source type; <c>Exact</c> is false when csharpc can't reproduce csc's attributes for it.</summary>
    private NullableType NullableOf(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        // A nested type inherits the context of its container, which votes with it: resolve from the outermost type.
        if (type.ContainingType is { } outer && !NullableOf(outer).Exact) return new NullableType();
        if (_nullableTypes.TryGetValue(type, out var cached) && (cached.Resolved || type.ContainingType is not null)) return cached;
        var info = Compute(type);
        if (info.Exact && type.ContainingType is null) Resolve(type, info, 0);
        return info;
    }

    private NullableType Compute(INamedTypeSymbol type)
    {
        if (_nullableTypes.TryGetValue(type, out var cached)) return cached;
        var info = new NullableType();
        _nullableTypes[type] = info; // guards recursion
        try
        {
            Analyze(type, info);
            info.Exact = true;
        }
        catch (NullableUnsupported)
        {
            info.Exact = false;
            info.Slots.Clear();
            info.Methods.Clear();
            try { AnalyzeExplicit(type, info); info.Explicit = true; }
            catch (NullableUnsupported) { info.Slots.Clear(); info.Methods.Clear(); }
        }
        return info;
    }

    /// <summary>With what the container passes down: whether the type and its methods carry [NullableContext], and what their members inherit.</summary>
    private void Resolve(INamedTypeSymbol type, NullableType info, byte parent)
    {
        info.Resolved = true;
        info.Emitted = info.Common is { } common && common != parent;
        info.Effective = info.Emitted ? info.Common!.Value : parent;
        foreach (var method in info.Methods.Values)
        {
            method.Emitted = method.Common is { } c && c != info.Effective;
            method.Effective = method.Emitted ? method.Common!.Value : info.Effective;
        }
        foreach (var nested in type.GetTypeMembers())
            if (_nullableTypes.TryGetValue(nested.OriginalDefinition, out var inner) && inner.Exact) Resolve(nested, inner, info.Effective);
    }

    private static byte? Uniform(List<byte> flags) => flags.Count > 0 && flags.All(b => b == flags[0]) ? flags[0] : null;

    private static byte? MostCommon(int[] votes)
    {
        var max = Math.Max(votes[0], Math.Max(votes[1], votes[2]));
        if (max == 0) return null;
        return votes[0] == max ? (byte)0 : votes[1] == max ? (byte)1 : (byte)2;
    }

    private static void Vote(int[] votes, List<byte> flags)
    {
        if (Uniform(flags) is { } value) votes[value]++;
    }

    private void Analyze(INamedTypeSymbol type, NullableType info)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum) || type.IsRecord || type.IsAnonymousType || type.IsTupleType
            || type.IsImplicitlyDeclared || NullableAttributeType is null || NullableContextAttributeType is null)
            throw new NullableUnsupported();
        // A generic container's type parameters are the nested type's too (csc votes with them as well): not reproduced.
        if (type.ContainingType is { } outer0 && IsGenericContext(outer0)) throw new NullableUnsupported();
        if (type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null })) throw new NullableUnsupported();

        // One annotations state for the whole type: a type that mixes `#nullable` regions would need the oblivious byte (0) per member.
        var enabled = AnnotationsEnabled(type);
        foreach (var member in type.GetMembers())
            if (member is not INamedTypeSymbol && !member.IsImplicitlyDeclared && member.DeclaringSyntaxReferences.Length > 0 && AnnotationsEnabled(member) != enabled)
                throw new NullableUnsupported();
        if (!enabled)
        {
            // Nothing is synthesized in an oblivious type; the callers' answers stay as they were.
            info.Exact = true;
            return;
        }
        if (type.TypeKind == TypeKind.Enum) return;

        var votes = new int[3];
        // The base type: [0, arguments...] (a type's base type is never annotated itself).
        if (type.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            var flags = new List<byte>();
            if (type.BaseType is { } baseType && IsGenericContext(baseType))
            {
                var list = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault()?.BaseList;
                if (list is null || list.Types.Count == 0 || list.Types[0] is not SimpleBaseTypeSyntax first) throw new NullableUnsupported();
                TypeFlags(baseType, first.Type, enabled, flags, topLevelOblivious: true);
            }
            else flags.Add(0);
            info.Base = flags;
            Vote(votes, flags);
        }
        var inherited = type.BaseType is null ? ImmutableArray<INamedTypeSymbol>.Empty : type.BaseType.AllInterfaces;
        foreach (var itf in type.AllInterfaces)
            if (!inherited.Contains(itf, SymbolEqualityComparer.Default))
            {
                var flags = new List<byte>();
                TypeFlags(itf, null, enabled, flags, topLevelOblivious: true);
                Vote(votes, flags);
            }
        foreach (var tp in type.TypeParameters)
        {
            var value = TypeParameterValue(tp, enabled, type);
            info.TypeParameters.Add(value);
            votes[value]++;
        }

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field when !field.IsImplicitlyDeclared:
                    {
                        if (field.IsFixedSizeBuffer) throw new NullableUnsupported();
                        var flags = new List<byte>();
                        TypeFlags(field.Type, FieldTypeSyntax(field), enabled, flags);
                        info.Slots[field.OriginalDefinition] = flags;
                        Vote(votes, flags);
                    }
                    break;
                case IPropertySymbol property:
                    {
                        if (property.IsIndexer || property.IsRequired) throw new NullableUnsupported();
                        var flags = new List<byte>();
                        TypeFlags(property.Type, PropertyTypeSyntax(property), enabled, flags);
                        info.Slots[property.OriginalDefinition] = flags;
                        Vote(votes, flags);
                    }
                    break;
                case IEventSymbol e:
                    {
                        var flags = new List<byte>();
                        TypeFlags(e.Type, EventTypeSyntax(e), enabled, flags);
                        info.Slots[e.OriginalDefinition] = flags;
                        Vote(votes, flags);
                    }
                    break;
                case INamedTypeSymbol nested:
                    if (nested.IsImplicitlyDeclared) break;
                    {
                        var inner = Compute(nested.OriginalDefinition);
                        if (!inner.Exact) throw new NullableUnsupported();
                        if (inner.Common is { } c) votes[c]++;
                    }
                    break;
            }
        }
        // The backing field of an auto-property votes with the property's type (an event's is counted with the event).
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsImplicitlyDeclared))
        {
            if (field.AssociatedSymbol is IPropertySymbol owner && info.Slots.TryGetValue(owner.OriginalDefinition, out var ownerFlags)) Vote(votes, ownerFlags);
            else if (field.AssociatedSymbol is not IEventSymbol) throw new NullableUnsupported();
        }
        // Methods last: accessors need their property's or event's slot.
        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        {
            var method = AnalyzeMethod(member, info, enabled);
            if (method is null) continue;
            info.Methods[member.OriginalDefinition] = method;
            if (method.Common is { } c) votes[c]++;
        }

        info.Common = MostCommon(votes);
    }

    /// <summary>
    /// A record or a type with a primary constructor: csc's [NullableContext] choice involves the members it synthesizes and
    /// is not reproduced, but the bytes of every declared member's type (a positional parameter and its property included) are
    /// known from the declarations. They are listed in full, [Nullable] on every member with a reference type, so that reading
    /// them through NullabilityInfoContext gives csc's answer; the lists stay unknown for everything else.
    /// </summary>
    private void AnalyzeExplicit(INamedTypeSymbol type, NullableType info)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAnonymousType || type.IsTupleType || type.IsImplicitlyDeclared
            || NullableAttributeType is null)
            throw new NullableUnsupported();
        if (type.ContainingType is { } outer && IsGenericContext(outer)) throw new NullableUnsupported();
        var declarations = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().ToList();
        if (declarations.Count != 1 || declarations[0].ParameterList is null && !type.IsRecord) throw new NullableUnsupported();
        var enabled = AnnotationsEnabled(type);
        foreach (var member in type.GetMembers())
            if (member is not INamedTypeSymbol && !member.IsImplicitlyDeclared && member.DeclaringSyntaxReferences.Length > 0 && AnnotationsEnabled(member) != enabled)
                throw new NullableUnsupported();
        if (!enabled) { info.Oblivious = true; return; } // nothing is synthesized in an oblivious type: every member's bytes are absent
        var positional = new Dictionary<string, List<byte>>();
        if (declarations[0].ParameterList is { } list)
        {
            var primary = type.InstanceConstructors.FirstOrDefault(c => c.DeclaringSyntaxReferences.Length == 1 && c.DeclaringSyntaxReferences[0].GetSyntax() is TypeDeclarationSyntax
                && c.Parameters.Length == list.Parameters.Count) ?? throw new NullableUnsupported();
            var method = new NullableMethod();
            for (var i = 0; i < list.Parameters.Count; i++)
            {
                if (list.Parameters[i].Type is not { } parameterType) throw new NullableUnsupported();
                var flags = new List<byte>();
                TypeFlags(primary.Parameters[i].Type, parameterType, enabled, flags);
                positional[list.Parameters[i].Identifier.Text] = flags;
                method.Parameters[i] = flags;
            }
            info.Methods[primary.OriginalDefinition] = method;
        }
        foreach (var member in type.GetMembers())
        {
            try
            {
                switch (member)
                {
                    case IFieldSymbol { IsImplicitlyDeclared: false } field when !field.IsFixedSizeBuffer && FieldTypeSyntax(field) is not null:
                        {
                            var flags = new List<byte>();
                            TypeFlags(field.Type, FieldTypeSyntax(field), enabled, flags);
                            info.Slots[field.OriginalDefinition] = flags;
                        }
                        break;
                    case IPropertySymbol { IsIndexer: false, IsRequired: false } property:
                        {
                            var flags = new List<byte>();
                            if (property.DeclaringSyntaxReferences.Length == 1 && property.DeclaringSyntaxReferences[0].GetSyntax() is ParameterSyntax positionalSyntax
                                && positional.TryGetValue(positionalSyntax.Identifier.Text, out var known))
                                flags = known;
                            else if (PropertyTypeSyntax(property) is { } propertySyntax) TypeFlags(property.Type, propertySyntax, enabled, flags);
                            else break;
                            info.Slots[property.OriginalDefinition] = flags;
                            foreach (var accessor in new[] { property.GetMethod, property.SetMethod })
                                if (accessor is not null && AnalyzeMethod(accessor, info, enabled) is { } analyzed) info.Methods[accessor.OriginalDefinition] = analyzed;
                        }
                        break;
                    case IMethodSymbol { MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: false } constructor when !info.Methods.ContainsKey(constructor.OriginalDefinition):
                        if (AnalyzeMethod(constructor, info, enabled) is { } analyzedConstructor) info.Methods[constructor.OriginalDefinition] = analyzedConstructor;
                        break;
                }
            }
            catch (NullableUnsupported) { } // that member's lists stay unknown
        }
    }

    private static bool IsGenericContext(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
            if (t.IsGenericType) return true;
        return false;
    }

    private NullableMethod? AnalyzeMethod(IMethodSymbol method, NullableType owner, bool enabled)
    {
        var result = new NullableMethod();
        var votes = new int[3];
        var syntax = method.DeclaringSyntaxReferences.Length == 1 ? method.DeclaringSyntaxReferences[0].GetSyntax() : null;
        switch (method.MethodKind)
        {
            case MethodKind.PropertyGet or MethodKind.PropertySet when method.AssociatedSymbol is IPropertySymbol property:
                if (!owner.Slots.TryGetValue(property.OriginalDefinition, out var propertyFlags)) throw new NullableUnsupported();
                if (method.MethodKind == MethodKind.PropertyGet) result.Return = propertyFlags;
                else result.Parameters[method.Parameters.Length - 1] = propertyFlags;
                break;
            case MethodKind.EventAdd or MethodKind.EventRemove when method.AssociatedSymbol is IEventSymbol e:
                if (!owner.Slots.TryGetValue(e.OriginalDefinition, out var eventFlags)) throw new NullableUnsupported();
                result.Parameters[0] = eventFlags;
                break;
            case MethodKind.Constructor or MethodKind.StaticConstructor when method.IsImplicitlyDeclared:
                if (method.Parameters.Length > 0) throw new NullableUnsupported();
                return null;
            case MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.UserDefinedOperator or MethodKind.Conversion
                or MethodKind.Destructor or MethodKind.ExplicitInterfaceImplementation:
                {
                    if (syntax is null || method.PartialDefinitionPart is not null || method.PartialImplementationPart is not null) throw new NullableUnsupported();
                    ParameterListSyntax? parameters;
                    TypeSyntax? returnType = null;
                    TypeParameterListSyntax? typeParameters = null;
                    SyntaxList<TypeParameterConstraintClauseSyntax> constraints = default;
                    switch (syntax)
                    {
                        case MethodDeclarationSyntax m: parameters = m.ParameterList; returnType = m.ReturnType; typeParameters = m.TypeParameterList; constraints = m.ConstraintClauses; break;
                        case OperatorDeclarationSyntax o: parameters = o.ParameterList; returnType = o.ReturnType; break;
                        case ConversionOperatorDeclarationSyntax c: parameters = c.ParameterList; returnType = c.Type; break;
                        case ConstructorDeclarationSyntax k: parameters = k.ParameterList; break;
                        case DestructorDeclarationSyntax d: parameters = d.ParameterList; break;
                        default: throw new NullableUnsupported();
                    }
                    if (parameters.Parameters.Count != method.Parameters.Length) throw new NullableUnsupported();
                    if (!method.ReturnsVoid && method.MethodKind is not (MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor))
                        TypeFlags(method.ReturnType, returnType, enabled, result.Return);
                    for (var i = 0; i < method.Parameters.Length; i++)
                    {
                        var flags = new List<byte>();
                        if (parameters.Parameters[i].Type is not { } parameterType) throw new NullableUnsupported();
                        TypeFlags(method.Parameters[i].Type, parameterType, enabled, flags);
                        result.Parameters[i] = flags;
                    }
                    if (method.IsGenericMethod)
                    {
                        if (method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0) throw new NullableUnsupported();
                        foreach (var tp in method.TypeParameters)
                            result.TypeParameters.Add(TypeParameterValue(tp, enabled, method, constraints));
                    }
                    break;
                }
            default:
                throw new NullableUnsupported();
        }
        Vote(votes, result.Return);
        foreach (var flags in result.Parameters.Values) Vote(votes, flags);
        foreach (var value in result.TypeParameters) votes[value]++;
        result.Common = MostCommon(votes);
        return result;
    }

    /// <summary>The value csc gives a type parameter: its constraint decides (unconstrained: annotated).</summary>
    private byte TypeParameterValue(ITypeParameterSymbol tp, bool enabled, ISymbol owner, SyntaxList<TypeParameterConstraintClauseSyntax> methodConstraints = default)
    {
        if (tp.HasValueTypeConstraint || tp.HasUnmanagedTypeConstraint) return 0;
        if (tp.ConstraintTypes.Length > 0 || tp.HasConstructorConstraint) throw new NullableUnsupported();
        if (tp.HasReferenceTypeConstraint)
        {
            var clauses = owner is INamedTypeSymbol named
                ? named.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().SelectMany(d => d.ConstraintClauses)
                : methodConstraints.AsEnumerable();
            var annotated = clauses.Where(c => c.Name.Identifier.ValueText == tp.Name)
                .SelectMany(c => c.Constraints).OfType<ClassOrStructConstraintSyntax>()
                .Any(c => c.ClassOrStructKeyword.IsKind(SyntaxKind.ClassKeyword) && !c.QuestionToken.IsKind(SyntaxKind.None));
            return annotated ? (byte)2 : (byte)1;
        }
        return tp.HasNotNullConstraint ? (byte)1 : (byte)2;
    }

    private static TypeSyntax? FieldTypeSyntax(IFieldSymbol field) =>
        field.DeclaringSyntaxReferences.Length == 1 && field.DeclaringSyntaxReferences[0].GetSyntax() is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } ? declaration.Type : null;

    private static TypeSyntax? PropertyTypeSyntax(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.Length == 1 && property.DeclaringSyntaxReferences[0].GetSyntax() is PropertyDeclarationSyntax declaration ? declaration.Type : null;

    private static TypeSyntax? EventTypeSyntax(IEventSymbol e) =>
        e.DeclaringSyntaxReferences.Length != 1 ? null : e.DeclaringSyntaxReferences[0].GetSyntax() switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } => declaration.Type,
            EventDeclarationSyntax declaration => declaration.Type,
            _ => null,
        };

    private static bool HasSourceEventBackingField(IEventSymbol e) =>
        !e.IsAbstract && !e.IsExtern && e.ContainingType.TypeKind != TypeKind.Interface && e.AddMethod is { IsImplicitlyDeclared: true };

    /// <summary>
    /// Appends the bytes csc writes for a type, reading `?` from <paramref name="syntax"/> (null: no annotations, as for a
    /// symbol that has no declaration of its own). Throws NullableUnsupported where the syntax and the symbol don't line up
    /// or the type isn't one csharpc models.
    /// </summary>
    private void TypeFlags(ITypeSymbol type, TypeSyntax? syntax, bool enabled, List<byte> flags, bool topLevelOblivious = false)
    {
        var annotated = false;
        while (true)
        {
            if (syntax is RefTypeSyntax reference) syntax = reference.Type;
            else if (syntax is ScopedTypeSyntax scoped) syntax = scoped.Type;
            else if (syntax is NullableTypeSyntax nullable) { annotated = true; syntax = nullable.ElementType; }
            else break;
        }
        byte Reference() => topLevelOblivious || !enabled ? (byte)0 : annotated ? (byte)2 : (byte)1;
        switch (type)
        {
            case ITypeParameterSymbol tp:
                flags.Add(tp.HasValueTypeConstraint || tp.HasUnmanagedTypeConstraint ? (byte)0 : Reference());
                return;
            case IArrayTypeSymbol array:
                {
                    // `T[][]?`: ArrayType(T, [[], []]) under a NullableType: the first rank specifier is the outermost array.
                    flags.Add(Reference());
                    ITypeSymbol element = array.ElementType;
                    var elementSyntax = (syntax as ArrayTypeSyntax)?.ElementType;
                    if (syntax is not null && syntax is not ArrayTypeSyntax) throw new NullableUnsupported();
                    if (syntax is ArrayTypeSyntax a)
                        for (var i = 1; i < a.RankSpecifiers.Count; i++)
                        {
                            if (element is not IArrayTypeSymbol inner) throw new NullableUnsupported();
                            flags.Add(enabled ? (byte)1 : (byte)0);
                            element = inner.ElementType;
                        }
                    TypeFlags(element, elementSyntax, enabled, flags);
                    return;
                }
            case INamedTypeSymbol:
                break;
            default:
                throw new NullableUnsupported(); // pointers, function pointers, dynamic
        }
        var symbol = (INamedTypeSymbol)type;
        if (symbol.TypeKind == TypeKind.Error) throw new NullableUnsupported();
        if (symbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            // `X?` for a struct X writes what X writes (`int?` nothing, `T?` with a struct-constrained T a 0).
            TypeFlags(symbol.TypeArguments[0], syntax, enabled, flags);
            return;
        }
        if (symbol.IsTupleType)
        {
            if (symbol.TupleElements.Length > 7 || symbol.TupleElements.Any(e => !e.IsImplicitlyDeclared)) throw new NullableUnsupported();
            flags.Add(0);
            var elements = syntax switch
            {
                TupleTypeSyntax tuple => tuple.Elements.Select(e => e.Type).ToList(),
                GenericNameSyntax generic => generic.TypeArgumentList.Arguments.ToList(),
                QualifiedNameSyntax { Right: GenericNameSyntax generic } => generic.TypeArgumentList.Arguments.ToList(),
                _ => throw new NullableUnsupported(),
            };
            if (elements.Count != symbol.TupleElements.Length) throw new NullableUnsupported();
            for (var i = 0; i < elements.Count; i++) TypeFlags(symbol.TupleElements[i].Type, elements[i], enabled, flags);
            return;
        }
        if (symbol.IsValueType && !IsGenericContext(symbol)) return; // a plain struct or enum: no byte
        flags.Add(symbol.IsValueType ? (byte)0 : Reference());
        // The arguments of the type and of its containing types, outermost first; the syntax names (some of) them.
        List<INamedTypeSymbol> chain = [];
        for (var t = symbol; t is not null; t = t.ContainingType) chain.Insert(0, t);
        var segments = syntax is null ? [] : NameSegments(syntax);
        if (syntax is not null && segments is null) throw new NullableUnsupported();
        segments ??= [];
        for (var i = 0; i < chain.Count; i++)
        {
            var level = chain[i];
            if (level.TypeArguments.Length == 0) continue;
            var segmentIndex = segments.Count - (chain.Count - i);
            TypeArgumentListSyntax? list = null;
            if (segmentIndex >= 0)
            {
                if (segments[segmentIndex] is GenericNameSyntax generic) list = generic.TypeArgumentList;
                else if (i == chain.Count - 1 && syntax is not null) throw new NullableUnsupported(); // an alias to a constructed type
            }
            if (list is not null && list.Arguments.Count != level.TypeArguments.Length) throw new NullableUnsupported();
            for (var a = 0; a < level.TypeArguments.Length; a++)
                TypeFlags(level.TypeArguments[a], list?.Arguments[a], enabled, flags);
        }
    }

    /// <summary>The identifiers of a (possibly qualified) type name, left to right; null for syntax that isn't a plain name.</summary>
    private static List<SimpleNameSyntax>? NameSegments(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case SimpleNameSyntax simple:
                return [simple];
            case PredefinedTypeSyntax:
                return [];
            case QualifiedNameSyntax qualified:
                {
                    var left = NameSegments(qualified.Left);
                    if (left is null) return null;
                    left.Add(qualified.Right);
                    return left;
                }
            case AliasQualifiedNameSyntax alias:
                return [alias.Name];
            default:
                return null;
        }
    }

    // ---- the attributes ----

    private AttributeInstance? NullableInstance(List<byte> flags)
    {
        if (NullableAttributeType is not { } type || flags.Count == 0) return null;
        var byteType = Compilation.GetSpecialType(SpecialType.System_Byte);
        if (Uniform(flags) is { } single)
            return type.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == 1 && c.Parameters[0].Type.SpecialType == SpecialType.System_Byte) is { } byteCtor
                ? new AttributeInstance(byteCtor, [new AttributeValue(byteType, single)], []) : null;
        var arrayType = Compilation.CreateArrayTypeSymbol(byteType);
        return type.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == 1 && c.Parameters[0].Type is IArrayTypeSymbol) is { } arrayCtor
            ? new AttributeInstance(arrayCtor, [new AttributeValue(arrayType, flags.Select(b => new AttributeValue(byteType, b)).ToImmutableArray())], []) : null;
    }

    private AttributeInstance? NullableContextInstance(byte value) =>
        NullableContextAttributeType?.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == 1) is { } ctor
            ? new AttributeInstance(ctor, [new AttributeValue(Compilation.GetSpecialType(SpecialType.System_Byte), value)], []) : null;

    /// <summary>[Nullable] for bytes that differ from what is inherited: one byte when all are equal, an array otherwise.</summary>
    private AttributeInstance? NullableIfDifferent(List<byte> flags, byte inherited) =>
        flags.Count == 0 || (Uniform(flags) is { } value && value == inherited) ? null : NullableInstance(flags);

    /// <summary>
    /// The synthesized nullable attributes of a type: null when csharpc can't reproduce them (the caller keeps the list
    /// unknown), else [NullableContext] and [Nullable] in csc's order (possibly none).
    /// </summary>
    private List<AttributeInstance>? NullableTypeAttributes(INamedTypeSymbol definition)
    {
        var info = NullableOf(definition);
        if (!info.Exact) return null;
        List<AttributeInstance> list = [];
        if (info.Emitted && NullableContextInstance(info.Common!.Value) is { } context) list.Add(context);
        if (info.Base is { } baseFlags && NullableIfDifferent(baseFlags, info.Effective) is { } baseAttribute) list.Add(baseAttribute);
        return list;
    }

    /// <summary>The analysis of the type of a member whose lists carry explicit [Nullable] attributes (AnalyzeExplicit), else null.</summary>
    private NullableType? ExplicitInfoOfMember(ISymbol member)
    {
        if (member.ContainingType is not { } type || type.OriginalDefinition.Locations.All(l => !l.IsInSource)) return null;
        var info = NullableOf(type);
        return info.Explicit && !info.Exact ? info : null;
    }

    /// <summary>
    /// The set with the member's full [Nullable] bytes in front and the flag that says they are exact (an incomplete list the nullability
    /// reader may use); unchanged for a member that is not in an explicit type or was not analyzed.
    /// </summary>
    private AttributeSet WithExplicitNullable(AttributeSet set, ISymbol member, Func<NullableType, List<byte>?> flagsOf)
    {
        if (ExplicitInfoOfMember(member) is not { } info || (flagsOf(info) ?? (info.Oblivious ? new List<byte>() : null)) is not { } flags) return set;
        var instances = set.Instances;
        if (flags.Any(b => b != 0) && NullableInstance(flags) is { } attribute) instances = [attribute, .. instances];
        return set with { Instances = instances, NullableExplicit = true };
    }

    private NullableType? NullableInfoOfMember(ISymbol member)
    {
        if (member.ContainingType is not { } type || type.OriginalDefinition.Locations.All(l => !l.IsInSource)) return null;
        var info = NullableOf(type);
        return info.Exact ? info : null;
    }

    private List<AttributeInstance>? NullableFieldAttributes(IFieldSymbol field)
    {
        if (NullableInfoOfMember(field) is not { } info) return null;
        var owner = field.AssociatedSymbol is IPropertySymbol or IEventSymbol ? field.AssociatedSymbol.OriginalDefinition : field.OriginalDefinition;
        if (!info.Slots.TryGetValue(owner, out var flags)) return [];
        return NullableIfDifferent(flags, info.Effective) is { } attribute ? [attribute] : [];
    }

    private List<AttributeInstance>? NullablePropertyAttributes(IPropertySymbol property)
    {
        if (NullableInfoOfMember(property) is not { } info) return null;
        return info.Slots.TryGetValue(property.OriginalDefinition, out var flags) && NullableIfDifferent(flags, info.Effective) is { } attribute ? [attribute] : [];
    }

    private List<AttributeInstance>? NullableEventAttributes(IEventSymbol e)
    {
        if (NullableInfoOfMember(e) is not { } info) return null;
        return info.Slots.TryGetValue(e.OriginalDefinition, out var flags) && NullableIfDifferent(flags, info.Effective) is { } attribute ? [attribute] : [];
    }

    private List<AttributeInstance>? NullableMethodAttributes(IMethodSymbol method)
    {
        if (NullableInfoOfMember(method) is not { } info) return null;
        if (!info.Methods.TryGetValue(method.OriginalDefinition, out var data)) return [];
        return data.Emitted && NullableContextInstance(data.Common!.Value) is { } context ? [context] : [];
    }

    /// <summary>The return value's [Nullable] (the flags of the return type when they differ from the method's context); null: not modelled.</summary>
    private List<AttributeInstance>? NullableReturnAttributes(IMethodSymbol method)
    {
        if (NullableInfoOfMember(method) is not { } info) return null;
        if (!info.Methods.TryGetValue(method.OriginalDefinition, out var data)) return [];
        return NullableIfDifferent(data.Return, data.Effective) is { } attribute ? [attribute] : [];
    }

    private List<AttributeInstance>? NullableParameterAttributes(IParameterSymbol parameter)
    {
        if (parameter.ContainingSymbol is not IMethodSymbol method || NullableInfoOfMember(method) is not { } info) return null;
        if (!info.Methods.TryGetValue(method.OriginalDefinition, out var data)) return [];
        return data.Parameters.TryGetValue(parameter.Ordinal, out var flags) && NullableIfDifferent(flags, data.Effective) is { } attribute ? [attribute] : [];
    }

    /// <summary>A generic parameter's attribute: its value when it differs from the context of its type or method.</summary>
    private List<AttributeInstance>? NullableGenericParameterAttributes(ITypeParameterSymbol parameter)
    {
        var owner = parameter.ContainingSymbol.OriginalDefinition;
        if (owner is INamedTypeSymbol type)
        {
            var info = NullableOf(type);
            if (!info.Exact) return null;
            if (parameter.Ordinal >= info.TypeParameters.Count) return [];
            var value = info.TypeParameters[parameter.Ordinal];
            return value != info.Effective && NullableInstance([value]) is { } attribute ? [attribute] : [];
        }
        if (owner is IMethodSymbol method && NullableInfoOfMember(method) is { } methodInfo)
        {
            if (!methodInfo.Methods.TryGetValue(method, out var data) || parameter.Ordinal >= data.TypeParameters.Count) return [];
            var value = data.TypeParameters[parameter.Ordinal];
            return value != data.Effective && NullableInstance([value]) is { } attribute ? [attribute] : [];
        }
        return null;
    }
}
