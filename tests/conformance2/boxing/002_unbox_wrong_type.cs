object o = 42;
try { long l = (long)o; Console.WriteLine(l); } catch (InvalidCastException) { Console.WriteLine("ICE"); }
long ok = (int)o;
Console.WriteLine(ok);
object n = null!;
try { int z = (int)n; } catch (NullReferenceException) { Console.WriteLine("NRE"); }
int? ni = (int?)n;
Console.WriteLine(ni.HasValue);
