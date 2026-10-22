int i = 5;
int j = i++ + ++i - i-- - --i;
Console.WriteLine($"{i} {j}");
int k = 1;
k += k++ * ++k;
Console.WriteLine(k);
