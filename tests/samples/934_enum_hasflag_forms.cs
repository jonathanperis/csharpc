// Enum.HasFlag in the shapes C# compiles it to (box the receiver, push the flag, box it, call): flags from
// constants, locals, arguments and fields, receivers from locals, fields, properties and calls, enums of
// several underlying types, zero flags, and HasFlag through Enum-typed values (which must stay boxed).
using System;
using System.Data;

[Flags] enum Small : byte { None = 0, A = 1, B = 2, C = 128 }
[Flags] enum Wide : long { None = 0, Low = 1, High = 1L << 62, Both = Low | High }
[Flags] enum Signed : short { None = 0, One = 1, Neg = -32768 }

class Holder
{
    public CommandBehavior Behavior = CommandBehavior.SequentialAccess | CommandBehavior.SingleResult;
    public ConnectionState State { get; set; } = ConnectionState.Open | ConnectionState.Executing;
    public static Wide Static = Wide.High;
}

static class Program
{
    static bool Arg(CommandBehavior value, CommandBehavior flag) => value.HasFlag(flag);
    static Small Make(int i) => (Small)i;

    static void Main()
    {
        var h = new Holder();
        Console.WriteLine(h.Behavior.HasFlag(CommandBehavior.SequentialAccess));
        Console.WriteLine(h.Behavior.HasFlag(CommandBehavior.SchemaOnly));
        Console.WriteLine(h.State.HasFlag(ConnectionState.Executing));
        Console.WriteLine(h.State.HasFlag(ConnectionState.Fetching));
        Console.WriteLine(h.State.HasFlag(ConnectionState.Closed)); // zero flag: always true
        var flag = CommandBehavior.SingleResult;
        Console.WriteLine(h.Behavior.HasFlag(flag));
        Console.WriteLine(Arg(CommandBehavior.KeyInfo, CommandBehavior.KeyInfo | CommandBehavior.SingleRow));
        Console.WriteLine(Arg(CommandBehavior.KeyInfo | CommandBehavior.SingleRow, CommandBehavior.KeyInfo));
        Console.WriteLine(Make(130).HasFlag(Small.C) + " " + Make(130).HasFlag(Small.A) + " " + Make(3).HasFlag(Small.A | Small.B));
        Console.WriteLine(Holder.Static.HasFlag(Wide.High) + " " + Holder.Static.HasFlag(Wide.Both) + " " + Wide.Both.HasFlag(Wide.Low));
        Signed s = Signed.Neg;
        Console.WriteLine(s.HasFlag(Signed.Neg) + " " + s.HasFlag(Signed.One) + " " + (s | Signed.One).HasFlag(Signed.Neg | Signed.One));
        Enum boxed = h.Behavior;
        Console.WriteLine(boxed.HasFlag(CommandBehavior.SingleResult));
        try { Console.WriteLine(boxed.HasFlag(Small.A)); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { Console.WriteLine(h.Behavior.HasFlag(null!)); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.ParamName); }
        int count = 0;
        for (int i = 0; i < 1000; i++)
            if (((CommandBehavior)i).HasFlag(CommandBehavior.SingleRow) && ((ConnectionState)i).HasFlag(ConnectionState.Open)) count++;
        Console.WriteLine(count);
    }
}
