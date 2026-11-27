// System.ObjectModel / System.Collections.NonGeneric throughput, the program behind the numbers of ADR 0023's ObjectModel row:
//   tools/il-first/ab.sh System.ObjectModel,System.Collections.NonGeneric tools/il-first/bench/observable_throughput.cs
// ObservableCollection (change notifications, moves, resets), ReadOnlyObservableCollection, a CollectionBase subclass and
// PropertyChanged raising. The output is checksums only.
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

class IntBag : CollectionBase
{
    public int Add(int v) => List.Add(v);
    public int this[int i] => (int)List[i]!;
    protected override void OnInsertComplete(int index, object? value) => Inserted++;
    public int Inserted;
}

class Model : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    int _value;
    public int Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
}

class Program
{
    static void Main()
    {
        long check = 0;
        for (int round = 0; round < 40; round++)
        {
            var oc = new ObservableCollection<int>();
            int changes = 0;
            oc.CollectionChanged += (s, e) => changes += e.Action == NotifyCollectionChangedAction.Add ? 2 : 1;
            for (int i = 0; i < 20_000; i++) oc.Add(i);
            for (int i = 0; i < 5_000; i++) oc.Move(i, oc.Count - 1 - i);
            for (int i = 0; i < 5_000; i++) oc.Insert(i * 2, -i);
            for (int i = 0; i < 10_000; i++) oc.RemoveAt(oc.Count - 1);
            var view = new ReadOnlyObservableCollection<int>(oc);
            foreach (int v in view) check += v & 7;
            check += changes + view.Count;
            oc.Clear();

            var bag = new IntBag();
            for (int i = 0; i < 20_000; i++) bag.Add(i);
            for (int i = 0; i < bag.Count; i++) check += bag[i] & 3;
            check += bag.Inserted;

            var model = new Model();
            int raised = 0;
            model.PropertyChanged += (s, e) => raised += e.PropertyName!.Length;
            for (int i = 0; i < 100_000; i++) model.Value = i;
            check += raised;
        }
        Console.WriteLine(check);
    }
}
