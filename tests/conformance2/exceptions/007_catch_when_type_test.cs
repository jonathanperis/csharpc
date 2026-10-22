foreach (Exception ex in new Exception[] { new ArgumentNullException("p"), new ArgumentOutOfRangeException("q", "msg"), new IOException("io") }) {
    try { throw ex; }
    catch (Exception e) when (e is ArgumentException ae) { Console.WriteLine("arg family: " + e.Message); }
    catch (Exception e) { Console.WriteLine("other: " + e.GetType().Name); }
}
