const int A = 1 << 10;
const string S = "a" + "b" + nameof(A);
const double D = 1.0 / 3;
const long L = int.MaxValue + 1L;
Console.WriteLine($"{A} {S} {D} {L} {unchecked(int.MaxValue + 1)}");
const char C = (char)('A' + 2);
Console.WriteLine(C);
