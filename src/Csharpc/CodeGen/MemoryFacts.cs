using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using static Csharpc.Llvm.Native;

namespace Csharpc.CodeGen;

/// <summary>
/// Metadata that tells LLVM what the CLR object model guarantees about memory, so bounds checks and
/// length reloads can be optimized away (docs/03-lowering.md, "Arrays"):
///  * array and string lengths are fixed at allocation and bounded (Array.MaxLength, the string
///    limit): `!range` + `!noundef` on their loads, so index arithmetic checked against a length is
///    known not to overflow (nsw/nuw inference, induction-variable widening);
///  * nothing a C# program writes through a field, an array element or a span element can be an
///    array's or a string's length: distinct TBAA tags for "array length", "string length" and
///    "managed data", so stores in a loop don't force the length (and its bounds check) to be reloaded.
///    RyuJIT makes the same assumption (array lengths are invariant memory);
///  * "managed data" has two disjoint children: fields of class instances and array elements live in
///    different heap objects, so storing an element never changes a field (`_items[i] = x` keeps
///    `_items` and `_size` in registers). Each class field gets its own tag below "object field":
///    instance layouts are sequential (no explicit layout), so two different fields never share
///    memory. Everything that may overlap fields and elements stays on the parent tag:
///    struct fields (a struct can be an array element) and span elements (a span can view a field).
///    String chars are a third child (strings are neither arrays nor field-holding objects).
/// The tags live under csharpc's own TBAA root. Accesses from the C runtime use clang's root and
/// untagged accesses (pointers, refs, runtime helpers) carry no tag, so LLVM treats both as aliasing
/// everything; only csharpc-tagged pairs with different tags are assumed disjoint.
/// </summary>
public sealed class MemoryFacts(LlvmContext llvm)
{
    /// <summary>Array.MaxLength (csrt.h CSRT_MAX_ARRAY_LENGTH); every array allocation rejects anything longer.</summary>
    public const long MaxArrayLength = 0x7FFFFFC7;
    /// <summary>CSRT_MAX_STRING_LENGTH; csrt_string_alloc_* reject anything longer.</summary>
    public const long MaxStringLength = 0x3FFFFFDF;

    private uint? _tbaaKind, _rangeKind, _noundefKind;
    private LLVMMetadataRef _root, _dataType, _fieldType;
    private readonly Dictionary<IFieldSymbol, LLVMValueRef> _fieldTags = new(SymbolEqualityComparer.Default);
    private LLVMValueRef _arrayLengthTag, _stringLengthTag, _dataTag, _elementTag, _stringCharTag, _arrayLengthRange, _stringLengthRange, _empty;

    private uint Kind(ref uint? cache, string name) => cache ??= LLVMGetMDKindIDInContext(llvm.Ref, name, (uint)name.Length);

    private LLVMMetadataRef Node(params LLVMMetadataRef[] operands) => LLVMMDNodeInContext2(llvm.Ref, operands, (nuint)operands.Length);
    private LLVMMetadataRef String(string s) => LLVMMDStringInContext2(llvm.Ref, s, (nuint)s.Length);
    private LLVMMetadataRef Int(long value, LLVMTypeRef? type = null) => LLVMValueAsMetadata(llvm.ConstInt(type ?? llvm.I64, value));
    private LLVMValueRef AsValue(LLVMMetadataRef md) => LLVMMetadataAsValue(llvm.Ref, md);

    private LLVMMetadataRef Root => _root.IsNull ? _root = Node(String("csharpc TBAA")) : _root;
    private LLVMMetadataRef DataType => _dataType.IsNull ? _dataType = Node(String("managed data"), Root, Int(0)) : _dataType;

    /// <summary>Scalar access tag (base type == access type, offset 0) for <paramref name="type"/>.</summary>
    private LLVMValueRef ScalarTag(LLVMMetadataRef type) => AsValue(Node(type, type, Int(0)));

    private void SetTbaa(LLVMValueRef access, ref LLVMValueRef tag, Func<LLVMMetadataRef> type)
    {
        if (tag.Handle == 0) tag = ScalarTag(type());
        LLVMSetMetadata(access, Kind(ref _tbaaKind, "tbaa"), tag);
    }

    private void SetTbaa(LLVMValueRef access, ref LLVMValueRef tag, string name, bool underData = false) =>
        SetTbaa(access, ref tag, () => Node(String(name), underData ? DataType : Root, Int(0)));

    private void SetRange(LLVMValueRef load, ref LLVMValueRef range, LLVMTypeRef type, long max)
    {
        if (range.Handle == 0) range = AsValue(Node(Int(0, type), Int(max + 1, type)));
        LLVMSetMetadata(load, Kind(ref _rangeKind, "range"), range);
        if (_empty.Handle == 0) _empty = AsValue(Node());
        LLVMSetMetadata(load, Kind(ref _noundefKind, "noundef"), _empty);
    }

    /// <summary>An i64 load of an array's (total) length.</summary>
    public void ArrayLengthLoad(LLVMValueRef load)
    {
        SetTbaa(load, ref _arrayLengthTag, "array length");
        SetRange(load, ref _arrayLengthRange, llvm.I64, MaxArrayLength);
    }

    /// <summary>An i32 load of string.Length.</summary>
    public void StringLengthLoad(LLVMValueRef load)
    {
        SetTbaa(load, ref _stringLengthTag, "string length");
        SetRange(load, ref _stringLengthRange, llvm.I32, MaxStringLength);
    }

    /// <summary>A load of a string's char (also "managed data": the corelib fills new strings through spans).</summary>
    public void StringCharLoad(LLVMValueRef load) => SetTbaa(load, ref _stringCharTag, "string char", underData: true);

    /// <summary>A load or store of a field, an array element or a span element.</summary>
    public void DataAccess(LLVMValueRef access, DataKind kind, IFieldSymbol? field)
    {
        switch (kind)
        {
            case DataKind.ObjectField when field is not null:
                if (!_fieldTags.TryGetValue(field, out var tag))
                {
                    if (_fieldType.IsNull) _fieldType = Node(String("object field"), DataType, Int(0));
                    _fieldTags[field] = tag = ScalarTag(Node(String($"field {field.ContainingType.ToDisplayString()}.{field.Name}"), _fieldType, Int(0)));
                }
                LLVMSetMetadata(access, Kind(ref _tbaaKind, "tbaa"), tag);
                break;
            case DataKind.ArrayElement: SetTbaa(access, ref _elementTag, "array element", underData: true); break;
            default: SetTbaa(access, ref _dataTag, () => DataType); break;
        }
    }
}

/// <summary>What a tagged address points into (<see cref="MemoryFacts.DataAccess"/>).</summary>
public enum DataKind
{
    /// <summary>May be a field or an element: struct fields, span elements.</summary>
    Any,
    /// <summary>A field of a class instance (tagged per field).</summary>
    ObjectField,
    /// <summary>An element of an array (any rank).</summary>
    ArrayElement,
}
