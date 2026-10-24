using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<INamedTypeSymbol, (LLVMValueRef Values, LLVMValueRef Names, int Count)> _enumTables = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Members as (raw unsigned value within the underlying width, name), in the order of .NET's
    /// Enum.GetValues/GetNames: declaration order sorted by Array.Sort(values, names), an introsort that is
    /// stable up to 16 members but not beyond (TypeAttributes lists AnsiClass before Class), so aliases
    /// of larger enums land where .NET puts them.
    /// </summary>
    public List<(ulong Raw, string Name)> EnumMembers(INamedTypeSymbol enumType)
    {
        var width = (int)Types.Value(enumType.EnumUnderlyingType!).IntWidth;
        var members = DeclarationOrder(enumType.OriginalDefinition)
            .Select(f => (Raw: f.ConstantValue switch { ulong u => u, uint u => u, ushort u => u, byte u => u, var v => unchecked((ulong)Convert.ToInt64(v)) }, f.Name))
            .Select(m => (Raw: width == 64 ? m.Raw : m.Raw & ((1UL << width) - 1), m.Name))
            .ToArray();
        DotNetIntroSort.Sort(members);
        return [.. members];
    }

    /// <summary>
    /// The name .NET gives <paramref name="raw"/> when several members share it. Enum.GetName and
    /// non-[Flags] ToString search the sorted values linearly up to 32 members (the first one wins) and
    /// binary-search larger enums; [Flags] ToString takes the first member for 0 and otherwise scans from
    /// the end (the last one wins).
    /// </summary>
    public static string EnumNameOf(List<(ulong Raw, string Name)> sorted, ulong raw, bool flags)
    {
        if (flags) return raw == 0 ? sorted.First(m => m.Raw == raw).Name : sorted.Last(m => m.Raw == raw).Name;
        if (sorted.Count <= 32) return sorted.First(m => m.Raw == raw).Name;
        int lo = 0, hi = sorted.Count - 1;
        while (lo <= hi)
        {
            var i = (int)(((uint)hi + (uint)lo) >> 1);
            var c = raw.CompareTo(sorted[i].Raw);
            if (c == 0) return sorted[i].Name;
            if (c > 0) lo = i + 1; else hi = i - 1;
        }
        throw new InvalidOperationException($"enum value {raw} not found");
    }

    /// <summary>ArraySortHelper&lt;TKey, TValue&gt;.IntrospectiveSort of .NET (keys: the raw values).</summary>
    private static class DotNetIntroSort
    {
        public static void Sort((ulong Raw, string Name)[] items)
        {
            if (items.Length > 1) IntroSort(items.AsSpan(), 2 * (System.Numerics.BitOperations.Log2((uint)items.Length) + 1));
        }

        private static void Swap(Span<(ulong Raw, string Name)> a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);

        private static void SwapIfGreater(Span<(ulong Raw, string Name)> a, int i, int j)
        {
            if (a[i].Raw > a[j].Raw) Swap(a, i, j);
        }

        private static void IntroSort(Span<(ulong Raw, string Name)> a, int depthLimit)
        {
            var size = a.Length;
            while (size > 1)
            {
                if (size <= 16)
                {
                    if (size == 2) { SwapIfGreater(a, 0, 1); return; }
                    if (size == 3) { SwapIfGreater(a, 0, 1); SwapIfGreater(a, 0, 2); SwapIfGreater(a, 1, 2); return; }
                    InsertionSort(a[..size]);
                    return;
                }
                if (depthLimit == 0) { HeapSort(a[..size]); return; }
                depthLimit--;
                var p = PickPivotAndPartition(a[..size]);
                IntroSort(a[(p + 1)..size], depthLimit);
                size = p;
            }
        }

        private static int PickPivotAndPartition(Span<(ulong Raw, string Name)> a)
        {
            var hi = a.Length - 1;
            var middle = hi >> 1;
            SwapIfGreater(a, 0, middle);
            SwapIfGreater(a, 0, hi);
            SwapIfGreater(a, middle, hi);
            var pivot = a[middle].Raw;
            Swap(a, middle, hi - 1);
            int left = 0, right = hi - 1;
            while (left < right)
            {
                while (pivot > a[++left].Raw) { }
                while (pivot < a[--right].Raw) { }
                if (left >= right) break;
                Swap(a, left, right);
            }
            if (left != hi - 1) Swap(a, left, hi - 1);
            return left;
        }

        private static void HeapSort(Span<(ulong Raw, string Name)> a)
        {
            var n = a.Length;
            for (var i = n >> 1; i >= 1; i--) DownHeap(a, i, n);
            for (var i = n; i > 1; i--)
            {
                Swap(a, 0, i - 1);
                DownHeap(a, 1, i - 1);
            }
        }

        private static void DownHeap(Span<(ulong Raw, string Name)> a, int i, int n)
        {
            var d = a[i - 1];
            while (i <= n >> 1)
            {
                var child = 2 * i;
                if (child < n && a[child - 1].Raw < a[child].Raw) child++;
                if (!(d.Raw < a[child - 1].Raw)) break;
                a[i - 1] = a[child - 1];
                i = child;
            }
            a[i - 1] = d;
        }

        private static void InsertionSort(Span<(ulong Raw, string Name)> a)
        {
            for (var i = 0; i < a.Length - 1; i++)
            {
                var t = a[i + 1];
                var j = i;
                while (j >= 0 && t.Raw < a[j].Raw)
                {
                    a[j + 1] = a[j];
                    j--;
                }
                a[j + 1] = t;
            }
        }
    }

    /// <summary>
    /// An enum's constants in declaration order, which decides which alias a value formats as. Enums bound
    /// from a reference assembly take the order of the shared framework's implementation assembly.
    /// </summary>
    private static IEnumerable<IFieldSymbol> DeclarationOrder(INamedTypeSymbol enumType)
    {
        var fields = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToList();
        if (enumType.ContainingType is null && enumType.ContainingAssembly is { } assembly && enumType.Locations.All(l => l.IsInMetadata)
            && Frontend.SharedFramework.FieldOrder(assembly.Name, enumType.ContainingNamespace.ToDisplayString(), enumType.MetadataName) is { } order)
        {
            var rank = order.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i, StringComparer.Ordinal);
            if (fields.All(f => rank.ContainsKey(f.Name))) return fields.OrderBy(f => rank[f.Name]);
        }
        return fields;
    }

    /// <summary>
    /// All members of an enum (aliases included), stably sorted by unsigned value like .NET's
    /// Enum.GetValues/GetNames: { u64 values[], string names[] } constant tables.
    /// </summary>
    public (LLVMValueRef Values, LLVMValueRef Names, int Count) EnumTable(INamedTypeSymbol enumType)
    {
        enumType = enumType.OriginalDefinition;
        if (_enumTables.TryGetValue(enumType, out var existing)) return existing;
        var members = EnumMembers(enumType);
        var valuesInit = LlvmContext.ConstArray(Llvm.I64, [.. members.Select(m => Llvm.ConstUInt(Llvm.I64, m.Raw))]);
        var namesInit = LlvmContext.ConstArray(Llvm.Ptr, [.. members.Select(m => StringLiteral(m.Name))]);
        var values = Module.AddGlobal(Mangler.TypeSymbol(enumType, "EnumValues"), Native.LLVMTypeOf(valuesInit), valuesInit, constant: true, linkage: LLVMLinkage.Internal);
        var names = Module.AddGlobal(Mangler.TypeSymbol(enumType, "EnumNames"), Native.LLVMTypeOf(namesInit), namesInit, constant: true, linkage: LLVMLinkage.Internal);
        return _enumTables[enumType] = (values, names, members.Count);
    }

    private readonly Dictionary<INamedTypeSymbol, LoweredFunction> _enumFormatWith = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// string EnumFormat(T value, string format): Enum.ToString(format) and "{e:X}". "G" (or no format)
    /// is the name formatter (EnumFormatter); D/X/F go through csrt_enum_format.
    /// </summary>
    public LoweredFunction EnumFormatWith(INamedTypeSymbol enumType)
    {
        enumType = enumType.OriginalDefinition;
        if (_enumFormatWith.TryGetValue(enumType, out var existing)) return existing;
        var underlying = enumType.EnumUnderlyingType!;
        var valueType = Types.Value(underlying);
        var signed = TypeMapper.IsSigned(underlying);
        var type = Llvm.FunctionType(Llvm.Ptr, valueType, Llvm.Ptr);
        var fn = Module.AddFunction(Mangler.TypeSymbol(enumType, "ToStringFormat"), type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        var lowered = _enumFormatWith[enumType] = new LoweredFunction(fn, type);

        var (values, names, count) = EnumTable(enumType);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        var value = fn.Param(0);
        var asU64 = valueType.IntWidth == 64 ? value : b.Cast(LLVMOpcode.ZExt, value, Llvm.I64);
        var asI64 = valueType.IntWidth == 64 ? value : b.Cast(signed ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, Llvm.I64);
        var format = Runtime("csrt_enum_format", Llvm.Ptr, Llvm.I64, Llvm.I64, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.I1);
        var formatted = b.Call(format.Type, format.Fn, [asU64, asI64, fn.Param(1), values, names,
            Llvm.ConstInt(Llvm.I32, count), Llvm.ConstInt(Llvm.I32, valueType.IntWidth), signed ? Llvm.True : Llvm.False]);
        var byName = b.AppendBlock(fn, "general");
        var done = b.AppendBlock(fn, "done");
        b.CondBr(b.ICmp(LLVMIntPredicate.EQ, formatted, LlvmContext.Null(Llvm.Ptr)), byName, done);
        b.PositionAtEnd(done);
        b.Ret(formatted);
        b.PositionAtEnd(byName);
        var general = EnumFormatter(enumType);
        b.Ret(b.Call(general.Type, general.Fn, [value]));
        return lowered;
    }
    /// <summary>Enum.GetHashCode(): the underlying integer's GetHashCode (small types widen, 64-bit folds lo ^ hi).</summary>
    public LLVMValueRef EnumHash(IrBuilder b, LLVMValueRef value, INamedTypeSymbol enumType)
    {
        var underlying = enumType.EnumUnderlyingType!;
        var width = Types.Value(underlying).IntWidth;
        if (width < 32) return b.Cast(TypeMapper.IsSigned(underlying) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, Llvm.I32);
        if (width == 32) return value;
        var high = b.Cast(LLVMOpcode.Trunc, b.BinOp(LLVMOpcode.LShr, value, Llvm.ConstInt(Llvm.I64, 32)), Llvm.I32);
        return b.BinOp(LLVMOpcode.Xor, b.Cast(LLVMOpcode.Trunc, value, Llvm.I32), high);
    }

    private List<INamedTypeSymbol>? _jsonEnumTypes;

    /// <summary>
    /// The enums a run-time Type can name in System.Text.Json: every enum declared in the program, and
    /// library enums the generated JSON metadata converts (GetEnumConverter&lt;E&gt;, JsonStringEnumConverter&lt;E&gt;).
    /// </summary>
    public List<INamedTypeSymbol> JsonEnumTypes()
    {
        if (_jsonEnumTypes is { } cached) return cached;
        var result = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        void Visit(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    if (ns.Name == CoreLib.Namespace && ns.ContainingNamespace.IsGlobalNamespace) continue;
                    Visit(ns);
                }
                else if (member is INamedTypeSymbol type)
                {
                    if (type.TypeKind == TypeKind.Enum && seen.Add(type)) result.Add(type);
                    if (!type.IsGenericType) Visit(type);
                }
            }
        }
        Visit(Compilation.Assembly.GlobalNamespace);
        foreach (var tree in Compilation.SyntaxTrees)
        {
            if (!tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal)) continue;
            var text = tree.GetText().ToString();
            if (!text.Contains("GetEnumConverter<", StringComparison.Ordinal) && !text.Contains("EnumConverter<", StringComparison.Ordinal)) continue;
            var model = Compilation.GetSemanticModel(tree);
            foreach (var generic in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax>())
            {
                if (generic.Identifier.ValueText is not ("GetEnumConverter" or "JsonStringEnumConverter" or "JsonNumberEnumConverter")) continue;
                foreach (var arg in generic.TypeArgumentList.Arguments)
                    if (model.GetTypeInfo(arg).Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } e && seen.Add(e)) result.Add(e);
            }
        }
        return _jsonEnumTypes = result;
    }
}
