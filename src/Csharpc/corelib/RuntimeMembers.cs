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

        /// <summary>
        /// As RuntimeInformation.{Windows,OSX,Unix}.cs compute it: "Microsoft Windows 10.0.22631" (and the
        /// service pack); "macOS 15.1.0"; on Linux /etc/os-release's PRETTY_NAME (else NAME[ VERSION]), else
        /// uname's "sysname release version".
        /// </summary>
        public static string OSDescription
        {
            get
            {
                if (s_osDescription != null) return s_osDescription;
#if CSHARPC_TARGET_WINDOWS
                var (major, minor, build, _, csd) = OSVersionMembers.WindowsVersion();
                return s_osDescription = "Microsoft Windows " + major + "." + minor + "." + build + (csd.Length > 0 ? " " + csd : "");
#elif CSHARPC_TARGET_MACOS
                return s_osDescription = MacProductVersion();
#elif CSHARPC_TARGET_WASI
                return s_osDescription = "WASI";
#else
                return s_osDescription = PrettyName("/etc/os-release")
                    ?? OSVersionMembers.UnameField(0) + " " + OSVersionMembers.UnameField(2) + " " + OSVersionMembers.UnameField(3);
#endif
            }
        }

#if CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern unsafe int sysctlbyname(byte* name, byte* value, nuint* length, byte* newValue, nuint newLength);

        /// <summary>"macOS " + NSProcessInfo's major.minor.patch (kern.osproductversion).</summary>
        private static unsafe string MacProductVersion()
        {
            byte* value = stackalloc byte[64];
            nuint length = 64;
            fixed (byte* name = "kern.osproductversion\0"u8)
                if (sysctlbyname(name, value, &length, null, 0) != 0) return "macOS";
            value[63] = 0;
            string[] parts = Utf8.FromNative(value).Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out int a) ? a : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out int b) ? b : 0;
            int patch = parts.Length > 2 && int.TryParse(parts[2], out int c) ? c : 0;
            return "macOS " + major + "." + minor + "." + patch;
        }
#endif

#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_MACOS && !CSHARPC_TARGET_WASI
        private static string PrettyName(string path)
        {
            if (!System.IO.File.Exists(path)) return null;
            string[] lines;
            try { lines = System.IO.File.ReadAllLines(path); }
            catch { return null; }
            string name = null, version = null;
            foreach (string line in lines)
            {
                if (Field(line, "PRETTY_NAME=") is { Length: > 0 } pretty) return pretty;
                if (Field(line, "NAME=") is { } n) name = n;
                else if (Field(line, "VERSION=") is { } v) version = v;
            }
            if (!string.IsNullOrEmpty(name)) return string.IsNullOrEmpty(version) ? name : name + " " + version;
            return null;
        }

        private static string Field(string line, string prefix)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string value = line.Substring(prefix.Length);
            // Enclosing quotes are removed.
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[0] == value[value.Length - 1])
                value = value.Substring(1, value.Length - 2);
            return value;
        }
#endif
        public static Architecture OSArchitecture => (Architecture)CoreIntrinsics.TargetArchitecture();

        /// <summary>".NET 11.0.0-rc.2.26504.105": the framework the program was compiled against.</summary>
        public static string FrameworkDescription => ".NET " + CoreIntrinsics.FrameworkVersion();

        // What the host (or NativeAOT's compiled-in configuration) passes as RUNTIME_IDENTIFIER.
#if CSHARPC_TARGET_WASM32
        private const string Arch = "wasm";
#elif CSHARPC_TARGET_ARM64
        private const string Arch = "arm64";
#else
        private const string Arch = "x64";
#endif
#if CSHARPC_TARGET_WINDOWS
        public static string RuntimeIdentifier => "win-" + Arch;
#elif CSHARPC_TARGET_MACOS
        public static string RuntimeIdentifier => "osx-" + Arch;
#elif CSHARPC_TARGET_WASI
        public static string RuntimeIdentifier => "wasi-wasm";
#else
        public static string RuntimeIdentifier => "linux-" + Arch;
#endif
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
#if CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern int getpid();
        [DllImport("libc")] private static extern int proc_pidinfo(int pid, int flavor, ulong arg, void* buffer, int size);
        private const int PROC_PIDTASKINFO = 4, ProcTaskInfoSize = 96; // struct proc_taskinfo; pti_resident_size at 8
#elif CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32")] private static extern int K32GetProcessMemoryInfo(nint process, void* counters, int size);
        private const int ProcessMemoryCountersSize = 72; // PROCESS_MEMORY_COUNTERS (64-bit); WorkingSetSize at 16
#elif CSHARPC_TARGET_WASI
        [DllImport("__Internal", EntryPoint = "csrt_wasi_memory_bytes")] private static extern long WasmMemoryBytes();
#endif

        /// <summary>The resident set: /proc/self/statm (Linux), proc_pidinfo (macOS), GetProcessMemoryInfo (Windows), as .NET reads it.</summary>
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
#elif CSHARPC_TARGET_MACOS
                byte* info = stackalloc byte[ProcTaskInfoSize];
                return proc_pidinfo(getpid(), PROC_PIDTASKINFO, 0, info, ProcTaskInfoSize) == ProcTaskInfoSize ? (long)*(ulong*)(info + 8) : 0;
#elif CSHARPC_TARGET_WINDOWS
                byte* counters = stackalloc byte[ProcessMemoryCountersSize];
                *(int*)counters = ProcessMemoryCountersSize;
                return K32GetProcessMemoryInfo(-1, counters, ProcessMemoryCountersSize) != 0 ? (long)*(ulong*)(counters + 16) : 0;
#elif CSHARPC_TARGET_WASI
                return WasmMemoryBytes(); // the linear memory: all of it is resident
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
#if CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32")] private static extern int GetProcessTimes(nint process, long* creation, long* exit, long* kernel, long* user);
#elif CSHARPC_TARGET_WASI
        [DllImport("__Internal", EntryPoint = "csrt_wasi_cpu_ticks")] private static extern long WasiCpuTicks();
#else
        [DllImport("libc")] private static extern int getrusage(int who, long* usage);
#endif

        /// <summary>getrusage(RUSAGE_SELF): ru_utime and ru_stime (struct timeval each).</summary>
        public static Environment.ProcessCpuUsage CpuUsage
        {
            get
            {
#if CSHARPC_TARGET_WINDOWS
                // GetProcessTimes: FILETIMEs in 100-ns units, as TimeSpan ticks.
                long creation, exit, kernel, user;
                if (GetProcessTimes(-1, &creation, &exit, &kernel, &user) == 0) return default;
                return (Environment.ProcessCpuUsage)(object)new ProcessCpuUsageImpl(new TimeSpan(user), new TimeSpan(kernel));
#elif CSHARPC_TARGET_WASI
                // WASI's process CPU clock doesn't split user from kernel time: all of it is user time.
                return (Environment.ProcessCpuUsage)(object)new ProcessCpuUsageImpl(new TimeSpan(WasiCpuTicks()), TimeSpan.Zero);
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
        public object GetData(string name) => AppContext.GetData(name);

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

        [DllImport("__Internal", EntryPoint = "csrt_gc_collection_count")] private static extern long Collections(int generation);

        /// <summary>Gen 0 counts minor and major collections; higher gens count majors only.</summary>
        public static int CollectionCount(int generation)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(generation);
            return (int)Collections(generation);
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
