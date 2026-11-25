using System.Reflection.Metadata;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Function pointers with an unmanaged calling convention (<c>delegate* unmanaged&lt;...&gt;</c>, IL <c>calli unmanaged</c>)
/// speak the platform's C ABI, as .NET marshals them like a P/Invoke: a <c>bool</c> is a 4-byte BOOL, a struct goes by value
/// the way the C compiler passes it (System V eightbytes, a byval copy, an sret result, AAPCS64 composites), small integers
/// are extended by the caller. Every other signature is already the C ABI under LLVM's default calling convention, so only
/// the signatures that need adapting get a stub:
///   - a call stub <c>R stub(ptr target, args...)</c> takes the managed representation and calls the pointer natively;
///   - an entry thunk is the address of an [UnmanagedCallersOnly] method (<c>&amp;M</c>), so native code, and the call stubs
///     above, reach it with the C ABI.
/// The classification is the P/Invoke stub's (ModuleCodeGen.PInvoke.cs), so a function pointer and a DllImport of the same
/// native function pass the same arguments.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<string, LoweredFunction> _nativeCallStubs = new();
    private readonly Dictionary<IMethodSymbol, LLVMValueRef> _nativeEntryThunks = new(SymbolEqualityComparer.Default);

    internal static bool IsUnmanagedConvention(SignatureCallingConvention convention) =>
        convention is SignatureCallingConvention.Unmanaged or SignatureCallingConvention.CDecl or SignatureCallingConvention.StdCall
            or SignatureCallingConvention.ThisCall or SignatureCallingConvention.FastCall;

    internal static bool IsUnmanagedCallersOnly(IMethodSymbol method) =>
        method.GetAttributes().Any(a => a.AttributeClass?.Name == "UnmanagedCallersOnlyAttribute");

    /// <summary>A function pointer type's native-ABI shape, or null when the managed ABI already is the native one.</summary>
    private sealed record NativeShape(List<NativeParam> Parameters, LLVMTypeRef NativeReturn, ParamKind ReturnKind, LLVMTypeRef ReturnStruct, bool Sret);

    private NativeShape? NativeShapeOf(IMethodSymbol signature)
    {
        if (signature.RefKind != RefKind.None) throw CompileError.Unsupported("an unmanaged function pointer that returns by reference");
        var adapt = signature.ReturnType.SpecialType == SpecialType.System_Boolean || IsStructByValue(signature.ReturnType)
            || signature.Parameters.Any(p => p.RefKind == RefKind.None && (p.Type.SpecialType == SpecialType.System_Boolean || IsStructByValue(p.Type)));
        // .NET marshals a char of such a pointer as an ANSI character (and a string, array or delegate with the P/Invoke
        // rules): not guessed here.
        foreach (var type in signature.Parameters.Where(p => p.RefKind == RefKind.None).Select(p => p.Type).Append(signature.ReturnType))
            if (type.SpecialType is SpecialType.System_Char or SpecialType.System_String || type is IArrayTypeSymbol || type.TypeKind == TypeKind.Delegate)
                throw CompileError.Unsupported($"'{type}' in the signature of an unmanaged function pointer or [UnmanagedCallersOnly] method (marshalled by .NET)");
        if (!adapt) return null;
        var parameters = signature.Parameters.Select(p => ClassifyParameter(p, unicodeDefault: false)).ToList();
        var (native, kind, structType, sret) = ClassifyReturn(signature, unicodeDefault: false);
        return new NativeShape(parameters, native, kind, structType, sret);
    }

    private static bool IsStructByValue(ITypeSymbol type) => type is INamedTypeSymbol { TypeKind: TypeKind.Struct } s && !IsPrimitiveLike(s);

    private LLVMTypeRef NativeFunctionType(NativeShape shape)
    {
        List<LLVMTypeRef> types = [];
        if (shape.Sret) types.Add(Llvm.Ptr);
        foreach (var p in shape.Parameters) types.AddRange(p.NativeTypes);
        return Llvm.FunctionType(shape.Sret ? Llvm.Void : shape.NativeReturn, [.. types]);
    }

    /// <summary>
    /// The function to call for an unmanaged function pointer of this signature, <c>R f(ptr target, managed args...)</c>, or
    /// null if a plain call through the pointer is already C-ABI correct (<see cref="NativeAttributes"/> still apply).
    /// </summary>
    internal LoweredFunction? NativeCallStub(IMethodSymbol signature)
    {
        if (NativeShapeOf(signature) is not { } shape) return null;
        var key = string.Join(",", signature.Parameters.Select(p => (p.RefKind == RefKind.None ? "" : "&") + IdentityKey(p.Type)))
            + "->" + IdentityKey(signature.ReturnType);
        if (_nativeCallStubs.TryGetValue(key, out var existing)) return existing;

        List<LLVMTypeRef> managed = [Llvm.Ptr];
        managed.AddRange(signature.Parameters.Select(p => p.RefKind != RefKind.None ? Llvm.Ptr : Types.Value(p.Type)));
        var managedType = Llvm.FunctionType(signature.ReturnsVoid ? Llvm.Void : Types.Value(signature.ReturnType), [.. managed]);
        var stub = Module.AddFunction("csharpc.nativecall." + _nativeCallStubs.Count, managedType);
        Native.LLVMSetLinkage(stub, LLVMLinkage.Internal);
        var lowered = _nativeCallStubs[key] = new LoweredFunction(stub, managedType);

        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(stub, "entry"));
        List<LLVMValueRef> args = [];
        LLVMValueRef sretSlot = default;
        if (shape.Sret) args.Add(sretSlot = b.Alloca(shape.ReturnStruct, "sret"));
        for (var i = 0; i < shape.Parameters.Count; i++)
        {
            var p = shape.Parameters[i];
            var value = stub.Param((uint)i + 1);
            switch (p.Kind)
            {
                case ParamKind.Direct or ParamKind.ByRef:
                    args.Add(value);
                    break;
                case ParamKind.Bool:
                    args.Add(b.Cast(LLVMOpcode.ZExt, value, p.NativeTypes[0]));
                    break;
                case ParamKind.Struct:
                {
                    var coerced = CoercedStruct(p.NativeTypes);
                    var slot = b.Alloca(LargerOf(p.StructType, coerced), "abi");
                    b.Store(value, slot);
                    for (var k = 0; k < p.NativeTypes.Length; k++)
                        args.Add(b.Load(p.NativeTypes[k], b.StructGep(coerced, slot, (uint)k)));
                    break;
                }
                case ParamKind.StructByVal:
                {
                    var slot = b.Alloca(p.StructType, "byval");
                    b.Store(value, slot);
                    args.Add(slot);
                    break;
                }
                default:
                    throw CompileError.Unsupported($"'{p.Type}' in the signature of an unmanaged function pointer");
            }
        }
        var nativeType = NativeFunctionType(shape);
        var result = b.Call(nativeType, stub.Param(0), [.. args]);
        foreach (var (index, attribute) in NativeAttributes(shape, signature)) Native.LLVMAddCallSiteAttribute(result, index, attribute);
        switch (shape.ReturnKind)
        {
            case ParamKind.Direct when signature.ReturnsVoid:
                b.RetVoid();
                break;
            case ParamKind.Direct:
                b.Ret(result);
                break;
            case ParamKind.Bool:
                b.Ret(b.ICmp(LLVMIntPredicate.NE, result, Llvm.ConstInt(shape.NativeReturn, 0)));
                break;
            case ParamKind.Struct:
            {
                var slot = b.Alloca(LargerOf(shape.ReturnStruct, shape.NativeReturn), "ret");
                b.Store(result, slot);
                b.Ret(b.Load(shape.ReturnStruct, slot));
                break;
            }
            case ParamKind.StructByVal:
                b.Ret(b.Load(shape.ReturnStruct, sretSlot));
                break;
            default:
                throw CompileError.Unsupported($"'{signature.ReturnType}' returned by an unmanaged function pointer");
        }
        return lowered;
    }

    /// <summary>ABI attributes (sret, byval, extension of small integers) of a native call of this shape; see <see cref="NativeAttributes(List{NativeParam}, ParamKind, IMethodSymbol, bool, LLVMTypeRef)"/>.</summary>
    private List<(uint Index, LLVMAttributeRef Attribute)> NativeAttributes(NativeShape shape, IMethodSymbol signature) =>
        NativeAttributes(shape.Parameters, shape.ReturnKind, signature, shape.Sret, shape.ReturnStruct);

    /// <summary>
    /// Call-site attributes for a direct call of a native-compatible signature: sub-int arguments and results are
    /// extended by the caller (zeroext/signext), as clang declares them, and callers of C code rely on it.
    /// </summary>
    internal List<(uint Index, LLVMAttributeRef Attribute)> NativeExtensionAttributes(IMethodSymbol signature)
    {
        List<(uint, LLVMAttributeRef)> attributes = [];
        LLVMAttributeRef Extension(ITypeSymbol type)
        {
            var name = TypeMapper.IsSigned(type) ? "signext" : "zeroext";
            return Native.LLVMCreateEnumAttribute(Llvm.Ref, Native.LLVMGetEnumAttributeKindForName(name, (nuint)name.Length), 0);
        }
        uint index = 1;
        foreach (var p in signature.Parameters)
        {
            if (p.RefKind == RefKind.None && Types.Value(p.Type) is { IsInteger: true, IntWidth: < 32 }) attributes.Add((index, Extension(p.Type)));
            index++;
        }
        if (!signature.ReturnsVoid && Types.Value(signature.ReturnType) is { IsInteger: true, IntWidth: < 32 }) attributes.Add((0, Extension(signature.ReturnType)));
        return attributes;
    }

    /// <summary>
    /// The address of an [UnmanagedCallersOnly] method: its managed function when the C ABI is the same, otherwise a
    /// function with the C signature that rebuilds the arguments and calls it (the inverse of a call stub).
    /// </summary>
    internal LLVMValueRef NativeEntryThunk(IMethodSymbol method, LoweredFunction target)
    {
        method = method.OriginalDefinition;
        if (_nativeEntryThunks.TryGetValue(method, out var existing)) return existing;
        if (NativeShapeOf(method) is not { } shape) return _nativeEntryThunks[method] = target.Fn;

        var nativeType = NativeFunctionType(shape);
        var thunk = Module.AddFunction(Mangler.Method(method) + "$native", nativeType);
        Native.LLVMSetLinkage(thunk, LLVMLinkage.Internal);
        _nativeEntryThunks[method] = thunk;
        foreach (var (index, attribute) in NativeAttributes(shape, method)) Native.LLVMAddAttributeAtIndex(thunk, index, attribute);

        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(thunk, "entry"));
        var next = shape.Sret ? 1u : 0u;
        List<LLVMValueRef> args = [];
        foreach (var p in shape.Parameters)
        {
            switch (p.Kind)
            {
                case ParamKind.Direct or ParamKind.ByRef:
                    args.Add(thunk.Param(next));
                    break;
                case ParamKind.Bool:
                    args.Add(b.ICmp(LLVMIntPredicate.NE, thunk.Param(next), Llvm.ConstInt(p.NativeTypes[0], 0)));
                    break;
                case ParamKind.Struct:
                {
                    var coerced = CoercedStruct(p.NativeTypes);
                    var slot = b.Alloca(LargerOf(p.StructType, coerced), "abi");
                    for (var k = 0; k < p.NativeTypes.Length; k++) b.Store(thunk.Param(next + (uint)k), b.StructGep(coerced, slot, (uint)k));
                    args.Add(b.Load(p.StructType, slot));
                    break;
                }
                case ParamKind.StructByVal:
                    args.Add(b.Load(p.StructType, thunk.Param(next)));
                    break;
                default:
                    throw CompileError.Unsupported($"'{p.Type}' in the signature of an [UnmanagedCallersOnly] method", method);
            }
            next += (uint)p.NativeTypes.Length;
        }
        var result = b.Call(target.Type, target.Fn, [.. args]);
        switch (shape.ReturnKind)
        {
            case ParamKind.Direct when method.ReturnsVoid:
                b.RetVoid();
                break;
            case ParamKind.Direct:
                b.Ret(result);
                break;
            case ParamKind.Bool:
                b.Ret(b.Cast(LLVMOpcode.ZExt, result, shape.NativeReturn));
                break;
            case ParamKind.Struct:
            {
                var slot = b.Alloca(LargerOf(shape.ReturnStruct, shape.NativeReturn), "ret");
                b.Store(result, slot);
                b.Ret(b.Load(shape.NativeReturn, slot));
                break;
            }
            case ParamKind.StructByVal:
                b.Store(result, thunk.Param(0));
                b.RetVoid();
                break;
            default:
                throw CompileError.Unsupported($"'{method.ReturnType}' returned by an [UnmanagedCallersOnly] method", method);
        }
        return thunk;
    }

    private readonly Dictionary<IMethodSymbol, LLVMValueRef> _libraryFunctionPointers = new(SymbolEqualityComparer.Default);

    /// <summary>The address of a thunk with the method's managed signature that calls the library method (`&amp;Math.Sqrt`).</summary>
    internal LLVMValueRef LibraryFunctionPointer(IMethodSymbol method, IOperation? at)
    {
        if (_libraryFunctionPointers.TryGetValue(method, out var existing)) return existing;
        if (!method.IsStatic || method.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter))
            throw CompileError.Unsupported($"function pointer to library method '{method}'", at);
        var parameters = method.Parameters.Select(p => p.RefKind == RefKind.None ? Types.Value(p.Type) : Llvm.Ptr).ToArray();
        var ret = method.RefKind != RefKind.None ? Llvm.Ptr : method.ReturnsVoid ? Llvm.Void : Types.Value(method.ReturnType);
        try
        {
            var thunk = SyntheticFunction(Mangler.Method(method) + ".fnptr", Llvm.FunctionType(ret, parameters), (g, syntax) => g.EmitLibraryMethodThunk(method, syntax));
            return _libraryFunctionPointers[method] = thunk;
        }
        catch (CompileError e) when (at is not null)
        {
            // Reported where the pointer is taken, not at the synthetic call.
            throw new CompileError(e.Id, e.Message, at?.Syntax.GetLocation());
        }
    }
}
