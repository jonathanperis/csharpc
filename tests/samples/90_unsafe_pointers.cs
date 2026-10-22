#:property AllowUnsafeBlocks=true
// Unsafe code: pointers, address-of, indirection, arithmetic, fixed, sizeof, function pointers.
unsafe
{
    int x = 5;
    int* p = &x;
    *p = 7;
    Console.WriteLine(x);

    int[] arr = [10, 20, 30, 40];
    fixed (int* q = arr)
    {
        Console.WriteLine(q[1] + *(q + 2));
        int* end = q + arr.Length;
        int sum = 0;
        for (int* it = q; it < end; it++) sum += *it;
        Console.WriteLine(sum);
        Console.WriteLine(end - q);
        q[3] = 99;
    }
    Console.WriteLine(arr[3]);

    int[]? empty = [];
    fixed (int* e = empty) Console.WriteLine(e == null);

    string text = "hello";
    fixed (char* c = text)
    {
        int vowels = 0;
        for (int i = 0; c[i] != '\0'; i++) if (c[i] is 'e' or 'o') vowels++;
        Console.WriteLine(vowels);
    }

    Point pt = new Point { X = 1, Y = 2 };
    Point* pp = &pt;
    pp->X = 30;
    (*pp).Y += 10;
    Console.WriteLine($"{pt.X} {pt.Y} {sizeof(Point)} {sizeof(long)}");

    byte* bytes = (byte*)&x;
    Console.WriteLine(bytes[0]);
    void* v = p;
    int* back = (int*)v;
    Console.WriteLine(*back == x);
    nint address = (nint)p;
    Console.WriteLine(address != 0);

    int a = 1, b = 2;
    Swap(&a, &b);
    Console.WriteLine($"{a} {b}");

    delegate*<int, int> square = &Square;
    Console.WriteLine(square(9));
    delegate*<int, int, int> add = &Add;
    Console.WriteLine(add(2, 3));
}

static unsafe void Swap(int* left, int* right) { int t = *left; *left = *right; *right = t; }
static int Square(int v) => v * v;
static int Add(int l, int r) => l + r;

struct Point { public int X; public int Y; }
