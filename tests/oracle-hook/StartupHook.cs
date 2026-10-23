// A .NET startup hook (DOTNET_STARTUP_HOOKS) for recording expectations on machines whose user
// culture isn't the invariant one (Windows): the tests' expectations come from Linux with the C
// locale, where .NET's current culture is the invariant culture with ICU's root collation, which is
// what csharpc implements. DOTNET_SYSTEM_GLOBALIZATION_INVARIANT would also make comparisons ordinal,
// so instead the hook only switches the current cultures before Main runs.
using System.Globalization;

internal static class StartupHook
{
    public static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
