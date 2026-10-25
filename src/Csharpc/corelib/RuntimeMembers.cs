// Small runtime-environment members ASP.NET Core and Microsoft.Extensions reach: the debugger (never
// attached to a csharpc process), RuntimeInformation's OS checks, Assembly.IsCollectible, the
// ReflectionTypeLoadException shape, and GC.GetGCMemoryInfo.
#nullable disable
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.OSPlatform")]
    public readonly struct OSPlatformImpl : IEquatable<OSPlatform>
    {
        private readonly string _osPlatform;

        private OSPlatformImpl(string osPlatform)
        {
            ArgumentException.ThrowIfNullOrEmpty(osPlatform);
            _osPlatform = osPlatform;
        }

        public static OSPlatform FreeBSD => Create("FREEBSD");
        public static OSPlatform Linux => Create("LINUX");
        public static OSPlatform OSX => Create("OSX");
        public static OSPlatform Windows => Create("WINDOWS");

        public static OSPlatform Create(string osPlatform) => (OSPlatform)(object)new OSPlatformImpl(osPlatform);

        internal string Name => _osPlatform;

        public bool Equals(OSPlatform other) => Equals((OSPlatformImpl)(object)other);
        internal bool Equals(OSPlatformImpl other) => string.Equals(_osPlatform, other._osPlatform, StringComparison.OrdinalIgnoreCase);
        public override bool Equals(object obj) => obj is OSPlatform other && Equals(other);
        public override int GetHashCode() => _osPlatform == null ? 0 : _osPlatform.GetHashCode(StringComparison.OrdinalIgnoreCase);
        public override string ToString() => _osPlatform ?? string.Empty;
        public static bool operator ==(OSPlatformImpl left, OSPlatformImpl right) => left.Equals(right);
        public static bool operator !=(OSPlatformImpl left, OSPlatformImpl right) => !left.Equals(right);
    }

    [ShadowMembers("System.Runtime.InteropServices.RuntimeInformation")]
    internal static class RuntimeInformationMembers
    {
        /// <summary>RuntimeInformation.IsOSPlatform: OperatingSystem.IsOSPlatform's answer (macOS also answers to "OSX").</summary>
        public static bool IsOSPlatform(OSPlatform osPlatform) => OperatingSystem.IsOSPlatform(((OSPlatformImpl)(object)osPlatform).Name ?? "");
    }

    /// <summary>
    /// GC.GetGCMemoryInfo: csharpc's collector (src/runtime/gc.c) is a different GC, so the figures are its
    /// own (heap size from GC.GetTotalMemory), as they are runtime-specific under .NET too; per-generation
    /// and pause details have no counterpart and throw NotSupportedException.
    /// </summary>
    [Shadows("System.GCMemoryInfo")]
    public readonly struct GCMemoryInfoImpl
    {
        private readonly long _heapSize;
        internal GCMemoryInfoImpl(long heapSize) { _heapSize = heapSize; }
        public long HighMemoryLoadThresholdBytes => 0;
        public long MemoryLoadBytes => 0;
        public long TotalAvailableMemoryBytes => 0;
        public long HeapSizeBytes => _heapSize;
        public long FragmentedBytes => 0;
        public long Index => 0;
        public int Generation => 0;
        public bool Compacted => false;
        public bool Concurrent => false;
        public long TotalCommittedBytes => _heapSize;
        public long PromotedBytes => 0;
        public long PinnedObjectsCount => 0;
        public long FinalizationPendingCount => 0;
        public double PauseTimePercentage => 0;
    }

    [ShadowMembers("System.Runtime.InteropServices.RuntimeInformation")]
    internal static class RuntimeInformationDescription
    {
        /// <summary>The target's architecture (x86-64, arm64 or wasm32), fixed at compile time as NativeAOT's is.</summary>
        public static Architecture ProcessArchitecture => (Architecture)CoreIntrinsics.TargetArchitecture();
        public static Architecture OSArchitecture => (Architecture)CoreIntrinsics.TargetArchitecture();

        /// <summary>".NET 11.0.0-rc.2.26504.105": the framework the program was compiled against.</summary>
        public static string FrameworkDescription => ".NET " + CoreIntrinsics.FrameworkVersion();
    }

    [ShadowMembers("System.Environment")]
    internal static class EnvironmentVersionMembers
    {
        private static Version s_version;

        /// <summary>The runtime's major.minor.build ("11.0.0" for 11.0.0-rc.2.*), as .NET reports Environment.Version.</summary>
        public static Version Version
        {
            get
            {
                if (s_version != null) return s_version;
                string version = CoreIntrinsics.FrameworkVersion();
                int dash = version.IndexOf('-');
                string[] parts = (dash < 0 ? version : version.Substring(0, dash)).Split('.');
                return s_version = new Version(int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 0, parts.Length > 2 ? int.Parse(parts[2]) : 0);
            }
        }

        public static string StackTrace =>
            throw new NotSupportedException("Environment.StackTrace (the current call stack as text) is not supported by csharpc yet.");
    }

    [ShadowMembers("System.GC")]
    internal static class GCMembers
    {
        public static GCMemoryInfo GetGCMemoryInfo() => (GCMemoryInfo)(object)new GCMemoryInfoImpl(GC.GetTotalMemory(false));
        public static GCMemoryInfo GetGCMemoryInfo(GCKind kind) => GetGCMemoryInfo();

        // csharpc's collector never moves objects: every array is pinned, and fresh memory is zeroed anyway.
        public static T[] AllocateUninitializedArray<T>(int length, bool pinned = false) => new T[length];
        public static T[] AllocateArray<T>(int length, bool pinned = false) => new T[length];
    }

    [Shadows("System.Reflection.ReflectionTypeLoadException")]
    public sealed class ReflectionTypeLoadExceptionImpl : SystemException
    {
        public ReflectionTypeLoadExceptionImpl(Type[] classes, Exception[] exceptions)
            : this(classes, exceptions, null) { }

        public ReflectionTypeLoadExceptionImpl(Type[] classes, Exception[] exceptions, string message)
            : base(message)
        {
            Types = classes ?? new Type[0];
            LoaderExceptions = exceptions ?? new Exception[0];
        }

        public Type[] Types { get; }
        public Exception[] LoaderExceptions { get; }

        public override string Message => CreateString(isMessage: true);
        public override string ToString() => CreateString(isMessage: false);

        private string CreateString(bool isMessage)
        {
            string baseValue = isMessage ? base.Message : base.ToString();
            Exception[] exceptions = LoaderExceptions;
            if (exceptions.Length == 0) return baseValue;
            var text = new System.Text.StringBuilder(baseValue);
            foreach (Exception e in exceptions)
                if (e != null) text.AppendLine().Append(isMessage ? e.Message : e.ToString());
            return text.ToString();
        }
    }
}
