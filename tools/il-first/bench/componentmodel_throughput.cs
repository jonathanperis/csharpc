// System.ComponentModel throughput, the program behind the numbers of ADR 0023's ComponentModel row:
//   tools/il-first/ab.sh System.ComponentModel.TypeConverter,System.ComponentModel,System.ComponentModel.Primitives tools/il-first/bench/componentmodel_throughput.cs
// Converters (primitives, enums, Point), TypeDescriptor property descriptors, attribute collections, CancelEventArgs. Checksums only.
#pragma warning disable IL2026
using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;

enum Level { Low, Mid, High }

class Item
{
    [DefaultValue(5)] public int Count { get; set; } = 5;
    [Description("a name")] public string Name { get; set; } = "x";
    public Level Level { get; set; }
    public double Ratio { get; set; }
}

class Program
{
    static void Main()
    {
        long check = 0;
        var intConverter = TypeDescriptor.GetConverter(typeof(int));
        var levelConverter = TypeDescriptor.GetConverter(typeof(Level));
        var pointConverter = TypeDescriptor.GetConverter(typeof(Point));
        var doubleConverter = TypeDescriptor.GetConverter(typeof(double));
        for (int round = 0; round < 30; round++)
        {
            for (int i = 0; i < 4_000; i++)
            {
                check += (int)intConverter.ConvertFromInvariantString((i * 7).ToString(CultureInfo.InvariantCulture))!;
                check += ((Level)levelConverter.ConvertFromInvariantString(((Level)(i % 3)).ToString())!).GetHashCode();
                check += (int)((Point)pointConverter.ConvertFromInvariantString($"{i}, {i + 1}")!).Y;
                check += intConverter.ConvertToInvariantString(i)!.Length;
                check += (long)(double)doubleConverter.ConvertFromInvariantString("1.5")!;
            }
            var item = new Item();
            var properties = TypeDescriptor.GetProperties(typeof(Item));
            for (int i = 0; i < 2_000; i++)
            {
                var count = properties["Count"]!;
                count.SetValue(item, i);
                check += (int)count.GetValue(item)!;
                check += count.ShouldSerializeValue(item) ? 1 : 0;
                check += properties["Name"]!.Description.Length;
                check += TypeDescriptor.GetAttributes(typeof(Item)).Count;
                var args = new CancelEventArgs(i % 2 == 0);
                check += args.Cancel ? 1 : 0;
            }
            foreach (PropertyDescriptor property in properties) check += property.Name.Length + property.PropertyType.Name.Length;
        }
        Console.WriteLine(check);
    }
}
