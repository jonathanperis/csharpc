// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib.RegularExpressions
{
    internal static partial class RegexCaseEquivalences
    {
        public const int CharactersPerRange = 1024;

        public static bool TryFindCaseEquivalencesForCharWithIBehavior(char c, CultureInfo culture, scoped ref RegexCaseBehavior mappingBehavior, out ReadOnlySpan<char> equivalences)
        {
            if ((c | 0x20) == 'i' || (c | 0x01) == '\u0131')
            {
                // If this is the first time that this method is being called then mappingBehavior will be set to default, so we calculate
                // the behavior to use and cache the value for future lookups.
                if (mappingBehavior == RegexCaseBehavior.NotSet)
                    mappingBehavior = GetRegexBehavior(culture);

                equivalences = c switch
                {
                    // Invariant mappings
                    'i' or 'I' when mappingBehavior is RegexCaseBehavior.Invariant => "Ii".AsSpan(),

                    // Non-Turkish mappings
                    'i' or 'I' or '\u0130' when mappingBehavior is RegexCaseBehavior.NonTurkish => "Ii\u0130".AsSpan(),

                    // Turkish mappings
                    'I' or '\u0131' when mappingBehavior is RegexCaseBehavior.Turkish => "I\u0131".AsSpan(),
                    'i' or '\u0130' when mappingBehavior is RegexCaseBehavior.Turkish => "i\u0130".AsSpan(),

                    // Default
                    _ => default
                };
                return !equivalences.IsEmpty;
            }
            else
            {
                return TryFindCaseEquivalencesForChar(c, out equivalences);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RegexCaseBehavior GetRegexBehavior(CultureInfo culture)
        {
            return
                culture.Name.Length == 0 ? RegexCaseBehavior.Invariant :
                IsTurkishOrAzeri(culture.Name) ? RegexCaseBehavior.Turkish :
                RegexCaseBehavior.NonTurkish;

            static bool IsTurkishOrAzeri(string cultureName)
            {
                if (cultureName.Length >= 2)
                {
                    // Assert that the first two characters in culture name are between a-z lowercase
                    Debug.Assert(cultureName[0] is >= 'a' and <= 'z');
                    Debug.Assert(cultureName[1] is >= 'a' and <= 'z');
                    switch (cultureName[0])
                    {
                        case 't':
                            return cultureName[1] == 'r' && (cultureName.Length == 2 || cultureName[2] == '-');
                        case 'a':
                            return cultureName[1] == 'z' && (cultureName.Length == 2 || cultureName[2] == '-');
                    }
                }
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryFindCaseEquivalencesForChar(char c, out ReadOnlySpan<char> equivalences)
        {
            // Dividing by CharactersPerRange, in order to get the range index for c
            Debug.Assert((c / CharactersPerRange) < 0xFF);
            byte index = (byte)((uint)c / CharactersPerRange);
            ushort firstLevelLookupValue = EquivalenceFirstLevelLookup[index];

            // If character belongs to a range that doesn't participate in casing, then just return false
            if (firstLevelLookupValue == 0xFFFF)
            {
                equivalences = default;
                return false;
            }

            // Using mod CharactersPerRange operator to get the offset of c in the range, and add the firstLevelLookupValue
            Debug.Assert(((c % CharactersPerRange) + firstLevelLookupValue) < 0xFFFF);
            ushort index2 = (ushort)(((uint)c % CharactersPerRange) + firstLevelLookupValue);
            ushort mappingValue = EquivalenceCasingMap[index2];
            if (mappingValue == 0xFFFF)
            {
                equivalences = default;
                return false;
            }

            byte count = (byte)((mappingValue >> 13) & 0b111);
            ushort index3 = (ushort)(mappingValue & 0x1FFF);
            equivalences = EquivalenceCasingValues.Slice(index3, count);

            return true;
        }
    }
}
