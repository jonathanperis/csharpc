// Ownership map of the corelib (issue #191, docs/21-ownership.md): for every file under src/Csharpc/corelib, what it
// shadows, which shared-framework assembly defines each target, and whether it stays csharpc's or is upstream's.
//
//   tools/il-first/ownership.sh                 writes tools/il-first/ownership.tsv
//   tools/il-first/ownership.sh --summary       prints the summary tables (Markdown) from the same data
//   tools/il-first/ownership.sh --contract      prints the FCall / QCall / native-shim surface of the framework assemblies
//   tools/il-first/ownership.sh --unmatched     CoreLib targets that fell through to the default rule (to refine the rules)
//
// The assembly index is read from the running .NET runtime's shared framework (Microsoft.NETCore.App and, next to it,
// Microsoft.AspNetCore.App), i.e. the pinned version in global.json. Lines are attributed per shadow declaration: the
// lines from one [Shadows]/[ShadowMembers] attribute to the next belong to that target (a ShadowMembers class with several
// targets shares its lines equally); header and helper code before the first attribute goes to the first target. The
// classification is by rule (the tables below) plus a few whole-file overrides, so a file's verdict is a statement about
// its family; the "basis" column says whether a file was read ("read") or classified by rule ("rule").
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

var repo = FindRepo();
var corelib = Path.Combine(repo, "src", "Csharpc", "corelib");
var mode = args.Length > 0 ? args[0] : "";

var fw = Framework.Load();
if (mode == "--contract") { fw.PrintContract(); return; }

var files = Directory.GetFiles(corelib, "*.cs", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(corelib, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
var texts = files.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(corelib, f)));

// Word index for the fan-in column: which corelib files mention an identifier.
var wordFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
foreach (var (f, t) in texts)
    foreach (Match m in Regex.Matches(t, @"[A-Za-z_]\w*"))
    {
        if (!wordFiles.TryGetValue(m.Value, out var set)) wordFiles[m.Value] = set = [];
        set.Add(f);
    }

// Compiler-side hooks: string literals in the compiler naming a shadowed type (Intrinsics.cs, BclValueTypes*.cs, ...).
var hookLiterals = new List<string>();
foreach (var dir in new[] { "CodeGen", "Frontend", "Driver" })
    foreach (var f in Directory.GetFiles(Path.Combine(repo, "src", "Csharpc", dir), "*.cs", SearchOption.AllDirectories))
        foreach (Match m in Regex.Matches(File.ReadAllText(f), "\"(System\\.[A-Za-z0-9_.<>,\\[\\]` ]*)\""))
            hookLiterals.Add(m.Groups[1].Value);

var attr = new Regex(@"^\s*\[(Shadows|ShadowMembers)\(""([^""]+)""\)\]", RegexOptions.Compiled);
var declLine = new Regex(@"\b(class|struct|interface|enum|record)\b", RegexOptions.Compiled);
var dataLine = new Regex(@"^\s*(?:new\s*\w*\[\]\s*)?\{?\s*(?:LIT)(?:\s*,\s*(?:LIT))*\s*,?\s*\}?\s*,?\s*(?://.*)?$".Replace("LIT", @"0x[0-9a-fA-F_]+[uUlLbB]*|-?\d+[uUlLfFdDmM]?|'(?:[^'\\]|\\.)+'|""(?:[^""\\]|\\.)*"""), RegexOptions.Compiled);

var rows = new List<Row>();
var unmatched = new SortedDictionary<string, int>();
foreach (var f in files)
{
    var text = texts[f];
    var lines = text.Split('\n');
    int total = text.Count(c => c == '\n');
    // Attribute positions, grouped when they sit on adjacent declarations.
    var hits = new List<(int Line, string Type)>();
    for (int i = 0; i < lines.Length; i++)
        if (attr.Match(lines[i]) is { Success: true } m) hits.Add((i, m.Groups[2].Value));
    var groups = new List<(int Start, List<string> Types)>();
    foreach (var (line, type) in hits)
    {
        if (groups.Count > 0)
        {
            var last = groups[^1];
            int lastLine = hits.Where(h => groups[^1].Types.Contains(h.Type)).Max(h => h.Line);
            bool decl = false;
            for (int k = lastLine + 1; k < line && !decl; k++) decl = declLine.IsMatch(lines[k]) && !lines[k].TrimStart().StartsWith("//");
            if (!decl && line - lastLine <= 3) { if (!last.Types.Contains(type)) last.Types.Add(type); continue; }
        }
        groups.Add((line, [type]));
    }

    // lines per (target type)
    var perType = new Dictionary<string, double>();
    double preambleLines = 0;
    for (int g = 0; g < groups.Count; g++)
    {
        int from = g == 0 && !Rules.Preamble.ContainsKey(f) ? 0 : groups[g].Start;
        if (g == 0 && Rules.Preamble.ContainsKey(f)) preambleLines = groups[0].Start;
        int to = g + 1 < groups.Count ? groups[g + 1].Start : total;
        double share = (double)(to - from) / groups[g].Types.Count;
        foreach (var t in groups[g].Types) perType[t] = perType.GetValueOrDefault(t) + share;
    }

    var targets = perType.Keys.ToList();
    var split = new Dictionary<string, double>();           // class -> lines
    var verdictLines = new Dictionary<string, double>();    // keep/shrink/delete -> lines
    var famLines = new Dictionary<Fam, double>();
    var assemblies = new SortedSet<string>();
    var targetDesc = new List<string>();
    foreach (var (type, l) in perType)
    {
        var asm = fw.AssembliesOf(type);
        foreach (var a in asm) assemblies.Add(a);
        targetDesc.Add(type + "@" + (asm.Count == 0 ? "?" : string.Join("|", asm.Select(Short))));
        var fam = Rules.ForTarget(type, asm, unmatched);
        famLines[fam] = famLines.GetValueOrDefault(fam) + l;
    }

    if (preambleLines > 0) famLines[Rules.Preamble[f]] = preambleLines;
    string basis = "rule";
    Fam? whole = Rules.ForFile(f);
    if (whole is not null && (groups.Count == 0 || Rules.WholeFileOverrides.ContainsKey(f)))
    {
        famLines.Clear();
        famLines[whole] = total;
        if (Rules.Read.Contains(f)) basis = "read";
    }
    else if (groups.Count == 0)
    {
        famLines[Rules.Unclassified] = total;
    }
    if (Rules.Read.Contains(f)) basis = "read";

    foreach (var (fam, l) in famLines)
    {
        split[fam.Class] = split.GetValueOrDefault(fam.Class) + l;
        verdictLines[fam.Verdict] = verdictLines.GetValueOrDefault(fam.Verdict) + l;
    }
    double sum = famLines.Values.Sum();
    // Rounding: scale to the file's line count.
    if (sum > 0) foreach (var k in split.Keys.ToList()) split[k] = split[k] * total / sum;
    if (sum > 0) foreach (var k in verdictLines.Keys.ToList()) verdictLines[k] = verdictLines[k] * total / sum;

    var main = famLines.OrderByDescending(kv => kv.Value).First().Key;
    var cls = split.OrderByDescending(kv => kv.Value).First().Key;
    double del = verdictLines.GetValueOrDefault("delete"), keep = verdictLines.GetValueOrDefault("keep"), shr = verdictLines.GetValueOrDefault("shrink");
    string verdict = total == 0 ? "keep" : del >= 0.7 * total ? "delete-when-IL-wins" : keep >= 0.7 * total ? "keep" : "shrink";

    // A family that holds under 5 % of the file (and under 25 lines) does not decide the file's blockers.
    var weighty = famLines.Where(kv => kv.Value >= Math.Max(25, 0.05 * total)).Select(kv => kv.Key).ToList();
    if (weighty.Count == 0) weighty = famLines.Keys.ToList();
    var blockers = weighty.SelectMany(x => x.Blockers.Split(',', StringSplitOptions.RemoveEmptyEntries)).Distinct().Order().ToList();
    var reasons = famLines.OrderByDescending(kv => kv.Value).Select(kv => kv.Key.Reason).Distinct().Take(2);

    // data lines
    int data = 0;
    foreach (var l in lines) if (l.Length > 300 || dataLine.IsMatch(l)) data++;

    // hooks and fan-in for the biggest target
    var primary = perType.Count > 0 ? perType.OrderByDescending(kv => kv.Value).First().Key : "";
    int hooks = 0, fanIn = 0;
    foreach (var t in perType.Keys) hooks += Hooks(t);
    if (primary.Length > 0)
    {
        var simple = Regex.Replace(primary[(primary.LastIndexOfAny(['.', '+']) + 1)..], "`\\d+", "");
        fanIn = wordFiles.TryGetValue(simple, out var set) ? set.Count(x => x != f) : 0;
    }

    var head = string.Join('\n', lines.Take(25));
    string origin = Regex.IsMatch(head, "dotnet/runtime|Ported (to csharpc )?from|after dotnet|port of") ? "ported" : "original";

    rows.Add(new Row(f, total, Encoding.UTF8.GetByteCount(text), origin, data, cls, split, targetDesc, string.Join(",", assemblies.Select(Short)), hooks, fanIn, verdict, string.Join(" / ", reasons), string.Join(",", blockers), basis, main.Id));
}

int Hooks(string type)
{
    // "System.Collections.Generic.List`1+Enumerator" -> "System.Collections.Generic.List.Enumerator"
    var name = Regex.Replace(type, "`\\d+", "").Replace('+', '.');
    int n = 0;
    foreach (var lit in hookLiterals)
    {
        int cut = lit.IndexOfAny(['(', '<', '[']);
        var head = cut < 0 ? lit : lit[..cut];
        if (head == name || head.StartsWith(name + ".", StringComparison.Ordinal)) n++;
    }
    return n;
}

if (mode == "--unmatched")
{
    foreach (var (t, l) in unmatched.OrderByDescending(kv => kv.Value)) Console.WriteLine($"{l,6}  {t}");
    return;
}

if (mode == "--summary") { Summary.Print(rows, fw); return; }

var outPath = Path.Combine(repo, "tools", "il-first", "ownership.tsv");
var sb = new StringBuilder();
sb.AppendLine("path\tlines\tbytes\torigin\tdata_lines\tclass\tclass_split\tshadow_targets\tdefining_assemblies\tcompiler_hooks\tfan_in\tverdict\tfamily\treason\tblockers\tbasis");
foreach (var r in rows)
{
    string splitText = string.Join(";", r.Split.Where(kv => Math.Round(kv.Value) >= 1).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{(int)Math.Round(kv.Value)}"));
    string targetsText = string.Join(" ", r.Targets.Select(t => t.StartsWith("System.") ? t[7..] : t));
    sb.Append(r.Path).Append('\t').Append(r.Lines).Append('\t').Append(r.Bytes).Append('\t').Append(r.Origin).Append('\t').Append(r.Data).Append('\t')
      .Append(r.Class).Append('\t').Append(splitText).Append('\t').Append(targetsText).Append('\t').Append(r.Assemblies).Append('\t')
      .Append(r.Hooks).Append('\t').Append(r.FanIn).Append('\t').Append(r.Verdict).Append('\t').Append(r.Family).Append('\t').Append(r.Reason).Append('\t').Append(r.Blockers).Append('\t').Append(r.Basis).Append('\n');
}
File.WriteAllText(outPath, sb.ToString());
Console.WriteLine($"{rows.Count} files, {rows.Sum(r => r.Lines)} lines -> {Path.GetRelativePath(repo, outPath)}");

static string Short(string a) => a.StartsWith("System.") ? a[7..] : a;

static string FindRepo()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Csharpc", "corelib"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("run from inside the csharpc repository");
}

// ---------------------------------------------------------------------------------------------------------------------

record Row(string Path, int Lines, int Bytes, string Origin, int Data, string Class, Dictionary<string, double> Split, List<string> Targets,
           string Assemblies, int Hooks, int FanIn, string Verdict, string Reason, string Blockers, string Basis, string Family);

/// <summary>A family of corelib code: what it is, what happens to it, and what stands in the way of deleting it.</summary>
record Fam(string Id, string Class, string Verdict, string Reason, string Blockers);

static class Rules
{
    // Classes
    public const string ClContract = "CORE-CONTRACT", ClPure = "CORELIB-PURE", ClAbove = "FRAMEWORK-ABOVE", ClGlue = "GLUE", ClData = "DATA";

    // Blocker codes (legend in docs/21-ownership.md):
    //  LAYOUT   real code reads String/Array/Object internals (RawData, _firstChar, MethodTable): the contract must provide them
    //  EQCMP    EqualityComparer<T>.Default / Comparer<T>.Default and CoreIntrinsics.AreEqual/HashOf/Compare (synthesized by codegen)
    //  SIMD     upstream's vectorized paths (Vector128/256/512, SpanHelpers); today's searches/sums go to src/runtime/span.c
    //  ICU      libSystem.Globalization.Native imports (csharpc links its own port of the shim, src/runtime/globalization)
    //  SYSNATIVE libSystem.Native imports (Interop.Sys: files, env, time, process, sockets, epoll); csharpc calls libc through Posix.cs
    //  OPENSSL  libSystem.Security.Cryptography.Native.OpenSsl imports; csharpc calls libcrypto/libssl directly (OpenSsl.cs, AesNative.cs)
    //  THREAD   Thread, Monitor, Interlocked, thread statics, ExecutionContext: the runtime's threads (docs/14-threading.md)
    //  TASK     Task and the async builders are runtime objects of the coroutine lowering (docs/10-async.md, src/runtime/async.c)
    //  REFLECT  reflection metadata (ADR 0018): upstream's RuntimeType/RuntimeMethodInfo sit on the CLR's MethodTable
    //  EXC      Exception layout, stack traces, ExceptionDispatchInfo (ADR 0005, ADR 0012)
    //  GC       GCHandle, weak references, finalizers (ADR 0004, ADR 0021)
    //  RES      SR resource strings: messages must stay byte-identical (Resources.cs, ModuleCodeGen.Resources.cs)
    //  GENMATH  static abstract interface members on the primitives (INumber<T>...): compiler-mapped statics (FunctionCodeGen.Calls.cs)
    //  SRCGEN   a source generator (GeneratedRegex, JsonSerializerContext) has to be linked instead of csharpc's own
    //  NEWTYPES .NET 11 CoreLib types the IL reaches (Decimal32/64/128, BFloat16...) that the corelib does not have
    //  EVENTPIPE EventPipe / NativeRuntimeEventSource QCalls
    //  APPCTX   AppContext switches and ILLink substitutions (ModuleCodeGen.IlSubstitutions.cs)
    //  CONSOLE  console fast paths in CodeGen/Intrinsics.cs (WriteLine -> csrt_console_*)
    //  AB187    the library's A/B is the other agent's work (issues #187/#188/#189, ADR 0023); no verdict here beyond the interim numbers
    //  MEMORY   Memory<T>/ReadOnlyMemory<T> as csharpc lays them out (no array/MemoryManager/string object recorded)
    //  NONE     nothing known beyond the common contract (dropped from the blockers column)

    // RES (resource strings) and NONE are not blockers: every flip needs the assembly's embedded resources, which the IL front-end reads.
    static Fam F(string id, string cls, string verdict, string reason, string blockers) =>
        new(id, cls, verdict, reason, string.Join(",", blockers.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(b => b is not ("RES" or "NONE"))));

    public static readonly Fam Unclassified = F("unclassified", ClGlue, "keep", "not classified", "");

    static readonly Fam CoreTypes = F("core-types", ClContract, "keep", "layout and metadata of the core types, read directly by the compiler and the runtime (csrt.h)", "");
    static readonly Fam ArrayT = F("array", ClContract, "shrink", "Array layout, Clone/Copy/GetLength stay; the generic static members (Sort, IndexOf, Resize, Fill, Reverse, BinarySearch...) are plain managed code upstream", "LAYOUT,EQCMP,SIMD");
    static readonly Fam StringT = F("string", ClContract, "shrink", "String layout and hot paths stay; plain-managed members (Split, Join, Trim, Pad, Replace...) could come from IL member by member", "LAYOUT,SIMD,ICU");
    static readonly Fam Exc = F("exception", ClContract, "keep", "exception objects, dispatch info and stack traces are the runtime's (ADR 0005, 0012)", "EXC");
    static readonly Fam Thread = F("thread", ClContract, "keep", "threads, monitors, interlocked ops, waits, events, timers and execution/synchronization context are the runtime's (docs/14, src/runtime/threads.c, async.c)", "THREAD");
    static readonly Fam TaskT = F("task", ClContract, "keep", "tasks, cancellation sources and the async builders are runtime objects of the LLVM coroutine lowering (docs/10-async.md, src/runtime/async.c); upstream's Task state machine needs a different async lowering", "TASK");
    static readonly Fam Gc = F("gc", ClContract, "keep", "GC handles, weak references and finalization are the collector's (ADR 0004, 0021)", "GC");
    static readonly Fam RuntimeT = F("runtime-services", ClContract, "keep", "runtime services and the OS boundary of CoreLib: helpers, unsafe access, marshalling, environment, app domain", "LAYOUT");
    static readonly Fam Reflection = F("reflection", ClContract, "keep", "reflection metadata emitted by the compiler (ADR 0018); upstream's RuntimeType family sits on the CLR's MethodTable", "REFLECT");
    static readonly Fam Stack = F("stack-trace", ClContract, "keep", "stack trace objects over the compiler's frame tables (ADR 0012)", "EXC");
    static readonly Fam EqCmp = F("default-comparers", ClContract, "shrink", "default comparers are synthesized by codegen per instantiation (CoreIntrinsics); the public shell could be upstream's", "EQCMP");
    static readonly Fam Span = F("span-helpers", ClContract, "shrink", "span helpers: searches and sums run in src/runtime/span.c; the managed algorithms could be upstream's once the SIMD mapping is measured", "SIMD,LAYOUT");

    static readonly Fam Coll = F("collections", ClPure, "delete", "plain managed code upstream (System.Private.CoreLib): mirrors (List, Dictionary, HashSet) or ports (ConcurrentDictionary, Hashtable) of it", "EQCMP");
    static readonly Fam Text = F("text", ClPure, "delete", "plain managed code upstream (StringBuilder, encodings, Convert, Base64, UTF-8 helpers)", "LAYOUT,SIMD,RES");
    static readonly Fam Numbers = F("numbers", ClPure, "delete", "number formatting, parsing and arithmetic are plain managed code upstream (Number.*, Math, Half, Int128, Decimal)", "GENMATH,LAYOUT,RES");
    static readonly Fam DateTimeF = F("datetime", ClPure, "delete", "DateTime, TimeSpan, Guid and friends are plain managed code upstream; clock, time zones and Guid.NewGuid's entropy come from Interop.Sys", "GENMATH,SYSNATIVE");
    static readonly Fam Calendars = F("calendars", ClPure, "delete", "calendar arithmetic is plain managed code upstream; the shadow is already a port of it. Cluster with CultureInfo/CalendarData (era data comes from ICU)", "ICU");
    static readonly Fam Culture = F("culture", ClPure, "delete", "culture data, collation and casing are upstream's managed code over the ICU shim, which csharpc links as a port (src/runtime/globalization)", "ICU,RES");
    static readonly Fam IoPure = F("io", ClPure, "delete", "File, Directory, Path and Stream are plain managed code upstream over Interop.Sys (libSystem.Native)", "SYSNATIVE,RES");
    static readonly Fam Interop = F("interop-managed", ClPure, "delete", "marshallers, SafeHandle and ComTypes are plain managed code upstream", "RES,GC");
    static readonly Fam Resources = F("resources", ClPure, "delete", "ResourceManager/ResourceReader: managed upstream; the messages come from the embedded .resources", "RES,APPCTX");
    static readonly Fam Tracing = F("tracing", ClPure, "delete", "EventSource/EventListener/counters: managed upstream, but its providers are EventPipe QCalls the contract would stub", "EVENTPIPE,REFLECT");
    static readonly Fam Attributes = F("attributes", ClPure, "delete", "attribute types without logic (compiler-synthesized attributes, assembly metadata attributes)", "NONE");
    static readonly Fam Sync = F("sync-managed", ClPure, "delete", "SpinLock, ReaderWriterLockSlim, SemaphoreSlim, PeriodicTimer, ThreadLocal: managed code over Monitor/Interlocked/Task upstream (ports in the corelib)", "THREAD,TASK");
    static readonly Fam ThrowH = F("throw-helpers", ClPure, "delete", "ArgumentNullException.ThrowIfNull & co.: managed upstream", "RES");
    static readonly Fam OtherPure = F("corelib-other", ClPure, "delete", "System.Private.CoreLib type that is plain managed code upstream", "RES");
    static readonly Fam Serialization = F("serialization", ClPure, "delete", "SerializationInfo & co.: plain managed code upstream", "REFLECT,RES");
    static readonly Fam Random = F("random", ClPure, "delete", "Random (xoshiro and the legacy implementation): already a verbatim port of upstream", "SYSNATIVE");
    static readonly Fam RuntimeOnlyPure = F("misc-runtime-pure", ClPure, "delete", "managed upstream, small", "RES");

    static Fam Above(string id, string reason, string blockers, string verdict = "delete") => F(id, ClAbove, verdict, reason, blockers);

    static readonly Dictionary<string, Fam> AssemblyFamilies = new()
    {
        ["System.Text.Json"] = Above("json", "System.Text.Json: from IL (ADR 0023, #286)", "NONE"),
        ["System.Text.Encodings.Web"] = Above("json", "System.Text.Encodings.Web: from IL (ADR 0023, #286)", "NONE"),
        ["System.Text.RegularExpressions"] = Above("regex", "System.Text.RegularExpressions: engine correct from IL (20/22 samples, #188); size is the cost", "AB187,SRCGEN,SIMD"),
        ["System.Linq"] = Above("linq", "System.Linq: managed upstream; hand-written iterator classes and span fast paths today", "SIMD,EQCMP"),
        ["System.Memory"] = Above("memory", "System.Memory: from IL (ADR 0023, #306)", "NONE"),
        ["System.Runtime.Numerics"] = Above("numerics", "System.Runtime.Numerics (BigInteger): managed upstream", "GENMATH"),
        ["System.Collections"] = Above("collections-asm", "System.Collections (Stack, LinkedList, SortedSet, PriorityQueue, OrderedDictionary...): managed upstream", "EQCMP"),
        ["System.Collections.Concurrent"] = Above("concurrent", "System.Collections.Concurrent: managed upstream over Interlocked/Monitor", "THREAD,EQCMP"),
        ["System.Collections.NonGeneric"] = Above("collections-asm", "System.Collections.NonGeneric: from IL (ADR 0023, #271)", "EQCMP"),
        ["System.ObjectModel"] = Above("collections-asm", "System.ObjectModel (ObservableCollection): managed upstream", "NONE"),
        ["System.ComponentModel"] = Above("componentmodel", "System.ComponentModel: managed upstream", "REFLECT"),
        ["System.ComponentModel.TypeConverter"] = Above("componentmodel", "System.ComponentModel.TypeConverter: managed upstream, reflection-heavy", "REFLECT,APPCTX"),
        ["System.Threading"] = Above("sync-asm", "System.Threading (Barrier, CountdownEvent): managed upstream", "THREAD"),
        ["System.Threading.Tasks.Parallel"] = Above("parallel", "System.Threading.Tasks.Parallel: managed upstream over Task and the thread pool; Parallel.For of arrays goes to the runtime today", "TASK,THREAD"),
        ["System.Private.Uri"] = Above("uri", "System.Uri: upstream's is complete; csharpc's models only part (no escaping, no user info)", "ICU,RES"),
        ["System.Security.Cryptography"] = Above("crypto", "System.Security.Cryptography: managed algorithms plus the OpenSSL shim upstream; csharpc ports the managed layers over libcrypto", "OPENSSL,RES"),
        ["System.Net.Security"] = Above("net", "System.Net.Security (SslStream, TLS): managed upstream over libSystem.Net.Security.Native and OpenSSL", "OPENSSL,SYSNATIVE"),
        ["System.Net.Sockets"] = Above("net", "System.Net.Sockets: managed upstream over libSystem.Native (epoll, sockaddr shims)", "SYSNATIVE,THREAD"),
        ["System.Net.Primitives"] = Above("net", "System.Net.Primitives: mixed assembly already (IPAddress/EndPoint shadows, the rest IL; ADR 0013)", "SYSNATIVE"),
        ["System.Net.NetworkInformation"] = Above("net", "System.Net.NetworkInformation: managed upstream over libSystem.Native", "SYSNATIVE"),
        ["System.Net.NameResolution"] = Above("net", "System.Net.NameResolution: managed upstream over libSystem.Native", "SYSNATIVE"),
        ["System.Net.Quic"] = Above("net", "System.Net.Quic: msquic binding; csharpc has a throwing stub", "SYSNATIVE"),
        ["System.Console"] = Above("console", "System.Console: managed upstream over libSystem.Native; csharpc's console is runtime fast paths (csrt_console_*)", "SYSNATIVE,CONSOLE"),
        ["System.IO.FileSystem.Watcher"] = Above("io-asm", "FileSystemWatcher: managed upstream over inotify/kqueue shims", "SYSNATIVE,THREAD"),
        ["System.IO.MemoryMappedFiles"] = Above("io-asm", "MemoryMappedFiles: managed upstream over libSystem.Native (mmap)", "SYSNATIVE"),
        ["System.IO.Pipes"] = Above("io-asm", "Pipes: managed upstream over libSystem.Native", "SYSNATIVE"),
        ["System.Diagnostics.Process"] = Above("process", "System.Diagnostics.Process: managed upstream over libSystem.Native (fork/exec shim)", "SYSNATIVE,THREAD"),
        ["System.Runtime.InteropServices"] = Above("interop-asm", "System.Runtime.InteropServices: managed upstream", "NONE"),
    };

    // Whole-file overrides: files that hold no [Shadows] attribute (support code), or whose family differs from their target.
    public static readonly Dictionary<string, Fam> Preamble = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, Fam> WholeFileOverrides = BuildOverrides();

    // Files read while building the table (the sample that verifies the rules; docs/21-ownership.md).
    public static readonly HashSet<string> Read = new(StringComparer.Ordinal)
    {
        "ArrayExtras.cs", "Buffers/ReadOnlySequence.cs", "Queue.cs", "Concurrent/ConcurrentDictionary.cs", "Convert.cs", "Core.cs",
        "Crypto/AesNative.cs", "Dictionary.cs", "ExceptionData.cs", "FloatingPointMath.cs", "Globalization/CultureData.cs", "Globalization/IcuLocaleData.cs",
        "Globalization/Interop.Globalization.cs", "Guid.cs", "HashSet.cs", "IO/Posix.cs", "IO/Streams.cs", "List.cs", "Net/NetNative.cs",
        "Net/UriRelative.cs", "Number.Formatting.cs", "Regex/RegexCaseEquivalences.Data.cs", "Resources.cs", "Rune.cs", "StringBuilder.cs", "StringExtras.cs",
        "Tasks/AsyncGate.cs", "Tasks/TaskState.cs", "Text/Encoding.cs", "Text/Latin1BestFit.cs", "Threading/ReaderWriterLockSlim.cs", "Tracing/EventSource.cs",
        "UnicodeData.g.cs", "AggregateExceptionSupport.cs", "ArrayInterfaces.cs", "BigIntegerOps.cs", "BigNum.cs", "DecimalOps.cs", "DelegateThunks.cs",
        "Dynamic/DynamicObjects.cs", "EnumRuntime.cs", "SpanSearch.cs", "RuntimeSpans.cs", "ValueBuilders.cs", "Random/CoreDebug.cs",
    };

    static Dictionary<string, Fam> BuildOverrides()
    {
        var d = new Dictionary<string, Fam>(StringComparer.Ordinal);
        void Add(string path, Fam fam) => d[path] = fam;
        var glueRt = F("compiler-hooks", ClGlue, "keep", "csharpc-specific support called by the code generator (shadow infrastructure, CoreIntrinsics, synthesized bodies)", "");
        var osGlue = F("os-boundary", ClGlue, "keep", "csharpc's OS boundary in C#: libc / Win32 / Winsock P/Invokes and errno mapping; upstream's equivalent is its native shims (libSystem.Native), which csharpc could build instead", "SYSNATIVE");
        var cryptoGlue = F("crypto-glue", ClGlue, "keep", "libcrypto/libssl/CommonCrypto/CNG bindings; upstream's equivalent is libSystem.Security.Cryptography.Native.OpenSsl", "OPENSSL");
        var ported = F("ports-glue", ClGlue, "delete", "stand-ins for internals that ports of upstream code call (ValueStringBuilder, Debug.Assert, GetRandomBytes); go with the ports", "NONE");

        Preamble["Core.cs"] = glueRt;
        Add("ArrayInterfaces.cs", glueRt);
        Add("DelegateThunks.cs", F("reverse-thunks", ClContract, "keep", "reverse P/Invoke thunk slots (ModuleCodeGen.ReverseThunks.cs): delegate-to-function-pointer marshalling", "LAYOUT"));
        Add("AggregateExceptionSupport.cs", F("exception", ClContract, "keep", "AggregateException construction called by the code generator", "EXC"));
        Add("ExceptionData.cs", F("exception", ClContract, "keep", "Exception.Data/HelpLink/Source over csrt_Exception, which has no slot for them (csrt.h)", "EXC"));
        Add("DebugProvider.cs", F("debug-provider", ClPure, "delete", "port of System.Diagnostics.DebugProvider", "NONE"));
        Add("EnumRuntime.cs", F("enum-runtime", ClContract, "keep", "Enum.Parse/IsDefined with a run-time Type: needs the run-time enum member table (reflection metadata)", "REFLECT"));
        Add("Dynamic/DynamicObjects.cs", F("dynamic", ClGlue, "keep", "the calls the compile-time `dynamic` binder makes (ADR 0019); upstream's meta-object path builds expression trees", "NONE"));
        Add("BigIntegerOps.cs", Above("numerics", "System.Numerics.BigInteger over uint[]: C# port of what was C; upstream's BigInteger is managed", "GENMATH"));
        Add("BigNum.cs", F("numbers", ClPure, "delete", "fixed-capacity bignum for float formatting/parsing; upstream's Number.BigInteger replaces it", "NONE"));
        Add("BinaryIntegerSupport.cs", F("numbers", ClPure, "delete", "IBinaryInteger<T>.ReadBigEndian for the primitives: managed upstream", "GENMATH"));
        Add("DecimalOps.cs", F("numbers", ClPure, "delete", "decimal arithmetic (formerly decimal.c): upstream's DecCalc is managed", "LAYOUT,GENMATH"));
        Add("Number.cs", Numbers);
        Add("Number.Formatting.cs", Numbers);
        Add("Number.Parsing.cs", Numbers);
        Add("Number.Parsing.Culture.cs", Numbers);
        Add("OrdinalCasing.cs", F("culture", ClPure, "delete", "OrdinalIgnoreCase comparison over the invariant simple case mapping: managed upstream (Ordinal, OrdinalCasing)", "ICU"));
        Add("SpanSearch.cs", Span);
        Add("RuntimeSpans.cs", Span);
        Add("ThreadApartment.cs", F("thread", ClContract, "keep", "Thread.GetApartmentState & co.", "THREAD"));
        Add("ValueBuilders.cs", ported);
        Add("Random/CoreDebug.cs", ported);
        Add("Random/Random.CompatImpl.cs", Random);
        Add("Random/Random.ImplBase.cs", Random);
        Add("Random/Random.Xoshiro256StarStarImpl.cs", Random);
        Add("Random/Random.cs", Random);
        Add("Reflection/MemberLookup.cs", Reflection);
        Add("Reflection/ObjectArrayDelegates.cs", F("reflection", ClContract, "keep", "NativeAOT's DynamicDelegateAugments for System.Linq.Expressions (ModuleCodeGen.ObjectArrayDelegates.cs)", "REFLECT"));
        Add("UnicodeData.g.cs", F("unicode-data", ClData, "keep", "generated Unicode category table (tools/unicode) behind Char.GetUnicodeCategory/IsLetter...; goes when Char and CharUnicodeInfo come from IL with their own embedded data", "NONE"));
        Add("Globalization/BidiData.g.cs", F("unicode-data", ClData, "keep", "generated bidi table (tools/unicode); replaced when CharUnicodeInfo comes from IL", "NONE"));
        Add("Globalization/GraphemeData.g.cs", F("unicode-data", ClData, "keep", "generated grapheme table (tools/unicode); replaced when StringInfo/GraphemeBreak come from IL", "NONE"));
        Add("Globalization/BidiData.cs", F("unicode-data", ClData, "keep", "bidi category lookup over the generated table", "NONE"));
        Add("Text/Latin1BestFit.cs", F("encoding-data", ClData, "delete", "Latin1 encoder best-fit fallback and its table, ported verbatim from upstream", "NONE"));
        Add("Regex/RegexCaseEquivalences.Data.cs", F("regex", ClData, "delete", "generated casing-equivalence table of System.Text.RegularExpressions", "AB187"));
        Add("Regex/RegexCharClass.Tables.cs", F("regex", ClData, "delete", "generated Unicode category/block tables of System.Text.RegularExpressions", "AB187"));
        Add("Globalization/IcuLocaleData.cs", F("icu-locale-data", ClData, "delete", "port of upstream's IcuLocaleData (LCID, code page and geo-id tables of 864 cultures; csharpc adds lazily built index caches); comes with CultureData from IL", "ICU"));
        Add("Globalization/Interop.Globalization.cs", F("os-boundary", ClGlue, "delete", "port of upstream's Interop.ICU/Locale/Calendar/Casing/Collation/Normalization/Idna.cs binding the linked shim; upstream's IL carries the same imports", "ICU"));
        Add("Globalization/GlobalizationMode.cs", F("culture", ClPure, "delete", "port of GlobalizationMode (invariant vs ICU)", "ICU,APPCTX"));
        Add("Globalization/GlobalizationSupport.cs", F("globalization-glue", ClGlue, "keep", "csharpc's links between the ported globalization code and the corelib (Ordinal, casing)", "NONE"));
        Add("Globalization/GlobalizationHelpers.cs", F("globalization-glue", ClGlue, "delete", "stand-ins for internals the ported code calls", "ICU"));
        Add("Globalization/SR.cs", F("resources", ClPure, "delete", "SR resource-string accessor for the ported globalization code", "RES"));
        Add("Globalization/CultureStrings.cs", F("globalization-glue", ClGlue, "delete", "culture name strings for the ported code", "ICU"));
        Add("Globalization/DateTimeParse.cs", F("datetime", ClPure, "delete", "DateTime.Parse (6 373 lines): verbatim port of upstream's DateTimeParse", "ICU,RES"));
        Add("Globalization/DateTimeFormat.cs", F("datetime", ClPure, "delete", "DateTime formatting: port of upstream's DateTimeFormat", "ICU,RES"));
        Add("Globalization/TimeSpanParse.cs", F("datetime", ClPure, "delete", "TimeSpan.Parse: port of upstream's TimeSpanParse; reads NumberFormatInfo/DateTimeFormatInfo of the current culture", "ICU"));
        Add("Globalization/DateOnlyTimeOnlyCulture.cs", F("datetime", ClPure, "delete", "DateOnly/TimeOnly parsing with a culture: port of upstream", "ICU"));
        Add("Globalization/DateTimeFormatInfoScanner.cs", F("culture", ClPure, "delete", "port of DateTimeFormatInfoScanner", "ICU"));
        Add("Globalization/CalendricalCalculationsHelper.cs", Calendars);
        Add("Globalization/GregorianCalendarHelper.cs", Calendars);
        Add("Globalization/HebrewNumber.cs", Calendars);
        Add("Globalization/InvariantModeCasing.cs", F("culture", ClPure, "delete", "port of InvariantModeCasing", "NONE"));
        Add("Globalization/SurrogateCasing.cs", F("culture", ClPure, "delete", "port of SurrogateCasing", "NONE"));
        foreach (var p in new[] { "Globalization/CalendarData.cs", "Globalization/CalendarData.Icu.cs", "Globalization/CalendarData.Unix.cs", "Globalization/CalendarData.Windows.cs",
                                  "Globalization/CompareInfo.Icu.cs", "Globalization/CompareInfo.Invariant.cs", "Globalization/CultureData.cs", "Globalization/CultureData.Icu.cs",
                                  "Globalization/CultureData.Unix.cs", "Globalization/CultureData.Windows.cs", "Globalization/CultureInfo.Unix.cs",
                                  "Globalization/HijriCalendar.Unix.cs", "Globalization/IdnMapping.Icu.cs", "Globalization/JapaneseCalendar.Icu.cs", "Globalization/TextInfo.Icu.cs" })
            Add(p, Culture);
        foreach (var p in new[] { "Tasks/AsyncGate.cs" }) Add(p, F("task-support", ClGlue, "keep", "WaitAsync-less stand-in used by streams (no SemaphoreSlim.WaitAsync yet)", "TASK"));
        foreach (var p in new[] { "Tasks/TaskCompletionChecks.cs", "Tasks/TaskExceptions.cs", "Tasks/TaskFactoryMembers.cs", "Tasks/TaskFactoryOps.cs", "Tasks/TaskState.cs", "Tasks/WaitTimeouts.cs" })
            Add(p, TaskT);
        Add("Tasks/ParallelForEach.cs", Above("parallel", "Parallel.ForEach over enumerables and partitioners", "TASK,THREAD"));
        Add("IO/ConsoleKeysWindows.cs", osGlue);
        Add("IO/FileStatus.cs", IoPure);
        Add("IO/FileSystemWatcher.Linux.cs", Above("io-asm", "FileSystemWatcher over inotify: a port of upstream's Linux implementation", "SYSNATIVE,THREAD"));
        Add("IO/PathSpans.cs", IoPure);
        Add("IO/PathWindows.cs", IoPure);
        Add("IO/Posix.cs", osGlue);
        Add("IO/Win32.cs", osGlue);
        Add("Crypto/AesNative.cs", cryptoGlue);
        Add("Crypto/OpenSsl.cs", cryptoGlue);
        Add("Net/NetNative.cs", osGlue);
        Add("Net/SocketEngine.cs", osGlue);
        Add("Net/UriRelative.cs", Above("uri", "System.Uri's relative-URI half, ported from upstream", "ICU,RES"));
        Add("Random/Random.cs", Random);
        return d;
    }

    /// <summary>Directory rules for files that hold no shadow attribute and have no explicit override.</summary>
    public static Fam? ForFile(string path)
    {
        if (WholeFileOverrides.TryGetValue(path, out var o)) return o;
        if (path.StartsWith("Regex/", StringComparison.Ordinal)) return AssemblyFamilies["System.Text.RegularExpressions"];
        if (path.StartsWith("Json/", StringComparison.Ordinal)) return AssemblyFamilies["System.Text.Json"];
        if (path.StartsWith("Globalization/", StringComparison.Ordinal)) return Culture;
        if (path.StartsWith("Tasks/", StringComparison.Ordinal)) return TaskT;
        if (path.StartsWith("Net/", StringComparison.Ordinal)) return AssemblyFamilies["System.Net.Sockets"];
        if (path.StartsWith("Crypto/", StringComparison.Ordinal)) return AssemblyFamilies["System.Security.Cryptography"];
        if (path.StartsWith("IO/", StringComparison.Ordinal)) return IoPure;
        return null;
    }

    // Rules for System.Private.CoreLib targets, first match wins.
    static readonly (Regex Type, Fam Fam)[] CoreLibRules =
    [
        (R(@"^System\.Array$"), ArrayT),
        (R(@"^System\.(Object|Type|Delegate|MulticastDelegate|Enum|ValueType|RuntimeTypeHandle|RuntimeMethodHandle|RuntimeFieldHandle|TypedReference|Attribute)$"), CoreTypes),
        (R(@"^System\.String$"), StringT),
        (R(@"^System\.Runtime\.ExceptionServices\.|^System\.Runtime\.CompilerServices\.RuntimeWrappedException$"), Exc),
        (R(@"^System\.Threading\.(Thread|Monitor|Interlocked|Volatile|ThreadPool|ExecutionContext|AsyncFlowControl|AsyncLocal`1|WaitHandle|RegisteredWaitHandle|NamedWaitHandleOptions|ManualResetEventSlim|Timer|SynchronizationContext)$|^System\.TimeProvider$|^Microsoft\.Win32\.SafeHandles\.SafeWaitHandle$"), Thread),
        (R(@"^System\.Threading\.Tasks\.(Task|TaskScheduler|TaskFactory|TaskFactory`1|ThreadPoolTaskScheduler|UnobservedTaskExceptionEventArgs|ValueTask|ValueTask`1|TaskExtensions|Sources\..*)$|^System\.Runtime\.CompilerServices\.(Async\w*MethodBuilder.*|PoolingAsync.*|AsyncHelpers|ValueTaskAwaiter`1|ConfiguredValueTaskAwaitable.*|ConfiguredCancelableAsyncEnumerable.*|ConfiguredAsyncDisposable)$|^System\.Threading\.Tasks\.TaskAsyncEnumerableExtensions$|^System\.Threading\.CancellationToken\w*$"), TaskT),
        (R(@"^System\.(GC|GCMemoryInfo|GCGenerationInfo|WeakReference|WeakReference`1)$|GCHandle|CriticalFinalizerObject|ConditionalWeakTable"), Gc),
        (R(@"^System\.Reflection\.(MemberInfo|MethodBase|ConstructorInfo|MethodInfo|ParameterInfo|PropertyInfo|FieldInfo|EventInfo|Module|Assembly|RuntimeModule|ConstructorInvoker|MethodInvoker|Runtime\w+|RtFieldInfo)$|^System\.Reflection\.(RuntimeAssembly|CustomAttribute(Data|NamedArgument|TypedArgument)|Missing|CustomAttributeExtensions|RuntimeReflectionExtensions)|^System\.Runtime\.Loader\."), Reflection),
        (R(@"^System\.Diagnostics\.(StackFrame|StackTrace|DiagnosticMethodInfo)$"), Stack),
        (R(@"^System\.Collections\.Generic\.(EqualityComparer`1|Comparer`1)$|^System\.HashCode$"), EqCmp),
        (R(@"^System\.(MemoryExtensions.*|Span`1|ReadOnlySpan`1)$"), Span),
        (R(@"^System\.Runtime\.CompilerServices\.(RuntimeHelpers|Unsafe|RuntimeFeature)$|^System\.Runtime\.InteropServices\.(MemoryMarshal|Marshal|NativeMemory|NativeLibrary|RuntimeInformation|RuntimeEnvironment|OSPlatform)$|^System\.(Buffer|Environment.*|OperatingSystem|AppDomain|AppContext|Activator|ResolveEventArgs)$|^System\.Runtime\.JitInfo$|^System\.Diagnostics\.Debugger$|ReflectionTypeLoadException"), RuntimeT),

        (R(@"^System\.Collections\.|^System\.(ArraySegment`1.*|Tuple`?\d*|ValueTuple`?\d*|Lazy`1|StringComparer|EventArgs)$|^System\.Buffers\.ArrayPool|StrongBox|CollectionsMarshal|^System\.Threading\.LazyInitializer$"), Coll),
        (R(@"^System\.Text\.|^System\.Buffers\.(?!ArrayPool)|^System\.(BitConverter|Convert|FormattableString)$|FormattableStringFactory|DefaultInterpolatedStringHandler"), Text),
        (R(@"^System\.Globalization\.\w*Calendar$"), Calendars),
        (R(@"^System\.Globalization\."), Culture),
        (R(@"^System\.(Int16|Int32|Int64|SByte|Byte|UInt16|UInt32|UInt64|Single|Double|Half|Int128|UInt128|Decimal|Math|MathF|Char|Boolean|IntPtr|UIntPtr|Numerics\..*)$"), Numbers),
        (R(@"^System\.(DateTime|DateTimeOffset|DateOnly|TimeOnly|TimeSpan|TimeZoneInfo|TimeProvider|Guid|Version)$"), DateTimeF),
        (R(@"^System\.Random$"), Random),
        (R(@"^System\.IO\.|^Microsoft\.Win32\.SafeHandles\.SafeFileHandle$"), IoPure),
        (R(@"^System\.Runtime\.InteropServices\.|^Microsoft\.Win32\.SafeHandles\.|^System\.Security\.SecureString$|^System\.ComponentModel\.Win32Exception$"), Interop),
        (R(@"^System\.Resources\."), Resources),
        (R(@"^System\.Diagnostics\.Tracing\."), Tracing),
        (R(@"Attribute$|^System\.Reflection\.ParameterModifier$|^System\.Diagnostics\.CodeAnalysis\."), Attributes),
        (R(@"^System\.Threading\.(SpinLock|SpinWait|ReaderWriterLockSlim|SemaphoreSlim|PeriodicTimer|ThreadLocal`1|AsyncLocalValueChangedArgs`1|Timeout)$|^System\.Threading\.Tasks\.ConcurrentExclusiveSchedulerPair$|TaskToAsyncResult"), Sync),
        (R(@"^System\.(ArgumentNullException|ArgumentException|ArgumentOutOfRangeException|ObjectDisposedException)$"), ThrowH),
        (R(@"^System\.Runtime\.Serialization\."), Serialization),
    ];

    static Regex R(string p) => new(p, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Fam ForTarget(string type, IReadOnlyCollection<string> assemblies, SortedDictionary<string, int> unmatched)
    {
        if (assemblies.Count == 0) return Unclassified;
        if (!assemblies.Contains("System.Private.CoreLib"))
        {
            var a = assemblies.First();
            if (AssemblyFamilies.TryGetValue(a, out var fam)) return fam;
            unmatched["[assembly] " + a] = unmatched.GetValueOrDefault("[assembly] " + a) + 1;
            return Above("above-other", a + ": managed upstream", "NONE");
        }
        foreach (var (rx, fam) in CoreLibRules) if (rx.IsMatch(type)) return fam;
        unmatched[type] = unmatched.GetValueOrDefault(type) + 1;
        return OtherPure;
    }
}

/// <summary>The shared framework's type index (System.Private.CoreLib and every assembly, by metadata name) and its native surface.</summary>
sealed class Framework
{
    readonly Dictionary<string, SortedSet<string>> _types = new(StringComparer.Ordinal);
    readonly Dictionary<string, AssemblyInfo> _assemblies = new(StringComparer.Ordinal);
    public string Version = "";
    /// <summary>System.Private.CoreLib types with runtime entry points: type -> (InternalCall methods, QCalls, libSystem.* imports).</summary>
    public readonly SortedDictionary<string, (int ICalls, int QCalls, int Native)> CoreLibEntryPoints = new(StringComparer.Ordinal);

    public sealed record AssemblyInfo(string Name, int Types, int InternalCalls, int PInvokes, SortedDictionary<string, int> Libraries, int QCalls);
    public IReadOnlyDictionary<string, AssemblyInfo> Assemblies => _assemblies;

    public IReadOnlyCollection<string> AssembliesOf(string type) => _types.TryGetValue(type, out var s) ? s : [];

    public static Framework Load()
    {
        var fw = new Framework();
        var dir = Environment.GetEnvironmentVariable("CSHARPC_FRAMEWORK_DIR") ?? RuntimeEnvironment.GetRuntimeDirectory();
        dir = dir.TrimEnd('/', '\\');
        fw.Version = Path.GetFileName(dir);
        var dirs = new List<string> { dir };
        var asp = Path.Combine(Path.GetDirectoryName(dir)!, "..", "Microsoft.AspNetCore.App", fw.Version);
        if (Directory.Exists(asp)) dirs.Add(Path.GetFullPath(asp));
        foreach (var d in dirs)
            foreach (var dll in Directory.GetFiles(d, "*.dll").Order(StringComparer.Ordinal)) fw.Index(dll);
        return fw;
    }

    void Index(string dll)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(dll));
            if (!pe.HasMetadata) return;
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) return;
            var asm = Path.GetFileNameWithoutExtension(dll);
            string Name(TypeDefinitionHandle h)
            {
                var t = md.GetTypeDefinition(h);
                var n = md.GetString(t.Name);
                var decl = t.GetDeclaringType();
                if (!decl.IsNil) return Name(decl) + "+" + n;
                var ns = md.GetString(t.Namespace);
                return ns.Length == 0 ? n : ns + "." + n;
            }
            int types = 0, icalls = 0, pinvokes = 0, qcalls = 0;
            var libs = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var h in md.TypeDefinitions)
            {
                var n = Name(h);
                if (!_types.TryGetValue(n, out var set)) _types[n] = set = [];
                set.Add(asm);
                types++;
                int ti = 0, tq = 0, tn = 0;
                foreach (var mh in md.GetTypeDefinition(h).GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    if ((m.ImplAttributes & MethodImplAttributes.InternalCall) != 0) { icalls++; ti++; }
                    var import = m.GetImport();
                    if (!import.Module.IsNil)
                    {
                        pinvokes++;
                        var lib = md.GetString(md.GetModuleReference(import.Module).Name);
                        if (lib == "QCall") { qcalls++; tq++; }
                        else if (lib.StartsWith("libSystem.", StringComparison.Ordinal)) tn++;
                        libs[lib] = libs.GetValueOrDefault(lib) + 1;
                    }
                }
                if (asm == "System.Private.CoreLib" && ti + tq + tn > 0) CoreLibEntryPoints[n] = (ti, tq, tn);
            }
            _assemblies[asm] = new AssemblyInfo(asm, types, icalls, pinvokes, libs, qcalls);
        }
        catch (BadImageFormatException) { }
    }

    public void PrintContract()
    {
        Console.WriteLine($"Framework {Version}");
        Console.WriteLine("assembly\ttypes\tinternal_calls\tpinvokes\tqcalls\tnative_libraries");
        foreach (var a in _assemblies.Values.OrderByDescending(a => a.InternalCalls + a.PInvokes).Where(a => a.InternalCalls + a.PInvokes > 0))
            Console.WriteLine($"{a.Name}\t{a.Types}\t{a.InternalCalls}\t{a.PInvokes}\t{a.QCalls}\t{string.Join(",", a.Libraries.Select(kv => kv.Key + ":" + kv.Value))}");
        Console.WriteLine();
        Console.WriteLine("# System.Private.CoreLib types with runtime entry points (InternalCall methods, QCalls, libSystem.* imports): the surface the contract layer stands in for");
        Console.WriteLine("type\tinternal_calls\tqcalls\tlibsystem_imports");
        foreach (var (t, c) in CoreLibEntryPoints.OrderByDescending(kv => kv.Value.ICalls + kv.Value.QCalls + kv.Value.Native).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine($"{t}\t{c.ICalls}\t{c.QCalls}\t{c.Native}");
    }
}

static class Summary
{
    static readonly string[] Classes = ["CORE-CONTRACT", "CORELIB-PURE", "FRAMEWORK-ABOVE", "GLUE", "DATA"];

    static readonly string[] TierName =
    [
        "",
        "ready once the IL front-end and the SIMD/comparer mapping hold (no blocker beyond SIMD, EQCMP, GENMATH)",
        "a library decision by A/B (Json, Regex: #187/#188/#189) or a layout question (Memory<T>, AppContext switches)",
        "needs members of the contract layer (threads, tasks, GC, reflection, exceptions, layout; ICU: the shim is linked already, the culture/thread contract is the work)",
        "needs upstream's native shims or a replacement (libSystem.Native, OpenSSL, console, EventPipe)",
    ];

    /// <summary>1 = ready, 2 = library A/B, 3 = contract members, 4 = native shims; the heaviest blocker decides.</summary>
    static int Tier(Row r)
    {
        var b = r.Blockers.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (b.Contains("AB187")) return 2;
        if (b.Any(x => x is "SYSNATIVE" or "OPENSSL" or "CONSOLE" or "EVENTPIPE")) return 4;
        if (b.Any(x => x is "THREAD" or "TASK" or "GC" or "REFLECT" or "EXC" or "LAYOUT" or "ICU")) return 3;
        if (b.Any(x => x is "SRCGEN" or "NEWTYPES" or "MEMORY" or "APPCTX")) return 2;
        return 1;
    }

    public static void Print(List<Row> rows, Framework fw)
    {
        int total = rows.Sum(r => r.Lines);
        Console.WriteLine($"Framework {fw.Version}; {rows.Count} files, {total:N0} lines, {rows.Sum(r => r.Bytes) / 1024:N0} KB.");
        Console.WriteLine();
        Console.WriteLine("### Lines by class (attributed per shadow declaration)");
        Console.WriteLine();
        Console.WriteLine("| Class | Lines | % | Files (by dominant class) | Of which ported from upstream (files) |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        foreach (var c in Classes)
        {
            double lines = rows.Sum(r => r.Split.GetValueOrDefault(c));
            var fs = rows.Where(r => r.Class == c).ToList();
            Console.WriteLine($"| {c} | {lines:N0} | {100 * lines / total:F1} | {fs.Count} | {fs.Count(r => r.Origin == "ported")} |");
        }
        Console.WriteLine();
        Console.WriteLine("### Lines by verdict (file level)");
        Console.WriteLine();
        Console.WriteLine("| Verdict | Files | Lines |");
        Console.WriteLine("|---|---:|---:|");
        foreach (var g in rows.GroupBy(r => r.Verdict).OrderBy(g => g.Key))
            Console.WriteLine($"| {g.Key} | {g.Count()} | {g.Sum(r => r.Lines):N0} |");
        Console.WriteLine();
        Console.WriteLine("### Class by area (lines; area = top-level directory, or `(root)`)");
        Console.WriteLine();
        Console.WriteLine("| Area | Lines | CONTRACT | PURE | ABOVE | GLUE | DATA |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var g in rows.GroupBy(r => r.Path.Contains('/') ? r.Path[..r.Path.IndexOf('/')] : "(root)").OrderByDescending(g => g.Sum(r => r.Lines)))
            Console.WriteLine($"| {g.Key} | {g.Sum(r => r.Lines):N0} | " + string.Join(" | ", Classes.Select(c => $"{g.Sum(r => r.Split.GetValueOrDefault(c)):N0}")) + " |");
        Console.WriteLine();

        Console.WriteLine("### Readiness of the deletable files (verdict delete-when-IL-wins; tier = the heaviest blocker of the file)");
        Console.WriteLine();
        Console.WriteLine("| Tier | Meaning | Files | Lines |");
        Console.WriteLine("|---|---|---:|---:|");
        foreach (var t in new[] { 1, 2, 3, 4 })
        {
            var fs = rows.Where(r => r.Verdict == "delete-when-IL-wins" && Tier(r) == t).ToList();
            Console.WriteLine($"| {t} | {TierName[t]} | {fs.Count} | {fs.Sum(r => r.Lines):N0} |");
        }
        Console.WriteLine();

        var ready = new HashSet<string> { "SIMD", "EQCMP", "GENMATH" };
        Console.WriteLine("### First 20 files to delete (verdict delete-when-IL-wins, tier 1: no blocker beyond SIMD, EQCMP, GENMATH; by lines)");
        Console.WriteLine();
        Console.WriteLine("| File | Lines | Class | Family | Blockers |");
        Console.WriteLine("|---|---:|---|---|---|");
        foreach (var r in rows.Where(r => r.Verdict == "delete-when-IL-wins" && r.Blockers.Split(',', StringSplitOptions.RemoveEmptyEntries).All(ready.Contains)).OrderByDescending(r => r.Lines).Take(20))
            Console.WriteLine($"| {r.Path} | {r.Lines:N0} | {r.Class} | {r.Family} | {(r.Blockers.Length == 0 ? "-" : r.Blockers)} |");
        Console.WriteLine();

        Console.WriteLine("### Blockers by lines they gate (delete-when-IL-wins and shrink files; a file counts once per blocker)");
        Console.WriteLine();
        Console.WriteLine("| Blocker | Files | Lines |");
        Console.WriteLine("|---|---:|---:|");
        var gated = rows.Where(r => r.Verdict != "keep").SelectMany(r => r.Blockers.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(b => (b, r)));
        foreach (var g in gated.GroupBy(x => x.b).OrderByDescending(g => g.Sum(x => x.r.Lines)))
            Console.WriteLine($"| {g.Key} | {g.Count()} | {g.Sum(x => x.r.Lines):N0} |");
        Console.WriteLine();

        Console.WriteLine("### Largest 20 files whose verdict is keep or shrink");
        Console.WriteLine();
        Console.WriteLine("| File | Lines | Class | Verdict | Family |");
        Console.WriteLine("|---|---:|---|---|---|");
        foreach (var r in rows.Where(r => r.Verdict != "delete-when-IL-wins").OrderByDescending(r => r.Lines).Take(20))
            Console.WriteLine($"| {r.Path} | {r.Lines:N0} | {r.Class} | {r.Verdict} | {r.Family} |");
        Console.WriteLine();
        Console.WriteLine($"Data heuristic: {rows.Sum(r => r.Data):N0} lines of literal tables or lines over 300 characters; {rows.Count(r => r.Origin == "ported")} of {rows.Count} files name dotnet/runtime in their header.");
    }
}
