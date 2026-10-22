Console.WriteLine(IShape.Count);
IShape.Count++;
Console.WriteLine(IShape.Describe());
interface IShape { static int Count = 3; static string Describe() => $"shapes: {Count}"; }
