using System;
public partial class PropertyBag
{
    public partial int MyProperty { get; set; }
    public partial string Name { get; set; }
}
public partial class PropertyBag
{
    public partial int MyProperty { get => field; set; }
    public partial string Name { get => field ?? "none"; set => field = value.Trim(); }
    public int Plain { get => field * 2; set; }
}
public partial struct S
{
    public partial int V { get; set; }
}
public partial struct S
{
    public partial int V { get => field + 1; set => field = value; }
}
class Program
{
    static void Main()
    {
        var bag = new PropertyBag();
        Console.WriteLine($"{bag.MyProperty} {bag.Name} {bag.Plain}");
        bag.MyProperty = 5; bag.Name = "  x  "; bag.Plain = 4;
        Console.WriteLine($"{bag.MyProperty} {bag.Name} {bag.Plain}");
        var s = new S { V = 3 };
        Console.WriteLine(s.V);
    }
}
