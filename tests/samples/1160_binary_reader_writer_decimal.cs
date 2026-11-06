// BinaryWriter.Write(decimal) / BinaryReader.ReadDecimal: the 16 bytes (lo, mid, hi, flags), scale and sign kept,
// invalid flag bits rejected, a truncated stream is an EndOfStreamException.
using System;
using System.IO;

class P
{
    static void Main()
    {
        decimal[] values = { 0m, 1m, -1m, 1.50m, -0.0001m, 79228162514264337593543950335m, -79228162514264337593543950335m, 12345.6789000m, 0.0000000000000000000000000001m };
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            foreach (var v in values) w.Write(v);
            w.Write(7);
        }
        Console.WriteLine(ms.Length);
        Console.WriteLine(Convert.ToHexString(ms.ToArray(), 0, 32));
        ms.Position = 0;
        using (var r = new BinaryReader(ms))
        {
            foreach (var v in values)
            {
                decimal got = r.ReadDecimal();
                Console.WriteLine(got + " " + (got == v) + " " + ((decimal.GetBits(got)[3] >> 16) & 0xFF));
            }
            Console.WriteLine(r.ReadInt32());
            try { r.ReadDecimal(); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        }
        var bad = new byte[16];
        bad[14] = 0x1D; // scale 29
        try { new BinaryReader(new MemoryStream(bad)).ReadDecimal(); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
