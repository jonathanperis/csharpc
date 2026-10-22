// Closed-world devirtualization must preserve virtual semantics in every case.
Animal[] zoo = [new Dog(), new Puppy(), new Cat(), new Lion()];
foreach (var a in zoo) Console.WriteLine($"{a.Name()} {a.Legs()} {a.Sound()} {a.Kind()}");
Feline f = new Lion();
Console.WriteLine(f.Sound() + " " + f.Legs());
Dog d = new Puppy();
Console.WriteLine(d.Sound() + " " + d.Kind());
Only o = new Only();
long total = 0;
for (int i = 0; i < 1000; i++) total += o.Value(i);
Console.WriteLine(total);
Base b = new OnlyImpl();
Console.WriteLine(b.Compute(20));

abstract class Animal
{
    public abstract string Name();
    public virtual int Legs() => 4;                 // never overridden: devirtualizable
    public virtual string Sound() => "...";
    public virtual string Kind() => "animal";
}
class Dog : Animal
{
    public override string Name() => "dog";
    public override string Sound() => "woof";
}
class Puppy : Dog
{
    public override string Name() => "puppy";
    public override string Kind() => "young";       // overridden only below Dog
}
abstract class Feline : Animal
{
    public override string Sound() => "meow";
}
class Cat : Feline { public override string Name() => "cat"; }
class Lion : Feline
{
    public override string Name() => "lion";
    public override string Sound() => "roar";
}
class Only { public virtual long Value(int i) => i * 3; }
abstract class Base { public abstract int Compute(int x); }
class OnlyImpl : Base { public override int Compute(int x) => x * x + 1; }
