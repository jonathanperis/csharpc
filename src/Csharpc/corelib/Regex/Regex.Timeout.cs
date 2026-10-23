// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Threading;

namespace CsharpcCoreLib.RegularExpressions
{
    public partial class Regex
    {
        private const ulong MaximumMatchTimeoutTicks = 10_000UL * (int.MaxValue - 1); // TimeSpan.FromMilliseconds(int.MaxValue - 1).Ticks;

        private const string DefaultMatchTimeout_ConfigKeyName = "REGEX_DEFAULT_MATCH_TIMEOUT";

        private const long InfiniteMatchTimeoutTicks = -10_000; // InfiniteMatchTimeout.Ticks

        // Historical note:
        // Regex.InfiniteMatchTimeout was created instead of Timeout.InfiniteTimeSpan because of:
        // 1) Concerns about a connection between Regex and multi-threading.
        // 2) Concerns around requiring an extra contract assembly reference to access Timeout.
        // Neither of these would motivate such an addition now, but the API exists.

        // csharpc: a corelib type's static fields are initialized at startup when the program references
        // them (docs/12-corelib.md); this one is read directly by user code, so it must stay a field.
        public static readonly TimeSpan InfiniteMatchTimeout = new TimeSpan(InfiniteMatchTimeoutTicks);

        // csharpc: no AppContext data source, so the REGEX_DEFAULT_MATCH_TIMEOUT override .NET reads from
        // runtimeconfig / AppDomain.SetData is never set: the default timeout is infinite.
        internal static TimeSpan s_defaultMatchTimeout => new TimeSpan(InfiniteMatchTimeoutTicks);

        protected internal TimeSpan internalMatchTimeout;

        public TimeSpan MatchTimeout => internalMatchTimeout;
    }
}
