// System.Collections.SortedList (the non-generic one, compiled from the shared framework's IL) and the corelib's
// non-generic Array.BinarySearch view an array as IList / ICollection / IEnumerable with no conversion instruction in
// IL; the arrays' interface maps must include those interfaces (System.Net.CookieContainer keeps its cookies in
// SortedLists). Before the fix the first lookup was an InvalidCastException: object[] to IList.
using System.Collections;

var list = new SortedList();
list.Add("b", 2);
list.Add("a", 1);
list.Add("c", 3);
Console.WriteLine(list["a"] + " " + list["b"] + " " + list["c"] + " count " + list.Count);
Console.WriteLine(list.IndexOfKey("c") + " " + list.IndexOfKey("zzz") + " " + list.ContainsKey("b") + " " + list.GetKey(0) + " " + list.GetByIndex(2));
list.Remove("b");
foreach (DictionaryEntry entry in list) Console.WriteLine(entry.Key + "=" + entry.Value);

Array words = new object[] { "a", "c", "e", "g" };
Console.WriteLine(Array.BinarySearch(words, "c") + " " + Array.BinarySearch(words, "d") + " " + Array.BinarySearch(words, "z"));
Console.WriteLine(Array.BinarySearch(words, 1, 2, "e") + " " + Array.BinarySearch(words, "e", StringComparer.Ordinal));
Console.WriteLine(Array.BinarySearch(new int[] { 1, 3, 5, 7 }, 5) + " " + Array.BinarySearch(new int[] { 1, 3, 5, 7 }, 4));
Console.WriteLine(Array.BinarySearch(new[] { "x", "y", "z" }, "y"));
