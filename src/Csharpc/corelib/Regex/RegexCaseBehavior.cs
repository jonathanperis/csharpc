// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Globalization;

namespace CsharpcCoreLib.RegularExpressions
{
    internal enum RegexCaseBehavior
    {
        NotSet,

        Invariant,

        NonTurkish,

        Turkish
    }
}
