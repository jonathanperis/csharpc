// CSV sales report + mini grep: generates a dataset on disk, parses it back (quoted fields,
// CRLF/LF), aggregates, writes a report file, then greps the working directory tree.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CsvReport;

record Sale(int Id, string Region, string Product, string Customer, int Quantity, long PriceCents);

static class Csv
{
    public static string Quote(string field) =>
        field.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;

    /// <summary>Splits one CSV record; handles quoted fields with embedded commas and doubled quotes.</summary>
    public static List<string> Parse(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}

static class Program
{
    static readonly string[] Regions = { "North", "South", "East", "West", "Central" };
    static readonly string[] Products = { "Widget", "Gadget, Deluxe", "Gizmo \"Pro\"", "Doohickey", "Thingamajig" };
    static readonly string[] Customers = { "Acme", "Globex", "Initech", "Umbrella", "Hooli", "Stark", "Wayne", "Wonka" };

    static string Money(long cents) => (cents < 0 ? "-" : "") + (Math.Abs(cents) / 100).ToString() + "." + (Math.Abs(cents) % 100).ToString("D2");

    static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "csvreport_" + Environment.ProcessId);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "data", "2026"));
        try
        {
            return Run(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static int Run(string root)
    {
        // ---- generate ---------------------------------------------------------------------
        string csvPath = Path.Combine(root, "data", "2026", "sales.csv");
        uint seed = 12345;
        uint Next() { seed = seed * 1103515245 + 12345; return (seed >> 16) & 0x7FFF; }
        using (var writer = new StreamWriter(csvPath))
        {
            writer.WriteLine("id,region,product,customer,quantity,price");
            for (int i = 1; i <= 5000; i++)
            {
                var row = new[]
                {
                    i.ToString(), Regions[Next() % Regions.Length], Products[Next() % Products.Length],
                    Customers[Next() % Customers.Length], (1 + Next() % 20).ToString(), (199 + Next() % 50000).ToString(),
                };
                writer.Write(string.Join(",", row.Select(Csv.Quote)));
                writer.Write(i % 3 == 0 ? "\r\n" : "\n");
            }
        }
        Console.WriteLine($"wrote {Path.GetFileName(csvPath)}: {new FileInfo(csvPath).Length} bytes");

        // ---- parse ------------------------------------------------------------------------
        var sales = new List<Sale>();
        int lineNo = 0;
        foreach (var line in File.ReadLines(csvPath))
        {
            if (lineNo++ == 0) continue;
            var f = Csv.Parse(line);
            if (f.Count != 6) { Console.WriteLine($"bad record at line {lineNo}"); return 1; }
            sales.Add(new Sale(int.Parse(f[0]), f[1], f[2], f[3], int.Parse(f[4]), long.Parse(f[5])));
        }
        Console.WriteLine($"parsed {sales.Count} sales");

        // ---- aggregate --------------------------------------------------------------------
        var report = new StringWriter();
        report.WriteLine("== Revenue by region ==");
        foreach (var g in sales.GroupBy(s => s.Region).OrderByDescending(g => g.Sum(s => s.Quantity * s.PriceCents)).ThenBy(g => g.Key, StringComparer.Ordinal))
            report.WriteLine($"{g.Key,-8} {g.Count(),5} orders {Money(g.Sum(s => s.Quantity * s.PriceCents)),14}");
        report.WriteLine("== Top products ==");
        foreach (var g in sales.GroupBy(s => s.Product).OrderByDescending(g => g.Sum(s => s.Quantity)).ThenBy(g => g.Key, StringComparer.Ordinal))
            report.WriteLine($"{g.Key,-16} units={g.Sum(s => s.Quantity),6} avg={Money(g.Sum(s => s.PriceCents) / g.Count())}");
        report.WriteLine("== Best customer per region ==");
        foreach (var region in Regions)
        {
            var best = sales.Where(s => s.Region == region).GroupBy(s => s.Customer)
                .Select(g => (Name: g.Key, Total: g.Sum(s => s.Quantity * s.PriceCents)))
                .OrderByDescending(t => t.Total).ThenBy(t => t.Name, StringComparer.Ordinal).First();
            report.WriteLine($"{region}: {best.Name} ({Money(best.Total)})");
        }
        var biggest = sales.OrderByDescending(s => s.Quantity * s.PriceCents).ThenBy(s => s.Id).Take(3);
        report.WriteLine("== Largest orders ==");
        foreach (var s in biggest) report.WriteLine($"#{s.Id} {s.Customer} {s.Product} x{s.Quantity} = {Money(s.Quantity * s.PriceCents)}");

        string reportPath = Path.Combine(root, "report.txt");
        File.WriteAllText(reportPath, report.ToString());
        Console.Write(File.ReadAllText(reportPath));

        // ---- per-region files + mini grep ---------------------------------------------------
        foreach (var g in sales.GroupBy(s => s.Region))
            File.WriteAllLines(Path.Combine(root, "data", "2026", g.Key.ToLowerInvariant() + ".txt"),
                g.Select(s => $"{s.Id}\t{s.Customer}\t{s.Product}"));
        File.WriteAllText(Path.Combine(root, "data", "notes.md"), "Remember: Hooli owes us.\nNothing else.\n");

        string pattern = "Hooli";
        var matches = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file) == ".csv") continue;
            int n = 0, hits = 0;
            using var reader = new StreamReader(file);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                n++;
                if (line.Contains(pattern)) { hits++; if (hits == 1) matches.Add($"{Path.GetRelativePath(root, file)}:{n}: {line}"); }
            }
            if (hits > 0) matches.Add($"{Path.GetRelativePath(root, file)}: {hits} match(es)");
        }
        matches.Sort(StringComparer.Ordinal);
        Console.WriteLine($"== grep {pattern} ==");
        foreach (var m in matches) Console.WriteLine(m);

        try { File.ReadAllText(Path.Combine(root, "missing.csv")); }
        catch (FileNotFoundException e) { Console.WriteLine("error: " + e.Message.Replace(root, "<root>")); }
        return 0;
    }
}
