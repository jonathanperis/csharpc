var makers = new List<Func<Func<int>>>();
for (int i = 0; i < 2; i++) {
    int outer = i * 10;
    makers.Add(() => { int inner = outer + 1; return () => outer + inner; });
}
foreach (var m in makers) Console.WriteLine(m()());
