string? s = null;
try { _ = s!.Length; } catch (NullReferenceException) { Console.WriteLine("NRE"); }
int zero = 0;
try { _ = 1 / zero; } catch (DivideByZeroException e) { Console.WriteLine(e.Message); }
object o = "x";
try { _ = (int)o; } catch (InvalidCastException) { Console.WriteLine("cast"); }
try { int.Parse("x"); } catch (FormatException) { Console.WriteLine("format"); }
