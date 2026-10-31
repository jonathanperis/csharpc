using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;

namespace Csharpc.Frontend;

/// <summary>
/// Reflection-based System.Text.Json for a program given as IL (`csharpc app.dll`, ADR 0010): the same
/// synthesized contexts as for sources, with the roots found in the program's method bodies instead of its
/// syntax, and the same JSON source generator producing their metadata.
///
/// The generator only describes what its context can access (Compilation.IsSymbolAccessibleWithin, the
/// rule csc applies to the code it generates). From source the context is compiled into the program's own
/// assembly; from IL it lives in csharpc's binding compilation, another assembly to Roslyn, so the program
/// grants that compilation internals access — what an [InternalsVisibleTo] in the program would say. The
/// program's internal types and members then look to the generator exactly as they do from source, and
/// private nested types (which a source context reaches by being nested beside them) stay unsupported:
/// such calls are reported where they are lowered (FunctionCodeGen.JsonRoots.cs).
/// </summary>
public static partial class JsonReflectionContexts
{
    /// <summary>
    /// Root types (fully qualified) the IL program's synthesized contexts cover; null when the program is
    /// not IL or no contexts were made. Reflection-based calls on other types are reported when lowered.
    /// </summary>
    public static IReadOnlySet<string>? IlCoveredRoots { get; private set; }

    /// <summary>Does the IL program call System.Text.Json.JsonSerializer?</summary>
    public static bool ReferencesJsonSerializer(MetadataReader reader)
    {
        foreach (var handle in reader.TypeReferences)
        {
            var reference = reader.GetTypeReference(handle);
            if (reader.StringComparer.Equals(reference.Name, "JsonSerializer") && reader.StringComparer.Equals(reference.Namespace, "System.Text.Json"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Lets the compilation named <paramref name="toAssembly"/> see the program's internals, as an
    /// [InternalsVisibleTo] in its metadata would. Roslyn has no public way to add one to a metadata
    /// reference; its PE assembly's IVT map is filled in through reflection before any compilation binds
    /// the reference (assembly symbols are cached per metadata). False when this Roslyn has another shape.
    /// </summary>
    public static bool GrantInternalsAccess(MetadataReference reference, string toAssembly)
    {
        try
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            // The metadata the compilation binds (GetMetadata() would hand out a copy with its own PE assembly).
            if (reference is not PortableExecutableReference pe
                || typeof(PortableExecutableReference).GetMethod("GetMetadataNoCopy", instance, Type.EmptyTypes)?.Invoke(pe, null) is not AssemblyMetadata metadata)
                return false;
            var peAssembly = typeof(AssemblyMetadata).GetMethod("GetAssembly", instance, Type.EmptyTypes)?.Invoke(metadata, null);
            if (peAssembly is null) return false;
            var type = peAssembly.GetType();
            // Builds the lazy map from the assembly's own attributes.
            if (type.GetMethod("GetInternalsVisibleToPublicKeys", instance, [typeof(string)])?.Invoke(peAssembly, [toAssembly]) is System.Collections.IEnumerable keys)
                foreach (var _ in keys) { }
            if (type.GetField("_lazyInternalsVisibleToMap", instance)?.GetValue(peAssembly) is not Dictionary<string, List<ImmutableArray<byte>>> map) return false;
            if (!map.TryGetValue(toAssembly, out var list)) map[toAssembly] = list = [];
            // An empty public key: the granted assembly is not strong-named (csharpc's compilation isn't).
            if (!list.Any(k => k.IsEmpty)) list.Add(ImmutableArray<byte>.Empty);
            return true;
        }
        catch (Exception e) when (e is TargetInvocationException or MemberAccessException or InvalidCastException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Why an IL root type can't get generated metadata, or null: compiler-generated types (anonymous,
    /// file-local, closures) have names no C# can spell.
    /// </summary>
    public static string? UnsupportedIlRoot(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol a:
                return UnsupportedIlRoot(a.ElementType);
            case INamedTypeSymbol n:
                for (var c = n; c is not null; c = c.ContainingType)
                    if (c.MetadataName.Contains('<'))
                    {
                        if (c.MetadataName.StartsWith("<>f__AnonymousType", StringComparison.Ordinal)) return "System.Text.Json serialization of anonymous types";
                        // csc names a file-local type <File>F<hex>__Name.
                        var marker = c.MetadataName.IndexOf(">F", StringComparison.Ordinal);
                        var separator = c.MetadataName.IndexOf("__", StringComparison.Ordinal);
                        if (marker > 0 && separator > marker)
                            return $"System.Text.Json serialization of file-local type '{c.MetadataName[(separator + 2)..].Split('`')[0]}'";
                        return $"System.Text.Json serialization of compiler-generated type '{c.MetadataName}'";
                    }
                foreach (var argument in n.TypeArguments)
                    if (UnsupportedIlRoot(argument) is { } inner) return inner;
                return null;
            default:
                return null;
        }
    }

    /// <summary>The private or protected nested type in <paramref name="type"/> (itself, a containing type or a type argument), if any.</summary>
    public static INamedTypeSymbol? PrivateNestedPart(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol a:
                return PrivateNestedPart(a.ElementType);
            case INamedTypeSymbol n:
                for (var c = n; c.ContainingType is not null; c = c.ContainingType)
                    if (c.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal) return c;
                foreach (var argument in n.TypeArguments)
                    if (PrivateNestedPart(argument) is { } inner) return inner;
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// The reflection-based serializer calls of the program's method bodies (all of them, as the source path
    /// scans every syntax tree): the types they serialize, and every generic instantiation, to close roots in
    /// generic code.
    /// </summary>
    private static void ScanIl(IlAssembly program, INamedTypeSymbol serializer, RootFinder finder)
    {
        var reader = program.Reader;
        foreach (var typeHandle in reader.TypeDefinitions)
            foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
            {
                if (program.Definition(methodHandle) is not IMethodSymbol method) continue;
                IlMethodBody body;
                try
                {
                    if (program.Body(method) is not { } block) continue;
                    body = IlMethodBody.Decode(program, method, block);
                }
                catch (Exception e) when (e is IlException or BadImageFormatException) { continue; }
                // Best effort: a body the scan can't follow leaves its roots uncovered, which the lowering of
                // the call reports (FunctionCodeGen.JsonRoots.cs) if the program can reach it.
                try { new IlRootScan(program, body, serializer, finder).Run(); }
                catch (Exception e) when (e is not OutOfMemoryException) { }
            }
    }

    /// <summary>One method body: calls and their argument producers (a backward walk over the evaluation stack).</summary>
    private sealed class IlRootScan(IlAssembly program, IlMethodBody body, INamedTypeSymbol serializer, RootFinder finder)
    {
        private readonly ImmutableArray<IlInstruction> _code = body.Instructions;
        private HashSet<int>? _joins;

        public void Run()
        {
            for (var i = 0; i < _code.Length; i++)
            {
                var instruction = _code[i];
                switch (instruction.OpCode)
                {
                    case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn:
                        if (Member(instruction.Token) is not IMethodSymbol method) break;
                        if (method.IsGenericMethod && !method.IsDefinition) finder.AddMethodUse(method);
                        if (method.ContainingType is { IsGenericType: true } owner && !owner.IsDefinition) finder.AddTypeUse(owner);
                        if (instruction.OpCode == ILOpCode.Call && SymbolEqualityComparer.Default.Equals(method.ContainingType?.OriginalDefinition, serializer))
                            SerializerCall(i, method);
                        else if (instruction.OpCode == ILOpCode.Callvirt && method is { Name: "GetTypeInfo" or "TryGetTypeInfo", ContainingType.Name: "JsonSerializerOptions" })
                            TypeArguments(i, method);
                        break;
                    case ILOpCode.Ldtoken or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Initobj
                        or ILOpCode.Unbox_any or ILOpCode.Ldobj or ILOpCode.Stobj or ILOpCode.Sizeof:
                        if (TypeOf(instruction.Token) is INamedTypeSymbol { IsGenericType: true } used && !used.IsDefinition) finder.AddTypeUse(used);
                        break;
                }
            }
        }

        private void SerializerCall(int at, IMethodSymbol method)
        {
            if (method.Parameters.Any(IsJsonMetadataParameter)) return;
            foreach (var t in method.TypeArguments) finder.AddIlRoot(t);
            TypeArguments(at, method);
            // Serialize<object>(person), Serialize<Base>(derived), Serialize(person, typeof(...)): the value's own
            // static type, where source sees an implicit conversion.
            for (var p = 0; p < method.Parameters.Length; p++)
                if (method.Parameters[p] is { Name: "value" } parameter
                    && TypeProduced(Producer(at, method.Parameters.Length - 1 - p)) is { SpecialType: not SpecialType.System_Object } valueType
                    && !SymbolEqualityComparer.Default.Equals(valueType, parameter.Type))
                    finder.AddIlRoot(valueType);
        }

        /// <summary>typeof(T) and x.GetType() given as a System.Type argument.</summary>
        private void TypeArguments(int at, IMethodSymbol method)
        {
            for (var p = 0; p < method.Parameters.Length; p++)
            {
                if (method.Parameters[p].Type is not { Name: "Type", ContainingNamespace.Name: "System" }) continue;
                var producer = Producer(at, method.Parameters.Length - 1 - p);
                if (producer < 0 || _code[producer].OpCode is not (ILOpCode.Call or ILOpCode.Callvirt) || Member(_code[producer].Token) is not IMethodSymbol made) continue;
                switch (made)
                {
                    case { Name: "GetTypeFromHandle", ContainingType.Name: "Type" }
                        when Producer(producer, 0) is >= 0 and var token && _code[token].OpCode == ILOpCode.Ldtoken && TypeOf(_code[token].Token) is { } typeOf:
                        finder.AddIlRoot(typeOf);
                        break;
                    case { Name: "GetType", Parameters.Length: 0, IsStatic: false }
                        when TypeProduced(Producer(producer, 0)) is { SpecialType: not SpecialType.System_Object } instanceType:
                        finder.AddIlRoot(instanceType);
                        break;
                }
            }
        }

        /// <summary>
        /// The instruction that pushed the stack entry <paramref name="depth"/> below the top just before
        /// instruction <paramref name="at"/>, within its basic block; -1 when unknown.
        /// </summary>
        private int Producer(int at, int depth)
        {
            _joins ??= Joins();
            for (var j = at - 1; j >= 0; j--)
            {
                if (_joins.Contains(_code[j + 1].Offset) || !StackEffect(_code[j], out var pops, out var pushes)) return -1;
                if (depth < pushes) return _code[j].OpCode == ILOpCode.Dup ? Producer(j, 0) : j;
                depth += pops - pushes;
            }
            return -1;
        }

        /// <summary>Offsets control can reach other than by falling through: block starts the walk must not cross.</summary>
        private HashSet<int> Joins()
        {
            var joins = new HashSet<int>();
            foreach (var instruction in _code)
                if (instruction.Targets is { } targets) joins.UnionWith(targets);
            foreach (var region in body.Regions)
            {
                joins.Add(region.TryOffset);
                joins.Add(region.HandlerOffset);
                if (region.Kind == ExceptionRegionKind.Filter) joins.Add(region.FilterOffset);
            }
            return joins;
        }

        private ITypeSymbol? TypeProduced(int at)
        {
            if (at < 0) return null;
            var instruction = _code[at];
            switch (instruction.OpCode)
            {
                case ILOpCode.Newobj:
                    if (Member(instruction.Token) is IMethodSymbol constructor) return constructor.ContainingType;
                    return program.TryArrayAccessor(instruction.Token, body.Context, out var created, out _, out _) ? created : null;
                case ILOpCode.Call or ILOpCode.Callvirt:
                    return Member(instruction.Token) is IMethodSymbol { ReturnsVoid: false, RefKind: RefKind.None } m ? m.ReturnType : null;
                case ILOpCode.Ldfld or ILOpCode.Ldsfld:
                    return (Member(instruction.Token) as IFieldSymbol)?.Type;
                case ILOpCode.Box or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Unbox_any:
                    return TypeOf(instruction.Token);
                case ILOpCode.Newarr:
                    return TypeOf(instruction.Token) is { } element ? program.Compilation.CreateArrayTypeSymbol(element) : null;
                case ILOpCode.Ldstr:
                    return program.Compilation.GetSpecialType(SpecialType.System_String);
                case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3:
                    return Local(instruction.OpCode - ILOpCode.Ldloc_0);
                case ILOpCode.Ldloc or ILOpCode.Ldloc_s:
                    return Local((int)instruction.Int);
                case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
                    return Argument(instruction.OpCode - ILOpCode.Ldarg_0);
                case ILOpCode.Ldarg or ILOpCode.Ldarg_s:
                    return Argument((int)instruction.Int);
                default:
                    return null;
            }
        }

        private ITypeSymbol? Local(int index) => index < body.Locals.Length && body.Locals[index] is { IsByRef: false } local ? local.Type : null;

        private ITypeSymbol? Argument(int index)
        {
            var method = body.Method;
            if (!method.IsStatic) index--;
            if (index < 0) return method.ContainingType.IsValueType ? null : method.ContainingType;
            return index < method.Parameters.Length && method.Parameters[index].RefKind == RefKind.None ? method.Parameters[index].Type : null;
        }

        private ISymbol? Member(int token)
        {
            try { return program.ResolveMember(token, body.Context); }
            catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { return null; }
        }

        private ITypeSymbol? TypeOf(int token)
        {
            try { return program.ResolveType(MetadataTokens.EntityHandle(token), body.Context) is { IsByRef: false } t ? t.Type : null; }
            catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { return null; }
        }

        /// <summary>Pops and pushes of an instruction that doesn't end its block; false for those that do (and calli).</summary>
        private bool StackEffect(IlInstruction instruction, out int pops, out int pushes)
        {
            (pops, pushes) = (0, 0);
            switch (instruction.OpCode)
            {
                case ILOpCode.Nop or ILOpCode.Break or ILOpCode.Constrained or ILOpCode.Volatile or ILOpCode.Unaligned or ILOpCode.Tail or ILOpCode.Readonly:
                    return true;
                case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or ILOpCode.Ldarg or ILOpCode.Ldarg_s
                    or ILOpCode.Ldarga or ILOpCode.Ldarga_s or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
                    or ILOpCode.Ldloc or ILOpCode.Ldloc_s or ILOpCode.Ldloca or ILOpCode.Ldloca_s or ILOpCode.Ldnull or ILOpCode.Ldstr
                    or ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4
                    or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                    or ILOpCode.Ldc_i8 or ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8 or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Ldtoken
                    or ILOpCode.Ldftn or ILOpCode.Sizeof or ILOpCode.Arglist:
                    pushes = 1;
                    return true;
                case ILOpCode.Starg or ILOpCode.Starg_s or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
                    or ILOpCode.Stloc or ILOpCode.Stloc_s or ILOpCode.Pop or ILOpCode.Stsfld or ILOpCode.Initobj:
                    pops = 1;
                    return true;
                case ILOpCode.Dup:
                    (pops, pushes) = (1, 2);
                    return true;
                case ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Ldind_i1 or ILOpCode.Ldind_u1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_u2
                    or ILOpCode.Ldind_i4 or ILOpCode.Ldind_u4 or ILOpCode.Ldind_i8 or ILOpCode.Ldind_i or ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8
                    or ILOpCode.Ldind_ref or ILOpCode.Ldobj or ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_i8
                    or ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_u4 or ILOpCode.Conv_u8 or ILOpCode.Conv_u2 or ILOpCode.Conv_u1
                    or ILOpCode.Conv_i or ILOpCode.Conv_u or ILOpCode.Conv_r_un or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i2
                    or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u4
                    or ILOpCode.Conv_ovf_u8 or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u or ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un
                    or ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_i8_un or ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un
                    or ILOpCode.Conv_ovf_u4_un or ILOpCode.Conv_ovf_u8_un or ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un
                    or ILOpCode.Neg or ILOpCode.Not or ILOpCode.Ckfinite or ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any
                    or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Ldlen or ILOpCode.Ldvirtftn or ILOpCode.Newarr or ILOpCode.Localloc
                    or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Refanytype:
                    (pops, pushes) = (1, 1);
                    return true;
                case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un or ILOpCode.Rem or ILOpCode.Rem_un
                    or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un
                    or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un
                    or ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un
                    or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_i4
                    or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_i or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
                    or ILOpCode.Ldelem_ref or ILOpCode.Ldelem or ILOpCode.Ldelema:
                    (pops, pushes) = (2, 1);
                    return true;
                case ILOpCode.Stfld or ILOpCode.Stind_ref or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4 or ILOpCode.Stind_i8
                    or ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_i or ILOpCode.Stobj or ILOpCode.Cpobj:
                    pops = 2;
                    return true;
                case ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8
                    or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref or ILOpCode.Stelem or ILOpCode.Initblk or ILOpCode.Cpblk:
                    pops = 3;
                    return true;
                case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
                {
                    var newobj = instruction.OpCode == ILOpCode.Newobj;
                    int count;
                    bool returns;
                    if (Member(instruction.Token) is IMethodSymbol m)
                        (count, returns) = (m.Parameters.Length + (m.IsStatic || newobj ? 0 : 1), newobj || !m.ReturnsVoid);
                    else if (program.TryArrayAccessor(instruction.Token, body.Context, out _, out var name, out var parameters))
                        (count, returns) = (parameters + (newobj ? 0 : 1), newobj || name != "Set");
                    else return false;
                    (pops, pushes) = (count, returns ? 1 : 0);
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
