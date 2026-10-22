var c = new C();
int i = 0;
c[Idx(ref i)] += Val();
Console.WriteLine($"{c.Get(0)} {i}");
static int Idx(ref int i) { Console.WriteLine("idx"); return i++; }
static int Val() { Console.WriteLine("val"); return 5; }
class C {
    int[] a = new int[2];
    public int this[int i] { get { Console.WriteLine($"get {i}"); return a[i]; } set { Console.WriteLine($"set {i}={value}"); a[i] = value; } }
    public int Get(int i) => a[i];
}
