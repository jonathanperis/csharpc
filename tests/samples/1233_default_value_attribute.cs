// System.ComponentModel.DefaultValueAttribute: read from members, type-tested, constructed with each overload, compared.
#nullable disable
using System;
using System.ComponentModel;
using System.Reflection;

class Settings
{
    [DefaultValue(5)] public int Count { get; set; }
    [DefaultValue("hello")] public string Greeting { get; set; }
    [DefaultValue(true)] public bool Enabled;
    [DefaultValue(2.5)] public double Ratio { get; set; }
    [DefaultValue('x')] public char Letter;
    [DefaultValue((byte)7)] public byte Small;
    [DefaultValue(typeof(int), "42")] public int Parsed;
    [DefaultValue(null)] public string Nothing;
    public int Plain { get; set; }
}

class Program
{
    static void Main()
    {
        foreach (var member in new MemberInfo[]
        {
            typeof(Settings).GetProperty("Count"), typeof(Settings).GetProperty("Greeting"), typeof(Settings).GetField("Enabled"),
            typeof(Settings).GetProperty("Ratio"), typeof(Settings).GetField("Letter"), typeof(Settings).GetField("Small"),
            typeof(Settings).GetField("Parsed"), typeof(Settings).GetField("Nothing"), typeof(Settings).GetProperty("Plain"),
        })
        {
            var found = false;
            foreach (object attribute in member.GetCustomAttributes(false))
            {
                // the type test the descriptors in System.ComponentModel make
                if (attribute is DefaultValueAttribute d)
                {
                    found = true;
                    object value;
                    try { value = d.Value; }
                    catch (Exception e) { value = e.GetType().Name + ": " + e.Message; }
                    Console.WriteLine($"{member.Name}: {value ?? "null"} ({value?.GetType().Name ?? "-"})");
                }
            }
            var viaGeneric = member.GetCustomAttribute<DefaultValueAttribute>();
            Console.WriteLine($"{member.Name}: found={found} generic={viaGeneric != null} defined={Attribute.IsDefined(member, typeof(DefaultValueAttribute))}");
        }

        object o = new DefaultValueAttribute(3);
        Console.WriteLine(o is DefaultValueAttribute);
        Console.WriteLine(o is Attribute);
        Console.WriteLine(o.GetType().FullName);
        Console.WriteLine(new object() is DefaultValueAttribute);
        Console.WriteLine(((DefaultValueAttribute)o).Value);

        Console.WriteLine(new DefaultValueAttribute(true).Value);
        Console.WriteLine(new DefaultValueAttribute('c').Value);
        Console.WriteLine(new DefaultValueAttribute(2.5).Value);
        Console.WriteLine(new DefaultValueAttribute(1.5f).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute((byte)1).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute((short)1).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute(1L).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute((sbyte)1).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute((ushort)1).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute(1u).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute(1ul).Value.GetType());
        Console.WriteLine(new DefaultValueAttribute("s").Value);
        Console.WriteLine(new DefaultValueAttribute((object)DayOfWeek.Friday).Value);
        Console.WriteLine(new DefaultValueAttribute(null).Value == null);

        Console.WriteLine(new DefaultValueAttribute(3).Equals(new DefaultValueAttribute(3)));
        Console.WriteLine(new DefaultValueAttribute(3).Equals(new DefaultValueAttribute(4)));
        Console.WriteLine(new DefaultValueAttribute(3).Equals(new DefaultValueAttribute(3L)));
        Console.WriteLine(new DefaultValueAttribute(null).Equals(new DefaultValueAttribute((string)null)));
        Console.WriteLine(new DefaultValueAttribute(null).Equals(new DefaultValueAttribute(0)));
        Console.WriteLine(new DefaultValueAttribute("a").Equals("a"));
        var same = new DefaultValueAttribute(1);
        Console.WriteLine(same.Equals(same) + " " + (same.GetHashCode() == same.GetHashCode()));
    }
}
