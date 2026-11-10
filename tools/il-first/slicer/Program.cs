// Spike for issue #191 part B (mixed-mode CoreLib): which part of System.Private.CoreLib's IL does a listed type need,
// and what does it need from the types csharpc keeps (the contract)?
//
//   dotnet run --project tools/il-first/slicer -- analyze --roots "System.Collections.Generic.List`1[,...]" [--out report.tsv]
//   dotnet run --project tools/il-first/slicer -- emit    --roots ... --out slice.dll          (see Emit.cs)
//
// Method-level reachability from every method of the root types (their public API and everything else), through calls,
// field accesses, type tokens, base types, interfaces and the virtual methods of every type that becomes used. A type that
// the reference pack declares publicly is a STOP type (programs bind to it there and csharpc serves it from its shadows or
// intrinsics); every other CoreLib type reached joins the slice. What the slice needs from a stop type that the reference
// pack does not declare (internal members) is the contract; so are the extern methods (FCalls, QCalls, P/Invokes) it reaches.
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length > 1 && args[0] == "types")
{
    // types <assembly>: the type definitions with their metadata tokens (to read a "type definition 0x0200000C has no symbol" diagnostic).
    foreach (var t in ModuleDefinition.ReadModule(args[1]).GetTypes()) Console.WriteLine($"0x{t.MetadataToken.ToInt32():X8} {t.FullName} ({t.Attributes})");
    return;
}
if (args.Length > 1 && args[0] == "synth-create-instance")
{
    Synth.CreateInstance(args[1]);
    return;
}
if (args.Length > 0 && args[0] == "internal-interfaces")
{
    // internal-interfaces: the CoreLib interfaces the reference pack does not declare that have static abstract/virtual members, with their
    // implementers among the stop types (primitives): what the compiler must supply for the built-in types (issue #221).
    var o = Cli.Parse(["--roots", "x"]);
    var core = ModuleDefinition.ReadModule(o.CoreLib);
    var publicNames = Directory.GetFiles(o.RefDir, "*.dll").Where(f => !Path.GetFileName(f).StartsWith("Microsoft.")).SelectMany(f =>
    {
        try { return ModuleDefinition.ReadModule(f).GetTypes().Where(t => t.IsPublic || t.IsNestedPublic).Select(t => t.FullName).ToList(); } catch { return []; }
    }).ToHashSet();
    foreach (var itf in core.GetTypes().Where(t => t.IsInterface && !publicNames.Contains(t.FullName) && t.Methods.Any(m => m.IsStatic && m.IsAbstract)))
    {
        var impls = core.GetTypes().Where(t => publicNames.Contains(t.FullName) && t.Interfaces.Any(i => i.InterfaceType.GetElementType().FullName == itf.FullName)).Select(t => t.Name).ToList();
        Console.WriteLine($"{itf.FullName}: {itf.Methods.Count(m => m.IsStatic && m.IsAbstract)} static abstract; implemented by {impls.Count}: {string.Join(", ", impls)}");
    }
    return;
}
if (args.Length > 1 && args[0] == "simd-refs")
{
    // simd-refs <dll or directory>...: the members of System.Runtime.Intrinsics (and System.Numerics.Vector) the assemblies' IL calls, with the number
    // of call sites: what a Vector128 lowering has to serve for them (FunctionCodeGen.Simd.cs, issue #252).
    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var path in args.Skip(1).SelectMany(a => Directory.Exists(a) ? Directory.GetFiles(a, "*.dll") : [a]))
    {
        ModuleDefinition module;
        try { module = ModuleDefinition.ReadModule(path); } catch { continue; }
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var i in method.Body.Instructions)
                if (i.Operand is MethodReference r && r.DeclaringType.GetElementType().FullName is { } type
                    && (type.StartsWith("System.Runtime.Intrinsics.", StringComparison.Ordinal) || type is "System.Numerics.Vector" or "System.Numerics.Vector`1"))
                {
                    var key = $"{type}::{r.Name}({string.Join(",", r.Parameters.Select(p => p.ParameterType.Name))}) [{Path.GetFileName(path)}]";
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
    }
    foreach (var (key, n) in counts) Console.WriteLine($"{n,4} {key}");
    return;
}
if (args.Length > 2 && args[0] == "callers")
{
    // callers <Type full name, ! for backtick> <method name>: the CoreLib methods that call it, and whether the call sits behind a
    // RuntimeFeature.IsDynamicCodeCompiled / IsDynamicCodeSupported test (which csharpc folds to false, like NativeAOT).
    var core = ModuleDefinition.ReadModule(Cli.Parse(["--roots", "x"]).CoreLib);
    var typeName = args[1].Replace('!', '`');
    foreach (var m in core.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
        if (m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == args[2] && r.DeclaringType.FullName == typeName))
            Console.WriteLine($"{m.FullName}  [{(m.Body.Instructions.Any(i => i.Operand is MethodReference f && f.DeclaringType.Name == "RuntimeFeature" && f.Name.StartsWith("get_IsDynamicCode")) ? "behind IsDynamicCode*" : "unguarded")}]");
    return;
}
if (args.Length > 1 && args[0] == "implementers")
{
    // implementers <interface name, ! for backtick>: the CoreLib types that implement it, with the methods that implement its members.
    var core = ModuleDefinition.ReadModule(Cli.Parse(["--roots", "x"]).CoreLib);
    var itf = args[1].Replace('!', '`');
    foreach (var t in core.GetTypes().Where(t => t.Interfaces.Any(i => i.InterfaceType.GetElementType().FullName == itf)))
    {
        Console.WriteLine($"{t.FullName}: {string.Join(", ", t.Interfaces.Where(i => i.InterfaceType.GetElementType().FullName == itf).Select(i => i.InterfaceType.FullName))}");
        foreach (var m in t.Methods.Where(m => m.HasOverrides && m.Overrides.Any(o => o.DeclaringType.GetElementType().FullName == itf)))
            Console.WriteLine($"    {m.Name}  [{string.Join(", ", m.Overrides.Where(o => o.DeclaringType.GetElementType().FullName == itf).Select(o => o.Name))}]");
    }
    return;
}
if (args.Length > 0 && args[0] == "dump")
{
    // dump <Type full name, ! for backtick> <method name>: the IL of CoreLib's methods of that name.
    var core = ModuleDefinition.ReadModule(Cli.Parse(["--roots", "x"]).CoreLib);
    var type = core.GetType(args[1].Replace('!', '`'));
    foreach (var m in type.Methods.Where(m => m.Name == args[2] && m.HasBody))
    {
        Console.WriteLine(m.FullName);
        foreach (var i in m.Body.Instructions) Console.WriteLine($"  {i.Offset:X4} {i.OpCode.Name} {(i.Operand is Instruction t ? $"{t.Offset:X4}" : i.Operand)}");
    }
    return;
}
var a = Cli.Parse(args);
var slice = new Slice(a);
slice.Run();
if (a.Command == "emit") new Emitter(slice, a).Run();
else slice.Report(a.Out);

sealed class Cli
{
    public string Command = "analyze", CoreLib = "", RefDir = "", Out = "", Corelib = "", Extra = "";
    public List<string> Roots = [];
    public List<string> Stop = [];
    /// <summary>"Ns.Type::Method" -> "Assembly:Ns.Type::Method": the slice calls the target with the same arguments instead of running the method's IL (the contract).</summary>
    public Dictionary<string, string> Replace = new();
    /// <summary>"Ns.Type`1" -> "Assembly:Ns.Type`1": references to this (stop) type point at csharpc's own type of the same shape instead.</summary>
    public Dictionary<string, string> Redirect = new();
    public string Replacement(Mono.Cecil.MethodDefinition m) => Replace.GetValueOrDefault(m.DeclaringType.FullName + "::" + m.Name);
    public string SliceName = "CsharpcUpstreamCore", Prefix = "CsharpcCoreLib.Upstream";

    public static Cli Parse(string[] args)
    {
        var c = new Cli();
        string fw = Environment.GetEnvironmentVariable("CSHARPC_FRAMEWORK_DIR") ?? System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        c.CoreLib = Path.Combine(fw, "System.Private.CoreLib.dll");
        var packs = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".dotnet11", "packs", "Microsoft.NETCore.App.Ref");
        if (Directory.Exists(packs)) c.RefDir = Directory.GetDirectories(packs).OrderBy(d => d).Select(d => Path.Combine(d, "ref", "net11.0")).LastOrDefault(Directory.Exists) ?? "";
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "analyze" or "emit": c.Command = args[i]; break;
                case "--roots": c.Roots.AddRange(args[++i].Replace('!', '`').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "--stop": c.Stop.AddRange(args[++i].Replace('!', '`').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "--roots-file":
                    // One CoreLib type per line (metadata name, '!' or '`' for the arity), '#' starts a comment: the build's list of types served from IL.
                    foreach (var line in File.ReadAllLines(args[++i]))
                        if (line.Split('#')[0].Trim() is { Length: > 0 } root) c.Roots.Add(root.Replace('!', '`'));
                    break;
                case "--profile":
                    // The contract choices of the shipped slice (docs/21-ownership.md Part B), the same for the build and for tools/il-first/slicer/slice.sh.
                    if (args[++i] != "csharpc") throw new ArgumentException("unknown profile " + args[i]);
                    c.Replace["System.SR::InternalGetResourceString"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::GetResourceString";
                    c.Replace["System.SR::GetUsingResourceKeysSwitchValue"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::GetUsingResourceKeys";
                    // The message of a missing key is string.Format(text, key): all of composite formatting, culture data and number formatting for a
                    // key of any type, in every program that reads a Dictionary.
                    c.Replace["System.ThrowHelper::GetKeyNotFoundException"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::GetKeyNotFoundException";
                    // (the generic throw helpers too: for the common key types the text needs no culture or number formatting machinery at all)
                    c.Replace["System.ThrowHelper::ThrowKeyNotFoundException"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::ThrowKeyNotFoundException";
                    c.Replace["System.ThrowHelper::ThrowAddingDuplicateWithKeyArgumentException"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::ThrowAddingDuplicateWithKeyArgumentException";
                    c.Replace["System.ThrowHelper::GetAddingDuplicateWithKeyArgumentException"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::GetAddingDuplicateWithKeyArgumentException";
                    c.Redirect["System.Collections.Generic.NonRandomizedStringEqualityComparer"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamNonRandomizedStringEqualityComparer";
                    c.Redirect["System.Collections.Generic.IInternalStringEqualityComparer"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamInternalStringEqualityComparer";
                    c.Redirect["System.Collections.Generic.RandomizedStringEqualityComparer"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamNonRandomizedStringEqualityComparer";
                    c.Stop.AddRange(["Interop", "Interop/Sys", "Interop/Globalization"]);
                    break;
                case "--out": c.Out = args[++i]; break;
                case "--corelib": c.CoreLib = args[++i]; break;
                case "--ref": c.RefDir = args[++i]; break;
                case "--name": c.SliceName = args[++i]; break;
                case "--prefix": c.Prefix = args[++i]; break;
                case "--redirect":
                    foreach (var r in args[++i].Replace('!', '`').Split(';', StringSplitOptions.RemoveEmptyEntries)) { var eq = r.IndexOf('='); c.Redirect[r[..eq]] = r[(eq + 1)..]; }
                    break;
                case "--replace":
                    foreach (var r in args[++i].Replace('!', '`').Split(';', StringSplitOptions.RemoveEmptyEntries)) { var eq = r.IndexOf('='); c.Replace[r[..eq]] = r[(eq + 1)..]; }
                    break;
                case "--shadows": c.Corelib = args[++i]; break;
                default: throw new ArgumentException("unknown argument " + args[i]);
            }
        }
        if (c.Roots.Count == 0) throw new ArgumentException("--roots is required");
        return c;
    }
}

sealed class Slice
{
    public readonly Cli Cli;
    public readonly ModuleDefinition Core;
    public readonly Dictionary<string, TypeDefinition> RefTypes = new(StringComparer.Ordinal);   // full name -> reference-pack type
    public readonly Dictionary<string, AssemblyNameReference> RefAssemblyOf = new(StringComparer.Ordinal);
    public readonly HashSet<string> Shadowed = new(StringComparer.Ordinal);                      // real names csharpc shadows (type or members)
    public readonly HashSet<string> ShadowedTypes = new(StringComparer.Ordinal);
    public readonly HashSet<TypeDefinition> Types = [];
    public readonly HashSet<MethodDefinition> Methods = [];
    public readonly HashSet<TypeDefinition> RootTypes = [];
    public readonly HashSet<FieldDefinition> UsedFields = [];
    public readonly Dictionary<TypeDefinition, SortedSet<string>> Needs = new();                 // stop type -> internal members used
    public readonly Dictionary<TypeDefinition, int> StopUses = new();
    /// <summary>Every member of a stop type the slice calls (public or not), by type: what the compiler has to serve for it.</summary>
    public readonly Dictionary<TypeDefinition, SortedSet<string>> UsedMembers = new();
    void NoteUsed(MethodDefinition m)
    {
        if (!UsedMembers.TryGetValue(m.DeclaringType, out var set)) UsedMembers[m.DeclaringType] = set = [];
        set.Add(Describe(m));
    }
    public readonly SortedDictionary<string, SortedSet<string>> Externs = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, int> Opcodes = new(StringComparer.Ordinal);
    readonly Queue<MethodDefinition> _queue = new();
    public readonly Dictionary<TypeDefinition, string> Why = new();
    string _current = "root";
    readonly HashSet<string> _forcedStop;

    public Slice(Cli cli)
    {
        Cli = cli;
        Core = ModuleDefinition.ReadModule(cli.CoreLib, new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true });
        foreach (var dll in Directory.GetFiles(cli.RefDir, "*.dll"))
        {
            var m = ModuleDefinition.ReadModule(dll, new ReaderParameters { ReadingMode = ReadingMode.Deferred });
            foreach (var t in m.GetTypes())
                if (t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamilyOrAssembly)
                    if (RefTypes.TryAdd(t.FullName, t)) RefAssemblyOf[t.FullName] = m.Assembly.Name;
        }
        _forcedStop = [.. cli.Stop];
        var shadowDir = cli.Corelib.Length > 0 ? cli.Corelib : FindCorelibSources();
        if (shadowDir is not null)
        {
            var rx = new Regex(@"^\s*\[(Shadows|ShadowMembers)\(""([^""]+)""\)\]", RegexOptions.Multiline);
            foreach (var f in Directory.GetFiles(shadowDir, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in rx.Matches(File.ReadAllText(f)))
                {
                    var name = m.Groups[2].Value.Replace('+', '/');
                    Shadowed.Add(name);
                    if (m.Groups[1].Value == "Shadows") ShadowedTypes.Add(name);
                }
        }
    }

    static string FindCorelibSources()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Csharpc", "corelib"))) dir = dir.Parent;
        return dir is null ? null : Path.Combine(dir.FullName, "src", "Csharpc", "corelib");
    }

    static readonly string[] ContractPrefixes =
    [
        "System.RuntimeType", "System.Reflection.", "System.Runtime.Loader.", "System.StubHelpers.", "Internal.Runtime.", "System.Diagnostics.Tracing.",
        "System.Runtime.CompilerServices.CastHelpers", "System.Runtime.CompilerServices.StaticsHelpers", "System.Runtime.EH", "System.Runtime.InteropServices.Marshal",
        "System.IRuntime", "System.Signature", "System.MdUtf8String", "System.DefaultBinder", "System.ModuleHandle", "System.Threading.ThreadPool", "System.Resources.",
    ];

    public bool IsStop(TypeDefinition t)
    {
        var top = t; while (top.DeclaringType is not null) top = top.DeclaringType;
        if (RootTypes.Contains(t)) return false;
        // A type nested in a type that stays outside cannot join the slice (its declaring type would have to be copied too).
        if (t.DeclaringType is not null && IsStop(t.DeclaringType)) return true;
        if (_forcedStop.Contains(t.FullName) || _forcedStop.Contains(top.FullName) || Cli.Redirect.ContainsKey(top.FullName)) return true;
        // Runtime-coupled internal types (the type system, reflection runtime, stubs, tracing providers): the contract, not slice material.
        foreach (var p in ContractPrefixes) if (t.FullName.StartsWith(p, StringComparison.Ordinal)) return true;
        return RefTypes.ContainsKey(t.FullName);
    }

    public void Run()
    {
        foreach (var name in Cli.Roots)
        {
            var t = Core.GetType(name) ?? throw new ArgumentException($"{name} is not a type of {Cli.CoreLib}");
            RootTypes.Add(t);
            UseType(t, force: true);
            void AddNested(TypeDefinition x) { foreach (var n in x.NestedTypes) { RootTypes.Add(n); UseType(n, force: true); AddNested(n); } }
            AddNested(t);
        }
        foreach (var t in RootTypes)
            foreach (var m in t.Methods) Enqueue(m);
        do
        {
            while (_queue.Count > 0) Walk(_queue.Dequeue());
        } while (ReachInterfaceImplementations());
    }

    /// <summary>
    /// The primitives' implementations of the static abstract members of the slice's interfaces (issue #221): stop type -> its methods.
    /// IUtfChar&lt;TChar&gt;, IBinaryIntegerParseAndFormatInfo&lt;T&gt; and the like are internal to CoreLib, so the reference pack's char/int/double do
    /// not implement them, yet the IL is generic over them (`constrained. TChar call IUtfChar&lt;TChar&gt;::CastFrom`). The emitter hosts these
    /// methods in one static class per stop type, which the compiler consults for a constrained call on that built-in type.
    /// </summary>
    public readonly Dictionary<TypeDefinition, List<MethodDefinition>> Hosted = new();
    Dictionary<string, List<TypeDefinition>> _implementers;

    bool ReachInterfaceImplementations()
    {
        _implementers ??= Core.GetTypes().Where(t => !t.IsInterface && !t.HasGenericParameters && t.DeclaringType is null && IsStop(t) && RefTypes.ContainsKey(t.FullName))
            .SelectMany(t => t.Interfaces.Select(i => (Name: i.InterfaceType.GetElementType().FullName, Type: t)))
            .GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.Select(x => x.Type).ToList());
        var changed = false;
        foreach (var member in Methods.Where(m => m.IsStatic && m.IsVirtual && m.DeclaringType.IsInterface && Types.Contains(m.DeclaringType)).ToList())
        {
            if (!_implementers.TryGetValue(member.DeclaringType.FullName, out var types)) continue;
            foreach (var stop in types)
                foreach (var impl in stop.Methods.Where(x => x.IsStatic && x.HasOverrides && x.Overrides.Any(o => SafeResolve(o) == member)))
                {
                    if (!Hosted.TryGetValue(stop, out var list)) Hosted[stop] = list = [];
                    if (list.Contains(impl)) continue;
                    list.Add(impl);
                    Enqueue(impl);
                    changed = true;
                }
        }
        return changed;
    }

    void Enqueue(MethodDefinition m)
    {
        if (m is null) return;
        if (Methods.Add(m)) _queue.Enqueue(m);
    }

    /// <summary>A CoreLib type that becomes part of the slice (or a stop type that is only recorded).</summary>
    public void UseType(TypeReference tr, bool force = false)
    {
        switch (tr)
        {
            case null: return;
            case GenericParameter: return;
            case GenericInstanceType g:
                UseType(g.ElementType); foreach (var arg in g.GenericArguments) UseType(arg); return;
            case ArrayType or ByReferenceType or PointerType or PinnedType or SentinelType: UseType(((TypeSpecification)tr).ElementType); return;
            case RequiredModifierType rm: UseType(rm.ElementType); UseType(rm.ModifierType); return;
            case OptionalModifierType om: UseType(om.ElementType); return;
            case FunctionPointerType fp: UseType(fp.ReturnType); foreach (var p in fp.Parameters) UseType(p.ParameterType); return;
        }
        if (tr is not TypeDefinition d || d.Module != Core) return;
        if (!force && IsStop(d))
        {
            StopUses[d] = StopUses.GetValueOrDefault(d) + 1;
            return;
        }
        if (!Types.Add(d)) return;
        Why[d] = _current;
        if (d.DeclaringType is not null) UseType(d.DeclaringType);
        UseType(d.BaseType);
        foreach (var i in d.Interfaces) UseType(i.InterfaceType);
        if (d.Name != "<PrivateImplementationDetails>") foreach (var f in d.Fields) UseType(f.FieldType);
        foreach (var gp in d.GenericParameters) foreach (var c in gp.Constraints) UseType(c.ConstraintType);
        // A used type: its static constructor runs, and its virtual methods may be reached through the base type or an interface.
        foreach (var m in d.Methods)
            if (m.IsConstructor && m.IsStatic || m.IsVirtual || m.HasOverrides) Enqueue(m);
    }

    void Walk(MethodDefinition m)
    {
        _current = m.DeclaringType.Name + "::" + m.Name;
        UseType(m.DeclaringType);
        UseType(m.ReturnType);
        foreach (var p in m.Parameters) UseType(p.ParameterType);
        foreach (var gp in m.GenericParameters) foreach (var c in gp.Constraints) UseType(c.ConstraintType);
        foreach (var o in m.Overrides) { UseType(o.DeclaringType); NoteRef(o); }
        if (m.IsInternalCall || m.IsPInvokeImpl || (m.ImplAttributes & MethodImplAttributes.Runtime) != 0 && !m.DeclaringType.BaseType?.FullName.StartsWith("System.MulticastDelegate") == true)
        {
            string kind = m.IsInternalCall ? "FCall" : m.IsPInvokeImpl ? (m.PInvokeInfo.Module.Name == "QCall" ? "QCall" : "P/Invoke " + m.PInvokeInfo.Module.Name) : "runtime";
            if (!Externs.TryGetValue(m.DeclaringType.FullName, out var set)) Externs[m.DeclaringType.FullName] = set = [];
            set.Add($"{kind} {m.Name}");
        }
        if (!m.HasBody || Cli.Replacement(m) is not null) return;
        var body = m.Body;
        foreach (var v in body.Variables) UseType(v.VariableType);
        foreach (var h in body.ExceptionHandlers) UseType(h.CatchType);
        foreach (var ins in body.Instructions)
        {
            switch (ins.OpCode.Code)
            {
                case Code.Calli or Code.Localloc or Code.Jmp or Code.Arglist or Code.Mkrefany or Code.Refanyval or Code.Refanytype
                    or Code.Cpblk or Code.Initblk:
                    Opcodes[ins.OpCode.Name] = Opcodes.GetValueOrDefault(ins.OpCode.Name) + 1; break;
            }
            switch (ins.Operand)
            {
                case MethodReference mr: ReachMethod(mr); break;
                case FieldReference fr: ReachField(fr); break;
                case TypeReference tr: UseType(tr); break;
                case CallSite cs: UseType(cs.ReturnType); foreach (var p in cs.Parameters) UseType(p.ParameterType); break;
            }
        }
    }

    void ReachMethod(MethodReference mr)
    {
        var element = mr is GenericInstanceMethod gim ? gim.ElementMethod : mr;
        UseType(mr.DeclaringType);
        foreach (var arg in (mr as GenericInstanceMethod)?.GenericArguments ?? (IEnumerable<TypeReference>)[]) UseType(arg);
        UseType(element.ReturnType);
        foreach (var p in element.Parameters) UseType(p.ParameterType);
        if (element.DeclaringType is ArrayType) return;
        var def = element as MethodDefinition ?? SafeResolve(element);
        if (def is null || def.Module != Core) return;
        if (IsStop(def.DeclaringType)) { NoteMember(def); NoteUsed(def); return; }
        Enqueue(def);
    }

    static MethodDefinition SafeResolve(MethodReference r)
    {
        try { return r.Resolve(); } catch (AssemblyResolutionException) { return null; }
    }

    void ReachField(FieldReference fr)
    {
        UseType(fr.DeclaringType); UseType(fr.FieldType); if (fr is FieldDefinition used) UsedFields.Add(used);
        var def = fr as FieldDefinition ?? fr.Resolve();
        if (def is not null && def.Module == Core && IsStop(def.DeclaringType)) NoteMember(def);
    }

    /// <summary>A member of a stop type that the slice uses: part of the contract unless the reference pack declares it too.</summary>
    void NoteMember(IMemberDefinition member)
    {
        var owner = member.DeclaringType;
        if (!IsStop(owner)) return;
        if (RefTypes.TryGetValue(owner.FullName, out var r) && InRef(r, member)) return;
        if (!Needs.TryGetValue(owner, out var set)) Needs[owner] = set = [];
        set.Add(Describe(member));
    }

    void NoteRef(MethodReference m) { var d = SafeResolve(m); if (d is not null && d.Module == Core) NoteMember(d); }

    static bool Visible(MethodDefinition m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

    static bool InRef(TypeDefinition r, IMemberDefinition member)
    {
        switch (member)
        {
            case MethodDefinition m:
                if (!Visible(m) && !m.IsFamilyOrAssembly) return false;
                return r.Methods.Any(x => x.Name == m.Name && x.Parameters.Count == m.Parameters.Count && x.GenericParameters.Count == m.GenericParameters.Count && Visible(x));
            case FieldDefinition f:
                return (f.IsPublic || f.IsFamily) && r.Fields.Any(x => x.Name == f.Name && (x.IsPublic || x.IsFamily));
            default:
                return false;
        }
    }

    static string Describe(IMemberDefinition m) => m switch
    {
        MethodDefinition md => $"{(md.IsStatic ? "static " : "")}{(md.IsPublic ? "public " : md.IsAssembly ? "internal " : md.IsFamily ? "protected " : md.IsFamilyOrAssembly ? "protected internal " : "private ")}{md.Name}({string.Join(", ", md.Parameters.Select(p => Short(p.ParameterType)))})",
        FieldDefinition fd => $"{(fd.IsStatic ? "static " : "")}{(fd.IsPublic ? "public " : fd.IsAssembly ? "internal " : "private ")}field {fd.Name}",
        _ => m.Name,
    };

    static string Short(TypeReference t) => t.FullName.Replace("System.", "");

    // ---------------------------------------------------------------------------------------------------------------

    public void Report(string outPath)
    {
        var sb = new System.Text.StringBuilder();
        int il = Methods.Where(m => m.HasBody).Sum(m => m.Body.CodeSize);
        int allMethods = Types.Sum(t => t.Methods.Count);
        sb.AppendLine($"# roots: {string.Join(", ", Cli.Roots)}");
        sb.AppendLine($"types in the slice: {Types.Count} ({Types.Count(t => !RootTypes.Contains(t))} beyond the roots); methods reached: {Methods.Count} of {allMethods} in those types; IL bytes of reached methods: {il:N0}");
        sb.AppendLine($"stop types used (declared by the reference pack): {StopUses.Count}; of those with internal members needed (the contract): {Needs.Count}, {Needs.Values.Sum(s => s.Count)} members");
        sb.AppendLine($"extern methods reached (FCall/QCall/P-Invoke): {Externs.Values.Sum(s => s.Count)} in {Externs.Count} types");
        sb.AppendLine("opcodes the IL front-end may refuse: " + (Opcodes.Count == 0 ? "none" : string.Join(", ", Opcodes.Select(kv => $"{kv.Key} x{kv.Value}"))));
        sb.AppendLine();
        sb.AppendLine("## slice types (beyond the roots)");
        foreach (var t in Types.Where(t => !RootTypes.Contains(t)).OrderBy(t => t.FullName, StringComparer.Ordinal))
            sb.AppendLine($"{t.FullName}\t{Methods.Count(m => m.DeclaringType == t)}/{t.Methods.Count} methods\t{(t.IsPublic ? "public" : "internal")}\tvia {Why.GetValueOrDefault(t)}");
        sb.AppendLine();
        sb.AppendLine("## contract: members of stop types the slice needs that the reference pack does not declare");
        foreach (var (t, set) in Needs.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine($"{t.FullName}\t{set.Count}\t{(ShadowedOrMembers(t) ? "shadowed" : "NOT shadowed")}");
            foreach (var m in set) sb.AppendLine("    " + m);
        }
        sb.AppendLine();
        sb.AppendLine("## members of stop types the slice calls, by type (every one the compiler must serve)");
        foreach (var (t, set) in UsedMembers.OrderBy(kv => kv.Key.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine($"{t.FullName}\t{set.Count}");
            foreach (var m in set) sb.AppendLine("    " + m);
        }
        sb.AppendLine();
        sb.AppendLine("## stop types used (public in the reference pack), by uses; shadow = csharpc has a shadow or shadow members for it");
        foreach (var (t, n) in StopUses.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.FullName, StringComparer.Ordinal))
            sb.AppendLine($"{t.FullName}\t{n}\t{(ShadowedTypes.Contains(t.FullName) ? "shadow" : Shadowed.Contains(t.FullName) ? "shadow-members" : "no shadow")}");
        sb.AppendLine();
        sb.AppendLine("## extern methods reached");
        foreach (var (t, set) in Externs) { sb.AppendLine($"{t}\t{set.Count}"); foreach (var m in set) sb.AppendLine("    " + m); }
        if (outPath.Length > 0) File.WriteAllText(outPath, sb.ToString()); else Console.Write(sb);
    }

    bool ShadowedOrMembers(TypeDefinition t) => Shadowed.Contains(t.FullName);
}
