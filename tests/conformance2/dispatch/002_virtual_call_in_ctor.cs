var d = new Derived();
Console.WriteLine(d.Value);
class Base { public Base() { Console.WriteLine("Base ctor sees: " + Describe()); } public virtual string Describe() => "base"; }
class Derived : Base {
    public string Value = "init";
    public Derived() { Value = "ctor"; }
    public override string Describe() => "derived, Value=" + (Value ?? "null");
}
