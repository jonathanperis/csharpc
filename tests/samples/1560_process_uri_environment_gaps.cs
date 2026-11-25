// Members from #102: Environment.UserDomainName, ProcessStartInfo.ErrorDialog, UriBuilder(scheme, host, port, path, extraValue).
#nullable enable
using System;
using System.Diagnostics;

static class Program
{
    static void Try(string name, Func<object?> f)
    {
        try { Console.WriteLine($"{name}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        Try("domain", () => Environment.UserDomainName.Length > 0 && (!OperatingSystem.IsLinux() || Environment.UserDomainName == Environment.MachineName));
        var psi = new ProcessStartInfo("/bin/echo") { ErrorDialog = true };
        Try("ErrorDialog", () => psi.ErrorDialog);
        Try("ErrorDialogParentHandle", () => psi.ErrorDialogParentHandle);
        Try("default", () => new ProcessStartInfo().ErrorDialog);
        Try("query", () => new UriBuilder("https", "example.com", 8443, "/path", "?q=1").Uri);
        Try("fragment", () => new UriBuilder("http", "h", 80, "p", "#frag").ToString());
        Try("both", () => new UriBuilder("http", "h", -1, "a/b", "?x=1#f").Uri);
        Try("parts", () => { var b = new UriBuilder("http", "h", 1, "p", "?x#y"); return b.Query + "|" + b.Fragment; });
        Try("empty", () => new UriBuilder("http", "h", 1, "p", "").Uri);
        Try("null", () => new UriBuilder("http", "h", 1, "p", null).Uri);
        Try("bad", () => new UriBuilder("http", "h", 1, "p", "x=1").Uri);
    }
}
