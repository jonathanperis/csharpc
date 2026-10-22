object[] xs = { new Dog(), new Cat(), new Animal(), "str" };
foreach (var x in xs) Console.WriteLine($"{x is Animal} {(x as Dog)?.Bark() ?? "-"} {x is Cat c && c.Lives == 9}");
try { var d = (Dog)(object)new Cat(); } catch (InvalidCastException) { Console.WriteLine("ICE"); }
class Animal { }
class Dog : Animal { public string Bark() => "woof"; }
class Cat : Animal { public int Lives = 9; }
