// Starting processes (System.Diagnostics.Process) is not supported by csharpc yet; the current process
// (GetCurrentProcess: Id, CPU times, working set on Linux) is. ProcessStartInfo is a
// property bag, as in .NET; Process.Start and the members around it throw NotSupportedException.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.ProcessStartInfo")]
    public sealed class ProcessStartInfoImpl
    {
        private string _fileName, _arguments, _workingDirectory;
        private Collection<string> _argumentList;

        public ProcessStartInfoImpl() { }
        public ProcessStartInfoImpl(string fileName) { _fileName = fileName; }
        public ProcessStartInfoImpl(string fileName, string arguments) { _fileName = fileName; _arguments = arguments; }
        public ProcessStartInfoImpl(string fileName, IEnumerable<string> arguments)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            ArgumentNullException.ThrowIfNull(arguments);
            _fileName = fileName;
            _argumentList = new Collection<string>(new List<string>(arguments));
        }

        public string FileName { get => _fileName ?? string.Empty; set => _fileName = value; }
        public string Arguments { get => _arguments ?? string.Empty; set => _arguments = value; }
        public Collection<string> ArgumentList => _argumentList ??= new Collection<string>();
        public string WorkingDirectory { get => _workingDirectory ?? string.Empty; set => _workingDirectory = value; }
        public bool UseShellExecute { get; set; }
        public bool RedirectStandardInput { get; set; }
        public bool RedirectStandardOutput { get; set; }
        public bool RedirectStandardError { get; set; }
        public bool CreateNoWindow { get; set; }
        public Encoding StandardOutputEncoding { get; set; }
        public Encoding StandardErrorEncoding { get; set; }
        public Encoding StandardInputEncoding { get; set; }
        public string Verb { get; set; }
        public ProcessWindowStyle WindowStyle { get; set; }
        public Microsoft.Win32.SafeHandles.SafeFileHandle StandardInputHandle { get; set; }
        public Microsoft.Win32.SafeHandles.SafeFileHandle StandardOutputHandle { get; set; }
        public Microsoft.Win32.SafeHandles.SafeFileHandle StandardErrorHandle { get; set; }

        private Dictionary<string, string> _environment;

        /// <summary>A copy of this process's environment on first use (variable names ignore case on Windows only).</summary>
        public IDictionary<string, string> Environment
        {
            get
            {
                if (_environment is null)
                {
                    var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                    foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
                        environment[(string)entry.Key] = (string)entry.Value;
                    _environment = environment;
                }
                return _environment;
            }
        }
    }

    [Shadows("System.Diagnostics.ProcessExitStatus")]
    public sealed class ProcessExitStatusImpl
    {
        private ProcessExitStatusImpl() { }
        public int ExitCode => throw new NotSupportedException("Starting processes (System.Diagnostics.Process) is not supported by csharpc yet.");
        public bool Canceled => false;
        public PosixSignal? Signal => null;
    }

    [Shadows("System.Diagnostics.Process")]
    public class ProcessImpl : IDisposable
    {
        private static NotSupportedException NotSupported() => new NotSupportedException("Starting processes (System.Diagnostics.Process) is not supported by csharpc yet.");

        public ProcessImpl() => throw NotSupported();

        // ---- the current process (Process.GetCurrentProcess): its id, CPU times and memory high-water mark ----
        private readonly bool _current;
        private ProcessImpl(bool current) { _current = current; }

        /// <summary>A new Process object for this process on every call, as in .NET.</summary>
        public static Process GetCurrentProcess() => (Process)(object)new ProcessImpl(current: true);

        private void RequireCurrent() { if (!_current) throw NotSupported(); }

        public TimeSpan TotalProcessorTime { get { RequireCurrent(); return Environment.CpuUsage.TotalTime; } }
        public TimeSpan UserProcessorTime { get { RequireCurrent(); return Environment.CpuUsage.UserTime; } }
        public TimeSpan PrivilegedProcessorTime { get { RequireCurrent(); return Environment.CpuUsage.PrivilegedTime; } }
        public long PeakWorkingSet64 { get { RequireCurrent(); return ProcStatusBytes("VmHWM:"); } }
        public long WorkingSet64 { get { RequireCurrent(); return ProcStatusBytes("VmRSS:"); } }

        /// <summary>A "Name:  N kB" line of /proc/self/status, in bytes (what .NET reads on Linux).</summary>
        private static long ProcStatusBytes(string field)
        {
#if CSHARPC_TARGET_LINUX
            foreach (var line in File.ReadAllLines("/proc/self/status"))
                if (line.StartsWith(field, StringComparison.Ordinal))
                {
                    var digits = line.Substring(field.Length).Trim();
                    if (digits.EndsWith(" kB", StringComparison.Ordinal)) digits = digits.Substring(0, digits.Length - 3);
                    return long.Parse(digits) * 1024;
                }
            return 0;
#else
            throw new NotSupportedException("Process." + (field == "VmHWM:" ? "PeakWorkingSet64" : "WorkingSet64") + " is not supported by csharpc yet on this platform.");
#endif
        }
        public static Process Start(ProcessStartInfo startInfo) => throw NotSupported();
        public static Process Start(string fileName) => throw NotSupported();
        public static Process Start(string fileName, string arguments) => throw NotSupported();
        public static Process Start(string fileName, IEnumerable<string> arguments) => throw NotSupported();
        public static ProcessExitStatus Run(ProcessStartInfo startInfo, TimeSpan? timeout = null) => throw NotSupported();
        public static ProcessExitStatus Run(string fileName, IList<string> arguments = null, TimeSpan? timeout = null) => throw NotSupported();
        public static ProcessTextOutput RunAndCaptureText(ProcessStartInfo startInfo, TimeSpan? timeout = null) => throw NotSupported();
        public static ProcessTextOutput RunAndCaptureText(string fileName, IList<string> arguments = null, TimeSpan? timeout = null) => throw NotSupported();
        public StreamReader StandardOutput => throw NotSupported();
        public StreamReader StandardError => throw NotSupported();
        public StreamWriter StandardInput => throw NotSupported();
        public int ExitCode => throw NotSupported();
        public bool HasExited { get { RequireCurrent(); return false; } }
        public int Id { get { RequireCurrent(); return Environment.ProcessId; } }
        public void WaitForExit() => throw NotSupported();
        public bool WaitForExit(int milliseconds) => throw NotSupported();
        public bool WaitForExit(TimeSpan timeout) => throw NotSupported();
        public void Kill() => throw NotSupported();
        public void Dispose() { }
    }
}
