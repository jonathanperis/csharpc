using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.CodeGen;

/// <summary>
/// The custom attributes of source members as CoreCLR reports them. Roslyn's symbols list what the source says
/// (including pseudo-custom attributes like [MethodImpl] that metadata keeps in flags) and omit what csc
/// synthesizes into metadata ([Nullable], [CompilerGenerated], [ParamArray], [IsReadOnly], state machine
/// attributes, ...), so a list taken from the symbols alone differs from the JIT's. This reproduces the
/// synthesized attributes csharpc can produce exactly (auto-property accessors and backing fields, field-like
/// event accessors, `params`, [Extension]), drops what reflection never lists, and marks the member's list
/// *unknown* when csc would add attributes csharpc doesn't (nullable annotations on reference types, async and
/// iterator methods, readonly struct members, `in` parameters, ...): the untyped queries (GetCustomAttributes(),
/// GetCustomAttributesData, IsDefined of a System attribute) then throw NotSupportedException instead of answering
/// with a shorter list. A query for an attribute csc never synthesizes (a program's own, [Obsolete]) stays exact.
/// The list's count word carries the flag (ReflectionRuntime.AttributesUnknown) and the number of trailing
/// pseudo-custom attributes: CoreCLR lists those last in GetCustomAttributes and first in GetCustomAttributesData.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>An attribute list plus what its count word also says (see <see cref="AttributeCount"/>).</summary>
    internal sealed class AttributeRecordList : List<AttributeRecordData>
    {
        public bool Unknown;
        public int Pseudo;
    }

    /// <summary>A member's attributes: metadata attributes first, then the pseudo-custom ones (<see cref="Pseudo"/> of them).</summary>
    internal readonly record struct AttributeSet(List<AttributeInstance> Instances, int Pseudo, bool Unknown);

    /// <summary>The count word of an attribute list: records in bits 0-23, trailing pseudo-custom attributes in 24-29, "unknown" in bit 30.</summary>
    private LLVMValueRef AttributeCount(List<AttributeRecordData> list) =>
        Llvm.ConstInt(Llvm.I32, list.Count | (list is AttributeRecordList l ? (l.Pseudo << 24) | (l.Unknown ? 0x40000000 : 0) : 0));

    private AttributeRecordList Attributes(AttributeSet set)
    {
        var list = Attributes(set.Instances);
        list.Pseudo = set.Pseudo;
        list.Unknown = set.Unknown;
        return list;
    }

    // Attributes reflection never lists: [MethodImpl] and [StructLayout] are flags of the method / type, [SpecialName]
    // is a flag, [DefaultParameterValue] is decoded into the parameter's default.
    private static readonly HashSet<string> s_unlistedAttributes =
    [
        "System.Runtime.CompilerServices.MethodImplAttribute", "System.Runtime.CompilerServices.SpecialNameAttribute",
        "System.Runtime.InteropServices.StructLayoutAttribute",
    ];

    // CoreCLR's pseudo-custom attributes (PseudoCustomAttribute): reflection lists them from flags, in its own place.
    private static readonly HashSet<string> s_pseudoAttributes =
    [
        "System.Runtime.InteropServices.FieldOffsetAttribute", "System.NonSerializedAttribute", "System.Runtime.InteropServices.MarshalAsAttribute",
        "System.Runtime.InteropServices.DllImportAttribute", "System.Runtime.InteropServices.PreserveSigAttribute",
        "System.Runtime.InteropServices.InAttribute", "System.Runtime.InteropServices.OutAttribute", "System.Runtime.InteropServices.OptionalAttribute",
        "System.SerializableAttribute", "System.Runtime.InteropServices.ComImportAttribute",
    ];

    private const string InAttributeName = "System.Runtime.InteropServices.InAttribute", OutAttributeName = "System.Runtime.InteropServices.OutAttribute",
        OptionalAttributeName = "System.Runtime.InteropServices.OptionalAttribute", DllImportName = "System.Runtime.InteropServices.DllImportAttribute",
        PreserveSigName = "System.Runtime.InteropServices.PreserveSigAttribute";

    /// <summary>Whether a declared attribute of a type is one reflection doesn't list (StructLayout, SpecialName).</summary>
    private static bool IsUnlistedAttribute(AttributeData a) => a.AttributeClass?.ToDisplayString() is { } name && s_unlistedAttributes.Contains(name);

    /// <summary>
    /// <paramref name="synthesized"/> attributes csc adds (listed first), then the declared ones in source order, with pseudo-custom
    /// ones moved to the end; <paramref name="derivedPseudo"/> are pseudo attributes taken from flags (parameters' In/Out/Optional).
    /// </summary>
    private AttributeSet BuildAttributeSet(IEnumerable<AttributeData> declared, IEnumerable<AttributeInstance?> synthesized, bool unknown,
        IEnumerable<AttributeInstance?>? derivedPseudo = null, bool derivesInOutOptional = false)
    {
        var real = synthesized.OfType<AttributeInstance>().ToList();
        var pseudo = new List<AttributeInstance>();
        var pseudoNames = new List<string>();
        foreach (var a in declared)
        {
            var name = a.AttributeClass?.ToDisplayString() ?? "";
            if (s_unlistedAttributes.Contains(name)) continue;
            // Decoded into the parameter's constant (and Optional flag): csc's exact result isn't reproduced.
            if (name == "System.Runtime.InteropServices.DefaultParameterValueAttribute") { unknown = true; continue; }
            if (derivesInOutOptional && name is InAttributeName or OutAttributeName or OptionalAttributeName) continue;
            if (FromAttributeData(a) is not { } instance) { unknown = true; continue; }
            if (s_pseudoAttributes.Contains(name)) { pseudo.Add(instance); pseudoNames.Add(name); }
            else real.Add(instance);
            // [DllImport] implies PreserveSig unless it says otherwise: CoreCLR lists it right after.
            if (name == DllImportName)
            {
                if (a.NamedArguments.Any(n => n.Key == "PreserveSig")) unknown = true;
                else if (!declared.Any(d => d.AttributeClass?.ToDisplayString() == PreserveSigName) && PseudoAttribute(PreserveSigName) is { } preserve)
                {
                    pseudo.Add(preserve);
                    pseudoNames.Add(PreserveSigName);
                }
            }
        }
        if (derivedPseudo is not null)
            foreach (var d in derivedPseudo.OfType<AttributeInstance>())
            {
                pseudo.Add(d);
                pseudoNames.Add(d.Constructor.ContainingType.ToDisplayString());
            }
        // The order of several pseudo attributes among themselves is known only for [DllImport] + its PreserveSig.
        if (pseudo.Count > 1 && !(pseudo.Count == 2 && pseudoNames[0] == DllImportName && pseudoNames[1] == PreserveSigName)) unknown = true;
        var instances = real;
        instances.AddRange(pseudo);
        return new AttributeSet(instances, pseudo.Count, unknown);
    }

    // ---- what csc synthesizes that csharpc doesn't reproduce ----

    private bool AnnotationsEnabled(ISymbol? symbol)
    {
        for (var depth = 0; symbol is not null && depth < 16; depth++)
        {
            var any = false;
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                any = true;
                var node = reference.GetSyntax();
                var model = Compilation.GetSemanticModel(reference.SyntaxTree);
                if (model.GetNullableContext(node.SpanStart).AnnotationsEnabled() || model.GetNullableContext(node.Span.End).AnnotationsEnabled()) return true;
            }
            if (any) return false;
            symbol = symbol switch
            {
                IMethodSymbol { AssociatedSymbol: { } associated } => associated,
                IFieldSymbol { AssociatedSymbol: { } associated } => associated,
                _ => symbol.ContainingSymbol,
            };
        }
        return false;
    }

    /// <summary>Does csc write a nullable byte for this type (a reference type, or a type parameter that may be one, anywhere in it)?</summary>
    private static bool NullableRelevant(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol tp => !(tp.HasValueTypeConstraint || tp.HasUnmanagedTypeConstraint),
        IArrayTypeSymbol or IDynamicTypeSymbol or IFunctionPointerTypeSymbol => true,
        IPointerTypeSymbol => false,
        INamedTypeSymbol n => n.IsReferenceType || n.TypeArguments.Any(NullableRelevant),
        _ => false,
    };

    /// <summary>[Dynamic], [TupleElementNames], [NativeInteger] and friends: attributes csc writes for what the type spells.</summary>
    private static bool NeedsTypeAttribute(ITypeSymbol type) => type switch
    {
        IDynamicTypeSymbol or IFunctionPointerTypeSymbol => true,
        IArrayTypeSymbol array => NeedsTypeAttribute(array.ElementType),
        IPointerTypeSymbol pointer => NeedsTypeAttribute(pointer.PointedAtType),
        INamedTypeSymbol named => named.IsNativeIntegerType || (named.IsTupleType && named.TupleElements.Any(e => !e.IsImplicitlyDeclared))
            || named.TypeArguments.Any(NeedsTypeAttribute) || (named.ContainingType is { } outer && NeedsTypeAttribute(outer)),
        _ => false,
    };

    private static bool InSynthesizedType(INamedTypeSymbol type) => type.IsRecord || type.IsAnonymousType || type.IsTupleType;

    private static bool IsIterator(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
            if (reference.GetSyntax().DescendantNodes(n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)).OfType<YieldStatementSyntax>().Any())
                return true;
        return false;
    }

    private bool MethodHasUnlistedAttributes(IMethodSymbol m)
    {
        var type = m.ContainingType;
        if (m.IsAsync || IsIterator(m) || InSynthesizedType(type)) return true;
        // readonly members of a non-readonly struct (an auto-property's getter is implicitly readonly) get [IsReadOnly].
        if (m.IsReadOnly && !type.IsReadOnly) return true;
        if (type.TypeKind == TypeKind.Struct && !type.IsReadOnly && !m.IsStatic && m.MethodKind == MethodKind.PropertyGet && IsAutoAccessor(m)) return true;
        if (!AnnotationsEnabled(m)) return false;
        return NullableRelevant(m.ReturnType) || m.Parameters.Any(p => NullableRelevant(p.Type))
            || m.TypeParameters.Any(tp => tp.ConstraintTypes.Length > 0 || tp.HasReferenceTypeConstraint || tp.HasNotNullConstraint);
    }

    private bool FieldHasUnlistedAttributes(IFieldSymbol f)
    {
        if (InSynthesizedType(f.ContainingType) || f.IsRequired || f.IsFixedSizeBuffer) return true;
        if (f.IsConst && f.Type.SpecialType == SpecialType.System_Decimal) return true; // [DecimalConstant]
        if (NeedsTypeAttribute(f.Type)) return true;
        return AnnotationsEnabled(f) && NullableRelevant(f.Type);
    }

    private bool PropertyHasUnlistedAttributes(IPropertySymbol p)
    {
        if (InSynthesizedType(p.ContainingType) || p.IsRequired || NeedsTypeAttribute(p.Type) || p.Parameters.Any(x => NeedsTypeAttribute(x.Type))) return true;
        return AnnotationsEnabled(p) && (NullableRelevant(p.Type) || p.Parameters.Any(x => NullableRelevant(x.Type)));
    }

    private bool ParameterHasUnlistedAttributes(IParameterSymbol p)
    {
        if (p.ContainingType is { } type && InSynthesizedType(type)) return true;
        if (p.RefKind is RefKind.In or RefKind.RefReadOnlyParameter) return true;                                 // [IsReadOnly]
        // [ScopedRef] / [ScopedValue] for the `scoped` keyword (`out` parameters are scoped implicitly, without an attribute).
        if (p.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is ParameterSyntax parameter && parameter.Modifiers.Any(SyntaxKind.ScopedKeyword))) return true;
        if (p.IsParams && p.Type is not IArrayTypeSymbol) return true;                                             // [ParamCollection]
        if (p.HasExplicitDefaultValue && p.Type.SpecialType == SpecialType.System_Decimal) return true;            // [DecimalConstant]
        if (NeedsTypeAttribute(p.Type)) return true;
        return AnnotationsEnabled(p) && NullableRelevant(p.Type);
    }

    /// <summary>`{ get; set; }` accessors (no body) of a property csc backs with a field.</summary>
    private static bool IsAutoAccessor(IMethodSymbol accessor) =>
        accessor.AssociatedSymbol is IPropertySymbol property && !property.IsAbstract && !property.IsExtern && property.ContainingType.TypeKind != TypeKind.Interface
        && accessor.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is AccessorDeclarationSyntax { Body: null, ExpressionBody: null });

    // ---- members of source symbols ----

    private AttributeSet SourceMethodAttributes(IMethodSymbol definition)
    {
        var synthesized = new List<AttributeInstance?>();
        // The accessors csc synthesizes for a field-like event, and an auto-property's, are [CompilerGenerated]; the
        // metadata lists that before the attributes the source gives them.
        if (definition is { MethodKind: MethodKind.EventAdd or MethodKind.EventRemove, IsImplicitlyDeclared: true }
            || (definition.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet && IsAutoAccessor(definition)))
            synthesized.Add(PseudoAttribute("System.Runtime.CompilerServices.CompilerGeneratedAttribute"));
        // Roslyn reports [Extension] as IsExtensionMethod, not among the attributes; the metadata has it
        // (ExpressionStringBuilder prints `xs.Where(...)` for an extension method's call because of it).
        var declared = definition.GetAttributes();
        if (definition.IsExtensionMethod && !declared.Any(a => a.AttributeClass?.Name == "ExtensionAttribute"))
            synthesized.Add(PseudoAttribute("System.Runtime.CompilerServices.ExtensionAttribute"));
        return BuildAttributeSet(declared, synthesized, MethodHasUnlistedAttributes(definition));
    }

    private AttributeSet SourceFieldAttributes(IFieldSymbol definition)
    {
        var synthesized = new List<AttributeInstance?>();
        // An auto-property's backing field: the attributes the compiler emits on it, before the property's [field: ...] ones.
        if (definition.IsImplicitlyDeclared && definition.AssociatedSymbol is IPropertySymbol)
        {
            var generated = new List<AttributeInstance>();
            AddCompilerGeneratedFieldAttributes(generated);
            synthesized.AddRange(generated);
        }
        return BuildAttributeSet(definition.GetAttributes(), synthesized, FieldHasUnlistedAttributes(definition));
    }

    private AttributeSet SourcePropertyAttributes(IPropertySymbol definition) =>
        BuildAttributeSet(definition.GetAttributes(), [], PropertyHasUnlistedAttributes(definition));

    private AttributeSet SourceParameterAttributes(IParameterSymbol p)
    {
        // [ParamArray] comes first (csc emits it before the source's attributes).
        var synthesized = new List<AttributeInstance?>();
        if (p.IsParams && p.Type is IArrayTypeSymbol) synthesized.Add(PseudoAttribute("System.ParamArrayAttribute"));
        var declared = p.GetAttributes();
        bool Has(string name) => declared.Any(a => a.AttributeClass?.ToDisplayString() == name);
        // In / Out / Optional are flags in metadata; reflection lists them as pseudo attributes, once.
        var derived = new List<AttributeInstance?>();
        if (p.RefKind is RefKind.In or RefKind.RefReadOnlyParameter || Has(InAttributeName)) derived.Add(PseudoAttribute(InAttributeName));
        if (p.RefKind == RefKind.Out || Has(OutAttributeName)) derived.Add(PseudoAttribute(OutAttributeName));
        if (p.IsOptional || Has(OptionalAttributeName)) derived.Add(PseudoAttribute(OptionalAttributeName));
        return BuildAttributeSet(declared, synthesized, ParameterHasUnlistedAttributes(p), derived, derivesInOutOptional: true);
    }

    // ---- methods csc adds to a type that Roslyn's symbols don't have ----

    /// <summary>
    /// Local functions (always methods of the declaring type, named `&lt;M&gt;g__F|0_0`) and lambdas that use `this` (methods
    /// `&lt;M&gt;b__0_0`) are private methods csc adds to a type; Roslyn lists neither among the type's members.
    /// </summary>
    private bool HasCompilerGeneratedMethods(INamedTypeSymbol type)
    {
        IEnumerable<SyntaxNode> roots = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax());
        if (type.IsImplicitlyDeclared && !roots.Any()) // top-level statements: the synthesized Program
            roots = Compilation.SyntaxTrees.Select(t => t.GetRoot()).OfType<CompilationUnitSyntax>().SelectMany(c => c.Members.OfType<GlobalStatementSyntax>());
        foreach (var root in roots)
        {
            SemanticModel? model = null;
            foreach (var node in root.DescendantNodesAndSelf())
            {
                if (node is LocalFunctionStatementSyntax) return true;
                if (node is not AnonymousFunctionExpressionSyntax lambda || lambda.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
                model ??= Compilation.GetSemanticModel(root.SyntaxTree);
                if (UsesThis(model, lambda)) return true;
            }
        }
        return false;
    }

    private static bool UsesThis(SemanticModel model, AnonymousFunctionExpressionSyntax lambda)
    {
        foreach (var node in lambda.DescendantNodes())
        {
            if (node is ThisExpressionSyntax or BaseExpressionSyntax) return true;
            if (node is not IdentifierNameSyntax identifier) continue;
            // `x.Name`, `x?.Name` and `new T { Name = ... }` don't name a member of `this`.
            if (identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier) continue;
            if (identifier.Parent is MemberBindingExpressionSyntax) continue;
            if (identifier.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax, Left: var left } && left == identifier) continue;
            if (model.GetSymbolInfo(identifier).Symbol is (IFieldSymbol or IPropertySymbol or IEventSymbol
                    or IMethodSymbol { MethodKind: not (MethodKind.LocalFunction or MethodKind.AnonymousFunction) }) and { IsStatic: false })
                return true;
        }
        return false;
    }
}
