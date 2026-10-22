// Ill-formed UTF-8 on stdin (input from 174_utf8_input.stdin) decodes to U+FFFD exactly like .NET's
// UTF8Encoding: no over-reads on truncated sequences, no overlongs, surrogates or > U+10FFFF.
string? line;
while ((line = Console.ReadLine()) != null)
    Console.WriteLine(string.Join(" ", line.Select(c => ((int)c).ToString("X4"))));
