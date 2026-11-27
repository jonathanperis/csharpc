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
if (args.Length > 5 && args[0] == "pointer-size")
{
    // pointer-size <slice64.dll> <slice32.dll> <CoreLib64> <CoreLib32> <report.md>: the members of the slice compiled differently for
    // 32-bit targets (PointerSize.cs, #382), and a failure when the two slices do not serve the same types.
    Environment.Exit(PointerSize.Run(args[1], args[2], args[3], args[4], args[5]));
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
    /// <summary>
    /// "Assembly:Ns.Type::Method": SR.Format's replacement, a formatter of `{n}` / `{n:spec}` items only (Emit.SrFormatIsSimple). Used only
    /// when the slice proves that every SR.Format call it carries formats a CoreLib resource text of that form; otherwise SR.Format keeps
    /// its IL (string.Format, full composite formatting).
    /// </summary>
    public string SrFormat;
    /// <summary>"Ns.Type`1" -> "Assembly:Ns.Type`1": references to this (stop) type point at csharpc's own type of the same shape instead.</summary>
    public Dictionary<string, string> Redirect = new();
    /// <summary>
    /// "Ns.Type::get_X" -> its value: a static bool getter folded to a constant, as NativeAOT's ILCompiler folds a feature switch. The emitted
    /// IL loads the constant instead of calling the getter, so csharpc never lowers the branch it rules out (FunctionCodeGen.IlFeatureSwitches),
    /// and the slicer does not follow that branch, so nothing only it reaches is cut into the slice (<see cref="FoldedValue"/>).
    /// </summary>
    public Dictionary<string, bool> Fold = new();
    public bool? FoldedValue(Mono.Cecil.MethodReference m) =>
        Fold.Count > 0 && m.Parameters.Count == 0 && !m.HasThis && Fold.TryGetValue(m.DeclaringType.FullName + "::" + m.Name, out var v) ? v : null;
    /// <summary>
    /// Runtime event sources whose <c>Log.IsEnabled()</c> is false in a csharpc program: an EventListener there sees only the program's own
    /// sources (EventPipe is a disabled contract, corelib/UpstreamTracing.cs), so the runtime's (NativeRuntimeEventSource's thread-pool and I/O events) are never enabled.
    /// </summary>
    public HashSet<string> DisabledEventSources = new();
    /// <summary>The constant an instruction folds to: a folded getter's call, or the IsEnabled() call on a disabled source's Log.</summary>
    public bool? FoldedValueAt(Mono.Cecil.Cil.Instruction i) => i.Operand switch
    {
        Mono.Cecil.MethodReference getter when i.OpCode.Code == Mono.Cecil.Cil.Code.Call => FoldedValue(getter),
        Mono.Cecil.MethodReference { Name: "IsEnabled", Parameters.Count: 0, DeclaringType.FullName: "System.Diagnostics.Tracing.EventSource" }
            when i.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt && IsDisabledLog(i.Previous) => false,
        _ => null,
    };
    /// <summary>An <c>ldsfld</c> of a disabled runtime event source's <c>Log</c> (folded together with the IsEnabled() call after it).</summary>
    public bool IsDisabledLog(Mono.Cecil.Cil.Instruction i) =>
        i is { OpCode.Code: Mono.Cecil.Cil.Code.Ldsfld, Operand: Mono.Cecil.FieldReference { Name: "Log" } f } && DisabledEventSources.Contains(f.DeclaringType.FullName);
    /// <summary>
    /// "self:Ns.Type::Method" (a replacement inside CoreLib): the method of CoreLib's own of that name (one overload), carried in the slice, which the
    /// replaced method calls with its own arguments instead of running its IL.
    /// </summary>
    public Mono.Cecil.MethodDefinition SelfTarget(Mono.Cecil.ModuleDefinition core, string forward)
    {
        if (!forward.StartsWith("self:", StringComparison.Ordinal)) return null;
        var sep = forward.IndexOf("::", StringComparison.Ordinal);
        var owner = core.GetType(forward[5..sep]) ?? throw new ArgumentException($"{forward}: no such type in {CoreLib}");
        var name = forward[(sep + 2)..];
        if (name.IndexOf('?') is var option and >= 0) name = name[..option];
        return owner.Methods.Single(x => x.Name == name);
    }
    /// <summary>A "Type::Method(ParamType,...)" key replaces that overload only; "Type::Method" every overload.</summary>
    public string Replacement(Mono.Cecil.MethodDefinition m) =>
        Replace.GetValueOrDefault(m.DeclaringType.FullName + "::" + m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")")
        ?? Replace.GetValueOrDefault(m.DeclaringType.FullName + "::" + m.Name);
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
                    // A directory stands for all its *.txt files in ordinal order (src/Csharpc/upstream-types/: one file per library, so flips of
                    // different libraries never edit the same file).
                    var rootsPath = args[++i];
                    var rootFiles = Directory.Exists(rootsPath)
                        ? Directory.GetFiles(rootsPath, "*.txt").Order(StringComparer.Ordinal).ToArray()
                        : [rootsPath];
                    foreach (var file in rootFiles)
                        foreach (var line in File.ReadAllLines(file))
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
                    // SR.Format(text, args) is string.Format: composite formatting, culture data and every number formatter for any message
                    // with an argument (Hashtable's duplicate key, #373). The texts CoreLib formats use `{n}` and `{n:spec}` only.
                    c.SrFormat = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::Format";
                    // Random's default seed: the OS RNG the runtime seeds string hashing with (csrt_os_random), where CoreLib calls libSystem.Native.
                    c.Replace["Interop::GetRandomBytes"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::GetRandomBytes";
                    // Stream's asynchronous defaults run as Stream.ReadWriteTask over Task's internals and test for BeginRead/BeginWrite overrides in
                    // the method table's auxiliary data; csharpc's tasks are the runtime's (corelib/IO/StreamContract.cs).
                    foreach (var member in new[] { "BeginRead", "EndRead", "BeginWrite", "EndWrite", "BeginEndReadAsync", "BeginEndWriteAsync" })
                        c.Replace["System.IO.Stream::" + member] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamStreamSupport::" + member;
                    // (Stream.Null's BeginRead hands out Task<int>.s_defaultResultTask, a field of the runtime's Task<int> csharpc does not have)
                    c.Replace["System.IO.Stream/NullStream::BeginRead"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamStreamSupport::NullStreamBeginRead";
                    // Guid.NewGuid / CreateVersion7: the same OS CSPRNG (getentropy, BCryptGenRandom-equivalent rand_s, WASI random_get).
                    c.Replace["Interop::GetCryptographicallySecureRandomBytes"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::GetCryptographicallySecureRandomBytes";
                    // The Windows CoreLib's Guid.NewGuid calls ole32's CoCreateGuid instead: the same CSPRNG, with v4's version and variant bits.
                    c.Replace["Interop/Ole32::CoCreateGuid"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamSupport::CoCreateGuid";
                    c.Redirect["System.Collections.Generic.NonRandomizedStringEqualityComparer"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamNonRandomizedStringEqualityComparer";
                    c.Redirect["System.Collections.Generic.IInternalStringEqualityComparer"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamInternalStringEqualityComparer";
                    c.Redirect["System.Collections.Generic.RandomizedStringEqualityComparer"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamRandomizedStringEqualityComparer";
                    // Interop and Interop.Sys come from the IL (the error mapping and the managed wrappers); their libSystem.Native P/Invokes are
                    // csharpc's C# contract over its libc layer (corelib/IO/SystemNative*.cs, ModuleCodeGen.PInvoke), not the pack's shim.
                    c.Stop.AddRange(["Interop/Globalization"]);
                    // The ICU boundary (#426): CoreLib's globalization IL calls Interop.Globalization; every such call goes to the corelib's
                    // forwarding class (corelib/UpstreamGlobalization.cs), which calls the corelib's own imports of the runtime's ICU shim, the
                    // single boundary into native globalization code. The types in those signatures are the corelib's (same values).
                    foreach (var member in (string[])["GetLocaleName", "GetLocaleInfoString", "GetDefaultLocaleName", "IsPredefinedLocale", "GetLocaleTimeFormat",
                        "GetLocaleInfoInt", "GetLocaleInfoGroupingSizes", "GetLocales", "GetCalendars", "GetCalendarInfo", "EnumCalendarInfo", "GetLatestJapaneseEra",
                        "GetJapaneseEraStartDate", "ChangeCase", "ChangeCaseInvariant", "ChangeCaseTurkish", "InitOrdinalCasingPage", "InitOrdinalLowerCasingPage",
                        "GetSortHandle", "CloseSortHandle", "CompareString", "IndexOf", "LastIndexOf", "StartsWith", "EndsWith", "GetSortKey", "GetSortVersion",
                        "ToAscii", "ToUnicode", "GetTimeZoneDisplayName", "IanaIdToWindowsId", "WindowsIdToIanaId"])
                        c.Replace["Interop/Globalization::" + member] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamGlobalizationInterop::" + member;
                    c.Redirect["Interop/Globalization/ResultCode"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamGlobalizationResultCode";
                    c.Redirect["Interop/Globalization/TimeZoneDisplayNameType"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamTimeZoneDisplayNameType";
                    c.Redirect["System.Globalization.CalendarId"] = "CsharpcCoreLib:CsharpcCoreLib.Globalization.CalendarId";
                    c.Redirect["System.Globalization.CalendarDataType"] = "CsharpcCoreLib:CsharpcCoreLib.Globalization.CalendarDataType";
                    // Invariant mode and ICU loading stay the corelib's: the project's InvariantGlobalization is compiled into the program, WASI is
                    // always invariant, and ICU is loaded by the first call that needs it (corelib/Globalization/GlobalizationMode.cs).
                    // The current cultures flow with the execution context through an AsyncLocal (corelib/UpstreamGlobalization.cs).
                    c.Replace["System.Globalization.CultureInfo::get_CurrentCulture"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamCultureInfo::GetCurrentCulture";
                    c.Replace["System.Globalization.CultureInfo::get_CurrentUICulture"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamCultureInfo::GetCurrentUICulture";
                    // GetFormat's DateTimeFormatInfo case through the runtime, so formatting a number doesn't link DateTimeFormatInfo and the calendars.
                    c.Replace["System.Globalization.CultureInfo::GetFormat"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamCultureInfo::GetFormat";
                    c.Redirect["System.Globalization.GlobalizationMode"] = "CsharpcCoreLib:CsharpcCoreLib.Globalization.GlobalizationMode";
                    // The Windows CoreLib carries both globalization back ends and picks one by GlobalizationMode.UseNls. .NET uses ICU on Windows
                    // unless the System.Globalization.UseNls switch is set, and csharpc always does (its GlobalizationMode answers false): the NLS
                    // paths (kernel32's locale and collation APIs, their callback types) are folded out instead of served. The Unix CoreLib has no
                    // NLS code, so the 64-bit slice of either host and the 32-bit one serve the same types.
                    c.Fold["System.Globalization.GlobalizationMode::get_UseNls"] = false;
                    c.Fold["System.Globalization.GlobalizationMode/Settings::get_UseNls"] = false;
                    // What the Windows CoreLib still asks Windows in ICU mode, as .NET does there: the locale check after ICU's, the user's
                    // regional overrides and code pages (GetLocaleInfoEx), calendar data (GetCalendarInfoEx), the user's region and UI languages,
                    // LCID names, ANSI marshalling, and the Hijri calendar's AddHijriDate registry setting. Each goes to the corelib's own import
                    // of the same Win32 function (corelib/UpstreamWindows.cs). None of these members exists in the Unix CoreLib.
                    foreach (var member in (string[])["GetLocaleInfoEx", "GetCalendarInfoEx", "GetGeoInfo", "GetUserGeoID", "GetUserPreferredUILanguages",
                        "LCIDToLocaleName", "MultiByteToWideChar"])
                        c.Replace["Interop/Kernel32::" + member] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamWindowsInterop::" + member;
                    c.Redirect["Interop/BOOL"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamWin32Bool";
                    c.Replace["System.Globalization.HijriCalendar::GetAdvanceHijriDate"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamWindowsInterop::GetAdvanceHijriDate";
                    // Overlapped file I/O on Windows (SafeFileHandle, RandomAccess, ThreadPoolBoundHandle) takes the Windows thread pool's path, as
                    // NativeAOT does there: Win32ThreadPoolNativeOverlapped and kernel32's thread-pool I/O objects. The portable path (Overlapped's
                    // runtime calls, ThreadPool.BindHandle) is folded out. Kernel32's CreateThreadpoolIo family is csharpc's contract
                    // (corelib/UpstreamThreadPoolIo.cs): its callbacks run on csharpc's own threads.
                    c.Fold["System.Threading.ThreadPool::get_UseWindowsThreadPool"] = true;
                    // csharpc's tracing serves the program's own EventSources only; the runtime's are never enabled, so their guarded events fold out.
                    c.DisabledEventSources.Add("System.Diagnostics.Tracing.NativeRuntimeEventSource");
                    c.Redirect["System.Threading.ThreadPoolCallbackWrapper"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamThreadPoolCallbackWrapper";
                    // Tracing (#423): EventPipe's QCalls are csharpc's disabled-EventPipe contract (corelib/UpstreamTracing.cs, bound by name like
                    // libSystem.Native's). LTTng's persistent listener (FEATURE_EVENTSOURCE_XPLAT, CoreCLR on Linux) does not exist, as in NativeAOT: EventSource's
                    // static constructor gets none (and XplatEventLogger, whose own static constructor reads CLR configuration through QCalls, never runs).
                    c.Replace["System.Diagnostics.Tracing.XplatEventLogger::InitializePersistentListener"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamTracing::InitializePersistentListener";
                    // NativeAOT's CoreLib does not read an event method's IL to check the id it passes to WriteEvent (no IL at run time).
                    c.Replace["System.Diagnostics.Tracing.EventSource::GetHelperCallFirstArg"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamTracing::GetHelperCallFirstArg";
                    // The runtime's and libraries' default event sources are the runtime's to create; csharpc's does not (no-op, both overloads).
                    c.Replace["System.Diagnostics.Tracing.EventSource::InitializeDefaultEventSources"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamTracing::InitializeDefaultEventSources";
                    // TraceLogging reads a reference type's property through a delegate made with MakeGenericType and CreateDelegate (a fast
                    // path); it reads the same value through PropertyInfo.GetValue, which its value-type path uses, with no run-time generics, and a
                    // null container is the delegate's NullReferenceException ("?nullref", Emit.WrapPropertyGetterNullCheck).
                    // TraceLogging's generic-match test asks every interface of a payload type for its definition; csharpc has no Type for the
                    // definition of a CoreLib-internal interface a primitive implements (IBinaryFloatParseAndFormatInfo<Half>), and such a definition is
                    // never one of the public ones TraceLogging compares with (IEnumerable<>, KeyValuePair<,>, Nullable<>).
                    c.Replace["System.Diagnostics.Tracing.Statics::IsGenericMatch"] = "CsharpcCoreLib:CsharpcCoreLib.UpstreamTracing::IsGenericMatch";
                    c.Replace["System.Diagnostics.Tracing.PropertyValue::GetReferenceTypePropertyGetter"] = "self:System.Diagnostics.Tracing.PropertyValue::GetBoxedValueTypePropertyGetter?nullref";
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
                case "--wasi":
                    // The slice wasm32 (WASI) uses, cut from the 32-bit CoreLib (#382). WASI has no ICU, so globalization runs in invariant mode,
                    // but csharpc keeps culture-aware comparisons with no option or IgnoreCase on an approximation of ICU's root collation
                    // (csrt_root_collation_compare, src/runtime/bclmisc.c) instead of turning them ordinal, so sorting Latin text agrees with
                    // .NET with ICU ("apple" < "Banana"). CompareInfo.Compare over spans is where every linguistic comparison of two strings
                    // arrives (string.Compare, CompareTo, the culture-aware StringComparers): corelib/UpstreamWasiCollation.cs.
                    c.Replace["System.Globalization.CompareInfo::Compare(System.ReadOnlySpan`1<System.Char>,System.ReadOnlySpan`1<System.Char>,System.Globalization.CompareOptions)"] =
                        "CsharpcCoreLib:CsharpcCoreLib.UpstreamWasiCollation::Compare";
                    break;
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
    // The methods the corelib's shadow files of each real type declare, as "Name(Type,Type)" with simple type names (ShadowSignatures):
    // a hosted type's helper that a shadow provides stays the shadow's, so the slice never trades csharpc's copy for upstream's code.
    public readonly Dictionary<string, HashSet<string>> ShadowMemberNames = new(StringComparer.Ordinal);
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
            {
                var text = File.ReadAllText(f);
                HashSet<string> words = null;
                foreach (Match m in rx.Matches(text))
                {
                    var name = m.Groups[2].Value.Replace('+', '/');
                    Shadowed.Add(name);
                    if (m.Groups[1].Value == "Shadows") ShadowedTypes.Add(name);
                    words ??= ShadowSignatures.Declared(text);
                    if (!ShadowMemberNames.TryGetValue(name, out var set)) ShadowMemberNames[name] = set = new(StringComparer.Ordinal);
                    set.UnionWith(words);
                }
            }
        }
        FoldOut();
    }

    /// <summary>Non-public members and nested types that only the branches a folded getter rules out use (<see cref="FoldOut"/>).</summary>
    public readonly HashSet<MethodDefinition> FoldedOutMethods = [];
    public readonly HashSet<TypeDefinition> FoldedOutTypes = [];

    /// <summary>
    /// A root type brings every member and nested type, but one that only dead code uses has no business in the slice: the Windows CoreLib's
    /// CultureData carries its NLS back end (NlsGetLocaleInfo, the EnumSystemLocalesEx callback and its EnumData state, JapaneseCalendar's
    /// registry eras...) next to the ICU one, reached only where GlobalizationMode.UseNls is true (Cli.Fold). The members referenced only from
    /// such branches, or from members that are themselves only so referenced, and the nested types only they use, are not rooted. Public,
    /// protected, virtual and constructor members always stay (they are API or dispatch targets), and so does a member nothing references.
    /// </summary>
    void FoldOut()
    {
        if (Cli.Fold.Count == 0 && Cli.DisabledEventSources.Count == 0) return;
        var sites = new Dictionary<MethodDefinition, List<(MethodDefinition Caller, bool Live)>>();
        foreach (var type in Core.GetTypes())
            foreach (var m in type.Methods.Where(m => m.HasBody))
            {
                var live = LiveInstructions(m.Body);
                foreach (var ins in m.Body.Instructions)
                    if (ins.Operand is MethodReference mr && (mr.GetElementMethod() as MethodDefinition ?? SafeResolve(mr.GetElementMethod())) is { } callee && callee.Module == Core)
                    {
                        if (!sites.TryGetValue(callee, out var list)) sites[callee] = list = [];
                        list.Add((m, live is null || live.Contains(ins)));
                    }
            }
        static bool Candidate(MethodDefinition m) => !(m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly || m.IsVirtual || m.IsConstructor);
        // Only what the folded branches lead to: the members called from a ruled-out branch, and what those call in turn...
        var callees = new Dictionary<MethodDefinition, List<MethodDefinition>>();
        foreach (var (callee, list) in sites)
            foreach (var (caller, _) in list)
            {
                if (!callees.TryGetValue(caller, out var c)) callees[caller] = c = [];
                c.Add(callee);
            }
        var dead = new HashSet<MethodDefinition>();
        var work = new Stack<MethodDefinition>(sites.Where(e => e.Value.Any(site => !site.Live)).Select(e => e.Key).Where(Candidate));
        while (work.Count > 0)
        {
            var m = work.Pop();
            if (!dead.Add(m)) continue;
            foreach (var c in callees.GetValueOrDefault(m) ?? []) if (Candidate(c)) work.Push(c);
        }
        // ...less any of them that live code still calls (greatest fixed point: a live call from outside the set keeps a member and what it calls).
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var m in dead.ToList())
                if (sites[m].Any(site => site.Live && !dead.Contains(site.Caller))) { dead.Remove(m); changed = true; }
        }
        FoldedOutMethods.UnionWith(dead);
        // A nested type is folded out when every method that uses it (in a signature, a local or an instruction) is dead or uses it only in dead code.
        var users = new Dictionary<TypeDefinition, List<(MethodDefinition User, bool Live)>>();
        foreach (var type in Core.GetTypes())
            foreach (var m in type.Methods)
            {
                void Use(TypeReference t, bool live)
                {
                    while (t is TypeSpecification spec) t = spec.ElementType;
                    if (t is null || (t as TypeDefinition ?? SafeResolveType(t)) is not { DeclaringType: not null } d || d.Module != Core) return;
                    if (!users.TryGetValue(d, out var l)) users[d] = l = [];
                    l.Add((m, live));
                }
                Use(m.ReturnType, true);
                foreach (var p in m.Parameters) Use(p.ParameterType, true);
                if (!m.HasBody) continue;
                var live = LiveInstructions(m.Body);
                foreach (var v in m.Body.Variables) Use(v.VariableType, true);
                foreach (var ins in m.Body.Instructions)
                {
                    bool isLive = live is null || live.Contains(ins);
                    switch (ins.Operand)
                    {
                        case MethodReference mr: Use(mr.DeclaringType, isLive); Use(mr.ReturnType, isLive); foreach (var p in mr.Parameters) Use(p.ParameterType, isLive); break;
                        case FieldReference fr: Use(fr.DeclaringType, isLive); Use(fr.FieldType, isLive); break;
                        case TypeReference tr: Use(tr, isLive); break;
                    }
                }
            }
        foreach (var (type, list) in users)
            if (type.Methods.All(dead.Contains) && list.All(u => !u.Live || dead.Contains(u.User) || u.User.DeclaringType == type))
                FoldedOutTypes.Add(type);
    }

    static TypeDefinition SafeResolveType(TypeReference r)
    {
        try { return r.Resolve(); } catch (AssemblyResolutionException) { return null; }
    }

    static string FindCorelibSources()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Csharpc", "corelib"))) dir = dir.Parent;
        return dir is null ? null : Path.Combine(dir.FullName, "src", "Csharpc", "corelib");
    }

    static readonly string[] ContractPrefixes =
    [
        "System.RuntimeType", "System.Reflection.", "System.Runtime.Loader.", "System.StubHelpers.", "Internal.Runtime.",
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
        var methodRoots = new List<MethodDefinition>();
        foreach (var rootName in Cli.Roots)
        {
            var name = rootName;
            // "unix:System.X": a root only the Unix CoreLibs (Linux, macOS) carry, such as the marshallers of libSystem.Native's
            // signatures; the Windows CoreLib calls kernel32 instead and has no such type, so it is skipped there.
            if (name.StartsWith("unix:", StringComparison.Ordinal))
            {
                name = name["unix:".Length..];
                if (Core.GetType("Interop")?.NestedTypes.Any(n => n.Name == "Sys") != true) continue;
            }
            // "windows:System.X": a root only the Windows CoreLib's code reaches (overlapped file I/O); skipped for the Unix CoreLibs.
            else if (name.StartsWith("windows:", StringComparison.Ordinal))
            {
                name = name["windows:".Length..];
                if (Core.GetType("Interop")?.NestedTypes.Any(n => n.Name == "Sys") == true) continue;
            }
            // "host:System.Int32,System.Int64::Log2,PopCount": static members of built-in types the slice carries in their host classes
            // (DeclareHosted), every overload, public ones and explicit interface implementations alike ("...INumberBase<System.Int32>.IsNegative").
            // The compiler calls them where it has no lowering of its own (FunctionCodeGen.BuiltinInterfaceMembers.cs).
            if (name.StartsWith("host:", StringComparison.Ordinal))
            {
                var parts = name["host:".Length..].Split("::");
                var members = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var typeName in parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var stop = Core.GetType(typeName) ?? throw new ArgumentException($"{typeName} is not a type of {Cli.CoreLib}");
                    if (!IsStop(stop)) throw new ArgumentException($"host: {typeName} is not a built-in type the slice stops at");
                    // "this.ToString": an instance member of a built-in value type, hosted as a static whose first parameter is `ref T` (the
                    // receiver's address, which `this` is in the member's IL); the compiler passes the receiver's address (#426). Of a built-in
                    // reference type (string.Split), the first parameter is the receiver itself, which the compiler null-checks first (#423).
                    var found = stop.Methods.Where(m => m.HasBody && !m.IsConstructor && members.Any(n =>
                        n.StartsWith("this.", StringComparison.Ordinal)
                            ? !m.IsStatic && (m.Name == n[5..] || m.Name.EndsWith("." + n[5..], StringComparison.Ordinal))
                            : m.IsStatic && (m.Name == n || m.Name.EndsWith("." + n, StringComparison.Ordinal)))).ToList();
                    if (found.Count == 0) throw new ArgumentException($"host: {typeName} has none of {parts[1]}");
                    if (!Hosted.TryGetValue(stop, out var list)) Hosted[stop] = list = [];
                    foreach (var m in found.Where(m => !list.Contains(m))) { list.Add(m); methodRoots.Add(m); }
                }
                continue;
            }
            // "System.Number::Dragon4": a method root, every overload of it; its declaring type is not a root (only what it reaches is cut).
            if (name.IndexOf("::", StringComparison.Ordinal) is var sep and > 0)
            {
                var owner = Core.GetType(name[..sep]) ?? throw new ArgumentException($"{name[..sep]} is not a type of {Cli.CoreLib}");
                var found = owner.Methods.Where(m => m.Name == name[(sep + 2)..]).ToList();
                if (found.Count == 0) throw new ArgumentException($"{name} is not a method of {Cli.CoreLib}");
                UseType(owner, force: true);
                methodRoots.AddRange(found);
                continue;
            }
            var t = Core.GetType(name) ?? throw new ArgumentException($"{name} is not a type of {Cli.CoreLib}");
            RootTypes.Add(t);
            UseType(t, force: true);
            void AddNested(TypeDefinition x) { foreach (var n in x.NestedTypes.Where(n => !FoldedOutTypes.Contains(n))) { RootTypes.Add(n); UseType(n, force: true); AddNested(n); } }
            AddNested(t);
        }
        foreach (var t in RootTypes)
            foreach (var m in t.Methods.Where(m => !FoldedOutMethods.Contains(m))) Enqueue(m);
        foreach (var m in methodRoots) Enqueue(m);
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
        // A SafeHandle a P/Invoke returns (or fills in) is created by the marshaller with Activator.CreateInstance<T>() (SafeHandleMarshaller,
        // the corelib's: corelib/SafeHandles.cs), which this walk does not see: keep its parameterless constructor.
        if (DerivesFrom(d, "System.Runtime.InteropServices.SafeHandle") && d.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && !m.HasParameters) is { } create)
            Enqueue(create);
        // An [EventData] type is a TraceLogging payload: EventSource.Write<T> reads its public properties by reflection (the annotation on T keeps
        // them for NativeAOT), so their accessors are kept although no IL calls them (the event counters' payloads).
        if (d.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Diagnostics.Tracing.EventDataAttribute"))
            KeepPublicProperties(d);
    }

    static bool DerivesFrom(TypeDefinition d, string baseName)
    {
        for (var b = d.BaseType; b is not null; b = SafeResolveType(b)?.BaseType)
            if (b.FullName == baseName) return true;
        return false;
    }

    void KeepPublicProperties(TypeDefinition d)
    {
        foreach (var p in d.Properties)
            foreach (var accessor in new[] { p.GetMethod, p.SetMethod })
                if (accessor is { IsPublic: true }) Enqueue(accessor);
    }

    /// <summary>
    /// [DynamicDependency(DynamicallyAccessedMemberTypes, typeof(X))] on a walked method, as ILLink honours it: the public properties it names stay
    /// (the event counters' CounterPayload, which TraceLogging reads by reflection). Other member kinds are not named by the slices' code.
    /// </summary>
    void KeepDynamicDependencies(MethodDefinition m)
    {
        foreach (var a in m.CustomAttributes.Where(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute"))
            if (a.ConstructorArguments is [{ Value: int kinds }, { Value: TypeReference target }] && (kinds & 0x200) != 0 && SafeResolveType(target) is { } type)
            {
                UseType(type);
                KeepPublicProperties(type);
            }
    }

    void Walk(MethodDefinition m)
    {
        _current = m.DeclaringType.Name + "::" + m.Name;
        KeepDynamicDependencies(m);
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
        if (Cli.Replacement(m) is { } replaced)
        {
            if (Cli.SelfTarget(Core, replaced) is { } self) Enqueue(self);
            return;
        }
        if (!m.HasBody) return;
        var body = m.Body;
        foreach (var v in body.Variables) UseType(v.VariableType);
        foreach (var h in body.ExceptionHandlers) UseType(h.CatchType);
        var live = LiveInstructions(body);
        foreach (var ins in body.Instructions)
        {
            if (live is not null && !live.Contains(ins)) continue;
            if (Cli.FoldedValueAt(ins) is not null || ins.Next is { } next && Cli.IsDisabledLog(ins) && Cli.FoldedValueAt(next) is not null) continue;
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

    /// <summary>
    /// The instructions a run can reach when the folded getters (Cli.Fold) have their constant values: a `call get_X; brtrue/brfalse` pair
    /// whose branch no other instruction jumps to goes one way only. Null when the method calls no folded getter (everything is live).
    /// </summary>
    HashSet<Instruction> LiveInstructions(MethodBody body)
    {
        if (Cli.Fold.Count == 0 && Cli.DisabledEventSources.Count == 0 || !body.Instructions.Any(i => Cli.FoldedValueAt(i) is not null)) return null;
        var targeted = new HashSet<Instruction>();
        foreach (var i in body.Instructions)
            if (i.Operand is Instruction t) targeted.Add(t);
            else if (i.Operand is Instruction[] ts) targeted.UnionWith(ts);
        var live = new HashSet<Instruction>();
        var work = new Stack<Instruction>();
        void Reach(Instruction i) { if (i is not null && live.Add(i)) work.Push(i); }
        Reach(body.Instructions[0]);
        foreach (var h in body.ExceptionHandlers) { Reach(h.HandlerStart); Reach(h.FilterStart); }
        while (work.Count > 0)
        {
            var i = work.Pop();
            var code = i.OpCode.Code;
            if (code is Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S && !targeted.Contains(i)
                && i.Previous is { } previous && Cli.FoldedValueAt(previous) is { } value)
            {
                Reach(value == (code is Code.Brtrue or Code.Brtrue_S) ? (Instruction)i.Operand : i.Next);
                continue;
            }
            if (i.Operand is Instruction target) Reach(target);
            if (i.Operand is Instruction[] targets) foreach (var t in targets) Reach(t);
            if (i.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw) && code is not (Code.Leave or Code.Leave_S or Code.Endfinally))
                Reach(i.Next);
        }
        return live;
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
        if (IsStop(def.DeclaringType))
        {
            // A non-public static helper of a type whose members the slice hosts (UInt128.LeadingZeroCountAsInt32, ulong's private
            // TryConvertFromChecked<TOther>, which the hosted CreateChecked calls): hosted with them, since nothing else provides it.
            if ((def.IsStatic || !def.IsConstructor && (def.DeclaringType.IsValueType || !def.IsVirtual) && HostsInstanceMembers(def.DeclaringType)) && def.HasBody && Hosted.TryGetValue(def.DeclaringType, out var hosted) && Cli.Replacement(def) is null
                && !(RefTypes.TryGetValue(def.DeclaringType.FullName, out var r) && InRef(r, def))
                && !(ShadowMemberNames.TryGetValue(def.DeclaringType.FullName, out var shadowNames) && shadowNames.Contains(ShadowSignatures.Of(def))))
            {
                if (!hosted.Contains(def)) hosted.Add(def);
                Enqueue(def);
                return;
            }
            if (Cli.Replacement(def) is null) { NoteMember(def); NoteUsed(def); }
            return;
        }
        Enqueue(def);
    }

    static MethodDefinition SafeResolve(MethodReference r)
    {
        try { return r.Resolve(); } catch (AssemblyResolutionException) { return null; }
    }

    /// <summary>Whether the slice hosts instance members of this built-in type ("this." `host:` roots): its non-public instance helpers
    /// are hosted with them, as the static ones are.</summary>
    bool HostsInstanceMembers(TypeDefinition type) => Hosted.TryGetValue(type, out var list) && list.Any(m => !m.IsStatic);

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
