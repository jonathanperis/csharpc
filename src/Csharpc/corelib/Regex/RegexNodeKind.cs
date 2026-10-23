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
    internal enum RegexNodeKind : byte
    {
        Unknown = 0,

        // The following are leaves (no children) and correspond to primitive operations in the regular expression.

        One = RegexOpcode.One,
        Notone = RegexOpcode.Notone,
        Set = RegexOpcode.Set,

        Multi = RegexOpcode.Multi,

        Oneloop = RegexOpcode.Oneloop,
        Notoneloop = RegexOpcode.Notoneloop,
        Setloop = RegexOpcode.Setloop,

        Onelazy = RegexOpcode.Onelazy,
        Notonelazy = RegexOpcode.Notonelazy,
        Setlazy = RegexOpcode.Setlazy,

        Oneloopatomic = RegexOpcode.Oneloopatomic,
        Notoneloopatomic = RegexOpcode.Notoneloopatomic,
        Setloopatomic = RegexOpcode.Setloopatomic,

        Backreference = RegexOpcode.Backreference,

        Bol = RegexOpcode.Bol,
        Eol = RegexOpcode.Eol,
        Boundary = RegexOpcode.Boundary,
        NonBoundary = RegexOpcode.NonBoundary,
        ECMABoundary = RegexOpcode.ECMABoundary,
        NonECMABoundary = RegexOpcode.NonECMABoundary,
        Beginning = RegexOpcode.Beginning,
        Start = RegexOpcode.Start,
        EndZ = RegexOpcode.EndZ,
        End = RegexOpcode.End,

        UpdateBumpalong = RegexOpcode.UpdateBumpalong,

        Nothing = RegexOpcode.Nothing,
        Empty = 23,

        // The following are interior nodes (have at least one child) and correspond to control structures composing other operations.

        Alternate = 24,
        Concatenate = 25,

        Loop = 26,
        Lazyloop = 27,

        Capture = 28,
        Group = 29,
        Atomic = 32,

        PositiveLookaround = 30,
        NegativeLookaround = 31,

        BackreferenceConditional = 33,
        ExpressionConditional = 34,
    }
}
