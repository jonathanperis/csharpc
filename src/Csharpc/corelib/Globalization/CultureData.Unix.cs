// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Private.CoreLib/src/System/Globalization;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md); changes of substance
// are marked `csharpc:`.
#nullable enable annotations
#if !CSHARPC_TARGET_WINDOWS

using System.Globalization;
using System;
using System.Diagnostics;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System.Globalization;
    using System;
    using System.Diagnostics;
    internal sealed partial class CultureData
    {
        private bool InitCultureDataCore() => InitIcuCultureDataCore();

        // Unix doesn't support user overrides
        partial void InitUserOverride(bool useUserOverride);

        private static string? LCIDToLocaleName(int culture)
        {
            Debug.Assert(!GlobalizationMode.Invariant);
            return IcuLocaleData.LCIDToLocaleName(culture);
        }

        private string[]? GetTimeFormatsCore(bool shortFormat)
        {
            string format = IcuGetTimeFormatString(shortFormat);
            return new string[] { format };
        }

        private static int GetAnsiCodePage(string cultureName)
        {
            int ansiCodePage = IcuLocaleData.GetLocaleDataNumericPart(cultureName, IcuLocaleDataParts.AnsiCodePage);
            return ansiCodePage == -1 ? CultureData.Invariant.ANSICodePage : ansiCodePage;
        }

        private static int GetOemCodePage(string cultureName)
        {
            int oemCodePage = IcuLocaleData.GetLocaleDataNumericPart(cultureName, IcuLocaleDataParts.OemCodePage);
            return oemCodePage == -1 ? CultureData.Invariant.OEMCodePage : oemCodePage;
        }

        private static int GetMacCodePage(string cultureName)
        {
            int macCodePage = IcuLocaleData.GetLocaleDataNumericPart(cultureName, IcuLocaleDataParts.MacCodePage);
            return macCodePage == -1 ? CultureData.Invariant.MacCodePage : macCodePage;
        }

        private static int GetEbcdicCodePage(string cultureName)
        {
            int ebcdicCodePage = IcuLocaleData.GetLocaleDataNumericPart(cultureName, IcuLocaleDataParts.EbcdicCodePage);
            return ebcdicCodePage == -1 ? CultureData.Invariant.EBCDICCodePage : ebcdicCodePage;
        }

        internal static bool IsWin32Installed => false;

        internal static unsafe CultureData GetCurrentRegionData() => CultureInfo.CurrentCulture._cultureData;

        private static bool ShouldUseUserOverrideNlsData => false;

        // csharpc: the NLS readers of the user-override paths (CultureData.Nls.cs) exist on Windows only;
        // ShouldUseUserOverrideNlsData is false here, so these are never called.
        private string NlsGetLocaleInfo(LocaleStringData type) => throw new PlatformNotSupportedException();
        private int NlsGetLocaleInfo(LocaleNumberData type) => throw new PlatformNotSupportedException();
        private int[] NlsGetLocaleInfo(LocaleGroupingData type) => throw new PlatformNotSupportedException();
        private string? NlsGetTimeFormatString() => throw new PlatformNotSupportedException();
        private int NlsGetFirstDayOfWeek() => throw new PlatformNotSupportedException();
    }
}
#endif
