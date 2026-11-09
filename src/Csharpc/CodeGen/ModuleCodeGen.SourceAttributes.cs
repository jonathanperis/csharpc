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

    /// <summary>Whether a file's project turns the nullable annotations context on (Compiler: the Nullable property of its project).</summary>
    public Func<string, bool> ProjectAnnotations { get; init; } = _ => false;

    private readonly Dictionary<SyntaxTree, List<(int Position, bool Enabled)>> _nullableStates = [];

    /// <summary>
    /// Whether the nullable annotations context is on at a position: the project's default (off in generated code) as
    /// `#nullable` directives change it. The compilation codegen binds has the context switched off (nullability never
    /// changes code), so Roslyn's own answer can't be used.
    /// </summary>
    private bool AnnotationsAt(SyntaxTree tree, int position)
    {
        if (!_nullableStates.TryGetValue(tree, out var states))
        {
            var fileDefault = !IsGeneratedCode(tree) && ProjectAnnotations(string.IsNullOrEmpty(tree.FilePath) ? "" : Path.GetFullPath(tree.FilePath));
            states = [(-1, fileDefault)];
            var current = fileDefault;
            foreach (var trivia in tree.GetRoot().DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.GetStructure() is not NullableDirectiveTriviaSyntax { IsActive: true } directive) continue;
                if (!directive.TargetToken.IsKind(SyntaxKind.None) && !directive.TargetToken.IsKind(SyntaxKind.AnnotationsKeyword)) continue; // `warnings` only
                current = directive.SettingToken.Kind() switch { SyntaxKind.EnableKeyword => true, SyntaxKind.DisableKeyword => false, _ => fileDefault };
                states.Add((directive.FullSpan.End, current));
            }
            _nullableStates[tree] = states;
        }
        var enabled = states[0].Enabled;
        foreach (var (start, state) in states)
            if (start <= position) enabled = state;
        return enabled;
    }

    /// <summary>Roslyn's GeneratedCodeUtilities: such files have the nullable context off until they say otherwise.</summary>
    private static bool IsGeneratedCode(SyntaxTree tree)
    {
        var name = Path.GetFileName(tree.FilePath ?? "");
        if (name.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var trivia in tree.GetRoot().GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) continue;
            var text = trivia.ToString();
            if (text.Contains("<autogenerated", StringComparison.OrdinalIgnoreCase) || text.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool AnnotationsEnabled(ISymbol? symbol)
    {
        for (var depth = 0; symbol is not null && depth < 16; depth++)
        {
            var any = false;
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                any = true;
                var node = reference.GetSyntax();
                if (AnnotationsAt(reference.SyntaxTree, node.SpanStart) || AnnotationsAt(reference.SyntaxTree, node.Span.End)) return true;
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

    /// <summary>
    /// Would csc put [NullableContext]/[Nullable] on this type: some member in an annotations context mentions a reference type
    /// (csc hoists the most common flag of the members onto the type), or the base class's type arguments do. Interface
    /// implementations carry theirs on the implementation row, not on the type.
    /// </summary>
    private bool TypeHasNullableAttributes(INamedTypeSymbol type)
    {
        if (type.BaseType is { } baseType && baseType.TypeArguments.Any(NullableRelevant) && AnnotationsEnabled(type)) return true;
        foreach (var member in type.GetMembers())
        {
            var relevant = member switch
            {
                IFieldSymbol f => NullableRelevant(f.Type),
                IPropertySymbol p => NullableRelevant(p.Type) || p.Parameters.Any(x => NullableRelevant(x.Type)),
                IEventSymbol e => NullableRelevant(e.Type),
                IMethodSymbol { AssociatedSymbol: not null } => false, // accessors: their property's or event's
                IMethodSymbol m => NullableRelevant(m.ReturnType) || m.Parameters.Any(x => NullableRelevant(x.Type))
                    || m.TypeParameters.Any(tp => tp.ConstraintTypes.Length > 0 || tp.HasReferenceTypeConstraint || tp.HasNotNullConstraint),
                _ => false,
            };
            if (relevant && AnnotationsEnabled(member)) return true;
        }
        return false;
    }

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
        if (!AnnotationsEnabled(m) || NullableMethodAttributes(m) is not null) return false;
        return NullableRelevant(m.ReturnType) || m.Parameters.Any(p => NullableRelevant(p.Type))
            || m.TypeParameters.Any(tp => tp.ConstraintTypes.Length > 0 || tp.HasReferenceTypeConstraint || tp.HasNotNullConstraint);
    }

    private bool FieldHasUnlistedAttributes(IFieldSymbol f)
    {
        if (InSynthesizedType(f.ContainingType) || f.IsRequired || f.IsFixedSizeBuffer) return true;
        if (f.IsConst && f.Type.SpecialType == SpecialType.System_Decimal) return true; // [DecimalConstant]
        if (NeedsTypeAttribute(f.Type)) return true;
        return AnnotationsEnabled(f) && NullableFieldAttributes(f) is null && NullableRelevant(f.Type);
    }

    private bool PropertyHasUnlistedAttributes(IPropertySymbol p)
    {
        if (InSynthesizedType(p.ContainingType) || p.IsRequired || NeedsTypeAttribute(p.Type) || p.Parameters.Any(x => NeedsTypeAttribute(x.Type))) return true;
        return AnnotationsEnabled(p) && NullablePropertyAttributes(p) is null && (NullableRelevant(p.Type) || p.Parameters.Any(x => NullableRelevant(x.Type)));
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
        return AnnotationsEnabled(p) && NullableParameterAttributes(p) is null && NullableRelevant(p.Type);
    }

    /// <summary>`{ get; set; }` accessors (no body) of a property csc backs with a field.</summary>
    private static bool IsAutoAccessor(IMethodSymbol accessor) =>
        accessor.AssociatedSymbol is IPropertySymbol property && !property.IsAbstract && !property.IsExtern && property.ContainingType.TypeKind != TypeKind.Interface
        && accessor.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is AccessorDeclarationSyntax { Body: null, ExpressionBody: null });

    // ---- members of source symbols ----

    private AttributeSet SourceMethodAttributes(IMethodSymbol definition)
    {
        var synthesized = new List<AttributeInstance?>();
        // [NullableContext] comes first (before [CompilerGenerated] and [Extension]).
        if (NullableMethodAttributes(definition) is { } nullableContext) synthesized.AddRange(nullableContext);
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
        if (NullableFieldAttributes(definition) is { } nullableField) synthesized.AddRange(nullableField);
        // An auto-property's backing field: the attributes the compiler emits on it, before the property's [field: ...] ones.
        if (definition.IsImplicitlyDeclared && definition.AssociatedSymbol is IPropertySymbol)
        {
            var generated = new List<AttributeInstance>();
            AddCompilerGeneratedFieldAttributes(generated);
            synthesized.AddRange(generated);
        }
        return BuildAttributeSet(definition.GetAttributes(), synthesized, FieldHasUnlistedAttributes(definition));
    }

    // csc moves the null-state attributes of a property to its accessors (they are not on the property itself in metadata):
    // [AllowNull] and [DisallowNull] go to the setter's value parameter, [MaybeNull] and [NotNull] to the getter's return value.
    private const string AllowNullName = "System.Diagnostics.CodeAnalysis.AllowNullAttribute", DisallowNullName = "System.Diagnostics.CodeAnalysis.DisallowNullAttribute",
        MaybeNullName = "System.Diagnostics.CodeAnalysis.MaybeNullAttribute", NotNullName = "System.Diagnostics.CodeAnalysis.NotNullAttribute";

    private static bool IsForwardedToAccessor(AttributeData a) =>
        a.AttributeClass?.ToDisplayString() is AllowNullName or DisallowNullName or MaybeNullName or NotNullName;

    private AttributeSet SourcePropertyAttributes(IPropertySymbol definition) =>
        BuildAttributeSet(definition.GetAttributes().Where(a => !IsForwardedToAccessor(a)).ToList(), NullablePropertyAttributes(definition) ?? [], PropertyHasUnlistedAttributes(definition));

    /// <summary>
    /// The attributes of a method's return value (the sequence-0 parameter): [Nullable] where the return type's flags differ from
    /// the method's context, the declared [return: ...] attributes, and a getter's [MaybeNull]/[NotNull] moved from its property.
    /// </summary>
    private AttributeSet SourceReturnAttributes(IMethodSymbol definition)
    {
        var synthesized = new List<AttributeInstance?>();
        if (NullableReturnAttributes(definition) is { } nullableReturn) synthesized.AddRange(nullableReturn);
        var declared = definition.GetReturnTypeAttributes().ToList();
        if (definition is { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property })
            declared.AddRange(property.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() is MaybeNullName or NotNullName));
        return BuildAttributeSet(declared, synthesized, ReturnHasUnlistedAttributes(definition));
    }

    private bool ReturnHasUnlistedAttributes(IMethodSymbol m)
    {
        if (InSynthesizedType(m.ContainingType) || m.RefKind is RefKind.RefReadOnly || NeedsTypeAttribute(m.ReturnType)) return true;
        // Nullable annotations csharpc doesn't model exactly (the callers keep the list unknown).
        return AnnotationsEnabled(m) && NullableReturnAttributes(m) is null && NullableRelevant(m.ReturnType);
    }

    private AttributeSet SourceParameterAttributes(IParameterSymbol p)
    {
        // [ParamArray] comes first (csc emits it before the source's attributes).
        var synthesized = new List<AttributeInstance?>();
        if (p.IsParams && p.Type is IArrayTypeSymbol) synthesized.Add(PseudoAttribute("System.ParamArrayAttribute"));
        if (NullableParameterAttributes(p) is { } nullableParameter) synthesized.AddRange(nullableParameter);
        IEnumerable<AttributeData> declared = p.GetAttributes();
        // A setter's value parameter takes the property's [AllowNull] / [DisallowNull].
        if (p.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.PropertySet, AssociatedSymbol: IPropertySymbol setterProperty } setter && p.Ordinal == setter.Parameters.Length - 1)
            declared = declared.Concat(setterProperty.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() is AllowNullName or DisallowNullName)).ToList();
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
