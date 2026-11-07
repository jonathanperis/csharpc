using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Driver;

/// <summary>
/// The bitcode of the whole program, shared by the partitions of ParallelCodegen and ParallelOptimize.
/// It stays in the native buffer LLVM wrote it into (a managed copy of a large program's image is
/// another few hundred megabytes on top of the module the partitions are about to parse, and
/// a managed array is limited to 2 GB), and each partition parses it into a context of its own.
/// </summary>
internal sealed unsafe class BitcodeImage : IDisposable
{
    private LLVMMemoryBufferRef _buffer;

    private BitcodeImage(LLVMMemoryBufferRef buffer) => _buffer = buffer;

    public static BitcodeImage Write(LLVMModuleRef module) => new(LLVMWriteBitcodeToMemoryBuffer(module));

    public long Size => (long)LLVMGetBufferSize(_buffer);

    /// <summary>Parses the whole image into <paramref name="context"/>; <paramref name="what"/> names the step in the error.</summary>
    public LLVMModuleRef Parse(LLVMContextRef context, string what)
    {
        var range = LLVMCreateMemoryBufferWithMemoryRange(LLVMGetBufferStart(_buffer), LLVMGetBufferSize(_buffer), "partition", 0);
        LLVMModuleRef m;
        try
        {
            if (LLVMParseBitcodeInContext2(context, range, out m) != 0)
                throw new InvalidOperationException($"internal compiler error: csharpc could not re-read its own bitcode for {what}");
        }
        finally { LLVMDisposeMemoryBuffer(range); }
        return m;
    }

    public void Dispose()
    {
        if (_buffer.Handle == 0) return;
        LLVMDisposeMemoryBuffer(_buffer);
        _buffer = default;
    }
}
