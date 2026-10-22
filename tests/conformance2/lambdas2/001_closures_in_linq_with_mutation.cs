int threshold = 2;
var q = new[] { 1, 2, 3, 4 }.Where(x => x > threshold);
threshold = 3;
Console.WriteLine(string.Join(",", q));
