using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Driver;

/// <summary>Exploit-mitigation level for the produced executable (docs/16-security.md).</summary>
public enum HardeningLevel
{
    /// <summary>Nothing beyond the platform defaults (PIE, NX).</summary>
    None,
    /// <summary>Full RELRO + BIND_NOW, CET (IBT + SHSTK), stack probes, and for the C runtime: stack
    /// protector, _FORTIFY_SOURCE=3, zero-initialized locals. Costs ≲1 % on the benchmarks.</summary>
    Default,
    /// <summary>Default + stack protector and call-used-register zeroing on generated code, and
    /// Spectre v1 speculative load hardening. Measurably slower; for hostile-input services.</summary>
    Max,
}

/// <summary>
/// Turns a <see cref="HardeningLevel"/> into compiler flags for the C runtime, attributes/module flags
/// for generated code, and linker flags. None of these change program semantics: they only make
/// memory-corruption bugs (in the C runtime or in unsafe C# code) harder to exploit.
/// </summary>
public static class Hardening
{
    public static HardeningLevel Parse(string value) => value.ToLowerInvariant() switch
    {
        "none" or "off" => HardeningLevel.None,
        "default" or "on" => HardeningLevel.Default,
        "max" => HardeningLevel.Max,
        _ => throw new ArgumentException($"unknown --hardening level '{value}' (none | default | max)"),
    };

    /// <summary>Extra clang flags for src/runtime/*.c (objects and LTO bitcode alike).</summary>
    public static string[] RuntimeCFlags(HardeningLevel level) => level switch
    {
        HardeningLevel.None => [],
        HardeningLevel.Default =>
        [
            "-fstack-protector-strong", "-D_FORTIFY_SOURCE=3", "-fcf-protection=full",
            "-fstack-clash-protection", "-ftrivial-auto-var-init=zero",
        ],
        _ =>
        [
            "-fstack-protector-strong", "-D_FORTIFY_SOURCE=3", "-fcf-protection=full",
            "-fstack-clash-protection", "-ftrivial-auto-var-init=zero",
            "-mspeculative-load-hardening", "-fzero-call-used-regs=used-gpr",
        ],
    };

    /// <summary>Linker flags (passed through clang to the system linker).</summary>
    public static string[] LinkFlags(HardeningLevel level) => level == HardeningLevel.None
        ? []
        : ["-Wl,-z,relro", "-Wl,-z,now", "-Wl,-z,noexecstack", "-Wl,-z,separate-code"];

    /// <summary>
    /// Module flags for the program module. Must run before the runtime bitcode is linked in: clang
    /// marks cf-protection with "Min" merge behaviour, so the program module has to carry the flag too
    /// for the final object to get endbr64 landing pads and the IBT/SHSTK GNU property note.
    /// </summary>
    public static void ApplyModuleFlags(LlvmModule module, LlvmContext llvm, HardeningLevel level)
    {
        if (level == HardeningLevel.None) return;
        AddFlag(module, llvm, 8 /* Min */, "cf-protection-branch", 1);
        AddFlag(module, llvm, 8 /* Min */, "cf-protection-return", 1);
    }

    /// <summary>Attributes on every function the compiler generated (the runtime gets clang's).</summary>
    public static void ApplyFunctionAttributes(LlvmModule module, LlvmContext llvm, HardeningLevel level)
    {
        if (level == HardeningLevel.None) return;
        for (var fn = LLVMGetFirstFunction(module.Ref); !fn.IsNull; fn = LLVMGetNextFunction(fn))
        {
            if (LLVMIsDeclaration(fn) != 0) continue;
            // Stack-clash protection: probe large frames page by page (cheap; only big frames pay).
            llvm.AddStringFunctionAttribute(fn, "probe-stack", "inline-asm");
            if (level == HardeningLevel.Max)
            {
                // Generated code is memory-safe C# except `unsafe`/stackalloc; Max adds a canary anyway.
                llvm.AddFunctionAttribute(fn, "sspstrong");
                llvm.AddFunctionAttribute(fn, "speculative_load_hardening");
                llvm.AddStringFunctionAttribute(fn, "zero-call-used-regs", "used-gpr");
            }
        }
    }

    /// <summary>
    /// Adds `!{i32 behavior, !"key", i32 value}` to llvm.module.flags directly: the C API's
    /// LLVMModuleFlagBehavior enum has no "Min" (IR value 8), which is what clang uses here.
    /// </summary>
    private static void AddFlag(LlvmModule module, LlvmContext llvm, int behavior, string key, long value)
    {
        LLVMMetadataRef[] operands =
        [
            LLVMValueAsMetadata(llvm.ConstInt(llvm.I32, behavior)),
            LLVMMDStringInContext2(llvm.Ref, key, (nuint)key.Length),
            LLVMValueAsMetadata(llvm.ConstInt(llvm.I32, value)),
        ];
        var node = LLVMMDNodeInContext2(llvm.Ref, operands, (nuint)operands.Length);
        LLVMAddNamedMetadataOperand(module.Ref, "llvm.module.flags", LLVMMetadataAsValue(llvm.Ref, node));
    }
}
