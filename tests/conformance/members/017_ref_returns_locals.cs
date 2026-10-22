int[] arr = { 1, 2, 3 };
ref int slot = ref Find(arr, 2);
slot = 20;
Console.WriteLine(string.Join(",", arr));
static ref int Find(int[] a, int v) { for (int i = 0; i < a.Length; i++) if (a[i] == v) return ref a[i]; throw new Exception(); }
