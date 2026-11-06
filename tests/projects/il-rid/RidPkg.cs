namespace RidPkg;

// The portable build (lib/) says so; the platform's build (runtimes/<rid>/lib/) names its runtime identifier, which the
// host and csharpc must select instead.
public static class Info
{
#if RID_SPECIFIC
    public static string Describe() => "runtime-specific asset";
#else
    public static string Describe() => "portable asset";
#endif

    public static int Twice(int value) => value * 2;
}
