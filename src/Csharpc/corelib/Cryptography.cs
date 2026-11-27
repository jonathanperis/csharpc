// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/
// System/Security/Cryptography/RSAParameters.cs; see THIRD-PARTY-NOTICES.md.
// RSA's key parameters, passed around without signing anything (strong-name keys); RSA is in Crypto/Rsa.cs.
// Linux compiles System.Security.Cryptography and System.Net.Security from the framework's IL over the OpenSSL and GSS contracts
// (Crypto/CryptoNative*.cs, Net/NetSecurityNative.cs; docs/il-first/System.Net.Security.md); the other targets keep this copy (#449).
#if CSHARPC_TARGET_WINDOWS || CSHARPC_TARGET_MACOS || CSHARPC_TARGET_WASI
#nullable disable
using System;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    [Shadows("System.Security.Cryptography.RSAParameters")]
    public struct RSAParametersImpl
    {
        public byte[] Exponent;
        public byte[] Modulus;
        public byte[] P;
        public byte[] Q;
        public byte[] DP;
        public byte[] DQ;
        public byte[] InverseQ;
        public byte[] D;
    }
}
#endif
