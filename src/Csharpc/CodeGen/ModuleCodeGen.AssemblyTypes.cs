using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Csharpc.Frontend;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.CodeGen;

/// <summary>
/// Assembly.GetTypes() and its relatives (GetExportedTypes, DefinedTypes, Module.GetTypes) as compile-time
/// metadata (closed world, ADR 0006): the type list of the program's own assembly and of the IL assemblies that
/// are compiled in, in metadata (TypeDef) order, as arrays of method tables (corelib/Reflection/Assembly.cs reads
/// them). An IL assembly's TypeDef table is its list. A source assembly's list is what csc would write: the program
/// is emitted once with Roslyn (as the program's own build configures it) and its TypeDef table read back, which
/// has what the source declares plus the types csc synthesizes: closure classes, state machines, `&lt;&gt;c`,
/// `&lt;PrivateImplementationDetails&gt;`, anonymous types. Declared types are the source symbols (so `typeof(X)` is the
/// Type the list holds); the synthesized ones get name-only Type objects (a <see cref="SynthesizedType"/>), as csharpc
/// already does for the closure classes expression trees show.
/// Only programs that read a type list carry any of it.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>The program's compilation before csharpc's own rewrites: Roslyn emits it to learn what csc would produce.</summary>
    public CSharpCompilation? ProgramCompilation { get; init; }

    /// <summary>A type csc writes into the assembly that the source doesn't declare (names, flags and hierarchy from the emitted metadata).</summary>
    internal sealed class SynthesizedType
    {
        public required string ClrName { get; init; }
        public required string? Namespace { get; init; }
        public required TypeAttributes Attributes { get; init; }
        public required bool IsValueType { get; init; }
        public required bool HasInterfaces { get; init; }
        /// <summary>The declaring type: a symbol or another SynthesizedType (null: top level).</summary>
        public object? Declaring { get; set; }
        public INamedTypeSymbol? Base { get; set; }
        public bool BaseUnknown { get; set; }
        public TypeDefinitionHandle Handle { get; init; }
        public LLVMValueRef Mt;
    }

    /// <summary>(assembly full name, failure, partial) per assembly with a table entry {method tables, count}.</summary>
    private List<(string FullName, string? Failure, bool Partial, List<object> Types)>? _assemblyTypeLists;
    private LLVMValueRef _assemblyTypeTable;
    private string? _assemblyTypeNames;

    /// <summary>
    /// The type lists of every assembly this program can answer for: (full name, failure or "", "1" when the list is
    /// still right for the public types although incomplete) triples.
    /// </summary>
    internal LLVMValueRef AssemblyTypeNamesBlob()
    {
        BuildAssemblyTypeTable();
        return StringLiteral(_assemblyTypeNames!);
    }

    /// <summary>The table the names index: per assembly {ptr method tables, i32 count, i32 reserved}.</summary>
    internal LLVMValueRef AssemblyTypeTable()
    {
        BuildAssemblyTypeTable();
        return _assemblyTypeTable;
    }

    private void BuildAssemblyTypeTable()
    {
        if (_assemblyTypeLists is not null) return;
        var lists = new List<(string, string?, bool, List<object>)>();
        var entryType = Llvm.Struct(Llvm.Ptr, Llvm.I32, Llvm.I32);
        var entries = new List<LLVMValueRef>();
        void Add(string fullName, string? failure, List<object> types, bool partial = false)
        {
            List<LLVMValueRef> mts = [];
            if (failure is null)
                try { mts = types.Select(MethodTableOfListed).ToList(); }
                catch (CompileError e)
                {
                    // A type csharpc can't describe (explicit layout, ...): the list would be short, so there is none.
                    failure = $"Assembly.GetTypes() of '{fullName}': {e.Message}";
                    types = [];
                    partial = false;
                }
            lists.Add((fullName, failure, partial, types));
            var array = ConstTable("csharpc.assemblytypes.mts", Llvm.Ptr, mts);
            entries.Add(Llvm.ConstStruct(array, Llvm.ConstInt(Llvm.I32, mts.Count), Llvm.ConstInt(Llvm.I32, 0)));
        }
        if (MainIl is null)
        {
            if (ProgramAssemblyName(null) is { } name)
            {
                var (types, failure, partial) = ProgramTypeList();
                Add(name, failure, types, partial);
            }
        }
        foreach (var il in Il.Assemblies)
        {
            if (il.IsCoreLib) continue;
            var fullName = il.Symbol.Identity.GetDisplayName();
            if (il.IsFramework && il != MainIl)
            {
                Add(fullName, $"Assembly.GetTypes() of the shared-framework assembly '{il.Name}' is not supported by csharpc yet (its types aren't kept as a list).", []);
                continue;
            }
            Add(fullName, null, IlTypeList(il));
        }
        _assemblyTypeTable = ConstTable("csharpc.assemblytypes", entryType, entries);
        _assemblyTypeLists = lists;
        _assemblyTypeNames = Encode(lists.SelectMany(l => new[] { l.Item1, l.Item2 ?? "", l.Item3 ? "1" : "" }));
    }

    private LLVMValueRef MethodTableOfListed(object type) => type switch
    {
        INamedTypeSymbol symbol => MetadataMethodTable(symbol.OriginalDefinition),
        SynthesizedType synthesized => SynthesizedMethodTable(synthesized),
        _ => throw new InvalidOperationException(),
    };

    /// <summary>The types of an IL assembly in TypeDef order (`&lt;Module&gt;` excluded, as Assembly.GetTypes does).</summary>
    private static List<object> IlTypeList(Csharpc.Frontend.Il.IlAssembly il)
    {
        var list = new List<object>();
        foreach (var handle in il.Reader.TypeDefinitions)
        {
            if (MetadataTokens.GetRowNumber(handle) == 1) continue;
            if (il.Definition(handle) is INamedTypeSymbol type) list.Add(type);
        }
        return list;
    }

    // ---- the program's own assembly ----

    private (List<object> Types, string? Failure, bool Partial)? _programTypes;

    private (List<object> Types, string? Failure, bool Partial) ProgramTypeList() => _programTypes ??= ReadProgramTypes();

    /// <summary>The assemblies the emitted program references, as csc writes its AssemblyRef table (null: it couldn't be emitted).</summary>
    internal List<string>? ProgramReferences()
    {
        ProgramTypeList();
        return _programReferences;
    }

    private List<string>? _programReferences;

    private (List<object> Types, string? Failure, bool Partial) ReadProgramTypes()
    {
        const string prefix = "Assembly.GetTypes() of the program's assembly is not supported by csharpc yet";
        if (ProgramCompilation is null) return ([], $"{prefix}: its sources are not available.", false);
        if (ProgramAssemblies.Count > 1) return ([], $"{prefix}: the program folds several projects into one assembly.", false);
        byte[] image;
        try
        {
            var user = ProgramCompilation.RemoveSyntaxTrees(ProgramCompilation.SyntaxTrees.Where(RoslynFrontend.IsCoreLibTree));
            user = user.WithOptions(user.Options.WithOptimizationLevel(SourceOptimized ? OptimizationLevel.Release : OptimizationLevel.Debug));
            using var pe = new MemoryStream();
            var result = user.Emit(pe);
            if (!result.Success)
                return ([], $"{prefix}: the program could not be emitted as .NET would build it ({result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)}).", false);
            image = pe.ToArray();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ([], $"{prefix}: the program could not be emitted as .NET would build it ({e.Message}).", false); }

        using var reader = new PEReader(new MemoryStream(image));
        var metadata = reader.GetMetadataReader();
        _programReferences = [.. metadata.AssemblyReferences.Select(h => metadata.GetAssemblyReference(h).GetAssemblyName().FullName)];
        var names = new Dictionary<TypeDefinitionHandle, string>();
        string ClrName(TypeDefinitionHandle handle)
        {
            if (names.TryGetValue(handle, out var known)) return known;
            var definition = metadata.GetTypeDefinition(handle);
            var name = metadata.GetString(definition.Name);
            var declaring = definition.GetDeclaringType();
            var ns = metadata.GetString(definition.Namespace);
            return names[handle] = !declaring.IsNil ? ClrName(declaring) + "+" + name : ns.Length == 0 ? name : ns + "." + name;
        }

        // Generic types csc synthesizes (anonymous types, closures of generic methods) can't be listed: the list is then
        // incomplete, still right for the public types (a synthesized type is never public).
        string? incomplete = null;
        var listed = new Dictionary<TypeDefinitionHandle, object>();
        var order = new List<TypeDefinitionHandle>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            if (MetadataTokens.GetRowNumber(handle) == 1) continue; // <Module>
            var clrName = ClrName(handle);
            var definition = metadata.GetTypeDefinition(handle);
            order.Add(handle);
            var symbol = Compilation.GetTypeByMetadataName(clrName);
            if (symbol is not null && SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, Compilation.Assembly))
            {
                listed[handle] = symbol;
                continue;
            }
            if (definition.GetGenericParameters().Count > 0)
            {
                incomplete ??= $"{prefix}: csc generates the generic type '{clrName}' (an anonymous type, or a closure or state machine of a generic method).";
                continue;
            }
            var baseHandle = definition.BaseType;
            var isValueType = false;
            if (baseHandle.Kind == HandleKind.TypeReference)
            {
                var reference = metadata.GetTypeReference((TypeReferenceHandle)baseHandle);
                isValueType = metadata.GetString(reference.Namespace) == "System" && metadata.GetString(reference.Name) == "ValueType";
            }
            var ns = metadata.GetString(definition.Namespace);
            listed[handle] = new SynthesizedType
            {
                ClrName = clrName,
                Namespace = definition.GetDeclaringType().IsNil ? (ns.Length == 0 ? null : ns) : null,
                Attributes = definition.Attributes,
                IsValueType = isValueType,
                HasInterfaces = definition.GetInterfaceImplementations().Count > 0,
                Handle = handle,
            };
        }
        // A synthesized type nested in one that couldn't be listed goes too.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var synthesized in listed.Values.OfType<SynthesizedType>().ToList())
                if (metadata.GetTypeDefinition(synthesized.Handle).GetDeclaringType() is { IsNil: false } outer && !listed.ContainsKey(outer))
                {
                    listed.Remove(synthesized.Handle);
                    changed = true;
                }
        }
        // Hierarchy of the synthesized types: declaring types and base classes, now that every row is known.
        foreach (var synthesized in listed.Values.OfType<SynthesizedType>())
        {
            var definition = metadata.GetTypeDefinition(synthesized.Handle);
            var declaring = definition.GetDeclaringType();
            if (!declaring.IsNil) synthesized.Declaring = listed[declaring];
            switch (definition.BaseType.Kind)
            {
                case HandleKind.TypeReference:
                {
                    var reference = metadata.GetTypeReference((TypeReferenceHandle)definition.BaseType);
                    var baseName = metadata.GetString(reference.Namespace) + "." + metadata.GetString(reference.Name);
                    if (Compilation.GetTypeByMetadataName(baseName) is { } baseType) synthesized.Base = baseType;
                    else synthesized.BaseUnknown = true;
                    break;
                }
                case HandleKind.TypeDefinition when listed.TryGetValue((TypeDefinitionHandle)definition.BaseType, out var baseDefinition) && baseDefinition is INamedTypeSymbol sourceBase:
                    synthesized.Base = sourceBase;
                    break;
                default:
                    synthesized.BaseUnknown = true;
                    break;
            }
        }
        return ([.. order.Where(listed.ContainsKey).Select(h => listed[h])], incomplete, incomplete is not null);
    }

    // ---- name-only Type objects of the synthesized types ----

    private LLVMValueRef SynthesizedMethodTable(SynthesizedType type)
    {
        if (!type.Mt.IsNull) return type.Mt;
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(type.ClrName), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        var global = Module.AddGlobal("csrt.synthesized." + type.ClrName, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        type.Mt = global;
        RegisterTypeMethodTable(global, type);
        return global;
    }

    private bool IsVisibleSynthesized(SynthesizedType type) =>
        (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public
        || ((type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic
            && type.Declaring switch { INamedTypeSymbol s => IsVisible(s), SynthesizedType t => IsVisibleSynthesized(t), _ => false });

    /// <summary>Type.Namespace: a nested type reports its declaring type's.</summary>
    private static string? NamespaceOfListed(object type) => type switch
    {
        INamedTypeSymbol symbol => NamespaceOf(symbol),
        SynthesizedType { Declaring: { } declaring } => NamespaceOfListed(declaring),
        SynthesizedType synthesized => synthesized.Namespace,
        _ => null,
    };

    private TypeInfoRecord SynthesizedRecord(LLVMValueRef mt, SynthesizedType type)
    {
        var none = LlvmContext.Null(Llvm.Ptr);
        LLVMValueRef BaseTable()
        {
            if (type.Base is { } baseType) return MetadataMethodTable(baseType);
            return type.BaseUnknown ? none : MetadataMethodTable(Compilation.GetSpecialType(type.IsValueType ? SpecialType.System_ValueType : SpecialType.System_Object));
        }
        var attributes = type.Attributes;
        ulong flags = TI.TypeDefinition;
        if ((attributes & TypeAttributes.Interface) != 0) flags |= TI.Interface | TI.Abstract;
        else flags |= type.IsValueType ? TI.ValueType : TI.Class | TI.ReferenceType;
        if ((attributes & TypeAttributes.Abstract) != 0) flags |= TI.Abstract;
        if ((attributes & TypeAttributes.Sealed) != 0) flags |= TI.Sealed;
        flags |= (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public => TI.Public,
            TypeAttributes.NotPublic => TI.NotPublic,
            TypeAttributes.NestedPublic => TI.Nested | TI.NestedPublic,
            TypeAttributes.NestedPrivate => TI.Nested | TI.NestedPrivate,
            TypeAttributes.NestedAssembly => TI.Nested | TI.NestedAssembly,
            TypeAttributes.NestedFamily => TI.Nested | TI.NestedFamily,
            TypeAttributes.NestedFamORAssem => TI.Nested | TI.NestedFamOrAssem,
            TypeAttributes.NestedFamANDAssem => TI.Nested | TI.NestedFamAndAssem,
            _ => 0UL,
        };
        if (IsVisibleSynthesized(type)) flags |= TI.Visible;
        if (type.BaseUnknown) flags |= TI.NoHierarchy;
        // State machines and iterators implement interfaces csharpc doesn't list for them.
        if (type.HasInterfaces) flags |= TI.InterfacesInexact;
        var declaring = type.Declaring switch
        {
            INamedTypeSymbol s => MetadataMethodTable(s.OriginalDefinition),
            SynthesizedType t => SynthesizedMethodTable(t),
            _ => none,
        };
        return new TypeInfoRecord
        {
            Fields = [mt, BaseTable(), none, none, declaring, none],
            Namespace = NamespaceOfListed(type) is { } ns ? CString(ns) : none,
            Assembly = ProgramAssemblyName(null) is { } assembly ? CString(assembly) : none,
            Flags = flags, ArgumentCount = 0, Rank = 0, Variance = 0,
        };
    }
}
