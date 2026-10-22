// Generic algorithms over IComparable<T>: monomorphization turns constrained interface calls
// into direct calls (and primitive comparisons into plain instructions).
int[] numbers = [42, 7, 19, -3, 0, 7, 100];
Sorter.Sort(numbers);
Console.WriteLine(Join(numbers));

string[] words = ["pear", "apple", "fig", "banana"];
Sorter.Sort(words);
Console.WriteLine(Join(words));

double[] reals = [3.5, double.NaN, -1.25, 2.0];
Sorter.Sort(reals);
Console.WriteLine(Join(reals));

Version[] versions = [new(1, 2), new(0, 9), new(1, 0), new(0, 10)];
Sorter.Sort(versions);
Console.WriteLine(Join(versions));
Console.WriteLine(Sorter.Max(versions));
Console.WriteLine(Sorter.Max(numbers));

var tree = new BinaryTree<int>();
foreach (var n in new[] { 5, 3, 8, 1, 4, 9 }) tree.Insert(n);
Console.WriteLine(tree.Contains(4) + " " + tree.Contains(7) + " depth=" + tree.Depth());
Console.WriteLine(tree.InOrder());

var names = new BinaryTree<string>();
names.Insert("m"); names.Insert("c"); names.Insert("x");
Console.WriteLine(names.InOrder());

static string Join<T>(T[] items)
{
    string s = "";
    for (int i = 0; i < items.Length; i++) s += (i > 0 ? "," : "") + items[i];
    return s;
}

static class Sorter
{
    public static void Sort<T>(T[] items) where T : IComparable<T>
    {
        for (int i = 1; i < items.Length; i++)
        {
            var current = items[i];
            int j = i - 1;
            while (j >= 0 && items[j].CompareTo(current) > 0)
            {
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = current;
        }
    }

    public static T Max<T>(T[] items) where T : IComparable<T>
    {
        T best = items[0];
        foreach (var item in items) if (item.CompareTo(best) > 0) best = item;
        return best;
    }
}

class Version(int major, int minor) : IComparable<Version>
{
    public int Major => major;
    public int Minor => minor;
    public int CompareTo(Version? other) =>
        Major != other!.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);
    public override string ToString() => $"v{Major}.{Minor}";
}

class BinaryTree<T> where T : IComparable<T>
{
    private class Node(T value)
    {
        public T Value = value;
        public Node? Left, Right;
    }

    private Node? _root;

    public void Insert(T value) => _root = Insert(_root, value);

    private Node Insert(Node? node, T value)
    {
        if (node is null) return new Node(value);
        if (value.CompareTo(node.Value) < 0) node.Left = Insert(node.Left, value);
        else node.Right = Insert(node.Right, value);
        return node;
    }

    public bool Contains(T value)
    {
        var node = _root;
        while (node != null)
        {
            int c = value.CompareTo(node.Value);
            if (c == 0) return true;
            node = c < 0 ? node.Left : node.Right;
        }
        return false;
    }

    public int Depth() => Depth(_root);
    private static int Depth(Node? n) => n is null ? 0 : 1 + Math.Max(Depth(n.Left), Depth(n.Right));

    public string InOrder()
    {
        string s = "";
        Walk(_root, ref s);
        return s.Trim2();
    }

    private static void Walk(Node? n, ref string acc)
    {
        if (n is null) return;
        Walk(n.Left, ref acc);
        acc += n.Value + " ";
        Walk(n.Right, ref acc);
    }
}

static class Ext
{
    public static string Trim2(this string s) => s.Length > 0 && s[s.Length - 1] == ' ' ? s.Substring2(s.Length - 1) : s;
    public static string Substring2(this string s, int length)
    {
        string r = "";
        for (int i = 0; i < length; i++) r += s[i];
        return r;
    }
}
