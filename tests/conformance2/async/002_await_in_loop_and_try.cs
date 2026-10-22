int total = 0;
for (int i = 0; i < 3; i++) {
    try { total += await Work(i); }
    finally { Console.WriteLine("finally " + i); }
}
Console.WriteLine(total);
static async Task<int> Work(int i) { await Task.Yield(); if (i == 1) return 10; return i; }
