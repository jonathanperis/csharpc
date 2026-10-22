using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Method tables for iterator objects (docs/10-async.md#iterators). Each element type gets one,
/// with a real interface map — IEnumerable&lt;T&gt;, IEnumerable, IEnumerator&lt;T&gt;, IEnumerator,
/// IDisposable (async: IAsyncEnumerable&lt;T&gt;, IAsyncEnumerator&lt;T&gt;, IAsyncDisposable) — whose
/// slots are small thunks over the iterator runtime (runtime/async.c). So foreach, LINQ-style
/// helpers and any code holding an IEnumerable&lt;T&gt; reach iterators through ordinary interface
/// dispatch.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private const int IteratorMethodTableFlag = 4; // CSRT_MT_ITERATOR (runtime/csrt.h)
    private readonly Dictionary<(ITypeSymbol Element, bool Async), LLVMValueRef> _iteratorTables =
        new(new IteratorKeyComparer());
    private int _iteratorTableCounter;

    public LLVMValueRef IteratorMethodTable(ITypeSymbol element, bool isAsync)
    {
        if (_iteratorTables.TryGetValue((element, isAsync), out var existing)) return existing;
        var id = _iteratorTableCounter++;

        List<INamedTypeSymbol> interfaces = isAsync
            ?
            [
                Constructed("System.Collections.Generic.IAsyncEnumerable`1", element),
                Constructed("System.Collections.Generic.IAsyncEnumerator`1", element),
                (INamedTypeSymbol)Compilation.GetTypeByMetadataName("System.IAsyncDisposable")!,
            ]
            :
            [
                Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T).Construct(element),
                Compilation.GetSpecialType(SpecialType.System_Collections_IEnumerable),
                Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerator_T).Construct(element),
                Compilation.GetSpecialType(SpecialType.System_Collections_IEnumerator),
                Compilation.GetSpecialType(SpecialType.System_IDisposable),
            ];

        var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
        List<LLVMValueRef> entries = [];
        foreach (var itf in interfaces)
        {
            var slots = InterfaceSlots(itf).Select(member => IteratorSlot(member, element, id)).ToArray();
            var slotsInit = LlvmContext.ConstArray(Llvm.Ptr, slots);
            var table = Module.AddGlobal($"csharpc.iterator{id}.itbl{entries.Count}", Native.LLVMTypeOf(slotsInit), slotsInit,
                constant: true, linkage: LLVMLinkage.Internal);
            entries.Add(LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(itf), table));
        }
        var mapInit = LlvmContext.ConstArray(entryType, [.. entries]);
        var map = Module.AddGlobal($"csharpc.iterator{id}.imap", Native.LLVMTypeOf(mapInit), mapInit, constant: true, linkage: LLVMLinkage.Internal);

        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var vtable = defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn).ToArray();
        var name = $"<{(isAsync ? "async " : "")}iterator of {element.ToDisplayString()}>";
        var init = Llvm.ConstStruct(
            ExternalGlobal("csrt_Object_MT"), CString(name),
            Llvm.ConstInt(Llvm.I32, 48), Llvm.ConstInt(Llvm.I32, IteratorMethodTableFlag),
            map, Llvm.ConstInt(Llvm.I32, entries.Count), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, vtable));
        var mt = Module.AddGlobal($"csharpc.iterator{id}.MT", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        Native.LLVMSetAlignment(mt, 8);
        return _iteratorTables[(element, isAsync)] = mt;
    }

    private INamedTypeSymbol Constructed(string metadataName, ITypeSymbol element) =>
        (Compilation.GetTypeByMetadataName(metadataName)
         ?? throw new CompileError("CSLLVM0005", $"reference assemblies lack '{metadataName}'", null)).Construct(element);

    /// <summary>One interface member of an iterator object, as a thunk with the member's signature.</summary>
    private LLVMValueRef IteratorSlot(IMethodSymbol member, ITypeSymbol element, int id)
    {
        var type = FunctionTypeOf(member);
        var fn = Module.AddFunction($"csharpc.iterator{id}.{member.ContainingType.Name}.{member.Name}", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        var self = fn.Param(0);
        LLVMValueRef Call(string runtime, LLVMTypeRef ret)
        {
            var f = Runtime(runtime, ret, Llvm.Ptr);
            return b.Call(f.Type, f.Fn, [self]);
        }
        switch (member.Name)
        {
            case "GetEnumerator" or "GetAsyncEnumerator":
                b.Ret(Call("csrt_iterator_get_enumerator", Llvm.Ptr));
                break;
            case "MoveNext":
                b.Ret(Call("csrt_iterator_move_next", Llvm.I1));
                break;
            case "MoveNextAsync":
                b.Ret(Call("csrt_async_iterator_move_next", Llvm.Ptr));
                break;
            case "DisposeAsync":
                b.Ret(Call("csrt_async_iterator_dispose", Llvm.Ptr));
                break;
            case "Dispose":
                Call("csrt_iterator_dispose", Llvm.Void);
                b.RetVoid();
                break;
            case "Reset":
                Call("csrt_iterator_reset", Llvm.Void);
                b.Unreachable();
                break;
            case "get_Current":
            {
                var payload = Call("csrt_iterator_current_ptr", Llvm.Ptr);
                var value = b.Load(Types.Storage(element), payload);
                if (member.ReturnType.SpecialType == SpecialType.System_Object && element.IsValueType)
                    value = BoxInThunk(b, value, element);
                else if (element.SpecialType == SpecialType.System_Boolean)
                    value = b.Cast(LLVMOpcode.Trunc, value, Llvm.I1);
                b.Ret(value);
                break;
            }
            default:
                throw CompileError.Unsupported($"iterator member '{member}'", member);
        }
        return fn;
    }

    /// <summary>Boxes a storage-typed value (non-generic IEnumerator.Current over a value-type iterator).</summary>
    private LLVMValueRef BoxInThunk(IrBuilder b, LLVMValueRef value, ITypeSymbol type)
    {
        LLVMValueRef mt = type switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Struct } s when IsUserType(s) => StructMethodTable(s),
            { SpecialType: not SpecialType.None } => ExternalGlobal($"csrt_Boxed_{type.MetadataName}_MT"),
            _ => throw CompileError.Unsupported($"non-generic enumeration of an iterator of '{type}'", type),
        };
        var alloc = Runtime("csrt_alloc", Llvm.Ptr, Llvm.Ptr, Llvm.I64);
        var box = b.Call(alloc.Type, alloc.Fn, [mt, Llvm.ConstInt(Llvm.I64, 8 + (long)Types.Target.SizeOf(Types.Storage(type)))]);
        b.Store(value, b.InBoundsGep(Llvm.I8, box, [Llvm.ConstInt(Llvm.I64, 8)]));
        return box;
    }

    private sealed class IteratorKeyComparer : IEqualityComparer<(ITypeSymbol Element, bool Async)>
    {
        public bool Equals((ITypeSymbol Element, bool Async) x, (ITypeSymbol Element, bool Async) y) =>
            x.Async == y.Async && SymbolEqualityComparer.Default.Equals(x.Element, y.Element);
        public int GetHashCode((ITypeSymbol Element, bool Async) key) =>
            HashCode.Combine(SymbolEqualityComparer.Default.GetHashCode(key.Element), key.Async);
    }
}
