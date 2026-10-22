int x = 5;
string s = $$"""{"value": {{x}}}""";
Console.WriteLine(s);
string t = $"""x is {x}""";
Console.WriteLine(t);
