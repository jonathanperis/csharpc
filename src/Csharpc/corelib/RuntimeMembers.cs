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

        // csharpc's collector has no generations: the five entries .NET reports, empty.
        private static GCGenerationInfo[] s_generations;
        public ReadOnlySpan<GCGenerationInfo> GenerationInfo => s_generations ??= new GCGenerationInfo[5];
    }

    [Shadows("System.GCGenerationInfo")]
    public readonly struct GCGenerationInfoImpl
    {
        public long SizeBeforeBytes => 0;
        public long FragmentationBeforeBytes => 0;
        public long SizeAfterBytes => 0;
        public long FragmentationAfterBytes => 0;
    }

    [ShadowMembers("System.Runtime.InteropServices.RuntimeInformation")]
    internal static class RuntimeInformationDescription
    {
        /// <summary>The target's architecture (x86-64, arm64 or wasm32), fixed at compile time as NativeAOT's is.</summary>
        public static Architecture ProcessArchitecture => (Architecture)CoreIntrinsics.TargetArchitecture();

        private static string s_osDescription;

        /// <summary>uname's "sysname release version", as .NET's GetUnixVersion formats it.</summary>
        public static string OSDescription
        {
            get
            {
#if CSHARPC_TARGET_WINDOWS || CSHARPC_TARGET_WASI
                throw new NotSupportedException("RuntimeInformation.OSDescription on this platform is not supported by csharpc yet.");
#else
                return s_osDescription ??= OSVersionMembers.UnameField(0) + " " + OSVersionMembers.UnameField(2) + " " + OSVersionMembers.UnameField(3);
#endif
            }
        }
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

    /// <summary>
    /// Runtime counters RuntimeMetrics (System.Diagnostics.DiagnosticSource) reports. csharpc has no JIT
    /// (zero, as under NativeAOT), one generation (GC.MaxGeneration still reports .NET's 2), and doesn't count
    /// lock contention yet (zero).
    /// </summary>
    [ShadowMembers("System.Runtime.JitInfo")]
    internal static class JitInfoMembers
    {
        public static long GetCompiledILBytes(bool currentThread = false) => 0;
        public static long GetCompiledMethodCount(bool currentThread = false) => 0;
        public static TimeSpan GetCompilationTime(bool currentThread = false) => TimeSpan.Zero;
    }

    [ShadowMembers("System.Threading.Monitor")]
    internal static class MonitorCounters
    {
        public static long LockContentionCount => 0;
    }

    [ShadowMembers("System.Threading.ThreadPool")]
    internal static class ThreadPoolCounters
    {
        [DllImport("__Internal", EntryPoint = "csrt_pool_thread_count")] private static extern int PoolThreads();
        [DllImport("__Internal", EntryPoint = "csrt_pool_pending_count")] private static extern long PoolPending();
        [DllImport("__Internal", EntryPoint = "csrt_pool_completed_count")] private static extern long PoolCompleted();

        public static int ThreadCount => PoolThreads();
        public static long PendingWorkItemCount => PoolPending();
        public static long CompletedWorkItemCount => PoolCompleted();
    }

    [ShadowMembers("System.Environment")]
    internal static unsafe class WorkingSetMembers
    {
        /// <summary>The resident set (/proc/self/statm's second field, in pages).</summary>
        public static long WorkingSet
        {
            get
            {
#if CSHARPC_TARGET_LINUX
                try
                {
                    string[] fields = System.IO.File.ReadAllText("/proc/self/statm").Split(' ');
                    return long.Parse(fields[1]) * Environment.SystemPageSize;
                }
                catch (Exception) { return 0; }
#else
                throw new NotSupportedException("Environment.WorkingSet on this platform is not supported by csharpc yet.");
#endif
            }
        }
    }

    [Shadows("System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs")]
    public class FirstChanceExceptionEventArgsImpl : EventArgs
    {
        public FirstChanceExceptionEventArgsImpl(Exception exception) { Exception = exception; }
        public Exception Exception { get; }
    }

    [Shadows("System.Environment+ProcessCpuUsage")]
    public readonly struct ProcessCpuUsageImpl
    {
        internal ProcessCpuUsageImpl(TimeSpan user, TimeSpan privileged)
        {
            UserTime = user;
            PrivilegedTime = privileged;
        }
        public TimeSpan UserTime { get; }
        public TimeSpan PrivilegedTime { get; }
        public TimeSpan TotalTime => UserTime + PrivilegedTime;
    }

    [ShadowMembers("System.Environment")]
    internal static unsafe class CpuUsageMembers
    {
#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern int getrusage(int who, long* usage);
#endif

        /// <summary>getrusage(RUSAGE_SELF): ru_utime and ru_stime (struct timeval each).</summary>
        public static Environment.ProcessCpuUsage CpuUsage
        {
            get
            {
#if CSHARPC_TARGET_WINDOWS || CSHARPC_TARGET_WASI
                throw new NotSupportedException("Environment.CpuUsage on this platform is not supported by csharpc yet.");
#else
                long* usage = stackalloc long[18];
                if (getrusage(0, usage) != 0) return default;
                var user = new TimeSpan(usage[0] * TimeSpan.TicksPerSecond + usage[1] * 10);
                var system = new TimeSpan(usage[2] * TimeSpan.TicksPerSecond + usage[3] * 10);
                return (Environment.ProcessCpuUsage)(object)new ProcessCpuUsageImpl(user, system);
#endif
            }
        }
    }

    [Shadows("System.AppDomain")]
    public sealed class AppDomainImpl
    {
        private static AppDomainImpl s_current;
        private AppDomainImpl() { }

        public static AppDomainImpl CurrentDomain => s_current ??= new AppDomainImpl();

        public string FriendlyName => System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        public string BaseDirectory => AppContext.BaseDirectory;
        public int Id => 1;
        public bool IsDefaultAppDomain() => true;

        // csharpc raises no first-chance notifications yet: handlers are kept but never called.
        public event EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> FirstChanceException { add { } remove { } }

        public Assembly[] GetAssemblies() =>
            throw new NotSupportedException("AppDomain.GetAssemblies (the loaded assemblies) is not supported by csharpc yet.");
    }

    [ShadowMembers("System.GC")]
    internal static class GCMembers
    {
        public static GCMemoryInfo GetGCMemoryInfo() => (GCMemoryInfo)(object)new GCMemoryInfoImpl(GC.GetTotalMemory(false));
        public static GCMemoryInfo GetGCMemoryInfo(GCKind kind) => GetGCMemoryInfo();

        [DllImport("__Internal", EntryPoint = "csrt_gc_total_allocated")] private static extern long TotalAllocated();
        [DllImport("__Internal", EntryPoint = "csrt_gc_total_pause_ticks")] private static extern long TotalPauseTicks();

        public static long GetTotalAllocatedBytes(bool precise = false) => TotalAllocated();
        public static TimeSpan GetTotalPauseDuration() => new TimeSpan(TotalPauseTicks());
        public static int MaxGeneration => 2;

        [DllImport("__Internal", EntryPoint = "csrt_gc_collection_count")] private static extern long Collections();

        /// <summary>Every csharpc collection is a full one, which .NET counts for every generation up to it.</summary>
        public static int CollectionCount(int generation)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(generation);
            return (int)Collections();
        }

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
