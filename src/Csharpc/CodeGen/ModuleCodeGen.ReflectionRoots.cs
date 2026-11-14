using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Closed-world MakeGenericType. csharpc compiles generic code per instantiation, so `typeof(G&lt;,&gt;).MakeGenericType(args)` finds
/// only the instantiations the program created. A framework that builds its helpers that way (System.Text.Json's converters)
/// gets them from the types the program rooted (FunctionCodeGen.ReflectionRoots.cs: a call to a [RequiresUnreferencedCode] method
/// names them): every open generic definition the framework's IL passes to MakeGenericType is instantiated over the roots, with the
/// argument shapes frameworks build them with, as far as the generic constraints allow:
///  * (T): the root itself;
///  * (T, its type arguments...) or (array, element): a constructed type and its parts;
///  * (T, the parameter types of one of its constructors..., object...): a type and what its constructor takes, padded with object.
/// The instantiations keep their constructors (Activator.CreateInstance). An argument list of another shape stays the explicit
/// "not supported by csharpc yet" MakeGenericType reports at run time.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly HashSet<INamedTypeSymbol> _makeGenericDefinitions = new(SymbolEqualityComparer.Default);
    private readonly List<INamedTypeSymbol> _makeGenericOrder = [];
    private readonly HashSet<ITypeSymbol> _reflectionRoots = new(SymbolEqualityComparer.Default);
    private readonly List<ITypeSymbol> _reflectionRootOrder = [];
    private readonly HashSet<string> _rootedInstantiations = [];
    private readonly HashSet<INamedTypeSymbol> _speculativeInstances = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// A method of a type made for reflection from the roots: the shapes are guesses (a converter for a ReadOnlyMemory&lt;float&gt; the F#
    /// converter factory would never be asked for), so what cannot be compiled there is a stub that reports itself if it ever runs, not an error.
    /// </summary>
    internal bool IsSpeculativeInstance(IMethodSymbol method) => method.ContainingType is { } type && _speculativeInstances.Contains(type);
    private readonly HashSet<ITypeSymbol> _reflectionKept = new(SymbolEqualityComparer.Default);
    private readonly List<ITypeSymbol> _programInstantiated = [];
    private int _rootedRootsScanned, _rootedDefinitionsScanned, _programInstantiatedScanned;
    private bool _objectRooted;

    /// <summary>
    /// A generic type or array the program's own code creates. Once a rooted graph has `object` in it (a value of any type may be
    /// stored there), these are roots too: the instantiations the serializer needs for the values the program makes.
    /// </summary>
    internal void NoteProgramInstantiation(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol || TypeSubstitution.IsOpen(type) || type is INamedTypeSymbol { IsGenericType: false }) return;
        // The collections and arrays of the BCL: a value of one of those is what an object-typed member holds. A library's own generic helper types
        // (what a JSON source generator's code builds) and the program's own generics are not values to serialize.
        if (type is INamedTypeSymbol named && (IsProgramOrPackageType(named) || CoreLib.TryMap(named) is null)) return;
        _programInstantiated.Add(type);
    }

    internal void KeepReflectionType(ITypeSymbol type)
    {
        CheckNotCanonical(type, "a reflection root");
        if (type is ITypeParameterSymbol || !_reflectionKept.Add(type)) return;
        NoteReflectionRoot(type);
        // object: the resolver asks for its (public, parameterless) constructor too, to create the instances of an object-typed value.
        if (type.SpecialType == SpecialType.System_Object) { MarkDynamicallyAccessed(type, ModuleCodeGen.DamPublicConstructors); return; }
        if (type is IArrayTypeSymbol array) { KeepReflectionType(array.ElementType); return; }
        if (type is not INamedTypeSymbol named) return;
        foreach (var argument in named.TypeArguments) KeepReflectionType(argument);
        // The members of the program's own types (its sources, its assembly as IL, packages) are kept; a library's types (BCL, the framework's
        // own) are roots by themselves, not descended into.
        if (named.SpecialType != SpecialType.None || !IsProgramOrPackageType(named))
        {
            // A BCL type of the graph (string, DateTime, List<T>...): the resolver asks it for its constructors. Primitives have
            // none, and the runtime answers that without a description.
            if (named.SpecialType is SpecialType.None or SpecialType.System_String or SpecialType.System_Decimal or SpecialType.System_DateTime)
                MarkDynamicallyAccessed(named, ModuleCodeGen.DamConstructors);
            // A generic struct or tuple of the BCL (KeyValuePair<,>, ValueTuple<,>, Tuple<,>) is serialized as an object: its properties and fields are read.
            if (named is { IsGenericType: true, SpecialType: SpecialType.None } && !TypeMapper.IsSpan(named) && !TypeMapper.IsNullable(named)
                && (named.IsValueType || named.Name == "Tuple") && !IsProgramOrPackageType(named) && named.ContainingNamespace?.ToDisplayString() == "System"
                || named is { Name: "KeyValuePair", IsGenericType: true })
                MarkDynamicallyAccessed(named, ModuleCodeGen.DamProperties | ModuleCodeGen.DamFields);
            return;
        }
        MarkDynamicallyAccessed(named, ModuleCodeGen.DamConstructors | ModuleCodeGen.DamMethods | ModuleCodeGen.DamProperties | ModuleCodeGen.DamFields | ModuleCodeGen.DamEvents);
        if (named.BaseType is { } baseType) KeepReflectionType(baseType);
        foreach (var attribute in named.GetAttributes()) KeepAttributeTypes(attribute);
        foreach (var member in named.GetMembers())
        {
            if (member.IsStatic) continue;
            foreach (var attribute in member.GetAttributes()) KeepAttributeTypes(attribute);
            switch (member)
            {
                case IPropertySymbol property: KeepReflectionType(property.Type); break;
                case IFieldSymbol field: KeepReflectionType(field.Type); break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    foreach (var parameter in constructor.Parameters) KeepReflectionType(parameter.Type);
                    break;
            }
        }
    }

    // [JsonConverter(typeof(C))], [JsonDerivedType(typeof(D))]: the Types an attribute names are created or serialized too.
    private void KeepAttributeTypes(AttributeData attribute)
    {
        foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(n => n.Value)))
            if (argument.Kind == TypedConstantKind.Type && argument.Value is ITypeSymbol named) KeepReflectionType(named);
    }


    /// <summary>A type of the program: its sources, or an IL assembly that is neither the shared framework's nor System.Private.CoreLib.</summary>
    internal bool IsProgramOrPackageType(INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, Compilation.Assembly) && !CoreLib.IsCoreLib(type)
        || Il.OwnerOf(type.OriginalDefinition) is { IsFramework: false, IsCoreLib: false };

    /// <summary>The type, a type argument of it or an element type is the program's or a package's (not the shared framework's, not CoreLib's).</summary>
    internal bool MentionsProgramOrPackageType(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => MentionsProgramOrPackageType(array.ElementType),
        INamedTypeSymbol named => IsProgramOrPackageType(named) || named.TypeArguments.Any(MentionsProgramOrPackageType),
        _ => false,
    };

    /// <summary>`typeof(G&lt;,&gt;).MakeGenericType(...)` was lowered: G is instantiated over the roots.</summary>
    internal void NoteMakeGenericDefinition(INamedTypeSymbol definition)
    {
        definition = definition.OriginalDefinition;
        if (!definition.IsGenericType || !_makeGenericDefinitions.Add(definition)) return;
        if (s_traceDam) Console.Error.WriteLine($"[csharpc] makegeneric {definition}");
        _makeGenericOrder.Add(definition);
        _rootedRootsScanned = 0;
    }

    /// <summary>A type a call to a [RequiresUnreferencedCode] method names (or reaches from one that it names).</summary>
    internal void NoteReflectionRoot(ITypeSymbol type)
    {
        CheckNotCanonical(type, "a reflection root");
        if (type is ITypeParameterSymbol || TypeSubstitution.IsOpen(type) || type.TypeKind == TypeKind.Error || !_reflectionRoots.Add(type)) return;
        if (type.SpecialType == SpecialType.System_Object) _objectRooted = true;
        if (s_traceDam) Console.Error.WriteLine($"[csharpc] root {type}");
        _reflectionRootOrder.Add(type);
        _rootedDefinitionsScanned = 0;
    }

    /// <summary>Instantiates the definitions over the roots found so far; true when a type was created.</summary>
    private bool CloseReflectionInstantiations()
    {
        if (_objectRooted)
            while (_programInstantiatedScanned < _programInstantiated.Count) KeepReflectionType(_programInstantiated[_programInstantiatedScanned++]);
        if (_makeGenericOrder.Count == 0 || _reflectionRootOrder.Count == 0) return false;
        if (_rootedRootsScanned >= _reflectionRootOrder.Count && _rootedDefinitionsScanned >= _makeGenericOrder.Count) return false;
        var any = false;
        for (var d = 0; d < _makeGenericOrder.Count; d++)
        {
            var definition = _makeGenericOrder[d];
            // Pairs already tried: all roots for a new definition, only the new roots for a known one.
            for (var r = d < _rootedDefinitionsScanned ? _rootedRootsScanned : 0; r < _reflectionRootOrder.Count; r++)
                foreach (var arguments in RootedArguments(definition, _reflectionRootOrder[r]))
                    any |= Instantiate(definition, arguments);
        }
        _rootedRootsScanned = _reflectionRootOrder.Count;
        _rootedDefinitionsScanned = _makeGenericOrder.Count;
        return any;
    }

    private bool Instantiate(INamedTypeSymbol definition, ImmutableArray<ITypeSymbol> arguments)
    {
        if (arguments.Any(a => a is IPointerTypeSymbol or IFunctionPointerTypeSymbol || a.IsRefLikeType || a.SpecialType == SpecialType.System_Void || TypeSubstitution.IsOpen(a)
                || ReflectionNestingDepth(a) > 4)) return false;
        var key = IdentityKey(definition) + "<" + string.Join(",", arguments.Select(IdentityKey)) + ">";
        if (!_rootedInstantiations.Add(key)) return false;
        if (!SatisfiesConstraints(definition, arguments)) return false;
        try
        {
            var closed = definition.Construct([.. arguments]);
            if (s_traceDam) Console.Error.WriteLine($"[csharpc] instantiate {closed}");
            _speculativeInstances.Add(closed);
            TypeMethodTable(closed, null!);
            MarkDynamicallyAccessed(closed, DamConstructors);
            return true;
        }
        catch (CompileError) { return false; } // not representable: MakeGenericType throws NotSupportedException at run time
    }

    /// <summary>The argument lists of <paramref name="definition"/> built from <paramref name="root"/> in the shapes frameworks use.</summary>
    private IEnumerable<ImmutableArray<ITypeSymbol>> RootedArguments(INamedTypeSymbol definition, ITypeSymbol root)
    {
        var arity = definition.Arity;
        // One argument: the root when it is a type with its own members (the program's, an enum, a constructed generic), or the value
        // type under a Nullable<T> root. Primitives and strings are never the sole argument of a helper the framework makes up (a converter
        // for float exists as a class, not as an instantiation of a generic one).
        if (arity == 1)
        {
            if (root is INamedTypeSymbol { SpecialType: SpecialType.None, OriginalDefinition.SpecialType: not SpecialType.System_Nullable_T } plain
                && (IsProgramOrPackageType(plain) || plain.TypeKind == TypeKind.Enum || plain.IsGenericType))
                yield return [root];
            else if (root is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                yield return [nullable.TypeArguments[0]];
            else if (root is IArrayTypeSymbol) yield return [root];
            // The element of a one-argument generic or an array (Memory<float> -> a converter for float elements).
            if (root is INamedTypeSymbol { IsGenericType: true, IsValueType: true, TypeArguments.Length: 1 } wrapper && wrapper.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) yield return [wrapper.TypeArguments[0]];
        }
        if (root is INamedTypeSymbol { IsGenericType: true } constructed && arity == 1 + constructed.TypeArguments.Length && arity > 1)
            yield return [root, .. constructed.TypeArguments];
        if (root is IArrayTypeSymbol { IsSZArray: true } array && arity == 2) yield return [root, array.ElementType];
        if (arity < 2 || root is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } type || type.IsStatic) yield break;
        var placeholder = Compilation.GetSpecialType(SpecialType.System_Object);
        foreach (var constructor in type.InstanceConstructors)
        {
            var parameters = constructor.Parameters;
            if (parameters.Length == 0 || parameters.Length > arity - 1) continue;
            // A library type is built by its constructor only when it takes its own type arguments in order (a tuple, a KeyValuePair); the
            // collections' constructors (capacity, comparer, source) are not how a serializer makes them.
            if (!IsProgramOrPackageType(type) && !(type.IsGenericType && parameters.Length == type.TypeArguments.Length
                && parameters.Select(parameter => parameter.Type).SequenceEqual(type.TypeArguments, SymbolEqualityComparer.Default)))
                continue;
            var arguments = ImmutableArray.CreateBuilder<ITypeSymbol>(arity);
            arguments.Add(root);
            foreach (var parameter in parameters) arguments.Add(parameter.Type);
            while (arguments.Count < arity) arguments.Add(placeholder);
            yield return arguments.MoveToImmutable();
        }
    }
}
