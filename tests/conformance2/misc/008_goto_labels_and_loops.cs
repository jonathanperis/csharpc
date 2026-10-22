int i = 0;
start:
if (i < 3) { Console.Write(i); i++; goto start; }
Console.WriteLine();
for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) { if (b == 2) goto next; Console.Write($"{a}{b} "); next:; }
Console.WriteLine();
