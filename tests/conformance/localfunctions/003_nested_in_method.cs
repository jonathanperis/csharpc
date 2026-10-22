Console.WriteLine(Outer(5));
static int Outer(int n)
{
    int factor = 3;
    return Inner(n);
    int Inner(int x) => x * factor;
}
