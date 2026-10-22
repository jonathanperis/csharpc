static int M() { int x = 1; try { return x; } finally { x = 99; Console.WriteLine("finally x=" + x); } }
Console.WriteLine(M());
for (int i = 0; i < 3; i++) { try { if (i == 1) break; Console.WriteLine("body " + i); } finally { Console.WriteLine("fin " + i); } }
