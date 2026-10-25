// System.Net.Sockets.UnixDomainSocketEndPoint, after dotnet/runtime's UnixDomainSocketEndPoint.cs
// (release/11.0, MIT): the path, its length check and formatting. csharpc's sockets are IP only, so a
// Unix domain socket itself is still an explicit NotSupportedException (the Socket constructor).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.Sockets.UnixDomainSocketEndPoint")]
    public sealed class UnixDomainSocketEndPointImpl : EndPoint
    {
        // sizeof(sockaddr_un.sun_path)
#if CSHARPC_TARGET_MACOS
        private const int NativePathLength = 104;
#else
        private const int NativePathLength = 108;
#endif
        private readonly string _path;

        public UnixDomainSocketEndPointImpl(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            bool isAbstract = IsAbstract(path);
            int bufferLength = Encoding.UTF8.GetByteCount(path) + (isAbstract ? 0 : 1);
            if (path.Length == 0 || bufferLength > NativePathLength)
                throw new ArgumentOutOfRangeException("path", path,
                    "The path '" + path + "' is of an invalid length for use with domain sockets on this platform.  The length must be between 1 and " + NativePathLength + " characters, inclusive.");
#if CSHARPC_TARGET_WASI
            throw new PlatformNotSupportedException();
#else
            _path = path;
#endif
        }

        public override AddressFamily AddressFamily => AddressFamily.Unix;

        public override string ToString() => IsAbstract(_path) ? string.Concat("@", _path.AsSpan(1)) : _path;

        public override bool Equals(object obj) => obj is UnixDomainSocketEndPointImpl ep && _path == ep._path;

        public override int GetHashCode() => _path.GetHashCode();

        private static bool IsAbstract(string path) => path.Length > 0 && path[0] == '\0';
    }
}
