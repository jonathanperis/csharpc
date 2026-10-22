var d = new Duck();
IFly f = d; ISwim s = d;
Console.WriteLine(f.Fly() + " " + s.Swim());
Console.WriteLine(d is IFly && d is ISwim);
interface IFly { string Fly(); }
interface ISwim { string Swim(); }
class Duck : IFly, ISwim { public string Fly() => "flap"; public string Swim() => "paddle"; }
