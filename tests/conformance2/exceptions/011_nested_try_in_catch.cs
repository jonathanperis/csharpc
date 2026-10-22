try { throw new Exception("A"); }
catch (Exception a) {
    try { throw new Exception("B"); }
    catch (Exception b) { Console.WriteLine(a.Message + b.Message); }
    Console.WriteLine("after inner " + a.Message);
}
