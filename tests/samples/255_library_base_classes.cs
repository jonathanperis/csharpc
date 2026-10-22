// User classes deriving from EventArgs, Attribute and exception types; the EventHandler<T> and
// INotifyPropertyChanged event patterns, including events of library interfaces.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class FileFoundArgs : EventArgs
{
    public string FoundFile { get; }
    public bool CancelRequested { get; set; }
    public FileFoundArgs(string fileName) => FoundFile = fileName;
}

internal class SearchDirectoryArgs : EventArgs
{
    internal string CurrentSearchDirectory { get; }
    internal SearchDirectoryArgs(string dir) => CurrentSearchDirectory = dir;
}

public class FileSearcher
{
    public event EventHandler<FileFoundArgs>? FileFound;
    public event EventHandler? Done;
    internal event EventHandler<SearchDirectoryArgs>? DirectoryChanged
    {
        add { _directoryChanged += value; }
        remove { _directoryChanged -= value; }
    }
    private EventHandler<SearchDirectoryArgs>? _directoryChanged;

    public void Search(IEnumerable<string> files)
    {
        _directoryChanged?.Invoke(this, new SearchDirectoryArgs("root"));
        foreach (var f in files)
        {
            var args = new FileFoundArgs(f);
            FileFound?.Invoke(this, args);
            if (args.CancelRequested) break;
        }
        Done?.Invoke(this, EventArgs.Empty);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuthorAttribute : Attribute
{
    public string Name { get; }
    public double Version { get; set; } = 1.0;
    public AuthorAttribute(string name) => Name = name;
}

[Author("P. Ackerman")]
[Author("R. Koch", Version = 2.0)]
public class Person : INotifyPropertyChanged
{
    private string _name = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class InsufficientFundsException : InvalidOperationException
{
    public decimal Shortfall { get; }
    public InsufficientFundsException(decimal shortfall) : base($"Short by {shortfall}") => Shortfall = shortfall;
    public InsufficientFundsException(string message, Exception inner) : base(message, inner) { }
}

public class ValidationException : Exception
{
    public ValidationException() { }
    public override string Message => "Validation failed: " + base.Message;
}

class Program
{
    static void Main()
    {
        var searcher = new FileSearcher();
        int count = 0;
        searcher.FileFound += (sender, e) => { Console.WriteLine($"found {e.FoundFile} from {sender?.GetType().Name}"); e.CancelRequested = ++count == 2; };
        searcher.DirectoryChanged += (sender, e) => Console.WriteLine("dir " + e.CurrentSearchDirectory);
        EventHandler done = (s, e) => Console.WriteLine($"done {e == EventArgs.Empty} {e.GetType()} {e}");
        searcher.Done += done;
        searcher.Search(new[] { "a.txt", "b.txt", "c.txt" });
        searcher.Done -= done;
        searcher.Search(new[] { "d.txt" });

        var p = new Person();
        p.PropertyChanged += (s, e) => Console.WriteLine($"changed {e.PropertyName}");
        p.Name = "Ann";
        p.Name = "Ann";
        p.Name = "Bob";
        INotifyPropertyChanged n = p;
        PropertyChangedEventHandler h = (s, e) => Console.WriteLine("via interface " + e.PropertyName);
        n.PropertyChanged += h;
        p.Name = "Cy";
        n.PropertyChanged -= h;
        n.PropertyChanged += (s, e) => Console.WriteLine("lambda " + e.PropertyName);
        p.Name = "Dee";

        try { throw new InsufficientFundsException(12.5m); }
        catch (InvalidOperationException ex) when (ex is InsufficientFundsException f) { Console.WriteLine($"{ex.Message} {f.Shortfall} {ex.GetType().Name}"); }
        try { throw new InsufficientFundsException("outer", new ArgumentException("inner")); }
        catch (Exception ex) { Console.WriteLine($"{ex.Message} / {ex.InnerException?.Message} / {ex.InnerException?.GetType()}"); }
        try { throw new ValidationException(); }
        catch (ValidationException ex) { Console.WriteLine(ex.Message); }
        var a = new AuthorAttribute("Z") { Version = 3 };
        Console.WriteLine($"{a.Name} {a.Version} {a is Attribute} {a.GetType().FullName}");
        var c = new CancelEventArgs();
        c.Cancel = true;
        Console.WriteLine(c.Cancel + " " + new EventArgs().GetType().FullName + " " + (EventArgs.Empty is FileFoundArgs));
    }
}
