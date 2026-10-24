// The csharpc side of tests/oracle-hook: compiled into the test programs on machines whose user culture
// isn't the invariant one (macOS, Windows; see tests/platform.sh, platform_culture_pin). The recordings come
// from Linux with the C locale, where the current culture is the invariant culture (with ICU's root
// collation); a module initializer makes it current before Main, exactly like the startup hook does for
// real .NET. DOTNET_SYSTEM_GLOBALIZATION_INVARIANT would also make comparisons ordinal.
using System.Globalization;
using System.Runtime.CompilerServices;

internal static class CsharpcTestCulturePin
{
#pragma warning disable CA2255 // the test harness's own initializer
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Pin()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
