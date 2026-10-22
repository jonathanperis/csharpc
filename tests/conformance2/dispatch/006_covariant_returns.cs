Animal a = new Cat();
Animal clone = a.Clone();
Console.WriteLine(clone.GetType().Name);
Cat c = new Cat().Clone();
Console.WriteLine(c.Meow());
class Animal { public virtual Animal Clone() => new Animal(); }
class Cat : Animal { public override Cat Clone() => new Cat(); public string Meow() => "meow"; }
