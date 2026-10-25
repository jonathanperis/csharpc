// ObservableCollection<T>: CollectionChanged and PropertyChanged for add, insert, replace, move,
// remove and clear; reentrancy checks; ReadOnlyObservableCollection forwarding; and
// NotifyCollectionChangedEventArgs' constructor checks.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

class Program
{
    static string Items(IList list) => list == null ? "null" : "[" + string.Join(",", Enumerate(list)) + "]";

    static IEnumerable<object> Enumerate(IList list)
    {
        foreach (var o in list) yield return o;
    }

    static void Main()
    {
        var c = new ObservableCollection<string>();
        c.CollectionChanged += (s, e) =>
            Console.WriteLine($"{e.Action} new={Items(e.NewItems)}@{e.NewStartingIndex} old={Items(e.OldItems)}@{e.OldStartingIndex} {ReferenceEquals(s, c)}");
        ((INotifyPropertyChanged)c).PropertyChanged += (s, e) => Console.WriteLine($"  property {e.PropertyName}");

        c.Add("a");
        c.Add("b");
        c.Insert(1, "x");
        c[0] = "A";
        c.Move(0, 2);
        c.Remove("x");
        c.RemoveAt(0);
        Console.WriteLine(string.Join(",", c));
        c.Clear();
        Console.WriteLine(c.Count);

        var r = new ObservableCollection<int>(new[] { 1, 2, 3 });
        var view = new ReadOnlyObservableCollection<int>(r);
        ((INotifyCollectionChanged)view).CollectionChanged += (s, e) => Console.WriteLine($"view {e.Action} {Items(e.NewItems)} {ReferenceEquals(s, view)}");
        r.Add(4);
        Console.WriteLine($"{view.Count} {view[3]}");

        // One listener may change the collection from its handler; two may not.
        var reentrant = new ObservableCollection<int>();
        reentrant.CollectionChanged += (s, e) => { if (reentrant.Count < 3) reentrant.Add(reentrant.Count); };
        reentrant.Add(0);
        Console.WriteLine(string.Join(",", reentrant));
        reentrant.CollectionChanged += (s, e) => { };
        try { reentrant.Add(9); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

        try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, (object)"x", 0); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset, (object)"x", -1); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        var args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new List<int> { 5, 6 }, 2);
        Console.WriteLine($"{Items(args.NewItems)} {args.NewStartingIndex} {args.NewItems.IsReadOnly}");
        try { args.NewItems.Add(1); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
        try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (object)1, 0).NewItems[1].ToString(); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
        Console.WriteLine(ReadOnlyObservableCollection<int>.Empty.Count);
    }
}
