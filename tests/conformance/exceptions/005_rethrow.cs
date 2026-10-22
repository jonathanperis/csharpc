try
{
    try { throw new ArgumentException("inner"); }
    catch (ArgumentException) { Console.WriteLine("logging"); throw; }
}
catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
