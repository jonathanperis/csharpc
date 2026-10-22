using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Raised while lowering when a construct is outside the subset csharpc currently supports.
/// Caught per-method so one unsupported construct doesn't hide the rest of the report.
/// </summary>
public sealed class CompileError(string id, string message, Location? location) : Exception(message)
{
    public string Id { get; } = id;
    public Location? Location { get; } = location;

    public static CompileError Unsupported(string what, IOperation? at = null) =>
        new("CSLLVM0001", $"not supported by csharpc yet: {what}", at?.Syntax.GetLocation());

    public static CompileError Unsupported(string what, ISymbol symbol) =>
        new("CSLLVM0001", $"not supported by csharpc yet: {what}", symbol.Locations.FirstOrDefault());

    public static CompileError MissingIntrinsic(IMethodSymbol method, IOperation at) =>
        new("CSLLVM0002", $"'{Intrinsics.Key(method)}' has no implementation in the csharpc runtime yet", at.Syntax.GetLocation());

    public string Format()
    {
        if (Location is { IsInSource: true } loc)
        {
            var span = loc.GetLineSpan();
            return $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): error {Id}: {Message}";
        }
        return $"error {Id}: {Message}";
    }
}
