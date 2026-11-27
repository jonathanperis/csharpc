// Generic methods of a generic interface implemented by structs and called through a type parameter (#192), the shape of
// System.Buffers.Text.Base64Helper's IBase64Decoder<T>.DecodeWithWhiteSpaceBlockwiseWrapper<TBase64Decoder>: constrained
// instance calls, and static abstract generic methods resolved once the type parameter is known.
using System;

interface IDecoder<T>
{
    int Decode<TSelf>(TSelf self, T value) where TSelf : IDecoder<T>;
    static abstract int Wrap<TSelf>(T value) where TSelf : IDecoder<T>;
    static abstract string Name<TOther>();
}

struct ByteDecoder : IDecoder<byte>
{
    public int Decode<TSelf>(TSelf self, byte value) where TSelf : IDecoder<byte> => value + 1;
    public static int Wrap<TSelf>(byte value) where TSelf : IDecoder<byte> => value * 2;
    public static string Name<TOther>() => "byte/" + typeof(TOther).Name;
}

struct CharDecoder : IDecoder<char>
{
    public int Decode<TSelf>(TSelf self, char value) where TSelf : IDecoder<char> => value - 'a';
    public static int Wrap<TSelf>(char value) where TSelf : IDecoder<char> => TSelf.Name<int>().Length + value;
    public static string Name<TOther>() => "char/" + typeof(TOther).Name;
}

static class Calls
{
    public static int Instance<TDecoder, T>(TDecoder decoder, T value) where TDecoder : IDecoder<T> => decoder.Decode(decoder, value);
    public static int Static<TDecoder, T>(T value) where TDecoder : IDecoder<T> => TDecoder.Wrap<TDecoder>(value);
    public static string Named<TDecoder, T>() where TDecoder : IDecoder<T> => TDecoder.Name<TDecoder>() + " " + TDecoder.Name<string>();
}

static class Program
{
    static void Main()
    {
        Console.WriteLine(Calls.Instance(new ByteDecoder(), (byte)41));
        Console.WriteLine(Calls.Static<ByteDecoder, byte>(21));
        Console.WriteLine(Calls.Instance(new CharDecoder(), 'z'));
        Console.WriteLine(Calls.Static<CharDecoder, char>('A'));
        Console.WriteLine(Calls.Named<ByteDecoder, byte>());
        Console.WriteLine(Calls.Named<CharDecoder, char>());
    }
}
