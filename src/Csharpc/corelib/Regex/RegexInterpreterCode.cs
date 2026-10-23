// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CsharpcCoreLib.RegularExpressions
{
    internal sealed class RegexInterpreterCode
    {
        public readonly RegexFindOptimizations FindOptimizations;
        public readonly RegexOptions Options;
        public readonly int[] Codes;
        public readonly string[] Strings;
        public readonly uint[]?[] StringsAsciiLookup;
        public readonly int TrackCount;

        public RegexInterpreterCode(RegexFindOptimizations findOptimizations, RegexOptions options, int[] codes, string[] strings, int trackcount)
        {
            FindOptimizations = findOptimizations;
            Options = options;
            Codes = codes;
            Strings = strings;
            StringsAsciiLookup = new uint[strings.Length][];
            TrackCount = trackcount;
        }

        public static bool OpcodeBacktracks(RegexOpcode opcode)
        {
            opcode &= RegexOpcode.OperatorMask;
            return opcode is
                RegexOpcode.Oneloop or
                RegexOpcode.Onelazy or
                RegexOpcode.Notoneloop or
                RegexOpcode.Notonelazy or
                RegexOpcode.Setloop or
                RegexOpcode.Setlazy or
                RegexOpcode.Lazybranch or
                RegexOpcode.Branchmark or
                RegexOpcode.Lazybranchmark or
                RegexOpcode.Nullcount or
                RegexOpcode.Setcount or
                RegexOpcode.Branchcount or
                RegexOpcode.Lazybranchcount or
                RegexOpcode.Setmark or
                RegexOpcode.Capturemark or
                RegexOpcode.Getmark or
                RegexOpcode.Setjump or
                RegexOpcode.Backjump or
                RegexOpcode.Forejump or
                RegexOpcode.Goto;
        }

    }
}
