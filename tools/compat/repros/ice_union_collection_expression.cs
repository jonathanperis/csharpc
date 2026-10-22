// Internal compiler error: C# 15 union types are not supported, and instead of a CSLLVM0001
// diagnostic csharpc crashes (SIGSEGV; in the project build a stack overflow inside LLVMBuildStore from StoreElements /
// EmitCollectionExpression) when a collection expression converts case values to the union.
// .NET: 23.5 / on / offline
// Found in dotnet/docs docs/csharp/whats-new/tutorials/snippets/shared/telemetry-monitor (SmartHome.App).
using System;

Reading[] readings = [23.5, true, "offline"];
foreach (Reading r in readings)
{
    Console.WriteLine(r switch
    {
        double d => $"{d:F1}",
        bool b => b ? "on" : "off",
        string s => s,
        null => "none",
    });
}

public union Reading(double, bool, string?);
