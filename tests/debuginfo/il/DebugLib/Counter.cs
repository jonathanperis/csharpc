namespace DebugLib;

public class Counter(int step)
{
    private int _value;

    public int Add(int times)
    {
        for (var i = 0; i < times; i++)
            _value += step;
        return _value;
    }

    public static string Describe(Counter counter, string name)
    {
        var total = counter.Add(3);
        var text = name + "=" + total;
        return text;
    }
}
