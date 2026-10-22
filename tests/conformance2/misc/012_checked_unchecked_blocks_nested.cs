int max = int.MaxValue;
checked {
    int a = unchecked(max + 1);
    Console.WriteLine(a);
    try { int b = max + 1; Console.WriteLine(b); } catch (OverflowException) { Console.WriteLine("ovf"); }
}
unchecked { Console.WriteLine(max * 2); }
