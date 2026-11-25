using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Repro;

// A type of the program standing in for System.Drawing's Bitmap and Icon: its converter takes a byte array (format 2), and it has a
// (Stream) constructor (format 4).
[TypeConverter(typeof(BlobConverter))]
sealed class Blob
{
    public byte[] Data { get; }
    public string Via { get; }
    public Blob(byte[] data, string via) { Data = data; Via = via; }
    public Blob(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        Data = copy.ToArray();
        Via = "stream" + (stream.CanWrite ? " (writable)" : "");
    }
    public override string ToString() => $"{Via}: {Data.Length} bytes [{string.Join(",", Data.Take(6))}]";
}

sealed class BlobConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(byte[]) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is byte[] bytes ? (bytes.Length == 0 ? throw new FormatException("empty blob") : new Blob(bytes, "converter")) : base.ConvertFrom(context, culture, value);
}

static class Program
{
    static void Main()
    {
        var resources = new ResourceManager("Repro.Assets", typeof(Program).Assembly);
        foreach (var name in new[] { "Icon", "Picture", "Empty", "Raw", "RawStream" })
        {
            try
            {
                var o = resources.GetObject(name);
                string shown = o switch
                {
                    byte[] b => $"{b.Length} bytes",
                    Stream s => $"stream of {s.Length}",
                    _ => o?.ToString() ?? "(null)",
                };
                Console.WriteLine($"{name}: {shown} [{o?.GetType().Name}]");
            }
            catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
        }
        // Each GetObject creates the object again.
        Console.WriteLine(ReferenceEquals(resources.GetObject("Icon"), resources.GetObject("Icon")));
    }
}
