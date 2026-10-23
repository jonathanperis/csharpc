// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Private.CoreLib/src/System/Globalization;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md); changes of substance
// are marked `csharpc:`.
#nullable enable annotations

using System.Globalization;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System.Globalization;
    using System;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    public partial class TextInfo
    {
        private NullableBool _needsTurkishCasing;

        private static bool NeedsTurkishCasing(string localeName)
        {
            Debug.Assert(localeName != null);

            return CultureInfo.GetCultureInfo(localeName).CompareInfo.Compare("\u0131", "I", CompareOptions.IgnoreCase) == 0;
        }

        internal unsafe void IcuChangeCase(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper)
        {
            Debug.Assert(!GlobalizationMode.Invariant);
            Debug.Assert(!GlobalizationMode.UseNls);

            if (HasEmptyCultureName)
            {
                Interop.Globalization.ChangeCaseInvariant(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper);
            }
            else
            {
                if (_needsTurkishCasing == NullableBool.Undefined)
                {
                    _needsTurkishCasing = NeedsTurkishCasing(_textInfoName) ? NullableBool.True : NullableBool.False;
                }
                if (_needsTurkishCasing == NullableBool.True)
                {
                    Interop.Globalization.ChangeCaseTurkish(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper);
                }
                else
                {
                    Interop.Globalization.ChangeCase(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper);
                }
            }
        }

    }
}
