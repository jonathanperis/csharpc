// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections;
using System.Diagnostics;
using System.Globalization;

namespace CsharpcCoreLib.RegularExpressions
{
    internal sealed class RegexTree
    {
        public readonly RegexOptions Options;
        public readonly RegexNode Root;
        public readonly RegexFindOptimizations FindOptimizations;
        public readonly int CaptureCount;
        public readonly CultureInfo? Culture;
        public readonly string[]? CaptureNames;
        public readonly Hashtable? CaptureNameToNumberMapping;
        public readonly Hashtable? CaptureNumberSparseMapping;

        internal RegexTree(RegexNode root, int captureCount, string[]? captureNames, Hashtable? captureNameToNumberMapping, Hashtable? captureNumberSparseMapping, RegexOptions options, CultureInfo? culture)
        {

            Root = root;
            Culture = culture;
            CaptureNumberSparseMapping = captureNumberSparseMapping;
            CaptureCount = captureCount;
            CaptureNameToNumberMapping = captureNameToNumberMapping;
            CaptureNames = captureNames;
            Options = options;
            FindOptimizations = RegexFindOptimizations.Create(root, options);
        }
    }
}
