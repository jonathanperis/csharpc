static bool Log(string s, bool r) { Console.WriteLine("filter " + s); return r; }
try {
    try { throw new ArgumentException("arg"); }
    catch (Exception e) when (Log("inner", false)) { Console.WriteLine("inner handler"); }
    finally { Console.WriteLine("inner finally"); }
} catch (Exception e) when (Log("outer", true)) { Console.WriteLine("outer handler " + e.Message); }
