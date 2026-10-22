var sb = new System.Text.StringBuilder("abc");
sb.Insert(0, 12).Append('x', 3).AppendLine().Replace("b", "B").Remove(0, 1);
Console.Write(sb.ToString());
Console.WriteLine(sb.Length + " " + sb[1]);
sb.Clear(); sb.AppendFormat("{0}-{1:D2}", "n", 5); sb.Append(1.5).Append(true);
Console.WriteLine(sb);
