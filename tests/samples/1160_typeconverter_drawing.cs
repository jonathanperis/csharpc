// TypeDescriptor.GetConverter(typeof(X)) and the framework's own converters for System.Drawing's primitives
// (PointConverter, SizeConverter, SizeFConverter, RectangleConverter, ColorConverter, from System.ComponentModel.TypeConverter's IL),
// enums, Nullable<T>, DateTime/Uri and a program type with a [TypeConverter] attribute: conversions from and to strings
// (invariant and current culture), CanConvertFrom/To, CreateInstance and the exceptions for bad input.
#nullable disable
#pragma warning disable IL2026, IL2067, IL2072
using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;

[Flags] enum Edges { None = 0, Top = 1, Left = 2, Bottom = 4 }
enum Mode { Fast, Slow }

[TypeConverter(typeof(TemperatureConverter))]
struct Temperature
{
    public double Degrees;
    public override string ToString() => Degrees.ToString(CultureInfo.InvariantCulture) + "C";
}

class TemperatureConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is string s)
            return new Temperature { Degrees = double.Parse(s.TrimEnd('C'), CultureInfo.InvariantCulture) };
        return base.ConvertFrom(context, culture, value);
    }
}

class Program
{
    static void Show(string label, Func<object> f)
    {
        try
        {
            object v = f();
            Console.WriteLine($"{label}: {Convert.ToString(v, CultureInfo.InvariantCulture) ?? "(null)"} [{v?.GetType().Name}]");
        }
        catch (Exception e)
        {
            Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}");
        }
    }

    static void Main()
    {
        TypeConverter point = TypeDescriptor.GetConverter(typeof(Point));
        Console.WriteLine(point.GetType().Name);
        Show("point", () => point.ConvertFromInvariantString("10, 20"));
        Show("point trimmed", () => point.ConvertFromInvariantString("  3 ,4 "));
        Show("point empty", () => point.ConvertFromInvariantString(""));
        Show("point one", () => point.ConvertFromInvariantString("1"));
        Show("point bad", () => point.ConvertFromInvariantString("a, b"));
        Show("point from int", () => point.ConvertFrom(5));
        Show("point to string", () => point.ConvertToInvariantString(new Point(7, 8)));
        Show("point to int", () => point.ConvertTo(new Point(7, 8), typeof(int)));
        Console.WriteLine($"{point.CanConvertFrom(typeof(string))} {point.CanConvertFrom(typeof(int))} {point.CanConvertTo(typeof(string))} {point.CanConvertTo(typeof(int))}");
        Console.WriteLine($"{point.GetCreateInstanceSupported()} {point.GetPropertiesSupported()}");
        Show("point create", () => point.CreateInstance(new Hashtable { ["X"] = 5, ["Y"] = 6 }));
        Show("point create bad", () => point.CreateInstance(new Hashtable { ["X"] = 5 }));

        Show("size", () => new SizeConverter().ConvertFromInvariantString("284, 261"));
        Show("size to string", () => new SizeConverter().ConvertToString(new Size(1, 2)));
        Show("sizeF", () => new SizeFConverter().ConvertFromInvariantString("1.5, 2.25"));
        Show("sizeF to string", () => new SizeFConverter().ConvertToInvariantString(new SizeF(1.5f, 2.25f)));
        Show("rectangle", () => TypeDescriptor.GetConverter(typeof(Rectangle)).ConvertFromInvariantString("1, 2, 300, 400"));
        Show("rectangle bad", () => TypeDescriptor.GetConverter(typeof(Rectangle)).ConvertFromInvariantString("1, 2, 3"));
        Show("rectangle to string", () => new RectangleConverter().ConvertToInvariantString(new Rectangle(1, 2, 3, 4)));

        TypeConverter color = TypeDescriptor.GetConverter(typeof(Color));
        foreach (string text in new[] { "Red", "red", "Control", "#FF8000", "#80FF8000", "128, 64, 32", "64, 1, 2, 3", "", "NoSuchColor" })
            Show("color [" + text + "]", () => color.ConvertFromInvariantString(text));
        Show("color to string", () => color.ConvertToInvariantString(Color.FromArgb(1, 2, 3)));
        Show("color named to string", () => color.ConvertToInvariantString(Color.Blue));
        Show("color alpha to string", () => color.ConvertToInvariantString(Color.FromArgb(10, 1, 2, 3)));
        Console.WriteLine(color.GetStandardValuesSupported() + " " + color.GetStandardValues().Count);

        TypeConverter edges = TypeDescriptor.GetConverter(typeof(Edges));
        Console.WriteLine(edges.GetType().Name);
        Show("edges", () => edges.ConvertFromInvariantString("Top, Left"));
        Show("edges number", () => edges.ConvertFromInvariantString("5"));
        Show("edges bad", () => edges.ConvertFromInvariantString("Sideways"));
        Show("edges to string", () => edges.ConvertToInvariantString(Edges.Bottom | Edges.Top));
        Show("mode", () => TypeDescriptor.GetConverter(typeof(Mode)).ConvertFromInvariantString("Slow"));

        Show("nullable", () => TypeDescriptor.GetConverter(typeof(int?)).ConvertFromInvariantString("42"));
        Show("nullable empty", () => TypeDescriptor.GetConverter(typeof(int?)).ConvertFromInvariantString(""));
        Show("datetime", () => TypeDescriptor.GetConverter(typeof(DateTime)).ConvertFromInvariantString("2024-03-05T06:07:08"));
        Show("datetime to string", () => TypeDescriptor.GetConverter(typeof(DateTime)).ConvertToInvariantString(new DateTime(2024, 3, 5, 6, 7, 8)));
        Show("uri", () => TypeDescriptor.GetConverter(typeof(Uri)).ConvertFromInvariantString("https://example.com/a?b=c"));
        Show("temperature", () => TypeDescriptor.GetConverter(typeof(Temperature)).ConvertFromInvariantString("21.5C"));
        Show("temperature bad type", () => TypeDescriptor.GetConverter(typeof(Temperature)).ConvertFrom(3));
        Show("int", () => TypeDescriptor.GetConverter(typeof(int)).ConvertFromInvariantString("0x1F"));
        Show("base", () => new TypeConverter().ConvertFromInvariantString("x"));
        Show("base to string", () => new TypeConverter().ConvertToInvariantString(12));
        Show("base to int", () => new TypeConverter().ConvertTo(12, typeof(long)));
    }
}
