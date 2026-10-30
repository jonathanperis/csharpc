namespace IlDynamicLib;

public static class Late
{
    public static object Twice(object value)
    {
        dynamic d = value;
        return d + d;
    }
}
