// File I/O: write ~50 MB of CSV through StreamWriter, read it back line by line with StreamReader,
// parse and aggregate; then a binary pass (FileStream + BinaryWriter/BinaryReader).
using System;
using System.IO;

string dir = Path.Combine(Path.GetTempPath(), "csharpc_fileio_" + Environment.ProcessId);
Directory.CreateDirectory(dir);
try
{
    string csv = Path.Combine(dir, "data.csv");
    const int Rows = 1_500_000;
    using (var w = new StreamWriter(csv))
    {
        uint seed = 42;
        for (int i = 0; i < Rows; i++)
        {
            seed = seed * 1664525 + 1013904223;
            w.Write(i);
            w.Write(',');
            w.Write("sensor-");
            w.Write((int)(seed % 97));
            w.Write(',');
            w.Write((int)(seed >> 8) % 100000);
            w.Write(",ok,lorem ipsum dolor\n");
        }
    }
    long bytes = new FileInfo(csv).Length;

    long sum = 0, lines = 0, maxValue = 0;
    var perSensor = new long[97];
    using (var r = new StreamReader(csv))
    {
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            lines++;
            int c1 = line.IndexOf(',');
            int c2 = line.IndexOf(',', c1 + 1);
            int c3 = line.IndexOf(',', c2 + 1);
            int sensor = (int)Digits(line, c1 + 8, c2);
            long value = Digits(line, c2 + 1, c3);
            perSensor[sensor] += value;
            sum += value;
            if (value > maxValue) maxValue = value;
        }
    }
    long best = 0;
    for (int s = 1; s < perSensor.Length; s++) if (perSensor[s] > perSensor[best]) best = s;

    string bin = Path.Combine(dir, "data.bin");
    using (var bw = new BinaryWriter(File.Create(bin)))
        for (int i = 0; i < 2_000_000; i++) { bw.Write(i); bw.Write(i * 0.5); }
    double total = 0;
    using (var br = new BinaryReader(File.OpenRead(bin)))
        for (int i = 0; i < 2_000_000; i++) { total += br.ReadInt32(); total += br.ReadDouble(); }

    Console.WriteLine($"csv bytes={bytes} lines={lines} sum={sum} max={maxValue} best=sensor-{best}");
    Console.WriteLine($"bin bytes={new FileInfo(bin).Length} total={total}");
}
finally
{
    Directory.Delete(dir, true);
}

static long Digits(string s, int start, int end)
{
    long v = 0;
    for (int i = start; i < end; i++) v = v * 10 + (s[i] - '0');
    return v;
}
