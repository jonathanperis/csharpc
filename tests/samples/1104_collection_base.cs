// System.Collections.CollectionBase (a corelib shadow since the certificate collections derive from it): the On* hooks that
// run around every change and their order, rollbacks when a *Complete hook throws, vetoes from the pre-change hooks,
// IList/ICollection behaviour, Capacity, the enumerator, and the out-of-range exceptions.
using System.Collections;

var list = new IntList();
list.Add(10);
list.Add(20);
list.Insert(1, 15);
Console.WriteLine("items: " + string.Join(",", list.Items()) + " count " + list.Count);
list.Flush("after adds");

list[0] = 11;
list.Flush("after set");

Console.WriteLine("contains 15: " + list.Contains(15) + ", index of 20: " + list.IndexOf(20) + ", of 99: " + list.IndexOf(99));
list.Remove(15);
list.RemoveAt(0);
Console.WriteLine("items: " + string.Join(",", list.Items()));
list.Flush("after removes");

try { list.AddObject("text"); } catch (ArgumentException e) { Console.WriteLine("validate: " + e.Message); }
try { list.AddObject(null!); } catch (ArgumentNullException e) { Console.WriteLine("null: " + e.ParamName); }
list.Flush("after invalid adds");

list.FailOn = "OnInsertComplete";
try { list.Add(30); } catch (InvalidOperationException e) { Console.WriteLine("rolled back insert: " + e.Message + " count " + list.Count); }
list.FailOn = "OnRemove";
try { list.Remove(20); } catch (InvalidOperationException e) { Console.WriteLine("vetoed remove: " + e.Message + " count " + list.Count); }
list.FailOn = "OnRemoveComplete";
try { list.RemoveAt(0); } catch (InvalidOperationException e) { Console.WriteLine("rolled back remove: " + e.Message + " items " + string.Join(",", list.Items())); }
list.FailOn = "OnSetComplete";
try { list[0] = 99; } catch (InvalidOperationException e) { Console.WriteLine("rolled back set: " + e.Message + " items " + string.Join(",", list.Items())); }
list.FailOn = null;
list.Flush("after failures");

try { _ = list[5]; } catch (ArgumentOutOfRangeException e) { Console.WriteLine("get: " + e.ParamName + " | " + e.Message); }
try { list[-1] = 1; } catch (ArgumentOutOfRangeException e) { Console.WriteLine("set: " + e.Message); }
try { list.RemoveAt(7); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("remove at: " + e.Message); }
try { list.Insert(9, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("insert: " + e.Message); }
try { list.RemoveObject("x"); } catch (ArgumentException e) { Console.WriteLine("remove object: " + e.GetType().Name); }
try { list.RemoveObject(12345); } catch (ArgumentException e) { Console.WriteLine("remove missing: " + e.Message); }

IList asList = list;
ICollection asCollection = list;
Console.WriteLine($"IList: fixed={asList.IsFixedSize} readonly={asList.IsReadOnly}; ICollection: sync={asCollection.IsSynchronized} root={asCollection.SyncRoot != null}");
var copy = new object[asCollection.Count];
asCollection.CopyTo(copy, 0);
Console.WriteLine("copy: " + string.Join(",", copy));
foreach (object item in list) Console.Write(item + ";");
Console.WriteLine();

list.Capacity = 64;
Console.WriteLine("capacity " + list.Capacity + " count " + list.Count);
list.Clear();
Console.WriteLine("cleared: " + list.Count);
list.Flush("after clear");

class IntList : CollectionBase
{
    public List<string> Log = new();
    public string? FailOn;

    public int Add(int value) => List.Add(value);
    public int AddObject(object value) => List.Add(value);
    public void RemoveObject(object value) => List.Remove(value);
    public void Insert(int index, int value) => List.Insert(index, value);
    public void Remove(int value) => List.Remove(value);
    public bool Contains(int value) => List.Contains(value);
    public int IndexOf(int value) => List.IndexOf(value);
    public IEnumerable<object> Items() => InnerList.Cast<object>();
    public int this[int index] { get => (int)List[index]!; set => List[index] = value; }

    public void Flush(string title) { Console.WriteLine(title + ": " + string.Join(" ", Log)); Log.Clear(); }

    private void Hook(string name, string detail)
    {
        Log.Add(name + detail);
        if (FailOn == name) throw new InvalidOperationException(name + " failed");
    }

    protected override void OnValidate(object value)
    {
        base.OnValidate(value);
        if (value is not int) throw new ArgumentException("only ints");
    }
    protected override void OnInsert(int index, object? value) => Hook("OnInsert", $"({index},{value})");
    protected override void OnInsertComplete(int index, object? value) => Hook("OnInsertComplete", $"({index},{value})");
    protected override void OnRemove(int index, object? value) => Hook("OnRemove", $"({index},{value})");
    protected override void OnRemoveComplete(int index, object? value) => Hook("OnRemoveComplete", $"({index},{value})");
    protected override void OnSet(int index, object? oldValue, object? newValue) => Hook("OnSet", $"({index},{oldValue}->{newValue})");
    protected override void OnSetComplete(int index, object? oldValue, object? newValue) => Hook("OnSetComplete", $"({index},{oldValue}->{newValue})");
    protected override void OnClear() => Hook("OnClear", "");
    protected override void OnClearComplete() => Hook("OnClearComplete", "");
}
