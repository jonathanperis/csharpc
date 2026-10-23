// AppContext switches and data: runtimeconfig defaults of a file-based app, SetSwitch/SetData,
// string values parsed as switches, argument validation.
using System;

static class Program
{
    static void Show(string name)
    {
        var found = AppContext.TryGetSwitch(name, out var enabled);
        Console.WriteLine($"{name}: found={found} enabled={enabled} data={AppContext.GetData(name) ?? "null"}");
    }

    static void Main()
    {
        Show("System.Resources.UseSystemResourceKeys");
        Show("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization");
        Show("System.Linq.Enumerable.IsSizeOptimized");
        Show("System.Globalization.Invariant");
        Show("My.Undefined.Switch");

        AppContext.SetSwitch("My.Switch", true);
        Show("My.Switch");
        AppContext.SetSwitch("My.Switch", false);
        Show("My.Switch");

        AppContext.SetData("My.Data", "True");
        Show("My.Data");
        AppContext.SetData("My.Number", 42);
        Console.WriteLine(AppContext.GetData("My.Number"));
        AppContext.SetData("My.Number", null);
        Console.WriteLine(AppContext.GetData("My.Number") is null);
        AppContext.SetData("My.Text", "not a bool");
        Show("My.Text");

        try { AppContext.TryGetSwitch(null!, out _); }
        catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
        try { AppContext.TryGetSwitch("", out _); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { AppContext.SetSwitch("", true); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { AppContext.GetData(null!); }
        catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
        try { AppContext.SetData(null!, 1); }
        catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
    }
}
