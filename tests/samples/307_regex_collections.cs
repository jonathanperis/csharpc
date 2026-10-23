// MatchCollection, GroupCollection and CaptureCollection as collections: indexers, CopyTo,
// IList/IReadOnlyList views, LINQ, enumeration and their exceptions.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        MatchCollection matches = Regex.Matches("cat bat rat hat", @"(\w)at");
        Console.WriteLine(matches.Count + " " + matches[1].Value + " " + matches.IsReadOnly + " " + matches.IsSynchronized);
        Console.WriteLine(string.Join(",", matches.Select(m => m.Groups[1].Value)));
        Console.WriteLine(string.Join(",", matches.Cast<Match>().Where(m => m.Index > 3).Select(m => m.Value)));
        Console.WriteLine(matches.First().Value + " " + matches.Last().Index + " " + matches.Count(m => m.Value.StartsWith('r')));
        IList<Match> list = matches;
        Console.WriteLine(list.IndexOf(matches[2]) + " " + list.Contains(matches[0]) + " " + list.Count);
        IReadOnlyList<Match> ro = matches;
        Console.WriteLine(ro[3].Value);
        var array = new Match[5];
        matches.CopyTo(array, 1);
        Console.WriteLine((array[0] == null) + " " + array[4].Value);
        ICollection nongeneric = matches;
        var objects = new object[4];
        nongeneric.CopyTo(objects, 0);
        Console.WriteLine(((Match)objects[3]).Value);
        IEnumerator e = ((IEnumerable)matches).GetEnumerator();
        while (e.MoveNext()) Console.Write(((Match)e.Current).Index + " ");
        Console.WriteLine();

        try { _ = matches[10]; }
        catch (ArgumentOutOfRangeException ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { list.Add(matches[0]); }
        catch (NotSupportedException ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }

        Match m2 = Regex.Match("x=1,y=22", @"(?<name>\w)=(?<val>\d+)(,(?<name>\w)=(?<val>\d+))*");
        GroupCollection groups = m2.Groups;
        Console.WriteLine(groups.Count + " " + string.Join(",", groups.Keys) + " " + groups.IsReadOnly);
        Console.WriteLine(string.Join(",", ((IEnumerable<Group>)groups).Select(g => g.Name + ":" + g.Value)));
        Console.WriteLine(string.Join(",", ((IEnumerable<KeyValuePair<string, Group>>)groups).Select(kv => kv.Key)));
        CaptureCollection caps = groups["val"].Captures;
        Console.WriteLine(caps.Count + " " + caps[0].Value + " " + caps[1].Value + " " + string.Join("+", caps.Select(c => c.Value)));
        var capArray = new Capture[2];
        caps.CopyTo(capArray, 0);
        Console.WriteLine(capArray[1].Index + " " + caps.IsReadOnly);
        try { _ = caps[5]; }
        catch (ArgumentOutOfRangeException ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        Console.WriteLine(groups.Values.Count() + " " + groups[1].Captures.Count);
        Console.WriteLine(m2.Captures.Count + " " + m2.Groups.Values.First().Value);
        Console.WriteLine(m2.ToString() + " " + groups["name"].ToString());
    }
}
