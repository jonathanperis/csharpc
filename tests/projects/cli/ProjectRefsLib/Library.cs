namespace ProjectRefsLib;

public struct Opaque
{
    private string data = "";
    public Opaque() { }
    public override string ToString() => data;
}

public static class Info
{
    public static string Symbols()
    {
        var text = "lib:";
#if LIB_ONLY
        text += " LIB_ONLY";
#endif
#if APP_ONLY
        text += " APP_ONLY (wrong)";
#endif
#if NET11_0_OR_GREATER
        text += " NET11_0_OR_GREATER";
#endif
        return text;
    }
}
