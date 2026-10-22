// Integer and floating-point semantics must match .NET exactly.
int max = int.MaxValue;
Console.WriteLine(max + 1);                 // unchecked wraparound
Console.WriteLine(-7 / 2);                  // truncation toward zero
Console.WriteLine(-7 % 2);
Console.WriteLine(7 % -2);
Console.WriteLine(1 << 33);                 // shift count masked to 5 bits
Console.WriteLine(-16 >> 2);                // arithmetic shift
Console.WriteLine(-16 >>> 28);              // C# 11 unsigned shift
Console.WriteLine(0xFF & 0x0F | 0x30 ^ 0x01);
Console.WriteLine(~5);

uint u = 3_000_000_000;
Console.WriteLine(u);
Console.WriteLine(u / 7);
ulong big = ulong.MaxValue;
Console.WriteLine(big);
long l = long.MinValue;
Console.WriteLine(l);

byte b = 250;
b += 10;                                    // computed in int, narrowed back
Console.WriteLine(b);
int fortyK = 40000;
short s = (short)fortyK;
Console.WriteLine(s);
char c = 'A';
c++;
Console.WriteLine(c);
Console.WriteLine((int)c);
Console.WriteLine((char)(c + 1));

double d = 10.0 / 3.0;
Console.WriteLine(d);
Console.WriteLine(1.0 / 0.0);
Console.WriteLine(-1.0 / 0.0);
Console.WriteLine(0.0 / 0.0);
Console.WriteLine(1e21);
Console.WriteLine(123.456e-10);
Console.WriteLine(2.5f * 2.0f);
Console.WriteLine((float)(1.0 / 3.0));
Console.WriteLine((int)3.99);
Console.WriteLine((int)-3.99);
double huge = 1e20;
Console.WriteLine((int)huge);               // saturating since .NET 9
double nan = double.NaN;
Console.WriteLine((long)nan);
Console.WriteLine(5 / 2 * 2.0);
Console.WriteLine(7.5 % 2);

int x = 10;
x *= 3; x -= 4; x /= 2; x %= 7; x <<= 2; x |= 1;
Console.WriteLine(x);
int y = x++ + ++x;
Console.WriteLine($"{x} {y}");
Console.WriteLine(0.1 + 0.2 == 0.3);
Console.WriteLine(double.NaN == double.NaN);
Console.WriteLine(double.NaN != double.NaN);
