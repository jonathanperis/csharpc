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
///   - it is *eligible*: it has code of its own (not System.Private.*, no type forwarders), csharpc
///     implements none of its types (no corelib shadow, no intrinsic), and it calls no native shim of
///     the runtime (libSystem.*.Native, QCalls).
/// `--framework-il A,B` forces eligible assemblies in, and `--framework-il none` turns the automatic
/// choice off. The result joins the IL references like a `-r`: Roslyn binds against the implementation
/// assembly instead of its reference-pack twin, and reachable methods are lowered from its IL.
/// </summary>
public static partial class FrameworkIl
{
    public sealed record Choice(string Name, string Path, string Reason);

    /// <summary>One framework assembly as the index records it.</summary>
    private sealed class Assembly
    {
        public required string Name { get; init; }
        /// <summary>Why it can't be compiled from IL; null if it can.</summary>
        public string? Ineligible { get; set; }
        public string[] References { get; set; } = [];
        /// <summary>Namespace → public top-level type names (no arity).</summary>
        public Dictionary<string, string[]> Types { get; } = new(StringComparer.Ordinal);
        /// <summary>Namespace → public methods of its public static classes (extension methods are used without naming their class).</summary>
        public Dictionary<string, string[]> Extensions { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Framework assemblies to compile from IL for these sources. <paramref name="forced"/> are names from
    /// --framework-il; <paramref name="automatic"/> = false is `--framework-il none`. Assemblies already
    /// among the IL references (<paramref name="ilPaths"/>) are not repeated.
    /// </summary>
    public static List<Choice> Select(FrontendSettings settings, IReadOnlyList<string> ilPaths, IReadOnlyList<string> forced, bool automatic)
    {
        var result = new List<Choice>();
        if (!automatic && forced.Count == 0) return result;
        if (SharedFramework.Directory is not { } dir)
        {
            if (forced.Count > 0) throw new InvalidOperationException("--framework-il: the .NET 11 shared framework (Microsoft.NETCore.App) was not found");
            return result;
        }
        var index = Index(dir);
        var given = new HashSet<string>(ilPaths.Select(p => Path.GetFileNameWithoutExtension(p)), StringComparer.OrdinalIgnoreCase);
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(Assembly a, string reason)
        {
            if (given.Contains(a.Name) || !chosen.Add(a.Name)) return;
            result.Add(new Choice(a.Name, Path.Combine(dir, a.Name + ".dll"), reason));
        }

        foreach (var name in forced)
        {
            if (!index.TryGetValue(name, out var a))
                throw new InvalidOperationException($"--framework-il: '{name}' is not an assembly of the shared framework ({dir})");
            if (a.Ineligible is { } why) throw new InvalidOperationException($"--framework-il: '{name}' can't be compiled from IL: {why}");
            Add(a, "--framework-il");
        }
        if (!automatic) return result;

        // Uses: a namespace named by the sources (every segment is one of their identifiers) and one of its types.
        var identifiers = SourceIdentifiers(settings);
        bool Named(string ns) => ns.Split('.').All(identifiers.Contains);
        var owners = index.Values.SelectMany(a => a.Types.Keys).GroupBy(n => n, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var a in index.Values.Where(a => a.Ineligible is null).OrderBy(a => a.Name, StringComparer.Ordinal))
        {
            var use = a.Types.Where(n => Named(n.Key)).SelectMany(n => n.Value.Where(identifiers.Contains).Select(t => n.Key + "." + t))
                .Concat(a.Extensions.Where(n => owners.GetValueOrDefault(n.Key) == 1 && Named(n.Key))
                    .SelectMany(n => n.Value.Where(identifiers.Contains).Select(m => n.Key + "." + m + "()")))
                .OrderBy(u => u, StringComparer.Ordinal).FirstOrDefault();
            if (use is not null) Add(a, $"the program uses {use}");
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
                if (chosen.Contains(reference) || given.Contains(reference) || !index.TryGetValue(reference, out var a) || a.Ineligible is not null) continue;
                Add(a, $"referenced by {from}");
                queue.Enqueue((a.Name, a.References));
            }
        }
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
        var cacheRoot = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        var build = typeof(FrameworkIl).Assembly.ManifestModule.ModuleVersionId;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{dir}|{build}|1")))[..16];
        var cache = Path.Combine(cacheRoot, "csharpc", $"framework-il-{key}.txt");
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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            var temp = cache + "." + Environment.ProcessId + ".tmp";
            File.WriteAllLines(temp, index.Values.OrderBy(a => a.Name, StringComparer.Ordinal).SelectMany(WriteAssembly));
            File.Move(temp, cache, overwrite: true);
            // Indexes of earlier csharpc builds are dead weight.
            foreach (var stale in Directory.EnumerateFiles(Path.GetDirectoryName(cache)!, "framework-il-*.txt"))
                if (!stale.Equals(cache, StringComparison.Ordinal)) File.Delete(stale);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return index;
    }

    private static Assembly Read(MetadataReader md)
    {
        var a = new Assembly { Name = md.GetString(md.GetAssemblyDefinition().Name) };
        a.References = [.. md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name))];
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
        string? ModuleOfRuntime()
        {
            for (var i = 1; i <= md.GetTableRowCount(TableIndex.ModuleRef); i++)
            {
                var module = md.GetString(md.GetModuleReference(MetadataTokens.ModuleReferenceHandle(i)).Name);
                if (module.StartsWith("libSystem.", StringComparison.Ordinal) || module.StartsWith("System.", StringComparison.Ordinal) || module == "QCall") return module;
            }
            return null;
        }
        a.Ineligible = a.Name.StartsWith("System.Private.", StringComparison.OrdinalIgnoreCase) ? "it is part of the runtime itself"
            : md.ExportedTypes.Count > 0 ? "it forwards types to other assemblies (a facade)"
            : ModuleOfRuntime() is { } native ? $"it calls native code of the .NET runtime ({native})"
            : fullNames.FirstOrDefault(known.Contains) is { } shadowed ? $"csharpc implements its type '{shadowed}' itself"
            : null;
        return a;
    }

    // Lines: "A\tname\treason-or-empty\treferences", then "T\tnamespace\ttypes" / "E\tnamespace\tmethods" for it.
    private static IEnumerable<string> WriteAssembly(Assembly a)
    {
        yield return $"A\t{a.Name}\t{a.Ineligible}\t{string.Join(' ', a.References)}";
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
                if (parts is ["A", var name, var reason, var references])
                {
                    current = new Assembly { Name = name, Ineligible = reason.Length == 0 ? null : reason, References = references.Split(' ', StringSplitOptions.RemoveEmptyEntries) };
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
