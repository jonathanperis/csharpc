// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.ComponentModel;

namespace CsharpcCoreLib.RegularExpressions
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    // Shadowed so framework IL built with the Regex source generator (System.Private.Xml) derives from these.
    [Shadows("System.Text.RegularExpressions.RegexRunnerFactory")]
    public abstract class RegexRunnerFactory
    {
        protected RegexRunnerFactory() { }

        protected internal abstract RegexRunner CreateInstance();
    }
}
