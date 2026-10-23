using System.Globalization;
using System.Resources;

namespace IlResources;

// The shape of the framework's SR classes: a ResourceManager over typeof(<resource class>).
internal static class Strings
{
    private static ResourceManager? s_manager;

    internal static ResourceManager Manager => s_manager ??= new ResourceManager(typeof(Strings));

    internal static string Get(string key) => Manager.GetString(key) ?? key;

    internal static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);
}

public sealed class OutOfStockException(string item, int requested)
    : InvalidOperationException(Strings.Format("OutOfStock", item, requested));

public static class Catalog
{
    public static string Greeting => Strings.Get("Greeting");
    public static string Unicode => Strings.Get("Unicode");
    public static string Empty => "[" + Strings.Manager.GetString("Empty") + "]";
    public static string Missing => Strings.Manager.GetString("NoSuchKey") ?? "null";
    public static string CaseInsensitive()
    {
        var manager = new ResourceManager(typeof(Strings)) { IgnoreCase = true };
        return manager.GetString("greeting") ?? "null";
    }

    public static void Order(string item, int count)
    {
        if (count > 3) throw new OutOfStockException(item, count);
    }

    public static string Verbose =>
        AppContext.TryGetSwitch("IlResources.Verbose", out var on) ? (on ? "verbose on" : "verbose off") : "verbose unset";

    public static string Limit => AppContext.GetData("IlResources.Limit") as string ?? "no limit";
}
