unsafe {
    int[] arr = { 10, 20, 30 };
    fixed (int* p = arr) {
        int* q = p + 1;
        *q += 5;
        Console.WriteLine($"{p[0]} {*q} {*(p + 2)} {q - p}");
    }
    int x = 7; int* px = &x; *px = 8;
    Console.WriteLine(x);
    Console.WriteLine(sizeof(long) + sizeof(char));
}
