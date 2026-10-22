// Array bounds are checked, as in .NET.
int[] numbers = new int[3];
int i = 0;
while (true)
{
    numbers[i] = i * i;
    Console.WriteLine(numbers[i]);
    i++;
}
