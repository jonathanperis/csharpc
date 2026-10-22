foreach (var code in new[] { 1, 2 })
{
    try { throw new Exception(code.ToString()); }
    catch (Exception e) when (e.Message == "1") { Console.WriteLine("filtered one"); }
    catch (Exception e) { Console.WriteLine("other " + e.Message); }
}
