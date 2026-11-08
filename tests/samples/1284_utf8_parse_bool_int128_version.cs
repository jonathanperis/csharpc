// Utf8Parser.TryParse(bool); the UTF-8 TryParse of Int128, UInt128 and Version.
using System;
using System.Buffers.Text;
using System.Text;

static void ParseBool(string s, char format = default)
{
    bool ok = Utf8Parser.TryParse(Encoding.UTF8.GetBytes(s), out bool value, out int consumed, format);
    Console.WriteLine($"bool '{s}' {(int)format} -> {ok} {value} {consumed}");
}

ParseBool("True");
ParseBool("False");
ParseBool("true");
ParseBool("false");
ParseBool("TRUE");
ParseBool("Truex");
ParseBool("Tru");
ParseBool("", 'G');
ParseBool("true", 'l');
ParseBool("True", 'l');

{
    bool ok = Int128.TryParse("-170141183460469231731687303715884105728"u8, null, out Int128 value);
    Console.WriteLine(ok + " " + value);
    ok = Int128.TryParse("12x"u8, null, out value);
    Console.WriteLine(ok + " " + value);
    ok = UInt128.TryParse("340282366920938463463374607431768211455"u8, null, out UInt128 big);
    Console.WriteLine(ok + " " + big);
    ok = UInt128.TryParse("-1"u8, null, out big);
    Console.WriteLine(ok + " " + big);
    ok = Version.TryParse("1.2.3.4"u8, out Version version);
    Console.WriteLine(ok + " " + version);
    ok = Version.TryParse("1"u8, out version);
    Console.WriteLine(ok + " " + (version == null));
}
