static void P(string[] a) => Console.WriteLine($"{a.Length}:[{string.Join("|", a)}]");
P("a,,b,".Split(','));
P("a,,b,".Split(',', StringSplitOptions.RemoveEmptyEntries));
P(" a , b ,".Split(',', StringSplitOptions.TrimEntries));
P("a-b_c".Split(new[] { '-', '_' }));
P("one  two".Split(' ', 2));
P("a::b::c".Split("::"));
