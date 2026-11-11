using System.Xml.Linq;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// The ILLink.Substitutions.xml an IL assembly embeds, applied as NativeAOT's ILCompiler applies it under the
/// program's feature switches (runtimeconfig configProperties, which carry the project's
/// RuntimeHostConfigurationOption items): parameterless bool methods stubbed with a value fold to it, and
/// resources marked `action="remove"` are not compiled in. With UseSystemResourceKeys=true, every runtime
/// library's SR.UsingResourceKeys() is true and its FxResources string table goes away: 670 KB of the interim
/// TechEmpower binary were those strings, which nothing could read (SR returns the key) — what NativeAOT
/// removes too. (The driver refuses that switch for now, Compiler.UsesSystemResourceKeys: the corelib's own
/// messages aren't keys.) Other substitution kinds (fields, removed bodies) are not applied:
/// the code they would drop just stays.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private sealed class IlSubstitutions
    {
        public readonly Dictionary<(string Type, string Method), bool> BoolStubs = [];
        public readonly HashSet<string> RemovedResources = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<IlAssembly, IlSubstitutions> _substitutions = [];
    private readonly object _substitutionsLock = new();

    private IlSubstitutions SubstitutionsOf(IlAssembly assembly)
    {
        lock (_substitutionsLock)
        {
            if (_substitutions.TryGetValue(assembly, out var known)) return known;
            var result = new IlSubstitutions();
            var xml = assembly.ReadManifestResources().FirstOrDefault(r => r.Name == "ILLink.Substitutions.xml").Bytes;
            if (xml is not null)
            {
                try { Apply(XDocument.Load(new MemoryStream(xml)).Root, result); }
                catch (System.Xml.XmlException) { } // not one ILLink can read either: nothing applies
            }
            return _substitutions[assembly] = result;
        }
    }

    private void Apply(XElement? element, IlSubstitutions into)
    {
        if (element is null || !ConditionHolds(element)) return;
        switch (element.Name.LocalName)
        {
            case "resource" when (string?)element.Attribute("action") == "remove" && (string?)element.Attribute("name") is { } name:
                into.RemovedResources.Add(name);
                return;
            case "type" when (string?)element.Attribute("fullname") is { } typeName:
                foreach (var method in element.Elements("method"))
                {
                    if (!ConditionHolds(method) || (string?)method.Attribute("body") != "stub") continue;
                    // "System.Boolean UsingResourceKeys()": a parameterless bool method with a constant value.
                    if ((string?)method.Attribute("signature") is not { } signature || !signature.StartsWith("System.Boolean ", StringComparison.Ordinal)
                        || !signature.EndsWith("()", StringComparison.Ordinal) || !bool.TryParse((string?)method.Attribute("value"), out var value)) continue;
                    into.BoolStubs[(typeName.Replace('/', '.'), signature["System.Boolean ".Length..^2])] = value;
                }
                return;
            default:
                foreach (var child in element.Elements()) Apply(child, into);
                return;
        }
    }

    /// <summary>ILLink's feature condition: the switch has `featurevalue`, or is unset and the element is `featuredefault`.</summary>
    private bool ConditionHolds(XElement element)
    {
        if ((string?)element.Attribute("feature") is not { } feature) return true;
        if (!bool.TryParse((string?)element.Attribute("featurevalue"), out var wanted)) return false;
        foreach (var (name, value) in RuntimeConfig)
            if (name == feature) return bool.TryParse(value, out var actual) && actual == wanted;
        return string.Equals((string?)element.Attribute("featuredefault"), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The value a substitution stubs this IL method with (a parameterless static bool method), or null.</summary>
    internal bool? SubstitutedSwitch(IMethodSymbol method)
    {
        if (!method.IsStatic || !method.Parameters.IsEmpty || method.IsGenericMethod || method.ReturnType.SpecialType != SpecialType.System_Boolean
            || Il.OwnerOf(method) is not { } owner) return null;
        return SubstitutionsOf(owner).BoolStubs.TryGetValue((method.ContainingType.ToDisplayString(), method.Name), out var value) ? value : null;
    }

    /// <summary>
    /// A static bool property marked [FeatureSwitchDefinition("name")] whose switch the program's runtime configuration sets: its value, as
    /// ILCompiler substitutes it (System.Linq.Enumerable.IsSizeOptimized, which NativeAOT's targets set to true). Null otherwise; the getter
    /// then runs and reads the same configuration through AppContext.
    /// </summary>
    internal bool? FeatureSwitchDefinitionValue(IMethodSymbol method)
    {
        if (method is not { IsStatic: true, MethodKind: MethodKind.PropertyGet, Parameters.IsEmpty: true, ReturnType.SpecialType: SpecialType.System_Boolean }
            || method.AssociatedSymbol is not IPropertySymbol property) return null;
        foreach (var attribute in property.GetAttributes())
            if (attribute.AttributeClass is { Name: "FeatureSwitchDefinitionAttribute" } && attribute.ConstructorArguments is [{ Value: string name }])
                foreach (var (key, value) in RuntimeConfig)
                    if (key == name && bool.TryParse(value, out var configured)) return configured;
        return null;
    }

    /// <summary>Whether a substitution removes this manifest resource ("FxResources.X.SR.resources") of the assembly.</summary>
    internal bool IsResourceRemoved(IlAssembly assembly, string resourceName) => SubstitutionsOf(assembly).RemovedResources.Contains(resourceName);
}
