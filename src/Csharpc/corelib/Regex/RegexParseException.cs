// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;


namespace CsharpcCoreLib.RegularExpressions
{
    // csharpc: the serialization support (GetObjectData) is omitted.
    [Shadows("System.Text.RegularExpressions.RegexParseException")]
    public sealed class RegexParseException : ArgumentException
    {
        public RegexParseError Error { get; }

        public int Offset { get; }

        internal RegexParseException(RegexParseError error, int offset, string message) : base(message)
        {
            Error = error;
            Offset = offset;
        }
    }
}
