try {
    try { throw new Exception("first"); }
    finally { throw new InvalidOperationException("second"); }
} catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
