// Sieve of Eratosthenes over a bool[] — memory bandwidth plus bounds-checked indexing.
int limit = 20000000;
bool[] composite = new bool[limit];
for (int i = 2; (long)i * i < limit; i++)
{
    if (composite[i]) continue;
    for (int j = i * i; j < limit; j += i) composite[j] = true;
}
int count = 0;
for (int i = 2; i < limit; i++)
    if (!composite[i]) count++;
Console.WriteLine("primes below " + limit + ": " + count);
