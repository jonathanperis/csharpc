// BigInteger.TryWriteBytes(destination, out bytesWritten, isUnsigned, isBigEndian): little- or big-endian two's complement
// (signed) or magnitude (unsigned) bytes, false and nothing written when the destination is too small, an OverflowException
// for a negative value in the unsigned form. Npgsql's numeric converter writes with it.
using System.Numerics;

foreach (var s in new[] { "0", "1", "255", "256", "-1", "-128", "-129", "65535", "-65536", "4294967295", "4294967296", "123456789012345678901234567890", "-123456789012345678901234567890" })
{
    var v = BigInteger.Parse(s);
    foreach (var (unsigned, bigEndian) in new[] { (false, false), (false, true), (true, false), (true, true) })
    {
        Span<byte> buffer = stackalloc byte[40];
        buffer.Fill(0xEE);
        try
        {
            bool ok = v.TryWriteBytes(buffer, out int written, unsigned, bigEndian);
            Span<byte> small = stackalloc byte[2];
            small.Fill(0xEE);
            bool smallOk = v.TryWriteBytes(small, out int smallWritten, unsigned, bigEndian);
            Console.WriteLine($"{s} unsigned={unsigned} bigEndian={bigEndian}: {ok} {written} {Convert.ToHexString(buffer[..Math.Min(written + 1, 40)])}; small {smallOk} {smallWritten} {Convert.ToHexString(small)}");
        }
        catch (Exception e) { Console.WriteLine($"{s} unsigned={unsigned} bigEndian={bigEndian}: {e.GetType().Name}: {e.Message}"); }
    }
}

// The defaults: signed, little-endian.
Span<byte> d = stackalloc byte[8];
Console.WriteLine(new BigInteger(-2).TryWriteBytes(d, out int w) + " " + w + " " + Convert.ToHexString(d[..w]));
Console.WriteLine(new BigInteger(300).TryWriteBytes(d, out w, true) + " " + w + " " + Convert.ToHexString(d[..w]));
Console.WriteLine(new BigInteger(300).TryWriteBytes(Span<byte>.Empty, out w) + " " + w);
