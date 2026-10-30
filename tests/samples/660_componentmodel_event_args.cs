// CollectionChangeEventArgs and ListChangedEventArgs (System.ComponentModel.TypeConverter's), served by the corelib.
using System;
using System.ComponentModel;

class Program
{
    static void Main()
    {
        var c = new CollectionChangeEventArgs(CollectionChangeAction.Add, "item");
        Console.WriteLine($"{c.Action} {c.Element}");
        var r = new CollectionChangeEventArgs(CollectionChangeAction.Refresh, null);
        Console.WriteLine($"{r.Action} {r.Element == null}");
        foreach (var e in new[] {
            new ListChangedEventArgs(ListChangedType.ItemAdded, 3),
            new ListChangedEventArgs(ListChangedType.ItemMoved, 4, 1),
            new ListChangedEventArgs(ListChangedType.ItemChanged, 2, (PropertyDescriptor?)null),
            new ListChangedEventArgs(ListChangedType.Reset, -1) })
            Console.WriteLine($"{e.ListChangedType} {e.NewIndex} {e.OldIndex} {e.PropertyDescriptor == null}");
    }
}
