object o = new Dog();
Console.WriteLine(o is Animal);
Animal? a = o as Animal;
Console.WriteLine(a != null);
Console.WriteLine((o as string) == null);
Dog d = (Dog)o;
Console.WriteLine(d.GetType().Name);
class Animal { }
class Dog : Animal { }
