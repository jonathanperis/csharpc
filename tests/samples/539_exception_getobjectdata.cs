// Exception classes that still override GetObjectData (most of the framework's do, and older user code):
// nothing dispatches to it since BinaryFormatter is gone, so the override is an ordinary method, and its
// body (SerializationInfo, which csharpc doesn't implement) is never reached.
using System;
using System.Runtime.Serialization;

try { throw new ParseFailedException("bad input", 42); }
catch (ParseFailedException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message} at {e.Position}"); }

try { throw new DerivedFailure(); }
catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message} ({e is ParseFailedException})"); }

Console.WriteLine(new ParseFailedException("x", 1).ToString().Split(Environment.NewLine)[0]);

public class ParseFailedException : Exception
{
    public int Position { get; }

    public ParseFailedException(string message, int position) : base(message) { Position = position; }

    [Obsolete("Formatter-based serialization is obsolete.")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(Position), Position);
    }
}

public sealed class DerivedFailure : ParseFailedException
{
    public DerivedFailure() : base("derived", -1) { }

    [Obsolete("Formatter-based serialization is obsolete.")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context) => base.GetObjectData(info, context);
}
