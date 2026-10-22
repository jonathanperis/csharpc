var p = new P();
p.Run();
partial class P
{
    partial void OnRun();
    public void Run() { OnRun(); Console.WriteLine("ran"); }
}
partial class P { partial void OnRun() => Console.WriteLine("hook"); }
