// A jagged array's interfaces through its element's array covariance: string[][] is an IList<object[]>, an IList<object>
// and an IEnumerable<IEnumerable<char>>, as arrays of arrays implement them in .NET.
using System;
using System.Collections;
using System.Collections.Generic;

string[][] jagged = { new[] { "a", "b" }, new[] { "c" } };
object boxed = jagged;

Console.WriteLine(boxed is IList<string[]>);
Console.WriteLine(boxed is IList<object[]>);
Console.WriteLine(boxed is IList<object>);
Console.WriteLine(boxed is IReadOnlyList<IEnumerable<string>>);
Console.WriteLine(boxed is IEnumerable<IEnumerable<char>>);
Console.WriteLine(boxed is ICollection<Array>);
Console.WriteLine(boxed is IList<int[]>);

IList<object[]> asObjects = jagged;
Console.WriteLine(asObjects.Count + " " + asObjects[1].Length + " " + asObjects.IndexOf(jagged[1]));
ICollection<Array> asArrays = jagged;
Console.WriteLine(asArrays.Contains(jagged[0]));

int[][] numbers = { new[] { 1, 2 } };
Console.WriteLine((object)numbers is IList<int[]>);
Console.WriteLine((object)numbers is IList<object>);
Console.WriteLine((object)numbers is IList<Array>);
