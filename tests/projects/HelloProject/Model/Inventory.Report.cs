namespace Hello.Model;

/// <summary>Partial class, part 2: queries, in a separate file.</summary>
public partial class Inventory
{
    private int IndexOf(string item)
    {
        for (int i = 0; i < _size; i++)
            if (_items[i] == item) return i;
        return -1;
    }

    public string Report()
    {
        var text = $"{Name}:";
        int total = 0;
        for (int i = 0; i < _size; i++)
        {
            text += $" {_items[i]}={_counts[i]}";
            total += _counts[i];
        }
        return text + $" (total {total})";
    }
}
