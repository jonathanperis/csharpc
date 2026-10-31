using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Where a program given as IL turns an exception into text. Its frames are recorded (from its
/// metadata and PDB, StackTraceInfo) when it names an exception type; if it doesn't but still gets an
/// exception's text, the text would silently lack the program's "   at ..." lines, so such a call site
/// is a diagnostic (ModuleCodeGen.WarnStackTraceFrames). In IL the exception often reaches the formatting code typed as
/// `object` (Console.WriteLine(object), string.Concat(object, object), `callvirt object::ToString`), so
/// the stack entry's own type is what tells.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private void IlCheckExceptionText(IMethodSymbol method, IlEntry? receiver, IlEntry[] args)
    {
        if (_m.MainIl is null || _m.Il.OwnerOf(_method) != _m.MainIl) return;
        if (method is { Name: "ToString", IsStatic: false, Parameters.IsEmpty: true } && receiver is { Type: { } receiverType })
            _m.WarnExceptionToString(S(receiverType), IlAt());
        if (!IsTextSink(method)) return;
        for (var i = 0; i < args.Length; i++)
            if (args[i].Type is { } argType) _m.WarnExceptionToString(S(argType), IlAt());
    }

    /// <summary>Library members that format an `object` (or T) argument with ToString().</summary>
    private static bool IsTextSink(IMethodSymbol method) => (method.ContainingType?.ToDisplayString(), method.Name) switch
    {
        ("System.Console" or "System.IO.TextWriter" or "System.IO.StreamWriter" or "System.IO.StringWriter", "Write" or "WriteLine") => true,
        ("string", "Concat" or "Format" or "Join") => true,
        ("System.Text.StringBuilder", "Append" or "AppendFormat" or "AppendLine") => true,
        ("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler", "AppendFormatted") => true,
        ("System.Text.StringBuilder.AppendInterpolatedStringHandler", "AppendFormatted") => true,
        ("System.Diagnostics.Debug" or "System.Diagnostics.Trace", "WriteLine" or "Write" or "Print") => true,
        _ => false,
    };
}
