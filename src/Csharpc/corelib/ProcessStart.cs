// System.Diagnostics.Process. Linux and macOS start processes with posix_spawnp (ArgumentList, the working
// directory, the environment, redirected standard output and error read through pipes, WaitForExit,
// ExitCode, Kill); Windows, WASI, ShellExecute, a redirected standard input and Arguments command lines
// are explicit NotSupportedExceptions. The current process (GetCurrentProcess: Id, CPU times, working
// set on Linux) works everywhere. ProcessStartInfo is a property bag, as in .NET.
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
    public unsafe class ProcessImpl : IDisposable
    {
        private static NotSupportedException NotSupported() => new NotSupportedException("Starting processes (System.Diagnostics.Process) is not supported by csharpc yet on this platform (Linux and macOS have Process.Start; Process.Run is not supported yet).");

        public ProcessImpl() { } // no process associated until Start

        // ---- the current process (Process.GetCurrentProcess): its id, CPU times and memory high-water mark ----
        private readonly bool _current;
        private ProcessImpl(bool current) { _current = current; }

        /// <summary>A new Process object for this process on every call, as in .NET.</summary>
        public static Process GetCurrentProcess() => (Process)(object)new ProcessImpl(current: true);

        private void RequireCurrent()
        {
            if (_current) return;
            RequireStarted();
            throw new NotSupportedException("The CPU times and memory of a started process are not supported by csharpc yet.");
        }

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

        // ---- started processes (Linux and macOS: posix_spawnp, pipes for the redirected streams) ----
        private int _pid;
        private bool _started, _exited;
        private int _exitCode;
        private StreamReader _stdout, _stderr;

        public static Process Start(ProcessStartInfo startInfo)
        {
            ArgumentNullException.ThrowIfNull(startInfo);
            return (Process)(object)StartCore((ProcessStartInfoImpl)(object)startInfo);
        }

        public static Process Start(string fileName) => Start(new ProcessStartInfo(fileName));
        public static Process Start(string fileName, string arguments) => Start(new ProcessStartInfo(fileName, arguments));
        public static Process Start(string fileName, IEnumerable<string> arguments) => Start(new ProcessStartInfo(fileName, arguments));
        public static ProcessExitStatus Run(ProcessStartInfo startInfo, TimeSpan? timeout = null) => throw NotSupported();
        public static ProcessExitStatus Run(string fileName, IList<string> arguments = null, TimeSpan? timeout = null) => throw NotSupported();
        public static ProcessTextOutput RunAndCaptureText(ProcessStartInfo startInfo, TimeSpan? timeout = null) => throw NotSupported();
        public static ProcessTextOutput RunAndCaptureText(string fileName, IList<string> arguments = null, TimeSpan? timeout = null) => throw NotSupported();

        public StreamReader StandardOutput => _stdout ?? throw new InvalidOperationException("StandardOut has not been redirected or the process hasn't started yet.");
        public StreamReader StandardError => _stderr ?? throw new InvalidOperationException("StandardError has not been redirected or the process hasn't started yet.");
        public StreamWriter StandardInput => throw new NotSupportedException("Redirecting a started process's standard input is not supported by csharpc yet.");

        public int Id { get { if (_current) return Environment.ProcessId; RequireStarted(); return _pid; } }

        public bool HasExited
        {
            get
            {
                if (_current) return false;
                RequireStarted();
                return _exited || Reap(wait: false);
            }
        }

        public int ExitCode
        {
            get
            {
                RequireStarted();
                if (!_exited && !Reap(wait: false)) throw new InvalidOperationException("Process must exit before requested information can be determined.");
                return _exitCode;
            }
        }

        public void WaitForExit()
        {
            RequireStarted();
            if (!_exited) Reap(wait: true);
        }

        public bool WaitForExit(int milliseconds)
        {
            if (milliseconds == -1) { WaitForExit(); return true; }
            ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, -1);
            RequireStarted();
            var deadline = Environment.TickCount64 + milliseconds;
            while (!_exited && !Reap(wait: false))
            {
                if (Environment.TickCount64 >= deadline) return false;
                System.Threading.Thread.Sleep(1);
            }
            return true;
        }

        public bool WaitForExit(TimeSpan timeout) => WaitForExit((int)timeout.TotalMilliseconds);

        public void Kill()
        {
            RequireStarted();
            if (_exited || Reap(wait: false)) return;
#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
            kill(_pid, 9); // SIGKILL
#endif
        }

        public void Dispose()
        {
            _stdout?.Dispose();
            _stderr?.Dispose();
        }

        private void RequireStarted()
        {
            if (!_started) throw new InvalidOperationException("No process is associated with this object.");
        }

#if CSHARPC_TARGET_WINDOWS || CSHARPC_TARGET_WASI
        private static ProcessImpl StartCore(ProcessStartInfoImpl startInfo) => throw NotSupported();
        private bool Reap(bool wait) => throw NotSupported();
#else
        [DllImport("libc", SetLastError = true)] private static extern int pipe(int* fds);
        [DllImport("libc", SetLastError = true)] private static extern int fcntl(int fd, int command, int argument);
        [DllImport("libc")] private static extern int posix_spawn_file_actions_init(void* actions);
        [DllImport("libc")] private static extern int posix_spawn_file_actions_destroy(void* actions);
        [DllImport("libc")] private static extern int posix_spawn_file_actions_adddup2(void* actions, int fd, int newFd);
        [DllImport("libc")] private static extern int posix_spawn_file_actions_addclose(void* actions, int fd);
        [DllImport("libc")] private static extern int posix_spawn_file_actions_addchdir_np(void* actions, byte* path);
        [DllImport("libc")] private static extern int posix_spawnp(int* pid, byte* file, void* actions, void* attributes, byte** argv, byte** envp);
        [DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, int* status, int options);
        [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);

        private static ProcessImpl StartCore(ProcessStartInfoImpl startInfo)
        {
            if (startInfo.UseShellExecute) throw new NotSupportedException("ProcessStartInfo.UseShellExecute is not supported by csharpc yet.");
            if (startInfo.RedirectStandardInput || startInfo.StandardInputHandle != null || startInfo.StandardOutputHandle != null || startInfo.StandardErrorHandle != null)
                throw new NotSupportedException("Redirecting a started process's standard input, or to given handles, is not supported by csharpc yet.");
            string fileName = startInfo.FileName;
            if (fileName.Length == 0) throw new InvalidOperationException("Cannot start process because a file name has not been provided.");
            if (startInfo.Arguments.Length > 0 && startInfo.ArgumentList.Count > 0)
                throw new InvalidOperationException("Only one of Arguments or ArgumentList may be used.");
            if (startInfo.Arguments.Length > 0)
                throw new NotSupportedException("ProcessStartInfo.Arguments (a command line to split) is not supported by csharpc yet; use ArgumentList.");

            var argv = new List<string> { fileName };
            argv.AddRange(startInfo.ArgumentList);
            var envp = new List<string>();
            foreach (var pair in startInfo.Environment) if (pair.Value != null) envp.Add(pair.Key + "=" + pair.Value);

            int* outPipe = stackalloc int[2];
            int* errPipe = stackalloc int[2];
            outPipe[0] = outPipe[1] = errPipe[0] = errPipe[1] = -1;
            byte* actions = (byte*)NativeMemory.AllocZeroed(256); // posix_spawn_file_actions_t: 80 bytes on glibc, a pointer on macOS
            byte** nativeArgv = NativeStrings(argv);
            byte** nativeEnvp = NativeStrings(envp);
            byte* nativeFile = Utf8.ToNative(fileName);
            byte* directory = startInfo.WorkingDirectory.Length > 0 ? Utf8.ToNative(startInfo.WorkingDirectory) : null;
            try
            {
                posix_spawn_file_actions_init(actions);
                if (startInfo.RedirectStandardOutput) RedirectTo(outPipe, 1, actions);
                if (startInfo.RedirectStandardError) RedirectTo(errPipe, 2, actions);
                if (directory != null) posix_spawn_file_actions_addchdir_np(actions, directory);
                int pid;
                int error = posix_spawnp(&pid, nativeFile, actions, null, nativeArgv, nativeEnvp);
                posix_spawn_file_actions_destroy(actions);
                if (outPipe[1] >= 0) Posix.close(outPipe[1]);
                if (errPipe[1] >= 0) Posix.close(errPipe[1]);
                if (error != 0)
                {
                    if (outPipe[0] >= 0) Posix.close(outPipe[0]);
                    if (errPipe[0] >= 0) Posix.close(errPipe[0]);
                    string cwd = startInfo.WorkingDirectory.Length > 0 ? startInfo.WorkingDirectory : Environment.CurrentDirectory;
                    throw new System.ComponentModel.Win32Exception(error,
                        "An error occurred trying to start process '" + fileName + "' with working directory '" + cwd + "'. " + Marshal.GetPInvokeErrorMessage(error));
                }
                var process = new ProcessImpl(current: false) { _pid = pid, _started = true };
                // .NET's defaults: the encoding given, else UTF-8, with byte order mark detection.
                if (outPipe[0] >= 0) process._stdout = new StreamReader(new PipeReadStream(outPipe[0]), startInfo.StandardOutputEncoding ?? new UTF8Encoding(false), true, 4096);
                if (errPipe[0] >= 0) process._stderr = new StreamReader(new PipeReadStream(errPipe[0]), startInfo.StandardErrorEncoding ?? new UTF8Encoding(false), true, 4096);
                return process;
            }
            finally
            {
                NativeMemory.Free(actions);
                FreeStrings(nativeArgv);
                FreeStrings(nativeEnvp);
                Libc.free(nativeFile);
                if (directory != null) Libc.free(directory);
            }
        }

        /// <summary>A pipe whose write end becomes the child's <paramref name="target"/> descriptor; the parent keeps the read end.</summary>
        private static void RedirectTo(int* fds, int target, void* actions)
        {
            if (pipe(fds) != 0) throw new System.ComponentModel.Win32Exception(Posix.Errno);
            fcntl(fds[0], 2 /* F_SETFD */, 1 /* FD_CLOEXEC */); // neither end leaks into other children
            fcntl(fds[1], 2, 1);
            posix_spawn_file_actions_adddup2(actions, fds[1], target); // dup2 clears FD_CLOEXEC on the child's copy
            posix_spawn_file_actions_addclose(actions, fds[0]);
        }

        private static byte** NativeStrings(List<string> values)
        {
            var array = (byte**)NativeMemory.AllocZeroed((nuint)(values.Count + 1), (nuint)sizeof(byte*));
            for (int i = 0; i < values.Count; i++) array[i] = Utf8.ToNative(values[i]);
            return array;
        }

        private static void FreeStrings(byte** array)
        {
            for (byte** p = array; *p != null; p++) Libc.free(*p);
            NativeMemory.Free(array);
        }

        /// <summary>waitpid: true once the child has exited (its code recorded: the exit status, or 128 + the signal).</summary>
        private bool Reap(bool wait)
        {
            if (_exited) return true;
            int status;
            int result;
            do result = waitpid(_pid, &status, wait ? 0 : 1 /* WNOHANG */);
            while (result < 0 && Posix.Errno == 4 /* EINTR */);
            if (result == 0) return false;
            if (result < 0) throw new System.ComponentModel.Win32Exception(Posix.Errno);
            _exitCode = (status & 0x7F) == 0 ? (status >> 8) & 0xFF : 128 + (status & 0x7F);
            _exited = true;
            return true;
        }

        /// <summary>The read end of a pipe from the child (StandardOutput/StandardError's stream).</summary>
        private sealed class PipeReadStream : Stream
        {
            private int _fd;
            internal PipeReadStream(int fd) { _fd = fd; }
            public override bool CanRead => _fd >= 0;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException("Stream does not support seeking.");
            public override long Position { get => throw new NotSupportedException("Stream does not support seeking."); set => throw new NotSupportedException("Stream does not support seeking."); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Stream does not support seeking.");
            public override void SetLength(long value) => throw new NotSupportedException("Stream does not support seeking.");
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Stream does not support writing.");

            public override int Read(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                if (_fd < 0) throw new ObjectDisposedException(null, "Cannot access a closed pipe.");
                if (count == 0) return 0;
                fixed (byte* p = &buffer[offset])
                {
                    while (true)
                    {
                        nint n = Posix.read(_fd, p, (nuint)count);
                        if (n >= 0) return (int)n;
                        if (Posix.Errno != 4 /* EINTR */) throw new IOException("Reading from the process's output failed.", Posix.Errno);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (_fd >= 0) { Posix.close(_fd); _fd = -1; }
                base.Dispose(disposing);
            }
        }
#endif
    }
}
