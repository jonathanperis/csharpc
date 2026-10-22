int i = 5;
int a = i++ + ++i;
Console.WriteLine($"{i} {a}");
int j = 10;
j += j++ * 2;
Console.WriteLine(j);
