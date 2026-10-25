// System.Runtime.InteropServices.PosixSignalRegistration and PosixSignalContext (ASP.NET Core's
// ConsoleLifetime stops the host on SIGINT/SIGTERM/SIGQUIT), after .NET's PosixSignalRegistration.Unix.cs:
// src/runtime/signals.c routes registered signals to a pipe; a background thread reads it and runs the
// signal's handlers, newest first, with one context (on a thread of their own for SIGINT/SIGQUIT/SIGTERM); unless a handler sets
// Cancel, the signal then gets its default action. Windows (console control events) is not supported
// by csharpc yet; WASI has no signals (PlatformNotSupportedException, as in .NET).
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.PosixSignalContext")]
    public sealed class PosixSignalContextImpl
    {
        public PosixSignalContextImpl(PosixSignal signal) { Signal = signal; }
        public PosixSignal Signal { get; internal set; }
        public bool Cancel { get; set; }
    }

    [Shadows("System.Runtime.InteropServices.PosixSignalRegistration")]
    public sealed unsafe class PosixSignalRegistrationImpl : IDisposable
    {
        private readonly int _signo;
        private readonly PosixSignal _signal;
        private Action<PosixSignalContext> _handler;

        private PosixSignalRegistrationImpl(int signo, PosixSignal signal, Action<PosixSignalContext> handler)
        {
            _signo = signo;
            _signal = signal;
            _handler = handler;
        }

        public static PosixSignalRegistrationImpl Create(PosixSignal signal, Action<PosixSignalContext> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
#if CSHARPC_TARGET_WASI
            throw new PlatformNotSupportedException();
#elif CSHARPC_TARGET_WINDOWS
            throw new NotSupportedException("PosixSignalRegistration on Windows is not supported by csharpc yet.");
#else
            int signo = SignalNumber(signal);
            if (signo <= 0) throw new PlatformNotSupportedException();
            var registration = new PosixSignalRegistrationImpl(signo, signal, handler);
            lock (s_lock)
            {
                if (s_reader == null)
                {
                    int fd = SignalPipe();
                    if (fd < 0) throw new System.ComponentModel.Win32Exception(-fd);
                    s_reader = new Thread(() => ReadSignals(fd));
                    s_reader.IsBackground = true;
                    s_reader.Name = ".NET Signal Handler";
                    s_reader.Start();
                }
                if (!s_registrations.TryGetValue(signo, out var list))
                {
                    int error = SignalEnable(signo);
                    if (error != 0) throw new System.ComponentModel.Win32Exception(error);
                    s_registrations[signo] = list = new List<PosixSignalRegistrationImpl>();
                }
                list.Add(registration);
            }
            return registration;
#endif
        }

        public void Dispose()
        {
#if !CSHARPC_TARGET_WASI && !CSHARPC_TARGET_WINDOWS
            lock (s_lock)
            {
                if (_handler == null) return;
                _handler = null;
                if (s_registrations.TryGetValue(_signo, out var list))
                {
                    list.Remove(this);
                    if (list.Count == 0)
                    {
                        s_registrations.Remove(_signo);
                        SignalDisable(_signo);
                    }
                }
            }
#endif
        }

#if !CSHARPC_TARGET_WASI && !CSHARPC_TARGET_WINDOWS
        [DllImport("__Internal", EntryPoint = "csrt_signal_pipe")] private static extern int SignalPipe();
        [DllImport("__Internal", EntryPoint = "csrt_signal_enable")] private static extern int SignalEnable(int signo);
        [DllImport("__Internal", EntryPoint = "csrt_signal_disable")] private static extern void SignalDisable(int signo);
        [DllImport("__Internal", EntryPoint = "csrt_signal_default")] private static extern void SignalDefault(int signo);
        [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte* buffer, nuint count);

        private static readonly object s_lock = new object();
        private static readonly Dictionary<int, List<PosixSignalRegistrationImpl>> s_registrations = new Dictionary<int, List<PosixSignalRegistrationImpl>>();
        private static Thread s_reader;

        // PosixSignal's portable values (SIGHUP = -1 ... SIGTSTP = -10) as this OS numbers them; raw numbers pass through.
        private static int SignalNumber(PosixSignal signal)
        {
            int value = (int)signal;
            if (value > 0) return value;
            switch (value)
            {
                case -1: return 1;   // SIGHUP
                case -2: return 2;   // SIGINT
                case -3: return 3;   // SIGQUIT
                case -4: return 15;  // SIGTERM
#if CSHARPC_TARGET_MACOS
                case -5: return 20;  // SIGCHLD
                case -6: return 19;  // SIGCONT
                case -7: return 28;  // SIGWINCH
                case -8: return 21;  // SIGTTIN
                case -9: return 22;  // SIGTTOU
                case -10: return 18; // SIGTSTP
#else
                case -5: return 17;
                case -6: return 18;
                case -7: return 28;
                case -8: return 21;
                case -9: return 22;
                case -10: return 20;
#endif
            }
            return 0;
        }

        private static void ReadSignals(int fd)
        {
            byte signo;
            while (true)
            {
                nint n = read(fd, &signo, 1);
                if (n != 1) { if (n == 0 || Marshal.GetLastPInvokeError() != 4 /* EINTR */) return; continue; }
                int number = signo;
                // As .NET: SIGINT, SIGQUIT and SIGTERM get a thread of their own (a busy pool can't delay shutdown).
                if (number is 2 or 3 or 15)
                {
                    var thread = new Thread(() => Dispatch(number));
                    thread.IsBackground = true;
                    thread.Name = ".NET Signal Handler";
                    thread.Start();
                }
                else ThreadPool.UnsafeQueueUserWorkItem(static s => Dispatch(s), number, false);
            }
        }

        private static void Dispatch(int signo)
        {
            PosixSignalRegistrationImpl[] handlers;
            lock (s_lock)
            {
                if (!s_registrations.TryGetValue(signo, out var list) || list.Count == 0) { SignalDefault(signo); return; }
                handlers = list.ToArray();
            }
            // The newest registration first, all with one context, as .NET runs them.
            var context = new PosixSignalContextImpl(0);
            for (int i = handlers.Length - 1; i >= 0; i--)
            {
                var registration = handlers[i];
                Action<PosixSignalContext> handler = registration._handler;
                if (handler == null) continue;
                context.Signal = registration._signal;
                handler((PosixSignalContext)(object)context);
            }
            if (!context.Cancel) SignalDefault(signo);
        }
#endif
    }
}
