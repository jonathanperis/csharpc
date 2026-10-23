using ProjectRefsLib;

Opaque value;
Assign(out value);
System.Console.WriteLine("assigned");
System.Console.WriteLine(Info.Symbols());
#if APP_ONLY
System.Console.WriteLine("app: APP_ONLY");
#endif
#if LIB_ONLY
System.Console.WriteLine("app: LIB_ONLY (wrong)");
#endif

static void Assign(out Opaque value)
{
    // Legal for a struct imported from another assembly whose fields are all private (CS8882).
}
