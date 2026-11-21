using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>Type facts used for performance decisions (see benchmarks/RESULTS.md).</summary>
internal static class Optimizations
{
    /// <summary>
    /// -Os / -Oz: mark every function defined in the module (user code and the LTO-linked runtime)
    /// optsize (+ minsize for -Oz), the attributes clang uses, so inlining, unrolling and the backend
    /// all favor code size.
    /// </summary>
    public static void MarkOptimizeForSize(LlvmModule module, LlvmContext llvm, bool minimize)
    {
        for (var fn = Native.LLVMGetFirstFunction(module.Ref); fn.Handle != 0; fn = Native.LLVMGetNextFunction(fn))
        {
            if (Native.LLVMIsDeclaration(fn) != 0) continue;
            llvm.AddFunctionAttribute(fn, "optsize");
            if (minimize) llvm.AddFunctionAttribute(fn, "minsize");
        }
    }

    /// <summary>
    /// Programs with coroutines (async methods, iterators): keeps out of the inliner every function that puts the address of one of its
    /// own stack slots into an aggregate value, the way `new ReadOnlySpan&lt;char&gt;(ref separator)` does in string.Split(char)'s IL. Inlined
    /// into a coroutine before CoroSplit, such a slot carries the inliner's lifetime markers, so LLVM 18's coroutine frame builder keeps it
    /// on the stack when its lifetime does not cross a suspension; but the span holding its address is a plain SSA value, which it does not
    /// count as an escape, and LICM hoists that span out of the loop, across the await: spilled to the frame and reloaded after the resume,
    /// it points into the stack of the call that started the coroutine (`s.Split(' ')` after an `await` in a loop returned the whole
    /// string). Not inlined, the slot lives in the callee's own frame. Returns how many functions were marked.
    /// </summary>
    public static int KeepStackAddressAggregatesOutOfInliner(LlvmModule module, LlvmContext llvm)
    {
        static bool FlowsIntoAggregate(LLVMValueRef value, int depth)
        {
            for (var use = Native.LLVMGetFirstUse(value); use != 0; use = Native.LLVMGetNextUse(use))
            {
                var user = Native.LLVMGetUser(use);
                if (Native.LLVMIsAInstruction(user).Handle == 0) continue;
                var opcode = Native.LLVMGetInstructionOpcode(user);
                if (opcode == LLVMOpcode.InsertValue) return true;
                if (opcode == LLVMOpcode.GetElementPtr && depth < 4 && FlowsIntoAggregate(user, depth + 1)) return true;
            }
            return false;
        }
        var noinline = Native.LLVMGetEnumAttributeKindForName("noinline", 8);
        var alwaysinline = Native.LLVMGetEnumAttributeKindForName("alwaysinline", 12);
        var marked = 0;
        for (var fn = Native.LLVMGetFirstFunction(module.Ref); fn.Handle != 0; fn = Native.LLVMGetNextFunction(fn))
        {
            if (Native.LLVMIsDeclaration(fn) != 0 || Native.LLVMGetEnumAttributeAtIndex(fn, uint.MaxValue, alwaysinline).Handle != 0
                || Native.LLVMGetEnumAttributeAtIndex(fn, uint.MaxValue, noinline).Handle != 0) continue;
            var entry = Native.LLVMGetFirstBasicBlock(fn);
            var found = false;
            for (var inst = Native.LLVMGetFirstInstruction(entry); inst.Handle != 0 && !found; inst = Native.LLVMGetNextInstruction(inst))
                found = Native.LLVMIsAAllocaInst(inst).Handle != 0 && FlowsIntoAggregate(inst, 0);
            if (!found) continue;
            llvm.AddFunctionAttribute(fn, "noinline");
            marked++;
        }
        return marked;
    }

    /// <summary>
    /// ABI and aliasing facts for calls into csrt (C):
    ///  * small integers in the csrt API are all unsigned (bool, uint8_t, uint16_t), and the SysV
    ///    ABI makes the *caller* extend them — clang-compiled callees rely on it — so mark them zeroext;
    ///  * allocation helpers return fresh, non-null memory (noalias + nonnull), which lets LLVM keep
    ///    values in registers across them and drop null checks on new objects when the runtime
    ///    isn't LTO-linked (with runtime LTO, the definitions are visible anyway).
    /// </summary>
    public static void AnnotateRuntimeDeclaration(LlvmContext llvm, LLVMValueRef fn, LLVMTypeRef ret, LLVMTypeRef[] parameters)
    {
        static bool IsSmallInt(LLVMTypeRef t) => t.IsInteger && t.IntWidth < 32;
        if (IsSmallInt(ret)) llvm.AddAttributeAt(fn, 0, "zeroext");
        for (var i = 0; i < parameters.Length; i++)
            if (IsSmallInt(parameters[i])) llvm.AddAttributeAt(fn, (uint)i + 1, "zeroext");
        if (s_allocators.Contains(Native.TakeName(fn)))
        {
            llvm.AddAttributeAt(fn, 0, "noalias");
            llvm.AddAttributeAt(fn, 0, "nonnull");
        }
    }

    private static readonly HashSet<string> s_allocators =
        ["csrt_alloc", "csrt_new_array", "csrt_new_array_primitive", "csrt_alloc_noclear"];

    /// <summary>
    /// Primitive and enum values contain no object references, so containers of them (arrays)
    /// can be skipped entirely by the GC's marker.
    /// </summary>
    public static bool HoldsNoReferences(ITypeSymbol type) => type switch
    {
        { SpecialType: SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte
            or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_IntPtr or SpecialType.System_UIntPtr } => true,
        { TypeKind: TypeKind.Enum } => true,
        _ when BclValueTypes.IsDecimal(type) || BclValueTypes.IsTicks(type) => true, // plain bits (BclValueTypes)
        _ => false,
    };

    /// <summary>
    /// RuntimeHelpers.IsReferenceOrContainsReferences&lt;T&gt;(): reference types, and structs with a
    /// reference-typed field at any depth. Reference assemblies keep a placeholder field of type object in
    /// library structs that hold references, so library structs answer as in .NET too.
    /// </summary>
    public static bool IsReferenceOrContainsReferences(ITypeSymbol type) => IsReferenceOrContainsReferences(type, 0);

    private static bool IsReferenceOrContainsReferences(ITypeSymbol type, int depth)
    {
        if (HoldsNoReferences(type) || type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) return false;
        if (!type.IsValueType || depth > 32) return true;
        if (TypeMapper.IsSpan(type)) return true; // { reference or pointer, length }; Memory<T> holds its owner object
        return type.GetMembers().OfType<IFieldSymbol>()
            .Any(f => !f.IsStatic && !f.IsConst && (f.IsFixedSizeBuffer ? false : IsReferenceOrContainsReferences(f.Type, depth + 1)));
    }
}
