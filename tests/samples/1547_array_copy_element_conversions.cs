// Array.Copy / Array.CopyTo / ICollection.CopyTo between arrays of different element types (#365), as Array.CopyImpl does them:
// boxing, unboxing (exact type; Nullable<T> takes null), primitive widening, reference casts, and the failures, with the elements
// copied before a failing one kept.
using System;
using System.Collections;

static class Program
{
    static string Show(Array a)
    {
        var parts = new string[a.Length];
        for (int i = 0; i < a.Length; i++) parts[i] = a.GetValue(i)?.ToString() ?? "null";
        return "[" + string.Join(",", parts) + "]";
    }

    static void Try(string label, Array destination, Action copy)
    {
        try { copy(); Console.WriteLine($"{label}: {Show(destination)}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message} {Show(destination)}"); }
    }

    static void Main()
    {
        int[] ints = { 1, 2, 3 };
        var objects = new object[3];
        Try("box int->object", objects, () => Array.Copy(ints, objects, 3));
        var comparables = new IComparable[3];
        Try("box int->IComparable", comparables, () => ints.CopyTo(comparables, 0));
        var valueTypes = new ValueType[3];
        Try("box int->ValueType", valueTypes, () => Array.Copy(ints, 0, valueTypes, 0, 3));
        var strings = new string[3];
        Try("int->string", strings, () => Array.Copy(ints, strings, 3));

        var unboxed = new int[3];
        Try("unbox object->int", unboxed, () => Array.Copy(new object[] { 7, 8, 9 }, unboxed, 3));
        var partial = new int[3];
        Try("unbox partial", partial, () => Array.Copy(new object[] { 4, "s", 6 }, partial, 3));
        var nulls = new int[2];
        Try("unbox null", nulls, () => Array.Copy(new object[] { 5, null! }, nulls, 2));
        var nullable = new int?[3];
        Try("unbox nullable", nullable, () => Array.Copy(new object[] { 1, null!, 3 }, nullable, 3));
        var longsFromBoxed = new long[1];
        Try("unbox int as long", longsFromBoxed, () => Array.Copy(new object[] { 1 }, longsFromBoxed, 1));

        var longs = new long[3];
        Try("widen int->long", longs, () => Array.Copy(ints, longs, 3));
        var doubles = new double[3];
        Try("widen int->double", doubles, () => ints.CopyTo(doubles, 0));
        var fromChars = new int[2];
        Try("widen char->int", fromChars, () => Array.Copy(new[] { 'a', 'b' }, fromChars, 2));
        var narrow = new short[3];
        Try("narrow int->short", narrow, () => Array.Copy(ints, narrow, 3));
        var floats = new float[2];
        Try("widen long->float", floats, () => Array.Copy(new long[] { 1L << 40, -3 }, floats, 2));

        var cast = new string[2];
        Try("cast object->string", cast, () => Array.Copy(new object[] { "a", "b" }, cast, 2));
        var castFail = new string[2];
        Try("cast object->string fails", castFail, () => Array.Copy(new object[] { "a", 3 }, castFail, 2));
        var collection = new string[1];
        Try("ICollection.CopyTo", collection, () => ((ICollection)new object[] { 3 }).CopyTo(collection, 0));
        var collectionOk = new string[1];
        Try("ICollection.CopyTo ok", collectionOk, () => ((ICollection)new object[] { "x" }).CopyTo(collectionOk, 0));
        object[] covariantStrings = new string[] { "a", "b" };
        var fromCovariant = new int[2];
        Try("unbox from covariant string[]", fromCovariant, () => Array.Copy(covariantStrings, fromCovariant, 2));
        object[] covariantDestination = new string[2];
        Try("box into covariant string[]", covariantDestination, () => Array.Copy(ints, covariantDestination, 2));
        object[] covariantComparables = new IComparable[2];
        Try("box into covariant IComparable[]", covariantComparables, () => Array.Copy(ints, covariantComparables, 2));
        object[] covariantValueTypes = new ValueType[] { 3, 4 };
        var fromValueTypes = new int[2];
        Try("unbox covariant ValueType[]", fromValueTypes, () => Array.Copy(covariantValueTypes, fromValueTypes, 2));

        var boxedFromCollection = new object[2];
        Try("ICollection int[] -> object[]", boxedFromCollection, () => ((ICollection)new[] { 1, 2 }).CopyTo(boxedFromCollection, 0));
    }
}
