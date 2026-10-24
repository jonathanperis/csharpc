// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Private.CoreLib/src/System/Globalization;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md); changes of substance
// are marked `csharpc:`.
#nullable enable annotations

using System.Globalization;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System.Globalization;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.CompilerServices;
    [Shadows("System.Globalization.SortVersion")]
    public sealed class SortVersion : IEquatable<SortVersion?>
    {
        private readonly int m_NlsVersion; // Do not rename (binary serialization)
        private readonly Guid m_SortId; // Do not rename (binary serialization)

        public int FullVersion => m_NlsVersion;

        public Guid SortId => m_SortId;

        public SortVersion(int fullVersion, Guid sortId)
        {
            m_SortId = sortId;
            m_NlsVersion = fullVersion;
        }

        internal SortVersion(int nlsVersion, int effectiveId, Guid customVersion)
        {
            m_NlsVersion = nlsVersion;

            if (customVersion == Guid.Empty)
            {
                byte b1 = (byte)(effectiveId >> 24);
                byte b2 = (byte)((effectiveId & 0x00FF0000) >> 16);
                byte b3 = (byte)((effectiveId & 0x0000FF00) >> 8);
                byte b4 = (byte)(effectiveId & 0xFF);
                customVersion = new Guid(0, 0, 0, 0, 0, 0, 0, b1, b2, b3, b4);
            }

            m_SortId = customVersion;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is SortVersion otherVersion && Equals(otherVersion);
        }

        public bool Equals([NotNullWhen(true)] SortVersion? other)
        {
            if (other == null)
            {
                return false;
            }

            return m_NlsVersion == other.m_NlsVersion && m_SortId == other.m_SortId;
        }

        public override int GetHashCode()
        {
            return m_NlsVersion * 7 | m_SortId.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(SortVersion? left, SortVersion? right)
        {
            // Test "right" first to allow branch elimination when inlined for null checks (== null)
            // so it can become a simple test
            if (right is null)
            {
                return left is null;
            }

            return right.Equals(left);
        }

        public static bool operator !=(SortVersion? left, SortVersion? right) =>
            !(left == right);
    }
}
