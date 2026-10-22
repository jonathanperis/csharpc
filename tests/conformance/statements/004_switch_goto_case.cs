Print(1);
static void Print(int n)
{
    switch (n)
    {
        case 1: Console.WriteLine("one"); goto case 2;
        case 2: Console.WriteLine("two"); goto default;
        default: Console.WriteLine("done"); break;
    }
}
