try
{
    try { throw new FormatException("parse"); }
    catch (Exception e) { throw new InvalidOperationException("wrapped", e); }
}
catch (Exception e) { Console.WriteLine(e.Message + " <- " + e.InnerException!.Message); }
