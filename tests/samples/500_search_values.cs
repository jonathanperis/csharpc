// System.Buffers.SearchValues over chars and bytes and MemoryExtensions' searches with them, as
// ASP.NET Core's HTTP parsers use them (token characters, header values, path validation).
using System.Buffers;

SearchValues<char> token = SearchValues.Create("!#$%&'*+-.^_`|~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");
SearchValues<char> unicode = SearchValues.Create("äé€\u2028");
SearchValues<byte> digits = SearchValues.Create("0123456789"u8);
SearchValues<char> none = SearchValues.Create(ReadOnlySpan<char>.Empty);

ReadOnlySpan<char> header = "Content-Type: text/plain";
Console.WriteLine($"{header.IndexOfAnyExcept(token)} {header.IndexOfAny(token)} {header.LastIndexOfAny(token)} {header.LastIndexOfAnyExcept(token)}");
Console.WriteLine($"{header.ContainsAny(token)} {header.ContainsAnyExcept(token)} {"abc".AsSpan().ContainsAnyExcept(token)}");
Console.WriteLine($"{token.Contains('a')} {token.Contains(' ')} {token.Contains('ä')} {unicode.Contains('ä')} {unicode.Contains('€')} {unicode.Contains('a')}");
Console.WriteLine($"{"price: 5€".AsSpan().IndexOfAny(unicode)} {"plain".AsSpan().IndexOfAny(unicode)} {"x".AsSpan().IndexOfAny(none)} {"x".AsSpan().IndexOfAnyExcept(none)}");

Span<byte> request = "GET /items/42 HTTP/1.1"u8.ToArray();
Console.WriteLine($"{request.IndexOfAny(digits)} {request.LastIndexOfAny(digits)} {request.IndexOfAnyExcept(digits)} {request.LastIndexOfAnyExcept(digits)}");
Console.WriteLine($"{digits.Contains((byte)'7')} {digits.Contains((byte)'x')} {request.ContainsAny(digits)} {"123"u8.ContainsAnyExcept(digits)}");
Console.WriteLine($"{ReadOnlySpan<char>.Empty.IndexOfAny(token)} {ReadOnlySpan<char>.Empty.IndexOfAnyExcept(token)} {ReadOnlySpan<byte>.Empty.LastIndexOfAny(digits)}");
try { "x".AsSpan().IndexOfAny((SearchValues<char>)null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
