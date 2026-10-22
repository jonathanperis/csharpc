var sb = new System.Text.StringBuilder();
for (int i = 0; i < 3; i++) sb.Append(i).Append(';');
sb.Insert(0, "[").Append(']');
Console.WriteLine(sb.ToString() + " " + sb.Length);
sb.Replace(";", ",");
Console.WriteLine(sb);
