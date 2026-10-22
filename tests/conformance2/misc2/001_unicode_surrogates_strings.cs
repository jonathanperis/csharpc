var s = "a😀b";
Console.WriteLine($"{s.Length} {s[1] == '\uD83D'} {char.IsSurrogate(s[2])} {s.IndexOf('b')}");
Console.WriteLine(s.Substring(1, 2));
Console.WriteLine(new string(s.Reverse().ToArray()).Length);
