// Environment.GetEnvironmentVariables(): a Hashtable of the whole environment (ASP.NET Core's
// environment-variable configuration provider reads it at start-up), consistent with
// Get/SetEnvironmentVariable.
using System;
using System.Collections;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        Environment.SetEnvironmentVariable("CSHARPC_T_A", "1");
        Environment.SetEnvironmentVariable("CSHARPC_T_B", "two words");
        Environment.SetEnvironmentVariable("CSHARPC_T_C", "x=y");
        Environment.SetEnvironmentVariable("CSHARPC_T_B", "replaced");
        Environment.SetEnvironmentVariable("CSHARPC_T_C", null);
        var all = Environment.GetEnvironmentVariables();
        Console.WriteLine(all.GetType());
        Console.WriteLine(all["CSHARPC_T_A"]);
        Console.WriteLine(all.Contains("CSHARPC_T_C") + " " + (all["CSHARPC_T_MISSING"] == null));
        var mine = new List<string>();
        foreach (DictionaryEntry e in all)
            if (((string)e.Key).StartsWith("CSHARPC_T_", StringComparison.Ordinal)) mine.Add(e.Key + "=" + e.Value);
        mine.Sort(StringComparer.Ordinal);
        Console.WriteLine(string.Join(",", mine));
        Console.WriteLine(all.Count > 3);
    }
}
