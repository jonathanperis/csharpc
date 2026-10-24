// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173),
// src/libraries/System.Private.CoreLib/src/System/Globalization/GlobalizationMode{,.Unix,.LoadICU.Unix}.cs and
// AppContextConfigHelper.cs; see THIRD-PARTY-NOTICES.md.
//
// csharpc: there is no runtimeconfig.json. The project's InvariantGlobalization / PredefinedCulturesOnly
// properties are compiled into the program (the corelib is parsed with CSHARPC_INVARIANT_GLOBALIZATION /
// CSHARPC_PREDEFINED_CULTURES_ONLY[_FALSE], see Driver/ProjectLoader.cs), and the environment variables
// still take precedence, as in .NET. ICU is loaded lazily, by the first call into it (.NET loads it in
// GlobalizationMode.Settings' static constructor during startup): a program that never needs ICU data
// (culture-sensitive operations with the invariant culture included) doesn't load ICU at all.
#nullable enable annotations

using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Runtime.InteropServices;
    [OptimizeForSize] // csharpc: builds culture data, once per culture (corelib/Core.cs)
    internal static partial class GlobalizationMode
    {
        // 0: ICU not loaded yet, 1: invariant mode, 2: ICU loaded.
        private static volatile int s_mode;
        // 0: not read yet, 1: false, 2: true.
        private static volatile int s_predefinedCulturesOnly;

#if CSHARPC_INVARIANT_GLOBALIZATION || CSHARPC_TARGET_WASI
        // WASI: there is no ICU to load (no dynamic loading), so invariant mode is the default, as for .NET's
        // WebAssembly builds.
        private const bool ConfiguredInvariant = true;
#else
        private const bool ConfiguredInvariant = false;
#endif

        /// <summary>
        /// Whether invariant mode is configured. csharpc: this doesn't load ICU (upstream loads it in
        /// GlobalizationMode.Settings' static constructor, during startup): ICU is loaded by the first call
        /// into it (<see cref="EnsureIcuLoaded"/>, from the Interop.Globalization wrappers), so code that
        /// never needs ICU data never loads it, e.g. formatting with the invariant culture, which is the
        /// current culture in the C locale (CultureInfo.Unix.cs). The paths taken are upstream's.
        /// </summary>
        internal static bool Invariant
        {
            get
            {
                int configured = s_configuredInvariant;
                if (configured == 0)
                {
                    configured = GetBooleanConfig("System.Globalization.Invariant", "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", ConfiguredInvariant) ? 1 : 2;
                    s_configuredInvariant = configured;
                }
                return configured == 1;
            }
        }

        // 0: not read yet, 1: invariant mode configured, 2: not.
        private static volatile int s_configuredInvariant;

        internal static bool InvariantNoLoad => Invariant;

        /// <summary>Loads ICU before the first call into it (fails like .NET's startup when it is missing).</summary>
        internal static void EnsureIcuLoaded()
        {
            if (s_mode == 0) Initialize();
        }

        internal static bool PredefinedCulturesOnly
        {
            get
            {
                int value = s_predefinedCulturesOnly;
                if (value == 0)
                {
#if CSHARPC_PREDEFINED_CULTURES_ONLY
                    bool configured = true;
#elif CSHARPC_PREDEFINED_CULTURES_ONLY_FALSE
                    bool configured = false;
#else
                    bool configured = Invariant;
#endif
                    value = GetBooleanConfig("System.Globalization.PredefinedCulturesOnly", "DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY", configured) ? 2 : 1;
                    s_predefinedCulturesOnly = value;
                }
                return value == 2;
            }
        }

        internal static bool UseNls => false;

        /// <summary>GlobalizationMode.Settings: loads ICU unless in invariant mode.</summary>
        private static int Initialize()
        {
            bool invariant = Invariant;
            if (!invariant)
            {
                if (TryGetAppLocalIcuSwitchValue(out string? icuSuffixAndVersion))
                {
                    LoadAppLocalIcu(icuSuffixAndVersion!);
                }
                else
                {
#if CSHARPC_TARGET_WINDOWS
                    // .NET falls back to NLS when icu.dll is missing (before Windows 10 1903) or when
                    // DOTNET_SYSTEM_GLOBALIZATION_USENLS is set; csharpc has only the ICU implementation.
                    if (GetBooleanConfig("System.Globalization.UseNls", "DOTNET_SYSTEM_GLOBALIZATION_USENLS", false) || Interop.Globalization.LoadICU() == 0)
                    {
                        FailFast("NLS globalization (Windows without icu.dll, or DOTNET_SYSTEM_GLOBALIZATION_USENLS) is not supported by csharpc yet.");
                    }
#else
                    int loaded = Interop.Globalization.LoadICU();
                    if (loaded == 0)
                    {
                        FailFast(GetIcuLoadFailureMessage());
                    }
#endif
                }
            }
            int mode = invariant ? 1 : 2;
            s_mode = mode;
            return mode;
        }

        /// <summary>
        /// AppContextConfigHelper.GetBooleanConfig: the environment variable ("1"/"true", "0"/"false", any case)
        /// first, then the runtimeconfig switch (AppContext, from the project's RuntimeHostConfigurationOption
        /// items or AppContext.SetSwitch), then the default (the project setting compiled into the corelib).
        /// </summary>
        private static bool GetBooleanConfig(string switchName, string envVariable, bool defaultValue)
        {
            string? str = global::System.Environment.GetEnvironmentVariable(envVariable);
            if (str != null)
            {
                if (str == "1" || IsAsciiIgnoreCase(str, "true"))
                {
                    return true;
                }
                if (str == "0" || IsAsciiIgnoreCase(str, "false"))
                {
                    return false;
                }
            }
            return CsharpcCoreLib.AppContextImpl.TryGetConfigSwitch(switchName, out bool value) ? value : defaultValue;
        }

        // bool.IsTrueStringIgnoreCase / IsFalseStringIgnoreCase (upstream's test): ASCII letters only, which is
        // also what OrdinalIgnoreCase gives against an ASCII word, without its Unicode case tables.
        private static bool IsAsciiIgnoreCase(string str, string lowerWord)
        {
            if (str.Length != lowerWord.Length) return false;
            for (int i = 0; i < str.Length; i++)
            {
                if ((str[i] | 0x20) != lowerWord[i]) return false;
            }
            return true;
        }

        private static string GetIcuLoadFailureMessage()
        {
            // These strings can't go into resources, because a resource lookup requires globalization, which requires ICU
            return "Couldn't find a valid ICU package installed on the system. " +
                "Please install libicu (or icu-libs) using your package manager and try again. " +
                "Alternatively you can set the configuration flag System.Globalization.Invariant to true if you want to run with no globalization support. " +
                "Please see https://aka.ms/dotnet-missing-libicu for more information.";
        }

        /// <summary>Environment.FailFast: the message on stderr, then abort (SIGABRT, exit status 134 on Unix).</summary>
        [DllImport("__Internal", EntryPoint = "csrt_fail_fast", CharSet = CharSet.Unicode)]
        internal static extern void FailFast(string message);

        private static bool TryGetAppLocalIcuSwitchValue(out string? value)
        {
            // csharpc: no runtimeconfig (System.Globalization.AppLocalIcu); the environment variable only.
            value = global::System.Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_APPLOCALICU");
            return !string.IsNullOrEmpty(value);
        }

        private static void LoadAppLocalIcu(string icuSuffixAndVersion)
        {
            string version;
            string icuSuffix = "";

            // Custom built ICU can have a suffix on the name, i.e: libicuucmyapp.so.67.1
            // So users would set the runtime switch as: myapp:67.1
            int indexOfSeparator = icuSuffixAndVersion.IndexOf(':');
            if (indexOfSeparator >= 0)
            {
                icuSuffix = icuSuffixAndVersion.Substring(0, indexOfSeparator);
                version = icuSuffixAndVersion.Substring(icuSuffix.Length + 1);
            }
            else
            {
                version = icuSuffixAndVersion;
            }

#if CSHARPC_TARGET_MACOS
            const string extension = ".dylib";
            bool versionAtEnd = false;
#else
            string extension = version.Length > 0 ? "so." : "so";
            bool versionAtEnd = true;
#endif
            string suffixAndSeparator = icuSuffix + ".";

#if !CSHARPC_TARGET_MACOS
            // In Linux we need to load libicudata first because libicuuc and libicui18n depend on it. In order for the loader to find
            // it on the same path, we load it before loading the other two libraries.
            LoadLibrary(CreateLibraryName("libicudata", suffixAndSeparator, extension, version, versionAtEnd));
#endif

            IntPtr icuucLib = LoadLibrary(CreateLibraryName("libicuuc", suffixAndSeparator, extension, version, versionAtEnd));
            IntPtr icuinLib = LoadLibrary(CreateLibraryName("libicui18n", suffixAndSeparator, extension, version, versionAtEnd));

            Interop.Globalization.InitICUFunctions(icuucLib, icuinLib, version, icuSuffix.Length > 0 ? icuSuffix : null);
        }

        private static string CreateLibraryName(string baseName, string suffix, string extension, string version, bool versionAtEnd = false) =>
            versionAtEnd ?
                string.Concat(baseName, suffix, extension, version) :
                string.Concat(baseName, suffix, version, extension);

        private static IntPtr LoadLibrary(string library)
        {
            if (!System.Runtime.InteropServices.NativeLibrary.TryLoad(library, out IntPtr lib))
            {
                FailFast($"Failed to load app-local ICU: {library}");
            }

            return lib;
        }
    }
}
