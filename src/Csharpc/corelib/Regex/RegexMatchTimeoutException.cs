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
    // csharpc: the serialization support (ISerializable) is omitted.
    [Shadows("System.Text.RegularExpressions.RegexMatchTimeoutException")]
    public class RegexMatchTimeoutException : TimeoutException
    {
        public RegexMatchTimeoutException(string regexInput, string regexPattern, TimeSpan matchTimeout)
            : base(SR.RegexMatchTimeoutException_Occurred)
        {
            Input = regexInput;
            Pattern = regexPattern;
            MatchTimeout = matchTimeout;
        }

        public RegexMatchTimeoutException() : base(SR.RegexMatchTimeoutException_Occurred) { }

        public RegexMatchTimeoutException(string message) : base(message ?? SR.RegexMatchTimeoutException_Occurred) { }

        public RegexMatchTimeoutException(string message, Exception inner) : base(message ?? SR.RegexMatchTimeoutException_Occurred, inner) { }

        public string Input { get; } = string.Empty;

        public string Pattern { get; } = string.Empty;

        public TimeSpan MatchTimeout { get; } = TimeSpan.FromTicks(-1);
    }
}
