// Static fields, static field initializers and static constructors.
Console.WriteLine(Config.Name);
Console.WriteLine(Config.Version);
Console.WriteLine(Registry.Count);
Registry.Add();
Registry.Add();
Console.WriteLine(Registry.Count);
Console.WriteLine(Registry.Log);

static class Config
{
    public static string Name = "csharpc";
    public static readonly int Version = 1 + 1;
}

class Registry
{
    public static int Count;
    public static string Log = "start";
    static Registry() { Count = 10; Log += ";cctor"; }
    public static void Add() { Count++; Log += ";add"; }
}
