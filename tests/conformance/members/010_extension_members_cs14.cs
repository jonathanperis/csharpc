Console.WriteLine("abc".FirstChar);
Console.WriteLine("abc".Repeat(2));
static class E
{
    extension(string s)
    {
        public char FirstChar => s[0];
        public string Repeat(int n) { var r = ""; for (int i = 0; i < n; i++) r += s; return r; }
    }
}
