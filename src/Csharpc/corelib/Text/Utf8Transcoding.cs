// System.Text.Unicode.Utf8's transcoders (FromUtf16 / ToUtf16), after dotnet/runtime's Utf8.cs (release/11.0):
// scalar by scalar, an invalid sequence either replaced by U+FFFD or stopping with InvalidData, an incomplete
// one at the end of a non-final block stopping with NeedMoreData, and DestinationTooSmall at the last whole
// scalar that fits. RazorSlices writes its HTML through FromUtf16.
#nullable disable
using System;
using System.Buffers;
using System.Text;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Text.Unicode.Utf8")]
    internal static class Utf8TranscodingMembers
    {
        public static OperationStatus FromUtf16(ReadOnlySpan<char> source, Span<byte> destination, out int charsRead, out int bytesWritten,
            bool replaceInvalidSequences = true, bool isFinalBlock = true)
        {
            int read = 0, written = 0;
            while (read < source.Length)
            {
                int c = source[read];
                if (c < 0x80)
                {
                    if (written == destination.Length) return Stop(OperationStatus.DestinationTooSmall, read, written, out charsRead, out bytesWritten);
                    destination[written++] = (byte)c;
                    read++;
                    continue;
                }
                int scalar, length = 1;
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (c <= 0xDBFF && read + 1 < source.Length && source[read + 1] >= 0xDC00 && source[read + 1] <= 0xDFFF)
                    {
                        scalar = ((c - 0xD800) << 10) + (source[read + 1] - 0xDC00) + 0x10000;
                        length = 2;
                    }
                    else if (c <= 0xDBFF && read + 1 == source.Length && !isFinalBlock)
                        return Stop(OperationStatus.NeedMoreData, read, written, out charsRead, out bytesWritten);
                    else if (!replaceInvalidSequences)
                        return Stop(OperationStatus.InvalidData, read, written, out charsRead, out bytesWritten);
                    else scalar = 0xFFFD;
                }
                else scalar = c;
                int needed = scalar < 0x800 ? 2 : scalar < 0x10000 ? 3 : 4;
                if (needed > destination.Length - written) return Stop(OperationStatus.DestinationTooSmall, read, written, out charsRead, out bytesWritten);
                new Rune(scalar).EncodeToUtf8(destination.Slice(written));
                written += needed;
                read += length;
            }
            return Stop(OperationStatus.Done, read, written, out charsRead, out bytesWritten);
        }

        public static OperationStatus ToUtf16(ReadOnlySpan<byte> source, Span<char> destination, out int bytesRead, out int charsWritten,
            bool replaceInvalidSequences = true, bool isFinalBlock = true)
        {
            int read = 0, written = 0;
            while (read < source.Length)
            {
                byte b = source[read];
                if (b < 0x80)
                {
                    if (written == destination.Length) return Stop(OperationStatus.DestinationTooSmall, read, written, out bytesRead, out charsWritten);
                    destination[written++] = (char)b;
                    read++;
                    continue;
                }
                OperationStatus status = Rune.DecodeFromUtf8(source.Slice(read), out Rune rune, out int length);
                if (status == OperationStatus.NeedMoreData && !isFinalBlock)
                    return Stop(OperationStatus.NeedMoreData, read, written, out bytesRead, out charsWritten);
                if (status != OperationStatus.Done)
                {
                    if (!replaceInvalidSequences) return Stop(OperationStatus.InvalidData, read, written, out bytesRead, out charsWritten);
                    rune = Rune.ReplacementChar;
                }
                int needed = rune.Utf16SequenceLength;
                if (needed > destination.Length - written) return Stop(OperationStatus.DestinationTooSmall, read, written, out bytesRead, out charsWritten);
                rune.EncodeToUtf16(destination.Slice(written));
                written += needed;
                read += length;
            }
            return Stop(OperationStatus.Done, read, written, out bytesRead, out charsWritten);
        }

        private static OperationStatus Stop(OperationStatus status, int read, int written, out int readOut, out int writtenOut)
        {
            readOut = read;
            writtenOut = written;
            return status;
        }
    }
}
