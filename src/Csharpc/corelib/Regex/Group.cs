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
    [Shadows("System.Text.RegularExpressions.Group")]
    public class Group : Capture
    {
        private static Group? s_emptyGroupInstance;
        internal static Group s_emptyGroup => s_emptyGroupInstance ??= new Group(string.Empty, Array.Empty<int>(), 0, string.Empty);

        internal readonly int[] _caps;
        internal int _capcount;
        internal CaptureCollection? _capcoll;

        internal Group(string? text, int[] caps, int capcount, string name)
            : base(text, capcount == 0 ? 0 : caps[(capcount - 1) * 2], capcount == 0 ? 0 : caps[(capcount * 2) - 1])
        {
            _caps = caps;
            _capcount = capcount;
            Name = name;
        }

        public bool Success => _capcount != 0;

        public string Name { get; }

        public CaptureCollection Captures => _capcoll ??= new CaptureCollection(this);

        public static Group Synchronized(Group inner)
        {
            if (inner == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.inner);
            }

            // force Captures to be computed.
            CaptureCollection capcoll = inner.Captures;
            if (inner.Success)
            {
                capcoll.ForceInitialized();
            }

            return inner;
        }
    }
}
