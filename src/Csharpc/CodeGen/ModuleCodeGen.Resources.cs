using System.Text;
using Csharpc.Llvm;

namespace Csharpc.CodeGen;

/// <summary>
/// Data the program reads at startup that .NET takes from files next to the app: string resources
/// embedded in the IL assemblies (ResourceManager, SR.GetResourceString) and runtimeconfig.json's
/// configProperties (AppContext). Both are compiled in as one string constant each and decoded by
/// the corelib on first use (corelib/Resources.cs).
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>runtimeconfig.json configProperties (FrontendSettings.RuntimeConfig).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> RuntimeConfig { get; init; } = [];

    private string? _resourceBlob;

    /// <summary>(base name, key, value) triples of every IL assembly's string resources.</summary>
    internal LLVMValueRef ResourceStringsBlob()
    {
        _resourceBlob ??= Encode(Il.Assemblies.SelectMany(a => a.ReadStringResources()).SelectMany(e => new[] { e.BaseName, e.Key, e.Value }));
        return StringLiteral(_resourceBlob);
    }

    /// <summary>
    /// Full names of the assemblies Assembly.Load can return (corelib/Reflection/Assembly.cs): the program's
    /// own and the IL assemblies, whose code is compiled in.
    /// </summary>
    internal LLVMValueRef LoadableAssembliesBlob()
    {
        var names = new List<string>();
        foreach (var unit in ProgramAssemblies)
        {
            var name = unit.IsRoot ? Compilation.AssemblyName ?? unit.Name : unit.Name;
            var version = unit.Version == "source" ? (unit.IsRoot ? Compilation.Assembly.Identity.Version.ToString() : null) : unit.Version ?? "1.0.0.0";
            if (version is not null) names.Add($"{name}, Version={version}, Culture=neutral, PublicKeyToken=null");
        }
        if (ProgramAssemblies.Count == 0 && ProgramAssemblyName(null) is { } single) names.Add(single);
        foreach (var assembly in Il.Assemblies)
            if (!assembly.IsCoreLib) names.Add(assembly.Symbol.Identity.GetDisplayName());
        return StringLiteral(Encode(names.Distinct()));
    }

    private string? _manifestIndex;
    private LLVMValueRef _manifestData;

    /// <summary>
    /// Assembly.GetManifestResourceStream / GetManifestResourceNames (corelib/Reflection/Assembly.cs): the IL
    /// assemblies' embedded resources, as (assembly full name, resource name, offset, length) quadruples
    /// into one read-only data global. Built only when the program reaches those members, so programs that
    /// don't read resources carry none of the bytes.
    /// </summary>
    internal LLVMValueRef ManifestResourceIndex()
    {
        BuildManifestResources();
        return StringLiteral(_manifestIndex!);
    }

    internal LLVMValueRef ManifestResourceData()
    {
        BuildManifestResources();
        return _manifestData;
    }

    private void BuildManifestResources()
    {
        if (_manifestIndex is not null) return;
        var fields = new List<string>();
        var data = new MemoryStream();
        foreach (var assembly in Il.Assemblies)
        {
            if (assembly.IsCoreLib) continue;
            var fullName = assembly.Symbol.Identity.GetDisplayName();
            foreach (var (name, bytes) in assembly.ReadManifestResources())
            {
                while (data.Length % 8 != 0) data.WriteByte(0); // each resource 8-byte aligned, as the PE resource directory is
                fields.AddRange([fullName, name, data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                data.Write(bytes);
            }
        }
        if (data.Length == 0) data.WriteByte(0);
        var init = Llvm.ConstBytes(data.ToArray());
        _manifestData = Module.AddGlobal(".manifest_resources", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        Native.LLVMSetAlignment(_manifestData, 8);
        _manifestIndex = Encode(fields);
    }

    private string? _moduleIds;

    /// <summary>
    /// (assembly full name, module name, MVID) of every IL assembly (Assembly.ManifestModule, corelib/Reflection/Assembly.cs):
    /// emitted only when the program reaches ManifestModule.
    /// </summary>
    internal LLVMValueRef ModuleVersionIdsBlob()
    {
        if (_moduleIds is null)
        {
            var fields = new List<string>();
            foreach (var assembly in Il.Assemblies)
            {
                if (assembly.IsCoreLib) continue;
                var module = assembly.Reader.GetModuleDefinition();
                fields.AddRange([assembly.Symbol.Identity.GetDisplayName(), assembly.Reader.GetString(module.Name),
                    assembly.Reader.GetGuid(module.Mvid).ToString("D")]);
            }
            _moduleIds = Encode(fields);
        }
        return StringLiteral(_moduleIds);
    }

    private string? _assemblyReferences;

    /// <summary>
    /// (assembly full name, number of references, their full names...) of every IL assembly, from its AssemblyRef
    /// table in metadata order (Assembly.GetReferencedAssemblies): emitted only when the program reaches it.
    /// </summary>
    internal LLVMValueRef AssemblyReferencesBlob()
    {
        if (_assemblyReferences is null)
        {
            var fields = new List<string>();
            foreach (var assembly in Il.Assemblies)
            {
                if (assembly.IsCoreLib) continue;
                var reader = assembly.Reader;
                var names = reader.AssemblyReferences.Select(h => reader.GetAssemblyReference(h).GetAssemblyName().FullName).ToList();
                fields.Add(assembly.Symbol.Identity.GetDisplayName());
                fields.Add(names.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                fields.AddRange(names);
            }
            _assemblyReferences = Encode(fields);
        }
        return StringLiteral(_assemblyReferences);
    }

    /// <summary>(name, value) pairs of the runtime configuration.</summary>
    internal LLVMValueRef AppContextBlob() => StringLiteral(Encode(RuntimeConfig.SelectMany(kv => new[] { kv.Key, kv.Value })));

    /// <summary>Fields as [length high 16 bits][length low 16 bits][chars] (CsharpcCoreLib.Blob decodes it).</summary>
    private static string Encode(IEnumerable<string> fields)
    {
        var sb = new StringBuilder();
        foreach (var f in fields)
        {
            sb.Append((char)(f.Length >> 16)).Append((char)(f.Length & 0xFFFF)).Append(f);
        }
        return sb.ToString();
    }
}
