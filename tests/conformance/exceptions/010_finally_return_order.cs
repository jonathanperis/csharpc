Console.WriteLine(F());
static int F()
{
    int x = 1;
    try { return x; }
    finally { x = 2; Console.WriteLine("finally x=" + x); }
}
