// Two assemblies may define a type of the same full name: they are two types, and so are the arrays over them.
// (Arrays of both shared one method table, keyed by the name, so storing one into an array of the other failed.)
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace System.Text.RegularExpressions
{
    // The same full name as the regex engine's own internal node class (another assembly): two types.
    internal sealed class RegexNode
    {
        public readonly int Id;
        public RegexNode(int id) { Id = id; }
    }
}

static class Program
{
    static void Main()
    {
        // The engine builds its trees in List<RegexNode> (its own RegexNode).
        foreach (var pattern in new[] { "^a", "ab+", "(?<key>\\w+)=(?<value>[^;]*)", "a$" })
            Console.WriteLine(pattern + " " + new Regex(pattern).IsMatch("key=ab;a"));

        var nodes = new List<RegexNode> { new RegexNode(1), new RegexNode(2) };
        nodes[0] = new RegexNode(3);
        RegexNode[] array = nodes.ToArray();
        object[] objects = array;
        objects[1] = new RegexNode(4);
        Console.WriteLine(nodes[0].Id + " " + array[1].Id + " " + array.GetType().Name);
        Console.WriteLine(objects is RegexNode[]);
        try { objects[0] = "not a node"; }
        catch (ArrayTypeMismatchException) { Console.WriteLine("mismatch"); }
    }
}
