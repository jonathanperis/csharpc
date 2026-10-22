Animal[] zoo = { new Dog(), new Cat(), new Animal() };
foreach (var a in zoo) Console.WriteLine(a.Speak());
class Animal { public virtual string Speak() => "..."; }
class Dog : Animal { public override string Speak() => "woof"; }
class Cat : Animal { public override string Speak() => "meow " + base.Speak(); }
