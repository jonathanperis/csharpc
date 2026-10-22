try { Run(); } catch (Exception e) { Console.WriteLine("outer " + e.Message); }
static void Run()
{
    try { Console.WriteLine("try"); throw new Exception("boom"); }
    finally { Console.WriteLine("finally"); }
}
