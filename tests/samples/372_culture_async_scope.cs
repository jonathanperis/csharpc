// A culture set inside an async method stays inside it (.NET restores the caller's ExecutionContext when
// the method returns to it); a culture set by a synchronous method stays set for the caller.
using System;
using System.Globalization;
using System.Threading.Tasks;

CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
await SetAndYield();
Console.WriteLine("after async (awaits): " + CultureInfo.CurrentCulture.Name);
await SetWithoutAwait();
Console.WriteLine("after async (no await): " + CultureInfo.CurrentCulture.Name);
Task pending = SetAndDelay();
Console.WriteLine("while pending: " + CultureInfo.CurrentCulture.Name);
await pending;
Console.WriteLine("after pending: " + CultureInfo.CurrentCulture.Name);
SetSync();
Console.WriteLine("after sync: " + CultureInfo.CurrentCulture.Name);
Console.WriteLine(await Nested());
Console.WriteLine("end: " + CultureInfo.CurrentCulture.Name + " " + 2.5);

static async Task SetAndYield()
{
    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
    await Task.Yield();
    Console.WriteLine("inside: " + CultureInfo.CurrentCulture.Name);
}

#pragma warning disable CS1998
static async Task SetWithoutAwait() => CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
#pragma warning restore CS1998

static async Task SetAndDelay()
{
    CultureInfo.CurrentCulture = new CultureInfo("ja-JP");
    await Task.Delay(20);
    Console.WriteLine("delayed: " + CultureInfo.CurrentCulture.Name);
}

static void SetSync() => CultureInfo.CurrentCulture = new CultureInfo("es-ES");

static async Task<string> Nested()
{
    string before = CultureInfo.CurrentCulture.Name;
    await SetAndYield();
    return "nested: " + before + " -> " + CultureInfo.CurrentCulture.Name;
}
