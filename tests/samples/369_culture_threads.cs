// The current culture is per thread: a thread starts with DefaultThreadCurrentCulture (or the user
// culture), and setting CurrentCulture on one thread doesn't change another's.
using System;
using System.Globalization;
using System.Threading;

double d = 1.5;
CultureInfo.CurrentCulture = new CultureInfo("de-DE");
string seen = "";
var t = new Thread(() => seen = CultureInfo.CurrentCulture.Name + " " + d);
t.Start();
t.Join();
Console.WriteLine(CultureInfo.CurrentCulture.Name + " " + d + " | thread: [" + seen + "]");
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("fr-FR");
t = new Thread(() =>
{
    seen = CultureInfo.CurrentCulture.Name + " " + d;
    CultureInfo.CurrentCulture = new CultureInfo("ja-JP");
    seen += " " + CultureInfo.CurrentCulture.Name;
});
t.Start();
t.Join();
Console.WriteLine(CultureInfo.CurrentCulture.Name + " " + d + " | thread: [" + seen + "] " + (CultureInfo.DefaultThreadCurrentCulture?.Name ?? "null"));
CultureInfo.DefaultThreadCurrentCulture = null;
t = new Thread(() => seen = "[" + CultureInfo.CurrentCulture.Name + "] " + d);
t.Start();
t.Join();
Console.WriteLine(seen + " " + Thread.CurrentThread.CurrentCulture.Name);
Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
Console.WriteLine(CultureInfo.CurrentCulture.Name + " " + d);
