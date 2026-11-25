// PropertyInfo.ToString lists an indexer's index parameter types, as .NET does (#159); other properties print "Type Name".
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

class Grid
{
    public int this[int row] => row;
    public string this[int row, string column] => column + row;
    public List<int>? this[double key, Grid other, int[] values] => null;
    public int Count { get; set; }
    public Dictionary<string, int[]>? Map { get; set; }
}

static class Program
{
    static void Show([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        foreach (var p in type.GetProperties().OrderBy(p => p.ToString(), StringComparer.Ordinal))
            Console.WriteLine($"{p} | index {p.GetIndexParameters().Length}");
    }

    static void Main() => Show(typeof(Grid));
}
