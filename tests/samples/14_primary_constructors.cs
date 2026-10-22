// C# 12 primary constructors: captured parameters become synthesized fields.
var d = new Dog("Rex", 3);
Console.WriteLine(d.Describe());
Console.WriteLine(new Point(3, 4).Length);
var c = new Counter(10);
c.Tick(); c.Tick();
Console.WriteLine(c.Value);

class Animal(string name)
{
    protected string Name => name;
    public virtual string Sound => "...";
    public string Describe() => $"{Name} says {Sound}";
}

class Dog(string name, int age) : Animal(name.Length > 0 ? name : "anon")
{
    private readonly int _years = age * 7;
    public override string Sound => "woof (" + _years + " dog years)";
}

class Point(double x, double y)
{
    public double Length { get; } = Math.Sqrt(x * x + y * y);
}

class Counter(int start)
{
    private int _count;
    public void Tick() => _count++;
    public int Value => start + _count;
}
