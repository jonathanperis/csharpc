var o = new Outer<int>.Inner<string>(1, "x");
Console.WriteLine(o);
class Outer<T> { public class Inner<U>(T t, U u) { public override string ToString() => $"{typeof(T).Name},{typeof(U).Name}:{t},{u}"; } }
