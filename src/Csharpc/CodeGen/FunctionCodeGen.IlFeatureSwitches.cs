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
    private bool? FeatureSwitchValue(IMethodSymbol method) => KnownFeatureSwitch(method) ?? _m.SubstitutedSwitch(method) ?? _m.FeatureSwitchDefinitionValue(method) ?? _m.ForwardingFeatureSwitch(method, 0);

    internal static bool? KnownFeatureSwitch(IMethodSymbol method) =>
        (method.ContainingType.ToDisplayString(), method.Name) switch
        {
            // No code is generated at run time (as under NativeAOT): corelib/RuntimeServices.cs answers the same.
            ("System.Runtime.CompilerServices.RuntimeFeature", "get_IsDynamicCodeSupported" or "get_IsDynamicCodeCompiled") => false,
            // [FeatureSwitchDefinition("System.Linq.Expressions.CanEmitObjectArrayDelegate")], which NativeAOT's
            // targets set to false: the interpreter's delegates come from DynamicDelegateAugments
            // (corelib/Reflection/ObjectArrayDelegates.cs), not Reflection.Emit.
            ("System.Dynamic.Utils.DelegateHelpers", "get_CanEmitObjectArrayDelegate") => false,
            // No hot reload (no edits are applied to a native program): [FeatureSwitchDefinition
            // ("System.Reflection.Metadata.MetadataUpdater.IsSupported")], false under NativeAOT and in the
            // runtimeconfig PublishAot writes. RazorSlices' HotReloadService and others guard their handlers with it.
            ("System.Reflection.Metadata.MetadataUpdater", "get_IsSupported") => false,
            // csharpc has no QUIC: false, as .NET answers without MsQuic; the getter itself still runs and stops
            // the program where MsQuic is installed (IsCheckedFeatureGuard). Kestrel's UseQuic tests it on start.
            ("System.Net.Quic.QuicListener" or "System.Net.Quic.QuicConnection", "get_IsSupported") => false,
            // csharpc has no hardware intrinsics yet: the framework's scalar fallbacks run (same results by
            // design). Ssse3.IsSupported, AdvSimd.Arm64.IsSupported, Vector128.IsHardwareAccelerated, ...
            (_, "get_IsSupported") when IsHardwareIntrinsicClass(method.ContainingType) => false,
            // Vector128 and Vector<T> (16 bytes, NativeAOT's default) are on LLVM vectors where the target has a 128-bit unit
            // (FunctionCodeGen.Simd.cs); Vector64/256/512 are not.
            ("System.Runtime.Intrinsics.Vector128" or "System.Numerics.Vector", "get_IsHardwareAccelerated") => Vector128Accelerated,
            ("System.Runtime.Intrinsics.Vector64" or "System.Runtime.Intrinsics.Vector256"
                or "System.Runtime.Intrinsics.Vector512", "get_IsHardwareAccelerated") => false,
            _ => null,
        };

    /// <summary>A known feature switch whose getter must still be called for its checks (it may throw).</summary>
    private static bool IsCheckedFeatureGuard(IMethodSymbol method) =>
        method.Name == "get_IsSupported" && method.ContainingType.ToDisplayString() is "System.Net.Quic.QuicListener" or "System.Net.Quic.QuicConnection";

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
            // csc's guard around a dynamic call site's creation: csharpc binds the site, there is nothing to create.
            case ILOpCode.Ldsfld when IlIsDynamicGuard(previous.Token): return true;
            case ILOpCode.Call or ILOpCode.Callvirt:
                try
                {
                    if (IlAssembly.ResolveMember(previous.Token, IlBody.Context) is not IMethodSymbol m) return null;
                    return IlTypeEqualityConstant(block, index - 1, m) ?? IlTypeQueryConstant(block, index - 1, m) ?? FeatureSwitchValue(m);
                }
                catch (Exception e) when (e is IlException or BadImageFormatException or InvalidOperationException) { return null; }
            default: return null;
        }
    }

    /// <summary>
    /// `typeof(A) == typeof(B)` / `!=` (ldtoken, GetTypeFromHandle, ldtoken, GetTypeFromHandle, op_Equality) with both types known: in a generic
    /// method lowered for one instantiation that is a constant, and the branch it guards is never lowered. This is what keeps
    /// `if (typeof(T) == typeof(string)) ... else general-path` from compiling the general path (and everything it reaches) for T = string, as
    /// ILCompiler's dead-branch elimination does; the framework's collections and formatting are full of it.
    /// </summary>
    private bool? IlTypeEqualityConstant(IlBlock block, int callIndex, IMethodSymbol equality)
    {
        if (equality is not { Name: "op_Equality" or "op_Inequality", ContainingType: { Name: "Type", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } }
            || callIndex - 4 < block.First) return null;
        var instructions = IlBody.Instructions;
        ITypeSymbol? TypeLoadedAt(int tokenIndex, int getIndex)
        {
            if (instructions[tokenIndex].OpCode != ILOpCode.Ldtoken || instructions[getIndex].OpCode != ILOpCode.Call) return null;
            var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instructions[tokenIndex].Token);
            if (handle.Kind is not (HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification)) return null;
            if (IlAssembly.ResolveMember(instructions[getIndex].Token, IlBody.Context) is not IMethodSymbol { Name: "GetTypeFromHandle", ContainingType.Name: "Type" }) return null;
            return S(ResolveIlType(instructions[tokenIndex].Token));
        }
        if (TypeLoadedAt(callIndex - 4, callIndex - 3) is not { } left || TypeLoadedAt(callIndex - 2, callIndex - 1) is not { } right) return null;
        if (left is ITypeParameterSymbol || right is ITypeParameterSymbol) return null;
        var same = SymbolEqualityComparer.Default.Equals(left, right);
        // Shared generic code: a marker stands for any reference type, so two different types that name one are only known to differ when
        // one is a value type and the other a reference type.
        if (!same && (_m.IsCanonDependent(left) || _m.IsCanonDependent(right)) && left.IsReferenceType == right.IsReferenceType) return null;
        return equality.Name == "op_Equality" ? same : !same;
    }

    /// <summary>`typeof(T).IsEnum` / `.IsValueType` (ldtoken, GetTypeFromHandle, callvirt) for a known T: a constant, like <see cref="IlTypeEqualityConstant"/>.</summary>
    private bool? IlTypeQueryConstant(IlBlock block, int callIndex, IMethodSymbol query)
    {
        if (query is not { Name: "get_IsEnum" or "get_IsValueType", ContainingType: { Name: "Type", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } }
            || callIndex - 2 < block.First) return null;
        var instructions = IlBody.Instructions;
        if (instructions[callIndex - 2].OpCode != ILOpCode.Ldtoken || instructions[callIndex - 1].OpCode != ILOpCode.Call) return null;
        var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instructions[callIndex - 2].Token);
        if (handle.Kind is not (HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification)) return null;
        if (IlAssembly.ResolveMember(instructions[callIndex - 1].Token, IlBody.Context) is not IMethodSymbol { Name: "GetTypeFromHandle", ContainingType.Name: "Type" }) return null;
        var type = S(ResolveIlType(instructions[callIndex - 2].Token));
        if (type is ITypeParameterSymbol) return null;
        return query.Name == "get_IsEnum" ? type.TypeKind == TypeKind.Enum : type.IsValueType;
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
