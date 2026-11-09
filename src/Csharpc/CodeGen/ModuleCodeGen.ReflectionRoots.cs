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
        _programInstantiated.Add(type);
    }

    internal void KeepReflectionType(ITypeSymbol type)
    {
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
            // A BCL type of the graph (string, DateTime, List<T>...): the resolver asks it for its public constructors. Primitives have
            // none, and the runtime answers that without a description.
            if (named.SpecialType is SpecialType.None or SpecialType.System_String or SpecialType.System_Decimal or SpecialType.System_DateTime)
                MarkDynamicallyAccessed(named, ModuleCodeGen.DamPublicConstructors);
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
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, Compilation.Assembly)
        || Il.OwnerOf(type.OriginalDefinition) is { IsFramework: false, IsCoreLib: false };

    /// <summary>`typeof(G&lt;,&gt;).MakeGenericType(...)` was lowered: G is instantiated over the roots.</summary>
    internal void NoteMakeGenericDefinition(INamedTypeSymbol definition)
    {
        definition = definition.OriginalDefinition;
        if (!definition.IsGenericType || !_makeGenericDefinitions.Add(definition)) return;
        _makeGenericOrder.Add(definition);
        _rootedRootsScanned = 0;
    }

    /// <summary>A type a call to a [RequiresUnreferencedCode] method names (or reaches from one that it names).</summary>
    internal void NoteReflectionRoot(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol || TypeSubstitution.IsOpen(type) || type.TypeKind == TypeKind.Error || !_reflectionRoots.Add(type)) return;
        if (type.SpecialType == SpecialType.System_Object) _objectRooted = true;
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
        var key = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "<" + string.Join(",", arguments.Select(a => a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">";
        if (!_rootedInstantiations.Add(key)) return false;
        if (!SatisfiesConstraints(definition, arguments)) return false;
        try
        {
            var closed = definition.Construct([.. arguments]);
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
            var arguments = ImmutableArray.CreateBuilder<ITypeSymbol>(arity);
            arguments.Add(root);
            foreach (var parameter in parameters) arguments.Add(parameter.Type);
            while (arguments.Count < arity) arguments.Add(placeholder);
            yield return arguments.MoveToImmutable();
        }
    }
}
