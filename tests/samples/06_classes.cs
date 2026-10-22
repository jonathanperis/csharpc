// Classes: fields, constructors, properties, inheritance, virtual dispatch, ToString, type tests.
var shapes = new Shape[] { new Circle(1.5), new Rectangle(2, 3), new Square(4), new Circle(0.5) };
double total = 0;
foreach (var shape in shapes)
{
    Console.WriteLine($"{shape.Name}: area={shape.Area()} perimeter={shape.Perimeter()}");
    total += shape.Area();
}
Console.WriteLine($"total area: {total}");
Console.WriteLine($"shapes created: {Shape.Count}");

Shape s = shapes[2];
Console.WriteLine(s is Rectangle);
Console.WriteLine(s is Circle);
if (s is Square sq) Console.WriteLine($"square with side {sq.Side}");
Console.WriteLine(s);                       // virtual ToString override
Console.WriteLine(new Plain());             // object.ToString() -> type name

var counter = new Counter();
counter.Increment();
counter.Increment();
counter.Value += 10;
Console.WriteLine(counter.Value);
Console.WriteLine(counter.Label);

var account = new Account("Ada", 100);
account.Deposit(50);
Console.WriteLine(account.Withdraw(500));
Console.WriteLine(account.Withdraw(30));
Console.WriteLine(account);

Animal[] zoo = [new Dog("Rex"), new Cat("Tom"), new Dog("Fido")];
foreach (var animal in zoo) Console.WriteLine(animal.Describe());

abstract class Shape
{
    public static int Count;
    protected Shape() => Count++;
    public abstract string Name { get; }
    public abstract double Area();
    public virtual double Perimeter() => 0;
    public override string ToString() => $"{Name}({Area()})";
}

class Circle(double radius) : Shape
{
    public override string Name => "circle";
    public override double Area() => Math.PI * radius * radius;
    public override double Perimeter() => 2 * Math.PI * radius;
}

class Rectangle : Shape
{
    public double Width { get; }
    public double Height { get; }
    public Rectangle(double width, double height) { Width = width; Height = height; }
    public override string Name => "rectangle";
    public override double Area() => Width * Height;
    public override double Perimeter() => 2 * (Width + Height);
}

sealed class Square : Rectangle
{
    public Square(double side) : base(side, side) { }
    public double Side => Width;
    public override string Name => "square";
}

class Plain { }

class Counter
{
    public int Value { get; set; }
    public string Label { get; } = "counter";
    public void Increment() => Value++;
}

class Account
{
    private readonly string _owner;
    private decimalish _balance;
    public Account(string owner, int initial) { _owner = owner; _balance = new decimalish(initial); }
    public void Deposit(int amount) => _balance.Cents += amount * 100;
    public bool Withdraw(int amount)
    {
        if (_balance.Cents < amount * 100) return false;
        _balance.Cents -= amount * 100;
        return true;
    }
    public override string ToString() => _owner + ": " + _balance.Cents / 100;
}

class decimalish(int units)
{
    public long Cents = units * 100L;
}

class Animal(string name)
{
    protected string Name => name;
    public virtual string Sound => "...";
    public string Describe() => $"{Name} says {Sound}";
}

class Dog(string name) : Animal(name) { public override string Sound => "woof"; }
class Cat(string name) : Animal(name) { public override string Sound => "meow"; }
