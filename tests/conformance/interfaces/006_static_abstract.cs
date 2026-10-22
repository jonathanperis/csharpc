Console.WriteLine(Make<Dog>().Sound());
static T Make<T>() where T : ICreatable<T> => T.Create();
interface ICreatable<T> { static abstract T Create(); }
class Dog : ICreatable<Dog> { public static Dog Create() => new(); public string Sound() => "woof"; }
