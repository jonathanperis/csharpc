// System.Collections.ObjectModel.Collection<T>: changes go through the protected virtuals a derived
// collection overrides (Microsoft.Net.Http.Headers' ObjectCollection<T> rejects nulls that way).
using System.Collections;
using System.Collections.ObjectModel;

var log = new LoggingCollection();
log.Add("a");
log.Add("b");
log.Insert(0, "z");
log[1] = "A";
log.Remove("b");
log.Remove("missing");
Console.WriteLine(string.Join(",", log) + " " + log.Count + " " + log.Contains("z") + " " + log.IndexOf("A"));
log.Clear();
Console.WriteLine(log.Count);
try { log.Add(null!); } catch (ArgumentNullException e) { Console.WriteLine("rejected: " + e.ParamName); }

var plain = new Collection<int> { 3, 1, 2 };
Console.WriteLine(string.Join(",", plain) + " " + plain.GetType().Name + " " + (plain is IList<int>) + " " + (plain is IReadOnlyList<int>));
try { plain[5] = 1; } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { plain.Insert(9, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { plain.RemoveAt(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

var overArray = new Collection<int>(new[] { 7, 8 });
try { overArray.Add(9); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
overArray.CopyTo(new int[2], 0);
Console.WriteLine(((ICollection<int>)overArray).IsReadOnly + " " + ((IList)overArray).IsFixedSize);

IList list = plain;
Console.WriteLine(list.Add(4) + " " + list.Contains(4) + " " + list.Contains("4") + " " + list.IndexOf(2) + " " + list.IndexOf(null));
try { list.Add("x"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { list.Add(null); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
var objects = new object[5];
list.CopyTo(objects, 1);
Console.WriteLine(string.Join("|", objects));
try { new Collection<string>(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

class LoggingCollection : Collection<string>
{
    protected override void InsertItem(int index, string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Console.WriteLine($"insert {index} {item}");
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, string item)
    {
        Console.WriteLine($"set {index} {item}");
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        Console.WriteLine($"remove {index} {Items[index]}");
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        Console.WriteLine($"clear {Count}");
        base.ClearItems();
    }
}
