int evaluated = 0;
var q = new[] { 1, 2, 3, 4 }.Where(x => { evaluated++; return x % 2 == 0; });
Console.WriteLine(evaluated);
Console.WriteLine(q.First());
Console.WriteLine(evaluated);
var list = q.ToList();
Console.WriteLine(evaluated);
