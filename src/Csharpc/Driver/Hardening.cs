using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Driver;

/// <summary>Exploit-mitigation level for the produced executable (docs/17-security.md).</summary>
public enum HardeningLevel
{
    /// <summary>Nothing beyond the platform defaults (PIE, NX).</summary>
    None,
    /// <summary>Full RELRO + BIND_NOW, non-executable stack, separate code, stack-clash probes, and for the
    /// C runtime: stack protector, _FORTIFY_SOURCE=3, zero-initialized locals. Within noise on the benchmarks.</summary>
    Default,
    /// <summary>Default + control-flow protection (x86-64: CET IBT landing pads and shadow-stack marking;
    /// AArch64: BTI landing pads and PAC return-address signing), stack protector and call-used-register
    /// zeroing on generated code, and Spectre v1 speculative load hardening. Measurably slower; for
    /// hostile-input services.</summary>
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
    public static string[] RuntimeCFlags(HardeningLevel level, TargetMachine target) => level switch
    {
        HardeningLevel.None => [],
        // Control-flow protection is Max-only: CET measured +8-16% on call-heavy benchmarks, and Linux
        // does not enforce user-space IBT at all (only the shadow stack), so it is opt-in.
        HardeningLevel.Default =>
        [
            "-fstack-protector-strong", "-D_FORTIFY_SOURCE=3",
            "-fstack-clash-protection", "-ftrivial-auto-var-init=zero",
        ],
        _ =>
        [
            "-fstack-protector-strong", "-D_FORTIFY_SOURCE=3", ControlFlowProtectionFlag(target),
            "-fstack-clash-protection", "-ftrivial-auto-var-init=zero",
            "-mspeculative-load-hardening", "-fzero-call-used-regs=used-gpr",
        ],
    };

    /// <summary>clang rejects -fcf-protection (CET) outside x86; AArch64's counterpart is BTI + PAC.</summary>
    private static string ControlFlowProtectionFlag(TargetMachine target) =>
        target.IsX86_64 ? "-fcf-protection=full"
        : target.IsAArch64 ? "-mbranch-protection=standard"
        : throw new ArgumentException($"--hardening max is not supported on target '{target.Triple}' yet");

    /// <summary>Linker flags (passed through clang to the system linker).</summary>
    public static string[] LinkFlags(HardeningLevel level) => level == HardeningLevel.None
        ? []
        : ["-Wl,-z,relro", "-Wl,-z,now", "-Wl,-z,noexecstack", "-Wl,-z,separate-code"];

    /// <summary>
    /// Module flags for the program module. Must run before the runtime bitcode is linked in: clang
    /// marks cf-protection (x86 CET) and branch-target-enforcement / sign-return-address (AArch64
    /// BTI + PAC) with "Min" merge behaviour, so the program module has to carry the flags too for the
    /// final object to get its landing pads and the GNU property note that enables enforcement.
    /// </summary>
    public static void ApplyModuleFlags(LlvmModule module, LlvmContext llvm, HardeningLevel level, TargetMachine target)
    {
        if (level != HardeningLevel.Max) return;
        if (target.IsAArch64)
        {
            // The same set clang 18 emits for -mbranch-protection=standard.
            AddFlag(module, llvm, 8 /* Min */, "branch-target-enforcement", 1);
            AddFlag(module, llvm, 8 /* Min */, "guarded-control-stack", 1);
            AddFlag(module, llvm, 8 /* Min */, "sign-return-address", 1);
            return;
        }
        _ = ControlFlowProtectionFlag(target); // rejects targets without a known scheme
        AddFlag(module, llvm, 8 /* Min */, "cf-protection-branch", 1); // CET: endbr64 + shadow-stack marking
        AddFlag(module, llvm, 8 /* Min */, "cf-protection-return", 1);
    }

    /// <summary>Attributes on every function the compiler generated (the runtime gets clang's).</summary>
    public static void ApplyFunctionAttributes(LlvmModule module, LlvmContext llvm, HardeningLevel level, TargetMachine target)
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
                if (target.IsAArch64)
                {
                    // BTI landing pads + PAC-signed return addresses, as clang's per-function attributes
                    // (LLVM 19+ no longer falls back to the module flags for these).
                    llvm.AddStringFunctionAttribute(fn, "branch-target-enforcement", "true");
                    llvm.AddStringFunctionAttribute(fn, "sign-return-address", "non-leaf");
                    llvm.AddStringFunctionAttribute(fn, "sign-return-address-key", "a_key");
                }
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
