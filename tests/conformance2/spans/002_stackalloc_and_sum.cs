Span<int> buf = stackalloc int[5];
for (int i = 0; i < buf.Length; i++) buf[i] = i * i;
int sum = 0;
foreach (var v in buf) sum += v;
Console.WriteLine(sum);
Span<byte> bytes = stackalloc byte[] { 1, 2, 3 };
Console.WriteLine(bytes.Length + " " + bytes[2]);
