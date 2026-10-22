unsafe
{
    int x = 10;
    int* p = &x;
    *p = 20;
    Console.WriteLine(x);
    int* arr = stackalloc int[3];
    arr[0] = 1; arr[1] = 2; arr[2] = 3;
    Console.WriteLine(arr[0] + arr[1] + arr[2]);
    Console.WriteLine(sizeof(long));
}
