Console.WriteLine(C.Max + C.Name);
Console.WriteLine(new C().Created);
class C
{
    public const int Max = 10;
    public const string Name = "c";
    public readonly int Created = 5;
}
