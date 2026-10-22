// Mandelbrot set (benchmarks-game style), rows computed in parallel with Parallel.For.
// Prints a checksum of the bitmap instead of PBM bytes.
using System.Threading.Tasks;

const int Size = 4000;
const int BytesPerRow = (Size + 7) / 8;
var bitmap = new byte[Size * BytesPerRow];

Parallel.For(0, Size, y =>
{
    double ci = 2.0 * y / Size - 1.0;
    int offset = y * BytesPerRow;
    for (int xb = 0; xb < BytesPerRow; xb++)
    {
        int bits = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            int x = xb * 8 + bit;
            double cr = 2.0 * x / Size - 1.5;
            double zr = 0, zi = 0, tr = 0, ti = 0;
            int i = 0;
            while (i < 50 && tr + ti <= 4.0)
            {
                zi = 2.0 * zr * zi + ci;
                zr = tr - ti + cr;
                tr = zr * zr;
                ti = zi * zi;
                i++;
            }
            bits = (bits << 1) | (tr + ti <= 4.0 ? 1 : 0);
        }
        bitmap[offset + xb] = (byte)bits;
    }
});

long checksum = 0;
for (int i = 0; i < bitmap.Length; i++) checksum = (checksum * 31 + bitmap[i]) % 1_000_000_007;
Console.WriteLine($"P4 {Size} {Size} checksum {checksum}");
