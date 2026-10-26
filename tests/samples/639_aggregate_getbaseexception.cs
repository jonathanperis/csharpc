try
{
    try { throw new ArgumentException("inner"); }
    catch (Exception e) { throw new AggregateException("agg", e); }
}
catch (AggregateException ae)
{
    var b = ae.GetBaseException();
    Console.WriteLine(b.GetType().Name + "|" + b.Message);
}
try { throw new AggregateException("agg", new ArgumentException("a"), new InvalidOperationException("b")); }
catch (AggregateException ae)
{
    var b = ae.GetBaseException();
    Console.WriteLine(b.GetType().Name + "|" + (b == ae ? "self" : b.Message));
}
try { throw new AggregateException("agg", new AggregateException("nested", new ArgumentException("deep"))); }
catch (AggregateException ae)
{
    var b = ae.GetBaseException();
    Console.WriteLine(b.GetType().Name + "|" + b.Message);
}
