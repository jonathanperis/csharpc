int? x = 5;
x += 2; x++; x *= null;
Console.WriteLine(x.HasValue);
int? y = null;
y++;
Console.WriteLine(y.HasValue);
double? d = 1.5;
d = d * 2;
Console.WriteLine(d);
