using System;
enum Color : byte { Red }
class P { static void Main() {
  foreach (var t in new[] { typeof(int), typeof(string), typeof(Color), typeof(DateTime), typeof(decimal), typeof(object), typeof(DBNull), typeof(Guid), typeof(char), typeof(ulong), typeof(int?) })
    Console.Write(Type.GetTypeCode(t) + " ");
  Console.WriteLine(Type.GetTypeCode(null)); } }
