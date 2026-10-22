// mandelbrot — Computer Language Benchmarks Game, printing a checksum instead of PBM bytes.
int size = 1200;
int checksum = 0;
int inside = 0;
for (int y = 0; y < size; y++)
{
    double ci = 2.0 * y / size - 1.0;
    for (int x = 0; x < size; x += 8)
    {
        int bits = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            double cr = 2.0 * (x + bit) / size - 1.5;
            double zr = 0, zi = 0;
            int i = 0;
            while (i < 50 && zr * zr + zi * zi <= 4.0)
            {
                double tr = zr * zr - zi * zi + cr;
                zi = 2.0 * zr * zi + ci;
                zr = tr;
                i++;
            }
            if (i == 50)
            {
                bits |= 1 << (7 - bit);
                inside++;
            }
        }
        checksum = checksum * 31 + bits;   // unchecked: wraps around
    }
}
Console.WriteLine("size " + size + ": " + inside + " points inside, checksum " + checksum);
