using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Repro;

[Flags] enum Edges { None = 0, Top = 1, Left = 2, Bottom = 4 }

// A type of the program with its own converter: .resx entries name it like any other.
[TypeConverter(typeof(MarginConverter))]
record struct Margin(int A, int B, int C, int D);

class MarginConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            var p = s.Split(';');
            return new Margin(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
        }
        return base.ConvertFrom(context, culture, value);
    }
}

static class Program
{
    static void Main()
    {
        var resources = new ResourceManager("Repro.Strings", typeof(Program).Assembly);
        Console.WriteLine(resources.GetString("Title"));
        string[] names = ["Location", "ClientSize", "Bounds", "Scale", "BackColor", "ForeColor", "Accent", "Overlay", "Hex", "EmptyPoint",
            "BadPoint", "ShortPoint", "Day", "Stamp", "Span", "Id", "Ver", "Home", "Culture", "Price", "Limit", "Flag", "Letter", "Padding", "Anchor",
            "EmptyUri", "EmptyDate", "EmptyColor", "EmptyMargin", "Hexish"];
        foreach (var name in names)
        {
            try
            {
                var o = resources.GetObject(name);
                Console.WriteLine($"{name}: {Convert.ToString(o, CultureInfo.InvariantCulture) ?? "(null)"} [{o?.GetType().FullName}]");
            }
            catch (Exception e)
            {
                Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}");
            }
        }

        // Each call creates the object again (user types aren't cached by the resource set), strings are the same instance.
        Console.WriteLine(ReferenceEquals(resources.GetObject("Home"), resources.GetObject("Home")));
        Console.WriteLine(ReferenceEquals(resources.GetString("Title"), resources.GetString("Title")));
        var location = (System.Drawing.Point)resources.GetObject("Location")!;
        Console.WriteLine(location.X + location.Y);
        var color = (System.Drawing.Color)resources.GetObject("Accent")!;
        Console.WriteLine($"{color.R},{color.G},{color.B}");
        try { resources.GetString("Location"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
