// `x is { } v` and property patterns on a nullable library struct (Guid?, KeyValuePair<,>?,
// ArraySegment<>?, SequencePosition?): the test is HasValue, whatever implements the struct.
using System.Buffers;

Guid? id = Guid.Empty;
Console.WriteLine(id is { } g ? "guid " + g : "none");
KeyValuePair<string, int>? pair = new KeyValuePair<string, int>("a", 2);
Console.WriteLine(pair is { } kv ? kv.Key + kv.Value : "none");
Console.WriteLine(pair is { Value: 2 } ? "two" : "other");
Console.WriteLine(pair is { Key: "b" } ? "b" : "not b");
ArraySegment<int>? segment = new ArraySegment<int>(new[] { 1, 2, 3 }, 1, 2);
Console.WriteLine(segment is { Count: 2 } s ? s[0] : -1);
SequencePosition? position = Find(new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 }), 3);
Console.WriteLine(position is { } p ? p.GetInteger() : -1);
SequencePosition? missing = Find(new ReadOnlySequence<byte>(new byte[] { 1 }), 9);
Console.WriteLine(missing is { } m ? m.GetInteger() : -1);
Console.WriteLine(Describe(Guid.Empty) + " " + Describe(null));

static SequencePosition? Find(ReadOnlySequence<byte> sequence, byte value) => sequence.PositionOf(value);

static string Describe(Guid? value) => value switch
{
    { } v when v == Guid.Empty => "empty",
    { } => "some",
    null => "null",
};
