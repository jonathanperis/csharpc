// Emit phase: writes the slice (the types and methods Slice reached) as a standalone assembly. Every reference to a CoreLib
// type that stays outside (a stop type) is re-pointed at the reference-pack assembly that declares it, so the result binds
// like any framework assembly compiled from IL: csharpc resolves those references through the reference pack and its
// shadows. Method bodies are copied instruction by instruction (tokens re-imported); nothing is changed in them.
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;

sealed class Emitter
{
    readonly Slice _s;
    readonly Cli _cli;
    readonly ModuleDefinition _m;
    readonly Dictionary<TypeDefinition, TypeDefinition> _types = new();
    readonly Dictionary<MethodDefinition, MethodDefinition> _methods = new();
    readonly Dictionary<FieldDefinition, FieldDefinition> _fields = new();
    readonly Dictionary<string, AssemblyNameReference> _asmRefs = new(StringComparer.Ordinal);
    int _skippedMembers;

    static readonly HashSet<string> CopiedAttributes = ["System.Runtime.CompilerServices.IsByRefLikeAttribute", "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Reflection.DefaultMemberAttribute", "System.Runtime.CompilerServices.IsReadOnlyAttribute", "System.Runtime.CompilerServices.InlineArrayAttribute",
        "System.FlagsAttribute", "System.Diagnostics.StackTraceHiddenAttribute"];

    public Emitter(Slice s, Cli cli)
    {
        _s = s; _cli = cli;
        var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(cli.SliceName, new Version(11, 0, 0, 0)), cli.SliceName, ModuleKind.Dll);
        _m = asm.MainModule;
        // System.Runtime first, so the module's TypeSystem takes it as the core library (primitive types encode as ELEMENT_TYPE_*).
        var sysRuntime = _s.RefAssemblyOf["System.Object"];
        _m.AssemblyReferences.Add(AsmRef(sysRuntime));
    }

    AssemblyNameReference AsmRef(AssemblyNameReference r)
    {
        if (_asmRefs.TryGetValue(r.Name, out var known)) return known;
        var n = new AssemblyNameReference(r.Name, r.Version) { PublicKeyToken = r.PublicKeyToken, Culture = r.Culture };
        if (!_m.AssemblyReferences.Any(x => x.Name == n.Name)) _m.AssemblyReferences.Add(n);
        return _asmRefs[r.Name] = _m.AssemblyReferences.First(x => x.Name == n.Name);
    }

    public void Run()
    {
        var order = _s.Types.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        // 1. type shells, outer before nested, then members, then bodies
        foreach (var t in order.Where(t => t.DeclaringType is null || Hoisted(t))) Shell(t);
        foreach (var t in order.Where(t => t.DeclaringType is not null && !Hoisted(t)).OrderBy(t => Depth(t))) Shell(t);
        foreach (var t in order) Declare(t);
        var hosted = DeclareHosted();
        foreach (var t in order) Bodies(t);
        foreach (var t in hosted) Bodies(t);

        EmbedStrings();
        _m.Assembly.Write(_cli.Out);
        var roots = _s.RootTypes.Concat(_s.RootTypes.SelectMany(Nested)).Distinct().Where(_types.ContainsKey);
        File.WriteAllLines(Path.ChangeExtension(_cli.Out, ".map"), roots.OrderBy(t => t.FullName, StringComparer.Ordinal).Select(t => MetadataName(t, false) + "=" + MetadataName(t, true))
            .Concat(_hostClass.OrderBy(kv => kv.Key.FullName, StringComparer.Ordinal).Select(kv => "members:" + MetadataName(kv.Key, false) + "=" + kv.Value.Namespace + "." + kv.Value.Name)));
        Console.WriteLine($"{_cli.Out}: {_types.Count} types, {_methods.Count} methods ({_methods.Values.Count(m => m.HasBody)} with bodies), {_fields.Count} fields; skipped {_skippedMembers} members");
        Console.WriteLine("UPSTREAM_TYPES=" + string.Join(",", _s.RootTypes.Where(t => t.DeclaringType is null).Select(t => t.FullName)));
    }

    /// <summary>
    /// The slice's SR looks its messages up in System.Private.CoreLib.Strings.resources; csharpc registers an IL assembly's embedded
    /// .resources at compile time (and puts every entry into the program), so the slice carries only the entries its SR getters name.
    /// </summary>
    Dictionary<string, string> Strings
    {
        get
        {
            if (_strings is not null) return _strings;
            _strings = new(StringComparer.Ordinal);
            foreach (var r in _s.Core.Resources.OfType<EmbeddedResource>().Where(r => r.Name == "System.Private.CoreLib.Strings.resources"))
            {
                using var reader = new System.Resources.ResourceReader(new MemoryStream(r.GetResourceData()));
                var e = reader.GetEnumerator();
                while (e.MoveNext()) if (e.Key is string k && e.Value is string v) _strings[k] = v;
            }
            return _strings;
        }
    }
    Dictionary<string, string> _strings;

    void EmbedStrings()
    {
        if (Environment.GetEnvironmentVariable("SLICER_EMBED_STRINGS") is null) return; // the SR getters carry their text (see Bodies)
        if (!_s.Types.Any(t => t.FullName == "System.SR")) return;
        var wanted = _s.Methods.Where(m => m.DeclaringType.FullName == "System.SR" && m.Name.StartsWith("get_")).Select(m => m.Name[4..]).ToHashSet(StringComparer.Ordinal);
        foreach (var r in _s.Core.Resources.OfType<EmbeddedResource>().Where(r => r.Name == "System.Private.CoreLib.Strings.resources"))
        {
            using var reader = new System.Resources.ResourceReader(new MemoryStream(r.GetResourceData()));
            var kept = new MemoryStream();
            int total = 0, count = 0;
            using (var writer = new System.Resources.ResourceWriter(kept))
            {
                var e = reader.GetEnumerator();
                while (e.MoveNext())
                {
                    total++;
                    if (e.Key is string key && e.Value is string value && wanted.Contains(key)) { writer.AddResource(key, value); count++; }
                }
                writer.Generate();
            }
            _m.Resources.Add(new EmbeddedResource(r.Name, r.Attributes, kept.ToArray()));
            Console.WriteLine($"resources: {count} of {total} entries of {r.Name} kept for {wanted.Count} SR getters");
        }
    }

    static IEnumerable<TypeDefinition> Nested(TypeDefinition t) => t.NestedTypes.SelectMany(n => new[] { n }.Concat(Nested(n)));

    static int Depth(TypeDefinition t) { int d = 0; for (var x = t.DeclaringType; x is not null; x = x.DeclaringType) d++; return d; }

    /// <summary>
    /// A nested type whose declaring type stays outside the slice (MemoryExtensions.TryWriteInterpolatedStringHandler: MemoryExtensions is
    /// csharpc's own): it becomes a top-level type of the slice named Outer_Name, and the .map names its real nested name.
    /// </summary>
    bool Hoisted(TypeDefinition t) => t.DeclaringType is { } outer && !_s.Types.Contains(outer);

    static string HoistedName(TypeDefinition t) => t.DeclaringType is { } outer ? HoistedName(outer) + "_" + t.Name : t.Name;

    static TypeDefinition Outermost(TypeDefinition t) { while (t.DeclaringType is not null) t = t.DeclaringType; return t; }

    void Shell(TypeDefinition t)
    {
        var hoisted = Hoisted(t);
        var attributes = hoisted ? (t.Attributes & ~TypeAttributes.VisibilityMask) | (t.IsNestedPublic ? TypeAttributes.Public : TypeAttributes.NotPublic) : t.Attributes;
        var n = new TypeDefinition(Prefixed(t), hoisted ? HoistedName(t) : t.Name, attributes);
        if (t.DeclaringType is null || hoisted) _m.Types.Add(n); else _types[t.DeclaringType].NestedTypes.Add(n);
        foreach (var gp in t.GenericParameters) n.GenericParameters.Add(new GenericParameter(gp.Name, n) { Attributes = gp.Attributes });
        if (t.HasLayoutInfo || t.IsExplicitLayout || t.IsSequentialLayout) { n.PackingSize = t.PackingSize; n.ClassSize = t.ClassSize; }
        _types[t] = n;
    }

    /// <summary>Slice types live under CsharpcCoreLib.Upstream.* so they never collide with the reference pack's types of the same
    /// name, and are "corelib" code to the compiler (policies; names in reflection and stack traces through the .map written below).</summary>
    string Prefixed(TypeDefinition t)
    {
        if (Hoisted(t)) return Outermost(t).Namespace.Length == 0 ? _cli.Prefix : _cli.Prefix + "." + Outermost(t).Namespace;
        if (t.DeclaringType is not null || t.Name.StartsWith('<') || t.FullName == "System.Private.CoreLib.Strings") return t.Namespace;
        return t.Namespace.Length == 0 ? _cli.Prefix : _cli.Prefix + "." + t.Namespace;
    }

    string MetadataName(TypeDefinition t, bool prefixed)
    {
        if (prefixed && Hoisted(t)) return Prefixed(t) + "." + HoistedName(t);
        string Plain(TypeDefinition x, bool withPrefix) =>
            x.DeclaringType is null
                ? (withPrefix ? Prefixed(x) : x.Namespace) is { Length: > 0 } ns ? ns + "." + x.Name : x.Name
                : Plain(x.DeclaringType, withPrefix) + "+" + x.Name;
        return Plain(t, prefixed);
    }

    void Declare(TypeDefinition t)
    {
        var n = _types[t];
        foreach (var (g, i) in t.GenericParameters.Select((g, i) => (g, i)))
            foreach (var c in g.Constraints) n.GenericParameters[i].Constraints.Add(new GenericParameterConstraint(Type(c.ConstraintType)));
        if (t.BaseType is not null) n.BaseType = Type(t.BaseType);
        if (Environment.GetEnvironmentVariable("SLICER_NOIFACE") is null) foreach (var i in t.Interfaces) n.Interfaces.Add(new InterfaceImplementation(Type(i.InterfaceType)));
        foreach (var a in t.CustomAttributes.Where(a => CopiedAttributes.Contains(a.AttributeType.FullName)))
            n.CustomAttributes.Add(new CustomAttribute(MethodRef(a.Constructor), a.GetBlob()));

        bool pid = t.Name == "<PrivateImplementationDetails>";
        foreach (var f in t.Fields)
        {
            if (pid && !_s.UsedFields.Contains(f)) { _skippedMembers++; continue; }
            var nf = new FieldDefinition(f.Name, f.Attributes, Type(f.FieldType));
            if (f.HasConstant) nf.Constant = f.Constant;
            if (f.HasLayoutInfo) nf.Offset = f.Offset;
            if (f.HasMarshalInfo) nf.MarshalInfo = f.MarshalInfo;
            if (f.InitialValue is { Length: > 0 } init) nf.InitialValue = init;
            n.Fields.Add(nf);
            _fields[f] = nf;
        }

        foreach (var m in t.Methods)
        {
            if (!_s.Methods.Contains(m)) { _skippedMembers++; continue; }
            var nm = new MethodDefinition(m.Name, m.Attributes, _m.TypeSystem.Void) { ImplAttributes = m.ImplAttributes, SemanticsAttributes = m.SemanticsAttributes,
                CallingConvention = m.CallingConvention, HasThis = m.HasThis, ExplicitThis = m.ExplicitThis };
            foreach (var gp in m.GenericParameters) nm.GenericParameters.Add(new GenericParameter(gp.Name, nm) { Attributes = gp.Attributes });
            n.Methods.Add(nm);
            _methods[m] = nm;
        }
        // Signatures and constraints once every method has a shell (a constraint may name the method's own parameters).
        foreach (var m in t.Methods.Where(_methods.ContainsKey))
        {
            var nm = _methods[m];
            nm.ReturnType = Type(m.ReturnType);
            foreach (var p in m.Parameters)
            {
                var np = new ParameterDefinition(p.Name, p.Attributes, Type(p.ParameterType));
                if (p.HasConstant) np.Constant = p.Constant;
                nm.Parameters.Add(np);
            }
            foreach (var (g, i) in m.GenericParameters.Select((g, i) => (g, i)))
                foreach (var c in g.Constraints) nm.GenericParameters[i].Constraints.Add(new GenericParameterConstraint(Type(c.ConstraintType)));
            if (m.IsPInvokeImpl && m.PInvokeInfo is { } pi)
            {
                var module = _m.ModuleReferences.FirstOrDefault(r => r.Name == pi.Module.Name) ?? AddModuleRef(pi.Module.Name);
                nm.PInvokeInfo = new PInvokeInfo(pi.Attributes, pi.EntryPoint, module);
            }
            foreach (var a in m.CustomAttributes.Where(a => CopiedAttributes.Contains(a.AttributeType.FullName)))
                nm.CustomAttributes.Add(new CustomAttribute(MethodRef(a.Constructor), a.GetBlob()));
        }
        foreach (var m in t.Methods.Where(_methods.ContainsKey))
            if (Environment.GetEnvironmentVariable("SLICER_NOOVR") is null) foreach (var o in m.Overrides) _methods[m].Overrides.Add(MethodRef(o));

        foreach (var p in t.Properties)
        {
            if (!(p.GetMethod is not null && _methods.ContainsKey(p.GetMethod)) && !(p.SetMethod is not null && _methods.ContainsKey(p.SetMethod))) continue;
            var np = new PropertyDefinition(p.Name, p.Attributes, Type(p.PropertyType));
            if (p.GetMethod is not null && _methods.TryGetValue(p.GetMethod, out var g)) np.GetMethod = g;
            if (p.SetMethod is not null && _methods.TryGetValue(p.SetMethod, out var s)) np.SetMethod = s;
            n.Properties.Add(np);
        }
        foreach (var e in t.Events)
        {
            var ne = new EventDefinition(e.Name, e.Attributes, Type(e.EventType));
            if (e.AddMethod is not null && _methods.TryGetValue(e.AddMethod, out var a)) ne.AddMethod = a;
            if (e.RemoveMethod is not null && _methods.TryGetValue(e.RemoveMethod, out var r)) ne.RemoveMethod = r;
            if (e.InvokeMethod is not null && _methods.TryGetValue(e.InvokeMethod, out var iv)) ne.InvokeMethod = iv;
            if (ne.AddMethod is not null || ne.RemoveMethod is not null) n.Events.Add(ne);
        }
    }

    readonly Dictionary<TypeDefinition, TypeDefinition> _hostClass = new();

    /// <summary>
    /// The primitives' implementations of the slice's static abstract interface members (Slice.Hosted), as static methods of one class per
    /// built-in type. The method is named after the member it implements, "System.IUtfChar`1.CastFrom" (the interface's metadata name
    /// without the slice prefix, '+' for a nested one), and the map file's "members:System.Char=CsharpcCoreLib.Upstream.System.CharInterfaceMembers"
    /// tells the compiler which built-in type the class stands in for (Frontend/UpstreamSlice.cs). Returns the CoreLib types whose bodies to copy.
    /// </summary>
    List<TypeDefinition> DeclareHosted()
    {
        var result = new List<TypeDefinition>();
        foreach (var (stop, methods) in _s.Hosted.OrderBy(kv => kv.Key.FullName, StringComparer.Ordinal))
        {
            var host = new TypeDefinition(_cli.Prefix + "." + stop.Namespace, stop.Name + "InterfaceMembers",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _m.TypeSystem.Object);
            _m.Types.Add(host);
            _hostClass[stop] = host;
            foreach (var m in methods)
            {
                var declared = m.Overrides.Select(o => o.Resolve()).First(d => d is not null && _types.ContainsKey(d.DeclaringType));
                var nm = new MethodDefinition(declared.DeclaringType.FullName.Replace('/', '+') + "." + declared.Name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, _m.TypeSystem.Void)
                    { ImplAttributes = m.ImplAttributes, CallingConvention = m.CallingConvention };
                host.Methods.Add(nm);
                _methods[m] = nm;
            }
            foreach (var m in methods)
            {
                var nm = _methods[m];
                nm.ReturnType = Type(m.ReturnType);
                foreach (var p in m.Parameters) nm.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Type(p.ParameterType)));
            }
            result.Add(stop);
        }
        return result;
    }

    ModuleReference AddModuleRef(string name) { var r = new ModuleReference(name); _m.ModuleReferences.Add(r); return r; }

    // ---------------------------------------------------------------------------------------------------------------
    // Bodies
    // ---------------------------------------------------------------------------------------------------------------

    void Bodies(TypeDefinition t)
    {
        foreach (var m in t.Methods.Where(_methods.ContainsKey))
        {
            if (!m.HasBody) continue;
            var nm = _methods[m];
            if (m.DeclaringType.FullName == "System.SR" && m.Name.StartsWith("get_") && m.IsStatic && m.Parameters.Count == 0 && Strings.TryGetValue(m.Name[4..], out var message))
            {
                // SR.Arg_Foo => GetResourceString("Arg_Foo"): the text itself, as ILLink's resource substitution does. No resource
                // table, no ResourceManager, nothing to look up at run time.
                var literal = nm.Body.GetILProcessor();
                literal.Emit(OpCodes.Ldstr, message);
                literal.Emit(OpCodes.Ret);
                continue;
            }
            if (_cli.Replacement(m) is { } forward)
            {
                // "Assembly:Ns.Type::Method": forward every argument to csharpc's own implementation of the contract member.
                var colon = forward.IndexOf(':'); var sep = forward.IndexOf("::", StringComparison.Ordinal);
                var typeName = forward[(colon + 1)..sep]; var dot = typeName.LastIndexOf('.');
                var scope = AsmRef(new AssemblyNameReference(forward[..colon], new Version(1, 0, 0, 0)));
                var call = new MethodReference(forward[(sep + 2)..], nm.ReturnType, new TypeReference(typeName[..dot], typeName[(dot + 1)..], _m, scope, false));
                foreach (var p in nm.Parameters) call.Parameters.Add(new ParameterDefinition(p.ParameterType));
                MethodReference target = call;
                if (nm.HasGenericParameters)
                {
                    // A generic method forwards with its own type parameters (ThrowHelper.ThrowKeyNotFoundException<T>(T key)).
                    foreach (var _ in nm.GenericParameters) call.GenericParameters.Add(new GenericParameter(call));
                    var instance = new GenericInstanceMethod(call);
                    foreach (var gp in nm.GenericParameters) instance.GenericArguments.Add(gp);
                    target = instance;
                }
                var il = nm.Body.GetILProcessor();
                if (nm.HasThis) il.Emit(OpCodes.Ldarg_0);
                foreach (var p in nm.Parameters) il.Emit(OpCodes.Ldarg, p);
                il.Emit(OpCodes.Call, target);
                il.Emit(OpCodes.Ret);
                continue;
            }
            var ob = m.Body;
            var nb = nm.Body;
            nb.InitLocals = ob.InitLocals; nb.MaxStackSize = ob.MaxStackSize;
            var vars = new List<VariableDefinition>();
            foreach (var v in ob.Variables) { var nv = new VariableDefinition(Type(v.VariableType)); nb.Variables.Add(nv); vars.Add(nv); }
            var map = new Dictionary<Instruction, Instruction>();
            foreach (var i in ob.Instructions) { var ni = Instruction.Create(OpCodes.Nop); ni.OpCode = i.OpCode; map[i] = ni; nb.Instructions.Add(ni); }
            foreach (var i in ob.Instructions)
            {
                var ni = map[i];
                ni.Operand = i.Operand switch
                {
                    Instruction target => map[target],
                    Instruction[] targets => targets.Select(x => map[x]).ToArray(),
                    VariableDefinition v => vars[v.Index],
                    ParameterDefinition p => p == ob.ThisParameter ? nb.ThisParameter : nm.Parameters[p.Index],
                    MethodReference mr => MethodRef(mr),
                    FieldReference fr => FieldRef(fr),
                    TypeReference tr => Type(tr),
                    CallSite cs => CallSiteOf(cs),
                    var other => other,
                };
            }
            foreach (var h in ob.ExceptionHandlers)
            {
                var nh = new ExceptionHandler(h.HandlerType)
                {
                    TryStart = map[h.TryStart], TryEnd = h.TryEnd is null ? null : map[h.TryEnd],
                    HandlerStart = map[h.HandlerStart], HandlerEnd = h.HandlerEnd is null ? null : map[h.HandlerEnd],
                    FilterStart = h.FilterStart is null ? null : map[h.FilterStart],
                    CatchType = h.CatchType is null ? null : Type(h.CatchType),
                };
                nb.ExceptionHandlers.Add(nh);
            }
        }
    }

    CallSite CallSiteOf(CallSite cs)
    {
        var n = new CallSite(Type(cs.ReturnType)) { CallingConvention = cs.CallingConvention, HasThis = cs.HasThis, ExplicitThis = cs.ExplicitThis };
        foreach (var p in cs.Parameters) n.Parameters.Add(new ParameterDefinition(Type(p.ParameterType)));
        return n;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // References
    // ---------------------------------------------------------------------------------------------------------------

    TypeReference Type(TypeReference t)
    {
        switch (t)
        {
            case null: return null;
            case GenericParameter gp: return Positional(gp.Position, gp.Type);
            case GenericInstanceType g:
            {
                var n = new GenericInstanceType(Type(g.ElementType));
                foreach (var a in g.GenericArguments) n.GenericArguments.Add(Type(a));
                return n;
            }
            case ArrayType a:
            {
                var n = new ArrayType(Type(a.ElementType), a.Rank);
                for (int i = 0; i < a.Rank; i++) n.Dimensions[i] = a.Dimensions[i];
                return n;
            }
            case ByReferenceType b: return new ByReferenceType(Type(b.ElementType));
            case PointerType p: return new PointerType(Type(p.ElementType));
            case PinnedType p: return new PinnedType(Type(p.ElementType));
            case SentinelType s: return new SentinelType(Type(s.ElementType));
            case RequiredModifierType rm: return new RequiredModifierType(Type(rm.ModifierType), Type(rm.ElementType));
            case OptionalModifierType om: return new OptionalModifierType(Type(om.ModifierType), Type(om.ElementType));
            case FunctionPointerType fp:
            {
                var n = new FunctionPointerType { ReturnType = Type(fp.ReturnType), CallingConvention = fp.CallingConvention, HasThis = fp.HasThis, ExplicitThis = fp.ExplicitThis };
                foreach (var p in fp.Parameters) n.Parameters.Add(new ParameterDefinition(Type(p.ParameterType)));
                return n;
            }
            case TypeDefinition d:
                if (_types.TryGetValue(d, out var copied)) return copied;
                return StopType(d);
            default:
                throw new InvalidOperationException("type reference outside CoreLib: " + t.FullName);
        }
    }

    TypeReference StopType(TypeDefinition d)
    {
        if (d.Namespace == "System" && d.DeclaringType is null)
        {
            var ts = _m.TypeSystem;
            switch (d.Name)
            {
                case "Void": return ts.Void;
                case "Boolean": return ts.Boolean;
                case "Char": return ts.Char;
                case "SByte": return ts.SByte;
                case "Byte": return ts.Byte;
                case "Int16": return ts.Int16;
                case "UInt16": return ts.UInt16;
                case "Int32": return ts.Int32;
                case "UInt32": return ts.UInt32;
                case "Int64": return ts.Int64;
                case "UInt64": return ts.UInt64;
                case "Single": return ts.Single;
                case "Double": return ts.Double;
                case "String": return ts.String;
                case "Object": return ts.Object;
                case "IntPtr": return ts.IntPtr;
                case "UIntPtr": return ts.UIntPtr;
                case "TypedReference": return ts.TypedReference;
            }
        }
        TypeReference r;
        if (_cli.Redirect.TryGetValue(d.FullName, out var redirect))
        {
            // "Assembly:Ns.Type`1": csharpc's own implementation of an internal contract type.
            var colon = redirect.IndexOf(':'); var full = redirect[(colon + 1)..]; var dot = full.LastIndexOf('.');
            return new TypeReference(full[..dot], full[(dot + 1)..], _m, AsmRef(new AssemblyNameReference(redirect[..colon], new Version(1, 0, 0, 0))), d.IsValueType);
        }
        if (d.DeclaringType is not null)
        {
            if (_types.ContainsKey(d.DeclaringType)) Console.Error.WriteLine($"warning: {d.FullName} is nested in the copied {d.DeclaringType.FullName} but stays outside the slice");
            r = new TypeReference(string.Empty, d.Name, _m, null, d.IsValueType) { DeclaringType = Type(d.DeclaringType) };
        }
        else
        {
            // Public types of the reference pack: its assembly; a contract-internal type: System.Private.CoreLib, which csharpc's
            // facade does not forward (the reference shows up as an unresolved token only if the method is reached).
            var scope = _s.RefAssemblyOf.TryGetValue(d.FullName, out var refAsm) ? AsmRef(refAsm) : CoreRef();
            r = new TypeReference(d.Namespace, d.Name, _m, scope, d.IsValueType);
        }
        // (generic parameter stand-ins on type references are not needed by signatures)
        return r;
    }

    // Signatures name generic parameters by kind and position only; the owners here are stand-ins that carry enough parameters.
    TypeReference _typeOwner; MethodReference _methodOwner;

    GenericParameter Positional(int position, GenericParameterType kind)
    {
        IGenericParameterProvider owner = kind == GenericParameterType.Type
            ? _typeOwner ??= new TypeReference("", "<T>", _m, null)
            : _methodOwner ??= new MethodReference("<M>", _m.TypeSystem.Void, new TypeReference("", "<D>", _m, null));
        while (owner.GenericParameters.Count <= position) owner.GenericParameters.Add(new GenericParameter((kind == GenericParameterType.Type ? "T" : "M") + owner.GenericParameters.Count, owner));
        return owner.GenericParameters[position];
    }

    AssemblyNameReference CoreRef() => AsmRef(new AssemblyNameReference("System.Private.CoreLib", new Version(11, 0, 0, 0)));

    MethodReference MethodRef(MethodReference mr)
    {
        if (mr is GenericInstanceMethod gim)
        {
            var n = new GenericInstanceMethod(MethodRef(gim.ElementMethod));
            foreach (var a in gim.GenericArguments) n.GenericArguments.Add(Type(a));
            return n;
        }
        if (mr is MethodDefinition md && _methods.TryGetValue(md, out var copied)) return copied;
        var r = new MethodReference(mr.Name, Type(mr.ReturnType), Type(mr.DeclaringType)) { HasThis = mr.HasThis, ExplicitThis = mr.ExplicitThis, CallingConvention = mr.CallingConvention };
        foreach (var p in mr.Parameters) r.Parameters.Add(new ParameterDefinition(Type(p.ParameterType)));
        for (int i = 0; i < mr.GenericParameters.Count; i++) r.GenericParameters.Add(new GenericParameter("M" + i, r));
        return r;
    }

    FieldReference FieldRef(FieldReference fr)
    {
        if (fr is FieldDefinition fd && _fields.TryGetValue(fd, out var copied)) return copied;
        return new FieldReference(fr.Name, Type(fr.FieldType), Type(fr.DeclaringType));
    }
}
