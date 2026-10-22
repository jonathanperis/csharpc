int[] data = { 5, 6, 7 };
unsafe
{
    fixed (int* p = data)
    {
        Console.WriteLine(*(p + 2));
    }
}
