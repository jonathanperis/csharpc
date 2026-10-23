// Regression: structs made of small fields (bytes, shorts) passed by value once the argument
// registers run out. On macOS arm64 at -O0, LLVM's GlobalISel and its SelectionDAG fallback laid
// such arguments out differently on the stack, so the callee read garbage. (Main falls back to
// SelectionDAG because of the saturating double -> long conversion.)
using System;
using System.Collections.Generic;

struct Packet
{
    public int Id;
    public short Kind, Flags;
    public byte B0, B1, B2, B3, B4, B5, B6, B7;

    public Packet(int id, byte seed)
    {
        Id = id;
        Kind = (short)(id * 3);
        Flags = (short)-id;
        B0 = seed; B1 = (byte)(seed + 1); B2 = (byte)(seed + 2); B3 = (byte)(seed + 3);
        B4 = (byte)(seed + 4); B5 = (byte)(seed + 5); B6 = (byte)(seed + 6); B7 = (byte)(seed + 7);
    }

    public override string ToString() => $"{Id}/{Kind}/{Flags}:{B0},{B1},{B2},{B3},{B4},{B5},{B6},{B7}";
}

class Program
{
    static int Sum(Packet p) => p.Id + p.Kind + p.Flags + p.B0 + p.B1 + p.B2 + p.B3 + p.B4 + p.B5 + p.B6 + p.B7;

    static string Both(Packet a, Packet b, byte extra) => $"{a} | {b} | {extra}";

    static bool Same(Packet a, Packet b) => a.Id == b.Id && a.B7 == b.B7 && a.B0 == b.B0;

    static void Main()
    {
        var p = new Packet(7, 10);
        var q = new Packet(-3, 250);
        double scale = p.Id * 1.5;
        Console.WriteLine((long)scale);
        try
        {
            Console.WriteLine(Sum(p) + " " + Sum(q));
            Console.WriteLine(Both(p, q, 99));
            Console.WriteLine(Same(p, p) + " " + Same(p, q));
            var byPacket = new Dictionary<Packet, string> { [p] = "p", [q] = "q" };
            Console.WriteLine(byPacket[new Packet(7, 10)] + byPacket[q]);
            object boxed = q;
            Console.WriteLine(boxed.Equals(q) + " " + boxed);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        Func<Packet, Packet, string> f = Both2;
        Console.WriteLine(f(q, p));
    }

    static string Both2(Packet a, Packet b) => Both(a, b, 1);
}
