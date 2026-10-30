using System.Reflection.Metadata;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Feature switches with a value fixed at compile time, folded in IL the way NativeAOT's ILCompiler does
/// (ILLink substitutions, [FeatureGuard]): `if (RuntimeFeature.IsDynamicCodeCompiled) ...` in
/// Microsoft.Extensions.DependencyInjection guards the Reflection.Emit-based resolver. Lowering is
/// flow-insensitive, so a guarded branch would otherwise be lowered (and the code it reaches compiled,
/// or reported as unsupported) although it can never run. The getter becomes its constant, a conditional
/// branch on it becomes a jump, and IL blocks no path reaches are not lowered.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>
    /// The compile-time value of a feature-switch getter, or null: a known switch, or (like ILCompiler's
    /// constant propagation) a static bool getter of an IL assembly whose body only forwards to one —
    /// `[FeatureGuard(typeof(RequiresDynamicCodeAttribute))] static bool CanCompileToIL =>
    /// RuntimeFeature.IsDynamicCodeSupported` in System.Linq.Expressions, without which Compile() would
    /// reach the LambdaCompiler (Reflection.Emit) and CallInstruction its ~20,000 generic fast paths.
    /// </summary>
    private bool? FeatureSwitchValue(IMethodSymbol method) => KnownFeatureSwitch(method) ?? _m.ForwardingFeatureSwitch(method, 0);

    internal static bool? KnownFeatureSwitch(IMethodSymbol method) =>
        (method.ContainingType.ToDisplayString(), method.Name) switch
        {
            // No code is generated at run time (as under NativeAOT): corelib/RuntimeServices.cs answers the same.
            ("System.Runtime.CompilerServices.RuntimeFeature", "get_IsDynamicCodeSupported" or "get_IsDynamicCodeCompiled") => false,
            // [FeatureSwitchDefinition("System.Linq.Expressions.CanEmitObjectArrayDelegate")], which NativeAOT's
            // targets set to false: the interpreter's delegates come from DynamicDelegateAugments
            // (corelib/Reflection/ObjectArrayDelegates.cs), not Reflection.Emit.
            ("System.Dynamic.Utils.DelegateHelpers", "get_CanEmitObjectArrayDelegate") => false,
            // csharpc has no hardware intrinsics yet: the framework's scalar fallbacks run (same results by
            // design). Ssse3.IsSupported, AdvSimd.Arm64.IsSupported, Vector128.IsHardwareAccelerated, ...
            (_, "get_IsSupported") when IsHardwareIntrinsicClass(method.ContainingType) => false,
            ("System.Runtime.Intrinsics.Vector64" or "System.Runtime.Intrinsics.Vector128" or "System.Runtime.Intrinsics.Vector256"
                or "System.Runtime.Intrinsics.Vector512" or "System.Numerics.Vector", "get_IsHardwareAccelerated") => false,
            _ => null,
        };

    private static bool IsHardwareIntrinsicClass(INamedTypeSymbol type)
    {
        var outer = type;
        while (outer.ContainingType is { } containing) outer = containing;
        return outer.ContainingNamespace?.ToDisplayString() is "System.Runtime.Intrinsics.X86" or "System.Runtime.Intrinsics.Arm" or "System.Runtime.Intrinsics.Wasm";
    }

    /// <summary>The value a conditional branch tests, when the instruction before it in its block is a constant.</summary>
    private bool? IlConstantCondition(IlBlock block, int index)
    {
        if (index <= block.First) return null;
        var previous = IlBody.Instructions[index - 1];
        switch (previous.OpCode)
        {
            case ILOpCode.Ldc_i4_0: return false;
            case ILOpCode.Ldc_i4_1: return true;
            case ILOpCode.Call or ILOpCode.Callvirt:
                try { return IlAssembly.ResolveMember(previous.Token, IlBody.Context) is IMethodSymbol m ? FeatureSwitchValue(m) : null; }
                catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { return null; }
            default: return null;
        }
    }

    /// <summary>
    /// For brtrue/brfalse on a constant: the block the branch always goes to (its target or fall-through).
    /// </summary>
    private int? IlFoldedBranch(IlBlock block, int index)
    {
        var instruction = IlBody.Instructions[index];
        if (instruction.OpCode is not (ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s)) return null;
        if (IlConstantCondition(block, index) is not { } value) return null;
        var jumpsWhenTrue = instruction.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s;
        return value == jumpsWhenTrue ? instruction.Target : instruction.Next;
    }

    /// <summary>
    /// Block starts reachable from the entry and from every handler and filter (entered by exceptions),
    /// following folded branches only one way.
    /// </summary>
    private HashSet<int> IlReachableBlocks(IlState il)
    {
        var reachable = new HashSet<int>();
        var work = new Stack<int>();
        void Reach(int offset)
        {
            if (il.BlockAt.ContainsKey(offset) && reachable.Add(offset)) work.Push(offset);
        }
        Reach(il.Blocks[0].Start);
        foreach (var region in il.Body.Regions)
        {
            Reach(region.HandlerOffset);
            if (region.Kind == ExceptionRegionKind.Filter) Reach(region.FilterOffset);
        }
        while (work.Count > 0)
        {
            var block = il.BlockAt[work.Pop()];
            var last = IlBody.Instructions[block.Last];
            if (IlFoldedBranch(block, block.Last) is { } only)
            {
                Reach(only);
                continue;
            }
            if (last.Targets is { } targets) foreach (var t in targets) Reach(t);
            if (!IlDecoder.EndsBlock(last.OpCode)) Reach(block.End);
        }
        return reachable;
    }
}
