// Path's ReadOnlySpan<char> overloads over the string ones (Path.cs / PathWindows.cs): the same characters,
// with an empty span where the string overload answers null, as .NET's span overloads do.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static partial class PathImpl
    {
        public static ReadOnlySpan<char> GetDirectoryName(ReadOnlySpan<char> path) => AsSpan(GetDirectoryName(path.ToString()));
        public static ReadOnlySpan<char> GetFileNameWithoutExtension(ReadOnlySpan<char> path) => AsSpan(GetFileNameWithoutExtension(path.ToString()));
        public static ReadOnlySpan<char> GetExtension(ReadOnlySpan<char> path) => AsSpan(GetExtension(path.ToString()));
        public static bool HasExtension(ReadOnlySpan<char> path) => HasExtension(path.ToString());
        public static bool IsPathRooted(ReadOnlySpan<char> path) => IsPathRooted(path.ToString());
        public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2) => Join(path1.ToString(), path2.ToString());
        public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3) => Join(path1.ToString(), path2.ToString(), path3.ToString());

        private static ReadOnlySpan<char> AsSpan(string value) => value == null ? ReadOnlySpan<char>.Empty : value.AsSpan();
    }
}
