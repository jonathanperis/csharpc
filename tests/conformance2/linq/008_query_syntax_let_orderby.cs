var q = from w in new[] { "delta", "alpha", "charlie", "bravo" }
        let len = w.Length
        where len > 4
        orderby len descending, w
        select $"{w}({len})";
Console.WriteLine(string.Join(" ", q));
var nested = from x in Enumerable.Range(1, 3) from y in Enumerable.Range(1, x) select x * 10 + y;
Console.WriteLine(string.Join(",", nested));
