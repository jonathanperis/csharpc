// Capture groups: numbered, named, explicitly numbered, duplicate names, backreferences,
// captures of repeated groups, balancing groups and the group-name APIs.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

class Program
{
    static void Dump(Regex r, string input)
    {
        Match m = r.Match(input);
        Console.Write($"{r} on '{input}':");
        if (!m.Success) { Console.WriteLine(" no match"); return; }
        foreach (Group g in m.Groups)
        {
            Console.Write($" {g.Name}=");
            Console.Write(g.Success ? $"'{g.Value}'@{g.Index}" : "-");
            if (g.Captures.Count > 1)
            {
                Console.Write("{");
                foreach (Capture c in g.Captures) Console.Write($"{c.Value}@{c.Index},");
                Console.Write("}");
            }
        }
        Console.WriteLine();
    }

    static void Main()
    {
        Dump(new Regex(@"(\w+)\s(\w+)"), "hello world");
        Dump(new Regex(@"(?<first>\w+)\s(?<last>\w+)"), "Ada Lovelace");
        Dump(new Regex(@"(?'y'\d{4})-(?'m'\d\d)"), "on 2023-11");
        Dump(new Regex(@"(a)(?<n>b)(c)"), "abc");
        Dump(new Regex(@"(?<5>a)(b)(?<2>c)"), "abc");
        Dump(new Regex(@"(?<x>a)(?<x>b)"), "ab");
        Dump(new Regex(@"(\w)\1"), "abccd");
        Dump(new Regex(@"(?<ch>\w)\k<ch>"), "xyzzy");
        Dump(new Regex(@"(?<q>['""]).*?\k'q'"), "say \"hi\" or 'yo'");
        Dump(new Regex(@"(\d)+"), "12345");
        Dump(new Regex(@"((a)|(b))+"), "abba");
        Dump(new Regex(@"(a)?b"), "b");
        Dump(new Regex(@"(?:non)(capturing)"), "noncapturing");
        Dump(new Regex(@"^(?:(?<open>\()|(?<close-open>\))|[^()])*(?(open)(?!))$"), "f(a(b)c)");
        Dump(new Regex(@"^(?:(?<open>\()|(?<-open>\))|[^()])*(?(open)(?!))$"), "f(a(b c)");
        Dump(new Regex(@"(?<1>x)(y)"), "xy");

        var r = new Regex(@"(?<year>\d{4})-(?<month>\d{2})(-(?<day>\d{2}))?");
        Console.WriteLine(string.Join(",", r.GetGroupNames()));
        Console.WriteLine(string.Join(",", r.GetGroupNumbers()));
        Console.WriteLine(r.GroupNumberFromName("month") + " " + r.GroupNumberFromName("nope") + " " + r.GroupNumberFromName("1"));
        Console.WriteLine(r.GroupNameFromNumber(1) + "|" + r.GroupNameFromNumber(3) + "|" + r.GroupNameFromNumber(99) + "|");

        Match m = r.Match("2024-02-29");
        Console.WriteLine(m.Groups["year"].Value + " " + m.Groups[3].Value + " " + m.Groups["day"].Index);
        Console.WriteLine(m.Groups["missing"].Success + " '" + m.Groups["missing"].Value + "' " + m.Groups[42].Success);
        Console.WriteLine(m.Groups.ContainsKey("day") + " " + m.Groups.ContainsKey("hour"));
        Console.WriteLine(m.Groups.TryGetValue("month", out Group? month) + " " + month!.Value);
        Console.WriteLine(string.Join(",", m.Groups.Keys));
        foreach (Group g in m.Groups.Values) Console.Write(g.Value + "|");
        Console.WriteLine();
        foreach (KeyValuePair<string, Group> kv in (IEnumerable<KeyValuePair<string, Group>>)m.Groups) Console.Write(kv.Key + "=" + kv.Value.Value + " ");
        Console.WriteLine();
        Console.WriteLine(m.Groups.Count + " " + m.Captures.Count + " " + m.Captures[0].Value);
        Console.WriteLine(m.Groups[0] == m);

        var noNames = new Regex(@"(a)(b)");
        Console.WriteLine(string.Join(",", noNames.GetGroupNames()) + " " + noNames.GroupNumberFromName("2"));
    }
}
