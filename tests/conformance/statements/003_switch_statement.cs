foreach (var x in new object[] { 1, "s", 2.5, null!, 'c' })
{
    switch (x)
    {
        case int i when i > 0: Console.WriteLine("pos int " + i); break;
        case string s: Console.WriteLine("string " + s); break;
        case double d: Console.WriteLine("double " + d); break;
        case null: Console.WriteLine("null"); break;
        default: Console.WriteLine("other"); break;
    }
}
