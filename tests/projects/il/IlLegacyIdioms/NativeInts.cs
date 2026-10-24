using System;

namespace IlLegacyIdioms
{
    public static unsafe class NativeInts
    {
        // C# 9 compiles these as calls to IntPtr/UIntPtr's conversion operators.
        public static IntPtr FromPointer(void* p) => (IntPtr)p;
        public static void* ToPointer(IntPtr p) => (void*)p;
        public static IntPtr FromInt(int value) => (IntPtr)value;
        public static IntPtr FromLong(long value) => (IntPtr)value;
        public static int ToInt(IntPtr value) => (int)value;
        public static long ToLong(IntPtr value) => (long)value;
        public static UIntPtr FromULong(ulong value) => (UIntPtr)value;
        public static uint ToUInt(UIntPtr value) => (uint)value;
        public static ulong ToULong(UIntPtr value) => (ulong)value;
        public static UIntPtr FromUPointer(void* p) => (UIntPtr)p;

        public static long RoundTrip(long value)
        {
            byte* buffer = stackalloc byte[16];
            IntPtr start = (IntPtr)buffer;
            void* back = (void*)start;
            return (byte*)back == buffer ? value : -1;
        }
    }
}
