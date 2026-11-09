using Csharpc.Frontend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// The trimmer's rule for reflection the framework cannot see through: a framework method marked
/// [RequiresUnreferencedCode] (System.Text.Json's serializer, ...) reads members and builds generic types from the Types it is
/// given, which the library leaves to the program to root. A call to one roots the types it names: the type arguments, the
/// typeof / GetType() / value arguments, and the member, base, generic-argument, constructor-parameter and attribute types of
/// those. The members of the program's own types stay (reflection reads them), and every type is recorded as a root for the
/// instantiations the framework builds with MakeGenericType (ModuleCodeGen.ReflectionRoots.cs).
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>The method (or its type) carries [RequiresUnreferencedCode].</summary>
    private static bool RequiresUnreferencedCode(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        static bool Has(ISymbol symbol) => symbol.GetAttributes().Any(a => a.AttributeClass is { Name: "RequiresUnreferencedCodeAttribute", ContainingNamespace: { Name: "CodeAnalysis", ContainingNamespace.Name: "Diagnostics" } });
        return Has(definition) || Has(definition.ContainingType) || definition.AssociatedSymbol is { } associated && Has(associated);
    }

    /// <summary>A call from the program (or any code but the callee's own assembly) to a [RequiresUnreferencedCode] method of the framework's IL.</summary>
    private void NoteReflectionRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        if (target.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)) return;
        var definition = target.OriginalDefinition;
        // Only the framework's IL: a shadowed library has its own metadata, and the program's own methods are not the trimmer's concern.
        var owner = _m.Il.OwnerOf(definition);
        if (!(owner is { IsFramework: true, IsCoreLib: false } || IlFirst.Assemblies.Contains(definition.ContainingAssembly?.Name ?? "", StringComparer.OrdinalIgnoreCase))) return;
        if (!RequiresUnreferencedCode(target)) return;
        // The framework's own methods calling each other are not a program rooting types.
        if (_method is not null && _m.Il.OwnerOf(_method.OriginalDefinition) is { } caller && caller.Name == definition.ContainingAssembly?.Name) return;
        KeepReflectionRoots(target, inv);
    }

    private void KeepReflectionRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var t in target.TypeArguments) KeepReflectionType(_subst.Apply(t), seen);
        foreach (var arg in inv.Arguments)
        {
            var value = arg.Value;
            if (arg.Parameter is { Name: "value" } && value is IConversionOperation { Operand.Type: { } operandType }) KeepReflectionType(_subst.Apply(operandType), seen);
            if (value is IlValueOperation { TypeOfOperand: { } typeOf }) KeepReflectionType(_subst.Apply(typeOf), seen);
            while (value is IConversionOperation c) value = c.Operand;
            switch (value)
            {
                case ITypeOfOperation typeOfOperation: KeepReflectionType(_subst.Apply(typeOfOperation.TypeOperand), seen); break;
                case IInvocationOperation { TargetMethod.Name: "GetType", Instance.Type: { } instanceType }: KeepReflectionType(_subst.Apply(instanceType), seen); break;
            }
            if (arg.Parameter is { Name: "value" } && value.Type is { } valueType) KeepReflectionType(_subst.Apply(valueType), seen);
        }
    }

    private void KeepReflectionType(ITypeSymbol type, HashSet<ITypeSymbol> seen)
    {
        if (type is ITypeParameterSymbol || !seen.Add(type)) return;
        _m.NoteReflectionRoot(type);
        // object: the resolver asks for its (public, parameterless) constructor too, to create the instances of an object-typed value.
        if (type.SpecialType == SpecialType.System_Object) { _m.MarkDynamicallyAccessed(type, ModuleCodeGen.DamPublicConstructors); return; }
        if (type is IArrayTypeSymbol array) { KeepReflectionType(array.ElementType, seen); return; }
        if (type is not INamedTypeSymbol named) return;
        foreach (var argument in named.TypeArguments) KeepReflectionType(argument, seen);
        // The members of the program's own types (its sources, its assembly as IL, packages) are kept; a library's types (BCL, the framework's
        // own) are roots by themselves, not descended into.
        if (named.SpecialType != SpecialType.None || !_m.IsProgramOrPackageType(named))
        {
            // A BCL type of the graph (string, DateTime, List<T>...): the resolver asks it for its public constructors. Primitives have
            // none, and the runtime answers that without a description.
            if (named.SpecialType is SpecialType.None or SpecialType.System_String or SpecialType.System_Decimal or SpecialType.System_DateTime)
                _m.MarkDynamicallyAccessed(named, ModuleCodeGen.DamPublicConstructors);
            return;
        }
        _m.MarkDynamicallyAccessed(named, ModuleCodeGen.DamConstructors | ModuleCodeGen.DamMethods | ModuleCodeGen.DamProperties | ModuleCodeGen.DamFields | ModuleCodeGen.DamEvents);
        if (named.BaseType is { } baseType) KeepReflectionType(baseType, seen);
        foreach (var attribute in named.GetAttributes()) KeepAttributeTypes(attribute, seen);
        foreach (var member in named.GetMembers())
        {
            if (member.IsStatic) continue;
            foreach (var attribute in member.GetAttributes()) KeepAttributeTypes(attribute, seen);
            switch (member)
            {
                case IPropertySymbol property: KeepReflectionType(property.Type, seen); break;
                case IFieldSymbol field: KeepReflectionType(field.Type, seen); break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    foreach (var parameter in constructor.Parameters) KeepReflectionType(parameter.Type, seen);
                    break;
            }
        }
    }

    // [JsonConverter(typeof(C))], [JsonDerivedType(typeof(D))]: the Types an attribute names are created or serialized too.
    private void KeepAttributeTypes(AttributeData attribute, HashSet<ITypeSymbol> seen)
    {
        foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(n => n.Value)))
            if (argument.Kind == TypedConstantKind.Type && argument.Value is ITypeSymbol named) KeepReflectionType(named, seen);
    }
}
