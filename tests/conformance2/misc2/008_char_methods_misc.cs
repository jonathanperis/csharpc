Console.WriteLine($"{char.GetNumericValue('7')} {char.IsPunctuation('!')} {char.IsSymbol('+')} {char.IsControl('\n')} {char.IsLetterOrDigit('_')} {(int)char.MaxValue}");
Console.WriteLine(string.Concat("Hello".Select(c => char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c))));
