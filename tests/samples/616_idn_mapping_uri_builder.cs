// IdnMapping (the managed punycode of invariant globalization mode, as ASP.NET Core's HostString uses
// it) and UriBuilder: components from a Uri, setters, and ToString's composition.
#nullable disable
using System;
using System.Globalization;

class Program
{
    static void Main()
    {
        var idn = new IdnMapping();
        Console.WriteLine(idn.GetAscii("bücher.example"));
        Console.WriteLine(idn.GetUnicode("xn--bcher-kva.example"));
        Console.WriteLine(idn.GetAscii("abc.déf.ghi", 4, 3));
        Console.WriteLine(idn.GetAscii("plain.host"));
        try { idn.GetAscii(""); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { idn.GetUnicode("xn--"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        Console.WriteLine($"{idn.AllowUnassigned} {idn.UseStd3AsciiRules} {idn.Equals(new IdnMapping())} {idn.GetHashCode() == new IdnMapping().GetHashCode()}");

        var builder = new UriBuilder(new Uri("http://api.example.localhost:5000/path?q=1#top"));
        Console.WriteLine($"{builder.Scheme} {builder.Host} {builder.Port} {builder.Path} {builder.Query} {builder.Fragment}");
        builder.Host = "localhost";
        Console.WriteLine(builder.ToString());
        Console.WriteLine(new UriBuilder(new Uri("https://example.com/")).ToString());
        var made = new UriBuilder("http", "example.org", 8080, "a/b") { Query = "x=1", Fragment = "f" };
        Console.WriteLine($"{made} {made.Uri.AbsoluteUri}");
        Console.WriteLine(new UriBuilder().ToString());
        try { made.Port = 70000; } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
        try { made.Host = "bad/host"; } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
    }
}
