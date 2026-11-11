// Hash flooding: Dictionary<string, V> and HashSet<string> start with a fast non-randomized hash and switch to a randomized one when an
// operation meets more than 100 colliding entries. The keys below are built to all have the same non-randomized hash (the documented
// algorithm of string.GetNonRandomizedHashCode, written out here), so without the switch every insert walks the whole chain: the 200 000
// key flood at the end would take minutes and the test run's time limit would fail it. The output holds no hash values and no timing:
// the collections must stay correct through the switch (every key found, counts, removal, enumeration, Comparer, alternate lookups).
using System;
using System.Collections.Generic;

class Program
{
    static uint Rol5(uint v) => (v << 5) | (v >> 27);

    // string.GetNonRandomizedHashCode for a string of exactly four chars (one iteration of the pair loop)
    static uint Hash4(uint pair0, uint pair1)
    {
        const uint h = (5381 << 16) + 5381;
        uint hash1 = (Rol5(h) + h) ^ pair0;
        uint hash2 = (Rol5(h) + h) ^ pair1;
        return hash1 + hash2 * 1566083941;
    }

    static uint Inverse(uint odd)
    {
        uint x = odd; // Newton's iteration: each step doubles the correct low bits
        for (int i = 0; i < 5; i++) x *= 2 - odd * x;
        return x;
    }

    static string[] CollidingKeys(int count)
    {
        const uint h = (5381 << 16) + 5381;
        uint x = Rol5(h) + h;
        uint inverse = Inverse(1566083941);
        uint target = Hash4(0x00410041, 0x00420042);
        var keys = new string[count];
        for (int i = 0; i < count; i++)
        {
            // pair0 counts up; pair1 is solved so that the hash stays the target
            uint a = (uint)i + 0x00410041;
            uint b = x ^ ((target - (x ^ a)) * inverse);
            keys[i] = new string(new[] { (char)(a & 0xFFFF), (char)(a >> 16), (char)(b & 0xFFFF), (char)(b >> 16) });
        }
        if (Hash4(0x00410041 + 5, x ^ ((target - (x ^ (0x00410041 + 5))) * inverse)) != target) throw new InvalidOperationException("not colliding");
        return keys;
    }

    static void Main()
    {
        var keys = CollidingKeys(400);
        Console.WriteLine($"{keys.Length} keys, {new HashSet<string>(keys).Count} distinct");

        foreach (var comparer in new IEqualityComparer<string>[] { null, EqualityComparer<string>.Default, StringComparer.Ordinal })
        {
            var dictionary = comparer is null ? new Dictionary<string, int>() : new Dictionary<string, int>(comparer);
            for (var i = 0; i < keys.Length; i++) dictionary[keys[i]] = i;
            Console.WriteLine($"{dictionary.Count} inserts");

            var found = 0;
            for (var i = 0; i < keys.Length; i++) if (dictionary.TryGetValue(keys[i], out var value) && value == i) found++;
            Console.WriteLine($"found {found}, missing {dictionary.ContainsKey("missing")} {dictionary.ContainsKey(keys[0] + "x")}");

            for (var i = 0; i < keys.Length; i += 2) dictionary.Remove(keys[i]);
            var sum = 0L;
            var seen = 0;
            foreach (var pair in dictionary) { sum += pair.Value; seen++; }
            Console.WriteLine($"after removal {dictionary.Count}: enumerated {seen}, sum {sum}");

            var reuse = 0;
            for (var i = 0; i < keys.Length; i += 2) if (dictionary.TryAdd(keys[i], -i)) reuse++;
            Console.WriteLine($"re-added {reuse}, count {dictionary.Count}, comparer {(comparer is null ? dictionary.Comparer == EqualityComparer<string>.Default : dictionary.Comparer == comparer)}");

            var lookup = dictionary.GetAlternateLookup<ReadOnlySpan<char>>();
            Console.WriteLine($"alternate: {lookup.TryGetValue(keys[3].AsSpan(), out var alt)} {alt} {lookup.ContainsKey(keys[0].AsSpan())} {lookup.ContainsKey("nope".AsSpan())}");
        }

        var set = new HashSet<string>();
        foreach (var key in keys) set.Add(key);
        var contained = 0;
        foreach (var key in keys) if (set.Contains(key)) contained++;
        Console.WriteLine($"set {set.Count}: contained {contained}, remove {set.Remove(keys[7])} {set.Remove(keys[7])}, count {set.Count}, add again {set.Add(keys[7])}");
        Console.WriteLine($"set comparer is default: {set.Comparer == EqualityComparer<string>.Default}");

        // The flood: without the switch to the randomized comparer this is quadratic.
        var flood = CollidingKeys(200_000);
        var floodDictionary = new Dictionary<string, int>();
        for (var i = 0; i < flood.Length; i++) floodDictionary[flood[i]] = i;
        var floodSet = new HashSet<string>();
        for (var i = 0; i < flood.Length; i++) floodSet.Add(flood[i]);
        var floodSum = 0L;
        for (var i = 0; i < flood.Length; i += 1000) floodSum += floodDictionary[flood[i]] + (floodSet.Contains(flood[i]) ? 1 : 0);
        Console.WriteLine($"flood: {floodDictionary.Count} {floodSet.Count} {floodSum}");
    }
}
