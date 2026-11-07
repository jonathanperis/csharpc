using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Assembly.GetCustomAttribute&lt;T&gt;() as compile-time metadata (closed world, ADR 0006): the attributes of
/// every IL assembly the program has, and of the program's own assembly (its sources plus the AssemblyInfo
/// the SDK generates), are known when it is compiled, so the call becomes a switch on the assembly's
/// identity whose cases construct the matching attribute from its metadata, as .NET's CustomAttribute
/// does at run time (a new object per call). Assemblies whose attributes csharpc doesn't have — the
/// shared framework, other projects of the build — throw NotSupportedException instead of answering null.
/// </summary>
public sealed partial class FunctionCodeGen
{
    internal LLVMValueRef AssemblyCustomAttribute(ITypeSymbol attributeType, LLVMValueRef assembly, IOperation at)
    {
        ThrowLibraryExceptionIf(_b.ICmp(LLVMIntPredicate.EQ, assembly, LlvmContext.Null(L.Ptr)),
            "System.ArgumentNullException", "Value cannot be null. (Parameter 'element')");
        var name = CallCoreLib("AssemblyImpl", "NameOf", assembly);
        var result = NewTemp(L.Ptr, "attribute");
        _b.Store(LlvmContext.Null(L.Ptr), result);
        var done = NewBlock("attribute.done");
        // KnownAssemblyAttributes: the program's own assembly, the IL ones and System.Private.CoreLib.
        foreach (var (identity, attributes) in KnownAssemblyAttributes())
        {
            var matches = attributes.Where(a => DerivesFromOrEquals(a.Class, attributeType)).ToList();
            var hit = NewBlock("attribute.assembly");
            var next = NewBlock("attribute.next");
            var same = CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], name, _m.StringLiteral(identity));
            _b.CondBr(same, hit, next);
            _b.PositionAtEnd(hit);
            if (SynthesizedForProgram(identity, attributeType))
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!, ProgramAttributesMessage);
            else if (matches.Any(m => m.Construct is null))
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!,
                    $"The assembly attribute '{attributeType.ToDisplayString()}' of {identity.Split(',')[0]} is not available to csharpc yet (csharpc has no class for it).");
            else if (matches.Count > 1)
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.Reflection.AmbiguousMatchException")!,
                    $"Multiple custom attributes of the same type '{attributeType.ToDisplayString()}' found.");
            else
            {
                if (matches.Count == 1) _b.Store(matches[0].Construct!(at), result);
                _b.Br(done);
            }
            _b.PositionAtEnd(next);
        }
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!,
            CallCoreLib("AssemblyImpl", "UnavailableAttributesMessage", name));
        _b.PositionAtEnd(done);
        return _b.Load(L.Ptr, result);
    }

    private const string ProgramAttributesMessage = "The custom attributes of the program's assembly are not available to csharpc yet for this attribute type: " +
        "csc adds [CompilationRelaxations], [RuntimeCompatibility] and [Debuggable] to the assembly, which csharpc doesn't list.";

    /// <summary>
    /// A query of the program's own (source) assembly that csc's synthesized assembly attributes could answer: for
    /// Attribute itself and for those attributes, the compiled-in list would be shorter than .NET's.
    /// </summary>
    private bool SynthesizedForProgram(string identity, ITypeSymbol attributeType) =>
        _m.MainIl is null && identity == _m.ProgramAssemblyName(null)
        && attributeType.ToDisplayString() is "System.Attribute" or "System.Runtime.CompilerServices.CompilationRelaxationsAttribute"
            or "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute" or "System.Diagnostics.DebuggableAttribute";

    /// <summary>
    /// assembly.GetCustomAttributes(typeof(A), inherit): an A[] of the matching attributes (assemblies have no
    /// base to inherit from, so `inherit` changes nothing), built from the same compile-time metadata.
    /// </summary>
    internal LLVMValueRef AssemblyCustomAttributes(INamedTypeSymbol attributeType, LLVMValueRef assembly, IOperation at)
    {
        NullCheck(assembly);
        // The A[] is handed out as the IEnumerable<A> of GetCustomAttributes<A>() (a conversion the call hides).
        foreach (var itf in new[] { SpecialType.System_Collections_Generic_IEnumerable_T, SpecialType.System_Collections_Generic_IReadOnlyCollection_T,
                     SpecialType.System_Collections_Generic_IReadOnlyList_T, SpecialType.System_Collections_Generic_ICollection_T, SpecialType.System_Collections_Generic_IList_T })
            _m.RequestArrayInterface(_m.Compilation.GetSpecialType(itf).Construct(attributeType));
        var name = CallCoreLib("AssemblyImpl", "NameOf", assembly);
        var result = NewTemp(L.Ptr, "attributes");
        var done = NewBlock("attributes.done");
        foreach (var (identity, attributes) in KnownAssemblyAttributes())
        {
            var matches = attributes.Where(a => DerivesFromOrEquals(a.Class, attributeType)).ToList();
            var hit = NewBlock("attributes.assembly");
            var next = NewBlock("attributes.next");
            _b.CondBr(CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], name, _m.StringLiteral(identity)), hit, next);
            _b.PositionAtEnd(hit);
            if (SynthesizedForProgram(identity, attributeType))
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!, ProgramAttributesMessage);
            else if (matches.Any(m => m.Construct is null))
                ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!,
                    $"The assembly attribute '{attributeType.ToDisplayString()}' of {identity.Split(',')[0]} is not available to csharpc yet (csharpc has no class for it).");
            else
            {
                var array = NewArray(attributeType, L.ConstInt(L.I64, matches.Count));
                for (var i = 0; i < matches.Count; i++)
                    StoreValue(matches[i].Construct!(at), ArrayElementAddress(array, attributeType, L.ConstInt(L.I64, i)));
                _b.Store(array, result);
                _b.Br(done);
            }
            _b.PositionAtEnd(next);
        }
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.NotSupportedException")!,
            CallCoreLib("AssemblyImpl", "UnavailableAttributesMessage", name));
        _b.PositionAtEnd(done);
        return _b.Load(L.Ptr, result);
    }

    /// <summary>
    /// Assembly.EntryPoint: the program's entry method for the assembly that has it (a standalone reflection record, as
    /// `ldtoken` of it would give), null for every other assembly. When csc's entry point is a method it synthesizes
    /// (`&lt;Main&gt;` bridging an async or Task-returning Main) csharpc has no symbol for it: NotSupportedException.
    /// </summary>
    internal LLVMValueRef AssemblyEntryPoint(LLVMValueRef assembly, IOperation at)
    {
        NullCheck(assembly);
        var entry = _m.ProgramEntryPoint;
        var owner = entry is null ? null : _m.AssemblyOf(entry.ContainingType);
        if (entry is null || owner is null) return LlvmContext.Null(L.Ptr);
        var result = NewTemp(L.Ptr, "entrypoint");
        _b.Store(LlvmContext.Null(L.Ptr), result);
        var hit = NewBlock("entrypoint.hit");
        var done = NewBlock("entrypoint.done");
        var name = CallCoreLib("AssemblyImpl", "NameOf", assembly);
        _b.CondBr(CallRuntime("csrt_string_equals", L.I1, [L.Ptr, L.Ptr], name, _m.StringLiteral(owner)), hit, done);
        _b.PositionAtEnd(hit);
        var bridged = _m.MainIl is null && (entry.IsAsync || entry.ReturnType.SpecialType is not (SpecialType.System_Void or SpecialType.System_Int32));
        _b.Store(bridged
            ? CallCoreLib("AssemblyImpl", "EntryPointUnsupported")
            : CallCoreLib("MemberRecords", "Method", _b.Cast(LLVMOpcode.PtrToInt, _m.MethodRecordGlobal(entry), T.NativeInt)), result);
        _b.Br(done);
        _b.PositionAtEnd(done);
        return _b.Load(L.Ptr, result);
    }

    /// <summary>(identity, attributes) of the assemblies whose attributes csharpc knows: the program's own and the IL ones.</summary>
    private List<(string Identity, IEnumerable<KnownAttribute> Attributes)> KnownAssemblyAttributes()
    {
        List<(string Identity, IEnumerable<KnownAttribute> Attributes)> assemblies = [];
        if (_m.MainIl is null && _m.ProgramAssemblyName(null) is { } programName)
        {
            var root = _m.ProgramAssemblies.FirstOrDefault(a => a.IsRoot);
            bool OfRoot(AttributeData a) => _m.ProgramAssemblies.Count <= 1
                || a.ApplicationSyntaxReference?.SyntaxTree.FilePath is not { } path
                || path == Frontend.RoslynFrontend.AssemblyInfoTreePath
                || root?.Files.Contains(Path.GetFullPath(path)) == true;
            // The program's own (root) assembly: its [assembly: ...] attributes, the SDK-generated AssemblyInfo
            // included (FrontendSettings.AssemblyInfo). Attributes of other projects folded into the compilation
            // belong to their own assemblies, which stay unknown.
            assemblies.Add((programName, _m.Compilation.Assembly.GetAttributes().Where(OfRoot).Select(Known)));
        }
        foreach (var il in _m.Il.Assemblies) assemblies.Add((il.Symbol.Identity.GetDisplayName(), il.Symbol.GetAttributes().Select(Known)));
        // System.Private.CoreLib: what typeof(object).Assembly says. The implementation assembly's own attributes, read at
        // compile time; those whose type the reference assemblies don't declare, or whose arguments are not plain values, are left out.
        if (Frontend.FrameworkAttributes.CoreLib is var (coreLibIdentity, coreLibAttributes))
            assemblies.Add((coreLibIdentity, coreLibAttributes.Select(FrameworkAttribute).OfType<KnownAttribute>().ToList()));
        return assemblies;
    }

    private static bool DerivesFromOrEquals(INamedTypeSymbol type, ITypeSymbol baseType)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
        return false;
    }

    /// <summary>An assembly attribute csharpc can construct: its class and how to build it (from Roslyn's AttributeData, or decoded from an assembly's metadata).</summary>
    private sealed record KnownAttribute(INamedTypeSymbol Class, Func<IOperation, LLVMValueRef>? Construct);

    private KnownAttribute Known(AttributeData data) =>
        new(data.AttributeClass!, at => ConstructAttribute(data, at));

    /// <summary>The System.Private.CoreLib attribute classes the corelib has (Reflection/AssemblyAttributes.cs): the others can't be instantiated by csharpc.</summary>
    private static readonly HashSet<string> s_constructibleFrameworkAttributes =
    [
        "System.Reflection.AssemblyInformationalVersionAttribute", "System.Reflection.AssemblyFileVersionAttribute", "System.Reflection.AssemblyVersionAttribute",
        "System.Reflection.AssemblyProductAttribute", "System.Reflection.AssemblyCompanyAttribute", "System.Reflection.AssemblyTitleAttribute",
        "System.Reflection.AssemblyConfigurationAttribute", "System.Reflection.AssemblyCopyrightAttribute", "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyMetadataAttribute", "System.Runtime.Versioning.TargetFrameworkAttribute",
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute", "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
        "System.Resources.NeutralResourcesLanguageAttribute",
    ];

    /// <summary>
    /// A System.Private.CoreLib attribute as a KnownAttribute: null when the reference assemblies don't declare its type
    /// (internal to the corelib). One csharpc can't build (no class for it in the corelib, no constructor for its argument
    /// types, a Type or array argument) is known without a Construct: asking for it is a NotSupportedException, not null.
    /// </summary>
    private KnownAttribute? FrameworkAttribute(Frontend.FrameworkAttribute attribute)
    {
        if (_m.Compilation.GetTypeByMetadataName(attribute.TypeName) is not { } type) return null;
        var constructor = type.InstanceConstructors.FirstOrDefault(c => c.DeclaredAccessibility == Accessibility.Public
            && c.Parameters.Length == attribute.Arguments.Count
            && c.Parameters.Select((p, i) => MetadataFullName(p.Type) == attribute.Arguments[i].Type).All(same => same));
        if (constructor is null || !s_constructibleFrameworkAttributes.Contains(attribute.TypeName)
            || attribute.Arguments.Any(a => a.Type == "System.Type" || a.Type.EndsWith("[]", StringComparison.Ordinal))
            || attribute.Named.Any(n => n.Type == "System.Type" || n.Type.EndsWith("[]", StringComparison.Ordinal))) return new KnownAttribute(type, null);
        return new KnownAttribute(type, at => ConstructAttribute(type, constructor,
            (i, t) => PlainValue(attribute.Arguments[i].Value, t, at),
            attribute.Named.Select(n => (n.Name, (Func<ITypeSymbol, IOperation>)(t => PlainValue(n.Value, t, at)))), at));
    }

    /// <summary>"System.String", "System.Diagnostics.DebuggableAttribute+DebuggingModes": how FrameworkAttribute names types.</summary>
    private static string MetadataFullName(ITypeSymbol type) => type is INamedTypeSymbol named
        ? named.ContainingType is { } outer ? MetadataFullName(outer) + "+" + named.MetadataName
            : (named.ContainingNamespace.IsGlobalNamespace ? "" : named.ContainingNamespace.ToDisplayString() + ".") + named.MetadataName
        : type.ToDisplayString();

    /// <summary>`new A(args) { Named = value }` from an attribute's metadata.</summary>
    private LLVMValueRef ConstructAttribute(AttributeData data, IOperation at)
    {
        var constructor = data.AttributeConstructor ?? throw CompileError.Unsupported($"attribute '{data.AttributeClass}' without a resolvable constructor", at);
        return ConstructAttribute(data.AttributeClass!, constructor, (i, t) => AttributeValue(data.ConstructorArguments[i], t, at),
            data.NamedArguments.Select(n => (n.Key, (Func<ITypeSymbol, IOperation>)(t => AttributeValue(n.Value, t, at)))), at);
    }

    private LLVMValueRef ConstructAttribute(INamedTypeSymbol attributeClass, IMethodSymbol constructor, Func<int, ITypeSymbol, IOperation> argument,
        IEnumerable<(string Name, Func<ITypeSymbol, IOperation> Value)> named, IOperation at)
    {
        var syntax = at.Syntax;
        var arguments = constructor.Parameters.Select((p, i) => (IArgumentOperation)new IlArgumentOperation(syntax, p, argument(i, p.Type))).ToImmutableArray();
        var instance = EmitValue(new IlObjectCreationOperation(syntax, constructor, arguments));
        foreach (var (memberName, value) in named)
        {
            var self = new IlValueOperation(syntax, attributeClass, () => instance, nonNull: true);
            var member = attributeClass.GetMembers(memberName).FirstOrDefault();
            switch (member)
            {
                case IPropertySymbol property:
                    EmitPropertySet(new IlPropertyReferenceOperation(syntax, property, self, [], null),
                        () => EmitValue(value(property.Type)), property.Type);
                    break;
                case IFieldSymbol field:
                    StoreValue(ToStorage(EmitValue(value(field.Type)), S(field.Type)), FieldAddressOn(instance, S(field)));
                    break;
                default:
                    throw CompileError.Unsupported($"named attribute argument '{memberName}' of '{attributeClass}'", at);
            }
        }
        return instance;
    }

    /// <summary>A constant read from an assembly's metadata (a string, a number, a bool, an enum's underlying value) as an operation of <paramref name="type"/>.</summary>
    private IOperation PlainValue(object? value, ITypeSymbol type, IOperation at) =>
        value is null
            ? new IlValueOperation(at.Syntax, type, () => LlvmContext.Null(T.Value(S(type))))
            : new IlValueOperation(at.Syntax, type, () => Coerce(Constant(value, type, at), type, S(type)));

    private IOperation AttributeValue(TypedConstant constant, ITypeSymbol type, IOperation at)
    {
        var syntax = at.Syntax;
        return constant.Kind switch
        {
            TypedConstantKind.Primitive or TypedConstantKind.Enum when constant.Type is { } valueType =>
                new IlValueOperation(syntax, type, () => Coerce(Constant(constant.Value, valueType, at), valueType, S(type))),
            TypedConstantKind.Type when constant.Value is ITypeSymbol typeOperand =>
                new IlValueOperation(syntax, type, () => TypeObject(S(typeOperand), at)),
            _ when constant.IsNull => new IlValueOperation(syntax, type, () => LlvmContext.Null(T.Value(S(type)))),
            _ => throw CompileError.Unsupported($"attribute argument of kind {constant.Kind}", at),
        };
    }
}
