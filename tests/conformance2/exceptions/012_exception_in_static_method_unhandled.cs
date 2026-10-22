Console.WriteLine("start");
Fail(3);
static void Fail(int n) { if (n == 0) throw new ApplicationException("deep"); Fail(n - 1); }
