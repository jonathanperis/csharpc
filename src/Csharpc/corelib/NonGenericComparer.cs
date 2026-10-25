// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Collections.Comparer, ported from dotnet/runtime (release/11.0)
// src/libraries/System.Private.CoreLib/src/System/Collections/Comparer.cs; see THIRD-PARTY-NOTICES.md.
// Not ported: serialization.
#nullable disable
using System;
using System.Collections;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Comparer")]
    public sealed class NonGenericComparerImpl : IComparer
    {
        private readonly CompareInfo _compareInfo;

        public static readonly Comparer Default = new Comparer(CultureInfo.CurrentCulture);
        public static readonly Comparer DefaultInvariant = new Comparer(CultureInfo.InvariantCulture);

        public NonGenericComparerImpl(CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            _compareInfo = culture.CompareInfo;
        }

        public int Compare(object a, object b)
        {
            if (a == b) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            if (a is string sa && b is string sb)
                return _compareInfo.Compare(sa, sb);

            if (a is IComparable ia)
                return ia.CompareTo(b);

            if (b is IComparable ib)
                return -ib.CompareTo(a);

            throw new ArgumentException("At least one object must implement IComparable.");
        }
    }
}
