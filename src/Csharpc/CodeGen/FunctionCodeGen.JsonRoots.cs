using Csharpc.Frontend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Reflection-based System.Text.Json calls whose type gets no compile-time metadata (anonymous and
/// file-local types: Frontend/JsonReflectionContexts.cs) are reported here, where the call is lowered,
/// rather than by the front end: in a method the program never reaches the failure becomes a stub
/// (CSLLVM0007, ModuleCodeGen.Il.cs), as NativeAOT compiles such code and only fails when it runs.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault as the program's runtimeconfig.json sets it (default: true).</summary>
    private bool JsonReflectionByDefault() =>
        _m.RuntimeConfig.LastOrDefault(kv => kv.Key == "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault").Value != "false";

    private void CheckJsonSerializerRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        // The library method as bound (S() would already name the corelib's JsonSerializer).
        if (inv.TargetMethod.ContainingType is not { Name: "JsonSerializer", ContainingNamespace: { Name: "Json", ContainingNamespace: { Name: "Text", ContainingNamespace.Name: "System" } } }) return;
        // Only the Serialize*/Deserialize* methods serialize: the IsReflectionEnabledByDefault getter is the
        // switch that code (ASP.NET Core's JsonOptions()) reads to pick a resolver, and reads like any property.
        if (inv.TargetMethod.MethodKind != MethodKind.Ordinary) return;
        // System.Text.Json compiled from the framework's IL (--il-first): its own methods call JsonSerializer's helpers, which is not
        // a program asking for reflection-based serialization.
        if (inv is IlOperation && _m.Il.OwnerOf(_method) is { } caller && caller.Name == inv.TargetMethod.ContainingAssembly?.Name) return;
        if (target.Parameters.Any(JsonReflectionContexts.IsJsonMetadataParameter)) return;
        if (IlFirst.Assemblies.Contains("System.Text.Json", StringComparer.OrdinalIgnoreCase)) { KeepJsonReflectionRoots(target, inv); return; }
        // Compiled code: the metadata of reflection-based serialization is generated from the C# sources of the
        // call sites (JsonReflectionContexts), which IL doesn't have; without it the call would throw at run time.
        // (Options may carry a source-generated TypeInfoResolver: calls given options are left to run time. With
        // reflection disabled in runtimeconfig.json, .NET throws InvalidOperationException there, as the corelib does.)
        if (inv is IlOperation && !_m.Il.IsCoreLib(_method) && JsonReflectionByDefault() && !inv.TargetMethod.Parameters.Any(JsonReflectionContexts.IsJsonMetadataParameter)
            && !inv.Arguments.Any(a => a.Parameter?.Type is { Name: "JsonSerializerOptions", ContainingNamespace.Name: "Json" } && a.Value is not IlValueOperation { IsNullLiteral: true }))
        {
            // The program as IL: its synthesized contexts cover the roots found in its bodies; one they don't
            // (a compiler-generated or private nested type) would only fail at run time.
            if (JsonReflectionContexts.IlCoveredRoots is not { } covered || _m.Il.OwnerOf(_method) is not { } owner || owner.IsCoreLib)
                throw CompileError.Unsupported($"reflection-based System.Text.Json serialization in compiled code ('{inv.TargetMethod}' without a JsonTypeInfo or JsonSerializerContext): csharpc generates that metadata for the program's own code only; use the JSON source generator ([JsonSerializable])", inv);
            foreach (var root in IlJsonRoots(target, inv))
            {
                if (JsonReflectionContexts.UnsupportedIlRoot(root) is { } why) throw CompileError.Unsupported(why, inv);
                if (!covered.Contains(root.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                    throw CompileError.Unsupported(JsonReflectionContexts.PrivateNestedPart(root) is { } nested
                        ? $"System.Text.Json serialization of '{root.ToDisplayString()}' from compiled code ('{nested.ToDisplayString()}' is a {nested.DeclaredAccessibility.ToString().ToLowerInvariant()} nested type: the generated metadata could only reach it from source)"
                        : $"System.Text.Json serialization of '{root.ToDisplayString()}' from compiled code (no compile-time metadata could be generated for it)", inv);
            }
        }
        foreach (var t in target.TypeArguments)
            if (JsonReflectionContexts.UnsupportedRoot(S(t)) is { } why) throw CompileError.Unsupported(why, inv);
        foreach (var arg in inv.Arguments)
        {
            var value = arg.Value;
            // Serialize<object>(x) and Serialize(x, typeof(T)) / x.GetType(): the value's own type is serialized.
            if (arg.Parameter is { Name: "value" } && value is IConversionOperation { IsImplicit: true, Operand.Type: { } operandType }
                && JsonReflectionContexts.UnsupportedRoot(S(operandType)) is { } valueWhy)
                throw CompileError.Unsupported(valueWhy, inv);
            if (arg.Parameter?.Type is not { Name: "Type", ContainingNamespace.Name: "System" }) continue;
            while (value is IConversionOperation c) value = c.Operand;
            var named = value switch
            {
                ITypeOfOperation typeOf => typeOf.TypeOperand,
                IInvocationOperation { TargetMethod.Name: "GetType", Instance.Type: { } instanceType } => instanceType,
                _ => null,
            };
            if (named is not null && JsonReflectionContexts.UnsupportedRoot(S(named)) is { } typeWhy) throw CompileError.Unsupported(typeWhy, inv);
        }
    }

    /// <summary>
    /// System.Text.Json compiled from the framework's IL reads the members of the types it serializes through real reflection
    /// (DefaultJsonTypeInfoResolver), which the trimmer leaves to the program ([RequiresUnreferencedCode]). A serializer call
    /// that names its types keeps every member of them and of the types their members and generic arguments name, as the
    /// program would have rooted them for ILLink; the shadow JSON route gets the same from its generated metadata.
    /// </summary>
    private void KeepJsonReflectionRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var t in target.TypeArguments) KeepJsonType(_subst.Apply(t), seen);
        foreach (var arg in inv.Arguments)
        {
            var value = arg.Value;
            if (arg.Parameter is { Name: "value" } && value is IConversionOperation { Operand.Type: { } operandType }) KeepJsonType(_subst.Apply(operandType), seen);
            if (value is IlValueOperation { TypeOfOperand: { } typeOf }) KeepJsonType(_subst.Apply(typeOf), seen);
            while (value is IConversionOperation c) value = c.Operand;
            switch (value)
            {
                case ITypeOfOperation typeOfOperation: KeepJsonType(_subst.Apply(typeOfOperation.TypeOperand), seen); break;
                case IInvocationOperation { TargetMethod.Name: "GetType", Instance.Type: { } instanceType }: KeepJsonType(_subst.Apply(instanceType), seen); break;
            }
            if (arg.Parameter is { Name: "value" } && value.Type is { } valueType) KeepJsonType(_subst.Apply(valueType), seen);
        }
    }

    private void KeepJsonType(ITypeSymbol type, HashSet<ITypeSymbol> seen)
    {
        if (type is ITypeParameterSymbol || type.SpecialType == SpecialType.System_Object || !seen.Add(type)) return;
        if (type is IArrayTypeSymbol array) { KeepJsonType(array.ElementType, seen); return; }
        if (type is not INamedTypeSymbol named) return;
        foreach (var argument in named.TypeArguments) KeepJsonType(argument, seen);
        if (named.SpecialType != SpecialType.None || !_m.IsUserType(S(named)))
        {
            // A BCL type of the graph (string, DateTime, List<T>...): the resolver asks it for its public constructors. Primitives have
            // none, and the runtime answers that without a description.
            if (named.SpecialType is SpecialType.None or SpecialType.System_String or SpecialType.System_Decimal or SpecialType.System_DateTime)
                _m.MarkDynamicallyAccessed(named, ModuleCodeGen.DamPublicConstructors);
            return;
        }
        _m.MarkDynamicallyAccessed(named, ModuleCodeGen.DamConstructors | ModuleCodeGen.DamMethods | ModuleCodeGen.DamProperties | ModuleCodeGen.DamFields | ModuleCodeGen.DamEvents);
        if (named.BaseType is { } baseType) KeepJsonType(baseType, seen);
        foreach (var attribute in named.GetAttributes()) KeepJsonAttributeTypes(attribute, seen);
        foreach (var member in named.GetMembers())
        {
            if (member.IsStatic) continue;
            foreach (var attribute in member.GetAttributes()) KeepJsonAttributeTypes(attribute, seen);
            switch (member)
            {
                case IPropertySymbol property: KeepJsonType(property.Type, seen); break;
                case IFieldSymbol field: KeepJsonType(field.Type, seen); break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    foreach (var parameter in constructor.Parameters) KeepJsonType(parameter.Type, seen);
                    break;
            }
        }
    }

    // [JsonConverter(typeof(C))], [JsonDerivedType(typeof(D))]: the Types the attribute names are created or serialized too.
    private void KeepJsonAttributeTypes(AttributeData attribute, HashSet<ITypeSymbol> seen)
    {
        foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(n => n.Value)))
            if (argument.Kind == TypedConstantKind.Type && argument.Value is ITypeSymbol named) KeepJsonType(named, seen);
    }

    /// <summary>The types an IL serializer call needs metadata for, as the front end found them (JsonReflectionContexts.Il.cs).</summary>
    private IEnumerable<ITypeSymbol> IlJsonRoots(IMethodSymbol target, IInvocationOperation inv)
    {
        // Substituted but not redirected to the corelib: the front end named the program's view of the types.
        foreach (var t in target.TypeArguments) yield return _subst.Apply(t);
        foreach (var arg in inv.Arguments)
        {
            if (arg.Value is not IlValueOperation value) continue;
            if (arg.Parameter is { Name: "value" } parameter
                && (value.BoxedFrom?.Type ?? value.EntryType) is { SpecialType: not SpecialType.System_Object } entry
                && !SymbolEqualityComparer.Default.Equals(_subst.Apply(entry), _subst.Apply(parameter.Type)))
                yield return _subst.Apply(entry);
            if (value.TypeOfOperand is { } typeOf) yield return _subst.Apply(typeOf);
        }
    }
}
