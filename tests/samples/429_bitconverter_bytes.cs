// BitConverter's byte conversions and System.Buffers.Binary.BinaryPrimitives.
using System;
using System.Buffers.Binary;

byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x88, 0xFF];
Console.WriteLine($"{BitConverter.ToInt16(data, 0)} {BitConverter.ToUInt16(data, 7)} {BitConverter.ToInt32(data, 1)} {BitConverter.ToUInt32(data, 5)}");
Console.WriteLine($"{BitConverter.ToInt64(data, 0)} {BitConverter.ToUInt64(data, 1)} {BitConverter.ToChar(data, 0)} {BitConverter.ToBoolean(data, 8)}");
Console.WriteLine($"{BitConverter.ToInt32(data.AsSpan(4))} {BitConverter.ToDouble(BitConverter.GetBytes(2.5), 0)} {BitConverter.ToSingle(BitConverter.GetBytes(-1.25f))}");
Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(0x12345678)));
Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes(-2L)));
Console.WriteLine(BitConverter.ToString(BitConverter.GetBytes((ushort)0xBEEF)) + " " + BitConverter.ToString(BitConverter.GetBytes(true)));

foreach (var (array, index) in new (byte[], int)[] { (null, 0), (data, -1), (data, 9), (data, 6) })
{
    try { Console.WriteLine(BitConverter.ToInt32(array, index)); }
    catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}
try { BitConverter.ToInt64(data.AsSpan(3)); }
catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

Span<byte> buffer = stackalloc byte[8];
BinaryPrimitives.WriteInt32BigEndian(buffer, 0x01020304);
BinaryPrimitives.WriteUInt16LittleEndian(buffer[4..], 0xA0B0);
Console.WriteLine(BitConverter.ToString(buffer.ToArray()));
Console.WriteLine($"{BinaryPrimitives.ReadInt32BigEndian(buffer):X8} {BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]):X4} {BinaryPrimitives.ReadInt64LittleEndian(buffer)}");
Console.WriteLine($"{BinaryPrimitives.ReverseEndianness(0x11223344):X8} {BinaryPrimitives.ReverseEndianness((short)0x1234):X4} {BinaryPrimitives.ReverseEndianness(0x0102030405060708UL):X16}");
Console.WriteLine($"{BinaryPrimitives.TryReadInt32LittleEndian(buffer[6..], out var partial)} {partial} {BinaryPrimitives.TryWriteInt64BigEndian(buffer[1..], 1)}");
BinaryPrimitives.WriteDoubleBigEndian(buffer, Math.PI);
Console.WriteLine($"{BitConverter.ToString(buffer.ToArray())} {BinaryPrimitives.ReadDoubleBigEndian(buffer)}");
try { BinaryPrimitives.ReadInt32LittleEndian(buffer[6..]); }
catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
