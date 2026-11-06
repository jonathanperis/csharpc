namespace IlTypes;

public interface IPlugin { string Name { get; } }

public abstract class PluginBase : IPlugin
{
    public abstract string Name { get; }
    public class Settings { }
    protected class Cache { }
}

public sealed class Alpha : PluginBase
{
    public override string Name => "alpha";
    public int Total(int seed)
    {
        Func<int, int> capture = x => x + seed;
        Func<int, int> plain = x => x * 2;
        return capture(1) + plain(2);
    }
    public IEnumerable<int> Numbers() { yield return 1; yield return 2; }
    public async Task<int> WaitAsync() { await Task.Yield(); return 3; }
}

public struct Pair { public int A, B; }
public enum Level { Low, High }
public delegate void Notify(string message);

internal class Hidden<T> where T : class
{
    internal class Node { }
}

public static class Registry
{
    public static List<IPlugin> All() => new() { new Alpha() };
}
