var n = new Outer.Inner();
Console.WriteLine(n.Hello());
Console.WriteLine(n);
class Outer { public class Inner { public string Hello() => "hi"; } }
