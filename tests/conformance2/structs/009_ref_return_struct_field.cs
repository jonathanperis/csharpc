var g = new Grid();
ref int cell = ref g.At(1);
cell = 42;
g.At(0) = 7;
Console.WriteLine($"{g.At(0)} {g.At(1)}");
class Grid { int[] c = new int[3]; public ref int At(int i) => ref c[i]; }
