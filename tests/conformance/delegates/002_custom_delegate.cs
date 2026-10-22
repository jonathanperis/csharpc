BinOp op = Add;
Console.WriteLine(op(2, 3));
op = Mul;
Console.WriteLine(op.Invoke(2, 3));
static int Add(int a, int b) => a + b;
static int Mul(int a, int b) => a * b;
delegate int BinOp(int a, int b);
