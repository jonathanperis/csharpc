static IEnumerable<string> Words(string text) { var sb = new System.Text.StringBuilder(); foreach (var c in text) { if (c == ' ') { if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); } } else sb.Append(c); } if (sb.Length > 0) yield return sb.ToString(); }
Console.WriteLine(string.Join("|", Words("  the quick  brown fox ")));
