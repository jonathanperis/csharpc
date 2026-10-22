using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

class Program
{
    static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "csio_" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            string f = Path.Combine(dir, "a.txt");
            File.WriteAllText(f, "hello\nworld\r\nlast");
            Console.WriteLine(File.Exists(f) + " " + new FileInfo(f).Length);
            foreach (var l in File.ReadAllLines(f)) Console.WriteLine("[" + l + "]");
            Console.WriteLine(File.ReadAllText(f).Length);
            File.AppendAllText(f, "\nmore ünïcødé €");
            Console.WriteLine(File.ReadAllText(f));
            byte[] b = File.ReadAllBytes(f);
            Console.WriteLine(b.Length);
            using (var w = new StreamWriter(Path.Combine(dir, "b.txt")))
            {
                for (int i = 0; i < 1000; i++) w.WriteLine("line " + i + "," + (i * 3));
            }
            long sum = 0; int count = 0;
            using (var r = new StreamReader(Path.Combine(dir, "b.txt")))
            {
                string line;
                while ((line = r.ReadLine()) != null) { count++; sum += int.Parse(line.Split(',')[1]); }
            }
            Console.WriteLine(count + " " + sum);
            int n = 0; foreach (var l in File.ReadLines(Path.Combine(dir, "b.txt"))) n++;
            Console.WriteLine(n);

            File.WriteAllText(Path.Combine(dir, "bom.txt"), "bom", new UTF8Encoding(true));
            Console.WriteLine(File.ReadAllBytes(Path.Combine(dir, "bom.txt")).Length + " " + File.ReadAllText(Path.Combine(dir, "bom.txt")));
            File.WriteAllText(Path.Combine(dir, "u16.txt"), "wide€", Encoding.Unicode);
            Console.WriteLine(File.ReadAllBytes(Path.Combine(dir, "u16.txt")).Length + " " + File.ReadAllText(Path.Combine(dir, "u16.txt")));

            Directory.CreateDirectory(Path.Combine(dir, "sub", "deep"));
            File.WriteAllText(Path.Combine(dir, "sub", "x.cs"), "x");
            File.WriteAllText(Path.Combine(dir, "sub", "deep", "y.cs"), "y");
            var files = new List<string>();
            foreach (var p in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)) files.Add(Path.GetRelativePath(dir, p));
            files.Sort(StringComparer.Ordinal);
            Console.WriteLine(string.Join(";", files));
            var top = Directory.GetFiles(dir, "*.txt"); Array.Sort(top, StringComparer.Ordinal);
            foreach (var t in top) Console.WriteLine(Path.GetFileName(t));
            Console.WriteLine(Directory.GetDirectories(dir).Length);

            File.Copy(f, Path.Combine(dir, "c.txt"));
            try { File.Copy(f, Path.Combine(dir, "c.txt")); } catch (IOException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message.Replace(dir, "DIR")); }
            File.Move(Path.Combine(dir, "c.txt"), Path.Combine(dir, "d.txt"));
            Console.WriteLine(File.Exists(Path.Combine(dir, "c.txt")) + " " + File.Exists(Path.Combine(dir, "d.txt")));
            File.Delete(Path.Combine(dir, "d.txt"));
            File.Delete(Path.Combine(dir, "nonexistent"));

            try { File.ReadAllText(Path.Combine(dir, "missing.txt")); } catch (FileNotFoundException e) { Console.WriteLine("FNF: " + e.Message.Replace(dir, "DIR")); }
            try { File.ReadAllText(Path.Combine(dir, "nodir", "missing.txt")); } catch (DirectoryNotFoundException e) { Console.WriteLine("DNF: " + e.Message.Replace(dir, "DIR")); }
            try { File.ReadAllText(dir); } catch (UnauthorizedAccessException e) { Console.WriteLine("UA: " + e.Message.Replace(dir, "DIR")); }
            try { Directory.Delete(Path.Combine(dir, "sub")); } catch (IOException e) { Console.WriteLine("IO: " + e.Message.Replace(dir, "DIR")); }

            using (var fs = new FileStream(Path.Combine(dir, "bin.dat"), FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(42); bw.Write(3.5); bw.Write("str€"); bw.Write(true); bw.Write((long)-7); bw.Write('z');
            }
            using (var fs = File.OpenRead(Path.Combine(dir, "bin.dat")))
            using (var br = new BinaryReader(fs))
            {
                Console.WriteLine(br.ReadInt32() + " " + br.ReadDouble() + " " + br.ReadString() + " " + br.ReadBoolean() + " " + br.ReadInt64() + " " + br.ReadChar());
                Console.WriteLine(fs.Position + " " + fs.Length);
                try { br.ReadInt32(); } catch (EndOfStreamException e) { Console.WriteLine(e.Message); }
            }
            var ms = new MemoryStream();
            var sw = new StreamWriter(ms);
            sw.Write("abc\ndef");
            sw.Flush();
            Console.WriteLine(ms.Length);
            ms.Position = 0;
            var sr = new StreamReader(ms);
            Console.WriteLine(sr.ReadLine() + "|" + sr.ReadLine() + "|" + (sr.ReadLine() == null));
            var strw = new StringWriter();
            strw.Write("x={0}", 5); strw.WriteLine(); strw.WriteLine(1.5);
            Console.Write(strw.ToString());
            var strr = new StringReader("a\nb\r\nc");
            string s2; while ((s2 = strr.ReadLine()) != null) Console.WriteLine("<" + s2 + ">");
            TextWriter o = Console.Out;
            o.WriteLine("via TextWriter " + 1);
            o.Write('c'); o.WriteLine(2);
            Console.Out.WriteLine("fast");
            Console.Error.WriteLine("to stderr");
            Console.Out.Flush();
            Directory.Delete(Path.Combine(dir, "sub"), true);
            Console.WriteLine(Directory.Exists(Path.Combine(dir, "sub")));
            Environment.SetEnvironmentVariable("CSIO_TEST", "val");
            Console.WriteLine(Environment.GetEnvironmentVariable("CSIO_TEST") + " " + (Environment.GetEnvironmentVariable("CSIO_NOPE") == null));
            string cwd = Environment.CurrentDirectory;
            Environment.CurrentDirectory = dir;
            Console.WriteLine(Path.GetFullPath("q/../r.txt").Replace(dir, "DIR"));
            Environment.CurrentDirectory = cwd;
            Console.WriteLine(Encoding.UTF8.GetByteCount("€uro") + " " + Encoding.UTF8.GetString(Encoding.UTF8.GetBytes("ok€")) + " " + Encoding.ASCII.GetString(new byte[] { 72, 105 }));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
        Console.WriteLine(Directory.Exists(dir));
    }
}
