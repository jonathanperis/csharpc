using System.Runtime.InteropServices;
using System.Text;
using static Csharpc.Llvm.Native;

namespace Csharpc.Llvm;

/// <summary>C# 14 extension members give the raw handles a little object-model feel.</summary>
public static class LlvmHandleExtensions
{
    extension(LLVMTypeRef type)
    {
        public LLVMTypeKind Kind => LLVMGetTypeKind(type);
        public bool IsInteger => type.Kind == LLVMTypeKind.Integer;
        public bool IsFloatingPoint => type.Kind is LLVMTypeKind.Float or LLVMTypeKind.Double;
        public bool IsPointer => type.Kind == LLVMTypeKind.Pointer;
        public bool IsVoid => type.Kind == LLVMTypeKind.Void;
        public uint IntWidth => LLVMGetIntTypeWidth(type);
    }

    extension(LLVMValueRef value)
    {
        public LLVMTypeRef Type => LLVMTypeOf(value);
        public LLVMValueRef Param(uint index) => LLVMGetParam(value, index);
        public void SetName(string name) => LLVMSetValueName2(value, name, (nuint)Encoding.UTF8.GetByteCount(name));
    }

    extension(LLVMBasicBlockRef block)
    {
        public bool HasTerminator => !LLVMGetBasicBlockTerminator(block).IsNull;
    }
}

public sealed class LlvmContext : IDisposable
{
    public LLVMContextRef Ref { get; } = LLVMContextCreate();

    public LLVMTypeRef I1 => LLVMInt1TypeInContext(Ref);
    public LLVMTypeRef I8 => LLVMInt8TypeInContext(Ref);
    public LLVMTypeRef I16 => LLVMInt16TypeInContext(Ref);
    public LLVMTypeRef I32 => LLVMInt32TypeInContext(Ref);
    public LLVMTypeRef I64 => LLVMInt64TypeInContext(Ref);
    public LLVMTypeRef F32 => LLVMFloatTypeInContext(Ref);
    public LLVMTypeRef F64 => LLVMDoubleTypeInContext(Ref);
    public LLVMTypeRef Void => LLVMVoidTypeInContext(Ref);
    /// <summary>LLVM 15+ uses opaque pointers: every managed reference is just <c>ptr</c>.</summary>
    public LLVMTypeRef Ptr => LLVMPointerTypeInContext(Ref, 0);

    public LLVMTypeRef FunctionType(LLVMTypeRef ret, params LLVMTypeRef[] parameters) =>
        LLVMFunctionType(ret, parameters, (uint)parameters.Length, 0);

    public LLVMTypeRef VarArgFunctionType(LLVMTypeRef ret, params LLVMTypeRef[] parameters) =>
        LLVMFunctionType(ret, parameters, (uint)parameters.Length, 1);

    public LLVMTypeRef Struct(params LLVMTypeRef[] elements) =>
        LLVMStructTypeInContext(Ref, elements, (uint)elements.Length, 0);

    public LLVMTypeRef NamedStruct(string name) => LLVMStructCreateNamed(Ref, name);

    public static void SetBody(LLVMTypeRef structType, params LLVMTypeRef[] elements) =>
        LLVMStructSetBody(structType, elements, (uint)elements.Length, 0);

    public static LLVMTypeRef Array(LLVMTypeRef element, ulong count) => LLVMArrayType2(element, count);

    public LLVMValueRef ConstInt(LLVMTypeRef type, long value) => LLVMConstInt(type, unchecked((ulong)value), 1);
    public LLVMValueRef ConstUInt(LLVMTypeRef type, ulong value) => LLVMConstInt(type, value, 0);
    public static LLVMValueRef ConstReal(LLVMTypeRef type, double value) => LLVMConstReal(type, value);
    public static LLVMValueRef Null(LLVMTypeRef type) => LLVMConstNull(type);
    public LLVMValueRef True => ConstInt(I1, 1);
    public LLVMValueRef False => ConstInt(I1, 0);

    public LLVMValueRef ConstStruct(params LLVMValueRef[] values) =>
        LLVMConstStructInContext(Ref, values, (uint)values.Length, 0);

    public static LLVMValueRef ConstNamedStruct(LLVMTypeRef type, params LLVMValueRef[] values) =>
        LLVMConstNamedStruct(type, values, (uint)values.Length);

    public static LLVMValueRef ConstArray(LLVMTypeRef element, params LLVMValueRef[] values) =>
        LLVMConstArray2(element, values, (ulong)values.Length);

    public unsafe LLVMValueRef ConstCString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* p = bytes) return LLVMConstStringInContext(Ref, p, (uint)bytes.Length, 0);
    }

    public void AddFunctionAttribute(LLVMValueRef fn, string name, ulong value = 0)
    {
        var kind = LLVMGetEnumAttributeKindForName(name, (nuint)name.Length);
        LLVMAddAttributeAtIndex(fn, uint.MaxValue /* LLVMAttributeFunctionIndex */, LLVMCreateEnumAttribute(Ref, kind, value));
    }

    /// <summary>Adds an enum attribute (e.g. "zeroext") to parameter <paramref name="index"/> (0-based).</summary>
    public void AddParameterAttribute(LLVMValueRef fn, uint index, string name)
    {
        var kind = LLVMGetEnumAttributeKindForName(name, (nuint)name.Length);
        LLVMAddAttributeAtIndex(fn, index + 1, LLVMCreateEnumAttribute(Ref, kind, 0));
    }

    public void AddReturnAttribute(LLVMValueRef fn, string name)
    {
        var kind = LLVMGetEnumAttributeKindForName(name, (nuint)name.Length);
        LLVMAddAttributeAtIndex(fn, 0 /* LLVMAttributeReturnIndex */, LLVMCreateEnumAttribute(Ref, kind, 0));
    }

    public void Dispose() => LLVMContextDispose(Ref);
}

public sealed class LlvmModule(LlvmContext context, string name) : IDisposable
{
    public LlvmContext Context { get; } = context;
    public LLVMModuleRef Ref { get; } = LLVMModuleCreateWithNameInContext(name, context.Ref);

    public string SourceFileName { set => LLVMSetSourceFileName(Ref, value, (nuint)Encoding.UTF8.GetByteCount(value)); }

    public LLVMValueRef AddFunction(string name, LLVMTypeRef type) => LLVMAddFunction(Ref, name, type);

    public LLVMValueRef GetFunction(string name) => LLVMGetNamedFunction(Ref, name);

    public LLVMValueRef GetOrDeclare(string name, LLVMTypeRef type)
    {
        var fn = GetFunction(name);
        return fn.IsNull ? AddFunction(name, type) : fn;
    }

    public LLVMValueRef AddGlobal(string name, LLVMTypeRef type, LLVMValueRef? initializer = null,
        bool constant = false, LLVMLinkage linkage = LLVMLinkage.External)
    {
        var global = LLVMAddGlobal(Ref, type, name);
        if (initializer is { } init) LLVMSetInitializer(global, init);
        LLVMSetGlobalConstant(global, constant ? 1 : 0);
        LLVMSetLinkage(global, linkage);
        return global;
    }

    public void ConfigureFor(TargetMachine target)
    {
        LLVMSetTarget(Ref, target.Triple);
        LLVMSetModuleDataLayout(Ref, target.DataLayout);
    }

    public string PrintToString() => TakeMessage(LLVMPrintModuleToString(Ref)) ?? "";

    public bool Verify(out string? error)
    {
        var failed = LLVMVerifyModule(Ref, 2 /* LLVMReturnStatusAction */, out var message) != 0;
        error = TakeMessage(message);
        return !failed;
    }

    public void WriteBitcode(string path)
    {
        if (LLVMWriteBitcodeToFile(Ref, path) != 0) throw new IOException($"failed to write bitcode to '{path}'");
    }

    /// <summary>Runs an LLVM new-pass-manager pipeline, e.g. <c>default&lt;O2&gt;</c>.</summary>
    public void RunPasses(string pipeline, TargetMachine target)
    {
        var options = LLVMCreatePassBuilderOptions();
        try
        {
            var error = LLVMRunPasses(Ref, pipeline, target.Ref, options);
            if (error.Handle != 0)
            {
                var message = LLVMGetErrorMessage(error);
                var text = Marshal.PtrToStringUTF8(message);
                LLVMDisposeErrorMessage(message);
                throw new InvalidOperationException($"LLVM pass pipeline '{pipeline}' failed: {text}");
            }
        }
        finally { LLVMDisposePassBuilderOptions(options); }
    }

    public void Dispose() => LLVMDisposeModule(Ref);
}

public sealed class TargetMachine : IDisposable
{
    public LLVMTargetMachineRef Ref { get; }
    public LLVMTargetDataRef DataLayout { get; }
    public string Triple { get; }

    private TargetMachine(LLVMTargetMachineRef machine, string triple)
    {
        Ref = machine;
        Triple = triple;
        DataLayout = LLVMCreateTargetDataLayout(machine);
    }

    private static bool s_initialized;

    private static void InitializeTargets()
    {
        if (s_initialized) return;
        s_initialized = true;
        LLVMInitializeX86TargetInfo(); LLVMInitializeX86Target(); LLVMInitializeX86TargetMC(); LLVMInitializeX86AsmPrinter();
        try
        {
            LLVMInitializeAArch64TargetInfo(); LLVMInitializeAArch64Target(); LLVMInitializeAArch64TargetMC(); LLVMInitializeAArch64AsmPrinter();
        }
        catch (EntryPointNotFoundException) { /* LLVM built without AArch64 */ }
    }

    public static string HostTriple
    {
        get { InitializeTargets(); return TakeMessage(LLVMGetDefaultTargetTriple())!; }
    }

    public static TargetMachine Create(string? triple, LLVMCodeGenOptLevel level)
    {
        InitializeTargets();
        var host = triple is null;
        triple ??= HostTriple;
        if (LLVMGetTargetFromTriple(triple, out var target, out var error) != 0)
            throw new InvalidOperationException($"unknown target '{triple}': {TakeMessage(error)}");
        var cpu = host ? TakeMessage(LLVMGetHostCPUName())! : "generic";
        var features = host ? TakeMessage(LLVMGetHostCPUFeatures())! : "";
        var machine = LLVMCreateTargetMachine(target, triple, cpu, features, level, LLVMRelocMode.PIC, LLVMCodeModel.Default);
        return new TargetMachine(machine, triple);
    }

    public ulong SizeOf(LLVMTypeRef type) => LLVMABISizeOfType(DataLayout, type);

    public void Emit(LlvmModule module, string path, LLVMCodeGenFileType kind)
    {
        if (LLVMTargetMachineEmitToFile(Ref, module.Ref, path, kind, out var error) != 0)
            throw new InvalidOperationException($"LLVM code generation failed: {TakeMessage(error)}");
    }

    public void Dispose()
    {
        LLVMDisposeTargetData(DataLayout);
        LLVMDisposeTargetMachine(Ref);
    }
}
