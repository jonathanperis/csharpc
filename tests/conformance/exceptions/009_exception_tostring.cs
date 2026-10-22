var e = new ArgumentOutOfRangeException("index", "must be positive");
Console.WriteLine(e.Message);
Console.WriteLine(e.ParamName);
Console.WriteLine(new Exception("m").Message);
