try { await Boom(); } catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }
var t = Boom();
Console.WriteLine(t.IsFaulted);
try { t.Wait(); } catch (AggregateException e) { Console.WriteLine(e.InnerException!.Message); }
static async Task Boom() { await Task.Yield(); throw new InvalidOperationException("boom"); }
