BinOp add = (a, b) => a + b;
BinOp mul = Mul;
Console.WriteLine(Apply(add, 3, 4) + " " + Apply(mul, 3, 4));
Transformer<string> up = s => s.ToUpper();
Console.WriteLine(up("hey"));
static int Mul(int a, int b) => a * b;
static int Apply(BinOp op, int a, int b) => op(a, b);
delegate int BinOp(int a, int b);
delegate T Transformer<T>(T t);
