// Interop: Marshal, NativeMemory, NativeLibrary and the string marshallers used by
// [LibraryImport]-generated stubs — written in C# over P/Invoke to libc and the csrt runtime.
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    /// <summary>libc and csrt entry points the interop corelib is built on.</summary>
    internal static unsafe class Libc
    {
        [DllImport("libc")] public static extern void* malloc(nuint size);
        [DllImport("libc")] public static extern void* calloc(nuint count, nuint size);
        [DllImport("libc")] public static extern void* realloc(void* p, nuint size);
        [DllImport("libc")] public static extern void free(void* p);
        [DllImport("libc")] public static extern void* memcpy(void* dest, void* src, nuint n);
        [DllImport("libc")] public static extern void* memmove(void* dest, void* src, nuint n);
        [DllImport("libc")] public static extern void* memset(void* dest, int c, nuint n);
        [DllImport("libc")] public static extern nuint strlen(byte* s);
        [DllImport("__Internal")] public static extern int csrt_get_last_pinvoke_error();
        [DllImport("__Internal")] public static extern void csrt_set_last_pinvoke_error(int error);
        [DllImport("__Internal")] public static extern int csrt_get_last_system_error();
        [DllImport("__Internal")] public static extern void csrt_set_last_system_error(int error);
        [DllImport("__Internal")] public static extern void* csrt_pinvoke_load_library(byte* name, byte throwOnError);
        [DllImport("__Internal")] public static extern void* csrt_native_library_get_export(void* handle, byte* entry, byte throwOnError);

        public static void* Alloc(nuint size)
        {
            void* p = malloc(size == 0 ? 1 : size);
            if (p == null) throw new OutOfMemoryException();
            return p;
        }
    }

    /// <summary>UTF-8 &lt;-&gt; UTF-16 in C# (replacement character for unpaired surrogates / invalid bytes, like .NET).</summary>
    internal static unsafe class Utf8
    {
        public static int GetByteCount(string s)
        {
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x80) count += 1;
                else if (c < 0x800) count += 2;
                else if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF) { count += 4; i++; }
                else count += 3;
            }
            return count;
        }

        /// <summary>Encodes s into dest (no terminator); returns the number of bytes written.</summary>
        public static int Encode(string s, byte* dest)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                uint cp = s[i];
                if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                {
                    cp = 0x10000 + ((cp - 0xD800) << 10) + ((uint)s[i + 1] - 0xDC00);
                    i++;
                }
                else if (cp >= 0xD800 && cp <= 0xDFFF) cp = 0xFFFD;
                if (cp < 0x80) dest[n++] = (byte)cp;
                else if (cp < 0x800) { dest[n++] = (byte)(0xC0 | (cp >> 6)); dest[n++] = (byte)(0x80 | (cp & 0x3F)); }
                else if (cp < 0x10000)
                {
                    dest[n++] = (byte)(0xE0 | (cp >> 12)); dest[n++] = (byte)(0x80 | ((cp >> 6) & 0x3F)); dest[n++] = (byte)(0x80 | (cp & 0x3F));
                }
                else
                {
                    dest[n++] = (byte)(0xF0 | (cp >> 18)); dest[n++] = (byte)(0x80 | ((cp >> 12) & 0x3F));
                    dest[n++] = (byte)(0x80 | ((cp >> 6) & 0x3F)); dest[n++] = (byte)(0x80 | (cp & 0x3F));
                }
            }
            return n;
        }

        /// <summary>Decodes length bytes of UTF-8; each invalid sequence becomes U+FFFD.</summary>
        public static string Decode(byte* p, int length)
        {
            if (length == 0) return "";
            char* buffer = (char*)Libc.Alloc((nuint)(length * 2));
            int n = DecodeInto(p, length, buffer);
            string s = new string(buffer, 0, n);
            Libc.free(buffer);
            return s;
        }

        /// <summary>Decodes into chars (capacity ≥ length); returns the char count.</summary>
        public static int DecodeInto(byte* p, int length, char* chars)
        {
            int n = 0, i = 0;
            while (i < length)
            {
                uint b0 = p[i];
                if (b0 < 0x80) { chars[n++] = (char)b0; i++; continue; }
                int need; uint cp, min;
                if ((b0 & 0xE0) == 0xC0) { need = 1; cp = b0 & 0x1F; min = 0x80; }
                else if ((b0 & 0xF0) == 0xE0) { need = 2; cp = b0 & 0x0F; min = 0x800; }
                else if ((b0 & 0xF8) == 0xF0) { need = 3; cp = b0 & 0x07; min = 0x10000; }
                else { chars[n++] = '�'; i++; continue; }
                int j = 1;
                for (; j <= need && i + j < length && (p[i + j] & 0xC0) == 0x80; j++) cp = (cp << 6) | (uint)(p[i + j] & 0x3F);
                if (j <= need || cp < min || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                {
                    chars[n++] = '�';
                    i += j > 1 ? j : 1;
                    continue;
                }
                i += need + 1;
                if (cp >= 0x10000)
                {
                    cp -= 0x10000;
                    chars[n++] = (char)(0xD800 + (cp >> 10));
                    chars[n++] = (char)(0xDC00 + (cp & 0x3FF));
                }
                else chars[n++] = (char)cp;
            }
            return n;
        }

        /// <summary>Malloc'd NUL-terminated UTF-8 copy (null for null).</summary>
        public static byte* ToNative(string s)
        {
            if (s == null) return null;
            byte* p = (byte*)Libc.Alloc((nuint)(GetByteCount(s) + 1));
            int n = Encode(s, p);
            p[n] = 0;
            return p;
        }

        public static string FromNative(byte* p) => p == null ? null : Decode(p, (int)Libc.strlen(p));
    }

    [Shadows("System.Runtime.InteropServices.NativeMemory")]
    internal static unsafe class NativeMemoryImpl
    {
        public static void* Alloc(nuint byteCount) => Libc.Alloc(byteCount);
        public static void* Alloc(nuint elementCount, nuint elementSize) => Libc.Alloc(elementCount * elementSize);
        public static void* AllocZeroed(nuint byteCount) => AllocZeroed(byteCount, 1);
        public static void* AllocZeroed(nuint elementCount, nuint elementSize)
        {
            void* p = Libc.calloc(elementCount == 0 ? 1 : elementCount, elementSize == 0 ? 1 : elementSize);
            if (p == null) throw new OutOfMemoryException();
            return p;
        }
        public static void* Realloc(void* ptr, nuint byteCount)
        {
            void* p = Libc.realloc(ptr, byteCount == 0 ? 1 : byteCount);
            if (p == null) throw new OutOfMemoryException();
            return p;
        }
        public static void Free(void* ptr) => Libc.free(ptr);
        public static void Clear(void* ptr, nuint byteCount) => Libc.memset(ptr, 0, byteCount);
        public static void Fill(void* ptr, nuint byteCount, byte value) => Libc.memset(ptr, value, byteCount);
        public static void Copy(void* source, void* destination, nuint byteCount) => Libc.memmove(destination, source, byteCount);
    }

    [Shadows("System.Runtime.InteropServices.Marshal")]
    internal static unsafe class MarshalImpl
    {
        public static int GetLastPInvokeError() => Libc.csrt_get_last_pinvoke_error();
        public static void SetLastPInvokeError(int error) => Libc.csrt_set_last_pinvoke_error(error);
        public static int GetLastWin32Error() => Libc.csrt_get_last_pinvoke_error();
        public static int GetLastSystemError() => Libc.csrt_get_last_system_error();
        public static void SetLastSystemError(int error) => Libc.csrt_set_last_system_error(error);
        public static string GetLastPInvokeErrorMessage() => GetPInvokeErrorMessage(GetLastPInvokeError());

        /// <summary>The last P/Invoke error as an HRESULT: negative values already are one, others become FACILITY_WIN32.</summary>
        public static int GetHRForLastWin32Error()
        {
            int error = GetLastPInvokeError();
            return (error & 0x80000000) == 0x80000000 ? error : (error & 0x0000FFFF) | unchecked((int)0x80070000);
        }

        // Success codes return; a failure HRESULT maps to an exception type through CoreCLR's table, which
        // csharpc doesn't have: an explicit NotSupportedException rather than a guessed exception type.
        public static void ThrowExceptionForHR(int errorCode)
        {
            if (errorCode < 0) ThrowForFailedHR(errorCode);
        }

        public static void ThrowExceptionForHR(int errorCode, IntPtr errorInfo)
        {
            if (errorCode < 0) ThrowForFailedHR(errorCode);
        }

        private static void ThrowForFailedHR(int errorCode) =>
            throw new NotSupportedException("Marshal.ThrowExceptionForHR(0x" + errorCode.ToString("X8") + ") is not supported by csharpc yet (no HRESULT-to-exception table).");

#if !CSHARPC_TARGET_WINDOWS
        // Built-in COM exists only on Windows: elsewhere .NET finds no class for a CLSID and refuses variants.
        // (Roslyn's DiaSymReader reaches these on its Windows-PDB path.)
        public static Type GetTypeFromCLSID(Guid clsid) => null;

        public static void GetNativeVariantForObject(object obj, IntPtr pDstNativeVariant) =>
            throw new PlatformNotSupportedException("COM Interop is not supported on this platform.");

        public static void GetNativeVariantForObject<T>(T obj, IntPtr pDstNativeVariant) =>
            throw new PlatformNotSupportedException("COM Interop is not supported on this platform.");
#else
        // Windows has built-in COM, which csharpc (like NativeAOT) doesn't implement: explicit, at run time.
        public static Type GetTypeFromCLSID(Guid clsid) => throw BuiltInComNotSupported();
        public static void GetNativeVariantForObject(object obj, IntPtr pDstNativeVariant) => throw BuiltInComNotSupported();
        public static void GetNativeVariantForObject<T>(T obj, IntPtr pDstNativeVariant) => throw BuiltInComNotSupported();
        private static NotSupportedException BuiltInComNotSupported() => new NotSupportedException("Built-in COM interop is not supported by csharpc yet.");
#endif

        /// <summary>Marshal.InitHandle: a SafeHandle created by marshalling code takes the native handle.</summary>
        public static void InitHandle(SafeHandle safeHandle, IntPtr handle) => ((SafeHandleImpl)(object)safeHandle).SetHandle(handle);
#if CSHARPC_TARGET_WINDOWS
        // Windows, as in .NET: HGlobal is LocalAlloc/LocalFree, CoTaskMem is CoTaskMemAlloc/Free (memory
        // native code frees with the matching allocator), "Ansi" is the system ANSI code page (CP_ACP,
        // no best-fit for these APIs) and "Auto" is UTF-16. On Unix they are all malloc/free and UTF-8.
        public static string GetPInvokeErrorMessage(int error) => WindowsMarshal.ErrorMessage(error);

        public static IntPtr AllocHGlobal(int cb) => AllocHGlobal((IntPtr)cb);
        public static IntPtr AllocHGlobal(IntPtr cb) => WindowsMarshal.Check(WindowsMarshal.LocalAlloc(0 /* LMEM_FIXED */, (nuint)(nint)cb));
        public static IntPtr ReAllocHGlobal(IntPtr pv, IntPtr cb) =>
            WindowsMarshal.Check(WindowsMarshal.LocalReAlloc((void*)pv, (nuint)(nint)cb, 2 /* LMEM_MOVEABLE */));
        public static void FreeHGlobal(IntPtr hglobal) { if (hglobal != IntPtr.Zero) WindowsMarshal.LocalFree((void*)hglobal); }
        public static IntPtr AllocCoTaskMem(int cb) => WindowsMarshal.Check(WindowsMarshal.CoTaskMemAlloc((nuint)cb));
        public static void FreeCoTaskMem(IntPtr ptr) => WindowsMarshal.CoTaskMemFree((void*)ptr);

        public static IntPtr StringToHGlobalAnsi(string s) => WindowsMarshal.ToAnsi(s, coTaskMem: false);
        public static IntPtr StringToCoTaskMemAnsi(string s) => WindowsMarshal.ToAnsi(s, coTaskMem: true);
        public static IntPtr StringToHGlobalAuto(string s) => StringToHGlobalUni(s);
        public static IntPtr StringToHGlobalUni(string s) => WindowsMarshal.ToUtf16(s, coTaskMem: false);
        public static IntPtr StringToCoTaskMemUni(string s) => WindowsMarshal.ToUtf16(s, coTaskMem: true);
        public static IntPtr StringToCoTaskMemUTF8(string s) => WindowsMarshal.ToUtf8(s);

        public static string PtrToStringUTF8(IntPtr ptr) => Utf8.FromNative((byte*)ptr);
        public static string PtrToStringUTF8(IntPtr ptr, int byteLen) => ptr == IntPtr.Zero ? null : Utf8.Decode((byte*)ptr, byteLen);
        public static string PtrToStringAnsi(IntPtr ptr) => ptr == IntPtr.Zero ? null : WindowsMarshal.FromAnsi((byte*)ptr, -1);
        public static string PtrToStringAnsi(IntPtr ptr, int len) => ptr == IntPtr.Zero ? null : WindowsMarshal.FromAnsi((byte*)ptr, len);
        public static string PtrToStringAuto(IntPtr ptr) => PtrToStringUni(ptr);
#else
        public static string GetPInvokeErrorMessage(int error) => Utf8.FromNative(strerror(error));
        [DllImport("libc")] private static extern byte* strerror(int error);

        public static IntPtr AllocHGlobal(int cb) => (IntPtr)Libc.Alloc((nuint)cb);
        public static IntPtr AllocHGlobal(IntPtr cb) => (IntPtr)Libc.Alloc((nuint)(nint)cb);
        public static IntPtr ReAllocHGlobal(IntPtr pv, IntPtr cb) => (IntPtr)Libc.realloc((void*)pv, (nuint)(nint)cb);
        public static void FreeHGlobal(IntPtr hglobal) => Libc.free((void*)hglobal);
        public static IntPtr AllocCoTaskMem(int cb) => (IntPtr)Libc.Alloc((nuint)cb);
        public static void FreeCoTaskMem(IntPtr ptr) => Libc.free((void*)ptr);

        public static IntPtr StringToHGlobalAnsi(string s) => (IntPtr)Utf8.ToNative(s);
        public static IntPtr StringToHGlobalAuto(string s) => (IntPtr)Utf8.ToNative(s);
        public static IntPtr StringToCoTaskMemUTF8(string s) => (IntPtr)Utf8.ToNative(s);
        public static IntPtr StringToCoTaskMemAnsi(string s) => (IntPtr)Utf8.ToNative(s);
        public static IntPtr StringToHGlobalUni(string s) => (IntPtr)Utf16.ToNative(s);
        public static IntPtr StringToCoTaskMemUni(string s) => (IntPtr)Utf16.ToNative(s);

        public static string PtrToStringUTF8(IntPtr ptr) => Utf8.FromNative((byte*)ptr);
        public static string PtrToStringUTF8(IntPtr ptr, int byteLen) => ptr == IntPtr.Zero ? null : Utf8.Decode((byte*)ptr, byteLen);
        public static string PtrToStringAnsi(IntPtr ptr) => Utf8.FromNative((byte*)ptr);
        public static string PtrToStringAnsi(IntPtr ptr, int len) => ptr == IntPtr.Zero ? null : Utf8.Decode((byte*)ptr, len);
        public static string PtrToStringAuto(IntPtr ptr) => Utf8.FromNative((byte*)ptr);
#endif
        public static string PtrToStringUni(IntPtr ptr) => Utf16.FromNative((char*)ptr);
        public static string PtrToStringUni(IntPtr ptr, int len) => ptr == IntPtr.Zero ? null : new string((char*)ptr, 0, len);

        public static byte ReadByte(IntPtr ptr) => *(byte*)ptr;
        public static byte ReadByte(IntPtr ptr, int ofs) => *((byte*)ptr + ofs);
        public static short ReadInt16(IntPtr ptr) => *(short*)ptr;
        public static short ReadInt16(IntPtr ptr, int ofs) => *(short*)((byte*)ptr + ofs);
        public static int ReadInt32(IntPtr ptr) => *(int*)ptr;
        public static int ReadInt32(IntPtr ptr, int ofs) => *(int*)((byte*)ptr + ofs);
        public static long ReadInt64(IntPtr ptr) => *(long*)ptr;
        public static long ReadInt64(IntPtr ptr, int ofs) => *(long*)((byte*)ptr + ofs);
        public static IntPtr ReadIntPtr(IntPtr ptr) => *(IntPtr*)ptr;
        public static IntPtr ReadIntPtr(IntPtr ptr, int ofs) => *(IntPtr*)((byte*)ptr + ofs);
        public static void WriteByte(IntPtr ptr, byte val) => *(byte*)ptr = val;
        public static void WriteByte(IntPtr ptr, int ofs, byte val) => *((byte*)ptr + ofs) = val;
        public static void WriteInt16(IntPtr ptr, short val) => *(short*)ptr = val;
        public static void WriteInt16(IntPtr ptr, int ofs, short val) => *(short*)((byte*)ptr + ofs) = val;
        public static void WriteInt32(IntPtr ptr, int val) => *(int*)ptr = val;
        public static void WriteInt32(IntPtr ptr, int ofs, int val) => *(int*)((byte*)ptr + ofs) = val;
        public static void WriteInt64(IntPtr ptr, long val) => *(long*)ptr = val;
        public static void WriteInt64(IntPtr ptr, int ofs, long val) => *(long*)((byte*)ptr + ofs) = val;
        public static void WriteIntPtr(IntPtr ptr, IntPtr val) => *(IntPtr*)ptr = val;
        public static void WriteIntPtr(IntPtr ptr, int ofs, IntPtr val) => *(IntPtr*)((byte*)ptr + ofs) = val;

        public static void Copy(byte[] source, int startIndex, IntPtr destination, int length)
        { fixed (byte* s = source) Libc.memmove((void*)destination, s + startIndex, (nuint)length); }
        public static void Copy(IntPtr source, byte[] destination, int startIndex, int length)
        { fixed (byte* d = destination) Libc.memmove(d + startIndex, (void*)source, (nuint)length); }
        public static void Copy(int[] source, int startIndex, IntPtr destination, int length)
        { fixed (int* s = source) Libc.memmove((void*)destination, s + startIndex, (nuint)length * 4); }
        public static void Copy(IntPtr source, int[] destination, int startIndex, int length)
        { fixed (int* d = destination) Libc.memmove(d + startIndex, (void*)source, (nuint)length * 4); }
        public static void Copy(long[] source, int startIndex, IntPtr destination, int length)
        { fixed (long* s = source) Libc.memmove((void*)destination, s + startIndex, (nuint)length * 8); }
        public static void Copy(IntPtr source, long[] destination, int startIndex, int length)
        { fixed (long* d = destination) Libc.memmove(d + startIndex, (void*)source, (nuint)length * 8); }
        public static void Copy(double[] source, int startIndex, IntPtr destination, int length)
        { fixed (double* s = source) Libc.memmove((void*)destination, s + startIndex, (nuint)length * 8); }
        public static void Copy(IntPtr source, double[] destination, int startIndex, int length)
        { fixed (double* d = destination) Libc.memmove(d + startIndex, (void*)source, (nuint)length * 8); }
        public static void Copy(char[] source, int startIndex, IntPtr destination, int length)
        { fixed (char* s = source) Libc.memmove((void*)destination, s + startIndex, (nuint)length * 2); }
        public static void Copy(IntPtr source, char[] destination, int startIndex, int length)
        { fixed (char* d = destination) Libc.memmove(d + startIndex, (void*)source, (nuint)length * 2); }

#pragma warning disable CS8500 // blittable structs only, like StructureToPtr for blittable types
        public static int SizeOf<T>() => sizeof(T);
        public static T PtrToStructure<T>(IntPtr ptr) => *(T*)ptr;
        public static void StructureToPtr<T>(T structure, IntPtr ptr, bool fDeleteOld) => *(T*)ptr = structure;
#pragma warning restore CS8500
    }

    /// <summary>UTF-16 native strings (NUL-terminated copies: csharpc strings carry no terminator).</summary>
    internal static unsafe class Utf16
    {
        public static char* ToNative(string s)
        {
            if (s == null) return null;
            char* p = (char*)Libc.Alloc((nuint)((s.Length + 1) * 2));
            for (int i = 0; i < s.Length; i++) p[i] = s[i];
            p[s.Length] = '\0';
            return p;
        }

        public static string FromNative(char* p)
        {
            if (p == null) return null;
            int n = 0;
            while (p[n] != '\0') n++;
            return new string(p, 0, n);
        }
    }

    [Shadows("System.Runtime.InteropServices.Marshalling.Utf8StringMarshaller")]
    internal static unsafe class Utf8StringMarshallerImpl
    {
        public static byte* ConvertToUnmanaged(string managed) => Utf8.ToNative(managed);
        public static string ConvertToManaged(byte* unmanaged) => Utf8.FromNative(unmanaged);
        public static void Free(byte* unmanaged) => Libc.free(unmanaged);

        [Shadows("System.Runtime.InteropServices.Marshalling.Utf8StringMarshaller+ManagedToUnmanagedIn")]
        internal unsafe ref struct Utf8ManagedToUnmanagedIn
        {
            private byte* _native;
            private bool _allocated;

            public static int BufferSize => 0x100;

            public void FromManaged(string managed, Span<byte> buffer)
            {
                _allocated = false;
                if (managed == null) { _native = null; return; }
                int needed = Utf8.GetByteCount(managed) + 1;
                if (needed <= buffer.Length)
                {
                    fixed (byte* b = buffer)
                    {
                        int n = Utf8.Encode(managed, b);
                        b[n] = 0;
                        _native = b; // stackalloc'd by the caller: stays valid for the call
                    }
                }
                else
                {
                    _native = Utf8.ToNative(managed);
                    _allocated = true;
                }
            }

            public byte* ToUnmanaged() => _native;

            public void Free()
            {
                if (_allocated) Libc.free(_native);
            }
        }
    }

    [Shadows("System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller")]
    internal static unsafe class AnsiStringMarshallerImpl
    {
        public static byte* ConvertToUnmanaged(string managed) => Utf8.ToNative(managed);
        public static string ConvertToManaged(byte* unmanaged) => Utf8.FromNative(unmanaged);
        public static void Free(byte* unmanaged) => Libc.free(unmanaged);
    }

    [Shadows("System.Runtime.InteropServices.Marshalling.Utf16StringMarshaller")]
    internal static unsafe class Utf16StringMarshallerImpl
    {
        public static ushort* ConvertToUnmanaged(string managed) => (ushort*)Utf16.ToNative(managed);
        public static string ConvertToManaged(ushort* unmanaged) => Utf16.FromNative((char*)unmanaged);
        public static void Free(ushort* unmanaged) => Libc.free(unmanaged);

        /// <summary>
        /// .NET pins the string itself (its buffer is NUL-terminated). csharpc strings aren't, so the
        /// pinned reference points into a terminated GC copy, kept alive by the reference.
        /// </summary>
        public static ref readonly char GetPinnableReference(string str)
        {
            if (str == null) return ref *(char*)null;
            char[] copy = new char[str.Length + 1];
            for (int i = 0; i < str.Length; i++) copy[i] = str[i];
            return ref copy[0];
        }
    }

    [Shadows("System.Runtime.InteropServices.NativeLibrary")]
    internal static unsafe class NativeLibraryImpl
    {
        public static IntPtr Load(string libraryPath)
        {
            byte* name = Utf8.ToNative(libraryPath);
            void* handle = Libc.csrt_pinvoke_load_library(name, 1);
            Libc.free(name);
            return (IntPtr)handle;
        }

        public static bool TryLoad(string libraryPath, out IntPtr handle)
        {
            byte* name = Utf8.ToNative(libraryPath);
            handle = (IntPtr)Libc.csrt_pinvoke_load_library(name, 0);
            Libc.free(name);
            return handle != IntPtr.Zero;
        }

        public static IntPtr GetExport(IntPtr handle, string name)
        {
            byte* entry = Utf8.ToNative(name);
            void* symbol = Libc.csrt_native_library_get_export((void*)handle, entry, 1);
            Libc.free(entry);
            return (IntPtr)symbol;
        }

        public static bool TryGetExport(IntPtr handle, string name, out IntPtr address)
        {
            byte* entry = Utf8.ToNative(name);
            address = (IntPtr)Libc.csrt_native_library_get_export((void*)handle, entry, 0);
            Libc.free(entry);
            return address != IntPtr.Zero;
        }

        public static void Free(IntPtr handle) { }
    }

#pragma warning disable CS8500 // blittable element types only (what the generator uses these for)
    [Shadows("System.Runtime.InteropServices.Marshalling.SpanMarshaller`2")]
    internal static unsafe class SpanMarshallerImpl<T, TUnmanaged> where TUnmanaged : unmanaged
    {
        [Shadows("System.Runtime.InteropServices.Marshalling.SpanMarshaller`2+ManagedToUnmanagedIn")]
        internal ref struct ManagedToUnmanagedIn
        {
            public static ref T GetPinnableReference(Span<T> managed)
            {
                if (managed.Length == 0) return ref *(T*)null;
                return ref managed[0];
            }
        }
    }

    [Shadows("System.Runtime.InteropServices.Marshalling.ReadOnlySpanMarshaller`2")]
    internal static unsafe class ReadOnlySpanMarshallerImpl<T, TUnmanaged> where TUnmanaged : unmanaged
    {
        [Shadows("System.Runtime.InteropServices.Marshalling.ReadOnlySpanMarshaller`2+ManagedToUnmanagedIn")]
        internal ref struct ManagedToUnmanagedIn
        {
            public static ref readonly T GetPinnableReference(ReadOnlySpan<T> managed)
            {
                if (managed.Length == 0) return ref *(T*)null;
                return ref managed[0];
            }
        }
    }

    [Shadows("System.Runtime.InteropServices.Marshalling.ArrayMarshaller`2")]
    internal static unsafe class ArrayMarshallerImpl<T, TUnmanaged> where TUnmanaged : unmanaged
    {
        public static TUnmanaged* AllocateContainerForUnmanagedElements(T[] managed, out int numElements)
        {
            if (managed == null) { numElements = 0; return null; }
            numElements = managed.Length;
            return (TUnmanaged*)Libc.Alloc((nuint)(numElements * sizeof(TUnmanaged)));
        }

        public static ReadOnlySpan<T> GetManagedValuesSource(T[] managed) => managed;
        public static Span<TUnmanaged> GetUnmanagedValuesDestination(TUnmanaged* unmanaged, int numElements) => new Span<TUnmanaged>(unmanaged, numElements);
        public static T[] AllocateContainerForManagedElements(TUnmanaged* unmanaged, int numElements) => unmanaged == null ? null : new T[numElements];
        public static Span<T> GetManagedValuesDestination(T[] managed) => managed;
        public static ReadOnlySpan<TUnmanaged> GetUnmanagedValuesSource(TUnmanaged* unmanagedValue, int numElements) => new ReadOnlySpan<TUnmanaged>(unmanagedValue, numElements);
        public static void Free(TUnmanaged* unmanaged) => Libc.free(unmanaged);

        [Shadows("System.Runtime.InteropServices.Marshalling.ArrayMarshaller`2+ManagedToUnmanagedIn")]
        internal ref struct ManagedToUnmanagedIn
        {
            public static ref T GetPinnableReference(T[] array)
            {
                if (array == null || array.Length == 0) return ref *(T*)null;
                return ref array[0];
            }
        }
    }
#pragma warning restore CS8500

#if CSHARPC_TARGET_WINDOWS
    /// <summary>Win32 allocators, code-page conversion and error text behind Marshal on Windows.</summary>
    internal static unsafe class WindowsMarshal
    {
        [DllImport("kernel32")] public static extern void* LocalAlloc(uint flags, nuint bytes);
        [DllImport("kernel32")] public static extern void* LocalReAlloc(void* mem, nuint bytes, uint flags);
        [DllImport("kernel32")] public static extern void* LocalFree(void* mem);
        [DllImport("ole32")] public static extern void* CoTaskMemAlloc(nuint bytes);
        [DllImport("ole32")] public static extern void CoTaskMemFree(void* mem);
        [DllImport("kernel32")] private static extern int WideCharToMultiByte(uint codePage, uint flags, char* wide, int wideCount,
            byte* multi, int multiCount, byte* defaultChar, int* usedDefault);
        [DllImport("kernel32")] private static extern int MultiByteToWideChar(uint codePage, uint flags, byte* multi, int multiCount,
            char* wide, int wideCount);
        [DllImport("kernel32")] private static extern int FormatMessageW(uint flags, void* source, uint messageId, uint languageId,
            char* buffer, uint size, void* arguments);

        private const uint CP_ACP = 0, WC_NO_BEST_FIT_CHARS = 0x400;

        public static IntPtr Check(void* p) => p != null ? (IntPtr)p : throw new OutOfMemoryException();

        private static byte* Alloc(nuint bytes, bool coTaskMem) => (byte*)Check(coTaskMem ? CoTaskMemAlloc(bytes) : LocalAlloc(0, bytes));

        /// <summary>Marshal.StringToHGlobalAnsi/StringToCoTaskMemAnsi: CP_ACP without best-fit mapping, as .NET.</summary>
        public static IntPtr ToAnsi(string s, bool coTaskMem)
        {
            if (s == null) return IntPtr.Zero;
            fixed (char* chars = s)
            {
                int n = s.Length == 0 ? 0 : WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, chars, s.Length, null, 0, null, null);
                byte* p = Alloc((nuint)n + 1, coTaskMem);
                if (n != 0) WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, chars, s.Length, p, n, null, null);
                p[n] = 0;
                return (IntPtr)p;
            }
        }

        public static IntPtr ToUtf16(string s, bool coTaskMem)
        {
            if (s == null) return IntPtr.Zero;
            char* p = (char*)Alloc(((nuint)s.Length + 1) * 2, coTaskMem);
            for (int i = 0; i < s.Length; i++) p[i] = s[i];
            p[s.Length] = '\0';
            return (IntPtr)p;
        }

        public static IntPtr ToUtf8(string s)
        {
            if (s == null) return IntPtr.Zero;
            byte* temp = Utf8.ToNative(s);
            nuint n = 0;
            while (temp[n] != 0) n++;
            byte* p = Alloc(n + 1, coTaskMem: true);
            for (nuint i = 0; i <= n; i++) p[i] = temp[i];
            Libc.free(temp);
            return (IntPtr)p;
        }

        /// <summary>PtrToStringAnsi: CP_ACP; <paramref name="length"/> &lt; 0 means NUL-terminated.</summary>
        public static string FromAnsi(byte* p, int length)
        {
            if (length < 0) { length = 0; while (p[length] != 0) length++; }
            if (length == 0) return "";
            int n = MultiByteToWideChar(CP_ACP, 0, p, length, null, 0);
            char[] chars = new char[n];
            fixed (char* c = chars) MultiByteToWideChar(CP_ACP, 0, p, length, c, n);
            return new string(chars);
        }

        /// <summary>.NET's Interop.Kernel32.GetMessage: FormatMessage text with trailing whitespace and
        /// control characters trimmed, or "Unknown error (0x...)".</summary>
        public static string ErrorMessage(int error)
        {
            const uint FROM_SYSTEM = 0x1000, IGNORE_INSERTS = 0x200, ARGUMENT_ARRAY = 0x2000;
            char* buffer = stackalloc char[256];
            int length = FormatMessageW(FROM_SYSTEM | IGNORE_INSERTS | ARGUMENT_ARRAY, null, (uint)error, 0, buffer, 256, null);
            if (length <= 0) return "Unknown error (0x" + ((uint)error).ToString("x") + ")";
            while (length > 0 && buffer[length - 1] <= ' ') length--;
            return new string(buffer, 0, length);
        }
    }
#endif
}
