using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Wasm;

/// <summary>Small helpers over the LLVM C API for the whole-module passes in this folder.</summary>
internal static class Ir
{
    public static List<LLVMValueRef> Functions(LLVMModuleRef module)
    {
        List<LLVMValueRef> list = [];
        for (var fn = LLVMGetFirstFunction(module); !fn.IsNull; fn = LLVMGetNextFunction(fn)) list.Add(fn);
        return list;
    }

    public static List<LLVMValueRef> DefinedFunctions(LLVMModuleRef module) =>
        [.. Functions(module).Where(fn => LLVMIsDeclaration(fn) == 0)];

    public static List<LLVMBasicBlockRef> Blocks(LLVMValueRef fn)
    {
        List<LLVMBasicBlockRef> list = [];
        for (var bb = LLVMGetFirstBasicBlock(fn); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb)) list.Add(bb);
        return list;
    }

    public static List<LLVMValueRef> Instructions(LLVMBasicBlockRef bb)
    {
        List<LLVMValueRef> list = [];
        for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst)) list.Add(inst);
        return list;
    }

    public static LLVMOpcode Opcode(LLVMValueRef inst) => LLVMGetInstructionOpcode(inst);

    public static bool IsCall(LLVMValueRef inst) => Opcode(inst) is LLVMOpcode.Call or LLVMOpcode.Invoke;

    /// <summary>The function a call targets directly (through aliases), or null for an indirect call.</summary>
    public static LLVMValueRef DirectCallee(LLVMValueRef call)
    {
        var callee = LLVMGetCalledValue(call);
        for (var i = 0; i < 8 && !LLVMIsAGlobalAlias(callee).IsNull; i++) callee = LLVMAliasGetAliasee(callee);
        return LLVMIsAFunction(callee);
    }

    public static bool IsInlineAsmCall(LLVMValueRef call) => !LLVMIsAInlineAsm(LLVMGetCalledValue(call)).IsNull;

    public static bool IsIntrinsic(LLVMValueRef fn) => LLVMGetIntrinsicID(fn) != 0;

    public static string Name(LLVMValueRef value) => TakeName(value);

    public static bool HasEnumAttribute(LLVMValueRef fn, string name) =>
        LLVMGetEnumAttributeAtIndex(fn, FunctionAttributeIndex, KindOf(name)).Handle != 0;

    public static uint KindOf(string attribute) => LLVMGetEnumAttributeKindForName(attribute, (nuint)attribute.Length);

    public static IEnumerable<LLVMValueRef> Operands(LLVMValueRef inst)
    {
        var n = LLVMGetNumOperands(inst);
        for (var i = 0; i < n; i++) yield return LLVMGetOperand(inst, (uint)i);
    }

    public static IEnumerable<LLVMBasicBlockRef> Successors(LLVMBasicBlockRef bb)
    {
        var terminator = LLVMGetBasicBlockTerminator(bb);
        if (terminator.IsNull) yield break;
        var n = LLVMGetNumSuccessors(terminator);
        for (var i = 0u; i < n; i++) yield return LLVMGetSuccessor(terminator, i);
    }

    /// <summary>
    /// Calls, per defined function, whose callee is a defined function, plus whether the function makes
    /// an indirect call (a virtual or delegate call, a function pointer) that could reach anything.
    /// </summary>
    public sealed class CallGraph
    {
        public Dictionary<nint, HashSet<nint>> Callees { get; } = [];
        public HashSet<nint> HasIndirectCall { get; } = [];

        public CallGraph(LLVMModuleRef module)
        {
            foreach (var fn in DefinedFunctions(module))
            {
                var callees = Callees[fn.Handle] = [];
                foreach (var bb in Blocks(fn))
                    for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
                    {
                        if (!IsCall(inst) || IsInlineAsmCall(inst)) continue;
                        var callee = DirectCallee(inst);
                        if (callee.IsNull) HasIndirectCall.Add(fn.Handle);
                        else callees.Add(callee.Handle);
                    }
            }
        }

        /// <summary>
        /// Functions that may (transitively) call one of <paramref name="roots"/> or make an indirect
        /// call. The roots themselves are included.
        /// </summary>
        public HashSet<nint> Reaching(IEnumerable<nint> roots, IReadOnlySet<nint>? except = null)
        {
            except ??= new HashSet<nint>();
            Dictionary<nint, List<nint>> callers = [];
            foreach (var (fn, callees) in Callees)
                foreach (var callee in callees)
                {
                    if (!callers.TryGetValue(callee, out var list)) callers[callee] = list = [];
                    list.Add(fn);
                }
            HashSet<nint> result = [.. roots, .. HasIndirectCall.Where(fn => !except.Contains(fn))];
            var work = new Stack<nint>(result);
            while (work.TryPop(out var fn))
                if (callers.TryGetValue(fn, out var list))
                    foreach (var caller in list)
                        if (!except.Contains(caller) && result.Add(caller)) work.Push(caller);
            return result;
        }
    }
}
