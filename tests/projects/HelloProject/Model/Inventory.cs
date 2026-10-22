namespace Hello.Model;

/// <summary>Partial class, part 1: state and mutation.</summary>
public partial class Inventory(string name)
{
    private readonly string[] _items = new string[16];
    private readonly int[] _counts = new int[16];
    private int _size;

    public string Name => name;

    public void Add(string item, int count)
    {
        int index = IndexOf(item);
        if (index < 0)
        {
            index = _size++;
            _items[index] = item;
        }
        _counts[index] += count;
    }
}
