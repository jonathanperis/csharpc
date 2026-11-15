// A generic interface method implemented by structs and called through a type parameter, with its own type argument (Base64's
// IBase64Decoder<T>.DecodeWithWhiteSpaceBlockwiseWrapper<TDecoder>, #423), and type tests on a boxed T (`decoder is ByteDecoder`) and
// typeof comparisons that decide a branch once T is known.
using System;

interface IDecoder<T>
{
    int Size { get; }
    string Wrap<TDecoder>(TDecoder inner, T value) where TDecoder : struct, IDecoder<T>;
    string Name();
}

struct ByteDecoder : IDecoder<byte>
{
    public int Size => 1;
    public string Wrap<TDecoder>(TDecoder inner, byte value) where TDecoder : struct, IDecoder<byte> => $"byte[{inner.Name()}:{value}:{typeof(TDecoder).Name}]";
    public string Name() => "B";
}

struct UrlDecoder : IDecoder<byte>
{
    public int Size => 2;
    public string Wrap<TDecoder>(TDecoder inner, byte value) where TDecoder : struct, IDecoder<byte> => $"url[{inner.Name()}:{value + inner.Size}]";
    public string Name() => "U";
}

struct CharDecoder : IDecoder<ushort>
{
    public int Size => 4;
    public string Wrap<TDecoder>(TDecoder inner, ushort value) where TDecoder : struct, IDecoder<ushort> => $"char[{inner.Name()}:{value}]";
    public string Name() => "C";
}

static class Program
{
    static string Run<TDecoder, T>(TDecoder decoder, T value) where TDecoder : struct, IDecoder<T>
    {
        // Constrained calls of a generic virtual method on the type parameter, with the caller's type argument.
        string wrapped = decoder.Wrap<TDecoder>(decoder, value);
        // A boxed T tested against struct types, and typeof equality, both constant once TDecoder is known.
        string kind = decoder is ByteDecoder ? "is-byte" : decoder is CharDecoder ? "is-char" : "other";
        string same = typeof(TDecoder) == typeof(UrlDecoder) ? "url" : typeof(TDecoder) != typeof(CharDecoder) ? "not-char" : "char";
        object boxed = decoder;
        return $"{wrapped} {kind} {same} {boxed is IDecoder<T>} {boxed.GetType().Name}";
    }

    static void Main()
    {
        Console.WriteLine(Run<ByteDecoder, byte>(default, 7));
        Console.WriteLine(Run<UrlDecoder, byte>(default, 7));
        Console.WriteLine(Run<CharDecoder, ushort>(default, 300));
    }
}
