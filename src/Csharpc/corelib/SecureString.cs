// System.Security.SecureString and the Marshal members that copy one to native memory. On Unix .NET keeps a
// SecureString's characters in unencrypted native memory too, so the behavior (Length, mutation, read-only,
// disposal, the copies Marshal hands out) is what matters here: NetworkCredential (System.Net.Primitives, from
// IL) holds its password as a string or a SecureString.
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Security;

namespace CsharpcCoreLib
{
    [Shadows("System.Security.SecureString")]
    public sealed unsafe class SecureStringImpl : IDisposable
    {
        private const int MaxLength = 65536;

        private char[] _chars;
        private int _length;
        private bool _readOnly;
        private bool _disposed;

        public SecureStringImpl() { _chars = new char[8]; }

        [CLSCompliant(false)]
        public SecureStringImpl(char* value, int length)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (length < 0) throw new ArgumentOutOfRangeException("length", length, "Non-negative number required.");
            if (length > MaxLength) throw new ArgumentOutOfRangeException("length", length, "The length cannot exceed 65536 characters.");
            _chars = new char[Math.Max(length, 8)];
            for (int i = 0; i < length; i++) _chars[i] = value[i];
            _length = length;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(null);
        }

        private void EnsureNotReadOnly()
        {
            if (_readOnly) throw new InvalidOperationException("Instance is read-only.");
        }

        public int Length
        {
            get { EnsureNotDisposed(); return _length; }
        }

        public void AppendChar(char c)
        {
            EnsureNotDisposed();
            EnsureNotReadOnly();
            if (_length == MaxLength) throw new ArgumentOutOfRangeException("capacity", "The length cannot exceed 65536 characters.");
            if (_length == _chars.Length) Array.Resize(ref _chars, _chars.Length * 2);
            _chars[_length++] = c;
        }

        public void Clear()
        {
            EnsureNotDisposed();
            EnsureNotReadOnly();
            Array.Clear(_chars, 0, _length);
            _length = 0;
        }

        public SecureString Copy()
        {
            EnsureNotDisposed();
            var copy = new SecureStringImpl();
            copy._chars = (char[])_chars.Clone();
            copy._length = _length;
            return (SecureString)(object)copy;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Array.Clear(_chars, 0, _chars.Length);
            _disposed = true;
        }

        public void InsertAt(int index, char c)
        {
            EnsureNotDisposed();
            EnsureNotReadOnly();
            if (index < 0 || index > _length) throw new ArgumentOutOfRangeException("index", index, "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (_length >= MaxLength) throw new ArgumentOutOfRangeException("index", "The length cannot exceed 65536 characters.");
            if (_length == _chars.Length) Array.Resize(ref _chars, _chars.Length * 2);
            Array.Copy(_chars, index, _chars, index + 1, _length - index);
            _chars[index] = c;
            _length++;
        }

        public bool IsReadOnly()
        {
            EnsureNotDisposed();
            return _readOnly;
        }

        public void MakeReadOnly()
        {
            EnsureNotDisposed();
            _readOnly = true;
        }

        public void RemoveAt(int index)
        {
            EnsureNotDisposed();
            EnsureNotReadOnly();
            if (index < 0 || index >= _length) throw new ArgumentOutOfRangeException("index", index, "Index was out of range. Must be non-negative and less than the size of the collection.");
            Array.Copy(_chars, index + 1, _chars, index, _length - index - 1);
            _chars[--_length] = '\0';
        }

        public void SetAt(int index, char c)
        {
            EnsureNotDisposed();
            EnsureNotReadOnly();
            if (index < 0 || index >= _length) throw new ArgumentOutOfRangeException("index", index, "Index was out of range. Must be non-negative and less than the size of the collection.");
            _chars[index] = c;
        }

        internal string ToPlainString()
        {
            EnsureNotDisposed();
            return new string(_chars, 0, _length);
        }
    }

    /// <summary>Marshal's SecureString conversions: a NUL-terminated copy in native memory the caller frees (and zeroes) with the matching ZeroFree.</summary>
    [ShadowMembers("System.Runtime.InteropServices.Marshal")]
    internal static unsafe class MarshalSecureStringMembers
    {
        private static string PlainOf(SecureString s)
        {
            if (s == null) throw new ArgumentNullException("s");
            return ((SecureStringImpl)(object)s).ToPlainString();
        }

        public static IntPtr SecureStringToGlobalAllocUnicode(SecureString s) => Marshal.StringToHGlobalUni(PlainOf(s));
        public static IntPtr SecureStringToCoTaskMemUnicode(SecureString s) => Marshal.StringToCoTaskMemUni(PlainOf(s));
        public static IntPtr SecureStringToBSTR(SecureString s) => Marshal.StringToBSTR(PlainOf(s));
        public static IntPtr SecureStringToGlobalAllocAnsi(SecureString s) => Marshal.StringToHGlobalAnsi(PlainOf(s));
        public static IntPtr SecureStringToCoTaskMemAnsi(SecureString s) => Marshal.StringToCoTaskMemAnsi(PlainOf(s));

        public static void ZeroFreeGlobalAllocUnicode(IntPtr s)
        {
            if (s == IntPtr.Zero) return;
            ZeroUnicode((char*)s);
            Marshal.FreeHGlobal(s);
        }

        public static void ZeroFreeCoTaskMemUnicode(IntPtr s)
        {
            if (s == IntPtr.Zero) return;
            ZeroUnicode((char*)s);
            Marshal.FreeCoTaskMem(s);
        }

        public static void ZeroFreeBSTR(IntPtr s)
        {
            if (s == IntPtr.Zero) return;
            int bytes = *(int*)((byte*)s - 4);
            for (int i = 0; i < bytes; i++) ((byte*)s)[i] = 0;
            Marshal.FreeBSTR(s);
        }

        public static void ZeroFreeGlobalAllocAnsi(IntPtr s)
        {
            if (s == IntPtr.Zero) return;
            ZeroAnsi((byte*)s);
            Marshal.FreeHGlobal(s);
        }

        public static void ZeroFreeCoTaskMemAnsi(IntPtr s)
        {
            if (s == IntPtr.Zero) return;
            ZeroAnsi((byte*)s);
            Marshal.FreeCoTaskMem(s);
        }

        private static void ZeroUnicode(char* p)
        {
            for (; *p != '\0'; p++) *p = '\0';
        }

        private static void ZeroAnsi(byte* p)
        {
            for (; *p != 0; p++) *p = 0;
        }
    }
}
