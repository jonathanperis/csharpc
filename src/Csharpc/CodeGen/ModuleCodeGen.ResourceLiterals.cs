using System.Reflection.Metadata;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// The texts of an IL framework library's exceptions and messages as literals, for the getters that are reached (issue #272). Every
/// shared-framework library has an internal <c>SR</c> class whose getters read its embedded string resources:
/// <c>internal static string Arg_X =&gt; GetResourceString("Arg_X");</c>. Carrying the whole table of each library costs its every text
/// (30 KB for System.ComponentModel.TypeConverter alone) in a program that reaches two of them; replacing a getter that has this shape by the
/// string it would return carries two. The tables of those libraries (<c>FxResources.X.SR</c>) are compiled in only when something lowered
/// asks for a text by a key it computes (a call of <c>SR.GetResourceString</c> that is not such a getter, or a ResourceManager for the table),
/// which keeps the previous behaviour for those. System.Private.CoreLib's own texts and the program's resources are not touched.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<IlAssembly, Dictionary<string, string>?> _srTexts = [];

    /// <summary>A lowered method asked for a framework library's text by a computed key: the tables of FxResources.*.SR stay in the binary.</summary>
    private bool _srTablesNeeded;

    private static bool IsSrBaseName(string baseName) =>
        baseName.StartsWith("FxResources.", StringComparison.Ordinal) && baseName.EndsWith(".SR", StringComparison.Ordinal);

    /// <summary>The library's neutral-culture texts of its SR class (key to value), or null without such a table.</summary>
    private Dictionary<string, string>? SrTextsOf(IlAssembly assembly)
    {
        lock (_srTexts)
        {
            if (_srTexts.TryGetValue(assembly, out var known)) return known;
            Dictionary<string, string>? texts = null;
            foreach (var (baseName, key, value) in assembly.ReadStringResources())
            {
                if (!IsSrBaseName(baseName) || IsResourceRemoved(assembly, baseName + ".resources")) continue;
                texts ??= new Dictionary<string, string>(StringComparer.Ordinal);
                texts[key] = value;
            }
            return _srTexts[assembly] = texts;
        }
    }

    /// <summary>
    /// The text a getter of the library's SR class returns, when its IL is exactly <c>ldstr key; (ldnull | ldstr default); call
    /// SR.GetResourceString; ret</c> and the key is in the library's table: the table's text, else the default, else the key (the order
    /// GetResourceString answers in). Null for any other method.
    /// </summary>
    private string? SrGetterText(IlAssembly assembly, IMethodSymbol method, IlMethodBody body)
    {
        if (method is not { IsStatic: true, MethodKind: MethodKind.PropertyGet, Parameters.IsEmpty: true, ReturnType.SpecialType: SpecialType.System_String }
            || method.ContainingType is not { Name: "SR", ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
            || SrTextsOf(assembly) is not { } texts) return null;
        var code = body.Instructions;
        if (code.Length is not (3 or 4) || code[0].OpCode != ILOpCode.Ldstr || code[^1].OpCode != ILOpCode.Ret || code[^2].OpCode != ILOpCode.Call) return null;
        string? fallback = null;
        if (code.Length == 4)
        {
            if (code[1].OpCode == ILOpCode.Ldstr) fallback = assembly.UserString(code[1].Token);
            else if (code[1].OpCode != ILOpCode.Ldnull) return null;
        }
        if (assembly.ResolveMember(code[^2].Token, body.Context) is not IMethodSymbol { Name: "GetResourceString", IsStatic: true } callee
            || callee.ContainingType.Name != "SR") return null;
        var key = assembly.UserString(code[0].Token);
        return texts.TryGetValue(key, out var text) ? text : fallback ?? key;
    }

    /// <summary>Gives <paramref name="method"/> a body that returns <paramref name="text"/>.</summary>
    private void EmitStringConstant(IMethodSymbol method, string text)
    {
        var fn = _functions[method].Fn;
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        b.Ret(StringLiteral(text));
    }

    /// <summary>A lowered library method that looks texts up by a key it computes: the tables must be there.</summary>
    private void NoteSrLookup(IlAssembly assembly, IMethodSymbol method)
    {
        if (!assembly.IsCoreLib && method is { IsStatic: true, Name: "GetResourceString" } && method.ContainingType is { Name: "SR", ContainingType: null })
            _srTablesNeeded = true;
    }

    private LLVMValueRef _resourceSlot;

    /// <summary>The slot the resource blob is stored in once lowering is done (<see cref="FinishResourceStrings"/>): the tables depend on what was lowered.</summary>
    internal LLVMValueRef ResourceStringsSlot()
    {
        if (_resourceSlot.IsNull)
            _resourceSlot = Module.AddGlobal("csharpc.resource_strings", Llvm.Ptr, LlvmContext.Null(Llvm.Ptr), constant: false, linkage: LLVMLinkage.Internal);
        return _resourceSlot;
    }

    private void FinishResourceStrings()
    {
        if (_resourceSlot.IsNull) return;
        Native.LLVMSetInitializer(_resourceSlot, ResourceStringsBlob());
    }
}
