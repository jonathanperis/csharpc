Console.WriteLine(Eq(1, 1) + " " + Eq("a", "a") + " " + Eq<string?>(null, null) + " " + Eq(new P(1), new P(1)) + " " + Eq(new C(), new C()));
static bool Eq<T>(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
record P(int X);
class C { }
