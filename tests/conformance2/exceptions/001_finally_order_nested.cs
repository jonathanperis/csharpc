try {
    try { Console.WriteLine("try"); throw new InvalidOperationException("x"); }
    finally { Console.WriteLine("inner finally"); }
} catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }
finally { Console.WriteLine("outer finally"); }
