long before = GC.GetTotalMemory(false);
for (int i = 0; i < 100000; i++) _ = new byte[100];
GC.Collect();
Console.WriteLine(GC.GetTotalMemory(true) >= 0);
Console.WriteLine(before >= 0);
