Console.WriteLine(Pick(true, "a", "b") + Pick(false, 1, 2));
static T Pick<T>(bool c, T a, T b) => c ? a : b;
