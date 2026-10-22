var arr = new S[3];
arr[1].V = 5;
ref S r = ref arr[2];
r.V = 9;
foreach (var s in arr) Console.Write(s.V + " ");
Console.WriteLine();
var list = new List<S> { new S { V = 1 } };
var copy = list[0];
copy.V = 100;
Console.WriteLine(list[0].V);
struct S { public int V; }
