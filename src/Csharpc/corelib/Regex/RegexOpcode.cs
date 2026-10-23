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
    internal enum RegexOpcode
    {
        // Primitive operations

        Onerep = 0,

        Notonerep = 1,

        Setrep = 2,

        Oneloop = 3,

        Notoneloop = 4,

        Setloop = 5,

        Onelazy = 6,

        Notonelazy = 7,

        Setlazy = 8,

        One = 9,

        Notone = 10,

        Set = 11,

        Multi = 12,

        Backreference = 13,

        Bol = 14,
        Eol = 15,
        Boundary = 16,
        NonBoundary = 17,
        Beginning = 18,
        Start = 19,
        EndZ = 20,
        End = 21,
        Nothing = 22,
        ECMABoundary = 41,
        NonECMABoundary = 42,

        Oneloopatomic = 43,
        Notoneloopatomic = 44,
        Setloopatomic = 45,

        UpdateBumpalong = 46,

        // Primitive control structures

        Lazybranch = 23,

        Branchmark = 24,

        Lazybranchmark = 25,

        Nullcount = 26,

        Setcount = 27,

        Branchcount = 28,

        Lazybranchcount = 29,

        Nullmark = 30,

        Setmark = 31,

        Capturemark = 32,

        Getmark = 33,

        Setjump = 34,

        Backjump = 35,

        Forejump = 36,

        TestBackreference = 37,

        Goto = 38,

        Stop = 40,

        // Modifiers for alternate modes

        OperatorMask = 63,

        RightToLeft = 64,

        Backtracking = 128,

        BacktrackingSecond = 256,

        CaseInsensitive = 512,
    }
}
