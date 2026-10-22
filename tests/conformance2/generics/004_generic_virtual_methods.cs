Base b = new Derived();
Console.WriteLine(b.Echo(5) + " " + b.Echo("s"));
class Base { public virtual string Echo<T>(T v) => $"base {v}"; }
class Derived : Base { public override string Echo<T>(T v) => $"derived {typeof(T).Name} {v}"; }
