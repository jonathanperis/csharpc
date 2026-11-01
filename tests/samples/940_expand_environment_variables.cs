// Environment.ExpandEnvironmentVariables: set and unset names, lone and doubled %, empty input, null.
using System;

class Program
{
    static void Main()
    {
        Environment.SetEnvironmentVariable("CSHARPC_EXPAND_A", "alpha");
        Environment.SetEnvironmentVariable("CSHARPC_EXPAND_B", "%CSHARPC_EXPAND_A%");
        string[] inputs =
        {
            "", "plain", "%CSHARPC_EXPAND_A%", "x%CSHARPC_EXPAND_A%y", "%CSHARPC_EXPAND_A%%CSHARPC_EXPAND_A%",
            "%CSHARPC_EXPAND_MISSING%", "%CSHARPC_EXPAND_MISSING%CSHARPC_EXPAND_A%", "50% off", "%", "%%", "%%%",
            "%CSHARPC_EXPAND_B%", "a%%CSHARPC_EXPAND_A%%b",
        };
        foreach (var s in inputs) Console.WriteLine($"[{s}] -> [{Environment.ExpandEnvironmentVariables(s)}]");
        try { Environment.ExpandEnvironmentVariables(null!); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
    }
}
