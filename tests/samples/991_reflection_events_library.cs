// EventInfo for events of library types and of types implementing library interfaces: INotifyPropertyChanged
// (the MVVM binding pattern: find the event by name, check its handler type, subscribe through AddEventHandler and
// get notified), Console.CancelKeyPress and an event with a generic handler type declared by a generic base class.
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

var viewModel = new ViewModel();
EventInfo? changed = typeof(ViewModel).GetEvent("PropertyChanged");
Console.WriteLine($"{changed?.Name} {changed?.EventHandlerType} {changed?.DeclaringType} {changed?.ReflectedType} {changed?.IsMulticast}");
Console.WriteLine(changed == typeof(INotifyPropertyChanged).GetEvent("PropertyChanged"));
Console.WriteLine(typeof(INotifyPropertyChanged).GetEvent("PropertyChanged")!.DeclaringType);

var seen = new System.Collections.Generic.List<string>();
changed!.AddEventHandler(viewModel, new PropertyChangedEventHandler((sender, e) => seen.Add(e.PropertyName ?? "?")));
viewModel.Title = "first";
viewModel.Title = "second";
Console.WriteLine(string.Join(",", seen));

Console.WriteLine(string.Join(", ", typeof(ViewModel).GetEvents().Select(e => e.Name + ":" + e.EventHandlerType)));
Console.WriteLine(string.Join(", ", typeof(INotifyPropertyChanged).GetEvents().Select(e => e.Name + ":" + e.EventHandlerType!.Name)));

EventInfo? cancel = typeof(Console).GetEvent("CancelKeyPress");
Console.WriteLine($"{cancel?.Name} {cancel?.EventHandlerType} {cancel?.AddMethod?.IsStatic} {cancel?.DeclaringType} {cancel?.IsMulticast} {cancel?.IsSpecialName}");
Console.WriteLine(typeof(Console).GetEvent("NoSuchEvent") is null);

var repository = new Repository();
EventInfo? loaded = typeof(Repository).GetEvent("Loaded");
Console.WriteLine($"{loaded?.Name} {loaded?.EventHandlerType} {loaded?.DeclaringType} {loaded?.ReflectedType}");
loaded!.AddEventHandler(repository, new EventHandler<string>((_, item) => Console.WriteLine("loaded " + item)));
repository.Load("alpha");
Console.WriteLine(typeof(Repository).GetEvents().Length + " " + typeof(Store<string>).GetEvents().Length);

class ViewModel : INotifyPropertyChanged
{
    private string _title = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Title
    {
        get => _title;
        set { _title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title))); }
    }
}

class Store<T>
{
    public event EventHandler<T>? Loaded;
    protected void OnLoaded(T item) => Loaded?.Invoke(this, item);
}

class Repository : Store<string>
{
    public void Load(string item) => OnLoaded(item);
}
