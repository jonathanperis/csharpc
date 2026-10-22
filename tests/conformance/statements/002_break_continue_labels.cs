int found = -1;
for (int i = 0; i < 10; i++)
{
    if (i % 2 == 0) continue;
    if (i > 6) { found = i; break; }
}
Console.WriteLine(found);
int n = 0;
loop:
n++;
if (n < 5) goto loop;
Console.WriteLine(n);
