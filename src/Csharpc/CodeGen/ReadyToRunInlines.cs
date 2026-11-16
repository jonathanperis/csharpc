using System.Collections.Concurrent;
using System.Reflection.PortableExecutable;

namespace Csharpc.CodeGen;

/// <summary>
/// Which methods a ReadyToRun framework assembly's precompiled code inlined into which: the image's inlining table
/// (READYTORUN_SECTION_CROSS_MODULE_INLINE_INFO, 119, or INLINING_INFO2, 114), as the runtime reads it for the profiler
/// (src/coreclr/vm/inlinetracking.cpp; written by ILCompiler.ReadyToRun's InliningInfoNode). .NET runs the framework from
/// that precompiled code, so a method inlined there has no frame of its own in a stack trace: the trace goes from the
/// inlinee's callee straight to the method it was inlined into (the root method compiled, however deep the inlining).
/// Only inlines within the assembly are read (an inlinee or inliner of another module is skipped).
/// </summary>
internal static class ReadyToRunInlines
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, int[]>> s_cache = new(StringComparer.Ordinal);

    /// <summary>Inlinee method RID → the RIDs of the methods it was inlined into; empty when the file has no such table.</summary>
    public static IReadOnlyDictionary<int, int[]> Of(string path) => s_cache.GetOrAdd(path, Read);

    private const uint Signature = 0x00525452; // "RTR"
    private const uint FlagMultiModuleVersionBubble = 0x40;
    private const int SectionInliningInfo2 = 114, SectionCrossModuleInlineInfo = 119;

    private static IReadOnlyDictionary<int, int[]> Read(string path)
    {
        var result = new Dictionary<int, int[]>();
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var pe = new PEReader(new MemoryStream(bytes));
            var native = pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory ?? default;
            if (native.Size == 0 || !pe.PEHeaders.TryGetDirectoryOffset(native, out int header)) return result;
            if (BitConverter.ToUInt32(bytes, header) != Signature) return result;
            var flags = BitConverter.ToUInt32(bytes, header + 8);
            var count = BitConverter.ToInt32(bytes, header + 12);
            for (var i = 0; i < count; i++)
            {
                var entry = header + 16 + i * 12;
                var type = BitConverter.ToInt32(bytes, entry);
                if (type is not (SectionInliningInfo2 or SectionCrossModuleInlineInfo)) continue;
                var directory = new DirectoryEntry(BitConverter.ToInt32(bytes, entry + 4), BitConverter.ToInt32(bytes, entry + 8));
                if (!pe.PEHeaders.TryGetDirectoryOffset(directory, out int start)) continue;
                var section = new ReadOnlySpan<byte>(bytes, start, directory.Size).ToArray();
                if (type == SectionCrossModuleInlineInfo) ReadCrossModule(section, (flags & FlagMultiModuleVersionBubble) != 0, result);
                else ReadInliningInfo2(section, result);
            }
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or ArgumentOutOfRangeException or IndexOutOfRangeException or ArgumentException)
        {
            result.Clear(); // not a ReadyToRun image we can read: no frame is hidden
        }
        return result;
    }

    // Native format (src/coreclr/vm/nativeformatreader.h): unsigned integers in 1 to 5 bytes, the low bits of the first saying how many.
    private static uint Unsigned(byte[] s, ref int p)
    {
        uint b = s[p];
        if ((b & 1) == 0) { p += 1; return b >> 1; }
        if ((b & 2) == 0) { var v = (b >> 2) | ((uint)s[p + 1] << 6); p += 2; return v; }
        if ((b & 4) == 0) { var v = (b >> 3) | ((uint)s[p + 1] << 5) | ((uint)s[p + 2] << 13); p += 3; return v; }
        if ((b & 8) == 0) { var v = (b >> 4) | ((uint)s[p + 1] << 4) | ((uint)s[p + 2] << 12) | ((uint)s[p + 3] << 20); p += 4; return v; }
        if ((b & 16) == 0) { var v = BitConverter.ToUInt32(s, p + 1); p += 5; return v; }
        throw new BadImageFormatException("ReadyToRun inlining table: bad integer");
    }

    /// <summary>Every entry of a NativeHashtable at the start of the section (AllEntriesEnumerator): the offset of each entry's data.</summary>
    private static IEnumerable<int> Entries(byte[] s)
    {
        int header = s[0], baseOffset = 1, buckets = 1 << (header >> 2), indexSize = header & 3;
        if (indexSize > 2) throw new BadImageFormatException("ReadyToRun inlining table: bad hashtable");
        int Bucket(int b) => indexSize switch
        {
            0 => s[baseOffset + b],
            1 => BitConverter.ToUInt16(s, baseOffset + 2 * b),
            _ => (int)BitConverter.ToUInt32(s, baseOffset + 4 * b),
        };
        for (var b = 0; b < buckets; b++)
        {
            int p = baseOffset + Bucket(b), end = baseOffset + Bucket(b + 1);
            while (p < end)
            {
                p++; // the hash code's low byte
                var at = p;
                var delta = (int)Unsigned(s, ref p); // GetRelativeOffset: relative to where the offset is stored
                yield return at + delta;
            }
        }
    }

    private static void Add(Dictionary<int, int[]> result, int inlinee, List<int> inliners)
    {
        if (inliners.Count == 0) return;
        result[inlinee] = result.TryGetValue(inlinee, out var known) ? [.. known, .. inliners] : [.. inliners];
    }

    /// <summary>CrossModulePersistentInlineTrackingMapR2R: { count, inlinee index &lt;&lt; 2 | flags, [module], [cross-module inliners], inliner RIDs as deltas }.</summary>
    private static void ReadCrossModule(byte[] s, bool multiModule, Dictionary<int, int[]> result)
    {
        foreach (var entry in Entries(s))
        {
            var p = entry;
            var size = (int)Unsigned(s, ref p);
            var inlinee = Unsigned(s, ref p); size--;
            bool crossModuleInlinee = (inlinee & 1) != 0, hasCrossModuleInliners = (inlinee & 2) != 0;
            if (crossModuleInlinee) continue; // an IL body of another module
            var foreign = false;
            if (multiModule) { foreign = Unsigned(s, ref p) != 0; size--; }
            if (hasCrossModuleInliners)
            {
                var n = (int)Unsigned(s, ref p); size--;
                for (var i = 0; i < n; i++) { Unsigned(s, ref p); size--; }
            }
            var inliners = new List<int>();
            uint rid = 0;
            while (size > 0)
            {
                var value = Unsigned(s, ref p); size--;
                var other = false;
                if (multiModule)
                {
                    rid += value >> 1;
                    if ((value & 1) != 0) { Unsigned(s, ref p); size--; other = true; }
                }
                else rid += value;
                if (!other) inliners.Add((int)rid);
            }
            if (!foreign) Add(result, (int)(inlinee >> 2), inliners);
        }
    }

    /// <summary>PersistentInlineTrackingMapR2R2: { count, inlinee RID &lt;&lt; 1 | foreign, [module], inliner RID deltas &lt;&lt; 1 | foreign, [module] }.</summary>
    private static void ReadInliningInfo2(byte[] s, Dictionary<int, int[]> result)
    {
        foreach (var entry in Entries(s))
        {
            var p = entry;
            var size = (int)Unsigned(s, ref p);
            var inlinee = Unsigned(s, ref p); size--;
            var foreign = (inlinee & 1) != 0;
            if (foreign) { Unsigned(s, ref p); size--; }
            var inliners = new List<int>();
            uint rid = 0;
            while (size > 0)
            {
                var value = Unsigned(s, ref p); size--;
                rid += value >> 1;
                if ((value & 1) != 0) { Unsigned(s, ref p); size--; continue; }
                inliners.Add((int)rid);
            }
            if (!foreign) Add(result, (int)(inlinee >> 1), inliners);
        }
    }
}
