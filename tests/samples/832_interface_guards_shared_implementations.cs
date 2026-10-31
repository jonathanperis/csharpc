// Interface calls whose implementing classes share a few implementations (inherited from a base, as
// Kestrel's HttpProtocol subclasses share its IFeatureCollection): every receiver class must reach
// its own class's implementation, overrides and re-implementations included, and receivers no
// guard knows (a boxed struct, a variant interface) must still dispatch through the cache.
using System;
using System.Collections.Generic;

interface IFeature { int Revision { get; } string Get(string key); }

abstract class Protocol : IFeature
{
    protected int _revision;
    int IFeature.Revision => _revision;
    public virtual string Get(string key) => $"{GetType().Name}.{key}";
}
class Http1 : Protocol { public Http1() => _revision = 1; }
class Http2 : Protocol { public Http2() => _revision = 2; }
class Http3 : Protocol { public Http3() => _revision = 3; public override string Get(string key) => "h3:" + key; }
sealed class Http3Sub : Http3 { public Http3Sub() => _revision = 33; }
class Collection : IFeature { public int Revision => 10; public string Get(string key) => "collection:" + key; }
class Reimplemented : Http1, IFeature { int IFeature.Revision => 99; }

interface IKind { string Kind(); }
struct Boxed : IKind { public string Kind() => "boxed"; }
class Named : IKind { public string Kind() => "named"; }

interface ISource<out T> { T Next(); }
class Counter : ISource<string> { int _n; public string Next() => "n" + _n++; }
class Words : ISource<object> { public object Next() => "w"; }

static class Program
{
    static string Use(IFeature f) => $"{f.Revision}/{f.Get("k")}";
    static object Pull(ISource<object> s) => s.Next();

    static void Main()
    {
        IFeature[] features = [new Http1(), new Http2(), new Http3(), new Http3Sub(), new Collection(), new Reimplemented(), new Http1()];
        for (int round = 0; round < 2; round++)
        {
            var parts = new List<string>();
            foreach (var f in features) parts.Add(Use(f));
            Console.WriteLine(string.Join(" ", parts));
        }
        IKind[] kinds = [new Named(), new Boxed(), new Named()];
        foreach (var k in kinds) Console.Write(k.Kind() + " ");
        Console.WriteLine();
        Console.WriteLine($"{Pull(new Counter())} {Pull(new Words())} {Pull(new Counter())}");
        int sum = 0;
        for (int i = 0; i < 1000; i++) sum += features[i % features.Length].Revision;
        Console.WriteLine(sum);
    }
}
