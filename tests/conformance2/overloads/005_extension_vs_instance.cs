var c = new C();
c.Do(1);
c.Do("x");
c.Other();
class C { public void Do(object o) => Console.WriteLine("instance object"); }
static class Ext {
    public static void Do(this C c, string s) => Console.WriteLine("ext string");
    public static void Do(this C c, int i) => Console.WriteLine("ext int");
    public static void Other(this C c) => Console.WriteLine("ext other");
}
