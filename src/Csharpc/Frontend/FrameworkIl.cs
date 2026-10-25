using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Csharpc.Frontend;

/// <summary>
/// Which framework assemblies are compiled from the shared framework's IL instead of being served by
/// the corelib (docs/adr/0013-framework-il.md). By default an assembly of Microsoft.NETCore.App is taken
/// as IL when
///   - the program *uses* it: the sources name one of its namespaces and a public type of it (or one of
///     its extension methods, when no other framework assembly shares the namespace), or an assembly
///     compiled from IL references it (transitively), and
///   - it is *eligible*: it is not System.Private.CoreLib or a facade of what the corelib serves, csharpc
///     implements none of its types (no corelib shadow, no intrinsic), and it calls no native code of the
///     runtime (QCalls, libSystem.*.Native) other than the shims csharpc links statically from the
///     NativeAOT runtime pack, as NativeAOT does (NativeAotPack: libSystem.IO.Compression.Native, ...).
/// `--framework-il A,B` forces eligible assemblies in, and `--framework-il none` turns the automatic
/// choice off. The result joins the IL references like a `-r`: Roslyn binds against the implementation
/// assembly instead of its reference-pack twin, and reachable methods are lowered from its IL. Facades of
/// a chosen System.Private.* library (System.Xml.ReaderWriter for System.Private.Xml) come along, so the
/// types the program binds to are the implementation's.
/// </summary>
public static partial class FrameworkIl
{
    /// <param name="NativeModules">The .NET shims its IL calls (NativeAotPack.Shims), linked from the NativeAOT runtime pack.</param>
    public sealed record Choice(string Name, string Path, string Reason, IReadOnlyList<string> NativeModules);

    /// <summary>One framework assembly as the index records it.</summary>
    private sealed class Assembly
    {
        public required string Name { get; init; }
        /// <summary>The shared-framework directory it is in (not part of the cached index).</summary>
        public string Directory { get; set; } = "";
        /// <summary>Why it can't be compiled from IL; null if it can.</summary>
        public string? Ineligible { get; set; }
        public string[] References { get; set; } = [];
        /// <summary>Assemblies it forwards types to (a facade such as System.Xml.ReaderWriter forwards everything).</summary>
        public string[] ForwardsTo { get; set; } = [];
        /// <summary>.NET shims it P/Invokes (NativeAotPack.Shims): eligible where their archives can be linked.</summary>
        public string[] NativeModules { get; set; } = [];
        /// <summary>Namespace → public top-level type names (no arity).</summary>
        public Dictionary<string, string[]> Types { get; } = new(StringComparer.Ordinal);
        /// <summary>Namespace → public methods of its public static classes (extension methods are used without naming their class).</summary>
        public Dictionary<string, string[]> Extensions { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Shared frameworks a project can reference besides Microsoft.NETCore.App.</summary>
    public static readonly string[] SupportedFrameworks = ["Microsoft.AspNetCore.App"];

    /// <summary>
    /// Framework assemblies to compile from IL for these sources. <paramref name="forced"/> are names from
    /// --framework-il; <paramref name="automatic"/> = false is `--framework-il none`. Assemblies already
    /// among the IL references (<paramref name="ilPaths"/>) are not repeated.
    /// </summary>
    public static List<Choice> Select(FrontendSettings settings, IReadOnlyList<string> ilPaths, IReadOnlyList<string> forced, bool automatic, string triple)
    {
        var result = new List<Choice>();
        foreach (var framework in settings.SharedFrameworks)
            if (!SupportedFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"the framework reference '{framework}' is not supported by csharpc yet (supported: Microsoft.NETCore.App, {string.Join(", ", SupportedFrameworks)})");
        if (!automatic && forced.Count == 0) return result;
        if (SharedFramework.Directory is not { } dir)
        {
            if (forced.Count > 0 || settings.SharedFrameworks.Count > 0) throw new InvalidOperationException("--framework-il: the .NET 11 shared framework (Microsoft.NETCore.App) was not found");
            return result;
        }
        var index = Index(dir);
        // Other shared frameworks (ASP.NET Core) have no corelib counterpart: everything of theirs the
        // program uses comes from their IL, with Microsoft.NETCore.App underneath.
        foreach (var framework in settings.SharedFrameworks)
        {
            var frameworkDir = SharedFramework.DirectoryOf(framework)
                ?? throw new InvalidOperationException($"the shared framework {framework} (version 11) was not found next to {dir}; install the matching runtime");
            foreach (var (name, a) in Index(frameworkDir)) index.TryAdd(name, a);
        }
        var given = new HashSet<string>(ilPaths.Select(p => Path.GetFileNameWithoutExtension(p)), StringComparer.OrdinalIgnoreCase);
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(Assembly a, string reason)
        {
            if (given.Contains(a.Name) || !chosen.Add(a.Name)) return;
            result.Add(new Choice(a.Name, Path.Combine(a.Directory, a.Name + ".dll"), reason, a.NativeModules));
        }

        // An assembly that calls .NET's native shims is usable where their static archives can be linked
        // (checked, and the NativeAOT pack restored, only for assemblies the program would take).
        string? Unusable(Assembly a)
        {
            if (a.Ineligible is { } why) return why;
            if (a.NativeModules.Length == 0) return null;
            if (NativeAotPack.Rid(triple) is not { } rid)
                return $"it calls .NET's native {string.Join(", ", a.NativeModules)}, which csharpc can't link for {triple} yet";
            if (NativeAotPack.NativeDirectory(rid, out var reason) is not { } native)
                return $"it calls .NET's native {string.Join(", ", a.NativeModules)}, and {reason}";
            return a.NativeModules.FirstOrDefault(m => !NativeAotPack.HasShim(native, m)) is { } missing
                ? $"it calls .NET's native {missing}, which the NativeAOT runtime pack for {rid} doesn't ship as a static library" : null;
        }

        foreach (var name in forced)
        {
            if (!index.TryGetValue(name, out var a))
                throw new InvalidOperationException($"--framework-il: '{name}' is not an assembly of the shared framework ({dir})");
            if (Unusable(a) is { } why) throw new InvalidOperationException($"--framework-il: '{name}' can't be compiled from IL: {why}");
            Add(a, "--framework-il");
        }
        if (!automatic) return result;

        // Uses: a namespace named by the sources (every segment is one of their identifiers) and one of its types.
        var identifiers = SourceIdentifiers(settings);
        bool Named(string ns) => ns.Split('.').All(identifiers.Contains);
        var owners = index.Values.SelectMany(a => a.Types.Keys).GroupBy(n => n, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        // In a namespace several assemblies share (System.Linq), an extension method only one of them defines
        // still names it: AsParallel is System.Linq.Parallel's, while Select is everybody's.
        var methodOwners = index.Values.SelectMany(a => a.Extensions.SelectMany(n => n.Value.Distinct(StringComparer.Ordinal).Select(m => n.Key + "." + m)))
            .GroupBy(m => m, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var a in index.Values.Where(a => a.Ineligible is null).OrderBy(a => a.Name, StringComparer.Ordinal))
        {
            var use = a.Types.Where(n => Named(n.Key)).SelectMany(n => n.Value.Where(identifiers.Contains).Select(t => n.Key + "." + t))
                .Concat(a.Extensions.Where(n => Named(n.Key))
                    .SelectMany(n => n.Value.Where(m => identifiers.Contains(m)
                            && (owners.GetValueOrDefault(n.Key) == 1 || methodOwners.GetValueOrDefault(n.Key + "." + m) == 1))
                        .Select(m => n.Key + "." + m + "()")))
                .OrderBy(u => u, StringComparer.Ordinal).FirstOrDefault();
            if (use is not null && Unusable(a) is null) Add(a, $"the program uses {use}");
        }

        // Closure: what the IL assemblies (the program's and the chosen framework ones) reference.
        var queue = new Queue<(string Name, string[] References)>();
        foreach (var path in ilPaths.Where(File.Exists)) queue.Enqueue((Path.GetFileNameWithoutExtension(path), ReferencesOf(path)));
        foreach (var choice in result.ToList()) queue.Enqueue((choice.Name, index[choice.Name].References));
        while (queue.Count > 0)
        {
            var (from, references) = queue.Dequeue();
            foreach (var reference in references)
            {
                if (chosen.Contains(reference) || given.Contains(reference) || !index.TryGetValue(reference, out var a) || Unusable(a) is not null) continue;
                Add(a, $"referenced by {from}");
                queue.Enqueue((a.Name, a.References));
            }
        }

        // Facades of a chosen assembly (System.Xml.ReaderWriter, System.Xml.XDocument for System.Private.Xml and
        // System.Private.Xml.Linq): their reference-pack twins define the types the program binds to, which
        // the implementation defines again; taking the facade instead makes Roslyn follow its forwards.
        foreach (var a in index.Values.Where(a => a.Ineligible is null && a.ForwardsTo.Length > 0).OrderBy(a => a.Name, StringComparer.Ordinal))
            if (a.ForwardsTo.FirstOrDefault(t => t.StartsWith("System.Private.", StringComparison.Ordinal) && chosen.Contains(t)) is { } target)
                Add(a, $"a facade of {target}");
        return result;
    }

    private static string[] ReferencesOf(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var md = pe.GetMetadataReader();
        return [.. md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name))];
    }

    /// <summary>Identifiers of the program's sources (and global usings), comments and strings included: a superset of the names it binds to.</summary>
    private static HashSet<string> SourceIdentifiers(FrontendSettings settings)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        void Scan(string text)
        {
            for (var i = 0; i < text.Length;)
            {
                if (char.IsLetter(text[i]) || text[i] == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    identifiers.Add(text[start..i]);
                }
                else i++;
            }
        }
        foreach (var file in settings.Files.Concat(settings.Projects.SelectMany(p => p.Files)).Distinct(StringComparer.Ordinal))
            if (File.Exists(file)) Scan(File.ReadAllText(file));
        if (settings.GlobalUsings is { } usings) Scan(usings);
        foreach (var project in settings.Projects)
            if (project.GlobalUsings is { } projectUsings) Scan(projectUsings);
        return identifiers;
    }

    // ---- the index ---------------------------------------------------------------------------

    /// <summary>
    /// Every assembly of the shared framework with its eligibility, references, namespaces, types and
    /// extension methods. Building it reads ~170 metadata tables (~100 ms), so it is cached in the csharpc
    /// cache directory, keyed on the framework directory (whose contents never change) and on this build
    /// of csharpc (eligibility depends on what the corelib implements).
    /// </summary>
    private static Dictionary<string, Assembly> Index(string dir)
    {
        var index = LoadIndex(dir);
        foreach (var a in index.Values) a.Directory = dir;
        return index;
    }

    private static Dictionary<string, Assembly> LoadIndex(string dir)
    {
        var cacheRoot = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        var build = typeof(FrameworkIl).Assembly.ManifestModule.ModuleVersionId;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{dir}|{build}|4")))[..16];
        // Named by build, then by framework directory: an index per framework (Microsoft.NETCore.App's and ASP.NET Core's).
        var buildTag = build.ToString("N")[..8];
        var cache = Path.Combine(cacheRoot, "csharpc", $"framework-il-{buildTag}-{key}.txt");
        if (File.Exists(cache) && ReadIndex(cache) is { Count: > 0 } cached) return cached;

        var index = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(dir, "*.dll").Order(StringComparer.Ordinal))
        {
            try
            {
                using var pe = new PEReader(File.OpenRead(path));
                if (!pe.HasMetadata) continue;
                var md = pe.GetMetadataReader();
                if (!md.IsAssembly) continue;
                var a = Read(md);
                index[a.Name] = a;
            }
            catch (BadImageFormatException) { }
        }
        MarkFacades(index);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            var temp = cache + "." + Environment.ProcessId + ".tmp";
            File.WriteAllLines(temp, index.Values.OrderBy(a => a.Name, StringComparer.Ordinal).SelectMany(WriteAssembly));
            File.Move(temp, cache, overwrite: true);
            // Indexes of earlier csharpc builds are dead weight.
            foreach (var stale in Directory.EnumerateFiles(Path.GetDirectoryName(cache)!, "framework-il-*.txt"))
                if (!Path.GetFileName(stale).StartsWith($"framework-il-{buildTag}-", StringComparison.Ordinal)) File.Delete(stale);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return index;
    }

    private static Assembly Read(MetadataReader md)
    {
        var a = new Assembly { Name = md.GetString(md.GetAssemblyDefinition().Name) };
        a.References = [.. md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name))];
        a.ForwardsTo = [.. md.ExportedTypes.Select(h => md.GetExportedType(h).Implementation)
            .Where(i => i.Kind == HandleKind.AssemblyReference)
            .Select(i => md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)i).Name)).Distinct(StringComparer.Ordinal)];
        var types = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var extensions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var fullNames = new List<string>();
        foreach (var th in md.TypeDefinitions)
        {
            var t = md.GetTypeDefinition(th);
            if (!t.GetDeclaringType().IsNil || (t.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public) continue;
            var ns = md.GetString(t.Namespace);
            var name = md.GetString(t.Name);
            var simple = name.IndexOf('`') is var tick and >= 0 ? name[..tick] : name;
            fullNames.Add(ns.Length == 0 ? simple : ns + "." + simple);
            if (ns.Length == 0) continue;
            if (!types.TryGetValue(ns, out var list)) types[ns] = list = [];
            list.Add(simple);
            if ((t.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) != (TypeAttributes.Abstract | TypeAttributes.Sealed)) continue;
            if (!extensions.TryGetValue(ns, out var methods)) extensions[ns] = methods = [];
            foreach (var mh in t.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if ((m.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public) methods.Add(md.GetString(m.Name));
            }
        }
        foreach (var (ns, list) in types) a.Types[ns] = [.. list.Distinct(StringComparer.Ordinal)];
        foreach (var (ns, list) in extensions) a.Extensions[ns] = [.. list.Distinct(StringComparer.Ordinal)];

        var known = KnownTypes();
        // Native code of the .NET runtime it calls: the shims csharpc links from the NativeAOT pack are
        // recorded (eligible where that works), any other (QCalls, libSystem.Native ...) rules it out.
        var shims = new List<string>();
        string? ModuleOfRuntime()
        {
            for (var i = 1; i <= md.GetTableRowCount(TableIndex.ModuleRef); i++)
            {
                var module = md.GetString(md.GetModuleReference(MetadataTokens.ModuleReferenceHandle(i)).Name);
                if (!(module.StartsWith("libSystem.", StringComparison.Ordinal) || module.StartsWith("System.", StringComparison.Ordinal) || module == "QCall")) continue;
                if (!NativeAotPack.MayCall(a.Name, module)) return module;
                shims.Add(module);
            }
            return null;
        }
        // System.Private.CoreLib is the runtime's own code (FCalls, intrinsics, the type system); the other
        // System.Private.* assemblies (Xml, Xml.Linq, DataContractSerialization, Uri) are ordinary libraries
        // behind facades and get the same checks as any other assembly.
        a.Ineligible = a.Name.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase) ? "it is part of the runtime itself"
            : ModuleOfRuntime() is { } native ? $"it calls native code of the .NET runtime ({native})"
            : fullNames.FirstOrDefault(known.Contains) is { } shadowed ? $"csharpc implements its type '{shadowed}' itself"
            : null;
        a.NativeModules = [.. shims.Distinct(StringComparer.Ordinal)];
        return a;
    }

    /// <summary>
    /// Facades: an assembly that forwards types into System.Private.* can only be compiled from IL when the
    /// target can (System.Xml.ReaderWriter → System.Private.Xml); System.Runtime, System.Collections ...
    /// are facades of CoreLib, which the corelib serves. Forwards to other assemblies are harmless — Roslyn
    /// follows them to a reference assembly or another assembly compiled from IL: System.Diagnostics.DiagnosticSource
    /// forwards only IsExternalInit to System.Runtime, ASP.NET Core's assemblies forward types they once
    /// defined to their neighbours (FeatureCollection to Microsoft.Extensions.Features).
    /// </summary>
    private static void MarkFacades(Dictionary<string, Assembly> index)
    {
        foreach (var a in index.Values)
        {
            if (a.Ineligible is not null) continue;
            var blocked = a.ForwardsTo.FirstOrDefault(t => t.StartsWith("System.Private.", StringComparison.Ordinal)
                && (!index.TryGetValue(t, out var target) || target.Ineligible is not null));
            if (blocked is not null) a.Ineligible = $"it forwards types to {blocked} (a facade)";
        }
    }

    // Lines: "A\tname\treason-or-empty\treferences\tforwarded-to\tnative-shims", then "T\tnamespace\ttypes" / "E\tnamespace\tmethods" for it.
    private static IEnumerable<string> WriteAssembly(Assembly a)
    {
        yield return $"A\t{a.Name}\t{a.Ineligible}\t{string.Join(' ', a.References)}\t{string.Join(' ', a.ForwardsTo)}\t{string.Join(' ', a.NativeModules)}";
        foreach (var (ns, names) in a.Types) yield return $"T\t{ns}\t{string.Join(' ', names)}";
        foreach (var (ns, names) in a.Extensions) yield return $"E\t{ns}\t{string.Join(' ', names)}";
    }

    private static Dictionary<string, Assembly>? ReadIndex(string path)
    {
        var index = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        Assembly? current = null;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split('\t');
                if (parts is ["A", var name, var reason, var references, var forwards, var natives])
                {
                    current = new Assembly
                    {
                        Name = name, Ineligible = reason.Length == 0 ? null : reason,
                        References = references.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                        ForwardsTo = forwards.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                        NativeModules = natives.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    };
                    index[name] = current;
                }
                else if (current is not null && parts is ["T" or "E", var ns, var names])
                    (parts[0] == "T" ? current.Types : current.Extensions)[ns] = names.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                else return null;
            }
        }
        catch (IOException) { return null; }
        return index;
    }

    private static HashSet<string>? s_known;

    /// <summary>
    /// Top-level library types csharpc implements itself: corelib shadows (`[Shadows]`/`[ShadowMembers]`
    /// in the shipped corelib sources) and the types of hand-written intrinsics.
    /// </summary>
    private static HashSet<string> KnownTypes()
    {
        if (s_known is { } cached) return cached;
        var known = new HashSet<string>(StringComparer.Ordinal);
        var corelib = Path.Combine(AppContext.BaseDirectory, "corelib");
        if (Directory.Exists(corelib))
            foreach (var file in Directory.EnumerateFiles(corelib, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in ShadowAttribute().Matches(File.ReadAllText(file)))
                {
                    var top = m.Groups[1].Value.Split('+')[0];
                    known.Add(top.IndexOf('`') is var tick and >= 0 ? top[..tick] : top);
                }
        known.UnionWith(CodeGen.FunctionCodeGen.TypesLoweredByName);
        // Intrinsic keys are display strings ("System.Console.WriteLine(int)", "System.Array.Length"): every
        // dotted prefix before the parameter list may be a type.
        foreach (var key in CodeGen.Intrinsics.All.Concat(CodeGen.BclValueTypes.All))
        {
            var head = key.IndexOf('(') is var paren and >= 0 ? key[..paren] : key;
            while (head.Contains('<')) head = GenericArguments().Replace(head, "");
            for (var i = head.IndexOf('.'); i >= 0; i = head.IndexOf('.', i + 1)) known.Add(head[..i]);
        }
        return s_known = known;
    }

    [GeneratedRegex(@"\[(?:Shadows|ShadowMembers)\(""([^""]+)""\)\]")]
    private static partial Regex ShadowAttribute();

    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex GenericArguments();
}
