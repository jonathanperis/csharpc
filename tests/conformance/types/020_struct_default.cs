S s = default;
Console.WriteLine(s.A + " " + (s.B == null));
var arr = new S[2];
Console.WriteLine(arr[1].A);
struct S { public int A; public string B; }
