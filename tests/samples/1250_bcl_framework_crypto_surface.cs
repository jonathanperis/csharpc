#:property AllowUnsafeBlocks=true
// BCL members that .NET's own System.Security.Cryptography, System.Formats.Asn1 and System.Net.Security call
// (the Windows native-TLS spike, docs/adr/0022-tls.md): BigInteger bytes, bit length, bitwise operators and
// shifts; Convert.TryFromBase64Chars; span case mapping; a MemoryManager<T> over unmanaged memory; the typed
// PinnedGCHandle<T>; IntPtr.ToPointer; IsWindowsVersionAtLeast; LazyInitializer over a Lock.
using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

BigInteger[] values =
[
    0, 1, -1, 127, 128, -128, -129, 255, 256, 65535, -65536, 4294967295, 4294967296, -4294967297,
    BigInteger.Pow(2, 70), -BigInteger.Pow(2, 70), -BigInteger.Pow(2, 70) - 1, BigInteger.Pow(3, 50),
];
foreach (BigInteger v in values)
{
    string unsigned = v.Sign >= 0 ? v.GetByteCount(true).ToString() : "-";
    Console.WriteLine($"{v}: bits={v.GetBitLength()} bytes={v.GetByteCount()} unsigned={unsigned} " +
        $"le={Convert.ToHexString(v.ToByteArray())} be={Convert.ToHexString(v.ToByteArray(false, true))} " +
        $">>3={v >> 3} <<5={v << 5} >>70={v >> 70} >>-4={v >> -4} <<-4={v << -4}");
}

BigInteger[] operands = [0, 1, -1, 12345678901234, -12345678901234, BigInteger.Pow(2, 65) + 7, -BigInteger.Pow(2, 65) + 7];
foreach (BigInteger a in operands)
    foreach (BigInteger b in operands)
        Console.WriteLine($"{a} & {b} = {a & b}; | = {a | b}; ^ = {a ^ b}");

Console.WriteLine(new BigInteger(new byte[] { 0xFF, 0x00 }));
Console.WriteLine(new BigInteger(new byte[] { 0xFF, 0xFF }));
Console.WriteLine(new BigInteger(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }));
Console.WriteLine(new BigInteger(new byte[] { 0x80 }));
Console.WriteLine(new BigInteger(new byte[] { }));
Console.WriteLine(new BigInteger(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }));
Console.WriteLine(new BigInteger(new byte[] { 0xFF, 0xFF }.AsSpan(), isUnsigned: true, isBigEndian: true));
Console.WriteLine(new BigInteger(new byte[] { 0xFF, 0xFE }.AsSpan(), isUnsigned: false, isBigEndian: true));
Console.WriteLine(new BigInteger(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 }.AsSpan(), isUnsigned: true, isBigEndian: true));

Span<byte> buffer = stackalloc byte[4];
BigInteger sample = 0x01020304;
Console.WriteLine($"{sample.TryWriteBytes(buffer, out int written)} {written} {Convert.ToHexString(buffer)}");
buffer.Clear();
Console.WriteLine($"{sample.TryWriteBytes(buffer, out written, isUnsigned: true, isBigEndian: true)} {written} {Convert.ToHexString(buffer)}");
Console.WriteLine($"{(sample << 8).TryWriteBytes(buffer, out written)} {written}");
try { _ = new BigInteger(-5).GetByteCount(true); Console.WriteLine("no exception"); }
catch (OverflowException e) { Console.WriteLine($"OverflowException: {e.Message}"); }

// Convert.TryFromBase64Chars
Span<byte> decoded = stackalloc byte[16];
foreach (string text in new[] { "SGVsbG8gd29ybGQ=", "SGVs bG8g\r\nd29ybGQ=", "SGVsbG8", "SGVsbG8gd29ybGQ", "!!!!", "SGVsbG8gd29ybGQ=====", "" })
{
    bool ok = Convert.TryFromBase64Chars(text.AsSpan(), decoded, out int count);
    Console.WriteLine($"'{text.Replace("\r", "\\r").Replace("\n", "\\n")}': {ok} {count} {Convert.ToHexString(decoded[..count])}");
}
Console.WriteLine(Convert.TryFromBase64Chars("SGVsbG8gd29ybGQ=", new byte[4], out int shortCount) + " " + shortCount);

// Span case mapping
Span<char> cased = stackalloc char[8];
Console.WriteLine($"{"ECDSA-P256".AsSpan(0, 5).ToLowerInvariant(cased)} {cased[..5].ToString()}");
Console.WriteLine($"{"nistP384".AsSpan().ToUpperInvariant(cased)} {cased[..8].ToString()}");
Console.WriteLine("too long for the buffer".AsSpan().ToLowerInvariant(cased));
Console.WriteLine("".AsSpan().ToLowerInvariant(cased));

// MemoryManager<T> over unmanaged memory
using (var manager = new NativeBuffer(16))
{
    Memory<byte> memory = ((IMemoryOwner<byte>)manager).Memory;
    Console.WriteLine($"memory length {memory.Length}");
    for (int i = 0; i < memory.Length; i++) memory.Span[i] = (byte)(i * 3);
    Console.WriteLine(Convert.ToHexString(manager.GetSpan()));
    Console.WriteLine(Convert.ToHexString(manager.Slice(4, 4).Span));
    using MemoryHandle handle = manager.Pin(2);
    unsafe { Console.WriteLine(((byte*)handle.Pointer)[0]); }
}

// PinnedGCHandle<T>
byte[] array = new byte[8];
var pinned = new PinnedGCHandle<byte[]>(array);
try
{
    Console.WriteLine(pinned.IsAllocated);
    IntPtr data;
    unsafe { data = (IntPtr)pinned.GetAddressOfObjectData(); }
    Console.WriteLine(Marshal.ReadInt32(data)); // an array's object data starts at its length; the elements follow 8 bytes later
    Marshal.WriteByte(data, 8 + 3, 42);
    Marshal.WriteInt32(data, 8 + 4, 0x01020304);
    Console.WriteLine(Convert.ToHexString(pinned.Target));
    pinned.Target = new byte[2];
    Console.WriteLine(pinned.Target.Length);
    Console.WriteLine(pinned.Equals(pinned));
}
finally { pinned.Dispose(); }
Console.WriteLine(pinned.IsAllocated);

// IntPtr.ToPointer / UIntPtr.ToPointer
unsafe
{
    nint address = 4096;
    nuint uaddress = 8192;
    Console.WriteLine((long)address.ToPointer() + " " + (ulong)uaddress.ToPointer());
}

// OperatingSystem.IsWindowsVersionAtLeast: a Windows 10 or later machine answers true on Windows only.
Console.WriteLine(OperatingSystem.IsWindowsVersionAtLeast(10) == OperatingSystem.IsWindows());
Console.WriteLine(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) == OperatingSystem.IsWindows());
Console.WriteLine(OperatingSystem.IsWindowsVersionAtLeast(99));

// LazyInitializer over a Lock
Lock? sync = null;
Lock first = LazyInitializer.EnsureInitialized(ref sync);
Lock second = LazyInitializer.EnsureInitialized(ref sync);
Console.WriteLine(ReferenceEquals(first, second));
lock (first) { Console.WriteLine(first.IsHeldByCurrentThread); }

// AsyncLocal<T> with a value-changed handler (WindowsIdentity's impersonation token): called on assignments that change the value.
var tracked = new AsyncLocal<string?>(args => Console.WriteLine($"changed {args.PreviousValue ?? "null"} -> {args.CurrentValue ?? "null"} (context {args.ThreadContextChanged})"));
tracked.Value = "a";
tracked.Value = "a";
tracked.Value = "b";
tracked.Value = null;
Console.WriteLine(tracked.Value ?? "null");

sealed unsafe class NativeBuffer : MemoryManager<byte>
{
    private byte* _pointer;
    private readonly int _length;

    public NativeBuffer(int length)
    {
        _pointer = (byte*)NativeMemory.AllocZeroed((nuint)length);
        _length = length;
    }

    public Memory<byte> Slice(int start, int length) => CreateMemory(start, length);

    public override Span<byte> GetSpan() => new Span<byte>(_pointer, _length);

    public override MemoryHandle Pin(int elementIndex = 0) => new MemoryHandle(_pointer + elementIndex);

    public override void Unpin() { }

    protected override void Dispose(bool disposing)
    {
        if (_pointer != null) NativeMemory.Free(_pointer);
        _pointer = null;
    }
}
