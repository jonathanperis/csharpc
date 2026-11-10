// System.Net.IPNetwork (an address prefix, "10.0.0.0/8") and System.Net.Security.SslClientHelloInfo,
// after dotnet/runtime's IPNetwork.cs (release/11.0, MIT), over the address bytes instead of UInt128.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.Security.SslClientHelloInfo")]
    public readonly struct SslClientHelloInfoImpl
    {
        public string ServerName { get; }
        public SslProtocols SslProtocols { get; }

        public SslClientHelloInfoImpl(string serverName, SslProtocols sslProtocols)
        {
            ServerName = serverName;
            SslProtocols = sslProtocols;
        }
    }
}
