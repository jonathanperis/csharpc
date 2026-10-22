var c = new C();
Console.WriteLine(((IConv<int>)c).Convert());
Console.WriteLine(((IConv<string>)c).Convert());
class C : IConv<int>, IConv<string> { int IConv<int>.Convert() => 42; string IConv<string>.Convert() => "forty-two"; }
interface IConv<T> { T Convert(); }
