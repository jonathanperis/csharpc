try { throw new MyError(42); }
catch (MyError e) { Console.WriteLine(e.Code + " " + e.Message); }
class MyError(int code) : Exception("code " + code) { public int Code = code; }
