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
    [Shadows("System.Text.RegularExpressions.Capture")]
    public class Capture
    {
        internal Capture(string? text, int index, int length)
        {
            Text = text;
            Index = index;
            Length = length;
        }

        public int Index { get; private protected set; }

        public int Length { get; private protected set; }

        internal string? Text { get; set; }

        public string Value => Text is string text ? text.Substring(Index, Length) : string.Empty;

        public ReadOnlySpan<char> ValueSpan => Text is string text ? text.AsSpan(Index, Length) : default;

        public override string ToString() => Value;

        internal ReadOnlyMemory<char> GetLeftSubstring() => Text is string text ? text.AsMemory(0, Index) : ReadOnlyMemory<char>.Empty;

        internal ReadOnlyMemory<char> GetRightSubstring() => Text is string text ? text.AsMemory(Index + Length, Text.Length - Index - Length) : ReadOnlyMemory<char>.Empty;
    }
}
